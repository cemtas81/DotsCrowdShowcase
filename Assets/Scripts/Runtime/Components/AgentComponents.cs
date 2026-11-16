using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace DotsCrowd
{
    public enum AgentKind : byte
    {
        Melee = 0,
        Ranged = 1,
    }

    public struct EnemyTag : IComponentData
    {
    }

    /// <summary>Per-archetype tuning, baked from the agent prefab.</summary>
    public struct AgentConfig : IComponentData
    {
        public float Radius;
        public float StopDistance;
        public float AttackRange;
        public float MaxSpeed;
        public float AttackCooldown;
        public AgentKind Kind;
    }

    public struct AgentVelocity : IComponentData
    {
        public float2 Value;
    }

    /// <summary>
    /// Cached result of the context-steering pass. Each agent refreshes it every few frames
    /// (amortised by camera distance); locomotion consumes it every frame.
    /// </summary>
    public struct SteeringState : IComponentData
    {
        public float2 Direction;
        public uint SpawnFrame;
        public byte Flags;
    }

    public static class SteeringFlags
    {
        public const byte Active = 1 << 0;
        public const byte Stuck = 1 << 1;
    }

    public struct AttackState : IComponentData
    {
        public float Cooldown;
        public float SwingTimer;
    }

    public struct Health : IComponentData
    {
        public float Value;
        public float Max;
    }

    /// <summary>
    /// Enabled while the death animation plays. Agents are recycled to the outer ring afterwards,
    /// so a kill never causes a structural change.
    /// </summary>
    public struct Dying : IComponentData, IEnableableComponent
    {
        public float Timer;
    }

    /// <summary>
    /// Per-instance input for the voxel unit shader.
    /// x: gait phase [0,1), y: move blend, z: attack swing,
    /// w: emerge from the ground (-1 -> 0) after a spawn, death progress (0 -> 1) after a kill.
    /// </summary>
    [MaterialProperty("_AnimParams")]
    public struct AnimParams : IComponentData
    {
        public float4 Value;
    }

    public static class CrowdTiming
    {
        public const float DeathDuration = 1.1f;
        public const float SwingDuration = 0.45f;
        public const uint SpawnGraceFrames = 30;
        public const uint EmergeFrames = 45;
    }
}
