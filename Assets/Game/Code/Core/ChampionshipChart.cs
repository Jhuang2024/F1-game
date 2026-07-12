using System.Collections.Generic;

namespace F1Game.Core
{
    /// <summary>
    /// Engine-free championship-graph geometry: turns a series' cumulative points and
    /// the season's round count into normalized [0,1] chart coordinates, plus the
    /// y-axis maximum and evenly-spaced tick values. Pure maths with no engine or
    /// career types, so it is unit-testable and handles the awkward cases (no rounds,
    /// a single round, all-zero points) without dividing by zero. The Unity chart view
    /// maps these normalized points into its plot rect.
    /// </summary>
    public static class ChampionshipChart
    {
        public readonly struct Point
        {
            public readonly float X;
            public readonly float Y;

            public Point(float x, float y)
            {
                X = x;
                Y = y;
            }
        }

        /// <summary>Normalized x for a round index: 0 at the first round, 1 at the last; 0 for a lone round.</summary>
        public static float NormalizeX(int roundIndex, int roundCount)
        {
            if (roundCount <= 1)
            {
                return 0f;
            }

            int i = roundIndex < 0 ? 0 : (roundIndex >= roundCount ? roundCount - 1 : roundIndex);
            return i / (float)(roundCount - 1);
        }

        /// <summary>Normalized y for a points value against the axis maximum (clamped to [0,1]).</summary>
        public static float NormalizeY(int points, int axisMax)
        {
            if (axisMax <= 0)
            {
                return 0f;
            }

            float y = points / (float)axisMax;
            return y < 0f ? 0f : (y > 1f ? 1f : y);
        }

        /// <summary>
        /// The y-axis maximum: the largest cumulative points across all series rounded
        /// UP to a magnitude-appropriate "nice" step so the top line sits below the
        /// frame edge. Always at least 1 (so nothing divides by zero on an empty board).
        /// </summary>
        public static int AxisMax(int maxPointsAcrossSeries)
        {
            if (maxPointsAcrossSeries <= 0)
            {
                return 1;
            }

            int step = NiceStep(maxPointsAcrossSeries);
            return CeilToMultiple(maxPointsAcrossSeries, step);
        }

        /// <summary>Evenly-spaced tick values 0..axisMax inclusive (<paramref name="tickCount"/>+1 values).</summary>
        public static List<int> AxisTicks(int axisMax, int tickCount)
        {
            var ticks = new List<int>();
            if (tickCount < 1)
            {
                ticks.Add(0);
                ticks.Add(axisMax < 0 ? 0 : axisMax);
                return ticks;
            }

            for (int i = 0; i <= tickCount; i++)
            {
                ticks.Add((int)System.Math.Round((double)axisMax * i / tickCount));
            }

            return ticks;
        }

        /// <summary>
        /// Normalize a whole series' cumulative points to plot points. The list is
        /// index-aligned with the season's rounds; an empty series yields no points.
        /// </summary>
        public static List<Point> NormalizeSeries(IReadOnlyList<int> cumulativePoints, int roundCount, int axisMax)
        {
            var points = new List<Point>();
            if (cumulativePoints == null)
            {
                return points;
            }

            for (int i = 0; i < cumulativePoints.Count; i++)
            {
                points.Add(new Point(NormalizeX(i, roundCount), NormalizeY(cumulativePoints[i], axisMax)));
            }

            return points;
        }

        static int NiceStep(int max)
        {
            if (max <= 50)
            {
                return 10;
            }

            if (max <= 150)
            {
                return 25;
            }

            if (max <= 400)
            {
                return 50;
            }

            return 100;
        }

        static int CeilToMultiple(int value, int step)
        {
            if (step <= 0)
            {
                return value;
            }

            int remainder = value % step;
            return remainder == 0 ? value : value + (step - remainder);
        }
    }
}
