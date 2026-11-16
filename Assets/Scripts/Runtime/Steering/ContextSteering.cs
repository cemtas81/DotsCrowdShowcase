using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace DotsCrowd
{
    /// <summary>
    /// 8-direction danger map (N, NE, E, SE, S, SW, W, NW) packed into two float4 lanes, so a neighbour
    /// updates every direction with a couple of SIMD compares.
    /// </summary>
    public struct DangerMap
    {
        public float4 D0;
        public float4 D1;
    }

    public struct SteeringDecision
    {
        public float2 Direction;
        public bool HasDirection;

        /// <summary>True when all eight directions are blocked.</summary>
        public bool Blocked;
    }

    /// <summary>
    /// Context steering: any neighbour inside the decision radius blocks every direction within 60 degrees
    /// of it; the target scores the free directions with (dot + 0.9) * interest and the best one wins.
    /// Pure math, shared by the jobs and the tests.
    /// </summary>
    public static class ContextSteering
    {
        public const int DirectionCount = 8;
        public const float ConeCos = 0.5f;
        public const float InterestBias = 0.9f;
        public const float BlockThreshold = 0.1f;
        public const float MinScore = 0.1f;

        // Unit directions i * 45 degrees, clockwise from +Z.
        static float4 X0 => new float4(0f, 0.70710678f, 1f, 0.70710678f);
        static float4 Z0 => new float4(1f, 0.70710678f, 0f, -0.70710678f);
        static float4 X1 => new float4(0f, -0.70710678f, -1f, -0.70710678f);
        static float4 Z1 => new float4(-1f, -0.70710678f, 0f, 0.70710678f);

        /// <summary>Blocks every direction within 60 degrees of <paramref name="direction"/> (unit, XZ).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddDanger(ref DangerMap map, float2 direction)
        {
            map.D0 = math.select(map.D0, 1f, X0 * direction.x + Z0 * direction.y > ConeCos);
            map.D1 = math.select(map.D1, 1f, X1 * direction.x + Z1 * direction.y > ConeCos);
        }

        /// <summary>True once every direction is blocked; further neighbours cannot change the decision.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSaturated(in DangerMap map)
        {
            return math.all(map.D0 > BlockThreshold) && math.all(map.D1 > BlockThreshold);
        }

        /// <summary>
        /// Picks the best free direction toward <paramref name="desired"/> (unit). An interest weight of zero
        /// (inside the stop distance) yields no direction, which makes the agent brake.
        /// </summary>
        public static SteeringDecision Choose(in DangerMap map, float2 desired, float interestWeight)
        {
            bool4 blocked0 = map.D0 > BlockThreshold;
            bool4 blocked1 = map.D1 > BlockThreshold;
            float4 score0 = math.select((X0 * desired.x + Z0 * desired.y + InterestBias) * interestWeight, -1f, blocked0);
            float4 score1 = math.select((X1 * desired.x + Z1 * desired.y + InterestBias) * interestWeight, -1f, blocked1);

            var decision = new SteeringDecision
            {
                Blocked = math.all(blocked0) && math.all(blocked1),
            };

            float best = math.cmax(math.max(score0, score1));
            if (best <= MinScore)
                return decision;

            // First direction with the best score wins, matching a sequential N..NW evaluation.
            for (int i = 0; i < DirectionCount; i++)
            {
                float score = i < 4 ? score0[i] : score1[i - 4];
                if (score == best)
                {
                    decision.Direction = Direction(i);
                    decision.HasDirection = true;
                    break;
                }
            }

            return decision;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 Direction(int index)
        {
            math.sincos(index * (2f * math.PI / DirectionCount), out float sin, out float cos);
            return new float2(sin, cos);
        }
    }
}
