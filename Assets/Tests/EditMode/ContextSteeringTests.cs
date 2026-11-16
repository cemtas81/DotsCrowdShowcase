using NUnit.Framework;
using Unity.Mathematics;

namespace DotsCrowd.Tests
{
    public class ContextSteeringTests
    {
        static readonly float2 Forward = new float2(0f, 1f);
        static readonly float2 Right = new float2(1f, 0f);

        [Test]
        public void NoDanger_HeadsStraightToTarget()
        {
            var decision = ContextSteering.Choose(default, Forward, 1f);

            Assert.IsTrue(decision.HasDirection);
            Assert.IsFalse(decision.Blocked);
            Assert.Greater(math.dot(decision.Direction, Forward), 0.999f);
        }

        [Test]
        public void NeighbourAhead_BlocksTheForwardConeAndSidesteps()
        {
            var danger = default(DangerMap);
            ContextSteering.AddDanger(ref danger, Forward);

            var decision = ContextSteering.Choose(danger, Forward, 1f);

            // N, NE and NW are inside the 60 degree cone; E and W are the best free directions.
            Assert.IsTrue(decision.HasDirection);
            Assert.AreEqual(0f, math.dot(decision.Direction, Forward), 1e-4f);
        }

        [Test]
        public void NeighbourToTheSide_LeavesForwardFree()
        {
            var danger = default(DangerMap);
            ContextSteering.AddDanger(ref danger, Right);

            var decision = ContextSteering.Choose(danger, Forward, 1f);

            Assert.Greater(math.dot(decision.Direction, Forward), 0.999f);
        }

        [Test]
        public void InsideStopDistance_Brakes()
        {
            var decision = ContextSteering.Choose(default, Forward, 0f);

            Assert.IsFalse(decision.HasDirection);
            Assert.IsFalse(decision.Blocked);
        }

        [Test]
        public void Surrounded_IsBlockedAndSaturated()
        {
            var danger = default(DangerMap);
            for (int i = 0; i < ContextSteering.DirectionCount; i++)
                ContextSteering.AddDanger(ref danger, ContextSteering.Direction(i));

            var decision = ContextSteering.Choose(danger, Forward, 1f);

            Assert.IsTrue(ContextSteering.IsSaturated(danger));
            Assert.IsTrue(decision.Blocked);
            Assert.IsFalse(decision.HasDirection);
        }
    }
}
