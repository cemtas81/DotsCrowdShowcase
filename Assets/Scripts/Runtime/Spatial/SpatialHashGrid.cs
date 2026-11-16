using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace DotsCrowd
{
    /// <summary>
    /// Uniform hash grid on the XZ plane, rebuilt every frame with a parallel counting sort:
    /// count agents per bucket, prefix-sum the counts, then scatter. Agents that share a bucket end up
    /// contiguous in <see cref="SortedPosition"/>, so a neighbour query walks linear memory instead of
    /// following hash-map links, and the rebuild never allocates.
    /// </summary>
    public struct SpatialHashGrid : IDisposable
    {
        const int PrefixBlock = 4096;

        public float CellSize;
        public float InvCellSize;
        public int BucketMask;
        public int Capacity;

        /// <summary>Exclusive prefix sum of the bucket counts: bucket b owns [BucketStart[b], BucketStart[b + 1]).</summary>
        public NativeArray<int> BucketStart;

        /// <summary>Scratch: per-bucket counter during the count pass, write cursor during the scatter pass.</summary>
        public NativeArray<int> BucketCursor;

        public NativeArray<int> AgentBucket;
        public NativeArray<int> BlockSums;
        public NativeArray<float2> SortedPosition;

        /// <summary>Index of the source agent stored in each sorted slot.</summary>
        public NativeArray<int> SortedAgent;

        public bool IsCreated => BucketStart.IsCreated;
        public int BucketCount => BucketMask + 1;

        public SpatialHashGrid(int capacity, float cellSize, Allocator allocator)
        {
            capacity = math.max(capacity, 64);

            // About two agents per bucket: keeps the prefix sum short without making buckets crowded.
            int buckets = math.max(1024, math.ceilpow2(capacity) / 2);

            CellSize = cellSize;
            InvCellSize = 1f / cellSize;
            BucketMask = buckets - 1;
            Capacity = capacity;

            BucketStart = new NativeArray<int>(buckets + 1, allocator);
            BucketCursor = new NativeArray<int>(buckets, allocator);
            AgentBucket = new NativeArray<int>(capacity, allocator, NativeArrayOptions.UninitializedMemory);
            BlockSums = new NativeArray<int>((buckets + PrefixBlock - 1) / PrefixBlock, allocator);
            SortedPosition = new NativeArray<float2>(capacity, allocator, NativeArrayOptions.UninitializedMemory);
            SortedAgent = new NativeArray<int>(capacity, allocator, NativeArrayOptions.UninitializedMemory);
        }

        public void Dispose()
        {
            if (BucketStart.IsCreated) BucketStart.Dispose();
            if (BucketCursor.IsCreated) BucketCursor.Dispose();
            if (AgentBucket.IsCreated) AgentBucket.Dispose();
            if (BlockSums.IsCreated) BlockSums.Dispose();
            if (SortedPosition.IsCreated) SortedPosition.Dispose();
            if (SortedAgent.IsCreated) SortedAgent.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 CellOf(float2 position, float invCellSize)
        {
            return (int2)math.floor(position * invCellSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BucketOf(int2 cell, int bucketMask)
        {
            return (int)(math.hash(cell) & (uint)bucketMask);
        }

        /// <summary>Schedules a rebuild from the first <paramref name="count"/> entries of <paramref name="positions"/>.</summary>
        public unsafe JobHandle ScheduleBuild(NativeArray<float2> positions, int count, JobHandle dependency)
        {
            if (count > Capacity)
                throw new ArgumentOutOfRangeException(nameof(count), $"Grid capacity is {Capacity}, got {count} agents.");

            // Raw counters for the atomic count and scatter passes. Access is ordered purely by the job chain
            // (the previous frame's readers are upstream of 'dependency'), so the main-thread check is skipped.
            var counters = (int*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(BucketCursor);

            var clear = new ClearJob { Values = BucketCursor }.Schedule(dependency);

            var assign = new AssignBucketsJob
            {
                Positions = positions,
                AgentBucket = AgentBucket,
                Counters = counters,
                InvCellSize = InvCellSize,
                BucketMask = BucketMask,
            }.Schedule(count, 1024, clear);

            // Parallel exclusive prefix sum: per-block totals, a short serial scan over the blocks, then
            // per-block local sums. Keeps the only serial step tiny however many buckets there are.
            int blocks = BlockSums.Length;
            var blockSums = new BlockSumJob
            {
                Counts = BucketCursor,
                BlockSums = BlockSums,
            }.Schedule(blocks, 1, assign);

            var scanBlocks = new ScanBlocksJob
            {
                BlockSums = BlockSums,
                Starts = BucketStart,
            }.Schedule(blockSums);

            var prefix = new BlockPrefixJob
            {
                Counts = BucketCursor,
                BlockOffsets = BlockSums,
                Starts = BucketStart,
            }.Schedule(blocks, 1, scanBlocks);

            return new ScatterJob
            {
                Positions = positions,
                AgentBucket = AgentBucket,
                Cursors = counters,
                SortedPosition = SortedPosition,
                SortedAgent = SortedAgent,
            }.Schedule(count, 1024, prefix);
        }

        [BurstCompile]
        unsafe struct ClearJob : IJob
        {
            public NativeArray<int> Values;

            public void Execute()
            {
                UnsafeUtility.MemClear(Values.GetUnsafePtr(), (long)Values.Length * sizeof(int));
            }
        }

        [BurstCompile]
        unsafe struct AssignBucketsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Positions;
            [WriteOnly] public NativeArray<int> AgentBucket;
            [NativeDisableUnsafePtrRestriction] public int* Counters;
            public float InvCellSize;
            public int BucketMask;

            public void Execute(int index)
            {
                int bucket = BucketOf(CellOf(Positions[index], InvCellSize), BucketMask);
                AgentBucket[index] = bucket;
                Interlocked.Increment(ref UnsafeUtility.AsRef<int>(Counters + bucket));
            }
        }

        [BurstCompile]
        struct BlockSumJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> Counts;
            [NativeDisableParallelForRestriction] public NativeArray<int> BlockSums;

            public void Execute(int block)
            {
                int end = math.min((block + 1) * PrefixBlock, Counts.Length);
                int sum = 0;
                for (int bucket = block * PrefixBlock; bucket < end; bucket++)
                    sum += Counts[bucket];
                BlockSums[block] = sum;
            }
        }

        [BurstCompile]
        struct ScanBlocksJob : IJob
        {
            public NativeArray<int> BlockSums;
            public NativeArray<int> Starts;

            public void Execute()
            {
                int running = 0;
                for (int block = 0; block < BlockSums.Length; block++)
                {
                    int sum = BlockSums[block];
                    BlockSums[block] = running; // becomes the block's offset
                    running += sum;
                }

                Starts[Starts.Length - 1] = running;
            }
        }

        [BurstCompile]
        struct BlockPrefixJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<int> Counts;
            [ReadOnly] public NativeArray<int> BlockOffsets;
            [NativeDisableParallelForRestriction] public NativeArray<int> Starts;

            public void Execute(int block)
            {
                int end = math.min((block + 1) * PrefixBlock, Counts.Length);
                int sum = BlockOffsets[block];
                for (int bucket = block * PrefixBlock; bucket < end; bucket++)
                {
                    int count = Counts[bucket];
                    Starts[bucket] = sum;
                    Counts[bucket] = sum; // becomes the write cursor for the scatter pass
                    sum += count;
                }
            }
        }

        [BurstCompile]
        unsafe struct ScatterJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Positions;
            [ReadOnly] public NativeArray<int> AgentBucket;
            [NativeDisableUnsafePtrRestriction] public int* Cursors;
            [NativeDisableParallelForRestriction] public NativeArray<float2> SortedPosition;
            [NativeDisableParallelForRestriction] public NativeArray<int> SortedAgent;

            public void Execute(int index)
            {
                int slot = Interlocked.Increment(ref UnsafeUtility.AsRef<int>(Cursors + AgentBucket[index])) - 1;
                SortedPosition[slot] = Positions[index];
                SortedAgent[slot] = index;
            }
        }
    }
}
