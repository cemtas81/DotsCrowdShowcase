using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace DotsCrowd.Tests
{
    public class SpatialHashGridTests
    {
        const float CellSize = 2.5f;

        [Test]
        public void Build_StoresEveryAgentExactlyOnce()
        {
            const int count = 5000;
            var positions = RandomPositions(count, seed: 1);
            var grid = new SpatialHashGrid(count, CellSize, Allocator.TempJob);
            try
            {
                grid.ScheduleBuild(positions, count, default).Complete();

                Assert.AreEqual(count, grid.BucketStart[grid.BucketCount]);
                var seen = new bool[count];
                for (int slot = 0; slot < count; slot++)
                {
                    int agent = grid.SortedAgent[slot];
                    Assert.IsFalse(seen[agent], $"Agent {agent} stored twice.");
                    seen[agent] = true;
                    Assert.AreEqual(positions[agent], grid.SortedPosition[slot]);
                }
            }
            finally
            {
                grid.Dispose();
                positions.Dispose();
            }
        }

        [Test]
        public void Build_KeepsAgentsInsideTheirBucketRange()
        {
            const int count = 3000;
            var positions = RandomPositions(count, seed: 7);
            var grid = new SpatialHashGrid(count, CellSize, Allocator.TempJob);
            try
            {
                grid.ScheduleBuild(positions, count, default).Complete();

                for (int bucket = 0; bucket < grid.BucketCount; bucket++)
                {
                    for (int slot = grid.BucketStart[bucket]; slot < grid.BucketStart[bucket + 1]; slot++)
                    {
                        int2 cell = SpatialHashGrid.CellOf(grid.SortedPosition[slot], grid.InvCellSize);
                        Assert.AreEqual(bucket, SpatialHashGrid.BucketOf(cell, grid.BucketMask));
                    }
                }
            }
            finally
            {
                grid.Dispose();
                positions.Dispose();
            }
        }

        [Test]
        public void Rebuild_PrefixSumAcrossBlocksMatchesBucketCounts()
        {
            // Large enough for the prefix sum to span several blocks; built twice to check the grid resets.
            const int count = 40000;
            var first = RandomPositions(count, seed: 3);
            var second = RandomPositions(count, seed: 11);
            var grid = new SpatialHashGrid(count, CellSize, Allocator.TempJob);
            try
            {
                grid.ScheduleBuild(first, count, default).Complete();
                grid.ScheduleBuild(second, count, default).Complete();

                var expected = new int[grid.BucketCount];
                for (int i = 0; i < count; i++)
                    expected[SpatialHashGrid.BucketOf(SpatialHashGrid.CellOf(second[i], grid.InvCellSize), grid.BucketMask)]++;

                Assert.AreEqual(0, grid.BucketStart[0]);
                Assert.AreEqual(count, grid.BucketStart[grid.BucketCount]);
                for (int bucket = 0; bucket < grid.BucketCount; bucket++)
                    Assert.AreEqual(expected[bucket], grid.BucketStart[bucket + 1] - grid.BucketStart[bucket], $"Bucket {bucket}.");
            }
            finally
            {
                grid.Dispose();
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void NeighbourScan_MatchesBruteForce()
        {
            const int count = 4000;
            const float radius = 2.4f; // must not exceed the cell size for a 3x3 scan
            var positions = RandomPositions(count, seed: 42);
            var grid = new SpatialHashGrid(count, CellSize, Allocator.TempJob);
            try
            {
                grid.ScheduleBuild(positions, count, default).Complete();

                for (int query = 0; query < count; query += 37)
                {
                    float2 origin = positions[query];
                    var expected = new HashSet<int>();
                    for (int other = 0; other < count; other++)
                    {
                        if (math.distancesq(positions[other], origin) < radius * radius)
                            expected.Add(other);
                    }

                    var found = new HashSet<int>();
                    int2 cell = SpatialHashGrid.CellOf(origin, grid.InvCellSize);
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int bucket = SpatialHashGrid.BucketOf(cell + new int2(dx, dz), grid.BucketMask);
                            for (int slot = grid.BucketStart[bucket]; slot < grid.BucketStart[bucket + 1]; slot++)
                            {
                                if (math.distancesq(grid.SortedPosition[slot], origin) < radius * radius)
                                    found.Add(grid.SortedAgent[slot]);
                            }
                        }
                    }

                    CollectionAssert.AreEquivalent(expected, found, $"Mismatch around agent {query}.");
                }
            }
            finally
            {
                grid.Dispose();
                positions.Dispose();
            }
        }

        static NativeArray<float2> RandomPositions(int count, uint seed)
        {
            var random = new Random(seed);
            var positions = new NativeArray<float2>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++)
                positions[i] = random.NextFloat2(new float2(-60f), new float2(60f));
            return positions;
        }
    }
}
