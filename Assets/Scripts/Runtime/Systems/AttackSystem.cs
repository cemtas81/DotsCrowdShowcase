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
    /// Agents in range attack on a cooldown: melee hits land immediately, ranged agents queue a shot that
    /// a single follow-up job takes from the projectile pool. Hits are counted on a queue and folded into
    /// the stats the same way.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(CrowdSimulationGroup))]
    [UpdateAfter(typeof(LocomotionSystem))]
    public partial struct AttackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FrameContext>();
            state.RequireForUpdate<EnemyShotConfig>();
            state.RequireForUpdate<CrowdStats>();
            state.RequireForUpdate<ProjectilePool>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var context = SystemAPI.GetSingleton<FrameContext>();
            var shotConfig = SystemAPI.GetSingleton<EnemyShotConfig>();
            var pool = SystemAPI.GetSingleton<ProjectilePool>();
            var hits = new NativeQueue<int>(Allocator.TempJob);
            var shots = new NativeQueue<ShotRequest>(Allocator.TempJob);

            var attack = new AttackJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Player = context.PlayerPosition.xz,
                ShotSpeed = shotConfig.Speed,
                Shots = shots.AsParallelWriter(),
                Hits = hits.AsParallelWriter(),
            }.ScheduleParallel(state.Dependency);

            var record = new RecordPlayerHitsJob
            {
                Hits = hits,
                Stats = SystemAPI.GetComponentLookup<CrowdStats>(),
                StatsEntity = SystemAPI.GetSingletonEntity<CrowdStats>(),
            }.Schedule(attack);

            var fire = new FireShotsJob
            {
                Requests = shots,
                Pool = pool.Shots,
                Cursor = pool.ShotCursor,
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(),
                Projectiles = SystemAPI.GetComponentLookup<Projectile>(),
                Visible = SystemAPI.GetComponentLookup<MaterialMeshInfo>(),
                Scale = shotConfig.Scale,
                Lifetime = shotConfig.Lifetime,
            }.Schedule(attack);

            state.Dependency = JobHandle.CombineDependencies(hits.Dispose(record), shots.Dispose(fire));
        }

        [BurstCompile]
        [WithAll(typeof(EnemyTag))]
        [WithDisabled(typeof(Dying))]
        partial struct AttackJob : IJobEntity
        {
            public float DeltaTime;
            public float2 Player;
            public float ShotSpeed;
            public NativeQueue<ShotRequest>.ParallelWriter Shots;
            public NativeQueue<int>.ParallelWriter Hits;

            void Execute(in LocalTransform transform, in AgentConfig config, ref AttackState attack)
            {
                attack.SwingTimer = math.max(0f, attack.SwingTimer - DeltaTime);
                attack.Cooldown -= DeltaTime;
                if (attack.Cooldown > 0f)
                    return;

                float2 toPlayer = Player - transform.Position.xz;
                if (math.lengthsq(toPlayer) > config.AttackRange * config.AttackRange)
                {
                    attack.Cooldown = 0f;
                    return;
                }

                attack.Cooldown = config.AttackCooldown;
                attack.SwingTimer = CrowdTiming.SwingDuration;

                if (config.Kind == AgentKind.Melee)
                {
                    Hits.Enqueue(1);
                    return;
                }

                Shots.Enqueue(new ShotRequest
                {
                    Origin = transform.Position + new float3(0f, 0.8f, 0f),
                    Velocity = math.normalizesafe(toPlayer) * ShotSpeed,
                });
            }
        }

        struct ShotRequest
        {
            public float3 Origin;
            public float2 Velocity;
        }

        /// <summary>Hands queued shots to pooled projectiles in order, reusing the oldest when the ring wraps.</summary>
        [BurstCompile]
        struct FireShotsJob : IJob
        {
            public NativeQueue<ShotRequest> Requests;
            [ReadOnly] public NativeArray<Entity> Pool;
            public NativeArray<int> Cursor;
            public ComponentLookup<LocalTransform> Transforms;
            public ComponentLookup<Projectile> Projectiles;
            public ComponentLookup<MaterialMeshInfo> Visible;
            public float Scale;
            public float Lifetime;

            public void Execute()
            {
                int cursor = Cursor[0];
                while (Requests.TryDequeue(out var request))
                {
                    var shot = Pool[cursor];
                    cursor = cursor + 1 == Pool.Length ? 0 : cursor + 1;

                    Transforms[shot] = LocalTransform.FromPositionRotationScale(request.Origin, quaternion.identity, Scale);
                    Projectiles[shot] = new Projectile { Velocity = request.Velocity, Life = Lifetime };
                    Visible.SetComponentEnabled(shot, true);
                }

                Cursor[0] = cursor;
            }
        }
    }

    [BurstCompile]
    struct RecordPlayerHitsJob : IJob
    {
        public NativeQueue<int> Hits;
        public ComponentLookup<CrowdStats> Stats;
        public Entity StatsEntity;

        public void Execute()
        {
            int total = 0;
            while (Hits.TryDequeue(out int hit))
                total += hit;

            if (total == 0)
                return;

            var stats = Stats[StatsEntity];
            stats.PlayerHits += total;
            Stats[StatsEntity] = stats;
        }
    }
}
