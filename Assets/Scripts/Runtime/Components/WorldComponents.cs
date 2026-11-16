using Unity.Entities;
using Unity.Mathematics;

namespace DotsCrowd
{
    /// <summary>Spawns the whole crowd once, as concentric rings around <see cref="Origin"/>.</summary>
    public struct CrowdSpawner : IComponentData
    {
        public Entity MeleePrefab;
        public Entity RangedPrefab;
        public float3 Origin;
        public int Count;
        public int MeleeWeight;
        public int RangedWeight;
        public float InnerRadius;
        public float RingSpacing;
        public float RespawnMinRadius;
        public float RespawnMaxRadius;
    }

    public struct CrowdSpawned : IComponentData
    {
    }

    /// <summary>Optional singleton (set from the command line) that replaces the authored crowd size.</summary>
    public struct CrowdCountOverride : IComponentData
    {
        public int Count;
    }

    public struct EnemyShotConfig : IComponentData
    {
        public Entity Prefab;
        public float Speed;
        public float Lifetime;
        public float Scale;
        public float HitRadius;
    }

    public struct Projectile : IComponentData
    {
        public float2 Velocity;
        public float Life;
    }

    public struct PlayerBulletTag : IComponentData
    {
    }

    public struct EnemyShotTag : IComponentData
    {
    }

    /// <summary>Static obstacle approximated by a circle on the XZ plane.</summary>
    public struct Obstacle : IComponentData
    {
        public float2 Center;
        public float Radius;
    }

    /// <summary>Per-frame values shared by all systems; only written on the main thread.</summary>
    public struct FrameContext : IComponentData
    {
        public float3 PlayerPosition;
        public float3 CameraPosition;
        public float PlayerRadius;
        public uint Frame;
    }

    public struct CrowdStats : IComponentData
    {
        public int Agents;
        public int Alive;
        public int Kills;
        public int PlayerHits;
    }
}
