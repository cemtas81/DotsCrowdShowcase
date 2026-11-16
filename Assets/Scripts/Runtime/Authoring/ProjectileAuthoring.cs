using Unity.Entities;
using UnityEngine;

namespace DotsCrowd
{
    public enum ProjectileKind
    {
        PlayerBullet,
        EnemyShot,
    }

    [DisallowMultipleComponent]
    public sealed class ProjectileAuthoring : MonoBehaviour
    {
        public ProjectileKind Kind = ProjectileKind.PlayerBullet;

        sealed class Baker : Baker<ProjectileAuthoring>
        {
            public override void Bake(ProjectileAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<Projectile>(entity);
                if (authoring.Kind == ProjectileKind.PlayerBullet)
                    AddComponent<PlayerBulletTag>(entity);
                else
                    AddComponent<EnemyShotTag>(entity);
            }
        }
    }
}
