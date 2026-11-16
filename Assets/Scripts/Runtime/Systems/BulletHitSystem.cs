using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace DotsCrowd
{
    /// <summary>
    /// Resolves player bullets against the crowd using the grid built earlier in the frame. Bullets are
    /// tested in parallel; the resulting hits are applied by one job, so two bullets striking the same
    /// agent never race.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(CrowdSimulationGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    public partial struct BulletHitSystem : ISystem
    {
        const float HitRadius = 0.75f;
        const float Damage = 1f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrowdGrid>();
            state.RequireForUpdate<CrowdStats>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var crowd = SystemAPI.GetSingleton<CrowdGrid>();
            var hits = new NativeQueue<Entity>(Allocator.TempJob);

            var find = new FindHitsJob
            {
                SortedPosition = crowd.Grid.SortedPosition,
                SortedAgent = crowd.Grid.SortedAgent,
                BucketStart = crowd.Grid.BucketStart,
                AgentEntity = crowd.AgentEntity,
                AgentAlive = crowd.AgentAlive,
                BucketMask = crowd.Grid.BucketMask,
                InvCellSize = crowd.Grid.InvCellSize,
                HitRadiusSq = HitRadius * HitRadius,
                Hits = hits.AsParallelWriter(),
            }.ScheduleParallel(JobHandle.CombineDependencies(state.Dependency, crowd.Ready));

            var apply = new ApplyHitsJob
            {
                Hits = hits,
                Health = SystemAPI.GetComponentLookup<Health>(),
                Dying = SystemAPI.GetComponentLookup<Dying>(),
                Stats = SystemAPI.GetComponentLookup<CrowdStats>(),
                StatsEntity = SystemAPI.GetSingletonEntity<CrowdStats>(),
                Damage = Damage,
            }.Schedule(find);

            state.Dependency = hits.Dispose(apply);
        }

        [BurstCompile]
        [WithAll(typeof(PlayerBulletTag))]
        partial struct FindHitsJob : IJobEntity
        {
            [ReadOnly] public NativeArray<float2> SortedPosition;
            [ReadOnly] public NativeArray<int> SortedAgent;
            [ReadOnly] public NativeArray<int> BucketStart;
            [ReadOnly] public NativeArray<Entity> AgentEntity;
            [ReadOnly] public NativeArray<byte> AgentAlive;
            public int BucketMask;
            public float InvCellSize;
            public float HitRadiusSq;
            public NativeQueue<Entity>.ParallelWriter Hits;

            void Execute(in LocalTransform transform, ref Projectile projectile, EnabledRefRW<MaterialMeshInfo> visible)
            {
                // Pooled bullets that are not in flight.
                if (projectile.Life <= 0f)
                    return;

                float2 position = transform.Position.xz;
                int2 cell = SpatialHashGrid.CellOf(position, InvCellSize);

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int bucket = SpatialHashGrid.BucketOf(cell + new int2(dx, dz), BucketMask);
                        int end = BucketStart[bucket + 1];
                        for (int slot = BucketStart[bucket]; slot < end; slot++)
                        {
                            int agent = SortedAgent[slot];
                            if (AgentAlive[agent] == 0 || math.distancesq(SortedPosition[slot], position) >= HitRadiusSq)
                                continue;

                            Hits.Enqueue(AgentEntity[agent]);
                            ProjectileSystem.Release(ref projectile, visible);
                            return;
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct ApplyHitsJob : IJob
        {
            public NativeQueue<Entity> Hits;
            public ComponentLookup<Health> Health;
            public ComponentLookup<Dying> Dying;
            public ComponentLookup<CrowdStats> Stats;
            public Entity StatsEntity;
            public float Damage;

            public void Execute()
            {
                int kills = 0;
                while (Hits.TryDequeue(out var agent))
                {
                    if (Dying.IsComponentEnabled(agent))
                        continue;

                    var health = Health[agent];
                    health.Value -= Damage;
                    Health[agent] = health;
                    if (health.Value > 0f)
                        continue;

                    Dying[agent] = new Dying { Timer = CrowdTiming.DeathDuration };
                    Dying.SetComponentEnabled(agent, true);
                    kills++;
                }

                if (kills == 0)
                    return;

                var stats = Stats[StatsEntity];
                stats.Kills += kills;
                Stats[StatsEntity] = stats;
            }
        }
    }
}
