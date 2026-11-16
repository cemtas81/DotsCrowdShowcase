using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsCrowd
{
    /// <summary>
    /// Drives the per-instance shader animation. Every agent carries its own gait phase (seeded at spawn), so
    /// the crowd never moves in lockstep. Idle breathing is computed in the shader from time, which means a
    /// settled agent needs no CPU writes at all: chunks whose agents are all at rest are left untouched and
    /// Entities Graphics skips re-uploading them.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(CrowdSimulationGroup), OrderLast = true)]
    public partial struct CrowdAnimationSystem : ISystem
    {
        EntityQuery m_Agents;
        ComponentTypeHandle<AnimParams> m_Animation;
        ComponentTypeHandle<AgentVelocity> m_Velocities;
        ComponentTypeHandle<AgentConfig> m_Configs;
        ComponentTypeHandle<AttackState> m_Attacks;
        ComponentTypeHandle<SteeringState> m_Steering;
        ComponentTypeHandle<Dying> m_Dying;

        public void OnCreate(ref SystemState state)
        {
            m_Agents = SystemAPI.QueryBuilder()
                .WithAllRW<AnimParams>()
                .WithAll<EnemyTag, AgentVelocity, AgentConfig, AttackState, SteeringState>()
                .WithPresent<Dying>()
                .Build();

            m_Animation = state.GetComponentTypeHandle<AnimParams>();
            m_Velocities = state.GetComponentTypeHandle<AgentVelocity>(true);
            m_Configs = state.GetComponentTypeHandle<AgentConfig>(true);
            m_Attacks = state.GetComponentTypeHandle<AttackState>(true);
            m_Steering = state.GetComponentTypeHandle<SteeringState>(true);
            m_Dying = state.GetComponentTypeHandle<Dying>(true);

            state.RequireForUpdate(m_Agents);
            state.RequireForUpdate<FrameContext>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_Animation.Update(ref state);
            m_Velocities.Update(ref state);
            m_Configs.Update(ref state);
            m_Attacks.Update(ref state);
            m_Steering.Update(ref state);
            m_Dying.Update(ref state);

            state.Dependency = new AnimateJob
            {
                Animation = m_Animation,
                Velocities = m_Velocities,
                Configs = m_Configs,
                Attacks = m_Attacks,
                Steering = m_Steering,
                Dying = m_Dying,
                DeltaTime = SystemAPI.Time.DeltaTime,
                Frame = SystemAPI.GetSingleton<FrameContext>().Frame,
            }.ScheduleParallel(m_Agents, state.Dependency);
        }

        [BurstCompile]
        unsafe struct AnimateJob : IJobChunk
        {
            const float WalkFrequency = 2.4f;
            const float BlendRate = 8f;

            public ComponentTypeHandle<AnimParams> Animation;
            [ReadOnly] public ComponentTypeHandle<AgentVelocity> Velocities;
            [ReadOnly] public ComponentTypeHandle<AgentConfig> Configs;
            [ReadOnly] public ComponentTypeHandle<AttackState> Attacks;
            [ReadOnly] public ComponentTypeHandle<SteeringState> Steering;
            [ReadOnly] public ComponentTypeHandle<Dying> Dying;
            public float DeltaTime;
            public uint Frame;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var velocities = chunk.GetNativeArray(ref Velocities);
                var configs = chunk.GetNativeArray(ref Configs);
                var attacks = chunk.GetNativeArray(ref Attacks);
                var steering = chunk.GetNativeArray(ref Steering);
                var dying = chunk.GetNativeArray(ref Dying);
                var dead = chunk.GetEnabledMask(ref Dying);
                var current = (AnimParams*)chunk.GetComponentDataPtrRO(ref Animation);

                bool anyChange = false;
                for (int i = 0; i < chunk.Count && !anyChange; i++)
                    anyChange = !Next(current[i].Value, velocities[i], configs[i], attacks[i], steering[i], dying[i], dead[i]).Equals(current[i].Value);

                if (!anyChange)
                    return;

                var animation = chunk.GetNativeArray(ref Animation);
                for (int i = 0; i < chunk.Count; i++)
                    animation[i] = new AnimParams { Value = Next(animation[i].Value, velocities[i], configs[i], attacks[i], steering[i], dying[i], dead[i]) };
            }

            float4 Next(float4 value, in AgentVelocity velocity, in AgentConfig config, in AttackState attack,
                in SteeringState steering, in Dying dying, bool dead)
            {
                float speed = dead ? 0f : math.saturate(math.length(velocity.Value) / math.max(config.MaxSpeed, 1e-3f));
                value.y = math.lerp(value.y, speed, math.saturate(DeltaTime * BlendRate));
                if (speed == 0f && value.y < 0.002f)
                    value.y = 0f;

                // The gait phase only advances while walking; at rest it stays put and doubles as the
                // per-agent offset of the shader's time-based idle breathing.
                if (value.y > 0f)
                    value.x = math.frac(value.x + DeltaTime * WalkFrequency);

                value.z = math.saturate(attack.SwingTimer / CrowdTiming.SwingDuration);

                if (dead)
                {
                    value.w = math.saturate(1f - dying.Timer / CrowdTiming.DeathDuration);
                }
                else
                {
                    float sinceSpawn = Frame - steering.SpawnFrame;
                    value.w = math.saturate(sinceSpawn / CrowdTiming.EmergeFrames) - 1f;
                }

                return value;
            }
        }
    }
}
