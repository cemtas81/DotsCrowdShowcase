using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsCrowd
{
    [DisallowMultipleComponent]
    public sealed class PlayerAuthoring : MonoBehaviour
    {
        public GameObject BulletPrefab;
        [Min(0f)] public float MoveSpeed = 8f;
        [Min(0.1f)] public float Radius = 1.2f;
        [Min(0.001f)] public float FireInterval = 0.01f;
        [Min(1f)] public float BulletSpeed = 26f;
        [Min(0.1f)] public float BulletLifetime = 1.8f;
        [Range(0f, 20f)] public float SpreadDegrees = 4f;
        [Min(0f)] public float MuzzleHeight = 1.8f;
        public uint Seed = 1;

        sealed class Baker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                var bulletTransform = GetComponent<Transform>(authoring.BulletPrefab);
                var transform = GetComponent<Transform>();

                AddComponent<PlayerTag>(entity);
                AddComponent(entity, new PlayerConfig
                {
                    BulletPrefab = GetEntity(authoring.BulletPrefab, TransformUsageFlags.Dynamic),
                    MoveSpeed = authoring.MoveSpeed,
                    Radius = authoring.Radius,
                    FireInterval = authoring.FireInterval,
                    BulletSpeed = authoring.BulletSpeed,
                    BulletLifetime = authoring.BulletLifetime,
                    BulletScale = bulletTransform.localScale.x,
                    SpreadRadians = math.radians(authoring.SpreadDegrees),
                    MuzzleHeight = authoring.MuzzleHeight,
                });
                AddComponent(entity, new PlayerInput { AimPoint = transform.position + transform.forward * 10f });
                AddComponent(entity, new PlayerState { Rng = Unity.Mathematics.Random.CreateFromIndex(authoring.Seed) });
                AddComponent<AnimParams>(entity);
            }
        }
    }
}
