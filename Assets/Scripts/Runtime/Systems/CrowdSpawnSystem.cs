using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsCrowd
{
    /// <summary>
    /// Spawns the whole crowd once as concentric rings: ring k has radius inner + k * spacing and
    /// max(8, circumference / spacing) slots, rotated by 0.5 rad per ring. Instances are created in
    /// two batched Instantiate calls and placed by a parallel job.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(CrowdSimulationGroup), OrderFirst = true)]
    public partial struct CrowdSpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrowdSpawner>();
            state.RequireForUpdate<FrameContext>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var spawnerEntity = SystemAPI.GetSingletonEntity<CrowdSpawner>();
            if (SystemAPI.HasComponent<CrowdSpawned>(spawnerEntity))
            {
                state.Enabled = false;
                return;
            }

            var spawner = SystemAPI.GetComponent<CrowdSpawner>(spawnerEntity);
            uint frame = SystemAPI.GetSingleton<FrameContext>().Frame;

            int count = SystemAPI.TryGetSingleton<CrowdCountOverride>(out var countOverride) && countOverride.Count > 0
                ? countOverride.Count
                : math.max(0, spawner.Count);
            int period = math.max(1, spawner.MeleeWeight + spawner.RangedWeight);
            int meleeWeight = math.min(spawner.MeleeWeight, period);
            int meleeCount = count / period * meleeWeight + math.min(count % period, meleeWeight);
            int rangedCount = count - meleeCount;

            var melee = state.EntityManager.Instantiate(spawner.MeleePrefab, meleeCount, Allocator.TempJob);
            var ranged = state.EntityManager.Instantiate(spawner.RangedPrefab, rangedCount, Allocator.TempJob);

            var ringStart = new NativeList<int>(128, Allocator.TempJob);
            int placed = 0;
            while (placed < count)
            {
                float radius = spawner.InnerRadius + ringStart.Length * spawner.RingSpacing;
                ringStart.Add(placed);
                placed += math.max(8, (int)(2f * math.PI * radius / spawner.RingSpacing));
            }

            // The last ring only holds what is left, spread evenly around the full circle.
            ringStart.Add(count);

            // Killed agents must come back outside the crowd, whatever its size turned out to be.
            float outerRadius = spawner.InnerRadius + math.max(0, ringStart.Length - 2) * spawner.RingSpacing;
            float band = math.max(spawner.RespawnMaxRadius - spawner.RespawnMinRadius, 10f);
            spawner.RespawnMinRadius = math.max(spawner.RespawnMinRadius, outerRadius + 5f);
            spawner.RespawnMaxRadius = spawner.RespawnMinRadius + band;
            SystemAPI.SetComponent(spawnerEntity, spawner);

            new PlaceJob
            {
                Melee = melee,
                Ranged = ranged,
                RingStart = ringStart.AsArray(),
                Origin = spawner.Origin,
                InnerRadius = spawner.InnerRadius,
                RingSpacing = spawner.RingSpacing,
                MeleeWeight = meleeWeight,
                Period = period,
                Frame = frame,
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(),
                Steering = SystemAPI.GetComponentLookup<SteeringState>(),
                Attacks = SystemAPI.GetComponentLookup<AttackState>(),
                Animation = SystemAPI.GetComponentLookup<AnimParams>(),
            }.Schedule(count, 512, state.Dependency).Complete();

            melee.Dispose();
            ranged.Dispose();
            ringStart.Dispose();

            state.EntityManager.AddComponent<CrowdSpawned>(spawnerEntity);
        }

        [BurstCompile]
        struct PlaceJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Melee;
            [ReadOnly] public NativeArray<Entity> Ranged;
            [ReadOnly] public NativeArray<int> RingStart;
            public float3 Origin;
            public float InnerRadius;
            public float RingSpacing;
            public int MeleeWeight;
            public int Period;
            public uint Frame;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
            [NativeDisableParallelForRestriction] public ComponentLookup<SteeringState> Steering;
            [NativeDisableParallelForRestriction] public ComponentLookup<AttackState> Attacks;
            [NativeDisableParallelForRestriction] public ComponentLookup<AnimParams> Animation;

            public void Execute(int slot)
            {
                int ring = FindRing(slot);
                int first = RingStart[ring];
                int slots = RingStart[ring + 1] - first;

                float angle = (float)(slot - first) / slots * 2f * math.PI + ring * 0.5f;
                float radius = InnerRadius + ring * RingSpacing;
                math.sincos(angle, out float sin, out float cos);
                float3 position = Origin + new float3(cos * radius, 0f, sin * radius);

                // Interleave the kinds exactly like the weights: MeleeWeight melee, then the rest ranged.
                int group = slot / Period;
                int lane = slot % Period;
                Entity entity = lane < MeleeWeight
                    ? Melee[group * MeleeWeight + lane]
                    : Ranged[group * (Period - MeleeWeight) + lane - MeleeWeight];

                var rng = Random.CreateFromIndex((uint)slot);
                Transforms[entity] = LocalTransform.FromPositionRotation(position, quaternion.RotateY(math.atan2(-cos, -sin)));
                Steering[entity] = new SteeringState { SpawnFrame = Frame };
                Attacks[entity] = new AttackState { Cooldown = rng.NextFloat(0f, 2f) };
                Animation[entity] = new AnimParams { Value = new float4(rng.NextFloat(), 0f, 0f, 0f) };
            }

            int FindRing(int slot)
            {
                int low = 0;
                int high = RingStart.Length - 2;
                while (low < high)
                {
                    int mid = (low + high + 1) >> 1;
                    if (RingStart[mid] <= slot)
                        low = mid;
                    else
                        high = mid - 1;
                }

                return low;
            }
        }
    }
}
