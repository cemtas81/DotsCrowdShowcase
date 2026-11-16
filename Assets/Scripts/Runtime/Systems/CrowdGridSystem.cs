using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsCrowd
{
    /// <summary>
    /// Frame snapshot of every agent plus the spatial grid built over it. Owned by
    /// <see cref="CrowdGridSystem"/>; read by steering and by bullet hit detection, so one grid serves
    /// both instead of each system hashing the crowd again.
    /// </summary>
    public struct CrowdGrid : IComponentData
    {
        public SpatialHashGrid Grid;
        public NativeArray<float2> AgentPosition;
        public NativeArray<Entity> AgentEntity;
        public NativeArray<byte> AgentAlive;
        public int Count;

        /// <summary>
        /// Completes when this frame's grid is built. Consumers chain their jobs on it instead of reading
        /// the grid through a main-thread sync point.
        /// </summary>
        public JobHandle Ready;
    }

    [BurstCompile]
    [UpdateInGroup(typeof(CrowdSimulationGroup))]
    [UpdateAfter(typeof(PlayerControlSystem))]
    public partial struct CrowdGridSystem : ISystem
    {
        /// <summary>Slightly larger than the widest steering query, so a 3x3 cell scan always covers it.</summary>
        public const float CellSize = 2.5f;

        EntityQuery m_Agents;
        Entity m_GridEntity;
        CrowdGrid m_Storage;
        ComponentTypeHandle<LocalTransform> m_Transforms;
        ComponentTypeHandle<Dying> m_Dying;
        EntityTypeHandle m_Entities;

        public void OnCreate(ref SystemState state)
        {
            m_Agents = SystemAPI.QueryBuilder().WithAll<EnemyTag, LocalTransform>().WithPresent<Dying>().Build();
            m_Storage = Allocate(1024);
            m_GridEntity = state.EntityManager.CreateSingleton(m_Storage, "CrowdGrid");
            m_Transforms = state.GetComponentTypeHandle<LocalTransform>(true);
            m_Dying = state.GetComponentTypeHandle<Dying>(true);
            m_Entities = state.GetEntityTypeHandle();

            state.RequireForUpdate(m_Agents);
            state.RequireForUpdate<CrowdStats>();
        }

        public void OnDestroy(ref SystemState state)
        {
            m_Storage.Ready.Complete();
            Release(ref m_Storage);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int count = m_Agents.CalculateEntityCount();
            if (count > m_Storage.Grid.Capacity)
            {
                // Rare (crowd grew past capacity): make sure no reader still holds the old buffers.
                state.EntityManager.CompleteAllTrackedJobs();
                m_Storage.Ready.Complete();
                Release(ref m_Storage);
                m_Storage = Allocate(math.ceilpow2(count));
            }

            m_Transforms.Update(ref state);
            m_Dying.Update(ref state);
            m_Entities.Update(ref state);

            var chunkAlive = CollectionHelper.CreateNativeArray<int>(m_Agents.CalculateChunkCountWithoutFiltering(), state.WorldUpdateAllocator);
            var baseIndex = m_Agents.CalculateBaseEntityIndexArrayAsync(state.WorldUpdateAllocator, state.Dependency, out var baseIndexReady);

            var extract = new ExtractJob
            {
                Transforms = m_Transforms,
                Dying = m_Dying,
                Entities = m_Entities,
                BaseIndex = baseIndex,
                Positions = m_Storage.AgentPosition,
                AgentEntities = m_Storage.AgentEntity,
                Alive = m_Storage.AgentAlive,
                ChunkAlive = chunkAlive,
            }.ScheduleParallel(m_Agents, baseIndexReady);

            var build = m_Storage.Grid.ScheduleBuild(m_Storage.AgentPosition, count, extract);

            var stats = new CountAliveJob
            {
                ChunkAlive = chunkAlive,
                Count = count,
                Stats = SystemAPI.GetComponentLookup<CrowdStats>(),
                StatsEntity = SystemAPI.GetSingletonEntity<CrowdStats>(),
            }.Schedule(extract);

            // Publish through the EntityManager rather than SystemAPI so the build jobs are not registered as
            // writers of CrowdGrid; readers depend on Ready instead and never block the main thread.
            m_Storage.Count = count;
            m_Storage.Ready = build;
            state.EntityManager.SetComponentData(m_GridEntity, m_Storage);

            state.Dependency = JobHandle.CombineDependencies(build, stats);
        }

        static CrowdGrid Allocate(int capacity)
        {
            return new CrowdGrid
            {
                Grid = new SpatialHashGrid(capacity, CellSize, Allocator.Persistent),
                AgentPosition = new NativeArray<float2>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                AgentEntity = new NativeArray<Entity>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                AgentAlive = new NativeArray<byte>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            };
        }

        static void Release(ref CrowdGrid crowd)
        {
            crowd.Grid.Dispose();
            if (crowd.AgentPosition.IsCreated) crowd.AgentPosition.Dispose();
            if (crowd.AgentEntity.IsCreated) crowd.AgentEntity.Dispose();
            if (crowd.AgentAlive.IsCreated) crowd.AgentAlive.Dispose();
        }

        [BurstCompile]
        struct ExtractJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalTransform> Transforms;
            [ReadOnly] public ComponentTypeHandle<Dying> Dying;
            [ReadOnly] public EntityTypeHandle Entities;
            [ReadOnly] public NativeArray<int> BaseIndex;
            [NativeDisableParallelForRestriction] public NativeArray<float2> Positions;
            [NativeDisableParallelForRestriction] public NativeArray<Entity> AgentEntities;
            [NativeDisableParallelForRestriction] public NativeArray<byte> Alive;
            [NativeDisableParallelForRestriction] public NativeArray<int> ChunkAlive;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref Transforms);
                var entities = chunk.GetNativeArray(Entities);
                int first = BaseIndex[unfilteredChunkIndex];
                int alive = 0;

                for (int i = 0; i < chunk.Count; i++)
                {
                    int index = first + i;
                    byte isAlive = chunk.IsComponentEnabled(ref Dying, i) ? (byte)0 : (byte)1;
                    Positions[index] = transforms[i].Position.xz;
                    AgentEntities[index] = entities[i];
                    Alive[index] = isAlive;
                    alive += isAlive;
                }

                ChunkAlive[unfilteredChunkIndex] = alive;
            }
        }

        [BurstCompile]
        struct CountAliveJob : IJob
        {
            [ReadOnly] public NativeArray<int> ChunkAlive;
            public int Count;
            public ComponentLookup<CrowdStats> Stats;
            public Entity StatsEntity;

            public void Execute()
            {
                int alive = 0;
                for (int i = 0; i < ChunkAlive.Length; i++)
                    alive += ChunkAlive[i];

                var stats = Stats[StatsEntity];
                stats.Agents = Count;
                stats.Alive = alive;
                Stats[StatsEntity] = stats;
            }
        }
    }
}
