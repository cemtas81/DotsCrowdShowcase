using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

namespace DotsCrowd
{
    /// <summary>
    /// Fixed pools of player bullets and enemy shots, created once at startup. Firing claims the next slot
    /// of a ring (reusing the oldest projectile if every slot is in flight) and expiry only hides the entity,
    /// so projectiles never cause a structural change or a command-buffer sync point during play.
    /// A pooled projectile is active while <see cref="Projectile.Life"/> is positive; inactive ones have
    /// their <see cref="MaterialMeshInfo"/> disabled and are skipped by the renderer.
    /// </summary>
    public struct ProjectilePool : IComponentData
    {
        public NativeArray<Entity> Bullets;
        public NativeArray<Entity> Shots;

        /// <summary>Next shot slot. Only touched by the shot-firing job; bullets keep their cursor on the player.</summary>
        public NativeArray<int> ShotCursor;
    }

    [UpdateInGroup(typeof(CrowdSimulationGroup))]
    [UpdateAfter(typeof(CrowdSpawnSystem))]
    [UpdateBefore(typeof(PlayerControlSystem))]
    public partial struct ProjectilePoolSystem : ISystem
    {
        /// <summary>Comfortably above fire rate x lifetime (100/s x 1.8 s).</summary>
        public const int BulletCapacity = 512;

        /// <summary>Upper bound on enemy shots in flight; past it the oldest shot is reused.</summary>
        public const int ShotCapacity = 4096;

        ProjectilePool m_Pool;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerConfig>();
            state.RequireForUpdate<EnemyShotConfig>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (m_Pool.Bullets.IsCreated) m_Pool.Bullets.Dispose();
            if (m_Pool.Shots.IsCreated) m_Pool.Shots.Dispose();
            if (m_Pool.ShotCursor.IsCreated) m_Pool.ShotCursor.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;
            m_Pool = new ProjectilePool
            {
                Bullets = Instantiate(entityManager, SystemAPI.GetSingleton<PlayerConfig>().BulletPrefab, BulletCapacity),
                Shots = Instantiate(entityManager, SystemAPI.GetSingleton<EnemyShotConfig>().Prefab, ShotCapacity),
                ShotCursor = new NativeArray<int>(1, Allocator.Persistent),
            };

            entityManager.CreateSingleton(m_Pool, "ProjectilePool");
            state.Enabled = false;
        }

        static NativeArray<Entity> Instantiate(EntityManager entityManager, Entity prefab, int count)
        {
            var entities = entityManager.Instantiate(prefab, count, Allocator.Persistent);
            for (int i = 0; i < entities.Length; i++)
                entityManager.SetComponentEnabled<MaterialMeshInfo>(entities[i], false);
            return entities;
        }
    }
}
