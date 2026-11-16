using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsCrowd
{
    /// <summary>
    /// Integrates agent movement from the cached steering decision and enforces the player's body as a
    /// hard constraint. Runs per chunk and only takes write access to a chunk when an agent in it will
    /// actually change: chunks of settled agents keep their change version, so the transform, render
    /// bounds and GPU upload passes skip them entirely.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(CrowdSimulationGroup))]
    [UpdateAfter(typeof(ContextSteeringSystem))]
    public partial struct LocomotionSystem : ISystem
    {
        EntityQuery m_Agents;
        ComponentTypeHandle<LocalTransform> m_Transforms;
        ComponentTypeHandle<AgentVelocity> m_Velocities;
        ComponentTypeHandle<SteeringState> m_Steering;
        ComponentTypeHandle<AgentConfig> m_Configs;

        public void OnCreate(ref SystemState state)
        {
            m_Agents = SystemAPI.QueryBuilder()
                .WithAllRW<LocalTransform, AgentVelocity>()
                .WithAll<EnemyTag, SteeringState, AgentConfig>()
                .WithDisabled<Dying>()
                .Build();

            m_Transforms = state.GetComponentTypeHandle<LocalTransform>();
            m_Velocities = state.GetComponentTypeHandle<AgentVelocity>();
            m_Steering = state.GetComponentTypeHandle<SteeringState>(true);
            m_Configs = state.GetComponentTypeHandle<AgentConfig>(true);

            state.RequireForUpdate(m_Agents);
            state.RequireForUpdate<FrameContext>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_Transforms.Update(ref state);
            m_Velocities.Update(ref state);
            m_Steering.Update(ref state);
            m_Configs.Update(ref state);

            var context = SystemAPI.GetSingleton<FrameContext>();
            state.Dependency = new LocomotionJob
            {
                Transforms = m_Transforms,
                Velocities = m_Velocities,
                Steering = m_Steering,
                Configs = m_Configs,
                DeltaTime = SystemAPI.Time.DeltaTime,
                Player = context.PlayerPosition.xz,
                PlayerRadius = context.PlayerRadius,
            }.ScheduleParallel(m_Agents, state.Dependency);
        }

        [BurstCompile]
        unsafe struct LocomotionJob : IJobChunk
        {
            const float Acceleration = 15f;
            const float Braking = 10f;
            const float TurnRate = 2.5f;
            const float AttackTurnRate = 8f;
            const float SettleSpeed = 1e-3f;

            // |dot| of two yaw quaternions is cos(half the angle between them): this stops turning within ~1.8 degrees.
            const float FacingTolerance = 0.99988f;

            public ComponentTypeHandle<LocalTransform> Transforms;
            public ComponentTypeHandle<AgentVelocity> Velocities;
            [ReadOnly] public ComponentTypeHandle<SteeringState> Steering;
            [ReadOnly] public ComponentTypeHandle<AgentConfig> Configs;
            public float DeltaTime;
            public float2 Player;
            public float PlayerRadius;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var steering = chunk.GetNativeArray(ref Steering);
                var configs = chunk.GetNativeArray(ref Configs);

                // Read through RO pointers first: requesting a writable array bumps the chunk's change version.
                var transformsIn = (LocalTransform*)chunk.GetComponentDataPtrRO(ref Transforms);
                var velocitiesIn = (AgentVelocity*)chunk.GetComponentDataPtrRO(ref Velocities);

                bool anyChange = false;
                var scan = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (scan.NextEntityIndex(out int i))
                {
                    if (Step(transformsIn[i], velocitiesIn[i], steering[i], configs[i], out _, out _))
                    {
                        anyChange = true;
                        break;
                    }
                }

                if (!anyChange)
                    return;

                var transforms = chunk.GetNativeArray(ref Transforms);
                var velocities = chunk.GetNativeArray(ref Velocities);
                var apply = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (apply.NextEntityIndex(out int i))
                {
                    if (Step(transforms[i], velocities[i], steering[i], configs[i], out var transform, out var velocity))
                    {
                        transforms[i] = transform;
                        velocities[i] = velocity;
                    }
                }
            }

            bool Step(in LocalTransform transform, in AgentVelocity velocity, in SteeringState steering, in AgentConfig config,
                out LocalTransform nextTransform, out AgentVelocity nextVelocity)
            {
                float2 position = transform.Position.xz;
                float2 current = velocity.Value;
                float2 toPlayer = Player - position;
                float distance = math.length(toPlayer);
                float2 facing = float2.zero;
                float turnRate = TurnRate;

                if ((steering.Flags & SteeringFlags.Stuck) != 0)
                {
                    current = float2.zero;
                }
                else if (distance <= config.AttackRange || distance < 0.1f)
                {
                    current = math.lerp(current, float2.zero, math.saturate(DeltaTime * Braking));
                    if (math.lengthsq(current) < SettleSpeed * SettleSpeed)
                        current = float2.zero;
                    if (distance > 1e-3f)
                    {
                        facing = toPlayer / distance;
                        turnRate = AttackTurnRate;
                    }
                }
                else
                {
                    float2 desired = (steering.Flags & SteeringFlags.Active) != 0 ? steering.Direction : toPlayer / distance;
                    current = math.lerp(current, desired * config.MaxSpeed, math.saturate(DeltaTime * Acceleration));

                    float speed = math.length(current);
                    if (speed > SettleSpeed)
                    {
                        position += current * DeltaTime;
                        facing = current / speed;
                    }
                    else
                    {
                        current = float2.zero;
                    }
                }

                // Hard constraint: never overlap the player's body.
                float clearance = PlayerRadius + config.Radius;
                float2 away = position - Player;
                float awaySq = math.lengthsq(away);
                if (awaySq < clearance * clearance && awaySq > 1e-6f)
                    position = Player + away * (clearance * math.rsqrt(awaySq));

                quaternion rotation = transform.Rotation;
                if (math.lengthsq(facing) > 0f)
                {
                    quaternion target = YawTowards(facing);
                    if (math.abs(math.dot(rotation, target)) < FacingTolerance)
                        rotation = math.nlerp(rotation, target, math.saturate(DeltaTime * turnRate));
                }

                nextTransform = transform;
                nextVelocity = velocity;
                if (position.Equals(transform.Position.xz) && rotation.Equals(transform.Rotation) && current.Equals(velocity.Value))
                    return false;

                nextTransform.Position = new float3(position.x, transform.Position.y, position.y);
                nextTransform.Rotation = rotation;
                nextVelocity.Value = current;
                return true;
            }

            /// <summary>
            /// Shortest-arc rotation from +Z to a unit direction on the XZ plane, without trigonometry:
            /// normalize(cross(+Z, d), 1 + dot(+Z, d)).
            /// </summary>
            static quaternion YawTowards(float2 direction)
            {
                float w = 1f + direction.y;
                return w > 1e-4f
                    ? math.normalize(new quaternion(0f, direction.x, 0f, w))
                    : new quaternion(0f, 1f, 0f, 0f);
            }
        }
    }
}
