using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace DotsCrowd
{
    /// <summary>
    /// Moves and aims the player, fires bullets and publishes the frame context. A single entity,
    /// so it runs on the main thread; bullets are taken from the projectile pool.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(CrowdSimulationGroup))]
    [UpdateAfter(typeof(CrowdSpawnSystem))]
    public partial struct PlayerControlSystem : ISystem
    {
        const float TurnRate = 14f;
        const float WorldExtent = 240f;

        EntityQuery m_Obstacles;

        public void OnCreate(ref SystemState state)
        {
            m_Obstacles = SystemAPI.QueryBuilder().WithAll<Obstacle>().Build();
            state.RequireForUpdate<FrameContext>();
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<ProjectilePool>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            ref var context = ref SystemAPI.GetSingletonRW<FrameContext>().ValueRW;
            context.Frame++;

            var obstacles = m_Obstacles.ToComponentDataArray<Obstacle>(Allocator.Temp);
            var bullets = SystemAPI.GetSingleton<ProjectilePool>().Bullets;

            foreach (var (transformRef, inputRef, configRef, playerRef, animationRef) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<PlayerInput>, RefRO<PlayerConfig>, RefRW<PlayerState>, RefRW<AnimParams>>()
                         .WithAll<PlayerTag>())
            {
                ref var transform = ref transformRef.ValueRW;
                ref var player = ref playerRef.ValueRW;
                var config = configRef.ValueRO;
                var input = inputRef.ValueRO;

                float2 move = input.Move;
                if (math.lengthsq(move) > 1f)
                    move = math.normalize(move);

                ref var animation = ref animationRef.ValueRW.Value;
                animation.y = math.lerp(animation.y, math.length(move), math.saturate(deltaTime * 8f));
                animation.x = math.frac(animation.x + deltaTime * math.lerp(0.5f, 1.8f, animation.y));

                float2 position = transform.Position.xz + move * (config.MoveSpeed * deltaTime);

                for (int i = 0; i < obstacles.Length; i++)
                {
                    float clearance = obstacles[i].Radius + config.Radius;
                    float2 offset = position - obstacles[i].Center;
                    float distanceSq = math.lengthsq(offset);
                    if (distanceSq < clearance * clearance)
                        position = obstacles[i].Center + math.normalizesafe(offset, new float2(1f, 0f)) * clearance;
                }

                position = math.clamp(position, -WorldExtent, WorldExtent);
                transform.Position = new float3(position.x, transform.Position.y, position.y);

                float2 aim = input.AimPoint.xz - position;
                float2 forward = math.normalizesafe(aim, math.mul(transform.Rotation, new float3(0f, 0f, 1f)).xz);
                if (math.lengthsq(aim) > 0.01f)
                {
                    var target = quaternion.RotateY(math.atan2(forward.x, forward.y));
                    transform.Rotation = math.slerp(transform.Rotation, target, math.saturate(deltaTime * TurnRate));
                }

                player.FireTimer -= deltaTime;
                if (input.Fire)
                {
                    for (int shot = 0; player.FireTimer <= 0f && shot < 8; shot++)
                    {
                        player.FireTimer += config.FireInterval;

                        float spread = player.Rng.NextFloat(-config.SpreadRadians, config.SpreadRadians);
                        math.sincos(spread, out float sin, out float cos);
                        float2 direction = new float2(forward.x * cos + forward.y * sin, forward.y * cos - forward.x * sin);
                        float3 muzzle = new float3(position.x + direction.x * config.Radius, config.MuzzleHeight, position.y + direction.y * config.Radius);

                        var bullet = bullets[player.NextBullet];
                        player.NextBullet = (player.NextBullet + 1) % bullets.Length;
                        SystemAPI.SetComponent(bullet, LocalTransform.FromPositionRotationScale(
                            muzzle, quaternion.RotateY(math.atan2(direction.x, direction.y)), config.BulletScale));
                        SystemAPI.SetComponent(bullet, new Projectile
                        {
                            Velocity = direction * config.BulletSpeed,
                            Life = config.BulletLifetime,
                        });
                        SystemAPI.SetComponentEnabled<MaterialMeshInfo>(bullet, true);
                    }
                }
                else
                {
                    player.FireTimer = math.max(player.FireTimer, 0f);
                }

                context.PlayerPosition = transform.Position;
                context.PlayerRadius = config.Radius;
            }
        }
    }
}
