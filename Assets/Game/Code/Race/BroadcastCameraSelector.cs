using System.Collections.Generic;

namespace F1Game.Race
{
    /// <summary>
    /// Engine-free TV-broadcast camera picker: given a ring of fixed trackside
    /// camera positions and the position of the car being covered, decides which
    /// trackside camera should be live. A camera is a candidate only while the car
    /// is within a usable distance band; among candidates the one whose distance is
    /// closest to an ideal framing distance wins. A hysteresis margin keeps the
    /// current camera live until another is meaningfully better, so the cut rate
    /// stays TV-like instead of flickering between two near-equal cameras. Pure
    /// decision logic (no transforms, no engine types) so it is unit-testable; the
    /// MonoBehaviour layer owns positions, aiming and the actual cut.
    /// </summary>
    public static class BroadcastCameraSelector
    {
        public readonly struct CameraPoint
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Z;

            public CameraPoint(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        public const float DefaultMinRange = 8f;
        public const float DefaultMaxRange = 170f;
        public const float DefaultIdealRange = 60f;
        public const float DefaultSwitchMargin = 14f;

        /// <summary>
        /// The index of the trackside camera that should be live for a car at
        /// (<paramref name="targetX"/>, <paramref name="targetY"/>,
        /// <paramref name="targetZ"/>). Cameras outside [<paramref name="minRange"/>,
        /// <paramref name="maxRange"/>] are ignored; the candidate whose distance is
        /// nearest <paramref name="idealRange"/> wins, but the current camera
        /// (<paramref name="currentIndex"/>) is retained while it stays a candidate
        /// unless a rival beats it by more than <paramref name="switchMargin"/>.
        /// Returns -1 only when there are no cameras at all; when none is in range it
        /// holds the current camera (or -1 if that is invalid too).
        /// </summary>
        public static int SelectCamera(
            IReadOnlyList<CameraPoint> cameras,
            float targetX, float targetY, float targetZ,
            int currentIndex,
            float minRange = DefaultMinRange,
            float maxRange = DefaultMaxRange,
            float idealRange = DefaultIdealRange,
            float switchMargin = DefaultSwitchMargin)
        {
            if (cameras == null || cameras.Count == 0)
            {
                return -1;
            }

            int best = -1;
            float bestCost = float.MaxValue;
            for (int i = 0; i < cameras.Count; i++)
            {
                float d = Distance(cameras[i], targetX, targetY, targetZ);
                if (d < minRange || d > maxRange)
                {
                    continue;
                }

                float cost = Abs(d - idealRange);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }

            if (best < 0)
            {
                // Nothing in range: keep the current camera if it is a real index,
                // otherwise report "no camera" so the caller can fall back.
                return (currentIndex >= 0 && currentIndex < cameras.Count) ? currentIndex : -1;
            }

            // Hysteresis: hold the current camera while it is still a candidate and
            // the new best is not better than it by more than the switch margin.
            if (currentIndex >= 0 && currentIndex < cameras.Count && currentIndex != best)
            {
                float dc = Distance(cameras[currentIndex], targetX, targetY, targetZ);
                if (dc >= minRange && dc <= maxRange)
                {
                    float currentCost = Abs(dc - idealRange);
                    if (currentCost - bestCost <= switchMargin)
                    {
                        return currentIndex;
                    }
                }
            }

            return best;
        }

        static float Distance(CameraPoint c, float x, float y, float z)
        {
            float dx = c.X - x;
            float dy = c.Y - y;
            float dz = c.Z - z;
            return Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static float Abs(float v) => v < 0f ? -v : v;

        static float Sqrt(float v) => (float)System.Math.Sqrt(v);
    }
}
