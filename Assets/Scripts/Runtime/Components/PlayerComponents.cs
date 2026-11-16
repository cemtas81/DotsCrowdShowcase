using Unity.Entities;
using Unity.Mathematics;

namespace DotsCrowd
{
    public struct PlayerTag : IComponentData
    {
    }

    public struct PlayerConfig : IComponentData
    {
        public Entity BulletPrefab;
        public float MoveSpeed;
        public float Radius;
        public float FireInterval;
        public float BulletSpeed;
        public float BulletLifetime;
        public float BulletScale;
        public float SpreadRadians;
        public float MuzzleHeight;
    }

    /// <summary>Written once per frame by <see cref="PlayerInputSystem"/> on the main thread.</summary>
    public struct PlayerInput : IComponentData
    {
        public float2 Move;
        public float3 AimPoint;
        public bool Fire;
    }

    public struct PlayerState : IComponentData
    {
        public Random Rng;
        public float FireTimer;

        /// <summary>Next slot in the bullet pool.</summary>
        public int NextBullet;
    }
}
