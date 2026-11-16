using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsCrowd
{
    /// <summary>
    /// Plays out the death timer, then returns the agent to a random point on the outer ring with full
    /// health. The crowd size stays constant and no entity is ever destroyed or created mid-game.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(CrowdSimulationGroup))]
    [UpdateAfter(typeof(BulletHitSystem))]
    public partial struct DeathRecycleSystem : ISystem
    {
        EntityQuery m_Dying;

        public void OnCreate(ref SystemState state)
        {
            // Requiring Dying (enabled) means only chunks that hold a dying agent are visited, so the
            // transforms of everyone else are never touched by this system.
            m_Dying = SystemAPI.QueryBuilder()
                .WithAll<EnemyTag>()
                .WithAllRW<Dying, LocalTransform>()
                .WithAllRW<Health, AgentVelocity>()
                .WithAllRW<SteeringState, AttackState>()
                .Build();

            state.RequireForUpdate<FrameContext>();
            state.RequireForUpdate<CrowdSpawner>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var context = SystemAPI.GetSingleton<FrameContext>();
            var spawner = SystemAPI.GetSingleton<CrowdSpawner>();

            state.Dependency = new RecycleJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Player = context.PlayerPosition.xz,
                MinRadiusSq = spawner.RespawnMinRadius * spawner.RespawnMinRadius,
                MaxRadiusSq = spawner.RespawnMaxRadius * spawner.RespawnMaxRadius,
                Frame = context.Frame,
            }.ScheduleParallel(m_Dying, state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(EnemyTag))]
        partial struct RecycleJob : IJobEntity
        {
            public float DeltaTime;
            public float2 Player;
            public float MinRadiusSq;
            public float MaxRadiusSq;
            public uint Frame;

            void Execute(
                Entity entity,
                ref Dying dying,
                EnabledRefRW<Dying> dyingEnabled,
                ref LocalTransform transform,
                ref Health health,
                ref AgentVelocity velocity,
                ref SteeringState steering,
                ref AttackState attack)
            {
                if (!dyingEnabled.ValueRO)
                    return;

                dying.Timer -= DeltaTime;
                if (dying.Timer > 0f)
                    return;

                var rng = Random.CreateFromIndex((uint)entity.Index * 0x9E3779B9u ^ Frame);
                math.sincos(rng.NextFloat(0f, 2f * math.PI), out float sin, out float cos);
                float radius = math.sqrt(rng.NextFloat(MinRadiusSq, MaxRadiusSq));
                float2 position = Player + new float2(cos, sin) * radius;

                transform.Position = new float3(position.x, transform.Position.y, position.y);
                transform.Rotation = quaternion.RotateY(math.atan2(-cos, -sin));
                health.Value = health.Max;
                velocity.Value = float2.zero;
                steering = new SteeringState { SpawnFrame = Frame };
                attack = new AttackState { Cooldown = rng.NextFloat(0f, 1.5f) };
                dyingEnabled.ValueRW = false;
            }
        }
    }
}
