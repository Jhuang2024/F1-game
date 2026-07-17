using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Track
{
    /// <summary>
    /// Resamples an authored spline (Catmull-Rom through the control points) into
    /// an evenly-spaced centerline with per-sample tangent, normal, width and
    /// camber, plus cumulative distance. Everything the authored-track runtime
    /// queries (progress, half-width, surface, pit, DRS, sectors) reads from this
    /// deterministic sampling, so a track builds identically every run.
    /// </summary>
    public sealed class TrackSplineSampler
    {
        public struct Sample
        {
            public Vector3 Position;
            public Vector3 Tangent;   // unit, forward along the lap
            public Vector3 Normal;    // unit, lateral (right of travel, on the ground plane)
            public float Width;       // full road width
            public float CamberDeg;
            public float Distance;    // cumulative from start/finish
        }

        readonly List<Sample> samples = new List<Sample>();

        public IReadOnlyList<Sample> Samples => samples;
        public float Length { get; private set; }
        public bool ClosedLoop { get; private set; }

        // Default resample spacing 3m (was 6m): halves the chord length the
        // road ribbon is built from, which is what keeps authored corner arcs
        // round instead of visibly faceted. Callers that need the old density
        // still pass spacing explicitly.
        public void Build(IReadOnlyList<TrackDefinitionAsset.SplinePoint> control, bool closedLoop, float spacing = 3f)
        {
            samples.Clear();
            Length = 0f;
            ClosedLoop = closedLoop;
            if (control == null || control.Count < 4)
            {
                return;
            }

            int count = control.Count;
            // Dense pass first (fixed subdivisions per segment), then arc-length
            // resample to the requested even spacing. 32 subdivisions (was 12)
            // keeps the dense pass from faceting tight corners before the
            // resample ever sees them - with typical 40-60-point control loops
            // this is still only ~2k transient samples.
            var dense = new List<Sample>();
            int segments = closedLoop ? count : count - 1;
            const int perSegment = 32;
            float cumulative = 0f;
            Vector3 previous = control[0].position;

            for (int seg = 0; seg < segments; seg++)
            {
                for (int step = 0; step < perSegment; step++)
                {
                    float t = step / (float)perSegment;
                    Vector3 p0 = control[Wrap(seg - 1, count, closedLoop)].position;
                    Vector3 p1 = control[Wrap(seg, count, closedLoop)].position;
                    Vector3 p2 = control[Wrap(seg + 1, count, closedLoop)].position;
                    Vector3 p3 = control[Wrap(seg + 2, count, closedLoop)].position;
                    Vector3 pos = CatmullRom(p0, p1, p2, p3, t);

                    if (dense.Count > 0)
                    {
                        cumulative += Vector3.Distance(previous, pos);
                    }

                    previous = pos;

                    float w0 = control[Wrap(seg, count, closedLoop)].width;
                    float w1 = control[Wrap(seg + 1, count, closedLoop)].width;
                    float c0 = control[Wrap(seg, count, closedLoop)].camberDegrees;
                    float c1 = control[Wrap(seg + 1, count, closedLoop)].camberDegrees;

                    dense.Add(new Sample
                    {
                        Position = pos,
                        Width = Mathf.Lerp(w0, w1, t),
                        CamberDeg = Mathf.Lerp(c0, c1, t),
                        Distance = cumulative,
                    });
                }
            }

            Length = cumulative;
            if (dense.Count < 2)
            {
                return;
            }

            // Even-spacing resample with tangent/normal.
            float target = 0f;
            int cursor = 0;
            while (target <= Length)
            {
                while (cursor < dense.Count - 1 && dense[cursor + 1].Distance < target)
                {
                    cursor++;
                }

                Sample a = dense[cursor];
                Sample b = dense[Mathf.Min(cursor + 1, dense.Count - 1)];
                float span = Mathf.Max(0.0001f, b.Distance - a.Distance);
                float f = Mathf.Clamp01((target - a.Distance) / span);

                Vector3 pos = Vector3.Lerp(a.Position, b.Position, f);
                Vector3 tangent = (b.Position - a.Position);
                tangent.y = 0f;
                tangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;
                Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;

                samples.Add(new Sample
                {
                    Position = pos,
                    Tangent = tangent,
                    Normal = normal,
                    Width = Mathf.Lerp(a.Width, b.Width, f),
                    CamberDeg = Mathf.Lerp(a.CamberDeg, b.CamberDeg, f),
                    Distance = target,
                });

                target += spacing;
            }
        }

        public Sample AtDistance(float distance)
        {
            if (samples.Count == 0)
            {
                return default;
            }

            distance = WrapDistance(distance);
            int index = Mathf.Clamp(Mathf.RoundToInt(distance / Mathf.Max(0.0001f, Length) * (samples.Count - 1)), 0, samples.Count - 1);
            return samples[index];
        }

        /// <summary>Nearest centerline distance to a world position (coarse scan).</summary>
        public float NearestDistance(Vector3 worldPos)
        {
            float best = 0f;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < samples.Count; i++)
            {
                float sqr = (samples[i].Position - worldPos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = samples[i].Distance;
                }
            }

            return best;
        }

        public float WrapDistance(float distance)
        {
            if (Length <= 0f)
            {
                return 0f;
            }

            if (!ClosedLoop)
            {
                return Mathf.Clamp(distance, 0f, Length);
            }

            distance %= Length;
            if (distance < 0f)
            {
                distance += Length;
            }

            return distance;
        }

        static int Wrap(int index, int count, bool closed)
        {
            if (closed)
            {
                return ((index % count) + count) % count;
            }

            return Mathf.Clamp(index, 0, count - 1);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
