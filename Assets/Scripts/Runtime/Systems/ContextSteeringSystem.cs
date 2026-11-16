using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsCrowd
{
    /// <summary>
    /// Re-plans each agent's heading with context steering over the shared grid. Planning is amortised:
    /// agents near the camera re-plan every 4 frames, distant ones every 12, staggered by entity index,
    /// and locomotion reuses the cached decision in between.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(CrowdSimulationGroup))]
    [UpdateAfter(typeof(CrowdGridSystem))]
    public partial struct ContextSteeringSystem : ISystem
    {
        const uint NearInterval = 4;
        const uint FarInterval = 12;
        const float NearDistance = 50f;

        EntityQuery m_Obstacles;

        public void OnCreate(ref SystemState state)
        {
            m_Obstacles = SystemAPI.QueryBuilder().WithAll<Obstacle>().Build();
            state.RequireForUpdate<CrowdGrid>();
            state.RequireForUpdate<FrameContext>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var crowd = SystemAPI.GetSingleton<CrowdGrid>();
            var context = SystemAPI.GetSingleton<FrameContext>();

            state.Dependency = new SteerJob
            {
                SortedPosition = crowd.Grid.SortedPosition,
                BucketStart = crowd.Grid.BucketStart,
                BucketMask = crowd.Grid.BucketMask,
                InvCellSize = crowd.Grid.InvCellSize,
                Obstacles = m_Obstacles.ToComponentDataArray<Obstacle>(state.WorldUpdateAllocator),
                Player = context.PlayerPosition.xz,
                Camera = context.CameraPosition.xz,
                Frame = context.Frame,
            }.ScheduleParallel(JobHandle.CombineDependencies(state.Dependency, crowd.Ready));
        }

        [BurstCompile]
        [WithAll(typeof(EnemyTag))]
        [WithDisabled(typeof(Dying))]
        partial struct SteerJob : IJobEntity
        {
            [ReadOnly] public NativeArray<float2> SortedPosition;
            [ReadOnly] public NativeArray<int> BucketStart;
            [ReadOnly] public NativeArray<Obstacle> Obstacles;
            public int BucketMask;
            public float InvCellSize;
            public float2 Player;
            public float2 Camera;
            public uint Frame;

            void Execute(Entity entity, in LocalTransform transform, in AgentConfig config, ref SteeringState steering)
            {
                float2 position = transform.Position.xz;

                uint interval = math.distancesq(position, Camera) > NearDistance * NearDistance ? FarInterval : NearInterval;
                if (((uint)entity.Index + Frame) % interval != 0)
                    return;

                var danger = default(DangerMap);
                float radius = config.Radius;
                float decisionRadius = radius * 3f;
                float decisionRadiusSq = decisionRadius * decisionRadius;

                // Every neighbour inside the decision radius blocks the directions around it. The scan
                // is uncapped (a capped scan would bias the map toward whichever cells come first) but
                // stops as soon as all eight directions are blocked: the outcome cannot change after that,
                // and dense regions are exactly where that happens.
                int2 cell = SpatialHashGrid.CellOf(position, InvCellSize);
                bool saturated = false;
                for (int dz = -1; dz <= 1 && !saturated; dz++)
                {
                    for (int dx = -1; dx <= 1 && !saturated; dx++)
                    {
                        int bucket = SpatialHashGrid.BucketOf(cell + new int2(dx, dz), BucketMask);
                        int end = BucketStart[bucket + 1];
                        for (int slot = BucketStart[bucket]; slot < end; slot++)
                        {
                            float2 offset = SortedPosition[slot] - position;
                            float distanceSq = math.lengthsq(offset);
                            if (distanceSq >= decisionRadiusSq || distanceSq < 1e-6f)
                                continue;

                            ContextSteering.AddDanger(ref danger, offset * math.rsqrt(distanceSq));
                            if (ContextSteering.IsSaturated(danger))
                            {
                                saturated = true;
                                break;
                            }
                        }
                    }
                }

                // Static obstacles block the same way, inside their radius plus four body radii.
                for (int i = 0; i < Obstacles.Length; i++)
                {
                    float2 offset = Obstacles[i].Center - position;
                    float zone = Obstacles[i].Radius + radius * 4f;
                    float distanceSq = math.lengthsq(offset);
                    if (distanceSq >= zone * zone || distanceSq < 1e-6f)
                        continue;

                    ContextSteering.AddDanger(ref danger, offset * math.rsqrt(distanceSq));
                }

                float2 toPlayer = Player - position;
                float distanceToPlayer = math.length(toPlayer);
                float2 desired = distanceToPlayer > 1e-3f ? toPlayer / distanceToPlayer : float2.zero;
                float interest = distanceToPlayer < config.StopDistance ? 0f : 1f;

                var decision = ContextSteering.Choose(danger, desired, interest);
                bool inGrace = Frame - steering.SpawnFrame < CrowdTiming.SpawnGraceFrames;

                if (decision.Blocked && !inGrace)
                {
                    steering.Direction = float2.zero;
                    steering.Flags = SteeringFlags.Stuck;
                }
                else
                {
                    steering.Direction = decision.HasDirection ? decision.Direction : float2.zero;
                    steering.Flags = SteeringFlags.Active;
                }
            }
        }
    }
}
