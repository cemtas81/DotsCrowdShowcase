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
    /// Moves both projectile kinds, expires them, and resolves enemy shots against the player. Expired or
    /// spent projectiles go back to the pool by clearing their life and hiding them; nothing is destroyed.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(CrowdSimulationGroup))]
    [UpdateAfter(typeof(AttackSystem))]
    public partial struct ProjectileSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FrameContext>();
            state.RequireForUpdate<EnemyShotConfig>();
            state.RequireForUpdate<CrowdStats>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            var context = SystemAPI.GetSingleton<FrameContext>();
            var shot = SystemAPI.GetSingleton<EnemyShotConfig>();
            var hits = new NativeQueue<int>(Allocator.TempJob);
            float hitRadius = shot.HitRadius + context.PlayerRadius;

            var bullets = new MoveBulletsJob
            {
                DeltaTime = deltaTime,
            }.ScheduleParallel(state.Dependency);

            var shots = new MoveShotsJob
            {
                DeltaTime = deltaTime,
                Player = context.PlayerPosition.xz,
                HitRadiusSq = hitRadius * hitRadius,
                Hits = hits.AsParallelWriter(),
            }.ScheduleParallel(bullets);

            var record = new RecordPlayerHitsJob
            {
                Hits = hits,
                Stats = SystemAPI.GetComponentLookup<CrowdStats>(),
                StatsEntity = SystemAPI.GetSingletonEntity<CrowdStats>(),
            }.Schedule(shots);

            state.Dependency = hits.Dispose(record);
        }

        [BurstCompile]
        [WithAll(typeof(PlayerBulletTag))]
        partial struct MoveBulletsJob : IJobEntity
        {
            public float DeltaTime;

            void Execute(ref LocalTransform transform, ref Projectile projectile, EnabledRefRW<MaterialMeshInfo> visible)
            {
                if (projectile.Life <= 0f)
                    return;

                projectile.Life -= DeltaTime;
                if (projectile.Life <= 0f)
                {
                    Release(ref projectile, visible);
                    return;
                }

                transform.Position += new float3(projectile.Velocity.x, 0f, projectile.Velocity.y) * DeltaTime;
            }
        }

        [BurstCompile]
        [WithAll(typeof(EnemyShotTag))]
        partial struct MoveShotsJob : IJobEntity
        {
            public float DeltaTime;
            public float2 Player;
            public float HitRadiusSq;
            public NativeQueue<int>.ParallelWriter Hits;

            void Execute(ref LocalTransform transform, ref Projectile projectile, EnabledRefRW<MaterialMeshInfo> visible)
            {
                if (projectile.Life <= 0f)
                    return;

                projectile.Life -= DeltaTime;
                transform.Position += new float3(projectile.Velocity.x, 0f, projectile.Velocity.y) * DeltaTime;

                if (math.distancesq(transform.Position.xz, Player) < HitRadiusSq)
                {
                    Hits.Enqueue(1);
                    Release(ref projectile, visible);
                }
                else if (projectile.Life <= 0f)
                {
                    Release(ref projectile, visible);
                }
            }
        }

        /// <summary>Returns a projectile to its pool.</summary>
        public static void Release(ref Projectile projectile, EnabledRefRW<MaterialMeshInfo> visible)
        {
            projectile.Life = 0f;
            visible.ValueRW = false;
        }
    }
}
