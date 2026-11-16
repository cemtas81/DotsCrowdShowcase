using Unity.Entities;
using UnityEngine;

namespace DotsCrowd
{
    [DisallowMultipleComponent]
    public sealed class CrowdSpawnerAuthoring : MonoBehaviour
    {
        [Header("Crowd")]
        public GameObject MeleePrefab;
        public GameObject RangedPrefab;
        [Min(0)] public int Count = 200000;
        [Tooltip("Out of every MeleeWeight + RangedWeight agents, this many are melee.")]
        [Min(0)] public int MeleeWeight = 5;
        [Min(0)] public int RangedWeight = 1;

        [Header("Rings")]
        [Min(1f)] public float InnerRadius = 75f;
        [Min(0.5f)] public float RingSpacing = 1.5f;

        [Header("Recycling")]
        [Tooltip("Killed agents return beyond the outer edge of the crowd.")]
        [Min(1f)] public float RespawnMinRadius = 250f;
        [Min(1f)] public float RespawnMaxRadius = 275f;

        [Header("Ranged attack")]
        public GameObject EnemyShotPrefab;
        [Min(0.5f)] public float ShotSpeed = 10f;
        [Min(0.1f)] public float ShotLifetime = 3f;
        [Min(0.05f)] public float ShotHitRadius = 0.3f;

        sealed class Baker : Baker<CrowdSpawnerAuthoring>
        {
            public override void Bake(CrowdSpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var transform = GetComponent<Transform>();
                var shotTransform = GetComponent<Transform>(authoring.EnemyShotPrefab);

                AddComponent(entity, new CrowdSpawner
                {
                    MeleePrefab = GetEntity(authoring.MeleePrefab, TransformUsageFlags.Dynamic),
                    RangedPrefab = GetEntity(authoring.RangedPrefab, TransformUsageFlags.Dynamic),
                    Origin = transform.position,
                    Count = authoring.Count,
                    MeleeWeight = authoring.MeleeWeight,
                    RangedWeight = authoring.RangedWeight,
                    InnerRadius = authoring.InnerRadius,
                    RingSpacing = authoring.RingSpacing,
                    RespawnMinRadius = Mathf.Min(authoring.RespawnMinRadius, authoring.RespawnMaxRadius),
                    RespawnMaxRadius = Mathf.Max(authoring.RespawnMinRadius, authoring.RespawnMaxRadius),
                });

                AddComponent(entity, new EnemyShotConfig
                {
                    Prefab = GetEntity(authoring.EnemyShotPrefab, TransformUsageFlags.Dynamic),
                    Speed = authoring.ShotSpeed,
                    Lifetime = authoring.ShotLifetime,
                    Scale = shotTransform.localScale.x,
                    HitRadius = authoring.ShotHitRadius,
                });
            }
        }
    }
}
