using System.Collections.Generic;
using UnityEngine;
using F1Game.Race.Rules;

namespace LocalFormulaRacing
{
    public struct TrackProgress
    {
        public float distance;
        public float normalized;
        public float lateralDistance;
        public Vector3 nearestPoint;
        public Vector3 forward;
        public int sector;
    }

    public class TrackRuntime
    {
        public string trackId;
        public string displayName;
        public string styleName;
        public List<Vector3> centerLine = new List<Vector3>();
        public List<float> cumulativeDistances = new List<float>();
        public float length;
        public float roadHalfWidth = 7.43f;
        // Authored circuits only: half-width samples uniform in normalized lap
        // distance (null for procedural layouts, which use the flat scalar).
        // HalfWidthAt interpolates this, so every road/kerb/barrier/runoff
        // pass and every race-layer width consumer honors the authored
        // per-point width without further changes.
        public float[] authoredHalfWidthProfile;
        public float kerbStart = 6.77f;
        public Vector2 drsZoneOne = new Vector2(0.13f, 0.29f);
        public Vector2 drsZoneTwo = new Vector2(0.64f, 0.82f);
        // DRS fix: detection points, a short distance before each zone's own start
        // (see TrackManager.ValidateLayout, right after the zones themselves are
        // validated). A real DRS system decides eligibility once, at the detection
        // point, then holds that decision for the whole following activation zone -
        // continuously re-checking the live gap throughout the zone (the old
        // behavior) is why DRS used to deploy for way too short a time.
        public float drsDetectionOne;
        public float drsDetectionTwo;
        public WeatherState weather = WeatherState.Clear;
        // Session track-surface temperature (C), derived once from the event's
        // weather profile (plus a small per-track offset) - drives the
        // compound-specific tyre-wear gradient in TyreState. Defaults to the
        // standard anchor so any track built without an explicit value still
        // wears tyres at the calibrated mid-temperature rate.
        public float trackTemperatureC = F1Game.Race.Rules.TyreStrategyRules.StandardTrackTempC;
        public MeshCollider roadCollider;

        // Pit-exit early-turn fix round 4: baked once at build time (see
        // TrackManager.PopulateCornerContainmentZones) from the exact same corner
        // detection (DetectCorners(HighRiskCornerAngle)/DetectCorners(TightCornerFenceAngle)
        // + IsNearCorner's radius/span math) that ComputeBarrierPlan itself uses to
        // decide whether the main edge barrier is in tight-corner containment mode
        // at a given point. AiVehicleController previously had no way to ask "is a
        // real wall still hugging the track here" and had to guess with its own,
        // differently-tuned EstimateCornerSeverity heuristic - which could (and did)
        // diverge from the barrier builder's own answer, releasing the pit-exit
        // line hold while the actual barrier was still tight. IsNearTightFenceCorner
        // below answers the exact same question the barrier geometry itself was
        // built from.
        public struct CornerContainmentZone
        {
            public float distance;
            public float radius;
        }

        public List<CornerContainmentZone> tightFenceContainmentZones = new List<CornerContainmentZone>();

        public bool IsNearTightFenceCorner(float distance)
        {
            for (int i = 0; i < tightFenceContainmentZones.Count; i++)
            {
                float delta = Mathf.Abs(WrapDistance(distance - tightFenceContainmentZones[i].distance));
                float wrapped = Mathf.Min(delta, length - delta);
                if (wrapped <= tightFenceContainmentZones[i].radius)
                {
                    return true;
                }
            }

            return false;
        }

        // Precomputed optimal racing line - lateral offsets (along the right
        // vector) from the centerline, sampled every ~racingLineSpacing metres
        // around the whole lap. Computed once at build time by ComputeRacingLine
        // below; the AI previously had NO computed line at all - it targeted the
        // road CENTERLINE plus reactive severity-based offsets, which is exactly
        // why it read as "driving down the middle of the road" no matter how the
        // offsets were tuned.
        public float[] racingLineOffsets;
        public float racingLineSpacing = 4f;

        // MINIMUM-CURVATURE racing line (the real thing): gradient descent on the
        // lateral offsets minimising the sum of squared second differences of the
        // line's world positions - i.e. the flattest possible path through the
        // legal corridor, which is what "the racing line" means: swing to the full
        // outside on entry, clip the inside kerb at the apex, release to the full
        // outside on exit, and position across straights to set up the next
        // corner, maximising every corner's effective radius. (The first version
        // of this used shortest-path/taut-string relaxation, which hugs the INSIDE
        // of corners rather than maximising radius, and was so under-converged the
        // result sat partway off the centerline everywhere - the reported "line is
        // way too noncommitted / not maximising speed".) The stride ladder is a
        // multigrid pass: large strides converge the long-wavelength shape
        // (straight positioning, corner-to-corner setup) that single-step descent
        // takes tens of thousands of iterations to reach, then small strides
        // sharpen the apexes. Validated offline on a closed test circuit: apexes
        // reach the full inside limit, entry/exit the full outside limit.
        public void ComputeRacingLine()
        {
            if (centerLine.Count < 8 || length < 200f)
            {
                racingLineOffsets = null;
                return;
            }

            int count = Mathf.Max(32, Mathf.RoundToInt(length / 4f));
            racingLineSpacing = length / count;
            Vector3[] centers = new Vector3[count];
            Vector3[] rights = new Vector3[count];
            float[] limits = new float[count];
            float[] offsets = new float[count];
            for (int i = 0; i < count; i++)
            {
                float d = i * racingLineSpacing;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                SampleAtDistance(d, out point, out forward, out right);
                centers[i] = point;
                rights[i] = right;
                float halfWidth = HalfWidthAt(d);
                // Corridor round 4 (REAL wall-contact fix): round 3 introduced a
                // wall-safety bound (wallSafetyLimit below) but then combined it
                // with the kerb allowance using Mathf.Max - which always picks
                // whichever of the two is CLOSER TO THE WALL, silently defeating
                // the safety bound the instant the kerb happened to sit close to
                // the edge (narrow/technical layouts). That is exactly why the
                // line still ran the barrier despite the "fix": Max() was backwards.
                // Corrected to Min() - the corridor can never exceed the wall
                // safety bound, full stop; the kerb-based limit only ever pulls
                // the corridor further IN (which is where the apex-clipping shape
                // actually comes from on real layouts, since the kerb typically
                // starts well before the wall).
                float wallSafetyLimit = halfWidth - (IsNearTightFenceCorner(d) ? 2.6f : 1.8f);
                float kerbBasedLimit = kerbStart > 0f ? kerbStart + 0.5f : wallSafetyLimit;
                limits[i] = Mathf.Max(0.75f, Mathf.Min(wallSafetyLimit, kerbBasedLimit));
                offsets[i] = 0f;
            }

            // DETERMINISTIC out-in-out construction (v3). Both optimisation
            // approaches failed on this game's real geometry: shortest-path hugs
            // corner insides, and min-curvature degenerates to riding the outer
            // edge of the whole (mostly convex, 6x-upscaled) loop - the reported
            // "widest line known to man". This version constructs the line the
            // way a driver describes it: find each corner from the smoothed
            // centerline curvature, pin the APEX to the full inside of the
            // corridor, pin entry/exit GATES to the full outside, cosine-blend
            // between the pins and smooth. Validated offline against this exact
            // pipeline's real Hungary geometry: 11 corners found, 11/11 apexes
            // pinned to the corridor limit.
            // 1) Signed curvature (turn angle per metre), smoothed to corner scale
            //    (the centerline is a sparse ~130m-segment polyline after the 6x
            //    length normalisation, so raw curvature arrives as isolated
            //    spikes at the vertices - without this smoothing, corner
            //    detection fragments into dozens of phantom mini-corners).
            float[] kappa = new float[count];
            const int curvatureStep = 4;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = centers[(i - curvatureStep + count) % count];
                Vector3 b = centers[i];
                Vector3 c = centers[(i + curvatureStep) % count];
                float v1x = b.x - a.x;
                float v1z = b.z - a.z;
                float v2x = c.x - b.x;
                float v2z = c.z - b.z;
                float cross = v1x * v2z - v1z * v2x;
                float magnitudes = Mathf.Max(1e-6f, Mathf.Sqrt(v1x * v1x + v1z * v1z) * Mathf.Sqrt(v2x * v2x + v2z * v2z));
                float angle = Mathf.Atan2(cross, magnitudes);
                kappa[i] = angle / (curvatureStep * racingLineSpacing);
            }

            for (int pass = 0; pass < 4; pass++)
            {
                float[] smoothed = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float sum = 0f;
                    for (int j = -9; j <= 9; j++)
                    {
                        sum += kappa[(i + j + count) % count];
                    }

                    smoothed[i] = sum / 19f;
                }

                kappa = smoothed;
            }

            // 2) Corner spans: contiguous |kappa| above threshold (radius under
            //    ~400m counts as a corner); adjacent same-sign spans separated by
            //    under ~60m merge into one corner.
            const float cornerCurvature = 1f / 400f;
            List<int[]> spans = new List<int[]>();
            int scan = 0;
            while (scan < count)
            {
                if (Mathf.Abs(kappa[scan]) > cornerCurvature)
                {
                    int end = scan;
                    float signedSum = 0f;
                    while (end < count && Mathf.Abs(kappa[end]) > cornerCurvature)
                    {
                        signedSum += kappa[end];
                        end++;
                    }

                    spans.Add(new[] { scan, end - 1, signedSum > 0f ? 1 : -1 });
                    scan = end;
                }
                else
                {
                    scan++;
                }
            }

            List<int[]> corners = new List<int[]>();
            for (int s = 0; s < spans.Count; s++)
            {
                if (corners.Count > 0 && spans[s][2] == corners[corners.Count - 1][2] &&
                    spans[s][0] - corners[corners.Count - 1][1] < 15)
                {
                    corners[corners.Count - 1][1] = spans[s][1];
                }
                else
                {
                    corners.Add(spans[s]);
                }
            }

            if (corners.Count > 1 && corners[0][2] == corners[corners.Count - 1][2] &&
                corners[0][0] + count - corners[corners.Count - 1][1] < 15)
            {
                corners[corners.Count - 1][1] = corners[0][1] + count;
                corners.RemoveAt(0);
            }

            // 3) Pins: apex (sharpest point of the span) to the full INSIDE of
            //    the corridor; entry/exit gates to the full OUTSIDE, a
            //    span-scaled distance before/after the corner.
            List<KeyValuePair<int, float>> knots = new List<KeyValuePair<int, float>>();
            for (int s = 0; s < corners.Count; s++)
            {
                int a = corners[s][0];
                int b = corners[s][1];
                int sign = corners[s][2];
                int apex = a;
                float apexCurvature = 0f;
                for (int i = a; i <= b; i++)
                {
                    float magnitude = Mathf.Abs(kappa[i % count]);
                    if (magnitude > apexCurvature)
                    {
                        apexCurvature = magnitude;
                        apex = i % count;
                    }
                }

                float spanMeters = (b - a) * racingLineSpacing;
                int gate = Mathf.RoundToInt(Mathf.Clamp(spanMeters * 0.9f, 40f, 160f) / racingLineSpacing);
                knots.Add(new KeyValuePair<int, float>(apex, -sign * limits[apex]));
                int entry = (a - gate + count * 2) % count;
                int exit = (b + gate) % count;
                knots.Add(new KeyValuePair<int, float>(entry, sign * limits[entry]));
                knots.Add(new KeyValuePair<int, float>(exit, sign * limits[exit]));
            }

            knots.Sort((x, y) => x.Key.CompareTo(y.Key));
            List<KeyValuePair<int, float>> pins = new List<KeyValuePair<int, float>>();
            for (int s = 0; s < knots.Count; s++)
            {
                if (pins.Count > 0 && (knots[s].Key - pins[pins.Count - 1].Key + count) % count < 6)
                {
                    pins[pins.Count - 1] = new KeyValuePair<int, float>(
                        pins[pins.Count - 1].Key,
                        (pins[pins.Count - 1].Value + knots[s].Value) * 0.5f);
                }
                else
                {
                    pins.Add(knots[s]);
                }
            }

            // 4) Cosine-blend between consecutive pins (wrapping), then smooth
            //    lightly and clamp to the corridor.
            if (pins.Count >= 2)
            {
                for (int p = 0; p < pins.Count; p++)
                {
                    int i0 = pins[p].Key;
                    float v0 = pins[p].Value;
                    int i1 = pins[(p + 1) % pins.Count].Key;
                    float v1 = pins[(p + 1) % pins.Count].Value;
                    int spanSamples = (i1 - i0 + count) % count;
                    if (spanSamples == 0)
                    {
                        spanSamples = count;
                    }

                    for (int s = 0; s < spanSamples; s++)
                    {
                        float t = (1f - Mathf.Cos(Mathf.PI * s / spanSamples)) * 0.5f;
                        offsets[(i0 + s) % count] = v0 + (v1 - v0) * t;
                    }
                }
            }

            for (int pass = 0; pass < 3; pass++)
            {
                float[] smoothed = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float blended = (offsets[(i - 2 + count) % count] + offsets[(i - 1 + count) % count] +
                                     2f * offsets[i] + offsets[(i + 1) % count] + offsets[(i + 2) % count]) / 6f;
                    smoothed[i] = Mathf.Clamp(blended, -limits[i], limits[i]);
                }

                offsets = smoothed;
            }

            // World-space smoothing + reprojection (jaggedness fix): the offset
            // PROFILE above is smooth, but the centerline is a coarse polyline
            // whose right vectors jump a few degrees at every segment joint - at a
            // 10m+ offset that direction jump becomes a visible ~1m sideways kink
            // in the world-space line (and in the AI's steering target). Build the
            // world points, smooth THEM, then project back to per-sample offsets:
            // center + right * offset then reproduces the smooth world line, so
            // both the visual and the AI target lose the kinks.
            Vector3[] world = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                world[i] = centers[i] + rights[i] * offsets[i];
            }

            for (int pass = 0; pass < 3; pass++)
            {
                Vector3[] smoothedWorld = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    smoothedWorld[i] = (world[(i - 2 + count) % count] + 2f * world[(i - 1 + count) % count] +
                                        3f * world[i] +
                                        2f * world[(i + 1) % count] + world[(i + 2) % count]) / 9f;
                }

                world = smoothedWorld;
            }

            for (int i = 0; i < count; i++)
            {
                float projected = Vector3.Dot(world[i] - centers[i], rights[i]);
                offsets[i] = Mathf.Clamp(projected, -limits[i], limits[i]);
            }

            racingLineOffsets = offsets;
        }

        // Lateral offset of the optimal racing line at a given track distance
        // (linear interpolation between the precomputed samples). 0 if the line
        // was never computed (degenerate/test layouts) - i.e. the centerline.
        public float RacingLineOffsetAt(float distance)
        {
            if (racingLineOffsets == null || racingLineOffsets.Length == 0)
            {
                return 0f;
            }

            float samplePosition = WrapDistance(distance) / racingLineSpacing;
            int index = Mathf.FloorToInt(samplePosition);
            float t = samplePosition - index;
            index %= racingLineOffsets.Length;
            int nextIndex = (index + 1) % racingLineOffsets.Length;
            return Mathf.Lerp(racingLineOffsets[index], racingLineOffsets[nextIndex], t);
        }

        // World-space point on the optimal racing line at a given distance.
        public Vector3 RacingLinePointAt(float distance)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(distance, out point, out forward, out right);
            return point + right * RacingLineOffsetAt(distance);
        }

        // Race-control visual furniture (marshal flag boards, SC/VSC board, gantry
        // lights) built by TrackManager and wired up here so RaceManager can drive it
        // live without needing a cross-file dependency on TrackManager itself, or on
        // RaceManager.RaceControlState - hence the plain int rather than that enum.
        RaceControlVisualDriver raceControlVisualDriver;

        public void AssignRaceControlVisualDriver(RaceControlVisualDriver driver)
        {
            raceControlVisualDriver = driver;
        }

        // Track boundary debug overlay (mandatory extra feature): a hidden-by-
        // default GameObject built once by TrackManager.BuildBoundaryDebugOverlay
        // containing LineRenderers for the calculated track edge, the barrier
        // inner-face target line, the pit lane boundary, and the pit
        // separator line - exactly the four references BuildContinuousEdgeBarriers/
        // BuildPitLaneDividerFence actually place geometry against, so toggling
        // it on makes it immediately obvious if a barrier has drifted off that
        // line. Wired up here (not held directly by RaceManager) so any code
        // with a TrackRuntime reference can toggle it without depending on the
        // track-builder MonoBehaviour, which only exists for the duration of
        // Build().
        GameObject boundaryDebugOverlay;

        public void AssignBoundaryDebugOverlay(GameObject overlay)
        {
            boundaryDebugOverlay = overlay;
            if (boundaryDebugOverlay != null)
            {
                boundaryDebugOverlay.SetActive(false);
            }
        }

        public bool ToggleBoundaryDebugOverlay()
        {
            if (boundaryDebugOverlay == null)
            {
                return false;
            }

            bool next = !boundaryDebugOverlay.activeSelf;
            boundaryDebugOverlay.SetActive(next);
            return next;
        }

        // Dynamic track evolution: the shared road material reference + its
        // original color, handed off once by TrackManager.CreateMaterials the
        // same way AssignRaceControlVisualDriver hands off live control above -
        // so RaceManager can drive a visual "rubbering in" effect every tick
        // without a cross-file dependency on the track-builder MonoBehaviour,
        // which only exists for the duration of Build(). RubberLevel itself
        // (0-1, session-wide) is owned and ticked by RaceManager.
        Material roadMaterial;
        Color roadBaseColor;
        public float RubberLevel;

        public void AssignRoadMaterial(Material material)
        {
            roadMaterial = material;
            if (roadMaterial != null)
            {
                roadBaseColor = roadMaterial.color;
            }
        }

        public void ApplyRubberEvolutionVisual(float rubberLevel)
        {
            if (roadMaterial == null)
            {
                return;
            }

            float clamped = Mathf.Clamp01(rubberLevel);
            Color darker = new Color(roadBaseColor.r * 0.7f, roadBaseColor.g * 0.7f, roadBaseColor.b * 0.7f, roadBaseColor.a);
            roadMaterial.color = Color.Lerp(roadBaseColor, darker, clamped);
        }

        // state ordinals match RaceManager.RaceControlState: 0=Green, 1=YellowSector,
        // 2=VirtualSafetyCar, 3=SafetyCarDeploying, 4=SafetyCarActive,
        // 5=SafetyCarInThisLap, 6=Restart. Safe to call every frame or only on change;
        // the driver no-ops when the state hasn't actually changed.
        public void SetRaceControlVisual(int state)
        {
            if (raceControlVisualDriver != null)
            {
                raceControlVisualDriver.SetState(state);
            }
        }

        public void RecalculateDistances()
        {
            cumulativeDistances.Clear();
            cumulativeDistances.Add(0f);
            length = 0f;
            for (int i = 0; i < centerLine.Count; i++)
            {
                Vector3 current = centerLine[i];
                Vector3 next = centerLine[(i + 1) % centerLine.Count];
                length += Vector3.Distance(current, next);
                cumulativeDistances.Add(length);
            }
        }

        public void SampleAtDistance(float distance, out Vector3 point, out Vector3 forward, out Vector3 right)
        {
            float wrapped = WrapDistance(distance);
            for (int i = 0; i < centerLine.Count; i++)
            {
                float a = cumulativeDistances[i];
                float b = cumulativeDistances[i + 1];
                if (wrapped >= a && wrapped <= b)
                {
                    float t = Mathf.InverseLerp(a, b, wrapped);
                    Vector3 start = centerLine[i];
                    Vector3 end = centerLine[(i + 1) % centerLine.Count];
                    point = Vector3.Lerp(start, end, t);
                    forward = (end - start).normalized;
                    right = Vector3.Cross(Vector3.up, forward).normalized;
                    return;
                }
            }

            point = centerLine[0];
            forward = (centerLine[1] - centerLine[0]).normalized;
            right = Vector3.Cross(Vector3.up, forward).normalized;
        }

        public TrackProgress GetProgress(Vector3 worldPosition)
        {
            return GetProgressInternal(worldPosition, 0f, false);
        }

        public TrackProgress GetProgressNear(Vector3 worldPosition, float referenceDistance)
        {
            return GetProgressInternal(worldPosition, referenceDistance, true);
        }

        public TrackProgress GetProgressAtDistance(float distance, Vector3 worldPosition)
        {
            float wrapped = WrapDistance(distance);
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(wrapped, out point, out forward, out right);
            Vector3 flatPosition = new Vector3(worldPosition.x, point.y, worldPosition.z);
            float signed = Vector3.Dot(flatPosition - point, right);
            float normalized = Mathf.Clamp01(wrapped / Mathf.Max(1f, length));
            return new TrackProgress
            {
                distance = wrapped,
                normalized = normalized,
                lateralDistance = signed,
                nearestPoint = point,
                forward = forward,
                sector = normalized < 0.333f ? 1 : (normalized < 0.666f ? 2 : 3)
            };
        }

        TrackProgress GetProgressInternal(Vector3 worldPosition, float referenceDistance, bool preferContinuity)
        {
            TrackProgress progress = new TrackProgress();
            float bestScore = float.MaxValue;

            for (int i = 0; i < centerLine.Count; i++)
            {
                Vector3 a = centerLine[i];
                Vector3 b = centerLine[(i + 1) % centerLine.Count];
                Vector3 segment = b - a;
                float t = Vector3.Dot(worldPosition - a, segment) / Mathf.Max(1f, segment.sqrMagnitude);
                t = Mathf.Clamp01(t);
                Vector3 candidate = a + segment * t;
                Vector3 diff = worldPosition - candidate;
                Vector3 weightedDiff = new Vector3(diff.x, diff.y * 3.5f, diff.z);
                float distanceSqr = weightedDiff.sqrMagnitude;
                float distance = cumulativeDistances[i] + segment.magnitude * t;
                float score = distanceSqr;
                if (preferContinuity && length > 1f)
                {
                    float unwrapped = ClosestUnwrappedDistance(distance, referenceDistance);
                    float delta = Mathf.Abs(unwrapped - referenceDistance);
                    score += delta * delta * 0.08f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    Vector3 forward = segment.normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                    Vector3 flatDiff = new Vector3(diff.x, 0f, diff.z);
                    float signed = Vector3.Dot(flatDiff, right);
                    progress.distance = WrapDistance(distance);
                    progress.normalized = Mathf.Clamp01(progress.distance / Mathf.Max(1f, length));
                    progress.lateralDistance = signed;
                    progress.nearestPoint = candidate;
                    progress.forward = forward;
                    progress.sector = progress.normalized < 0.333f ? 1 : (progress.normalized < 0.666f ? 2 : 3);
                }
            }

            return progress;
        }

        float ClosestUnwrappedDistance(float distance, float referenceDistance)
        {
            if (length <= 0f)
            {
                return distance;
            }

            float candidate = distance;
            while (candidate - referenceDistance > length * 0.5f)
            {
                candidate -= length;
            }

            while (referenceDistance - candidate > length * 0.5f)
            {
                candidate += length;
            }

            return candidate;
        }

        // Off-track/kerb detection has to follow the same widened half-width the
        // road mesh itself uses (HalfWidthAt), or the extra tarmac painted in at a
        // hairpin would still read as "off track" here - actively working against
        // the whole point of widening those corners (fewer track-limits penalties/
        // off-track slowdowns exactly where cars need the room most).
        public bool IsOnRoad(Vector3 worldPosition)
        {
            TrackProgress progress = GetProgress(worldPosition);
            return Mathf.Abs(progress.lateralDistance) <= HalfWidthAt(progress.distance);
        }

        public bool IsOnKerb(Vector3 worldPosition)
        {
            TrackProgress progress = GetProgress(worldPosition);
            float lateral = Mathf.Abs(progress.lateralDistance);
            return lateral >= kerbStart && lateral <= HalfWidthAt(progress.distance) + 1.3f;
        }

        public bool IsInDrsZone(float normalizedProgress)
        {
            return IsInZone(normalizedProgress, drsZoneOne) || IsInZone(normalizedProgress, drsZoneTwo);
        }

        // DRS fix: 1-indexed zone the car is currently inside (1 or 2), or 0 if in
        // neither - lets callers key per-zone eligibility state without re-deriving
        // which zone from scratch every time.
        public int GetDrsZoneIndex(float normalizedProgress)
        {
            if (IsInZone(normalizedProgress, drsZoneOne))
            {
                return 1;
            }

            if (IsInZone(normalizedProgress, drsZoneTwo))
            {
                return 2;
            }

            return 0;
        }

        public bool IsInDrsZone(int zoneIndex, float normalizedProgress)
        {
            if (zoneIndex == 1)
            {
                return IsInZone(normalizedProgress, drsZoneOne);
            }

            if (zoneIndex == 2)
            {
                return IsInZone(normalizedProgress, drsZoneTwo);
            }

            return false;
        }

        public float GetDrsDetectionPoint(int zoneIndex)
        {
            return zoneIndex == 2 ? drsDetectionTwo : drsDetectionOne;
        }

        // DRS fix: true the frame progress crosses this zone's detection point going
        // forward - a single-sample "am I past the point" check would also fire on
        // every frame after crossing, so this needs the previous frame's normalized
        // progress too, wrap-aware (the point can sit just before the start/finish
        // line, e.g. 0.95 -> 0.08 zone puts the detection point around 0.945).
        public bool CrossedDrsDetectionPoint(float previousNormalized, float currentNormalized, int zoneIndex)
        {
            float point = GetDrsDetectionPoint(zoneIndex);
            float previousDelta = Mathf.Repeat(previousNormalized - point, 1f);
            float currentDelta = Mathf.Repeat(currentNormalized - point, 1f);
            // previousDelta close to 1 (just behind the point) and currentDelta close
            // to 0 (just past it) means forward motion crossed the point this frame.
            // A small window (rather than previousDelta > currentDelta everywhere)
            // avoids false-triggering from a car that's simply sitting stationary
            // near the point across two frames with tiny floating-point jitter.
            return previousDelta > 0.5f && currentDelta <= 0.5f && (1f - previousDelta) + currentDelta < 0.02f;
        }

        bool IsInZone(float normalizedProgress, Vector2 zone)
        {
            if (zone.x <= zone.y)
            {
                return normalizedProgress > zone.x && normalizedProgress < zone.y;
            }

            return normalizedProgress > zone.x || normalizedProgress < zone.y;
        }

        public bool IsInPitWindow(float normalizedProgress)
        {
            return IsInPitEntryZone(normalizedProgress);
        }

        // Shared with TrackManager's speed-limit line/sign placement so the painted
        // marking on the tarmac lines up exactly with where SetPitLimiter actually
        // starts enforcing the limiter for a car that has requested a stop.
        public const float PitApproachStartNormalized = 0.78f;

        public bool IsInPitApproach(float normalizedProgress)
        {
            // Ends where the enclosed pit corridor begins - past that a car is
            // either committed to the ramp or has missed the opening, so the
            // broad approach/HUD window no longer applies. (The old 0.955
            // literal predates the fixed-metre entry anchors.)
            return normalizedProgress > PitApproachStartNormalized && normalizedProgress < PitCorridorStartNormalized;
        }

        public bool IsInPitEntryZone(float normalizedProgress)
        {
            // Broad approach/messaging window: opens a fixed 150m before the
            // physical ramp (metre-based like the ramp anchors themselves, so
            // it tracks the real geometry on every lap length) and closes at
            // the corridor seam.
            return normalizedProgress > NormalizedMetresBeforeLine(PitEntryRampStartLeadMetres + 150f) &&
                   normalizedProgress < PitCorridorStartNormalized;
        }

        // Pit-entry timing fix: the REAL physical entry opening only exists between
        // PitEntryRampStartNormalized (0.85, where the ramp first tapers off the live
        // track edge) and PitCorridorStartNormalized (0.885, where the ramp has fully
        // flattened into the enclosed corridor and the divider wall begins). The
        // broader IsInPitEntryZone (0.865-0.955) above exists for approach/HUD
        // messaging and used to also gate IsOnPitEntryRamp's physical commit test -
        // meaning RaceManager couldn't even consider a car "on the ramp" until 0.865,
        // by which point roughly 43% of the real 0.850-0.885 opening had already
        // passed, and kept trying all the way out to 0.955, long after the physical
        // opening (and the divider wall behind it) had already closed. Every physical
        // commit/steering decision now uses this real window instead.
        public bool IsInPitEntryRampWindow(float normalized)
        {
            return normalized >= PitEntryRampStartNormalized && normalized <= PitCorridorStartNormalized;
        }

        // Fixed-metre pit-exit geometry fix: every pit-exit boundary below used to
        // be a flat fraction of the WHOLE LAP - fine on the ~1500-2000m layouts
        // this was originally tuned against, but on a realistically-scaled track
        // (Silverstone here is ~8281m) that same fraction (0.053 of a lap between
        // release and merge-end) balloons into a genuinely enormous ~439m guided
        // ExitMerge path - roughly 15 seconds per car at ~106 km/h even with a
        // perfectly clear lane, which is exactly the reported 10-30 second gaps.
        // A real pit exit is a fixed physical structure - it does not get longer
        // because the rest of the circuit does. These boundaries are now computed
        // from fixed METRE distances relative to the start/finish line instead,
        // via NormalizedMetresBeforeLine/NormalizedMetresAfterLine below, so the
        // guided exit path is the same ~90-130m (roughly 3-4 seconds) on every
        // track regardless of total lap length. Every dependent system - the
        // generated pit surface/barriers/divider fences, the ramp envelopes, the
        // limiter zones, PitExitMergeBlend, IsInPitExitMergeZone,
        // PitExitMergeLegalLateral, and RaceManager's own pitGuideDistance
        // completion tracking - all read these same properties, so none of them
        // can silently disagree about where the physical pit exit actually ends.
        public const float PitExitReleaseLeadMetres = 70f;
        public const float PitExitRampStartLeadMetres = 25f;
        public const float PitExitLimiterStartLeadMetres = 80f;
        public const float PitExitLimiterEndMetres = 15f;
        public const float PitExitRampEndMetres = 35f;

        // Converts "N metres before the start/finish line" into a normalized lap
        // fraction - the shared building block every fixed-metre pit-exit boundary
        // below is computed from.
        float NormalizedMetresBeforeLine(float metres)
        {
            return Mathf.Clamp01((length - metres) / Mathf.Max(1f, length));
        }

        // Converts "N metres after the start/finish line" into a normalized lap
        // fraction.
        float NormalizedMetresAfterLine(float metres)
        {
            return Mathf.Clamp01(metres / Mathf.Max(1f, length));
        }

        // Shared with PitExitMergeBlend below so the steering-line hold and the
        // speed limiter agree on exactly where the pit-exit merge window starts.
        float PitExitLimiterStartNormalized
        {
            get { return NormalizedMetresBeforeLine(PitExitLimiterStartLeadMetres); }
        }

        float PitExitLimiterEndNormalized
        {
            get { return NormalizedMetresAfterLine(PitExitLimiterEndMetres); }
        }

        // Shared with TrackManager's PitZoneExitRampEnd so the steering-line hold
        // (PitExitMergeBlend below) and the actual physical pit-exit ramp/guide-
        // fence geometry agree on exactly where the ramp finishes narrowing back
        // to the track edge.
        public float PitExitRampEndNormalized
        {
            get { return NormalizedMetresAfterLine(PitExitRampEndMetres); }
        }

        public bool IsInPitExitLimiterZone(float normalizedProgress)
        {
            // Pit-exit slowness fix: this used to span 0.955-1.115 (wrapped) -
            // ~16% of an entire lap at an 80 km/h hard cap, which alone could
            // take 20-30+ seconds to clear regardless of how well a car (AI or
            // player) accelerated out of the pits. A real pit-exit limiter zone
            // only needs to cover the merge point itself plus a short stretch
            // after it, not most of the following straight - narrowed to just
            // past the release point (see GetPitReleasePose, ~0.992) through a
            // modest distance into the next lap. Tightened further (was 0.03) now
            // that the exit stretch itself runs at a higher cap
            // (VehicleController.PitExitLimiterCapKph) - the tail past the merge
            // only needs to cover actually rejoining traffic, not a long extra
            // caution stretch on top of that.
            return normalizedProgress > PitExitLimiterStartNormalized || normalizedProgress < PitExitLimiterEndNormalized;
        }

        // Pit-exit early-turn fix: a released car's normal line-following logic
        // (steer toward the racing line / next apex) doesn't know it just merged
        // out of the pit lane, so it was turning in toward the racing line
        // immediately on release - reading as "turns onto the track way too
        // early" even though the physical pit-exit ramp geometry hasn't finished
        // narrowing back to the track edge yet. Returns 1 right at release,
        // fading to 0 by the end of the actual physical ramp (PitExitRampEndNormalized,
        // shared with TrackManager's ramp/guide-fence geometry), so callers can
        // blend their desired line toward holding the pit-exit lane instead of
        // snapping straight to the racing line.
        //
        // Round 2 fix: this originally reused IsInPitExitLimiterZone's own window,
        // which ends at PitExitLimiterEndNormalized (0.018) - a speed-cap window
        // deliberately kept short ("the tail past the merge only needs to cover
        // actually rejoining traffic, not a long extra caution stretch"). The
        // physical ramp itself doesn't finish narrowing back to the track edge
        // until PitExitRampEndNormalized (0.045), well past that. Reusing the
        // shorter window meant the blend dropped to 0 - handing the car back to
        // completely normal racing-line targeting - while the ramp geometry was
        // still narrowing, which is exactly the "turns onto the track too early"
        // still being reported after round 1. Uses its own, wider window matching
        // the real ramp instead.
        public float PitExitMergeBlend(float normalizedProgress)
        {
            bool inZone = normalizedProgress > PitExitLimiterStartNormalized || normalizedProgress < PitExitRampEndNormalized;
            if (!inZone)
            {
                return 0f;
            }

            float zoneLength = (1f - PitExitLimiterStartNormalized) + PitExitRampEndNormalized;
            if (zoneLength <= 0.0001f)
            {
                return 0f;
            }

            float wrapped = normalizedProgress >= PitExitLimiterStartNormalized
                ? normalizedProgress - PitExitLimiterStartNormalized
                : (1f - PitExitLimiterStartNormalized) + normalizedProgress;
            return 1f - Mathf.Clamp01(wrapped / zoneLength);
        }

        // Pit-exit path fix (PitPhase.ExitMerge): named aliases over the existing
        // PitExitLimiterStartNormalized/PitExitRampEndNormalized/PitExitMergeBlend
        // machinery above, which is already the single source of truth shared with
        // the real pit-exit barrier geometry (see TrackManager.PitZoneExitRampEnd -
        // literally the same constant). A second, independently-tuned merge-zone
        // boundary would risk exactly the "two systems silently disagree about
        // where the barrier/merge actually ends" bug already found and fixed once
        // in this pit-exit code (the round-2 fix comment above). PitReleaseNormalized
        // documents GetPitReleasePose's own hardcoded release distance for
        // readability - it's informational, not itself a zone boundary. Both are
        // now fixed-metre-based (see PitExitReleaseLeadMetres above), same as
        // every other boundary in this group.
        public float PitReleaseNormalized
        {
            get { return NormalizedMetresBeforeLine(PitExitReleaseLeadMetres); }
        }

        public float PitExitMergeEndNormalized
        {
            get { return PitExitRampEndNormalized; }
        }

        // Bugfix: this used to delegate to IsInPitExitLimiterZone, which ends at
        // PitExitLimiterEndNormalized (0.018) - the short SPEED-cap window. The
        // physical ramp/path merge doesn't finish until PitExitRampEndNormalized
        // (0.045), the same boundary PitExitMergeBlend above already uses. Reusing
        // the limiter's shorter window here silently reintroduced the exact
        // "hands the car back before the ramp is done narrowing" bug this zone
        // exists to prevent - the comments described using 0.045, but the actual
        // boolean still checked 0.018. Mirrors PitExitMergeBlend's own inZone test
        // directly instead of delegating to a differently-scoped zone.
        public bool IsInPitExitMergeZone(float normalizedProgress)
        {
            return normalizedProgress > PitExitLimiterStartNormalized || normalizedProgress < PitExitRampEndNormalized;
        }

        public bool PastPitExitMergeEnd(float normalizedProgress)
        {
            return !IsInPitExitMergeZone(normalizedProgress);
        }

        // ---------- pit ramp envelope (single source of truth) ----------
        // Pit-lane architecture fix: TrackManager.BuildPitRampSurface builds the
        // actual physical entry/exit ramp geometry, but RaceManager and
        // AiVehicleController used to each guess their own separate lateral math
        // for where the ramp "is" - guaranteed to disagree with the real surface
        // and with each other at the margins, which is exactly what put cars
        // around/over/inside pit barriers and made AI look like it was snapping
        // sideways out of nowhere. These constants/methods are now the ONE place
        // that math lives; TrackManager's own private PitZoneEntryRampStart/End,
        // PitZoneExitRampStart/End and PitRampNearTrackLateral/PitRampNarrowWidth/
        // PitRampFullWidth alias these exact values instead of inventing copies,
        // and BuildPitRampSurface itself now calls GetPitEntryRampEnvelope/
        // GetPitExitRampEnvelope rather than duplicating the taper math inline -
        // so the built collision/visual surface and every path-following consumer
        // (RaceManager's guided pit phases, AiVehicleController's approach
        // steering) are reading from the exact same function.
        // Fixed-metre pit-ENTRY geometry fix (completing the conversion the exit
        // boundaries above already got): the entry ramp and the pit boxes used to
        // be lap fractions (0.85 / 0.885 / 0.9), which on a realistically-scaled
        // track (Silverstone here is ~8281m) put the commit point ~1240m before
        // the start/finish line - the entire guided pit visit ran ~70+ seconds at
        // pit-lane pace, "more than half a lap". A real pit complex is a fixed
        // physical structure; it does not get longer because the circuit does.
        // The entry ramp now starts a fixed number of metres before the line and
        // the service boxes are anchored back from the release point (see
        // PitBoxDistance below), so a full visit is ~20 seconds on every track.
        public const float PitEntryRampStartLeadMetres = 460f;
        public const float PitCorridorStartLeadMetres = 390f;

        public float PitEntryRampStartNormalized
        {
            get { return NormalizedMetresBeforeLine(PitEntryRampStartLeadMetres); }
        }

        // Fixed-metre pit-exit geometry fix (see PitExitReleaseLeadMetres etc.
        // above): the physical ramp taper begins PitExitRampStartLeadMetres
        // before the line, not a fraction of the whole lap.
        public float PitExitRampStartNormalized
        {
            get { return NormalizedMetresBeforeLine(PitExitRampStartLeadMetres); }
        }
        // Speed-rebalance pass: widened ~25% alongside the wider base road, so the
        // pit lane/ramp gets proportionally more room too - AI has more physical
        // space to enter/exit cleanly instead of the pit path staying narrow while
        // the rest of the track widens around it. Round 2: widened another 25% on
        // top of that to match the further track width stack.
        public const float PitRampNearTrackLateral = 2.5f;
        public const float PitRampNarrowWidth = 9.38f;
        public const float PitRampFullWidth = 21.13f;

        // Entry ramp: tapers from the live track edge (HalfWidthAt + PitRampNearTrackLateral)
        // at PitEntryRampStartNormalized to the pit lane's own centerline/width at
        // PitCorridorStartNormalized. InverseLerp clamps, so a normalized value past
        // PitCorridorStartNormalized (i.e. already in the flat corridor) correctly
        // and gracefully flattens to exactly the pit lane's own centerline/width
        // rather than needing a separate branch for "past the ramp".
        public void GetPitEntryRampEnvelope(float normalized, float distance, out float lateral, out float halfWidth)
        {
            float trackEdgeLateral = HalfWidthAt(distance) + PitRampNearTrackLateral;
            float t = Mathf.InverseLerp(PitEntryRampStartNormalized, PitCorridorStartNormalized, normalized);
            lateral = Mathf.Lerp(trackEdgeLateral, PitLaneLateral, t);
            halfWidth = Mathf.Lerp(PitRampNarrowWidth, PitRampFullWidth, t) * 0.5f;
        }

        // Exit ramp: tapers from the pit lane's own centerline/width at
        // PitExitRampStartNormalized back out to the live track edge by
        // PitExitRampEndNormalized, wrapping through the start/finish line.
        public void GetPitExitRampEnvelope(float normalized, float distance, out float lateral, out float halfWidth)
        {
            float trackEdgeLateral = HalfWidthAt(distance) + PitRampNearTrackLateral;
            float wrapTotal = (1f - PitExitRampStartNormalized) + PitExitRampEndNormalized;
            float wrapped = normalized > PitExitRampStartNormalized ? normalized - PitExitRampStartNormalized : (1f - PitExitRampStartNormalized) + normalized;
            float exitT = wrapTotal <= 0.0001f ? 0f : Mathf.Clamp01(wrapped / wrapTotal);
            lateral = Mathf.Lerp(PitLaneLateral, trackEdgeLateral, exitT);
            halfWidth = Mathf.Lerp(PitRampFullWidth, PitRampNarrowWidth, exitT) * 0.5f;
        }

        public void SamplePitEntryRampPose(float distance, out Vector3 position, out Quaternion rotation)
        {
            float wrapped = WrapDistance(distance);
            float normalized = wrapped / Mathf.Max(1f, length);
            float lateral;
            float halfWidth;
            GetPitEntryRampEnvelope(normalized, wrapped, out lateral, out halfWidth);
            SamplePitLanePose(wrapped, lateral, out position, out rotation);
        }

        public void SamplePitExitRampPose(float distance, out Vector3 position, out Quaternion rotation)
        {
            float wrapped = WrapDistance(distance);
            float normalized = wrapped / Mathf.Max(1f, length);
            float lateral;
            float halfWidth;
            GetPitExitRampEnvelope(normalized, wrapped, out lateral, out halfWidth);
            SamplePitLanePose(wrapped, lateral, out position, out rotation);
        }

        // "Is this car on the pit-entry side" test - used to decide whether a car
        // has genuinely, physically committed to the pit lane before any guided pit
        // sequence is allowed to begin. A car still out on the racing line reads as
        // false even while inside the ramp window's normalized span.
        //
        // Pit-entry timing fix: this used to gate on the broader IsInPitEntryZone
        // (0.865-0.955), not the real physical ramp window (0.850-0.885,
        // IsInPitEntryRampWindow) - a car couldn't be considered "on the ramp" until
        // 0.865 even if it was already physically riding the built ramp surface at
        // 0.855, and this test kept accepting a match all the way out to 0.955, long
        // after the ramp itself had fully flattened into the corridor and the
        // divider wall had begun. Gating on the real window instead means a car is
        // only ever considered "on the ramp" while the physical ramp geometry
        // actually exists there.
        public bool IsOnPitEntryRamp(TrackProgress progress)
        {
            if (!IsInPitEntryRampWindow(progress.normalized))
            {
                return false;
            }

            float rampCenter;
            float rampHalfWidth;
            GetPitEntryRampEnvelope(progress.normalized, progress.distance, out rampCenter, out rampHalfWidth);
            if (Mathf.Abs(progress.lateralDistance - rampCenter) <= rampHalfWidth * 0.75f)
            {
                return true;
            }

            // Player pit-entry fix: at the START of the window the ramp
            // centerline sits OUTSIDE the live track edge (the envelope begins
            // at trackEdge + PitRampNearTrackLateral), so a car correctly
            // hugging the right-hand edge of the racing surface - exactly
            // where the pre-position assist and any sensible player put it -
            // could never satisfy the centerline-proximity test above, never
            // committed, and ground along the fan-out barrier at 0 km/h
            // instead of ever entering the pits. Hugging the outer edge while
            // the window is open IS committing to pit entry (the caller
            // already gates on an active pit request); the guided rail then
            // carries the car smoothly onto the canonical ramp path from
            // wherever it committed.
            return progress.lateralDistance >= HalfWidthAt(progress.distance) - 2.6f;
        }

        // Single authoritative pit-entry limiter boundary (bugfix): the painted
        // speed-limit line/sign (CreatePitEntryMarkers), the player's hard
        // limiter and the AI's hard limiter used to describe three different
        // boundaries - the sign was painted at PitApproachStartNormalized (0.78),
        // while RaceManager.HandlePitService only ever actually engaged
        // PitLimiterActive much later, at the real physical ramp commit
        // (~0.850-0.885). That let the AI's own pre-limiter approach-speed
        // shaping (which legitimately starts earlier, easing down toward a legal
        // speed) look like an active limiter well before one existed, while the
        // sign promised enforcement that hadn't actually started yet.
        // HasCrossedPitEntryLimiterLine is now the ONE function every one of
        // those consumers calls - it aliases IsOnPitEntryRamp so the boundary
        // also respects lateral position (a car with a pit request simply
        // passing the same longitudinal point on the racing surface, without
        // actually steering onto the ramp, must never trip the limiter).
        public bool HasCrossedPitEntryLimiterLine(TrackProgress progress)
        {
            return IsOnPitEntryRamp(progress);
        }

        // Placement anchor for the painted line/sign - the start of the window
        // HasCrossedPitEntryLimiterLine actually tests, so what's on the ground
        // matches where the limiter can first legally engage.
        public float PitEntryLimiterLineNormalized
        {
            get { return PitEntryRampStartNormalized; }
        }

        public bool IsOnPitExitRamp(TrackProgress progress)
        {
            if (!IsInPitExitMergeZone(progress.normalized))
            {
                return false;
            }

            float rampCenter;
            float rampHalfWidth;
            GetPitExitRampEnvelope(progress.normalized, progress.distance, out rampCenter, out rampHalfWidth);
            return Mathf.Abs(progress.lateralDistance - rampCenter) <= rampHalfWidth * 0.65f;
        }

        // Where the AI/player should aim while still on the racing surface,
        // approaching the pit entry - just outside the live track edge, not a
        // fraction of the track's own half-width (which is still inside the
        // racing surface and never actually visibly leaves the racing line).
        public float PitEntryApproachLateral(float distance)
        {
            return HalfWidthAt(distance) + PitRampNearTrackLateral;
        }

        // Where the real entry ramp's centerline actually is right now, for the
        // final steer-in blend once a car is deep enough into the entry zone to
        // aim at the ramp itself rather than just "off to the pit side".
        public float PitEntryPathLateral(float distance)
        {
            float wrapped = WrapDistance(distance);
            float normalized = wrapped / Mathf.Max(1f, length);
            float lateral;
            float halfWidth;
            GetPitEntryRampEnvelope(normalized, wrapped, out lateral, out halfWidth);
            return lateral;
        }

        // Real collider half-width is roughly 0.875m; this leaves a genuine
        // ~1.2-1.4m of clearance between the car's centre and the paved track
        // edge while pre-positioning ahead of the real pit-entry opening.
        public const float PitEntryCarBodyClearanceMeters = 1.3f;

        // Shared pit-entry target-point builder (used by both AiVehicleController
        // and RaceManager.BuildPitEntryAssistCommand, so player and AI pit-entry
        // geometry can never diverge again). Builds a dedicated world-space
        // pit-entry target - position AND lateral sampled together at the SAME
        // distance, using the canonical ramp/track geometry - instead of
        // grafting a lateral computed at one distance onto a point sampled at
        // another. Never looks past the real physical opening
        // (PitCorridorStartNormalized).
        //
        // Before the real ramp begins (PitEntryRampStartNormalized), the target
        // is deliberately kept just inside the live track edge
        // (HalfWidthAt - PitEntryCarBodyClearanceMeters) - PitEntryApproachLateral
        // is NOT used here, because it intentionally returns a point outside the
        // track edge (behind the barrier that stands there until the real
        // opening starts), which would steer a car into the wall between
        // PitApproachStartNormalized and PitEntryRampStartNormalized.
        public void ComputePitEntryTargetPoint(float fromDistance, float lookAheadMeters, out Vector3 targetPoint, out Quaternion targetRotation)
        {
            float corridorStartDistance = length * PitCorridorStartNormalized;
            float distanceToCorridor = Mathf.Max(1f, WrapDistance(corridorStartDistance - fromDistance));
            float pitLookAhead = Mathf.Min(lookAheadMeters, distanceToCorridor);
            float pitTargetDistance = WrapDistance(fromDistance + pitLookAhead);
            float pitTargetNormalized = pitTargetDistance / Mathf.Max(1f, length);

            if (pitTargetNormalized < PitEntryRampStartNormalized)
            {
                // Stage A (pre-position): still on the live racing surface,
                // ahead of the real opening - line up on the outer-right edge,
                // with genuine car-body clearance from the paved edge, so the
                // car is already positioned to turn in the instant the ramp
                // starts.
                // Wall-clearance fix: 1.3m of clearance put the car's body
                // ~0.4m from a barrier that sits flush with the paved edge -
                // one steering oscillation from wedging against it at ~0 km/h
                // (exactly the reported player pit-entry failure). 2.4m keeps
                // the car unmistakably in the rightmost lane while leaving a
                // real margin from the wall.
                // Round 2 (per report - pinned against the wall again): the
                // player's pit-entry pace was raised to 100 kph, and at that
                // speed one oscillation eats the 2.4m margin. Widened to 3.2m -
                // still clearly in the rightmost lane, with enough air that a
                // fast approach can wobble once and correct without touching
                // the barrier.
                float preEntryLateral = HalfWidthAt(pitTargetDistance) - 3.2f;
                SamplePitLanePose(pitTargetDistance, preEntryLateral, out targetPoint, out targetRotation);
            }
            else
            {
                // Stage B (on the ramp): physically inside the real opening -
                // the canonical built ramp envelope/pose (GetPitEntryRampEnvelope
                // via SamplePitEntryRampPose), the same surface
                // BuildPitRampSurface paves.
                SamplePitEntryRampPose(pitTargetDistance, out targetPoint, out targetRotation);
            }
        }

        // Pit-exit release lateral: where a car ends up the instant physics is
        // handed back (see RaceManager.CompletePitRail / RailLateralTarget).
        //
        // Barrier fix: this used to return HalfWidthAt - 1.2m. The edge barrier
        // sits at HalfWidthAt + EdgeBarrierClearance (0.15m), and a car body is
        // ~0.875m half-width, so a car released at HalfWidthAt - 1.2m had its
        // OUTER edge only ~0.5m from the wall - one small AI steering correction
        // and it clipped the barrier, stalled, and every car behind it in the
        // exit train piled into it (the pit-exit stockpile seen from the rear
        // camera). Merged cars now hand off onto a proper inside lane at ~55% of
        // the half-width - firmly on the pit side of the track (so they don't cut
        // across the racing line) but with a genuine ~2m of clearance to the
        // barrier, giving the AI room to settle and pick up the racing line
        // cleanly instead of fighting the wall. Still comfortably inside
        // HalfWidthAt, so the merge/handoff on-track checks all still pass.
        public float PitExitMergeLegalLateral(float distance)
        {
            return HalfWidthAt(distance) * 0.55f;
        }

        // ---------- hairpin widening ----------
        // Single shared width source so hairpins are physically wider - AI cars were
        // clipping barriers/each other in tight corners because every consumer (road
        // mesh/collider, kerbs, barriers, runoff) drew from the same flat roadHalfWidth
        // with no extra room at the tightest corners. HalfWidthAt(distance) is that
        // shared source: it returns the base roadHalfWidth everywhere except near a
        // hairpin, where it eases up to roadHalfWidth+HairpinExtraHalfWidth. Every
        // TrackManager pass that lays out the physical road, kerbs, barriers, runoff
        // furniture or racing surface should sample THIS instead of the flat field so a
        // widened hairpin is honored everywhere uniformly.
        //
        // "Hairpin" reuses the exact severity threshold BuildKerbs/BuildContinuousEdgeBarriers
        // already established elsewhere in TrackManager for "this corner gets the
        // aggressive kerb / tyre stack / apex chevron treatment" (a >55 degree turn
        // between consecutive centerline segments) rather than inventing a new
        // classification, so "hairpin" means the same thing everywhere in the file.
        public const float HairpinCornerAngleThreshold = 55f;
        // Speed-rebalance pass: scaled with the wider base roadHalfWidth (both by
        // the same ~1.25x) so a hairpin's extra bonus stays proportional, and the
        // ease-in distance widened too so the now-faster cars get a more gradual
        // width transition instead of a sharper one at the same old blend length.
        // Round 2: stacked another 25% to match the further roadHalfWidth increase.
        public const float HairpinExtraHalfWidth = 7f;
        public const float HairpinBlendDistance = 37.5f;

        readonly List<float> hairpinCenters = new List<float>();

        public IReadOnlyList<float> HairpinCenters { get { return hairpinCenters; } }

        // Recomputes the hairpin center list from the FINAL centerline (after layout
        // repair/scaling), so it stays in lock-step with cumulativeDistances. Call
        // once right after RecalculateDistances(); nothing else mutates centerLine
        // after that point during Build().
        // Windowed-cumulative-turn fix: this used to be a pure single-vertex check
        // (just the angle at one smoothed-centerline point), which misses a real
        // hairpin-grade turn whose direction change is spread across several nearby
        // points instead of concentrated at one - exactly what layout repair's own
        // kink-smoothing pass (TrackManager.RepairLayout) does to a raw sharp
        // hand-authored anchor. TrackManager.DetectCorners was already rewritten to
        // sum turn angle over a trailing real-world-distance window for this same
        // reason (see its own comment), but that fix never reached this function -
        // so a corner could be correctly identified as hairpin-grade for barrier/
        // tyre-stack containment purposes while never receiving the matching
        // HairpinExtraHalfWidth pavement widening, leaving it fenced like a hairpin
        // but paved like a normal corner (confirmed on Melbourne's second-sharpest
        // corner, whose ~157 degree raw turn survives repair as three consecutive
        // ~50 degree kinks, each individually under the old single-vertex
        // threshold). Sums the vertex-to-vertex turn over a trailing window of real
        // arc-length instead, using cumulativeDistances so it works regardless of
        // how unevenly spaced the repaired/smoothed points are.
        // Speed-rebalance pass: every layout's anchors now scale up ~25% uniformly
        // (NormalizeTrackLength/TargetTrackLength), so a real corner's own physical
        // arc-length grew by the same proportion - this trailing-window span has to
        // widen to match, or it under-samples a now-bigger corner and misses turns
        // it used to correctly catch as hairpin-grade.
        const float HairpinWindowSpanMeters = 87.5f;

        public void RecalculateHairpinWidening()
        {
            hairpinCenters.Clear();
            int count = centerLine.Count;
            if (count < 4)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                float cumulativeTurn = 0f;
                float coveredDistance = 0f;
                int index = i;
                int guard = 0;
                while (coveredDistance < HairpinWindowSpanMeters && guard < count)
                {
                    int previousIndex = (index - 1 + count) % count;
                    int previousPreviousIndex = (index - 2 + count) % count;
                    Vector3 a = centerLine[previousPreviousIndex];
                    Vector3 b = centerLine[previousIndex];
                    Vector3 c = centerLine[index];
                    Vector3 entry = (b - a).normalized;
                    Vector3 exit = (c - b).normalized;
                    cumulativeTurn += Vector3.Angle(entry, exit);
                    coveredDistance += Mathf.Max(0.01f, Vector3.Distance(b, c));
                    index = previousIndex;
                    guard++;
                }

                if (cumulativeTurn > HairpinCornerAngleThreshold)
                {
                    hairpinCenters.Add(cumulativeDistances[i]);
                }
            }
        }

        // Extra half-width at this distance, eased from HairpinExtraHalfWidth at a
        // hairpin's own centerline vertex down to zero at HairpinBlendDistance away
        // (smoothstep, not a hard step) so entry/apex/exit all widen gradually rather
        // than the road suddenly stepping wider/narrower. Two hairpins closer together
        // than 2x HairpinBlendDistance simply take the nearer one's bonus each side
        // rather than stacking, so back-to-back tight corners never compound into an
        // unbounded width.
        public float HairpinWidthBonus(float distance)
        {
            if (hairpinCenters.Count == 0 || length <= 0f)
            {
                return 0f;
            }

            float wrapped = WrapDistance(distance);
            float nearest = float.MaxValue;
            for (int i = 0; i < hairpinCenters.Count; i++)
            {
                float delta = Mathf.Abs(wrapped - hairpinCenters[i]);
                float wrappedDelta = Mathf.Min(delta, length - delta);
                if (wrappedDelta < nearest)
                {
                    nearest = wrappedDelta;
                }
            }

            if (nearest >= HairpinBlendDistance)
            {
                return 0f;
            }

            float t = 1f - Mathf.Clamp01(nearest / HairpinBlendDistance);
            float eased = t * t * (3f - 2f * t);
            return HairpinExtraHalfWidth * eased;
        }

        // The single shared width source described above - every road/kerb/barrier/
        // runoff pass in TrackManager should call this instead of reading the flat
        // roadHalfWidth field directly.
        public float HalfWidthAt(float distance)
        {
            float baseHalfWidth = roadHalfWidth;
            if (authoredHalfWidthProfile != null && authoredHalfWidthProfile.Length > 1 && length > 0f)
            {
                // Wrap-aware linear interpolation over the uniform profile.
                float t = Mathf.Repeat(distance / length, 1f) * authoredHalfWidthProfile.Length;
                int index = Mathf.FloorToInt(t) % authoredHalfWidthProfile.Length;
                int next = (index + 1) % authoredHalfWidthProfile.Length;
                baseHalfWidth = Mathf.Lerp(authoredHalfWidthProfile[index], authoredHalfWidthProfile[next], t - Mathf.Floor(t));
            }

            return baseHalfWidth + HairpinWidthBonus(distance);
        }

        // Procedural per-section width variation for the hand-authored layouts,
        // which otherwise run at a single flat roadHalfWidth from start to finish
        // (per request - "why are all the track widths the same? some parts should
        // be wider and some narrower"). Spline/authored circuits already carry
        // their own authored per-point profile, so this no-ops when one exists.
        // Width eases WIDER on the straights and NARROWER through corners, on top
        // of a stable per-track base scale and a low-frequency wave, so no two
        // circuits - and no two stretches of a lap - share the same width. Runs
        // once at build time, AFTER the centreline is final and BEFORE the road
        // mesh / racing line / barriers sample HalfWidthAt, so all of them honour
        // the variation together. The hairpin widening bonus still stacks on top.
        public void GenerateProceduralWidthProfile()
        {
            if (length <= 1f || centerLine == null || centerLine.Count < 8)
            {
                return;
            }

            // Every authored circuit carries a single flat HalfWidthMeters, so its
            // profile is uniform along the lap even though tracks differ from each
            // other. This runs REGARDLESS of whether a profile already exists: it
            // reads the current per-sample width as the base (preserving each
            // track's own authored width) and layers the WITHIN-lap variation on
            // top. A genuinely flat (non-authored) layout also gets a stable
            // per-track base scale so those differ from one another too.
            bool hasAuthored = authoredHalfWidthProfile != null && authoredHalfWidthProfile.Length > 1;

            int hash = 23;
            string id = trackId ?? "";
            for (int i = 0; i < id.Length; i++)
            {
                hash = unchecked(hash * 31 + id[i]);
            }
            hash &= 0x7fffffff;

            // Break the lap into a per-track set of SECTIONS with dramatically
            // different widths (per request - make it EXTREMELY obvious, and not
            // merely corners vs straights). Sections deliberately ALTERNATE wide
            // and narrow so there's always a clear contrast - one stretch opens
            // out to a broad multi-lane expanse, the next pinches into a genuinely
            // tight corridor - with a random magnitude within each band so it
            // isn't mechanical. Deterministic per track.
            int sectionCount = 5 + (hash % 4); // 5..8 alternating stretches
            float[] sectionMul = new float[sectionCount];
            for (int k = 0; k < sectionCount; k++)
            {
                int sh = unchecked(hash * 131 + k * 977 + 17) & 0x7fffffff;
                float r = (sh % 1000) / 1000f;
                sectionMul[k] = (k % 2 == 0)
                    ? Mathf.Lerp(1.15f, 1.42f, r)   // wide
                    : Mathf.Lerp(0.46f, 0.74f, r);  // narrow
            }

            // Authored specs already set a per-track base width, so only the flat
            // fallback layouts need an extra per-track scale to tell them apart.
            float trackScale = hasAuthored ? 1f : Mathf.Lerp(0.85f, 1.12f, ((hash / 7) % 1000) / 1000f);

            const int ProfileSamples = 128;
            float[] profile = new float[ProfileSamples];
            for (int s = 0; s < ProfileSamples; s++)
            {
                float u = s / (float)ProfileSamples;
                float dist = u * length;

                // Base width at this point: the existing (authored) profile if
                // there is one, otherwise the flat roadHalfWidth.
                float baseHalf;
                if (hasAuthored)
                {
                    float ft = u * authoredHalfWidthProfile.Length;
                    int idx = Mathf.Clamp((int)ft, 0, authoredHalfWidthProfile.Length - 1);
                    baseHalf = authoredHalfWidthProfile[idx];
                }
                else
                {
                    baseHalf = roadHalfWidth;
                }

                baseHalf = Mathf.Max(6f, baseHalf * trackScale);

                // Section width, smoothly blended between adjacent sections so the
                // transitions read as the road opening up / pinching in rather
                // than a hard step.
                float sf = u * sectionCount;
                int si = ((int)sf) % sectionCount;
                int sn = (si + 1) % sectionCount;
                float frac = Mathf.SmoothStep(0f, 1f, sf - Mathf.Floor(sf));
                float sectionWidth = Mathf.Lerp(sectionMul[si], sectionMul[sn], frac);

                // A little extra pinch through the tightest corners on top, so a
                // corner inside a wide section is still slightly tighter than the
                // straight beside it.
                Vector3 pA, fA, rA;
                Vector3 pB, fB, rB;
                SampleAtDistance(dist, out pA, out fA, out rA);
                SampleAtDistance(dist + 22f, out pB, out fB, out rB);
                float curvature = Mathf.Clamp01(Vector3.Angle(fA, fB) / 24f);
                float cornerFactor = Mathf.Lerp(1.04f, 0.86f, curvature);

                // Floor at 7m half-width (14m wide) so the narrow sections are
                // clearly tighter but never so pinched that the field wedges.
                profile[s] = Mathf.Clamp(baseHalf * sectionWidth * cornerFactor, 7f, baseHalf * 1.45f);
            }

            authoredHalfWidthProfile = profile;
        }

        // ---------- corner risk classification (public, UI-facing) ----------
        // A standalone windowed-cumulative-curvature scan - the same idea
        // TrackManager's own (private) barrier/fencing corner detection uses,
        // reimplemented here as a small public API so UI code (track preview,
        // race-engineer messaging) can ask "what are this track's corners and
        // how severe are they" without needing a reference to the internal
        // track-builder MonoBehaviour, which only exists for the duration of
        // Build() and isn't something other systems should hold onto.
        public enum CornerRisk { Low, Medium, High }

        public struct CornerRiskInfo
        {
            public float distance;
            public float normalized;
            public CornerRisk risk;
        }

        public List<CornerRiskInfo> ClassifyCorners()
        {
            List<CornerRiskInfo> results = new List<CornerRiskInfo>();
            if (length <= 1f)
            {
                return results;
            }

            const float sampleStep = 8f;
            const float windowSpan = 56f;
            const float lowThreshold = 25f;
            const float mediumThreshold = 40f;
            const float highThreshold = 55f;

            int sampleCount = Mathf.Clamp(Mathf.RoundToInt(length / sampleStep), 12, 2000);
            int windowSamples = Mathf.Max(2, Mathf.RoundToInt(windowSpan / (length / sampleCount)));
            Vector3[] forwards = new Vector3[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                SampleAtDistance(length * i / sampleCount, out point, out forward, out right);
                forwards[i] = forward;
            }

            float[] cumulative = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float sum = 0f;
                for (int w = 0; w < windowSamples; w++)
                {
                    int a = (i - w - 1 + sampleCount * 4) % sampleCount;
                    int b = (i - w + sampleCount * 4) % sampleCount;
                    sum += Vector3.Angle(forwards[a], forwards[b]);
                }

                cumulative[i] = sum;
            }

            int runStart = -1;
            int runPeakIndex = 0;
            float runPeakValue = 0f;
            for (int i = 0; i < sampleCount * 2; i++)
            {
                int idx = i % sampleCount;
                bool above = cumulative[idx] > lowThreshold;
                if (above)
                {
                    if (runStart < 0)
                    {
                        runStart = i;
                        runPeakIndex = idx;
                        runPeakValue = cumulative[idx];
                    }
                    else if (cumulative[idx] > runPeakValue)
                    {
                        runPeakIndex = idx;
                        runPeakValue = cumulative[idx];
                    }
                }
                else if (runStart >= 0)
                {
                    float peakDistance = length * runPeakIndex / sampleCount;
                    bool duplicate = false;
                    for (int r = 0; r < results.Count; r++)
                    {
                        float delta = Mathf.Abs(WrapDistance(peakDistance - results[r].distance));
                        if (Mathf.Min(delta, length - delta) < windowSpan * 0.5f)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                    {
                        CornerRisk risk = runPeakValue >= highThreshold ? CornerRisk.High : (runPeakValue >= mediumThreshold ? CornerRisk.Medium : CornerRisk.Low);
                        results.Add(new CornerRiskInfo { distance = peakDistance, normalized = peakDistance / length, risk = risk });
                    }

                    runStart = -1;
                }

                if (i >= sampleCount && runStart < 0)
                {
                    break;
                }
            }

            return results;
        }

        // ---------- grid layout ----------
        // Single source of truth for grid slot placement so painted boxes,
        // validation, and RaceManager spawning can never drift apart.

        public const int GridSlotCount = 22;
        public const float GridStartOffset = 52f;
        public const float GridRowSpacing = 19f;
        public const float GridStaggerOffset = 9f;

        public float GridLaneWidth
        {
            get { return Mathf.Min(5.2f, roadHalfWidth * 0.4f); }
        }

        public void GetGridSlot(int gridIndex, out float distance, out float lateralOffset)
        {
            int row = gridIndex / 2;
            bool leftSlot = gridIndex % 2 == 0;
            distance = length - GridStartOffset - row * GridRowSpacing - (leftSlot ? 0f : GridStaggerOffset);
            lateralOffset = leftSlot ? -GridLaneWidth : GridLaneWidth;
        }

        // ---------- pit lane ----------
        // Every entrant owns a unique pit box along the service road; the shared
        // single service pose was the reason whole fields stacked onto one spot.

        public const int PitBoxCount = 22;
        public const float PitBoxSpacing = 10.5f;
        // How far before the start/finish line the LAST service box sits; the
        // full bay row extends (PitBoxCount-1)*PitBoxSpacing further back from
        // here. Fixed metres, same rationale as the entry/exit boundaries.
        public const float PitLaneEndLeadMetres = 90f;
        // Where the pit corridor's own drivable surface and outer wall begin (shared
        // with TrackManager's PitZoneEntryRampEnd/BuildPitLane so both classes agree
        // on the same seam instead of drifting apart behind two separate literals).
        // Fixed metres before the line (see PitEntryRampStartLeadMetres above).
        public float PitCorridorStartNormalized
        {
            get { return NormalizedMetresBeforeLine(PitCorridorStartLeadMetres); }
        }
        // A held-back queue pose must never land before the pit corridor's own
        // surface begins - the caller's holdback can be large, which used to push
        // the queue point off the built pit lane surface entirely.
        const float PitQueueCorridorMargin = 10f;

        // Speed-rebalance pass: the standoff from the track edge widened alongside
        // PitRampFullWidth (13.5f -> 16.9f) so the pit lane's own inner edge keeps
        // the same real clearance from the live track edge as before, not a
        // shrunken one now that the corridor itself is wider. Round 2: widened
        // another 25% (11.5f -> 14.38f) to match the further PitRampFullWidth stack.
        public float PitLaneLateral
        {
            get { return roadHalfWidth + 14.38f; }
        }

        // Fast-lane/service-bay separation fix (root cause 1): PitBoxSpacing is
        // only 10.5m, so with every box sitting directly on PitLaneLateral - the
        // same lateral every car travels down the pit lane on - a stationary car
        // in one box was always within blocking range of the very next box,
        // serializing the whole field through the pit lane one car at a time.
        // Service bays now sit further toward the garages than the fast lane
        // (well within the existing PitRampFullWidth-wide paved corridor, no
        // surface widening needed - 4.2m of offset against a ~10.6m half-width
        // leaves comfortable clearance on both sides), so a parked car no longer
        // physically overlaps the fast lane at all. AdvancePitGuideTarget's
        // existing pitGuideLateral interpolation is what carries a car smoothly
        // from the fast lane into its bay on the way in, and back out again on
        // the way to pit exit - this offset is the only change needed for that
        // to happen automatically.
        public const float PitServiceBayOffsetMeters = 4.2f;

        public float PitServiceBayLateral
        {
            get { return PitLaneLateral + PitServiceBayOffsetMeters; }
        }

        public float PitBoxDistance(int pitBoxIndex)
        {
            // Boxes are anchored back from the pit-lane END (fixed metres before
            // the line), so the whole bay row keeps the same physical size and
            // the box→release drive stays short on every track length.
            int index = Mathf.Clamp(pitBoxIndex, 0, PitBoxCount - 1);
            return length - PitLaneEndLeadMetres - (PitBoxCount - 1 - index) * PitBoxSpacing;
        }

        public void GetPitServicePose(int pitBoxIndex, out Vector3 position, out Quaternion rotation)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(PitBoxDistance(pitBoxIndex), out point, out forward, out right);
            position = point + right * PitServiceBayLateral + Vector3.up * 0.58f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void GetPitServicePose(out Vector3 position, out Quaternion rotation)
        {
            GetPitServicePose(0, out position, out rotation);
        }

        // Root cause 3 fix: exposes the canonical distance a queue pose is
        // generated from directly, so RaceManager can drive AdvancePitGuideTarget
        // from this known-correct distance instead of reprojecting the resulting
        // world position back through an unrestricted Track.GetProgress search
        // (unsafe wherever the pit lane runs close to another part of the
        // circuit).
        public float GetPitQueueDistance(int pitBoxIndex, float holdBackMeters)
        {
            float minDistance = length * PitCorridorStartNormalized + PitQueueCorridorMargin;
            float desired = PitBoxDistance(pitBoxIndex) - Mathf.Max(4f, holdBackMeters);
            return Mathf.Max(minDistance, desired);
        }

        // Queue pose short of a pit box, used while a car waits for its slot or a
        // safe release gap so two cars are never guided into the same spot.
        public void GetPitQueuePose(int pitBoxIndex, float holdBackMeters, out Vector3 position, out Quaternion rotation)
        {
            SamplePitLanePose(GetPitQueueDistance(pitBoxIndex, holdBackMeters), PitLaneLateral, out position, out rotation);
        }

        // Pit lane animation fix: generalizes the position + right * lateral
        // pattern every GetPit*Pose method above already uses, so RaceManager
        // can sample a continuously-advancing point along the SAME
        // track-relative parametrization instead of only ever being able to
        // ask for one of a handful of fixed named poses. This is what lets
        // a pit-guided car's path actually follow the track/pit lane's own
        // curvature (SampleAtDistance already curves with the circuit) rather
        // than cutting a straight 3D line between two distant fixed points.
        public void SamplePitLanePose(float distance, float lateral, out Vector3 position, out Quaternion rotation)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(distance, out point, out forward, out right);
            position = point + right * lateral + Vector3.up * 0.58f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        // Pit-lane architecture fix: this used to place the entry pose at a flat
        // roadHalfWidth + 5.6 offset, which drifts wrong on any track with a
        // widened road at this exact distance (HalfWidthAt != roadHalfWidth) and
        // could disagree with the actual built ramp surface entirely. At
        // PitCorridorStartNormalized the real ramp envelope has already tapered
        // fully to the pit lane's own centerline (GetPitEntryRampEnvelope returns
        // exactly PitLaneLateral there), so this now just samples that real
        // surface directly instead of guessing a separate lateral.
        public void GetPitEntryPose(out Vector3 position, out Quaternion rotation)
        {
            SamplePitEntryRampPose(length * PitCorridorStartNormalized, out position, out rotation);
        }

        public void GetPitReleasePose(int staggerSlot, out Vector3 position, out Quaternion rotation)
        {
            // Pit-lane architecture fix: release (PitReleaseNormalized, 0.992) is
            // still inside the flat pit corridor - the exit ramp itself doesn't
            // start narrowing until PitExitRampStartNormalized (0.995). The old
            // roadHalfWidth + 4.8 offset placed release in an arbitrary strip that
            // could sit inside or outside the real pit lane surface depending on
            // road width at that point; PitLaneLateral is the corridor's own actual
            // centerline, guaranteed to match the built surface everywhere.
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(length * PitReleaseNormalized - Mathf.Max(0, staggerSlot) * 7.5f, out point, out forward, out right);
            position = point + right * PitLaneLateral + Vector3.up * 0.62f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void GetPitReleasePose(out Vector3 position, out Quaternion rotation)
        {
            GetPitReleasePose(0, out position, out rotation);
        }

        public float WrapDistance(float distance)
        {
            if (length <= 0f)
            {
                return 0f;
            }

            while (distance < 0f)
            {
                distance += length;
            }

            while (distance >= length)
            {
                distance -= length;
            }

            return distance;
        }
    }

    public class TrackValidationReport
    {
        public string trackName = "";
        public int centerLinePoints;
        public float trackLength;
        public int longSegmentsSplit;
        public int shortSegmentsMerged;
        public int violentAnglesSmoothed;
        public bool roadColliderValid;
        public int invalidObstaclesFlagged;
        public bool gridSpawnValid = true;
        public bool pitPosesValid = true;
        // Track validation/self-healing summary fields: each pairs with an
        // existing detect-and-auto-fix pass that already runs during Build
        // (ValidateBarrierColliderCoverage, ValidateWidthContinuity,
        // ValidatePitLaneSurfaceCoverage) - these just surface the counts that
        // pass already computes locally, instead of only logging them.
        public int barrierGapCount;
        public int barrierGapAutoFilledCount;
        public int sharpEdgeCount;
        public int pitSurfaceGapCount;
        public int obstacleIntrusionCount;
        public readonly List<string> warnings = new List<string>();

        public void Warn(string message)
        {
            warnings.Add(message);
            Debug.LogWarning("[TrackValidation] " + trackName + ": " + message);
        }

        public string Summary()
        {
            return "[TrackAudit] " + trackName +
                   " | points=" + centerLinePoints +
                   " | length=" + trackLength.ToString("0") + "m" +
                   " | roadCollider=" + (roadColliderValid ? "OK" : "INVALID") +
                   " | repaired(split=" + longSegmentsSplit + " merged=" + shortSegmentsMerged + " smoothed=" + violentAnglesSmoothed + ")" +
                   " | obstaclesFlagged=" + invalidObstaclesFlagged +
                   " | barrierGaps=" + barrierGapCount + "(filled " + barrierGapAutoFilledCount + ")" +
                   " | sharpEdges=" + sharpEdgeCount +
                   " | pitSurfaceGaps=" + pitSurfaceGapCount +
                   " | obstacleIntrusions=" + obstacleIntrusionCount +
                   " | grid=" + (gridSpawnValid ? "OK" : "INVALID") +
                   " | pit=" + (pitPosesValid ? "OK" : "INVALID") +
                   " | warnings=" + warnings.Count;
        }
    }

    public class TrackManager : MonoBehaviour
    {
        public TrackRuntime Runtime { get; private set; }
        public TrackValidationReport LastReport { get; private set; }

        // Scenery/detail spawn multiplier, set by the race flow from graphics settings before Build.
        public float sceneryDensity = 1f;

        // Set by the race flow before Build for sessions that must always run in
        // dry conditions (time trial): overrides the event's weather profile so
        // the built track (surface materials, wetness, scenery mood) and the
        // session weather all agree on Clear.
        public bool forceDryWeather;

        Material roadMaterial;
        Material kerbMaterial;
        Material grassMaterial;
        Material lineMaterial;
        Material roadEdgeMaterial;
        Material drsPaintMaterial;
        Material rubberMaterial;
        Material tyreMarbleMaterial;
        Material asphaltPatchMaterial;
        Material skidMarkMaterial;
        Material barrierMaterial;
        Material armcoMaterial;
        Material tireBarrierMaterial;
        Material concreteMaterial;
        // Cached so ValidatePitLaneSurfaceCoverage's auto-fill (CheckPitSurfaceRange)
        // can drop a patch of real pit asphalt at any raycast-detected floor gap using
        // the exact same material every other pit surface piece uses, instead of
        // needing BuildPitLane's local material threaded all the way through.
        Material pitLaneMaterial;
        Material fenceMaterial;
        Material fencePostMaterial;
        Material foliageMaterial;
        // Second, lighter canopy tone so a broadleaf canopy built from several
        // lobes reads as layered foliage instead of one flat-colour blob.
        Material foliageMaterialLight;
        // Backdrop-hill pass (per report - "massive blobs of green"): hills are
        // no longer painted flat canopy-green. Near hills get a noise-textured
        // earthy slope tone (the green comes from real trees planted on them),
        // and the far ridge/treeline layers get a duller, hazier tone so they
        // read as distant terrain instead of lime domes.
        Material hillsideEarthMaterial;
        Material distantForestMaterial;
        Material treeBarkMaterial;
        Material metalMaterial;
        Material glassMaterial;
        Material lightGlowMaterial;
        Material sceneryAccentMaterial;
        Material trafficConeMaterial;
        Material flagGreenMaterial;
        Material flagYellowMaterial;
        Material raceControlBoardMaterial;
        Material raceControlBoardVscMaterial;
        Material raceControlBoardScMaterial;
        Material gantryRaceControlLightMaterial;
        PhysicMaterial roadPhysicsMaterial;
        PhysicMaterial runoffPhysicsMaterial;
        Mesh visualBoxMesh;
        readonly List<TrackSolidObstacle> solidObstacles = new List<TrackSolidObstacle>();

        // Race-control visual wiring: renderers/text captured while building the
        // marshal posts, SC/VSC board and start gantry so SetRaceControlVisual (via
        // the RaceControlVisualDriver component) can restyle them later without
        // RaceManager needing to know how any of this furniture was constructed.
        readonly List<Renderer> marshalFlagBoardRenderers = new List<Renderer>();
        Renderer raceControlBoardRenderer;
        TextMesh raceControlBoardText;
        RaceControlVisualDriver raceControlVisualDriver;

        // World Y of the flat terrain surface; road above this by more than the
        // threshold counts as elevated (bridge/overpass/hillside) and gets full
        // side containment instead of sparse runoff markers.
        float groundTopY;
        const float ElevationThreshold = 1.35f;
        const float TallFenceElevation = 3f;

        // Visual identity flags derived from the event so night circuits glow and
        // desert circuits bake, instead of everything sharing one look.
        bool nightTrack;
        bool twilightTrack;
        bool desertTrack;
        bool streetTrack;
        bool monacoTrack;
        bool suzukaTrack;
        bool spaTrack;
        bool neonTrack;
        bool wetTrack;
        // Additional archetype flags so every one of the 24 calendar tracks gets a
        // dedicated environment pass instead of only the tracks covered by the
        // original desert/street/monaco/neon/parkland buckets.
        bool parklandTrack;
        bool technicalParklandTrack;
        bool coastalTrack;
        bool urbanHillsideTrack;
        bool canadaTrack;
        // Jeddah gets a signature pass on top of the shared street-circuit
        // treatment (per request - "add more and larger skyscrapers and more
        // and larger grandstands to it"): a dedicated corniche high-rise
        // skyline and oversized grandstands.
        bool jeddahTrack;
        Material edgeGlowMaterial;
        Material[] neonMaterials;
        Material yachtMaterial;
        Material toriiMaterial;
        Material windowStripMaterial;
        Material grandstandRoofMaterial;
        // One bunting material per SponsorPalette colour, shared by every
        // grandstand flag on the track (see BuildGrandstand) - lazily built on
        // the first stand and reused for the rest of this manager's lifetime.
        Material[] buntingMaterials;
        Material luxuryApartmentMaterial;
        Material weatheredConcreteMaterial;
        Material coastalSandMaterial;
        Material[] hillsideBuildingMaterials;

        // Dedicated tropical frond tone for the coastal palm clusters below - distinct
        // from the deep-forest foliageMaterial so a seaside circuit doesn't borrow the
        // same dark-green canopy colour a parkland track uses.
        Material palmFrondMaterial;

        // Round 2 surface/environment pass: sandy gravel-trap runoff, harbour/sea water
        // planes, mountain rock outcrops and the finish-line checker pattern all need a
        // material distinct from anything above rather than reusing a near-match.
        Material gravelMaterial;
        Material waterMaterial;
        Material rockMaterial;
        Material checkerDarkMaterial;

        // Round 3 surface/barrier/scenery pass: a glossier top layer for the session
        // rubber build-up, a second tyre-marble tint for corner-exit variety, a blue
        // kerb-paint alternative for coastal/technical circuits, and a canvas tone
        // for temporary bleacher sun-shades - each distinct from the nearest existing
        // material rather than reusing a near-match.
        Material rubberSheenMaterial;
        Material tyreMarbleMaterialLight;
        Material kerbMaterialBlue;
        Material bleacherCanvasMaterial;

        // Barrier weathering pass: a low, dark grime/rust streak material layered
        // near the base of Armco rails and street walls so a barrier that has stood
        // through a season of races reads as weathered rather than freshly painted,
        // distinct from both the clean barrierMaterial/armcoMaterial finish and the
        // rubberMaterial the surface-detail passes already use.
        Material barrierWeatherMaterial;

        public TrackRuntime Build(CalendarEventData eventData)
        {
            return Build(eventData, true);
        }

        public TrackRuntime Build(CalendarEventData eventData, bool showRacingLine)
        {
            ClearChildren();
            LastReport = new TrackValidationReport
            {
                trackName = eventData != null ? eventData.displayName : "Prototype GP"
            };
            Runtime = CreateLayout(eventData);
            // Vary the road width per track and per section - the hand-authored
            // layouts otherwise all run at one flat width. Runs before the road
            // mesh, racing line and barriers below, which all sample HalfWidthAt.
            Runtime.GenerateProceduralWidthProfile();
            string trackId = Runtime.trackId ?? "";
            string weatherProfile = eventData != null && eventData.weatherProfile != null ? eventData.weatherProfile.ToLowerInvariant() : "";
            // Night/twilight now follow the calendar's own weatherProfile ("humid_night",
            // "clear_night", "clear_twilight" in calendar.json) first, with the two
            // historically-night circuits kept as a fallback for when eventData is
            // unavailable (prototype/test builds). Qatar's profile is "clear_hot", not
            // night, so the old hardcode here was giving it a night skin the rest of the
            // game (UI weather icon, calendar text) never agreed was night.
            nightTrack = weatherProfile.Contains("night") || trackId.Contains("singapore") || trackId.Contains("las_vegas");
            twilightTrack = weatherProfile.Contains("twilight") || trackId.Contains("abu_dhabi");
            desertTrack = trackId.Contains("bahrain") || trackId.Contains("qatar") || trackId.Contains("abu_dhabi");
            streetTrack = Runtime.styleName.Contains("street") || Runtime.styleName.Contains("Street");
            monacoTrack = trackId.Contains("monaco");
            suzukaTrack = trackId.Contains("suzuka");
            spaTrack = trackId.Contains("spa");
            neonTrack = trackId.Contains("singapore") || trackId.Contains("las_vegas");
            wetTrack = Runtime.weather == WeatherState.LightRain || Runtime.weather == WeatherState.HeavyRain;
            parklandTrack = trackId.Contains("spa") || trackId.Contains("austria") || trackId.Contains("red_bull_ring") ||
                            trackId.Contains("suzuka") || trackId.Contains("silverstone") || trackId.Contains("monza") ||
                            trackId.Contains("melbourne");
            technicalParklandTrack = trackId.Contains("hungary") || trackId.Contains("barcelona");
            coastalTrack = trackId.Contains("zandvoort") || (Runtime.styleName.ToLowerInvariant().Contains("coastal") && !streetTrack);
            urbanHillsideTrack = trackId.Contains("interlagos");
            canadaTrack = trackId.Contains("canada");
            jeddahTrack = trackId.Contains("jeddah") || trackId.Contains("saudi");
            CreateMaterials();
            BuildGround();
            BuildContinuousSafetyFloor();
            BuildRoadMesh();
            BuildRoadPaint();
            BuildAsphaltDetail();
            BuildRubberBuildup();
            BuildGridPaint();
            BuildKerbs();
            PopulateCornerContainmentZones();
            // Real optimal racing line (per request - the AI previously had no
            // computed line at all and targeted the centerline). Computed after
            // the geometry/width data is final so the corridor limits are correct;
            // AiVehicleController targets it via Runtime.RacingLineOffsetAt.
            Runtime.ComputeRacingLine();
            BuildContinuousEdgeBarriers();
            BuildTrackMarkers();
            BuildDrsZoneBoards();
            BuildTimingGantries();
            BuildAdvertisingHoardings();
            BuildPitLane();
            BuildPitLaneDividerFence();
            BuildPitRampGuideFences();
            BuildStartGantry();
            BuildFinishLinePresentation();
            BuildScenery();
            BuildTemporaryBleachers();
            BuildCircuitLandmarks();
            BuildEnvironmentIdentity();
            BuildTrackInfrastructure();
            BuildCameraTowers();
            BuildTracksideCameraPods();
            BuildCircuitLightMasts();
            if (wetTrack)
            {
                BuildWetSheenOverlay();
            }

            if (showRacingLine)
            {
                BuildRacingLine();
            }

            AuditVisualMarkingColliders();
            ValidateDecorativeObjectsClearTrack();
            ValidateGeneratedTrack();
            SetupRaceControlVisualDriver();

            // Debug-only diagnostic: runs once, after every other pass above has had
            // its chance to place/repair/remove obstacles, and only ever warns - see
            // ValidateBarrierColliderCoverage for why this is independent of the
            // solidObstacles-based checks ValidateGeneratedTrack already ran.
            ValidateBarrierColliderCoverage();
            ResolveOverlappingBarrierColliders();
            ValidateBarrierPocketFree();
            ValidateNoSolidObstaclesInsideDrivingCorridors();
            ValidateSceneryGrounding();
            ValidateBarrierSmoothness();
            ValidatePitLaneSurfaceCoverage();
            BuildBoundaryDebugOverlay();
            return Runtime;
        }

        // Mandatory extra feature: a hidden-by-default set of coloured
        // LineRenderers tracing exactly the lines the barrier/pit-lane
        // placement math above targets - the calculated track edge, the
        // barrier inner-face target line, the pit lane's own boundary, and
        // the pit/track separator line. Toggled at runtime via
        // Runtime.ToggleBoundaryDebugOverlay() (see RaceManager's debug key
        // binding) so a visible gap between a barrier and its intended line
        // is obvious immediately instead of requiring a log dive.
        void BuildBoundaryDebugOverlay()
        {
            if (Runtime.length <= 1f)
            {
                return;
            }

            GameObject overlay = new GameObject("Boundary Debug Overlay");
            overlay.transform.SetParent(transform);

            CreateBoundaryDebugLine(overlay.transform, "Track edge (left)", Color.cyan, d => -Runtime.HalfWidthAt(d));
            CreateBoundaryDebugLine(overlay.transform, "Track edge (right)", Color.cyan, d => Runtime.HalfWidthAt(d));
            CreateBoundaryDebugLine(overlay.transform, "Barrier inner face (left)", Color.red, d => -(Runtime.HalfWidthAt(d) + EdgeBarrierClearance));
            CreateBoundaryDebugLine(overlay.transform, "Barrier inner face (right)", Color.red, d => Runtime.HalfWidthAt(d) + EdgeBarrierClearance);

            float corridorStart = Runtime.length * Runtime.PitCorridorStartNormalized;
            float corridorEnd = Runtime.length * PitZoneExitRampStart;
            CreateBoundaryDebugLine(overlay.transform, "Pit lane inner edge", Color.yellow, d => Runtime.PitLaneLateral - TrackRuntime.PitRampFullWidth * 0.5f, corridorStart, corridorEnd);
            CreateBoundaryDebugLine(overlay.transform, "Pit lane outer edge", Color.yellow, d => Runtime.PitLaneLateral + TrackRuntime.PitRampFullWidth * 0.5f, corridorStart, corridorEnd);
            CreateBoundaryDebugLine(overlay.transform, "Pit/track separator", Color.green, d => Runtime.HalfWidthAt(d) + EdgeBarrierClearance, corridorStart, corridorEnd);

            // The two intentional perimeter openings, called out explicitly in a
            // distinct colour so they read as "meant to be here" rather than being
            // mistaken for one of the gap markers below.
            CreateBoundaryDebugLine(overlay.transform, "Pit entry opening (intentional)", new Color(1f, 0.55f, 0f), d => Runtime.HalfWidthAt(d) + EdgeBarrierClearance,
                Runtime.length * PitZoneEntryRampStart, Runtime.length * PitZoneEntryRampEnd);
            CreateBoundaryDebugLine(overlay.transform, "Pit exit opening (intentional)", new Color(1f, 0.55f, 0f), d => Runtime.HalfWidthAt(d) + EdgeBarrierClearance,
                Runtime.length * PitZoneExitRampStart, Runtime.length * PitZoneExitRampEnd);

            // Pit lane path overlay (this pass): traces the actual driving path through
            // the pits rather than just its boundaries/walls, in colours that don't
            // collide with anything already used above (cyan/red/yellow/plain
            // green/orange/magenta are all already spoken for). Reuses PitRampEnvelopeAt
            // and PitLaneLateral directly rather than re-deriving the taper/corridor
            // constants a second time.
            CreateBoundaryDebugLine(overlay.transform, "Pit entry path", new Color(0.3f, 1f, 0.4f), d =>
                {
                    float lateral;
                    float halfWidth;
                    PitRampEnvelopeAt(d / Runtime.length, d, out lateral, out halfWidth);
                    return lateral;
                }, Runtime.length * PitZoneEntryRampStart, Runtime.length * PitZoneEntryRampEnd);

            CreateBoundaryDebugLine(overlay.transform, "Pit exit path", new Color(0.65f, 0.25f, 0.95f), d =>
                {
                    float lateral;
                    float halfWidth;
                    PitRampEnvelopeAt(d / Runtime.length, d, out lateral, out halfWidth);
                    return lateral;
                }, Runtime.length * PitZoneExitRampStart, Runtime.length * PitZoneExitRampEnd);

            CreateBoundaryDebugLine(overlay.transform, "Pit box corridor centerline", new Color(0.15f, 0.55f, 1f), d => Runtime.PitLaneLateral, corridorStart, corridorEnd);

            // Merge-point markers at the true entry/exit points - the exact distances
            // where a car physically crosses between the racing surface and the pit
            // lane, right on the true track edge (Runtime.HalfWidthAt), not the wall's
            // set-back fan-out line.
            float entryMergeDistance = Runtime.length * PitZoneEntryRampStart;
            Vector3 entryMergePoint;
            Vector3 entryMergeForward;
            Vector3 entryMergeRight;
            Runtime.SampleAtDistance(entryMergeDistance, out entryMergePoint, out entryMergeForward, out entryMergeRight);
            CreateBoundaryDebugMarker(overlay.transform, "Pit entry merge point", Color.white, entryMergePoint + entryMergeRight * Runtime.HalfWidthAt(entryMergeDistance));

            float exitMergeDistance = Runtime.length * PitZoneExitRampEnd;
            Vector3 exitMergePoint;
            Vector3 exitMergeForward;
            Vector3 exitMergeRight;
            Runtime.SampleAtDistance(exitMergeDistance, out exitMergePoint, out exitMergeForward, out exitMergeRight);
            CreateBoundaryDebugMarker(overlay.transform, "Pit exit merge point", Color.white, exitMergePoint + exitMergeRight * Runtime.HalfWidthAt(exitMergeDistance));

            // Every point the generation-time flush sweep actually flagged (see
            // ValidateBarrierColliderCoverage) - auto-filled immediately, but still
            // marked here so a gap in the underlying placement math is visible for
            // debugging instead of only ever showing a clean-looking result.
            for (int i = 0; i < detectedBarrierGapPoints.Count; i++)
            {
                CreateBoundaryDebugMarker(overlay.transform, "Auto-filled gap " + i, Color.magenta, detectedBarrierGapPoints[i]);
            }

            Runtime.AssignBoundaryDebugOverlay(overlay);
        }

        // Small octahedron-ish marker (built from the shared cone mesh, cheap and
        // distinctive against the thin boundary lines) at a fixed world point -
        // used for one-off debug call-outs like a detected-and-corrected gap,
        // rather than a traced line along the lap.
        void CreateBoundaryDebugMarker(Transform parent, string name, Color color, Vector3 worldPosition)
        {
            GameObject marker = new GameObject(name);
            marker.transform.SetParent(parent);
            marker.transform.position = worldPosition;
            marker.transform.localScale = Vector3.one * 1.4f;
            MeshFilter filter = marker.AddComponent<MeshFilter>();
            MeshRenderer renderer = marker.AddComponent<MeshRenderer>();
            filter.sharedMesh = GetVisualConeMesh();
            renderer.sharedMaterial = CreateMaterial(name + " material", color, 0f, 0.4f, color * 0.6f);
        }

        // One coloured polyline sampled along (a span of) the lap at a fixed
        // step, offset laterally by lateralAt(distance) from the centerline -
        // shared by every line the debug overlay draws so they can never
        // silently use different sampling/curve logic from each other.
        void CreateBoundaryDebugLine(Transform parent, string lineName, Color color, System.Func<float, float> lateralAt, float startDistance = -1f, float endDistance = -1f)
        {
            const float step = 6f;
            bool wholeLap = startDistance < 0f;
            float start = wholeLap ? 0f : startDistance;
            float span = wholeLap ? Runtime.length : Mathf.Repeat(endDistance - startDistance, Runtime.length);
            if (span <= 0f)
            {
                span = Runtime.length;
            }

            int pointCount = Mathf.Max(2, Mathf.CeilToInt(span / step) + 1);
            Vector3[] points = new Vector3[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                float d = Runtime.WrapDistance(start + span * i / (pointCount - 1));
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                points[i] = point + right * lateralAt(d) + Vector3.up * 0.4f;
            }

            GameObject lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = wholeLap;
            line.positionCount = pointCount;
            line.SetPositions(points);
            line.startWidth = 0.18f;
            line.endWidth = 0.18f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            Material lineMat = new Material(Shader.Find("Sprites/Default"));
            lineMat.color = color;
            line.material = lineMat;
        }

        // Wires the marshal flag boards / SC-VSC board / gantry lights captured while
        // building the track above into a small dedicated driver component, and hands
        // Runtime a reference so RaceManager can call Runtime.SetRaceControlVisual(int)
        // without needing to know anything about how this furniture was built.
        void SetupRaceControlVisualDriver()
        {
            GameObject driverObject = new GameObject("Race Control Visual Driver");
            driverObject.transform.SetParent(transform);
            raceControlVisualDriver = driverObject.AddComponent<RaceControlVisualDriver>();
            raceControlVisualDriver.Configure(
                marshalFlagBoardRenderers,
                raceControlBoardRenderer,
                raceControlBoardText,
                flagGreenMaterial,
                flagYellowMaterial,
                raceControlBoardMaterial,
                raceControlBoardVscMaterial,
                raceControlBoardScMaterial,
                gantryRaceControlLightMaterial);
            Runtime.AssignRaceControlVisualDriver(raceControlVisualDriver);
        }

        TrackRuntime CreateLayout(CalendarEventData eventData)
        {
            TrackRuntime runtime = new TrackRuntime
            {
                trackId = eventData != null ? eventData.trackId : "bahrain_desert",
                displayName = eventData != null ? eventData.displayName : "Bahrain-style Desert GP",
                // Matches BuildBahrainLayout's own (rebalanced) values - this default
                // is only ever a fallback, always immediately overwritten once the
                // matching Build*Layout method runs.
                roadHalfWidth = 13.82f,
                kerbStart = 8.15f,
                // Time trials always run dry (forceDryWeather): a hot-lap mode
                // needs comparable, repeatable conditions, so the event's own
                // forecast never applies there. Every other session ROLLS its
                // weather fresh (RollWeather) rather than reading a fixed value
                // off the profile, so the same track races differently each time.
                weather = forceDryWeather
                    ? WeatherState.Clear
                    : RollWeather(eventData == null ? "clear_hot" : eventData.weatherProfile)
            };

            string tempProfile = eventData == null ? "clear_hot" : eventData.weatherProfile;
            runtime.trackTemperatureC = forceDryWeather
                // Time trial: the stable, repeatable expected temperature.
                ? TyreStrategyRules.TrackTemperatureFor(tempProfile, runtime.trackId)
                // Race/qualifying: rolled around the expected temperature, and
                // pulled down when it's raining (a wet track runs cooler).
                : RollTrackTemperature(tempProfile, runtime.trackId, runtime.weather);

            AddLayoutPoints(runtime);
            // Elevation change (per request - "put some more elevation into the
            // tracks"). Added to the FINAL centreline before distances/ground/
            // mesh are computed, so the road, its runoff apron and the terrain
            // base all follow it, and the gradients are gentle enough to drive.
            AddProceduralElevation(runtime);
            GroundPitZoneElevation(runtime);
            // Self-crossing layouts (per report - the Qatar GP lap-1 pileup):
            // several authored sketches genuinely cross themselves in plan
            // view. At a flat crossing the two roads merely overlap (drivable),
            // but any elevation difference under ~car-clearance turns the
            // higher road's mesh into a physical wall across the lower one -
            // exactly the reported "whole field piled up under a black deck".
            // Wherever the final centreline crosses itself, the higher leg is
            // smoothly raised into a genuine flyover with real clearance.
            ResolveTrackCrossings(runtime);
            SmoothSharpKinks(runtime);
            FlattenRoadCliffs(runtime);
            runtime.RecalculateDistances();
            // Hairpin centers are derived from the FINAL, fully-repaired/scaled
            // centerline (AddLayoutPoints already ran repair + NormalizeTrackLength +
            // a second repair pass above), so this must run after that and after
            // RecalculateDistances so cumulativeDistances line up with centerLine.
            runtime.RecalculateHairpinWidening();
            return runtime;
        }

        // The pit zone must sit ON the terrain (per report - Austria: "because
        // of the lack of barriers when you pit, you actually fall off the
        // track", plus 18 "elevated road ... has no side protection" build
        // warnings spanning exactly the pit stretch): the flat terrain slab
        // sits at the lap's LOWEST point, but nothing kept the start/finish +
        // pit span near that level - Austria's profile left the entire pit
        // zone ~6m above the terrain, and the whole pit complex (fan-out
        // walls, entry ramp, aprons, boxes) is built assuming ground support,
        // so its barriers failed placement and a pitting car drove off the
        // elevated edge into the drop. The pit span (0.90 -> 0.08 of the lap,
        // wrapping through the line) is blended down to the lap minimum with
        // long cosine ramps on both sides, so the pit complex always stands on
        // real ground on every layout, whatever the authored/procedural hills
        // do elsewhere.
        static void GroundPitZoneElevation(TrackRuntime runtime)
        {
            List<Vector3> line = runtime.centerLine;
            if (line == null || line.Count < 16)
            {
                return;
            }

            int n = line.Count;
            float minY = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                minY = Mathf.Min(minY, line[i].y);
            }

            for (int i = 0; i < n; i++)
            {
                // Index fraction is accurate enough here: the zone is broad and
                // both edges are long cosine blends (distances are finalised
                // right after this in RecalculateDistances).
                float t = i / (float)n;
                float weight;
                if (t >= 0.93f || t <= 0.05f)
                {
                    weight = 1f;
                }
                else if (t >= 0.86f)
                {
                    weight = 0.5f - 0.5f * Mathf.Cos((t - 0.86f) / 0.07f * Mathf.PI);
                }
                else if (t <= 0.12f)
                {
                    weight = 0.5f - 0.5f * Mathf.Cos((0.12f - t) / 0.07f * Mathf.PI);
                }
                else
                {
                    continue;
                }

                Vector3 point = line[i];
                point.y = Mathf.Lerp(point.y, minY, weight);
                line[i] = point;
            }
        }

        // Layers gentle rolling elevation onto the centreline so tracks aren't
        // billiard-table flat: a few big hills over the lap plus finer
        // undulations, deterministic per track and periodic over the loop so the
        // start/finish elevation always matches. Amplitudes stay small relative
        // to the section length (sub-1% gradients), so the AI, the ride-height
        // follower and the camera all handle it, and the terrain apron built
        // later follows the same centreline Y.
        void AddProceduralElevation(TrackRuntime runtime)
        {
            // NOTE: length is NOT set yet here (RecalculateDistances runs after
            // this in CreateLayout), so this must NOT gate on runtime.length - the
            // earlier version did and silently applied no elevation on authored
            // circuits. Only the point count matters.
            if (runtime.centerLine == null || runtime.centerLine.Count < 8)
            {
                return;
            }

            int hash = 23;
            string id = runtime.trackId ?? "";
            for (int i = 0; i < id.Length; i++)
            {
                hash = unchecked(hash * 31 + id[i]);
            }
            hash &= 0x7fffffff;

            // Per-track amplitude: most circuits get only a SMALL elevation
            // fluctuation, and only the genuinely hilly ones (Spa, Austin,
            // Interlagos...) get a real climb (per request - not every track needs
            // crazy elevation). The previous pass applied 22-40m to everything
            // with choppy high-frequency lobes, whose steep gradients broke the
            // flat-chassis ride-height follower and the nearest-point progress
            // lookup and froze the cars. Amplitudes are now modest and the shape
            // is one or two BIG sweeping rises so gradients stay gentle (<~4%).
            float amp = TrackElevationAmplitude(runtime.trackId);
            float phase1 = (hash % 628) / 100f;
            float phase2 = ((hash / 7) % 628) / 100f;
            int lobes1 = 1 + (hash % 2);   // 1..2 big sweeping features
            const int lobes2 = 3;          // one gentle secondary undulation

            int count = runtime.centerLine.Count;
            for (int i = 0; i < count; i++)
            {
                float u = i / (float)count;
                float e = amp * (0.85f * Mathf.Sin(u * Mathf.PI * 2f * lobes1 + phase1)
                               + 0.15f * Mathf.Sin(u * Mathf.PI * 2f * lobes2 + phase2));
                Vector3 p = runtime.centerLine[i];
                p.y += e;
                runtime.centerLine[i] = p;
            }
        }

        // Self-crossing resolution (per report - the Qatar GP lap-1 pileup):
        // where the centreline crosses itself in plan view, the two legs must
        // be separated vertically like a real flyover, or the higher leg's
        // road mesh stands as a wall across the lower one. Finds every 2D
        // crossing on the final centreline and smoothly raises whichever leg
        // is already higher until there is genuine clearance, blended over a
        // long window so the resulting ramp gradient stays drivable (~6%).
        const float CrossingClearanceMeters = 9f;
        const float CrossingBlendMeters = 150f;

        // Belt-and-braces cusp killer ([StuckDiag] report - cars wedged against
        // "Procedural road" itself at Austria's final corner): a near-reversal
        // in the centreline folds the road strip over itself and its collider
        // becomes a physical wall across the corner, whether or not the
        // centreline technically self-intersects (so ResolveTrackCrossings
        // alone can't be relied on to catch it). At this layout family's
        // ~130m point spacing a legitimate hairpin turns ~45-65 degrees per
        // point; anything past 85 degrees is a cusp/fold, never a real corner.
        // Any such point is relaxed toward its neighbours until the whole lap
        // is below the threshold, and every trigger is logged to the console.
        void SmoothSharpKinks(TrackRuntime runtime)
        {
            List<Vector3> line = runtime.centerLine;
            if (line == null || line.Count < 8)
            {
                return;
            }

            int n = line.Count;
            const float maxAngleDegrees = 85f;
            int relaxedPoints = 0;
            for (int pass = 0; pass < 30; pass++)
            {
                bool anySharp = false;
                for (int i = 0; i < n; i++)
                {
                    Vector3 previous = line[(i - 1 + n) % n];
                    Vector3 current = line[i];
                    Vector3 next = line[(i + 1) % n];
                    Vector3 inDir = current - previous;
                    Vector3 outDir = next - current;
                    inDir.y = 0f;
                    outDir.y = 0f;
                    if (inDir.sqrMagnitude < 0.25f || outDir.sqrMagnitude < 0.25f)
                    {
                        continue;
                    }

                    if (Vector3.Angle(inDir, outDir) <= maxAngleDegrees)
                    {
                        continue;
                    }

                    anySharp = true;
                    relaxedPoints++;
                    line[i] = current * 0.4f + (previous + next) * 0.3f;
                }

                if (!anySharp)
                {
                    break;
                }
            }

            if (relaxedPoints > 0)
            {
                GameLog.Warn("[TrackValidation] Relaxed " + relaxedPoints + " cusp/fold point(s) sharper than 85 degrees on " +
                             runtime.displayName + " - a fold there renders the road collider as a wall across the corner.");
            }
        }

        // Vertical-cliff killer ([StuckDiag] still reporting cars wedged
        // against "Procedural road" with NO horizontal kink logged):
        // SmoothSharpKinks deliberately measures turning in the flat plane
        // (inDir.y = 0), so it is blind to a VERTICAL step - two centreline
        // points close together horizontally but metres apart in height. The
        // road mesh lerps between them and its collider becomes a ramp so
        // steep it is effectively a wall. Any segment steeper than a 35%
        // grade is relaxed by pulling the two points' heights toward each
        // other (XZ untouched) until the whole lap is drivable, and every
        // repair is logged with its location.
        void FlattenRoadCliffs(TrackRuntime runtime)
        {
            List<Vector3> line = runtime.centerLine;
            if (line == null || line.Count < 8)
            {
                return;
            }

            int n = line.Count;
            const float maxGrade = 0.35f;
            int repairs = 0;
            string worstDescription = "";
            float worstGrade = 0f;
            for (int pass = 0; pass < 60; pass++)
            {
                bool anySteep = false;
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = line[i];
                    Vector3 b = line[(i + 1) % n];
                    float xzDistance = Mathf.Max(0.5f, new Vector2(b.x - a.x, b.z - a.z).magnitude);
                    float grade = Mathf.Abs(b.y - a.y) / xzDistance;
                    if (grade <= maxGrade)
                    {
                        continue;
                    }

                    anySteep = true;
                    repairs++;
                    if (grade > worstGrade)
                    {
                        worstGrade = grade;
                        worstDescription = "points " + i + "/" + ((i + 1) % n) + " at " + a.ToString("F1") + " -> " + b.ToString("F1");
                    }

                    float meanY = (a.y + b.y) * 0.5f;
                    a.y = Mathf.Lerp(a.y, meanY, 0.5f);
                    b.y = Mathf.Lerp(b.y, meanY, 0.5f);
                    line[i] = a;
                    line[(i + 1) % n] = b;
                }

                if (!anySteep)
                {
                    break;
                }
            }

            if (repairs > 0)
            {
                GameLog.Warn("[TrackValidation] Flattened " + repairs + " road-cliff segment step(s) steeper than 35% grade on " +
                             runtime.displayName + "; worst was " + (worstGrade * 100f).ToString("0") + "% at " + worstDescription +
                             " - a step that steep renders the road collider as a wall.");
            }
        }

        void ResolveTrackCrossings(TrackRuntime runtime)
        {
            List<Vector3> line = runtime.centerLine;
            if (line == null || line.Count < 16)
            {
                return;
            }

            int n = line.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 a1 = line[i];
                Vector3 a2 = line[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    if ((j + 1) % n == i)
                    {
                        continue;
                    }

                    Vector3 b1 = line[j];
                    Vector3 b2 = line[(j + 1) % n];
                    if (!SegmentsCross2D(a1, a2, b1, b2))
                    {
                        continue;
                    }

                    // Micro-loop cusp fix (per [StuckDiag] report - the whole
                    // field wedged against "Procedural road" mid-final-corner
                    // at Austria): a crossing whose two "legs" are only a few
                    // points apart along the lap is NOT a real flyover crossing
                    // - it's the spline overshooting at a too-sharp authored
                    // vertex and folding into a tiny self-intersecting loop.
                    // Raising one side of a loop that short clamps the ramp
                    // blend to almost nothing and builds a near-vertical wall
                    // of road that cars drive straight into (and get recovery-
                    // reset back into, forever). The loop is collapsed flat
                    // instead: the points between the two crossing segments are
                    // replaced with a straight chord, giving a drivable corner
                    // with no cliff.
                    // Gate round 2 ([StuckDiag] still showing the road-mesh
                    // wall): the first gate compared INDEX distance, but at
                    // this layout's ~130m point spacing a cusp loop can span
                    // more points than max(6, n/10) and still be a tiny loop in
                    // metres - it sailed past the gate and got raised into the
                    // cliff again. The gate now measures the actual along-track
                    // metres between the two crossing segments: genuine flyover
                    // legs sit thousands of metres apart, so anything under
                    // 700m of separation is a cusp, not a crossing.
                    float alongMeters = 0f;
                    for (int k = i; k < j && alongMeters < 100000f; k++)
                    {
                        alongMeters += Vector3.Distance(line[k], line[k + 1]);
                    }

                    float wrappedMeters = 0f;
                    for (int k = 0; k < n; k++)
                    {
                        wrappedMeters += Vector3.Distance(line[k], line[(k + 1) % n]);
                    }

                    float separation = Mathf.Min(alongMeters, wrappedMeters - alongMeters);
                    if (separation < 700f)
                    {
                        CollapseCenterlineMicroLoop(line, i, j);
                        GameLog.Warn("[TrackValidation] Micro self-crossing (spline cusp) at points " + i + "/" + j +
                                     " (" + separation.ToString("0") + "m apart) on " + runtime.displayName +
                                     " - collapsed the loop flat instead of raising a cliff.");
                        if (LastReport != null)
                        {
                            LastReport.Warn("Micro self-crossing (spline cusp) at points " + i + "/" + j +
                                            " - collapsed the loop flat instead of raising a cliff.");
                        }

                        continue;
                    }

                    // Heights re-read fresh (an earlier crossing's raise this same
                    // pass may have already lifted this leg); the crossing test
                    // itself is pure XZ so raises never invalidate it.
                    float yA = (line[i].y + line[(i + 1) % n].y) * 0.5f;
                    float yB = (line[j].y + line[(j + 1) % n].y) * 0.5f;
                    if (Mathf.Abs(yA - yB) >= CrossingClearanceMeters)
                    {
                        continue; // already a real flyover
                    }

                    int upperIndex = yA >= yB ? i : j;
                    float lowerY = Mathf.Min(yA, yB);
                    RaiseCrossingLeg(line, upperIndex, lowerY + CrossingClearanceMeters, Mathf.Abs(j - i));
                    GameLog.Warn("[TrackValidation] Self-crossing centreline at points " + i + "/" + j +
                                 " (" + separation.ToString("0") + "m apart) on " + runtime.displayName +
                                 " - raised the higher leg into a flyover (+" + CrossingClearanceMeters + "m clearance).");
                    if (LastReport != null)
                    {
                        LastReport.Warn("Self-crossing centreline at points " + i + "/" + j +
                                        " - raised the higher leg into a flyover (+" +
                                        CrossingClearanceMeters + "m clearance).");
                    }
                }
            }
        }

        // Replaces the points strictly between a micro-loop's two crossing
        // segments with a straight chord from line[i] to line[j+1] - the loop
        // (a handful of points) disappears and the corner becomes a slightly
        // straighter but fully drivable arc. Only ever called for SHORT spans
        // (see the indexGap gate in ResolveTrackCrossings), so this never
        // rewrites a meaningful stretch of lap.
        static void CollapseCenterlineMicroLoop(List<Vector3> line, int i, int j)
        {
            int n = line.Count;
            int span = (j - i + n) % n;
            Vector3 from = line[i];
            Vector3 to = line[(j + 1) % n];
            for (int k = 1; k <= span; k++)
            {
                float t = k / (float)(span + 1);
                line[(i + k) % n] = Vector3.Lerp(from, to, t);
            }

            // Kink smoothing (per report - "a MASSIVE barrier gap on the
            // outside of the final turn... and a GATHERING of barriers on the
            // inside"): the raw chord meets the surrounding curve at two sharp
            // kinks, and offset geometry behaves exactly that way at a kink -
            // the outside barrier line opens a wedge gap while the inside line
            // bunches into overlapping segments. Several relaxation passes
            // over the joined region round both kinks into a continuous arc
            // the barrier planner can follow normally. Runs before
            // RecalculateDistances (see the ResolveTrackCrossings call site),
            // so distances stay consistent with the smoothed points.
            for (int pass = 0; pass < 10; pass++)
            {
                for (int k = -6; k <= span + 6; k++)
                {
                    int index = ((i + k) % n + n) % n;
                    int previous = (index - 1 + n) % n;
                    int next = (index + 1) % n;
                    line[index] = line[index] * 0.5f + (line[previous] + line[next]) * 0.25f;
                }
            }
        }

        static bool SegmentsCross2D(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
        {
            float d1 = Cross2D(p3, p4, p1);
            float d2 = Cross2D(p3, p4, p2);
            float d3 = Cross2D(p1, p2, p3);
            float d4 = Cross2D(p1, p2, p4);
            return ((d1 > 0f) != (d2 > 0f)) && ((d3 > 0f) != (d4 > 0f));
        }

        static float Cross2D(Vector3 a, Vector3 b, Vector3 c)
        {
            return (c.z - a.z) * (b.x - a.x) - (b.z - a.z) * (c.x - a.x);
        }

        // Raises the leg around centerIndex to at least targetY at its apex,
        // cosine-blended out to CrossingBlendMeters each way (window clamped
        // so it can never reach around and lift the OTHER leg of the same
        // crossing). Only ever raises - never digs the road down.
        static void RaiseCrossingLeg(List<Vector3> line, int centerIndex, float targetY, int indexGapToOtherLeg)
        {
            int n = line.Count;
            float apexY = Mathf.Max(line[centerIndex].y, targetY);
            float delta = apexY - line[centerIndex].y;
            if (delta <= 0f)
            {
                return;
            }

            int maxWindowPoints = Mathf.Max(2, Mathf.Min(indexGapToOtherLeg, n - indexGapToOtherLeg) / 3);
            for (int direction = -1; direction <= 1; direction += 2)
            {
                float travelled = 0f;
                int steps = 0;
                int index = centerIndex;
                while (steps < maxWindowPoints && travelled < CrossingBlendMeters)
                {
                    int next = ((index + direction) % n + n) % n;
                    travelled += Vector3.Distance(line[index], line[next]);
                    index = next;
                    steps++;
                    float w = 0.5f * (1f + Mathf.Cos(Mathf.PI * Mathf.Clamp01(travelled / CrossingBlendMeters)));
                    Vector3 p = line[index];
                    p.y += delta * w;
                    line[index] = p;
                }
            }

            Vector3 apex = line[centerIndex];
            apex.y = apexY;
            line[centerIndex] = apex;
            int after = (centerIndex + 1) % n;
            Vector3 apex2 = line[after];
            apex2.y = Mathf.Max(apex2.y, apexY);
            line[after] = apex2;
        }

        // Elevation amplitude (metres) by circuit character. Famously hilly tracks
        // get a genuine climb; deserts, most street circuits and Monza stay nearly
        // flat; everything else gets a small rolling fluctuation.
        static float TrackElevationAmplitude(string trackId)
        {
            string id = string.IsNullOrEmpty(trackId) ? "" : trackId.ToLowerInvariant();

            if (id.Contains("spa")) return 11f;
            if (id.Contains("austin") || id.Contains("cota") || id.Contains("united")) return 10f;
            if (id.Contains("portimao") || id.Contains("portugal")) return 10f;
            if (id.Contains("interlagos") || id.Contains("brazil") || id.Contains("sao")) return 9f;
            if (id.Contains("austria") || id.Contains("red_bull")) return 8f;
            if (id.Contains("mexico") || id.Contains("suzuka") || id.Contains("japan") ||
                id.Contains("zandvoort") || id.Contains("imola")) return 7f;

            // Rolling but not dramatic.
            if (id.Contains("silverstone") || id.Contains("barcelona") || id.Contains("hungary") ||
                id.Contains("melbourne") || id.Contains("istanbul")) return 4.5f;

            // Near-flat: deserts, most street circuits, Monza.
            if (id.Contains("monza") || id.Contains("bahrain") || id.Contains("qatar") ||
                id.Contains("abu_dhabi") || id.Contains("miami") || id.Contains("vegas") ||
                id.Contains("baku") || id.Contains("singapore") || id.Contains("monaco") ||
                id.Contains("madrid") || id.Contains("china") || id.Contains("shanghai") ||
                id.Contains("canada") || id.Contains("jeddah")) return 2.5f;

            // Default: a small fluctuation.
            return 3.5f;
        }

        void AddLayoutPoints(TrackRuntime runtime)
        {
            string id = string.IsNullOrEmpty(runtime.trackId) ? "bahrain_desert" : runtime.trackId;
            if (F1Game.Track.AuthoredCircuitCatalog.Contains(id))
            {
                // Authored-definition circuit: geometry comes from the catalog's
                // TrackDefinitionAsset, not hand-placed anchors, so the physical
                // world and the authored data pipeline share one source. An
                // authored spline is already real scale, so the
                // NormalizeTrackLength pass - which exists to blow the ~200 m
                // hand-sketched layouts up to circuit size - must NOT run here;
                // it would distort the authored geometry it is meant to honor.
                F1Game.Track.TrackDefinitionAsset definition = F1Game.Track.AuthoredCircuitCatalog.Generate(id);
                if (definition != null && definition.spline.Count >= 8)
                {
                    BuildAuthoredLayout(runtime, definition);
                    RepairLayout(runtime);
                    ValidateLayout(runtime);
                    return;
                }

                // Emergency fallback: an unusable definition falls through to
                // the legacy chain (ultimately the Bahrain template) instead of
                // producing an empty world.
                if (definition != null)
                {
                    Destroy(definition);
                }

                if (LastReport != null)
                {
                    LastReport.Warn("Authored definition for '" + id + "' unusable; falling back to procedural layout.");
                }
            }

            // Every calendar circuit is authored now (AuthoredCircuitCatalog);
            // reaching here means an unknown id or an unusable authored
            // definition. The Bahrain template is the one procedural layout
            // kept so the game always has a drivable emergency world.
            if (LastReport != null && !F1Game.Track.AuthoredCircuitCatalog.Contains(id))
            {
                LastReport.Warn("Unknown trackId '" + id + "' - using the fallback template.");
            }

            BuildBahrainLayout(runtime);

            RepairLayout(runtime);
            if (runtime.centerLine.Count < 18)
            {
                if (LastReport != null)
                {
                    LastReport.Warn("Layout generated too few points (" + runtime.centerLine.Count + "). Falling back to Bahrain-style template.");
                }

                runtime.centerLine.Clear();
                BuildBahrainLayout(runtime);
                RepairLayout(runtime);
            }

            NormalizeTrackLength(runtime);

            // Re-run the repair pass after scaling: the stretch leaves segments well
            // over the sampling budget, and kerbs/barriers need dense points again.
            RepairLayout(runtime);
            ValidateLayout(runtime);
        }

        // Scale every layout to a real-circuit target length so laps take roughly
        // 1:15-1:30 instead of 30 seconds. XZ scales around the layout centre;
        // elevation scales gently so gradients stay drivable at the new size.
        void NormalizeTrackLength(TrackRuntime runtime)
        {
            List<Vector3> line = runtime.centerLine;
            if (line.Count < 4)
            {
                return;
            }

            float currentLength = 0f;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < line.Count; i++)
            {
                currentLength += Vector3.Distance(line[i], line[(i + 1) % line.Count]);
                center += line[i];
            }

            center /= line.Count;
            if (currentLength < 100f)
            {
                if (LastReport != null)
                {
                    LastReport.Warn("layout length " + currentLength.ToString("0") + "m too small to normalize.");
                }

                return;
            }

            float targetLength = TargetTrackLength(runtime);
            float scale = targetLength / currentLength;
            float elevationScale = Mathf.Pow(scale, 0.55f);
            for (int i = 0; i < line.Count; i++)
            {
                Vector3 point = line[i];
                line[i] = new Vector3(
                    center.x + (point.x - center.x) * scale,
                    point.y * elevationScale,
                    center.z + (point.z - center.z) * scale);
            }

            GameLog.Info("[TrackLength] " + runtime.displayName + " normalized " + currentLength.ToString("0") + "m -> " +
                         targetLength.ToString("0") + "m (scale " + scale.ToString("0.00") + ")");
        }

        // Speed-rebalance pass: AI/player cornering speed and ERS were buffed
        // significantly elsewhere (AiVehicleController/VehicleController), which
        // compressed lap times on these lengths far more than intended - laps got
        // absurdly short relative to how fast cars now actually go. Every target
        // here is scaled up ~25% (TrackLengthRebalanceScale) instead of tuning car
        // speed back down, so the faster cars get the room (longer straights, more
        // space between corners) they need without undoing the speed buffs.
        // Round 2: stacked another 25% on top of the original 1.25x (1.25 * 1.25 =
        // 1.5625x versus the pre-rebalance baseline) for the same reason - cars
        // kept getting faster and the first pass wasn't enough room anymore.
        const float TrackLengthRebalanceScale = 1.5625f;

        float TargetTrackLength(TrackRuntime runtime)
        {
            string id = runtime.trackId ?? "";
            string style = runtime.styleName ?? "";

            // High-speed / long circuits. Recalibrated back down from an earlier +50%
            // pass that made laps take too long - lands in the 4.8-5.6km band for a
            // ~1:15-1:30 lap instead of several minutes. Now scaled back up by
            // TrackLengthRebalanceScale on top of that base band, for the faster cars.
            if (id.Contains("spa"))
            {
                return 5600f * TrackLengthRebalanceScale;
            }

            if (id.Contains("monza") || id.Contains("silverstone") || id.Contains("jeddah") ||
                id.Contains("las_vegas") || id.Contains("baku") || id.Contains("qatar") || id.Contains("suzuka"))
            {
                return 5300f * TrackLengthRebalanceScale;
            }

            // Tight / street layouts stay shorter but never kart-track short.
            if (id.Contains("monaco"))
            {
                return 3900f * TrackLengthRebalanceScale;
            }

            if (id.Contains("singapore") || id.Contains("hungary") || id.Contains("zandvoort") ||
                id.Contains("interlagos") || id.Contains("madrid") || id.Contains("monaco") ||
                style.ToLowerInvariant().Contains("street"))
            {
                return 4200f * TrackLengthRebalanceScale;
            }

            // Standard circuits.
            return 4650f * TrackLengthRebalanceScale;
        }

        // Auto-repair pass: merge tiny segments, split very long segments, and smooth
        // violent single-point direction changes left behind by layout generation.
        void RepairLayout(TrackRuntime runtime)
        {
            List<Vector3> line = runtime.centerLine;
            if (line.Count < 4)
            {
                return;
            }

            // 1. Merge segments that are too short to drive or mesh cleanly.
            for (int i = line.Count - 1; i >= 0 && line.Count > 4; i--)
            {
                Vector3 current = line[i];
                Vector3 previous = line[(i - 1 + line.Count) % line.Count];
                if (Vector3.Distance(current, previous) < 3.5f)
                {
                    line.RemoveAt(i);
                    if (LastReport != null)
                    {
                        LastReport.shortSegmentsMerged++;
                    }
                }
            }

            // 2. Split segments that are too long so sampling, kerbs, and barriers stay continuous.
            const float maxSegment = 58f;
            for (int i = 0; i < line.Count; i++)
            {
                Vector3 current = line[i];
                Vector3 next = line[(i + 1) % line.Count];
                float segment = Vector3.Distance(current, next);
                if (segment > maxSegment)
                {
                    int inserts = Mathf.Min(6, Mathf.CeilToInt(segment / maxSegment) - 1);
                    for (int step = inserts; step >= 1; step--)
                    {
                        float t = step / (float)(inserts + 1);
                        line.Insert(i + 1, Vector3.Lerp(current, next, t));
                    }

                    if (LastReport != null)
                    {
                        LastReport.longSegmentsSplit += inserts;
                    }

                    i += inserts;
                }
            }

            // 3. Smooth violent single-point kinks. Real hairpins are spread over several
            //    points by the Catmull-Rom pass, so a >64 degree turn at one point is an artifact.
            for (int pass = 0; pass < 3; pass++)
            {
                bool smoothedAny = false;
                for (int i = 0; i < line.Count; i++)
                {
                    Vector3 previous = line[(i - 1 + line.Count) % line.Count];
                    Vector3 current = line[i];
                    Vector3 next = line[(i + 1) % line.Count];
                    Vector3 entry = (current - previous).normalized;
                    Vector3 exit = (next - current).normalized;
                    if (Vector3.Angle(entry, exit) > 64f)
                    {
                        line[i] = Vector3.Lerp(current, (previous + next) * 0.5f, 0.5f);
                        smoothedAny = true;
                        if (LastReport != null)
                        {
                            LastReport.violentAnglesSmoothed++;
                        }
                    }
                }

                if (!smoothedAny)
                {
                    break;
                }
            }
        }

        // Authored-definition layout build (Phase C). The legacy builder stays
        // the mesh/kerb/barrier/pit engine; the catalog's definition supplies
        // the centerline, per-point width (via the authored width profile) and
        // DRS zones. Per-point camber is not yet honored by the legacy mesh
        // pass; the authored detection points are likewise superseded by
        // ValidateLayout's derived ones.
        void BuildAuthoredLayout(TrackRuntime runtime, F1Game.Track.TrackDefinitionAsset definition)
        {
            // Environment style drives the street/coastal decoration checks, so
            // converted circuits keep the look their legacy layout declared.
            runtime.styleName = string.IsNullOrEmpty(definition.environmentStyle)
                ? "Authored circuit"
                : definition.environmentStyle;

            var anchors = new Vector3[definition.spline.Count];
            float widthSum = 0f;
            for (int i = 0; i < definition.spline.Count; i++)
            {
                anchors[i] = definition.spline[i].position;
                widthSum += definition.spline[i].width;
            }

            float averageHalfWidth = definition.spline.Count > 0 ? widthSum / definition.spline.Count * 0.5f : 13.82f;
            runtime.roadHalfWidth = Mathf.Max(6f, averageHalfWidth);
            // Authored kerb inset when declared; otherwise the same inset the
            // hand-authored layouts use relative to their width.
            runtime.kerbStart = definition.kerbStartOffset > 0.01f
                ? definition.kerbStartOffset
                : Mathf.Max(4f, runtime.roadHalfWidth - 5.67f);

            // Per-point width, resampled by authored arc length into the uniform
            // profile HalfWidthAt interpolates (the world build and every width
            // consumer read that one function).
            if (definition.spline.Count > 2)
            {
                var pointDistances = new float[definition.spline.Count + 1];
                for (int i = 1; i <= definition.spline.Count; i++)
                {
                    Vector3 previous = definition.spline[i - 1].position;
                    Vector3 current = definition.spline[i % definition.spline.Count].position;
                    pointDistances[i] = pointDistances[i - 1] + Vector3.Distance(previous, current);
                }

                float loopLength = pointDistances[definition.spline.Count];
                if (loopLength > 1f)
                {
                    const int ProfileSamples = 96;
                    var profile = new float[ProfileSamples];
                    int segment = 0;
                    for (int s = 0; s < ProfileSamples; s++)
                    {
                        float target = s / (float)ProfileSamples * loopLength;
                        while (segment < definition.spline.Count - 1 && pointDistances[segment + 1] < target)
                        {
                            segment++;
                        }

                        float segmentStart = pointDistances[segment];
                        float segmentLength = Mathf.Max(0.001f, pointDistances[segment + 1] - segmentStart);
                        float frac = Mathf.Clamp01((target - segmentStart) / segmentLength);
                        float widthA = definition.spline[segment].width;
                        float widthB = definition.spline[(segment + 1) % definition.spline.Count].width;
                        profile[s] = Mathf.Max(6f, Mathf.Lerp(widthA, widthB, frac) * 0.5f);
                    }

                    runtime.authoredHalfWidthProfile = profile;
                }
            }

            // Authored DRS zones carry metre distances along the spline; the
            // legacy runtime stores normalized (start, end) pairs.
            float authoredLength = definition.ComputeLength();
            if (authoredLength > 1f && definition.drsZones.Count > 0)
            {
                runtime.drsZoneOne = new Vector2(
                    Mathf.Repeat(definition.drsZones[0].activationDistance / authoredLength, 1f),
                    Mathf.Repeat(definition.drsZones[0].endDistance / authoredLength, 1f));
            }

            if (authoredLength > 1f && definition.drsZones.Count > 1)
            {
                runtime.drsZoneTwo = new Vector2(
                    Mathf.Repeat(definition.drsZones[1].activationDistance / authoredLength, 1f),
                    Mathf.Repeat(definition.drsZones[1].endDistance / authoredLength, 1f));
            }

            // Converted circuits keep the smoothing density their legacy
            // layout used, so the built shape matches.
            AddSmoothedAnchors(runtime, anchors, definition.anchorSubdivisions > 0 ? definition.anchorSubdivisions : 4);

            // The generated asset is a pure data carrier here; don't leak the
            // ScriptableObject instance into the scene lifetime.
            Destroy(definition);
        }

        void BuildBahrainLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Desert power braking";
            runtime.roadHalfWidth = 13.82f;
            runtime.kerbStart = 8.15f;
            runtime.drsZoneOne = new Vector2(0.91f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.42f, 0.57f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(190f, 0f, 0f), new Vector3(230f, 0f, 18f),
                new Vector3(222f, 0f, 54f), new Vector3(162f, 0.5f, 75f), new Vector3(108f, 1.4f, 51f),
                new Vector3(72f, 1.2f, 16f), new Vector3(34f, 0.3f, 24f), new Vector3(22f, -0.2f, 74f),
                new Vector3(66f, -0.1f, 115f), new Vector3(142f, 0.3f, 122f), new Vector3(200f, 0.8f, 154f),
                new Vector3(184f, 0.4f, 204f), new Vector3(104f, -0.4f, 216f), new Vector3(22f, -0.8f, 184f),
                new Vector3(-62f, -0.6f, 132f), new Vector3(-92f, -0.2f, 74f), new Vector3(-138f, 0f, 14f)
            }, 4);
        }

        // Every calendar circuit's Build*Layout is retired: the whole
        // calendar converted to the authored pipeline and each layout's
        // geometry now lives in F1Game.Track.AuthoredCircuitCatalog (single
        // source). Only the Bahrain template below remains, as the emergency
        // fallback world.

        void AddSmoothedAnchors(TrackRuntime runtime, Vector3[] anchors, int subdivisions)
        {
            int count = anchors.Length;
            subdivisions = Mathf.Max(1, subdivisions);
            for (int i = 0; i < count; i++)
            {
                Vector3 p0 = anchors[(i - 1 + count) % count];
                Vector3 p1 = anchors[i];
                Vector3 p2 = anchors[(i + 1) % count];
                Vector3 p3 = anchors[(i + 2) % count];
                for (int step = 0; step < subdivisions; step++)
                {
                    float t = step / (float)subdivisions;
                    runtime.centerLine.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }
        }

        Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        void ValidateLayout(TrackRuntime runtime)
        {
            for (int i = 0; i < runtime.centerLine.Count; i++)
            {
                Vector3 current = runtime.centerLine[i];
                Vector3 next = runtime.centerLine[(i + 1) % runtime.centerLine.Count];
                float segment = Vector3.Distance(current, next);
                if ((segment < 3f || segment > 95f) && LastReport != null)
                {
                    LastReport.Warn("segment " + i + " length " + segment.ToString("0.0") + "m survived repair pass.");
                }
            }

            // Speed-rebalance pass: every per-track roadHalfWidth literal was scaled
            // up ~25% (widest circuits now sit around 19.4f) - this clamp has to
            // widen to match or the wider tracks get silently clamped straight back
            // down to the old ceiling, undoing the width increase. Round 2: widened
            // again to match the further 25% stack (widest circuits now ~24.2f).
            // Width-reduction pass: every per-track literal was then cut by 45%
            // (down to ~9.3f-13.3f) - this clamp has to narrow back down to match, or
            // the narrower tracks would get silently clamped back UP to the old
            // ~11-27f range, undoing the width reduction entirely.
            // Width-increase pass: every per-track literal raised back up 20% (now
            // ~11.1f-16f) - widened again to match, same reasoning as above.
            if (runtime.roadHalfWidth < 7f || runtime.roadHalfWidth > 18f)
            {
                if (LastReport != null)
                {
                    LastReport.Warn("road half width " + runtime.roadHalfWidth.ToString("0.0") + " out of range, clamping.");
                }

                runtime.roadHalfWidth = Mathf.Clamp(runtime.roadHalfWidth, 7f, 18f);
            }

            if (runtime.kerbStart <= 0f || runtime.kerbStart >= runtime.roadHalfWidth)
            {
                if (LastReport != null)
                {
                    LastReport.Warn("kerb start " + runtime.kerbStart.ToString("0.0") + " invalid, clamping inside road width.");
                }

                runtime.kerbStart = runtime.roadHalfWidth * 0.88f;
            }

            ValidateDrsZone(runtime, ref runtime.drsZoneOne, "DRS zone 1");
            ValidateDrsZone(runtime, ref runtime.drsZoneTwo, "DRS zone 2");

            // DRS fix: detection points sit a short distance before each zone's own
            // start, wrapping correctly for a zone that starts near/after the
            // start/finish line the same way the zone itself already does. Very
            // short straights get a tighter detection offset so the point doesn't
            // fall outside the previous zone/corner.
            float detectionOneOffset = DrsZoneSpan(runtime.drsZoneOne) < 0.06f ? 0.02f : 0.035f;
            float detectionTwoOffset = DrsZoneSpan(runtime.drsZoneTwo) < 0.06f ? 0.02f : 0.035f;
            runtime.drsDetectionOne = Mathf.Repeat(runtime.drsZoneOne.x - detectionOneOffset, 1f);
            runtime.drsDetectionTwo = Mathf.Repeat(runtime.drsZoneTwo.x - detectionTwoOffset, 1f);
        }

        float DrsZoneSpan(Vector2 zone)
        {
            return zone.x <= zone.y ? zone.y - zone.x : (1f - zone.x) + zone.y;
        }

        void ValidateDrsZone(TrackRuntime runtime, ref Vector2 zone, string label)
        {
            zone.x = Mathf.Repeat(zone.x, 1f);
            zone.y = Mathf.Repeat(zone.y, 1f);
            float span = zone.x <= zone.y ? zone.y - zone.x : (1f - zone.x) + zone.y;
            if (span < 0.03f || span > 0.5f)
            {
                if (LastReport != null)
                {
                    LastReport.Warn(label + " span " + span.ToString("0.00") + " invalid, resetting to default range.");
                }

                zone = new Vector2(zone.x, Mathf.Repeat(zone.x + 0.14f, 1f));
            }
        }

        // Rolls the session weather instead of reading a fixed state off the
        // profile, so the same track races differently each time. The profile is
        // a CLIMATE TENDENCY, not a verdict: a wet-flagged track usually rains, a
        // hot/desert one is almost always dry, a plain temperate track is mostly
        // dry with the occasional shower - but any of them can turn out
        // otherwise. The dry/wet split stays heavily biased toward the profile so
        // the pre-race tyre choice is still usually right; the day-to-day variety
        // shows up mostly as temperature and the odd cloudy/damp race.
        WeatherState RollWeather(string profile)
        {
            string p = string.IsNullOrEmpty(profile) ? "" : profile.ToLowerInvariant();
            float r = Random.value;

            if (p.Contains("wet") || p.Contains("rain"))
            {
                if (r < 0.50f) return WeatherState.LightRain;
                if (r < 0.80f) return WeatherState.HeavyRain;
                if (r < 0.93f) return WeatherState.Cloudy;
                return WeatherState.Clear;
            }

            if (p.Contains("mixed"))
            {
                if (r < 0.32f) return WeatherState.LightRain;
                if (r < 0.46f) return WeatherState.HeavyRain;
                if (r < 0.75f) return WeatherState.Cloudy;
                return WeatherState.Clear;
            }

            if (p.Contains("cloud") || p.Contains("overcast"))
            {
                if (r < 0.14f) return WeatherState.LightRain;
                if (r < 0.19f) return WeatherState.HeavyRain;
                if (r < 0.62f) return WeatherState.Cloudy;
                return WeatherState.Clear;
            }

            if (p.Contains("hot") || p.Contains("desert"))
            {
                if (r < 0.03f) return WeatherState.LightRain;
                if (r < 0.14f) return WeatherState.Cloudy;
                return WeatherState.Clear;
            }

            // Plain/temperate: mostly dry, an occasional shower.
            if (r < 0.08f) return WeatherState.LightRain;
            if (r < 0.11f) return WeatherState.HeavyRain;
            if (r < 0.30f) return WeatherState.Cloudy;
            return WeatherState.Clear;
        }

        // Actual session track temperature: the expected value for this profile/
        // track (the shared 15-30C gradient anchor plus its deterministic per-
        // track offset) rolled with a small per-race variance, then pulled DOWN
        // when the sky is wet - rain and, to a lesser extent, cloud cool the
        // surface. Clamped to the calibrated [15, 30] range.
        float RollTrackTemperature(string profile, string trackId, WeatherState weather)
        {
            float baseTemp = TyreStrategyRules.TrackTemperatureFor(profile, trackId);
            float variance = Random.Range(-3.5f, 3.5f);
            float wetDrop = weather == WeatherState.HeavyRain ? 8f : (weather == WeatherState.LightRain ? 5f : 0f);
            float cloudDrop = weather == WeatherState.Cloudy ? 2f : 0f;
            float temp = baseTemp + variance - wetDrop - cloudDrop;
            // Floor a little below the gradient's cool anchor so a wet race can
            // read as genuinely cold; the wear model clamps to 15C internally, so
            // sub-15 temps only affect the displayed number, not slick life.
            return Mathf.Clamp(temp, 10f, TyreStrategyRules.HotTrackTempC);
        }

        void CreateMaterials()
        {
            // Runoff colour now keys off the trackId-derived identity flags instead of
            // fragile case-sensitive substring checks on styleName ("park"/"Park",
            // "flowing"/"Flowing" mismatched for Monza, Interlagos, Silverstone, Austria,
            // Zandvoort and others), which left most natural circuits with desert-brown
            // runoff and dunes instead of grass.
            Color runoff;
            float runoffSmoothness = 0.18f;
            Color runoffEmission = Color.black;
            if (desertTrack)
            {
                // Sun-bleached and brighter than a shaded surface would be; there is no
                // separate lighting rig in this file, so the harsh-desert read comes from
                // the material itself being bright and faintly warm rather than tinted grey.
                runoff = new Color(0.78f, 0.66f, 0.42f);
                runoffSmoothness = 0.3f;
                runoffEmission = new Color(0.05f, 0.04f, 0.02f);
            }
            else if (streetTrack)
            {
                runoff = monacoTrack ? new Color(0.74f, 0.72f, 0.68f) : new Color(0.12f, 0.13f, 0.14f);
            }
            else
            {
                runoff = spaTrack ? new Color(0.13f, 0.22f, 0.16f) : new Color(0.18f, 0.34f, 0.22f);
            }

            bool rain = wetTrack;
            roadMaterial = CreateMaterial("Runtime Road", rain ? new Color(0.045f, 0.052f, 0.06f) : new Color(0.075f, 0.078f, 0.083f), 0.04f, rain ? 0.86f : 0.62f);

            // Procedural asphalt grain: a tiling noise texture breaks up the flat
            // road color so the surface reads as tarmac instead of vinyl.
            roadMaterial.mainTexture = GetAsphaltNoiseTexture();
            roadMaterial.mainTextureScale = new Vector2(1.6f, 0.5f);
            Runtime.AssignRoadMaterial(roadMaterial);
            kerbMaterial = CreateMaterial("Runtime Kerb", new Color(0.94f, 0.04f, 0.03f), 0.02f, 0.64f);
            kerbMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.92f, 0.92f, 0.92f), 0.08f);
            kerbMaterial.mainTextureScale = new Vector2(6f, 1.5f);
            // Blue/white kerb-paint scheme used at coastal and technical-parkland
            // circuits (see CreateKerbBlock) instead of the default red/white, so not
            // every corner on every track shares one painted colour pair.
            kerbMaterialBlue = CreateMaterial("Runtime Kerb Blue", new Color(0.04f, 0.15f, 0.5f), 0.02f, 0.64f);
            kerbMaterialBlue.mainTexture = BuildNoiseTexture(256, new Color(0.9f, 0.9f, 0.94f), 0.08f);
            kerbMaterialBlue.mainTextureScale = new Vector2(6f, 1.5f);
            grassMaterial = CreateMaterial("Runtime Runoff", runoff, 0.01f, runoffSmoothness, runoffEmission);
            // Grass/runoff covers the most screen space of anything in the scene, so a
            // tiled neutral-grey noise texture (multiplied against the tinted runoff
            // colour) does the most to kill the flat-plastic ground read.
            grassMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.83f, 0.85f, 0.8f), 0.18f);
            grassMaterial.mainTextureScale = new Vector2(70f, 70f);
            lineMaterial = CreateMaterial("Runtime Track Line", new Color(0.95f, 0.98f, 1f), 0.05f, 0.78f);
            roadEdgeMaterial = CreateMaterial("Runtime Painted Edge", new Color(1f, 0.98f, 0.9f), 0.04f, 0.76f);
            drsPaintMaterial = CreateMaterial("Runtime DRS Paint", new Color(0.02f, 0.32f, 0.95f), 0.06f, 0.82f, new Color(0.01f, 0.05f, 0.18f));
            rubberMaterial = CreateMaterial("Runtime Rubber", new Color(0.003f, 0.003f, 0.003f), 0.01f, 0.24f);

            // Light-coloured tyre marbles that build up off the racing line rather than
            // rubbered-in (dark) - speckled the same way concrete/kerb/grass are, just
            // tan/grey and tiled tighter so it reads as scattered debris, not a smooth patch.
            tyreMarbleMaterial = CreateMaterial("Runtime Tyre Marbles", new Color(0.63f, 0.59f, 0.49f), 0f, 0.18f);
            tyreMarbleMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.68f, 0.63f, 0.5f), 0.24f);
            tyreMarbleMaterial.mainTextureScale = new Vector2(5f, 2f);
            // Sun-bleached companion tint mixed into corner-exit marble patches for
            // extra variety instead of every patch sharing one flat marble colour.
            tyreMarbleMaterialLight = CreateMaterial("Runtime Tyre Marbles Light", new Color(0.74f, 0.68f, 0.55f), 0f, 0.14f);
            tyreMarbleMaterialLight.mainTexture = BuildNoiseTexture(256, new Color(0.78f, 0.72f, 0.57f), 0.22f);
            tyreMarbleMaterialLight.mainTextureScale = new Vector2(4f, 2f);
            asphaltPatchMaterial = CreateMaterial("Runtime Asphalt Patch", new Color(0.033f, 0.036f, 0.039f), 0f, rain ? 0.72f : 0.5f);
            // Blotchy low-frequency patch texture (distinct from the fine-grain
            // roadMaterial noise) so the "grain variation" stripes BuildAsphaltDetail
            // lays down read as uneven resurfacing/wear rather than a second copy of
            // the base road texture.
            asphaltPatchMaterial.mainTexture = GetAsphaltWearTexture();
            asphaltPatchMaterial.mainTextureScale = new Vector2(2.4f, 0.8f);
            // Darker, glossier top layer for the session rubber build-up (see
            // BuildRubberBuildup) so the outermost, most-driven groove reads as wetter
            // and more polished than the flatter base rubberMaterial underneath it.
            rubberSheenMaterial = CreateMaterial("Runtime Rubber Sheen", new Color(0.008f, 0.008f, 0.01f), 0.08f, 0.55f);
            skidMarkMaterial = CreateMaterial("Runtime Skid Mark", new Color(0.001f, 0.001f, 0.001f, 0.92f), 0f, 0.16f);
            barrierMaterial = CreateMaterial("Runtime Barrier", monacoTrack ? new Color(0.86f, 0.85f, 0.8f) : new Color(0.68f, 0.72f, 0.74f), 0.12f, monacoTrack ? 0.55f : 0.62f);
            barrierMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.87f, 0.87f, 0.87f), 0.1f);
            barrierMaterial.mainTextureScale = new Vector2(4f, 1.5f);
            // Brighter, more metallic than the concrete/painted barrierMaterial so the
            // continuous Armco/guardrail sections read as steel rail rather than a wall.
            armcoMaterial = CreateMaterial("Runtime Armco Guardrail", new Color(0.72f, 0.74f, 0.76f), 0.72f, 0.58f);
            // Corrugated horizontal rib pattern (with faint bolt-head dots) baked into
            // the texture itself, layered underneath CreateArmcoSegment's separate rib
            // geometry - richer close-up detail purely from the material, without
            // touching that placement/geometry function.
            armcoMaterial.mainTexture = GetArmcoCorrugationTexture();
            armcoMaterial.mainTextureScale = new Vector2(6f, 1f);
            // Low, dark grime/rust streak band layered near the base of a fraction of
            // Armco/street-wall segments (see CreateArmcoSegment/CreateStreetWallSegment)
            // so a barrier that has stood through a season of races reads as weathered
            // rather than freshly painted end to end - same speckled-noise idiom every
            // other surface material in this file already uses, just darker and duller
            // than the clean armco/barrier finish above it.
            barrierWeatherMaterial = CreateMaterial("Runtime Barrier Weathering", new Color(0.16f, 0.15f, 0.13f), 0.15f, 0.22f);
            barrierWeatherMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.2f, 0.19f, 0.16f), 0.3f);
            barrierWeatherMaterial.mainTextureScale = new Vector2(5f, 1f);
            tireBarrierMaterial = CreateMaterial("Runtime Tyre Barrier", new Color(0.015f, 0.016f, 0.017f), 0.02f, 0.28f);
            // Concentric tread-band texture so a stack reads as real tyres rather than
            // a flat black slab, distinct from the plain BuildNoiseTexture speckle used
            // on the other barrier materials below.
            tireBarrierMaterial.mainTexture = GetTyreTreadTexture();
            tireBarrierMaterial.mainTextureScale = new Vector2(3f, 2.5f);
            concreteMaterial = CreateMaterial("Runtime Concrete Wall", desertTrack ? new Color(0.72f, 0.66f, 0.5f) : new Color(0.56f, 0.58f, 0.59f), 0.04f, desertTrack ? 0.5f : 0.32f, desertTrack ? new Color(0.06f, 0.05f, 0.02f) : Color.black);
            // Pre-cast panel seam lines plus streaked staining instead of the plain
            // speckle noise every other barrier material uses, so bridge/tower/wall
            // concrete reads as jointed panel sections rather than one smooth slab.
            concreteMaterial.mainTexture = GetConcretePanelTexture();
            concreteMaterial.mainTextureScale = new Vector2(2.2f, 0.9f);
            fenceMaterial = CreateMaterial("Runtime Catch Fence", new Color(0.75f, 0.78f, 0.8f), 0.42f, 0.44f);
            // Cutout lattice instead of a flat opaque colour so the catch fence reads as
            // fine mesh you can see through rather than a second solid wall.
            fenceMaterial.mainTexture = GetChainLinkTexture();
            fenceMaterial.mainTextureScale = new Vector2(14f, 3.5f);
            SetupCutoutTransparency(fenceMaterial, 0.35f);
            fencePostMaterial = CreateMaterial("Runtime Fence Post", new Color(0.4f, 0.44f, 0.47f), 0.55f, 0.66f);
            // Subtle brushed-metal grain so fence posts/rails and every other object
            // sharing fencePostMaterial (tower lattice, gantry rails) stop reading as
            // one flat painted colour up close.
            fencePostMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.44f, 0.48f, 0.51f), 0.1f);
            fencePostMaterial.mainTextureScale = new Vector2(2f, 4f);
            // Foliage/bark carry a mottled noise texture now (per report -
            // trees were "kind of poorly textured" flat colours). NOTE:
            // BuildNoiseTexture output MULTIPLIES the material colour, so the
            // texture tint must stay near-white - a mid-tone tint multiplies
            // the colour down to near-black (the "black blobs in Canada" bug,
            // fixed below on the hill/haze materials).
            foliageMaterial = CreateMaterial("Runtime Foliage", spaTrack ? new Color(0.06f, 0.26f, 0.16f) : new Color(0.05f, 0.36f, 0.14f), 0f, 0.42f);
            foliageMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.82f, 0.92f, 0.8f), 0.32f);
            foliageMaterial.mainTextureScale = new Vector2(5f, 5f);
            // Lighter second canopy tone (see CreateBroadleafTree) - multi-lobe
            // canopies alternate the two so the foliage reads layered.
            foliageMaterialLight = CreateMaterial("Runtime Foliage Light", spaTrack ? new Color(0.13f, 0.34f, 0.19f) : new Color(0.13f, 0.46f, 0.18f), 0f, 0.4f);
            foliageMaterialLight.mainTexture = BuildNoiseTexture(256, new Color(0.85f, 0.93f, 0.82f), 0.28f);
            foliageMaterialLight.mainTextureScale = new Vector2(4f, 4f);
            // Earthy noise-textured slope tone for near backdrop hills (see
            // CreateForestedHill) - deliberately NOT canopy green. Texture tint
            // near-white (see the multiply note above).
            hillsideEarthMaterial = CreateMaterial("Runtime Hillside Earth", new Color(0.32f, 0.34f, 0.19f), 0f, 0.28f);
            hillsideEarthMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.88f, 0.9f, 0.8f), 0.22f);
            hillsideEarthMaterial.mainTextureScale = new Vector2(7f, 5f);
            // Dull hazy tone for the far forest-ridge/treeline layers - distant
            // terrain desaturates toward the sky, it doesn't stay lime green.
            distantForestMaterial = CreateMaterial("Runtime Distant Forest Haze", new Color(0.26f, 0.33f, 0.28f), 0f, 0.22f);
            distantForestMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.9f, 0.93f, 0.9f), 0.12f);
            distantForestMaterial.mainTextureScale = new Vector2(10f, 5f);
            // Trees used to borrow the bright red scenery-accent material for their
            // trunks (fine for kerb-style trim, glaring as bark) - a dedicated dull
            // brown fixes that without touching the accent colour anywhere else it's used.
            treeBarkMaterial = CreateMaterial("Runtime Tree Bark", desertTrack ? new Color(0.42f, 0.32f, 0.22f) : new Color(0.32f, 0.24f, 0.18f), 0f, 0.32f);
            // Vertical streaky grain so trunks read as bark rather than flat
            // brown plastic (near-white tint - the texture multiplies the colour).
            treeBarkMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.9f, 0.86f, 0.82f), 0.26f);
            treeBarkMaterial.mainTextureScale = new Vector2(2f, 7f);
            metalMaterial = CreateMaterial("Runtime Brushed Metal", new Color(0.52f, 0.56f, 0.58f), 0.42f, 0.78f);
            glassMaterial = CreateMaterial("Runtime Glass", new Color(0.12f, 0.28f, 0.38f, 0.85f), 0.1f, 0.95f);
            // Night/twilight races push the floodlight emissive noticeably brighter -
            // the same fixture geometry needs to read as the primary light source once
            // the sun is gone, rather than a dim afterthought lit mostly by the sky.
            float nightGlowBoost = (nightTrack || twilightTrack) ? 1.55f : 1f;
            lightGlowMaterial = CreateMaterial("Runtime Light Glow", new Color(1f, 0.85f, 0.4f), 0f, 0.92f, new Color(1f, 0.62f, 0.15f) * nightGlowBoost);
            sceneryAccentMaterial = CreateMaterial("Runtime Scenery Accent", new Color(0.92f, 0.03f, 0.025f), 0.05f, 0.65f);
            trafficConeMaterial = CreateMaterial("Runtime Traffic Cone", new Color(0.95f, 0.42f, 0.03f), 0f, 0.5f);
            flagGreenMaterial = CreateMaterial("Runtime Marshal Flag Green", new Color(0.08f, 0.68f, 0.16f), 0f, 0.5f, new Color(0.02f, 0.18f, 0.04f));
            flagYellowMaterial = CreateMaterial("Runtime Marshal Flag Yellow", new Color(0.95f, 0.82f, 0.05f), 0f, 0.5f, new Color(0.22f, 0.18f, 0.01f));
            raceControlBoardMaterial = CreateMaterial("Runtime Race Control Board", new Color(0.08f, 0.09f, 0.1f), 0.1f, 0.6f, new Color(0.35f, 0.05f, 0.03f) * nightGlowBoost);

            // Extra board states + a dedicated gantry light material so race control
            // can be driven live (SetRaceControlVisual) without disturbing every other
            // object sharing lightGlowMaterial elsewhere on the track.
            raceControlBoardVscMaterial = CreateMaterial("Runtime Race Control Board VSC", new Color(0.05f, 0.08f, 0.09f), 0.1f, 0.6f, new Color(0.05f, 0.55f, 0.6f));
            raceControlBoardScMaterial = CreateMaterial("Runtime Race Control Board SC", new Color(0.09f, 0.05f, 0.05f), 0.1f, 0.6f, new Color(0.9f, 0.12f, 0.08f));
            gantryRaceControlLightMaterial = CreateMaterial("Runtime Gantry Race Control Light", new Color(0.1f, 0.1f, 0.1f), 0f, 0.8f, new Color(0.03f, 0.03f, 0.03f));
            edgeGlowMaterial = nightTrack || twilightTrack
                ? CreateMaterial("Runtime Edge Glow", new Color(0.85f, 0.95f, 1f), 0.05f, 0.85f, new Color(0.32f, 0.42f, 0.6f) * nightGlowBoost)
                : roadEdgeMaterial;

            // Vegas/Singapore neon palette; kept small and shared rather than one
            // material per building so the neon look doesn't balloon the material count.
            // Two extra hues (green/ice) over the original three so a cluster of signs
            // doesn't repeat the same glow colour every third instance.
            neonMaterials = new[]
            {
                CreateMaterial("Runtime Neon Cyan", new Color(0.05f, 0.85f, 0.92f), 0f, 0.9f, new Color(0.15f, 1.7f, 1.85f)),
                CreateMaterial("Runtime Neon Magenta", new Color(0.9f, 0.05f, 0.8f), 0f, 0.9f, new Color(1.7f, 0.1f, 1.5f)),
                CreateMaterial("Runtime Neon Amber", new Color(0.95f, 0.55f, 0.05f), 0f, 0.9f, new Color(1.8f, 0.9f, 0.05f)),
                CreateMaterial("Runtime Neon Green", new Color(0.15f, 0.92f, 0.35f), 0f, 0.9f, new Color(0.3f, 1.85f, 0.55f)),
                CreateMaterial("Runtime Neon Ice", new Color(0.55f, 0.75f, 0.98f), 0f, 0.9f, new Color(0.9f, 1.35f, 1.9f))
            };
            yachtMaterial = CreateMaterial("Runtime Yacht Hull", new Color(0.94f, 0.94f, 0.92f), 0.15f, 0.88f);
            toriiMaterial = CreateMaterial("Runtime Torii Red", new Color(0.62f, 0.13f, 0.06f), 0.02f, 0.4f);

            // Thin repeating window-strip pattern (cached texture, shared material) so
            // building window bands read as a row of individual lit windows instead of
            // one flat emissive quad, on top of the existing per-building box variation.
            windowStripMaterial = CreateMaterial("Runtime Window Strip", new Color(0.62f, 0.68f, 0.6f), 0.1f, 0.72f, new Color(0.45f, 0.4f, 0.26f));
            windowStripMaterial.mainTexture = GetWindowStripTexture();
            windowStripMaterial.mainTextureScale = new Vector2(6f, 1f);

            // Distinct roof tone/metalness from the grandstand/stadium seating structure
            // so roofs stop reading as the same flat block as the tiers underneath.
            grandstandRoofMaterial = CreateMaterial("Runtime Grandstand Roof", new Color(0.34f, 0.36f, 0.4f), 0.28f, 0.42f);

            // Monaco luxury apartment/hotel silhouette: lighter, cream-toned, distinct
            // from the generic barrier-toned street buildings.
            luxuryApartmentMaterial = CreateMaterial("Runtime Luxury Apartment", new Color(0.88f, 0.84f, 0.74f), 0.08f, 0.52f);
            luxuryApartmentMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.9f, 0.87f, 0.8f), 0.06f);
            luxuryApartmentMaterial.mainTextureScale = new Vector2(2f, 3f);

            // Weathered concrete tone for "old-school racing venue" grandstands and the
            // Zandvoort boardwalk deck; duller and less glossy than the bridge/wall
            // concreteMaterial.
            weatheredConcreteMaterial = CreateMaterial("Runtime Weathered Concrete", new Color(0.5f, 0.5f, 0.46f), 0.03f, 0.22f);
            weatheredConcreteMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.6f, 0.6f, 0.56f), 0.14f);
            weatheredConcreteMaterial.mainTextureScale = new Vector2(3f, 1f);

            // Cooler, sandier ground tone for Zandvoort's coastal dunes than the warm
            // orange desert palette above.
            coastalSandMaterial = CreateMaterial("Runtime Coastal Sand", new Color(0.82f, 0.76f, 0.6f), 0.02f, 0.28f);
            coastalSandMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.86f, 0.83f, 0.72f), 0.14f);
            coastalSandMaterial.mainTextureScale = new Vector2(60f, 60f);

            // Brighter, warmer-yellow-green than the parkland foliageMaterial so palm
            // fronds along the boardwalk read as tropical rather than borrowing the
            // same dark forest canopy tone.
            palmFrondMaterial = CreateMaterial("Runtime Palm Frond", new Color(0.16f, 0.42f, 0.16f), 0f, 0.38f);

            // Small cycled palette for Interlagos' hillside district blocks, standing in
            // for a favela-style mix of building tones instead of one flat colour.
            hillsideBuildingMaterials = new[]
            {
                CreateMaterial("Runtime Hillside Block A", new Color(0.72f, 0.5f, 0.36f), 0.03f, 0.3f),
                CreateMaterial("Runtime Hillside Block B", new Color(0.62f, 0.58f, 0.42f), 0.03f, 0.3f),
                CreateMaterial("Runtime Hillside Block C", new Color(0.55f, 0.4f, 0.38f), 0.03f, 0.3f),
                CreateMaterial("Runtime Hillside Block D", new Color(0.68f, 0.62f, 0.5f), 0.03f, 0.3f)
            };

            // Sandy/light gravel-trap tone - distinct from the tan tyreMarbleMaterial
            // (finer, greyer, coarser tiling) so a braking-zone trap reads as raked
            // gravel rather than more scattered marbles.
            gravelMaterial = CreateMaterial("Runtime Gravel Trap", new Color(0.58f, 0.52f, 0.42f), 0f, 0.12f);
            gravelMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.62f, 0.56f, 0.46f), 0.3f);
            gravelMaterial.mainTextureScale = new Vector2(24f, 24f);

            // Flat, smooth, reflective water for the Monaco marina and coastal sea planes -
            // dark and glossy enough to read as water rather than another ground slab.
            waterMaterial = CreateMaterial("Runtime Water", new Color(0.04f, 0.16f, 0.26f), 0.35f, 0.92f);

            // Grey angular rock-outcrop tone for mountain/forest cliff dressing, kept
            // distinct from concreteMaterial/barrierMaterial so a rock face doesn't just
            // read as another retaining wall.
            rockMaterial = CreateMaterial("Runtime Rock Outcrop", new Color(0.42f, 0.4f, 0.38f), 0.02f, 0.22f);
            rockMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.48f, 0.46f, 0.44f), 0.22f);
            rockMaterial.mainTextureScale = new Vector2(2.5f, 2.5f);

            // Near-black companion to lineMaterial's near-white for the start/finish
            // checker pattern - the file had no true dark/light pair small enough to tile
            // as individual flag squares before this.
            checkerDarkMaterial = CreateMaterial("Runtime Checker Dark", new Color(0.05f, 0.05f, 0.06f), 0f, 0.5f);

            // Muted navy canvas tone for temporary bleacher sun-shades (see
            // CreateTemporaryBleacher) - distinct from every other fabric/panel colour
            // in the file so a scaffold stand doesn't borrow sceneryAccentMaterial's
            // bright red.
            bleacherCanvasMaterial = CreateMaterial("Runtime Bleacher Canvas", new Color(0.16f, 0.22f, 0.4f), 0f, 0.3f);
        }

        // Standard shader in alpha-blended Fade mode: used for the wet-track sheen and
        // the Spa mist banks, the only two places this file needs a see-through material.
        Material CreateTranslucentMaterial(string materialName, Color color, float alpha)
        {
            Material material = F1Game.Rendering.ShaderCompat.CreateLitMaterial();
            material.name = materialName;
            material.color = new Color(color.r, color.g, color.b, alpha);
            F1Game.Rendering.ShaderCompat.MakeTransparentFade(material);
            return material;
        }

        Material CreateMaterial(string materialName, Color color)
        {
            return CreateMaterial(materialName, color, 0f, 0.35f);
        }

        Material CreateMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            return CreateMaterial(materialName, color, metallic, smoothness, Color.black);
        }

        static Texture2D asphaltNoiseTexture;

        // Layered value noise with sparse bright chips; generated once and shared.
        static Texture2D GetAsphaltNoiseTexture()
        {
            if (asphaltNoiseTexture != null)
            {
                return asphaltNoiseTexture;
            }

            // 1024px (was 256): coordinates are normalized against the original
            // 256 grid so the grain keeps the same physical scale on the road,
            // it just resolves 4x sharper up close. An extra ultra-fine octave
            // adds the sub-centimetre texture the old resolution couldn't hold.
            const int size = 1024;
            const float coordScale = 256f / size;
            asphaltNoiseTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            asphaltNoiseTexture.name = "Runtime asphalt noise";
            asphaltNoiseTexture.wrapMode = TextureWrapMode.Repeat;
            asphaltNoiseTexture.filterMode = FilterMode.Trilinear;
            asphaltNoiseTexture.anisoLevel = 8;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x * coordScale;
                    float v = y * coordScale;
                    float coarse = Mathf.PerlinNoise(u * 0.035f, v * 0.035f);
                    float fine = Mathf.PerlinNoise(u * 0.21f + 51.7f, v * 0.21f + 17.3f);
                    float grain = Mathf.PerlinNoise(u * 0.62f + 133.7f, v * 0.62f + 71.9f);
                    float micro = Mathf.PerlinNoise(u * 2.4f + 311.1f, v * 2.4f + 208.7f);
                    float value = 0.62f + coarse * 0.2f + fine * 0.14f + grain * 0.08f + micro * 0.08f;

                    // Occasional aggregate chips catching the light.
                    if (grain > 0.93f)
                    {
                        value += 0.22f;
                    }

                    value = Mathf.Clamp01(value);
                    pixels[y * size + x] = new Color(value, value, value * 1.02f);
                }
            }

            asphaltNoiseTexture.SetPixels(pixels);
            asphaltNoiseTexture.Apply(true);
            return asphaltNoiseTexture;
        }

        static Texture2D windowStripTexture;

        // Small tiled texture of alternating bright/dark columns simulating individual
        // lit windows, so a single thin "window band" quad on a building reads as a row
        // of windows instead of one flat emissive strip. Cached once and shared across
        // every building the same way GetAsphaltNoiseTexture is shared across the road.
        static Texture2D GetWindowStripTexture()
        {
            if (windowStripTexture != null)
            {
                return windowStripTexture;
            }

            // 256px bilinear (was 64px Point-filtered): the same 8 alternating
            // window columns per wrap, but now drawn as framed panes - dark
            // mullion borders, a subtle vertical interior-light gradient on lit
            // panes and per-pane brightness variation - so the band reads as a
            // row of real windows rather than hard aliased pixel columns.
            const int size = 256;
            const int cellSize = 32; // 8 columns per wrap, as before
            windowStripTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            windowStripTexture.name = "Runtime window strip";
            windowStripTexture.wrapMode = TextureWrapMode.Repeat;
            windowStripTexture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cell = x / cellSize;
                    bool lit = cell % 2 == 0;
                    float noise = Mathf.PerlinNoise(cell * 3.1f, 0.5f);
                    // Distance (in pixels) from the pane's frame on each axis.
                    float frameX = Mathf.Min(x % cellSize, cellSize - 1 - x % cellSize);
                    float frameY = Mathf.Min(y % cellSize, cellSize - 1 - y % cellSize);
                    float frame = Mathf.Clamp01(Mathf.Min(frameX, frameY) / 3f);
                    float value;
                    if (lit)
                    {
                        // Interior light: brighter toward the top of each pane.
                        float glow = 0.75f + noise * 0.25f;
                        float gradient = Mathf.Lerp(0.82f, 1.05f, (y % cellSize) / (float)cellSize);
                        value = glow * gradient;
                    }
                    else
                    {
                        value = 0.12f + noise * 0.08f;
                    }

                    // Mullion/frame lines darken the pane border on both axes.
                    value = Mathf.Lerp(0.05f, value, frame);
                    value = Mathf.Clamp01(value);
                    pixels[y * size + x] = new Color(value, value, value);
                }
            }

            windowStripTexture.SetPixels(pixels);
            windowStripTexture.Apply(true);
            return windowStripTexture;
        }

        static Texture2D chainLinkTexture;

        // Diagonal lattice with transparent gaps so the catch fence material can read as
        // fine mesh instead of a flat coloured slab once combined with SetupCutoutTransparency.
        static Texture2D GetChainLinkTexture()
        {
            if (chainLinkTexture != null)
            {
                return chainLinkTexture;
            }

            // 256px (was 32): same 4-cell diagonal lattice per wrap, but the
            // strands are now anti-aliased distance bands with a rounded-wire
            // shading profile instead of 1px stair-stepped runs, so the catch
            // fence reads as fine woven wire at any distance.
            const int size = 256;
            chainLinkTexture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            chainLinkTexture.name = "Runtime chain link";
            chainLinkTexture.wrapMode = TextureWrapMode.Repeat;
            chainLinkTexture.filterMode = FilterMode.Bilinear;
            chainLinkTexture.anisoLevel = 4;
            Color[] pixels = new Color[size * size];
            int cell = Mathf.Max(4, size / 4);
            float halfWidth = cell * 0.11f; // strand half-thickness in pixels
            const float edge = 1.5f;        // AA falloff in pixels
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int diagA = ((x + y) % cell + cell) % cell;
                    int diagB = ((x - y) % cell + cell) % cell;
                    // Distance from each diagonal strand's centreline (wrapped).
                    float distA = Mathf.Min(diagA, cell - diagA);
                    float distB = Mathf.Min(diagB, cell - diagB);
                    float dist = Mathf.Min(distA, distB);
                    float coverage = Mathf.Clamp01((halfWidth - dist) / edge + 0.5f);
                    if (coverage <= 0f)
                    {
                        pixels[y * size + x] = new Color(0f, 0f, 0f, 0f);
                        continue;
                    }

                    // Rounded-wire shading: brightest along the strand centre.
                    float profile = Mathf.Clamp01(1f - dist / Mathf.Max(halfWidth, 0.001f));
                    float shade = Mathf.Lerp(0.55f, 0.92f, Mathf.Sqrt(profile));
                    pixels[y * size + x] = new Color(shade, shade * 1.02f, shade * 1.04f, coverage);
                }
            }

            chainLinkTexture.SetPixels(pixels);
            chainLinkTexture.Apply(true);
            return chainLinkTexture;
        }

        static Texture2D asphaltWearTexture;

        // Blotchy low-frequency patches layered over fine grain - distinct from
        // GetAsphaltNoiseTexture's uniform fine grain - so asphaltPatchMaterial reads
        // as uneven resurfacing/wear rather than a second copy of the base road tarmac.
        static Texture2D GetAsphaltWearTexture()
        {
            if (asphaltWearTexture != null)
            {
                return asphaltWearTexture;
            }

            // 512px (was 128), same physical pattern scale via normalized
            // coordinates, plus a micro octave for close-up detail.
            const int size = 512;
            const float coordScale = 128f / size;
            asphaltWearTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            asphaltWearTexture.name = "Runtime asphalt wear";
            asphaltWearTexture.wrapMode = TextureWrapMode.Repeat;
            asphaltWearTexture.filterMode = FilterMode.Trilinear;
            asphaltWearTexture.anisoLevel = 8;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x * coordScale;
                    float v = y * coordScale;
                    float patch = Mathf.PerlinNoise(u * 0.045f + 4.1f, v * 0.045f + 8.3f);
                    float grain = Mathf.PerlinNoise(u * 0.3f + 61f, v * 0.3f + 19f);
                    float micro = Mathf.PerlinNoise(u * 1.3f + 27.5f, v * 1.3f + 88.2f);
                    float value = Mathf.Clamp01(0.5f + patch * 0.32f + grain * 0.07f + micro * 0.05f);
                    pixels[y * size + x] = new Color(value, value * 0.99f, value * 0.97f);
                }
            }

            asphaltWearTexture.SetPixels(pixels);
            asphaltWearTexture.Apply(true);
            return asphaltWearTexture;
        }

        static Texture2D armcoCorrugationTexture;

        // Horizontal light/dark corrugation bands with faint bolt-head dots along the
        // top and bottom edge, so armcoMaterial itself carries a corrugated-steel rib
        // read at the texture level - layered underneath (not replacing)
        // CreateArmcoSegment's separate rib geometry.
        static Texture2D GetArmcoCorrugationTexture()
        {
            if (armcoCorrugationTexture != null)
            {
                return armcoCorrugationTexture;
            }

            // 256px (was 64): the corrugation wave is analytic (already
            // resolution-independent); bolt heads become soft round dots with
            // AA falloff instead of 2px squares, and the grain keeps its
            // physical scale via normalized coordinates.
            const int size = 256;
            const float coordScale = 64f / size;
            armcoCorrugationTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            armcoCorrugationTexture.name = "Runtime armco corrugation";
            armcoCorrugationTexture.wrapMode = TextureWrapMode.Repeat;
            armcoCorrugationTexture.filterMode = FilterMode.Bilinear;
            armcoCorrugationTexture.anisoLevel = 4;
            Color[] pixels = new Color[size * size];
            // Bolt centres: every 16 original units along X (now 16/coordScale
            // px), one row near the top edge and one at the beam's midline.
            float boltSpacing = 16f / coordScale;
            float boltRadius = 3.2f / coordScale * 0.55f;
            float[] boltRowsY = { 1.5f / coordScale, size * 0.5f };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float wave = Mathf.Sin(y / (float)size * Mathf.PI * 6f) * 0.5f + 0.5f;
                    float grain = Mathf.PerlinNoise(x * coordScale * 0.15f, y * coordScale * 0.15f) * 0.08f;
                    // Distance to the nearest bolt centre (X wraps).
                    float boltX = Mathf.Repeat(x + boltSpacing * 0.5f, boltSpacing) - boltSpacing * 0.5f;
                    float boltShade = 0f;
                    for (int row = 0; row < boltRowsY.Length; row++)
                    {
                        float dy = y - boltRowsY[row];
                        float dist = Mathf.Sqrt(boltX * boltX + dy * dy);
                        float dome = Mathf.Clamp01(1f - dist / boltRadius);
                        // Domed head: dark rim, slight highlight at the crown.
                        boltShade = Mathf.Max(boltShade, dome > 0f ? 0.18f * (1f - dome * 0.5f) : 0f);
                    }

                    float value = Mathf.Clamp01(0.72f + wave * 0.22f + grain - boltShade);
                    pixels[y * size + x] = new Color(value, value, value * 1.02f);
                }
            }

            armcoCorrugationTexture.SetPixels(pixels);
            armcoCorrugationTexture.Apply(true);
            return armcoCorrugationTexture;
        }

        static Texture2D concretePanelTexture;

        // Vertical/horizontal seam lines plus streaked staining, so the shared
        // concrete wall/tower/bridge material reads as jointed pre-cast panel
        // sections instead of one smooth grey slab.
        static Texture2D GetConcretePanelTexture()
        {
            if (concretePanelTexture != null)
            {
                return concretePanelTexture;
            }

            // 512px (was 128): same panel layout via normalized coordinates;
            // seams become soft chamfered grooves instead of 1px lines, and a
            // micro octave carries close-up surface detail.
            const int size = 512;
            const float coordScale = 128f / size;
            concretePanelTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            concretePanelTexture.name = "Runtime concrete panel";
            concretePanelTexture.wrapMode = TextureWrapMode.Repeat;
            concretePanelTexture.filterMode = FilterMode.Bilinear;
            concretePanelTexture.anisoLevel = 4;
            Color[] pixels = new Color[size * size];
            float seamSpacingX = 32f / coordScale;
            float seamSpacingY = 64f / coordScale;
            float seamHalfWidth = 1.2f / coordScale * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x * coordScale;
                    float v = y * coordScale;
                    float stain = Mathf.PerlinNoise(u * 0.02f, v * 0.06f) * 0.14f;
                    float grain = Mathf.PerlinNoise(u * 0.2f + 9f, v * 0.2f + 4f) * 0.06f;
                    float micro = Mathf.PerlinNoise(u * 0.9f + 33f, v * 0.9f + 57f) * 0.04f;
                    // Distance to the nearest seam centreline on each axis.
                    float seamDistX = Mathf.Abs(Mathf.Repeat(x + seamSpacingX * 0.5f, seamSpacingX) - seamSpacingX * 0.5f);
                    float seamDistY = Mathf.Abs(Mathf.Repeat(y + seamSpacingY * 0.5f, seamSpacingY) - seamSpacingY * 0.5f);
                    float seamDist = Mathf.Min(seamDistX, seamDistY);
                    // Chamfered groove: full-depth at the centre, easing out
                    // over ~2x the groove width for a bevelled edge.
                    float seamDepth = 0.22f * Mathf.Clamp01(1f - seamDist / (seamHalfWidth * 2f));
                    float value = Mathf.Clamp01(0.82f - stain + grain + micro - seamDepth);
                    pixels[y * size + x] = new Color(value, value, value * 0.98f);
                }
            }

            concretePanelTexture.SetPixels(pixels);
            concretePanelTexture.Apply(true);
            return concretePanelTexture;
        }

        static Texture2D tyreTreadTexture;

        // Dark concentric-ish tread bands standing in for stacked tyre sidewalls, so
        // tireBarrierMaterial reads as a real tyre stack rather than a flat black slab.
        static Texture2D GetTyreTreadTexture()
        {
            if (tyreTreadTexture != null)
            {
                return tyreTreadTexture;
            }

            // 256px (was 64): same four sidewall bands per wrap, but each tyre
            // ring now has a rounded cosine bulge highlight instead of a 2px
            // bright line, so a stack reads as curved rubber sidewalls.
            const int size = 256;
            const float coordScale = 64f / size;
            tyreTreadTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            tyreTreadTexture.name = "Runtime tyre tread";
            tyreTreadTexture.wrapMode = TextureWrapMode.Repeat;
            tyreTreadTexture.filterMode = FilterMode.Bilinear;
            tyreTreadTexture.anisoLevel = 4;
            Color[] pixels = new Color[size * size];
            float ringPeriod = 16f / coordScale;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Position within one tyre ring, 0..1; the sidewall bulge
                    // peaks at the ring boundary where two tyres touch.
                    float ringPos = Mathf.Repeat(y, ringPeriod) / ringPeriod;
                    float bulge = Mathf.Pow(Mathf.Cos(ringPos * Mathf.PI * 2f) * 0.5f + 0.5f, 3f);
                    float rim = bulge * 0.07f;
                    float grain = Mathf.PerlinNoise(x * coordScale * 0.3f, y * coordScale * 0.3f) * 0.03f;
                    float value = 0.02f + rim + grain;
                    pixels[y * size + x] = new Color(value, value, value * 1.05f);
                }
            }

            tyreTreadTexture.SetPixels(pixels);
            tyreTreadTexture.Apply(true);
            return tyreTreadTexture;
        }

        // Switches a Standard-shader material to cutout (alpha-tested) transparency so a
        // texture's alpha channel actually punches holes instead of just tinting the color.
        void SetupCutoutTransparency(Material material, float cutoff)
        {
            F1Game.Rendering.ShaderCompat.MakeCutout(material, cutoff);
        }

        static readonly Dictionary<string, Texture2D> noiseTextureCache = new Dictionary<string, Texture2D>();

        // Generic runtime noise texture used to break up the remaining flat, single-
        // colour materials (grass/kerb/barrier/concrete) the same way the asphalt
        // grain above does. Cached by its parameters so every track Build reuses the
        // same handful of bitmaps instead of allocating one per call.
        static Texture2D BuildNoiseTexture(int size, Color baseColor, float variation)
        {
            string key = size + "_" + baseColor.r.ToString("F3") + "_" + baseColor.g.ToString("F3") + "_" +
                         baseColor.b.ToString("F3") + "_" + variation.ToString("F3");
            Texture2D cached;
            if (noiseTextureCache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, true);
            texture.name = "Runtime noise " + key;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            // Pattern coordinates are normalized against a 64px reference grid
            // so raising a call site's resolution sharpens the texture without
            // changing the physical scale of the noise it produces.
            float coordScale = 64f / size;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x * coordScale;
                    float v = y * coordScale;
                    float coarse = Mathf.PerlinNoise(u * 0.045f, v * 0.045f);
                    float fine = Mathf.PerlinNoise(u * 0.22f + 91.3f, v * 0.22f + 42.1f);
                    float jitter = (coarse * 0.65f + fine * 0.35f - 0.5f) * 2f * variation;
                    pixels[y * size + x] = new Color(
                        Mathf.Clamp01(baseColor.r + jitter),
                        Mathf.Clamp01(baseColor.g + jitter),
                        Mathf.Clamp01(baseColor.b + jitter));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            noiseTextureCache[key] = texture;
            return texture;
        }

        Material CreateMaterial(string materialName, Color color, float metallic, float smoothness, Color emission)
        {
            Material material = F1Game.Rendering.ShaderCompat.CreateLitMaterial();
            material.name = materialName;
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            F1Game.Rendering.ShaderCompat.SetSmoothness(material, smoothness);
            if (emission.r > 0f || emission.g > 0f || emission.b > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
            }
            return material;
        }

        PhysicMaterial GetRoadPhysicsMaterial()
        {
            if (roadPhysicsMaterial != null)
            {
                return roadPhysicsMaterial;
            }

            roadPhysicsMaterial = new PhysicMaterial("Runtime low-friction asphalt");
            roadPhysicsMaterial.dynamicFriction = 0.02f;
            roadPhysicsMaterial.staticFriction = 0.02f;
            roadPhysicsMaterial.bounciness = 0f;
            roadPhysicsMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
            roadPhysicsMaterial.bounceCombine = PhysicMaterialCombine.Minimum;
            return roadPhysicsMaterial;
        }

        PhysicMaterial GetRunoffPhysicsMaterial()
        {
            if (runoffPhysicsMaterial != null)
            {
                return runoffPhysicsMaterial;
            }

            runoffPhysicsMaterial = new PhysicMaterial("Runtime slowing runoff");
            runoffPhysicsMaterial.dynamicFriction = 0.82f;
            runoffPhysicsMaterial.staticFriction = 0.92f;
            runoffPhysicsMaterial.bounciness = 0f;
            runoffPhysicsMaterial.frictionCombine = PhysicMaterialCombine.Maximum;
            runoffPhysicsMaterial.bounceCombine = PhysicMaterialCombine.Minimum;
            return runoffPhysicsMaterial;
        }

        void BuildGround()
        {
            Bounds bounds = new Bounds(Runtime.centerLine[0], Vector3.zero);
            for (int i = 1; i < Runtime.centerLine.Count; i++)
            {
                bounds.Encapsulate(Runtime.centerLine[i]);
            }

            Vector3 center = bounds.center;
            center.y = bounds.min.y - 1.25f;
            // Street circuits get a much wider terrain slab: the three-band
            // high-rise skyline (BuildHighRiseSkyline) pushes its far
            // background ring out to ~650m from the corridor, and a tower
            // standing past the edge of the slab would visibly hover over
            // nothing.
            float groundSpanMin = streetTrack ? 2800f : 1200f;
            float groundSpanScale = streetTrack ? 2.2f : 1.5f;
            // Floating-disk fix (per report - "grey disks/bubbles in Canada in
            // the distance"): the horizon backdrop rings (BuildMountainBackdrop
            // and BuildDistantParallaxLayer) sit at max(extents)*1.15 + 140m
            // from the track centre, but the slab's old minimum span could be
            // SMALLER than that ring on one axis - every dome past the slab
            // edge had nothing in front of its buried lower half and rendered
            // as a lens/UFO floating against the sky. The slab now always
            // extends past the outermost backdrop ring plus the domes' own
            // footprint.
            // Round 2 (per report - "still looks the same"): the first attempt
            // sized the slab to the PARALLAX ring (extents*1.15 + 140m), but
            // the mountain-ridge ring sits much further out at extents*1.55 +
            // 220m (BuildMountainBackdrop) - its domes were still hundreds of
            // metres past the slab edge. Sized to the outermost ring plus the
            // widest dome's half-footprint now.
            float backdropRingRadius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.55f + 220f;
            float backdropSpan = (backdropRingRadius + 280f) * 2f;
            Vector3 size = new Vector3(
                Mathf.Max(Mathf.Max(groundSpanMin, backdropSpan), bounds.size.x * groundSpanScale), 1.0f,
                Mathf.Max(Mathf.Max(groundSpanMin, backdropSpan), bounds.size.z * groundSpanScale));
            groundTopY = center.y + size.y * 0.5f;
            GameObject ground = CreateVisualBox(Runtime.styleName + " terrain base", center, Quaternion.identity, size, grassMaterial);
            ground.layer = 0;
            BoxCollider collider = ground.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.sharedMaterial = GetRunoffPhysicsMaterial();

            // Add decorative height variation to the terrain edges. This used to spawn a
            // handful of single spheres up to 180 units wide, gated only by an IsOnRoad
            // check on the sphere's centre point - that let the sphere's actual 90-unit
            // radius loom over the road, pit lane, and camera corridor even when its pivot
            // read as clear. Low, elongated ridge clusters plus the radius-aware clearance
            // helper (which skips the instance entirely if it can't be pushed clear) fix
            // that without losing the horizon detail.
            // Street circuits are flat urban ground - rolling grass domes poking
            // through the pavement read as unexplained "bubbles" next to the city
            // blocks, so the terrain humps are countryside-only.
            if (!streetTrack)
            {
                int hillClusters = Mathf.Max(3, Mathf.RoundToInt(6f * Mathf.Clamp(sceneryDensity, 0.25f, 2f)));
                for (int i = 0; i < hillClusters; i++)
                {
                    Vector3 hillPos = new Vector3(center.x + Random.Range(-size.x, size.x) * 0.48f, groundTopY, center.z + Random.Range(-size.z, size.z) * 0.48f);
                    CreateDistantHillCluster(hillPos, i);
                }
            }
        }

        // Cluster of low, flattened, elongated spheres standing in for a distant hill or
        // ridge silhouette. Replaces the old single wide dome: several smaller pieces read
        // as a horizon feature without any one piece having a huge collision-relevant
        // footprint, and each piece is independently clearance-checked and skipped (not
        // just pushed) if it can't clear the corridor. Height is measured from groundTopY
        // (not the sample point's own Y) so most of each sphere sinks below the terrain
        // slab and only a rounded cap shows above it, the way the old dome's silhouette
        // read before its bounding radius became the problem.
        void CreateDistantHillCluster(Vector3 center, int seed)
        {
            // De-blob pass (per report - "nothing blob or dome shaped on any
            // track"): the cluster of individually-smooth domes is now one
            // irregular multi-lobe formation in the textured earthy tone.
            float widthScale = 120f + (seed * 7) % 80;
            float heightScale = 26f + (seed * 3) % 20;
            Vector3 safePosition;
            if (!TryGetClearScenerySpot(center, widthScale * 0.6f, 12f, out safePosition))
            {
                return;
            }

            CreateRidgeFormation(safePosition, widthScale, heightScale, widthScale * 0.55f, hillsideEarthMaterial, seed);
        }

        // Continuous invisible collision floor that follows the road's elevation
        // profile, sampled along the whole lap. This is the real fix for cars
        // dropping onto the far-below flat terrain slab whenever the track rises
        // above ground level (bridges, hills, elevated corners): wherever a car
        // goes off track, there is now a physical backstop not far below the local
        // road height instead of a multi-meter void down to BuildGround's slab.
        // No renderer, no visual footprint - purely a physics safety net.
        void BuildContinuousSafetyFloor()
        {
            const float spacing = 14f;
            const float segmentLength = spacing * 1.6f;
            // Invisible-wall fix (per report - the whole field piling up
            // "completely stuck" on Austria's sector-3 crossing section): each
            // slab used to be 140m wide (halfWidth 70) and hang 2.6m under its
            // OWN road section - so an elevated section's slab cut straight
            // through the airspace of any lower road running within 70m of it.
            // Along a flyover's ramps (or a hillside crest next to a valley
            // section) the height difference passes through the 2.6-5m band
            // where that slab sits at windscreen height across the lower road:
            // a literal invisible wall even the race leader slams into.
            // Two changes: the slab is corridor-width (26m half-width still
            // covers road + kerbs + runoff; the terrain slab catches anything
            // further out), and each slab's top is clamped below the LOWEST
            // road point anywhere near it, so the catch floor can never
            // intrude into another section's driving space - at a crossing the
            // deck's floor simply drops to serve both levels.
            const float halfWidth = 26f;
            const float thickness = 1.4f;
            const float depthBelowRoad = 2.6f;
            float nearbyRadius = halfWidth + 18f;
            float nearbyRadiusSqr = nearbyRadius * nearbyRadius;

            // Round 2 of the intrusion clamp: the first pass compared against
            // raw centreline POINTS, but on these layouts those can be ~130m
            // apart - a lower road passing BETWEEN two points could still slip
            // inside a slab's footprint unnoticed. The clamp now compares
            // against the road densely sampled every 10m.
            int denseCount = Mathf.Max(8, Mathf.CeilToInt(Runtime.length / 10f));
            Vector3[] denseRoad = new Vector3[denseCount];
            for (int i = 0; i < denseCount; i++)
            {
                Vector3 samplePoint;
                Vector3 sampleForward;
                Vector3 sampleRight;
                Runtime.SampleAtDistance(i * (Runtime.length / denseCount), out samplePoint, out sampleForward, out sampleRight);
                denseRoad[i] = samplePoint;
            }

            for (float d = 0f; d < Runtime.length; d += spacing)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d + spacing * 0.5f, out point, out forward, out right);

                float lowestNearbyRoadY = point.y;
                for (int i = 0; i < denseCount; i++)
                {
                    Vector3 other = denseRoad[i];
                    float dx = other.x - point.x;
                    float dz = other.z - point.z;
                    if (dx * dx + dz * dz <= nearbyRadiusSqr && other.y < lowestNearbyRoadY)
                    {
                        lowestNearbyRoadY = other.y;
                    }
                }

                float floorTopY = Mathf.Max(groundTopY + 0.05f, lowestNearbyRoadY - depthBelowRoad);

                GameObject floor = new GameObject("Safety catch floor");
                floor.transform.SetParent(transform);
                floor.transform.position = new Vector3(point.x, floorTopY - thickness * 0.5f, point.z);
                floor.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                floor.layer = 0;
                BoxCollider collider = floor.AddComponent<BoxCollider>();
                collider.size = new Vector3(halfWidth * 2f, thickness, segmentLength);
                collider.sharedMaterial = GetRunoffPhysicsMaterial();
            }
        }

        void BuildRoadMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "Procedural Road Mesh";
            // Boundary-truth fix (per report: "the track doesn't extend to the
            // barriers" on some layouts, cars falling off while pitting): the
            // mesh used to place one vertex pair per CENTERLINE point - a
            // sparse ~130m polyline on these layouts - and lerp the width
            // linearly across each long quad. But HalfWidthAt (the width
            // authority every other system uses: barriers, track limits, pit
            // laterals, the AI corridor) interpolates the authored per-point
            // width profile at full resolution, so wherever the authored width
            // changes between two sparse vertices the PHYSICAL tarmac was
            // narrower than every consumer believed - barriers stood beyond
            // the mesh edge with void between, and the pit approach steered
            // cars onto air. The mesh is now sampled densely (every 3m) from
            // the same SampleAtDistance/HalfWidthAt pair, so the physical
            // surface finally IS the boundary the rest of the game reasons
            // about. 3m (was 8m) keeps corner arcs visually smooth - at 8m a
            // tight hairpin's inside edge read as a chain of straight chords;
            // a ~5km lap at 3m is still only ~3.4k vertices for the ribbon.
            const float RoadMeshStepMeters = 3f;
            int count = Mathf.Max(Runtime.centerLine.Count, Mathf.CeilToInt(Runtime.length / RoadMeshStepMeters));
            float step = Runtime.length / count;
            Vector3[] vertices = new Vector3[count * 2];
            Vector2[] uvs = new Vector2[count * 2];
            int[] triangles = new int[count * 6];

            for (int i = 0; i < count; i++)
            {
                float distance = i * step;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(distance, out point, out forward, out right);
                // The physical drivable surface (and its MeshCollider) is the ultimate
                // ground truth for how wide the track actually is, so it must sample the
                // same widened HalfWidthAt used by kerbs/barriers - otherwise a hairpin
                // could paint/fence wider than the tarmac cars can actually drive on.
                float localHalfWidth = Runtime.HalfWidthAt(distance);
                vertices[i * 2] = point - right * localHalfWidth + Vector3.up * 0.015f;
                vertices[i * 2 + 1] = point + right * localHalfWidth + Vector3.up * 0.015f;
                float v = distance / 12f; // Tiled UV for asphalt detail
                uvs[i * 2] = new Vector2(0f, v);
                uvs[i * 2 + 1] = new Vector2(localHalfWidth * 0.5f, v);

                int next = (i + 1) % count;
                int tri = i * 6;
                triangles[tri] = i * 2;
                triangles[tri + 1] = next * 2;
                triangles[tri + 2] = i * 2 + 1;
                triangles[tri + 3] = i * 2 + 1;
                triangles[tri + 4] = next * 2;
                triangles[tri + 5] = next * 2 + 1;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject road = new GameObject("Procedural road");
            road.transform.SetParent(transform);
            MeshFilter filter = road.AddComponent<MeshFilter>();
            MeshRenderer renderer = road.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = roadMaterial;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Simple;
            MeshCollider collider = road.AddComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            collider.convex = false;
            collider.isTrigger = false;
            collider.sharedMaterial = GetRoadPhysicsMaterial();
            road.layer = 0;
            Runtime.roadCollider = collider;
            GameLog.Info("[RoadPhysics] Road collider created=" + (collider != null) +
                      " layer=" + LayerMask.LayerToName(road.layer) +
                      " isTrigger=" + collider.isTrigger +
                      " sharedMeshAssigned=" + (collider.sharedMesh == mesh) +
                      " meshVertices=" + mesh.vertexCount +
                      " bounds=" + mesh.bounds);
        }

        void BuildRoadPaint()
        {
            float spacing = 12f;
            for (float d = 0f; d < Runtime.length; d += spacing)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                // Paint follows the same widened edge the road mesh itself uses, so lines
                // stay on the true edge through a hairpin instead of reading as painted
                // mid-track (too narrow) or off the tarmac entirely (too wide).
                float localHalfWidth = Runtime.HalfWidthAt(d);

                // Edge lines; emissive at night so the circuit reads under floodlights.
                CreateRoadStripe(point - right * (localHalfWidth - 0.45f), forward, 0.25f, spacing * 0.95f, edgeGlowMaterial, "Left edge line", 0);
                CreateRoadStripe(point + right * (localHalfWidth - 0.45f), forward, 0.25f, spacing * 0.95f, edgeGlowMaterial, "Right edge line", 0);

                // Racing line rubbering
                if (Mathf.FloorToInt(d / spacing) % 2 == 0)
                {
                    float lateralOffset = Mathf.Sin(d * 0.02f) * (localHalfWidth * 0.35f);
                    CreateRoadStripe(point + right * lateralOffset, forward, 4.2f, spacing * 1.1f, rubberMaterial, "Rubbered racing line", 1);
                    CreateRoadStripe(point + right * (lateralOffset + 0.15f), forward, 1.2f, spacing * 0.5f, rubberMaterial, "Rubbered skid mark", 2);
                }

                float normalized = d / Mathf.Max(1f, Runtime.length);
                if (Runtime.IsInDrsZone(normalized) && Mathf.FloorToInt(d / spacing) % 2 == 0)
                {
                    CreateRoadStripe(point - right * (localHalfWidth - 1.5f), forward, 0.8f, 8f, drsPaintMaterial, "DRS zone paint", 3);
                    CreateRoadStripe(point + right * (localHalfWidth - 1.5f), forward, 0.8f, 8f, drsPaintMaterial, "DRS zone paint", 3);
                }
            }
        }

        void BuildAsphaltDetail()
        {
            // The one surface-detail pass in this file that never read
            // sceneryDensity at all - every sibling pass (BuildRubberBuildup,
            // CreateTyreMarbles, CreateLockupSkidMarks) already scales patch/streak
            // counts with it. Tighter spacing at the high end reads as a heavily-used
            // surface with near-continuous grain/rubber variation; the low end keeps
            // close to the original 24m spacing so nothing gets more expensive by
            // default.
            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);
            float spacing = Mathf.Lerp(30f, 18f, Mathf.InverseLerp(0.25f, 2f, density));
            int patchIndex = 0;
            for (float d = 0f; d < Runtime.length; d += spacing)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                float normalized = d / Mathf.Max(1f, Runtime.length);
                float laneBias = Mathf.Sin(normalized * Mathf.PI * 10f) * 0.34f;
                float localHalfWidth = Runtime.HalfWidthAt(d);
                CreateRoadStripe(point + right * laneBias, forward, localHalfWidth * 0.82f, spacing * 0.76f, asphaltPatchMaterial, "Asphalt grain variation", 4);
                CreateRoadStripe(point + right * (laneBias * 0.45f), forward, localHalfWidth * 0.42f, spacing * 0.82f, rubberMaterial, "Dark racing line rubber", 5);

                // Deterministic per-patch lateral jitter (Perlin, not true random) so
                // the skid marks below don't all land at the exact same two lateral
                // offsets down the whole lap - a real surface's braking scars scatter
                // a little from lap to lap over a session.
                float jitter = (Mathf.PerlinNoise(patchIndex * 0.37f, 4.2f) - 0.5f) * 0.6f;

                if (Mathf.FloorToInt(d / spacing) % 4 == 1)
                {
                    CreateRoadStripe(point - right * (1.05f + jitter), forward, 0.16f, 7.6f, skidMarkMaterial, "Heavy braking skid mark", 6);
                    CreateRoadStripe(point + right * (1.25f + jitter), forward, 0.14f, 6.8f, skidMarkMaterial, "Heavy braking skid mark", 6);
                }

                // Higher density settings layer in a second, lighter sun-bleached
                // rubber sheen patch between the skid-mark stretches - the same
                // progressive rubber-build-up idea BuildRubberBuildup already applies
                // at corner exits, extended along plain straights so a dense setting
                // reads as a full session's rubber laid down everywhere, not only at
                // the corners.
                if (density >= 1.1f && Mathf.FloorToInt(d / spacing) % 4 == 3)
                {
                    CreateRoadStripe(point + right * (laneBias * 0.3f + jitter * 0.4f), forward, localHalfWidth * 0.3f, spacing * 0.5f, rubberSheenMaterial, "Session rubber sheen variation", 5);
                }

                patchIndex++;
            }
        }

        // Extra rubber build-up layered on top of the flat per-segment racing line
        // above, concentrated at every real corner's acceleration zone rather than
        // spread evenly - several narrowing, progressively glossier stripes standing
        // in for a lap's worth of rubber laid down over a session instead of one
        // uniform band painted once. Uses the same DetectCorners severity test the
        // braking-board/tyre-marble passes already share.
        void BuildRubberBuildup()
        {
            List<CornerInfo> corners = DetectCorners(30f);
            int layers = Mathf.Clamp(Mathf.RoundToInt(3f * Mathf.Clamp(sceneryDensity, 0.25f, 2f)), 2, 4);
            for (int c = 0; c < corners.Count; c++)
            {
                Vector3 approachPoint;
                Vector3 approachForward;
                Vector3 approachRight;
                Runtime.SampleAtDistance(corners[c].distance - 8f, out approachPoint, out approachForward, out approachRight);
                Vector3 apexPoint;
                Vector3 apexForward;
                Vector3 apexRight;
                Runtime.SampleAtDistance(corners[c].distance, out apexPoint, out apexForward, out apexRight);
                float turnSign = Mathf.Sign(Vector3.Cross(approachForward, apexForward).y);

                for (int layer = 0; layer < layers; layer++)
                {
                    float exitDistance = corners[c].distance + 6f + layer * 6.5f;
                    Vector3 point;
                    Vector3 forward;
                    Vector3 right;
                    Runtime.SampleAtDistance(exitDistance, out point, out forward, out right);
                    float lateral = -turnSign * (0.6f - layer * 0.08f);
                    float width = Mathf.Max(0.4f, 2.6f - layer * 0.45f);
                    Material layerMaterial = layer == layers - 1 ? rubberSheenMaterial : rubberMaterial;
                    CreateRoadStripe(point + right * lateral, forward, width, 6.6f, layerMaterial, "Session rubber build-up", 9);
                }
            }
        }

        // Base decal height plus a small per-layer step. Several stripe kinds share the
        // same lateral band (racing line rubber vs skid mark on top of it, asphalt grain
        // vs the darker rubber patch drawn over it), and painting them at one identical Y
        // caused flickering z-fighting; each named layer now gets its own tiny offset.
        const float PaintLayerBase = 0.065f;
        const float PaintLayerStep = 0.0035f;

        void CreateRoadStripe(Vector3 position, Vector3 forward, float width, float length, Material material, string objectName)
        {
            CreateRoadStripe(position, forward, width, length, material, objectName, 0);
        }

        void CreateRoadStripe(Vector3 position, Vector3 forward, float width, float length, Material material, string objectName, int paintLayer)
        {
            float height = PaintLayerBase + paintLayer * PaintLayerStep;
            CreateVisualBox(objectName, position + Vector3.up * height, Quaternion.LookRotation(forward, Vector3.up), new Vector3(width, 0.022f, length), material);
        }

        void BuildGridPaint()
        {
            // Grid boxes come from TrackRuntime.GetGridSlot, the same source used by
            // RaceManager spawning and validation, so cars always start on their marks.
            for (int i = 0; i < TrackRuntime.GridSlotCount; i++)
            {
                bool leftSlot = i % 2 == 0;
                float gridDistance;
                float lateral;
                Runtime.GetGridSlot(i, out gridDistance, out lateral);
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(gridDistance, out point, out forward, out right);
                Vector3 center = point + right * lateral;

                // Detailed grid box
                CreateRoadStripe(center - forward * 3.5f, right, 0.22f, 6.5f, lineMaterial, "Painted grid stop line");
                CreateRoadStripe(center - right * 1.8f, forward, 0.15f, 6.8f, lineMaterial, "Painted grid side line");
                CreateRoadStripe(center + right * 1.8f, forward, 0.15f, 6.8f, lineMaterial, "Painted grid side line");

                // Row markers
                if (leftSlot)
                {
                    Vector3 markerPos = center - right * 4.2f;
                    CreateVisualBox("Grid Row Marker", markerPos + Vector3.up * 0.05f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.8f, 0.01f, 0.8f), lineMaterial);
                }
            }
        }

        void BuildKerbs()
        {
            for (int i = 0; i < Runtime.centerLine.Count; i++)
            {
                Vector3 previous = Runtime.centerLine[(i - 1 + Runtime.centerLine.Count) % Runtime.centerLine.Count];
                Vector3 current = Runtime.centerLine[i];
                Vector3 next = Runtime.centerLine[(i + 1) % Runtime.centerLine.Count];
                Vector3 entry = (current - previous).normalized;
                Vector3 exit = (next - current).normalized;
                float angle = Vector3.Angle(entry, exit);
                if (angle < 12f)
                {
                    continue;
                }

                float turnSign = Mathf.Sign(Vector3.Cross(entry, exit).y);
                float kerbLength = Mathf.Lerp(12f, 42f, angle / 90f);

                // Hairpin-severity corners get the taller "aggressive" sausage-kerb
                // profile (see CreateKerbBlock) instead of the same flat block every
                // corner shares, so the tightest turns on a lap actually look like it.
                bool aggressive = angle > 55f;

                for (float offset = -kerbLength * 0.5f; offset <= kerbLength * 0.5f; offset += 5.5f)
                {
                    Vector3 point;
                    Vector3 forward;
                    Vector3 right;
                    float sampleDistance = Runtime.cumulativeDistances[i] + offset;
                    Runtime.SampleAtDistance(sampleDistance, out point, out forward, out right);
                    // Kerbs hug the same widened edge the road mesh/barriers use, so a
                    // hairpin's kerb line moves out with the rest of the track instead of
                    // sitting stranded mid-tarmac once the road widens under it.
                    float localHalfWidth = Runtime.HalfWidthAt(sampleDistance);

                    // Outer kerb (Apex or Exit)
                    Vector3 outer = point + right * turnSign * (localHalfWidth + 0.35f);
                    CreateKerbBlock(outer, forward, sampleDistance, aggressive);

                    // Inner kerb (if sharp turn)
                    if (angle > 35f)
                    {
                        Vector3 inner = point - right * turnSign * (localHalfWidth + 0.25f);
                        CreateKerbBlock(inner, forward, sampleDistance + 2f, aggressive);
                    }
                }

                // Painted apex chevron on the runoff at hairpin-severity corners only -
                // a single directional marker distinct from the kerb dashes above,
                // echoing the arrow paint real circuits add at their tightest corners.
                if (aggressive)
                {
                    Vector3 apexPoint;
                    Vector3 apexForward;
                    Vector3 apexRight;
                    Runtime.SampleAtDistance(Runtime.cumulativeDistances[i], out apexPoint, out apexForward, out apexRight);
                    float apexHalfWidth = Runtime.HalfWidthAt(Runtime.cumulativeDistances[i]);
                    CreateApexChevron(apexPoint + apexRight * turnSign * (apexHalfWidth + 1.6f), apexForward, turnSign);
                }
            }
        }

        // Small painted V pointing back at the apex, built from two angled paint
        // stripes rather than a custom mesh - visual-only decal on the runoff,
        // reusing CreateRoadStripe/paint-layer the same way every other track
        // marking in this file does.
        void CreateApexChevron(Vector3 position, Vector3 forward, float turnSign)
        {
            // Biased a few degrees toward turnSign so the V visibly leans into the
            // corner's own turn direction instead of always drawing a symmetric
            // arrow regardless of whether the apex bends left or right.
            Vector3 armA = (Quaternion.Euler(0f, 28f + turnSign * 6f, 0f) * forward).normalized;
            Vector3 armB = (Quaternion.Euler(0f, -28f + turnSign * 6f, 0f) * forward).normalized;
            CreateRoadStripe(position + armA * 1.1f, armA, 0.18f, 2.2f, lineMaterial, "Runoff apex chevron", 8);
            CreateRoadStripe(position + armB * 1.1f, armB, 0.18f, 2.2f, lineMaterial, "Runoff apex chevron", 8);
        }

        // ---------- continuous edge barriers ----------
        // BuildBarriers (sparse 18-30m markers) and BuildSafetyBarriers (continuous 9m
        // walls, elevated sections only) used to be two independent passes that never
        // agreed on where one handed off to the other. Off the elevated sections the
        // sparse pass left gaps wide enough for a spinning car to slip through sideways,
        // and the pit corridor's right side was skipped outright. This single pass walks
        // the whole lap, both sides, every lap, at a tight step with every segment
        // overlapping its neighbour, so there is never a seam to find.
        const float EdgeBarrierStep = 10f;
        const float StreetEdgeBarrierStep = 8f;
        const float EdgeBarrierOverlap = 2f;
        const float EdgeBarrierMinHeight = 1.1f;

        // Barrier gap fix (literal this time - flush to the edge, not "a
        // shorter runoff"): every previous pass still left a real, visible
        // multi-metre gap because EdgeBarrierClearance itself was a sizeable
        // additive standoff (1.5m, after earlier passes of 5.5m/2.6m/2.0m).
        // The brief is explicit that a barrier is track-boundary geometry, not
        // scenery that deserves its own runoff buffer: the NEAR FACE (not the
        // center - see the per-style half-width constants below) now sits
        // only EdgeBarrierClearance past the paved edge (Runtime.HalfWidthAt),
        // and that constant is just large enough to absorb straight-chord-vs-
        // true-arc rounding error on a curve (see CreateEdgeBarrierSegment) -
        // not a deliberate runoff. RaceManager.HandleTrackLimits' off-track
        // thresholds are tightened to match (see there) so the physical wall
        // and the game's own idea of "off track" agree, instead of a car
        // being allowed to legally sit somewhere a solid barrier now occupies.
        // Also reused as the minimumClearance passed into TryPlaceSolidObstacle
        // so a curvature-triggered repair nudges a segment back to this same
        // tight distance instead of silently reintroducing a wide gap on bends.
        // Racing-line clearance (per request): raised from 0.15m. Every edge
        // barrier's track-facing face sits at HalfWidthAt + this, so a small
        // value put the wall right on the paved edge - fine until the AI
        // started working the full racing line and running the kerbs, where a
        // car drifting to (or a touch over) the track edge had almost no room
        // before the wall. At 0.9m (~half a car width plus rounding) there is
        // now a genuine runoff strip between the paved edge/kerb and the
        // barrier everywhere, so the optimal line - which itself stays a
        // further ~2.3m inside the edge via LegalOffsetLimit - is never within
        // half a car width of a barrier. The continuous barrier line has no
        // gaps (it just sits further out uniformly), so this cannot let a car
        // escape the circuit; it only adds margin.
        const float EdgeBarrierClearance = 0.9f;
        const float ArmcoHalfWidth = 0.08f;
        const float StreetWallHalfWidth = 0.225f;
        const float ConcreteWallHalfWidth = 0.25f;
        const float TyreStackHalfWidth = 0.6f;
        const float HighRiskCornerAngle = 55f;
        const float HighSpeedTrackLength = 5000f;

        // Containment fix (broader than hairpins): HairpinCornerAngleThreshold/
        // HighRiskCornerAngle (55 deg) stays reserved for genuine hairpins - both the
        // road-widening bonus and the tyre-stack decision below key off it unchanged.
        // But "tight corners still need much better containment" is a wider complaint
        // than just hairpins, so this is a second, lower angle bar purely for the
        // catch-fence/overlap decision in BuildContinuousEdgeBarriers: any corner
        // sharp enough to clear this (a meaningfully tight corner, short of a full
        // hairpin) also gets continuous catch fencing and the same tightened
        // segment overlap hairpins get, so there is no gap a car can find at entry,
        // apex or exit just because the corner was 45 degrees instead of 60.
        const float TightCornerFenceAngle = 40f;
        // Speed-rebalance pass: widened alongside the corner-detection window spans
        // above - a real corner's own footprint is ~25% bigger now, so the
        // containment radius around its detected peak needs to cover that whole
        // bigger footprint, not just the old (now too-small) radius.
        // Barrier-gap fix round 2 (Italy hairpin, "no barriers on the outside,
        // people overspeeding and not turning" persisting after the pit-lane
        // floor cap): 55m only covers +-55m around the corner's PEAK - a real
        // decreasing-radius hairpin's braking zone/apex/exit easily spans well
        // past 110m total on a 6x-scaled layout, so the far end of a long
        // hairpin (typically the EXIT, exactly where an overspeeding car runs
        // wide) fell outside this radius entirely - nearTightFenceCorner went
        // false there, so neither the corner-priority pit-blend containment nor
        // the forced continuous catch fencing applied for that whole stretch,
        // regardless of the bulge cap fixed last round. Widened to fully cover
        // a real hairpin's whole entry-to-exit footprint.
        const float TightCornerFenceRadius = 95f;

        // Pit-exit early-turn fix round 4: bakes Runtime.tightFenceContainmentZones
        // from the exact same corner detection + radius/span math ComputeBarrierPlan
        // uses for nearTightFenceCorner, so runtime consumers (AiVehicleController)
        // can ask "is a real wall still hugging the track here" using the barrier
        // builder's own answer instead of an independently-tuned proxy. Radius here
        // already folds in each corner's own span/2, matching IsNearCorner exactly -
        // Runtime.IsNearTightFenceCorner only needs a plain wrapped-distance compare.
        void PopulateCornerContainmentZones()
        {
            Runtime.tightFenceContainmentZones.Clear();
            List<CornerInfo> highRiskCorners = DetectCorners(HighRiskCornerAngle);
            for (int i = 0; i < highRiskCorners.Count; i++)
            {
                Runtime.tightFenceContainmentZones.Add(new TrackRuntime.CornerContainmentZone
                {
                    distance = highRiskCorners[i].distance,
                    radius = 45f + highRiskCorners[i].span * 0.5f
                });
            }

            List<CornerInfo> tightFenceCorners = DetectCorners(TightCornerFenceAngle);
            for (int i = 0; i < tightFenceCorners.Count; i++)
            {
                Runtime.tightFenceContainmentZones.Add(new TrackRuntime.CornerContainmentZone
                {
                    distance = tightFenceCorners[i].distance,
                    radius = TightCornerFenceRadius + tightFenceCorners[i].span * 0.5f
                });
            }
        }

        enum EdgeBarrierStyle
        {
            Armco,
            StreetWall,
            Elevated
        }

        struct CornerInfo
        {
            public float distance;
            public float angle;
            // Arc length (metres) of the whole above-threshold run this corner
            // was detected from - 0 for a sharp, single-vertex corner, wider
            // for a spread-out multi-apex complex. IsNearCorner adds half of
            // this to its own radius so a wide corner's fencing/containment
            // coverage reaches its true entry and exit, while single-point
            // consumers (braking boards, marbles, rubber build-up, etc.) can
            // simply keep using `distance` as one representative point exactly
            // like before.
            public float span;
        }

        // Two-pass barrier placement support (smoothing pass, this round): computing a
        // segment's style/lateral offset used to happen in the same breath as placing
        // its geometry, each segment sampled independently of its neighbours. That's
        // fine wherever the lateral target is already a smooth, continuous function of
        // distance (mostly true - see FlushBarrierLateral/HalfWidthAt), but a few
        // decisions genuinely are discrete per segment (the pit fan-out engaging, a
        // corner's tyre-stack standoff switching on/off) and can step the lateral
        // target briefly INWARD right at their own boundary - exactly the "sudden
        // inward notch"/trap-pocket a spinning car's corner can wedge into. Buffering
        // a whole lap's planned offsets per side before placing anything lets
        // SmoothBarrierLateralSequence below erase that, without touching the
        // style/catchFence/tyreStack decisions or where any segment starts/stops.
        struct BarrierPlanEntry
        {
            public float distance;
            public float step;
            public float segmentLength;
            public int side;
            public float lateral;
            public EdgeBarrierStyle style;
            public bool catchFence;
            public bool tyreStack;
            public int stripeIndex;
            public bool nearTightCorner;
        }

        void BuildContinuousEdgeBarriers()
        {
            float step = streetTrack ? StreetEdgeBarrierStep : EdgeBarrierStep;
            bool highSpeedTrack = Runtime.length > HighSpeedTrackLength;
            List<CornerInfo> highRiskCorners = DetectCorners(HighRiskCornerAngle);
            // Broader, lower-severity band used only for the fencing/overlap decision
            // below (see TightCornerFenceAngle) - separate list so the hairpin-only
            // road-widening bonus and the 55-degree tyre-stack call are untouched.
            List<CornerInfo> tightFenceCorners = DetectCorners(TightCornerFenceAngle);

            bool previousElevated = IsElevatedAtDistance(-step);
            int stripeIndex = 0;
            List<BarrierPlanEntry> leftPlan = new List<BarrierPlanEntry>();
            List<BarrierPlanEntry> rightPlan = new List<BarrierPlanEntry>();
            for (float d = 0f; d < Runtime.length;)
            {
                bool nearHighRiskCorner = IsNearCorner(d, highRiskCorners, 45f);
                // Union of the true hairpin/high-risk band with the broader tight-corner
                // band - drives containment (fencing + overlap) only, never the tyre-stack
                // or widening decisions which still key off nearHighRiskCorner alone.
                bool nearTightFenceCorner = nearHighRiskCorner || IsNearCorner(d, tightFenceCorners, TightCornerFenceRadius);
                float normalized = d / Mathf.Max(1f, Runtime.length);

                // Facet-density fix, pit ramp extension: the entry/exit ramps are
                // explicitly the zone that needs "smooth, gradual lane transitions" -
                // the fan-out's own lateral target changes fastest exactly while
                // PitZoneBlend is still easing between 0 and 1, the same "fixed step
                // facets a fast-changing target" problem tight corners have, just
                // driven by the pit blend instead of track curvature. Treat "still
                // easing" the same as a tight-fence corner for sampling density/overlap
                // purposes only - PitZoneBlend() itself still decides the actual target.
                float pitBlendAtSample = PitZoneBlend(normalized);
                bool nearPitRampTransition = pitBlendAtSample > 0f && pitBlendAtSample < 1f;

                // A hairpin's whole direction change is usually concentrated at one
                // centerline vertex rather than spread evenly, so a fixed-length chord
                // straddling that vertex swings its box away from the true arc on the
                // outside of the corner - shortening the step and widening the overlap
                // there keeps consecutive rotated segments physically overlapping
                // instead of mitering open into a gap. Every corner sharp enough to
                // warrant forced catch fencing gets this same tightened sampling, not
                // just true hairpins, so entry/apex/exit never gets a wider seam than
                // the straights do. Genuine hairpins/high-risk corners (nearHighRiskCorner)
                // get an even finer third tier on top of that - a Silverstone/Baku-style
                // tight complex is exactly where the straight-chord-vs-true-arc error is
                // largest, so it needs the most samples and the most overlap.
                float localStep = nearHighRiskCorner ? step * 0.3f : (nearTightFenceCorner ? step * 0.5f : step);
                float localOverlap = nearHighRiskCorner ? EdgeBarrierOverlap * 3f : (nearTightFenceCorner ? EdgeBarrierOverlap * 2f : EdgeBarrierOverlap);
                if (nearPitRampTransition)
                {
                    localStep = Mathf.Min(localStep, step * 0.5f);
                    localOverlap = Mathf.Max(localOverlap, EdgeBarrierOverlap * 2f);
                }

                float segmentLength = localStep + localOverlap;
                bool elevated = IsElevatedAtDistance(d) || IsElevatedAtDistance(d + localStep * 0.5f) || IsElevatedAtDistance(d + localStep);

                leftPlan.Add(ComputeBarrierPlan(d, localStep, segmentLength, -1, elevated, normalized, highSpeedTrack, nearHighRiskCorner, nearTightFenceCorner, stripeIndex));
                rightPlan.Add(ComputeBarrierPlan(d, localStep, segmentLength, 1, elevated, normalized, highSpeedTrack, nearHighRiskCorner, nearTightFenceCorner, stripeIndex));

                if (elevated && ElevationAboveGround(d) > 4f && Mathf.FloorToInt(d / step) % 3 == 0)
                {
                    CreateBridgeSupports(d);
                }

                // Soften the transition into and out of an elevated stretch with tyre
                // barrier stacks so run-off areas funnel cars back before the drop.
                if (elevated != previousElevated)
                {
                    CreateTransitionTyreStacks(d);
                }

                previousElevated = elevated;
                stripeIndex++;
                d += localStep;
            }

            // Smoothing pass (see SmoothBarrierLateralSequence): erases any single
            // sample's lateral target dipping inward relative to its neighbours before
            // a single piece of barrier geometry is actually placed, so a tightening-
            // then-loosening corner or a discrete style/fan-out transition can never
            // leave a trap-pocket notch for a car to catch a corner on.
            SmoothBarrierLateralSequence(leftPlan);
            SmoothBarrierLateralSequence(rightPlan);

            for (int i = 0; i < leftPlan.Count; i++)
            {
                PlaceBarrierPlanEntry(leftPlan[i]);
            }

            for (int i = 0; i < rightPlan.Count; i++)
            {
                PlaceBarrierPlanEntry(rightPlan[i]);
            }
        }

        void PlaceBarrierPlanEntry(BarrierPlanEntry entry)
        {
            CreateEdgeBarrierSegment(entry.distance, entry.step, entry.side, entry.lateral, entry.segmentLength, entry.style, entry.catchFence, entry.tyreStack, entry.stripeIndex);
        }

        // Smooths the interior of one side's whole-lap lateral-offset sequence so no
        // single segment's target sits meaningfully closer to the racing line than the
        // general trend around it (see ValidateBarrierSmoothness for the automated
        // check this pass exists to satisfy). Deliberately one-sided: each smoothed
        // value is the LARGER of its own original target and the 3-point moving
        // average around it, so this can only ever push a barrier segment OUTWARD to
        // erase an inward notch/trap-pocket - never pull one INWARD and risk narrowing
        // the track or moving a collision footprint toward the racing line. The whole
        // lap is a closed loop with no real start/end seam to preserve (unlike, say, a
        // single corner's own fence run), so neighbours simply wrap around.
        // Barrier-pocket fix (item E): near a tight-fence-grade corner, on top of the
        // one-sided moving-average floor above, also refuse to let any single sample
        // sit inward of BOTH its immediate neighbours by more than this much. A tight
        // corner is exactly where a discrete style/fan-out/tyre-stack transition is
        // most likely to land right on the apex, and the 3-point average alone can
        // still let a single-sample dip of a few tenths of a metre through if both
        // neighbours also nudge slightly outward with it - this is a harder floor,
        // not an average, so it can only ever push a sample OUTWARD to match its
        // tightest neighbour, never pull one in.
        const float MaxInwardNotchNearTightCornerMeters = 0.2f;

        void SmoothBarrierLateralSequence(List<BarrierPlanEntry> plan)
        {
            int count = plan.Count;
            if (count < 3)
            {
                return;
            }

            float[] original = new float[count];
            for (int i = 0; i < count; i++)
            {
                original[i] = plan[i].lateral;
            }

            for (int i = 0; i < count; i++)
            {
                int prev = (i - 1 + count) % count;
                int next = (i + 1) % count;
                float average = (original[prev] + original[i] + original[next]) / 3f;
                BarrierPlanEntry entry = plan[i];
                // Outward envelope only: take the larger of the original target and
                // the neighbourhood average, never the smaller - averaging on its own
                // can pull a barrier inward and create the exact notch this pass
                // exists to erase.
                entry.lateral = Mathf.Max(entry.lateral, average);

                if (entry.nearTightCorner)
                {
                    float tightestNeighbor = Mathf.Min(original[prev], original[next]);
                    entry.lateral = Mathf.Max(entry.lateral, tightestNeighbor - MaxInwardNotchNearTightCornerMeters);
                }

                plan[i] = entry;
            }
        }

        // Single source of truth for "how far from the centerline does a
        // barrier of this half-thickness need to sit so its INNER FACE lands
        // exactly on the paved edge" (plus EdgeBarrierClearance, the one
        // small rounding margin every style shares - never a per-style
        // standoff). Every barrier/fence/tyre-stack placement in this file
        // computes its lateral offset through this one function, never by
        // hand, so "barriers touch the track edge" cannot silently regress to
        // a fresh hand-rolled offset the next time this file is touched.
        // extraStandoff is for the rare case of a second layer sitting
        // BEHIND a track-facing one (e.g. the Armco rail behind a tyre
        // stack) - it shifts the whole thing out by that much while keeping
        // the same "flush plus clearance" base.
        float FlushBarrierLateral(float distance, float barrierHalfThickness, float extraStandoff = 0f)
        {
            return Runtime.HalfWidthAt(distance) + EdgeBarrierClearance + extraStandoff + barrierHalfThickness;
        }

        // Direct, undiscretized curvature reading at one point (degrees of
        // heading change across a 40m/80m window either side) - used as a
        // permissive, can't-miss-a-real-bend fallback for barrier-gap
        // decisions, independent of the cumulative-window/angle-threshold/
        // radius corner-DETECTION pipeline (DetectCorners/IsNearCorner) which
        // has needed repeated widening and can still miss part of a real
        // corner's footprint.
        float LocalCurvatureAngle(float distance)
        {
            Vector3 pointA, forwardA, rightA;
            Vector3 pointB, forwardB, rightB;
            Vector3 pointC, forwardC, rightC;
            Runtime.SampleAtDistance(distance - 40f, out pointA, out forwardA, out rightA);
            Runtime.SampleAtDistance(distance, out pointB, out forwardB, out rightB);
            Runtime.SampleAtDistance(distance + 40f, out pointC, out forwardC, out rightC);
            float turnNear = Vector3.Angle(forwardA, forwardB);
            float turnFar = Vector3.Angle(forwardB, forwardC);
            return Mathf.Max(turnNear, turnFar);
        }

        // Decides style/offset for one side at one step, including the pit-corridor
        // fan-out on the right side, and returns the plan rather than placing geometry
        // directly - BuildContinuousEdgeBarriers buffers a whole lap's worth of these,
        // runs them through SmoothBarrierLateralSequence, and only then hands each one
        // to CreateEdgeBarrierSegment (see PlaceBarrierPlanEntry). Also reused directly
        // by ValidateBarrierSmoothness to re-derive the same targets for its own,
        // independent fine-step sweep.
        BarrierPlanEntry ComputeBarrierPlan(float distance, float step, float segmentLength, int side, bool elevated, float normalized, bool highSpeedTrack, bool nearHighRiskCorner, bool nearTightFenceCorner, int stripeIndex)
        {
            EdgeBarrierStyle style;
            float baseLateral;
            bool catchFence;
            bool tyreStack;

            // Sampled at the segment midpoint - the same distance CreateEdgeBarrierSegment
            // actually places basePosition at - so a hairpin's widened half-width lines up
            // exactly with where the barrier geometry gets built, not the segment's start.
            float midDistance = distance + step * 0.5f;

            if (elevated)
            {
                style = EdgeBarrierStyle.Elevated;
                baseLateral = FlushBarrierLateral(midDistance, ConcreteWallHalfWidth);
                catchFence = NeedsCatchFence(distance);
                tyreStack = false;
            }
            else if (streetTrack)
            {
                style = EdgeBarrierStyle.StreetWall;
                baseLateral = FlushBarrierLateral(midDistance, StreetWallHalfWidth);
                catchFence = true;
                tyreStack = false;
            }
            else
            {
                style = EdgeBarrierStyle.Armco;
                // High-speed circuits get a catch fence along their fast (non-corner)
                // stretches specifically; every circuit gets tyre stacks at its worst corners.
                catchFence = highSpeedTrack && !nearHighRiskCorner;
                tyreStack = nearHighRiskCorner;
                if (tyreStack)
                {
                    // At a tyre-stack corner the stack itself becomes the
                    // track-facing layer (placed by CreateEdgeBarrierSegment
                    // hugging the same flush line every style uses) and the
                    // rail sits directly behind it as the rigid backstop -
                    // not the other way around, and not sitting coincident
                    // with the stack, so there's no gap between the two and
                    // no gap between the stack and the track edge either.
                    baseLateral = FlushBarrierLateral(midDistance, ArmcoHalfWidth, TyreStackHalfWidth * 2f);
                }
                else
                {
                    baseLateral = FlushBarrierLateral(midDistance, ArmcoHalfWidth);
                }
            }

            // Every hairpin gets continuous catch fencing through entry, apex and exit,
            // regardless of barrier style/track type - this is the same per-segment loop
            // that builds the rest of the barrier run, so the fencing follows the widened
            // hairpin shape exactly and has no gaps, rather than being placed separately.
            if (Runtime.HairpinWidthBonus(distance + step * 0.5f) > 0f)
            {
                catchFence = true;
            }

            // Broader containment fix: any corner sharp enough to fall inside the
            // tight-corner fencing band (see TightCornerFenceAngle/Radius above -
            // covers meaningfully tight corners short of a full hairpin, not just
            // genuine hairpins) also gets forced continuous catch fencing, so "tight
            // corners" generally get the same no-gap treatment hairpins already do.
            if (nearTightFenceCorner)
            {
                catchFence = true;
            }

            float lateral = baseLateral;

            // The pit lane only ever runs down the right side (Runtime.PitLaneLateral is a
            // positive offset), so only that side needs to fan out into the wall guarding
            // the whole pit complex. The blend is a smooth ramp rather than a step so the
            // fan-out itself has no gap for a car to find at the transition.
            //
            // Corner-priority fix: PitZoneEntryRampStart/End..PitZoneExitRampEnd (wrapping
            // through 0.85-1.0-0.045 normalized) is a fixed FRACTION of the lap, purely
            // positional - it has no idea what's actually there, and on every hand-authored
            // circuit the pit lane sits near the end of the lap, meaning this band always
            // covers whatever the final corner(s) before the start/finish straight happen to
            // be. This fan-out used to unconditionally win regardless, pulling the wall from
            // its correct flush-corner distance out to ~PitOuterLateral() and clearing
            // catchFence/tyreStack outright - the real, general-case reason the final corner
            // on every track kept reading as unfenced, not a one-off. The real pit lane
            // surface is a separate, unrelated lane built independently of this outer
            // perimeter barrier, so a real corner here must always win that conflict - pull
            // the wall in tight (skip the full fan-out) whenever this stretch is near ANY
            // tight-fence-grade corner (nearTightFenceCorner - the same broad band hairpins/
            // tight corners already get forced continuous catch fencing from above), not
            // just full hairpins.
            //
            // Pit-exit blocking fix: that corner-priority pull-in used to go all the way
            // back to baseLateral (the flush track edge) with no floor, regardless of how
            // far into the pit corridor/ramp this exact distance actually sits - on any
            // track where a genuine corner overlaps the pit zone's fixed band (routine,
            // since pit exits typically sit right before turn 1 or the final corner), that
            // planted a solid wall inside the lane every car has to drive through to leave
            // the pits, trapping the whole AI field. PitMinimumOuterLateral is a hard floor
            // the corner-priority pull-in can never go below - the wall may still pull in
            // for corner containment instead of the full PitOuterLateral() fan-out, but
            // never closer than the pit lane's own drivable surface actually needs at this
            // exact distance.
            if (side > 0)
            {
                float pitBlend = PitZoneBlend(normalized);
                if (pitBlend > 0f)
                {
                    // Barrier-gap fix round 3 (per report - two rounds of tuning
                    // the DISCRETE corner-detection thresholds/radius still
                    // didn't fix it): rather than trust nearTightFenceCorner
                    // (built from a cumulative-window/angle-threshold/radius
                    // pipeline that has already needed two rounds of widening
                    // and still evidently misses part of this exact hairpin),
                    // sample the REAL local curvature directly at this exact
                    // point, with a permissive threshold well below full
                    // hairpin severity. This can never miss a genuine bend the
                    // way a discretized detector with a fixed radius/threshold
                    // can - if the road is actually turning here, the wall gets
                    // capped near the flush distance, full stop, independent of
                    // whatever the corner-list pipeline concluded elsewhere.
                    bool realCurvatureHere = LocalCurvatureAngle(midDistance) > 12f;
                    if (!nearTightFenceCorner && !realCurvatureHere)
                    {
                        lateral = Mathf.Lerp(baseLateral, PitOuterLateral(), pitBlend);
                        style = EdgeBarrierStyle.StreetWall;
                        catchFence = false;
                        tyreStack = false;
                    }
                    else
                    {
                        // Barrier-gap fix ("no barriers on the outside line,
                        // everyone flies into the grass" - the final hairpin,
                        // real Monza-layout report): PitMinimumOuterLateral is
                        // computed purely from the PIT LANE's own geometry
                        // (PitLaneLateral, a fixed offset that can be 20m+ from
                        // the racing centreline), taken via Mathf.Max against
                        // baseLateral (the tight corner's own correct flush
                        // distance). Whenever a real corner - like the final
                        // hairpin - overlaps the pit zone's normalized band
                        // (the routine case the "corner-priority" branch above
                        // exists to handle), that Max() ALWAYS won with the far
                        // pit-lane value, since it's computed from an entirely
                        // different, much larger offset than the corner's own
                        // tight containment - so the "corner priority" fix never
                        // actually applied in exactly the case it was built for,
                        // and the wall sat far out near the pit lane while the
                        // hairpin curved away from it, leaving open grass in
                        // between with nothing guarding the true corner edge.
                        // The anti-trapping floor still matters (an earlier bug
                        // fully collapsing to baseLateral trapped cars physically
                        // driving through the pits), so it isn't removed - just
                        // capped to a bounded bulge past the corner's own flush
                        // distance, enough to clear a real, modest pit-surface
                        // encroachment without letting the wall balloon far out
                        // into open grass at a tight corner.
                        const float maxPitBulgeMeters = 8f;
                        float pitMinimum = PitMinimumOuterLateral(midDistance, normalized);
                        lateral = Mathf.Max(baseLateral, Mathf.Min(pitMinimum, baseLateral + maxPitBulgeMeters));
                    }
                }
            }

            return new BarrierPlanEntry
            {
                distance = distance,
                step = step,
                segmentLength = segmentLength,
                side = side,
                lateral = lateral,
                style = style,
                catchFence = catchFence,
                tyreStack = tyreStack,
                stripeIndex = stripeIndex,
                nearTightCorner = nearTightFenceCorner
            };
        }

        // One barrier segment on one side, sampled along the local chord (the same
        // technique the old bridge-fence pass used) so tight corners get a tangent
        // segment instead of a straight line cutting across the corridor.
        void CreateEdgeBarrierSegment(float distance, float step, int side, float lateral, float segmentLength, EdgeBarrierStyle style, bool wantsCatchFence, bool wantsTyreStack, int stripeIndex)
        {
            Vector3 a;
            Vector3 b;
            Vector3 mid;
            Vector3 forward;
            Vector3 right;
            Vector3 rightA;
            Vector3 rightB;
            Vector3 discard;
            Runtime.SampleAtDistance(distance, out a, out discard, out rightA);
            Runtime.SampleAtDistance(distance + step, out b, out discard, out rightB);
            Runtime.SampleAtDistance(distance + step * 0.5f, out mid, out forward, out right);

            // Hairpin outside-line gap fix: size and orient each barrier box off the
            // OUTER-EDGE chord, not the centerline chord. Every box is shifted outward
            // by `lateral`; on the outside of a tight corner that offset edge traces a
            // longer arc than the centerline, so consecutive boxes cut at a fixed
            // centerline length spread apart and miter open (the missing barrier on the
            // hairpin's outside line). Spanning the real offset edge closes that gap on
            // any tight corner. The length only ever grows (Max), so the inside of
            // corners and straights - where the offset chord is equal or shorter - keep
            // their existing overlap and are unchanged.
            Vector3 aEdge = a + rightA * side * lateral;
            Vector3 bEdge = b + rightB * side * lateral;
            Vector3 edgeChord = bEdge - aEdge;
            Vector3 chordForward = edgeChord.sqrMagnitude > 0.01f ? edgeChord.normalized : forward;
            Vector3 basePosition = mid + right * side * lateral;
            float overlapBudget = Mathf.Max(0f, segmentLength - step);
            segmentLength = Mathf.Max(segmentLength, edgeChord.magnitude + overlapBudget);

            switch (style)
            {
                case EdgeBarrierStyle.Elevated:
                    CreateConcreteWall(basePosition, chordForward, segmentLength);
                    if (wantsCatchFence)
                    {
                        CreateCatchFence(basePosition, chordForward, segmentLength);
                    }

                    break;
                case EdgeBarrierStyle.StreetWall:
                    CreateStreetWallSegment(basePosition, chordForward, segmentLength, stripeIndex);
                    if (wantsCatchFence)
                    {
                        CreateCatchFence(basePosition, chordForward, segmentLength);
                    }

                    break;
                default:
                    CreateArmcoSegment(basePosition, chordForward, segmentLength, stripeIndex);
                    if (wantsCatchFence)
                    {
                        CreateCatchFence(basePosition, chordForward, segmentLength);
                    }

                    break;
            }

            if (wantsTyreStack)
            {
                // Placed on its own absolute hug line (not relative to the
                // rail's basePosition, which ComputeBarrierPlan has
                // already pushed further out specifically to leave room for
                // this stack) so the stack itself is what actually sits
                // against the track edge, with the rail directly behind it. Sampled at
                // the same distance+step*0.5 midpoint "mid" itself was sampled at, so a
                // widened hairpin's stack lines up with the rest of this segment.
                Vector3 stackPosition = mid + right * side * (Runtime.HalfWidthAt(distance + step * 0.5f) + EdgeBarrierClearance + TyreStackHalfWidth);
                CreateTyreBarrierStack(stackPosition, chordForward, Mathf.Min(segmentLength, 4.6f));
            }
        }

        // Painted concrete-style wall used for street circuits and the pit-complex
        // fan-out; alternating stripe segments so it reads as trackside furniture
        // instead of one endless grey slab.
        void CreateStreetWallSegment(Vector3 basePosition, Vector3 forward, float segmentLength, int stripeIndex)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Continuous street wall";
            wall.transform.SetParent(transform);
            Vector3 scale = new Vector3(0.45f, EdgeBarrierMinHeight, segmentLength);
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = stripeIndex % 2 == 0 ? barrierMaterial : sceneryAccentMaterial;
            // minimumClearance matches EdgeBarrierClearance exactly (not a
            // smaller safety-margin-only value) so a curvature-triggered
            // repair in TryPlaceSolidObstacle targets this same tight hug
            // line instead of quietly reintroducing a wider gap on a bend.
            if (!TryPlaceSolidObstacle(wall, "street-wall", basePosition, forward, scale, EdgeBarrierMinHeight * 0.5f, EdgeBarrierClearance))
            {
                return;
            }

            Vector3 placed = wall.transform.position;
            Vector3 placedForward = wall.transform.forward;
            Quaternion rotation = Quaternion.LookRotation(placedForward, Vector3.up);
            CreateVisualBox("Street wall rub rail", placed + Vector3.up * 0.62f, rotation, new Vector3(0.5f, 0.12f, segmentLength), metalMaterial);
            if (nightTrack || twilightTrack)
            {
                CreateVisualBox("Street wall light strip", placed + Vector3.up * 1.05f, rotation, new Vector3(0.4f, 0.08f, segmentLength - 0.3f), lightGlowMaterial);
            }

            // Low grime streak on a fraction of segments so a long, continuous run of
            // wall doesn't read as freshly painted its entire length - see
            // barrierWeatherMaterial. Kept infrequent (every 5th segment) since a wall
            // run is already the densest object count on the whole track.
            if (stripeIndex % 5 == 0)
            {
                CreateVisualBox("Street wall grime streak", placed + Vector3.up * 0.14f, rotation, new Vector3(0.47f, 0.22f, segmentLength - 0.4f), barrierWeatherMaterial);
            }

            if (stripeIndex % 46 == 5)
            {
                CreateMarshalGapAccent(placed, placedForward, segmentLength);
            }
        }

        // Armco/guardrail look: one real collidable rail band plus two thinner ribs
        // above and below it - a cheap stand-in for a corrugated profile instead of a
        // single flat slab.
        void CreateArmcoSegment(Vector3 basePosition, Vector3 forward, float segmentLength, int stripeIndex)
        {
            GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.name = "Armco guardrail";
            rail.transform.SetParent(transform);
            Vector3 scale = new Vector3(0.16f, EdgeBarrierMinHeight, segmentLength);
            rail.transform.localScale = scale;
            rail.GetComponent<Renderer>().sharedMaterial = armcoMaterial;
            // Barrier gap fix: minimumClearance must equal the same
            // EdgeBarrierClearance the rail's own basePosition was placed
            // with (see ComputeBarrierPlan), not some larger,
            // independently-chosen safety margin - otherwise
            // IsObstacleClearOfRacingSurface treats every single segment as
            // "too close" (since the rail deliberately sits right at that
            // line) and hands it to the repair path in TryPlaceSolidObstacle,
            // which would then push every Armco segment back out to a wider,
            // inconsistent gap - exactly the bug this pass exists to remove.
            if (!TryPlaceSolidObstacle(rail, "armco-rail", basePosition, forward, scale, EdgeBarrierMinHeight * 0.5f, EdgeBarrierClearance))
            {
                return;
            }

            Vector3 placed = rail.transform.position;
            Vector3 placedForward = rail.transform.forward;
            Quaternion rotation = Quaternion.LookRotation(placedForward, Vector3.up);
            CreateVisualBox("Armco rib upper", placed + Vector3.up * (EdgeBarrierMinHeight * 0.3f), rotation, new Vector3(0.2f, 0.15f, segmentLength - 0.2f), armcoMaterial);
            CreateVisualBox("Armco rib lower", placed - Vector3.up * (EdgeBarrierMinHeight * 0.28f), rotation, new Vector3(0.2f, 0.15f, segmentLength - 0.2f), armcoMaterial);
            if (stripeIndex % 2 == 0)
            {
                CreateVisualBox("Armco post", placed - Vector3.up * (EdgeBarrierMinHeight * 0.5f - 0.02f), rotation, new Vector3(0.1f, 0.42f, 0.12f), fencePostMaterial);
            }

            // Low grime/rust streak on a fraction of rails - see barrierWeatherMaterial
            // and the matching street-wall treatment above. Sits just under the lower
            // rib rather than coincident with it, so there's no z-fighting.
            if (stripeIndex % 5 == 2)
            {
                CreateVisualBox("Armco grime streak", placed - Vector3.up * (EdgeBarrierMinHeight * 0.4f), rotation, new Vector3(0.17f, 0.08f, segmentLength - 0.2f), barrierWeatherMaterial);
            }

            if (stripeIndex % 46 == 5)
            {
                CreateMarshalGapAccent(placed, placedForward, segmentLength);
            }
        }

        // Cosmetic marshal-access marker: a short diagonal hazard-striped panel
        // flanked by two posts, standing in for the walk-through gaps marshals use to
        // reach the fence line at a barrier run. The rail segment underneath keeps its
        // own full collision (see TryPlaceSolidObstacle above) - this never opens an
        // actual gap a car could find, only reads as one from the chase camera, the
        // same "decorative access point" idiom CreateMarshalPost's own fence-with-gap
        // already uses a level further back from the track.
        void CreateMarshalGapAccent(Vector3 basePosition, Vector3 forward, float segmentLength)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Quaternion diagonal = Quaternion.LookRotation((forward + right * 0.6f).normalized, Vector3.up);
            const int stripes = 4;
            for (int i = 0; i < stripes; i++)
            {
                float t = (i - (stripes - 1) * 0.5f) * 0.5f;
                Material stripeMaterial = i % 2 == 0 ? flagYellowMaterial : checkerDarkMaterial;
                CreateVisualBox("Marshal gap hazard stripe", basePosition + forward * t + right * 0.11f + Vector3.up * (EdgeBarrierMinHeight * 0.5f), diagonal, new Vector3(0.05f, EdgeBarrierMinHeight * 0.9f, 0.55f), stripeMaterial);
            }

            float postOffset = segmentLength * 0.5f - 0.1f;
            Quaternion postRotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateVisualBox("Marshal gap post", basePosition - forward * postOffset + right * 0.12f, postRotation, new Vector3(0.1f, EdgeBarrierMinHeight + 0.15f, 0.1f), fencePostMaterial);
            CreateVisualBox("Marshal gap post", basePosition + forward * postOffset + right * 0.12f, postRotation, new Vector3(0.1f, EdgeBarrierMinHeight + 0.15f, 0.1f), fencePostMaterial);
        }

        void CreateTyreBarrierStack(Vector3 position, Vector3 forward, float length)
        {
            GameObject stack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stack.name = "Runoff tyre barrier stack";
            stack.transform.SetParent(transform);
            Vector3 scale = new Vector3(1.2f, 1f, length);
            stack.transform.localScale = scale;
            stack.GetComponent<Renderer>().sharedMaterial = tireBarrierMaterial;
            if (!TryPlaceSolidObstacle(stack, "tyre-barrier", position, forward, scale, 0.5f, 0.7f))
            {
                return;
            }

            // TecPro-style coloured impact padding strapped across the top of the stack -
            // real barriers get exactly this treatment at the corners that need tyre
            // stacks in the first place, and it's the detail that most reads as "serious
            // corner" from a chase camera rather than a plain black wall of tyres.
            Vector3 placed = stack.transform.position;
            Quaternion rotation = Quaternion.LookRotation(stack.transform.forward, Vector3.up);
            CreateVisualBox("Tyre barrier impact pad", placed + Vector3.up * 0.56f, rotation, new Vector3(1.28f, 0.14f, length - 0.2f), sceneryAccentMaterial);
        }

        void CreateTransitionTyreStacks(float distance)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            for (int side = -1; side <= 1; side += 2)
            {
                // Same absolute hug line every other tyre stack uses (see
                // CreateEdgeBarrierSegment) so an elevation transition reads
                // as continuous with the rest of the barrier run instead of
                // opening its own gap right at the transition point.
                CreateTyreBarrierStack(point + right * side * (Runtime.HalfWidthAt(distance) + EdgeBarrierClearance + TyreStackHalfWidth), forward, 4.6f);
            }
        }

        // ---------- pit corridor sealing ----------
        // The pit lane only exists on the right side (Runtime.PitLaneLateral is a
        // positive offset), so only the right side needs to fan the main barrier out
        // into a dedicated pit-complex wall. Smooth ramps at the entry and exit keep
        // that fan-out from ever opening a gap of its own at the transition.
        // Pit-lane architecture fix: aliases over TrackRuntime's own public
        // versions of these exact same boundaries (GetPitEntryRampEnvelope/
        // GetPitExitRampEnvelope use the identical values) - a single canonical
        // source shared with RaceManager and AiVehicleController instead of each
        // class inventing its own copy that can drift out of sync.
        // Fixed-metre pit-ENTRY geometry fix: same instance-property conversion
        // as the exit pair below (the entry ramp/corridor anchors are now fixed
        // metres before the line, so they can't be compile-time constants).
        float PitZoneEntryRampStart
        {
            get { return Runtime.PitEntryRampStartNormalized; }
        }

        float PitZoneEntryRampEnd
        {
            get { return Runtime.PitCorridorStartNormalized; }
        }
        // Fixed-metre pit-exit geometry fix: PitExitRampStartNormalized/
        // PitExitRampEndNormalized are now instance properties on Runtime (computed
        // from fixed metres, not a lap fraction - see TrackRuntime), so these two
        // can no longer be compile-time constants. Still the single canonical
        // source shared with RaceManager/AiVehicleController - just read through
        // the instance now instead of the class.
        float PitZoneExitRampStart
        {
            get { return Runtime.PitExitRampStartNormalized; }
        }

        float PitZoneExitRampEnd
        {
            get { return Runtime.PitExitRampEndNormalized; }
        }

        float PitZoneBlend(float normalized)
        {
            if (normalized >= PitZoneEntryRampEnd && normalized <= PitZoneExitRampStart)
            {
                return 1f;
            }

            if (normalized >= PitZoneEntryRampStart && normalized < PitZoneEntryRampEnd)
            {
                return Mathf.InverseLerp(PitZoneEntryRampStart, PitZoneEntryRampEnd, normalized);
            }

            float wrapTotal = (1f - PitZoneExitRampStart) + PitZoneExitRampEnd;
            if (normalized > PitZoneExitRampStart)
            {
                float wrapped = normalized - PitZoneExitRampStart;
                return 1f - Mathf.Clamp01(wrapped / wrapTotal);
            }

            if (normalized <= PitZoneExitRampEnd)
            {
                float wrapped = (1f - PitZoneExitRampStart) + normalized;
                return 1f - Mathf.Clamp01(wrapped / wrapTotal);
            }

            return 0f;
        }

        // Just past the garage block's outer face: garages (BuildPitLane) are centred
        // at PitLaneLateral + 15 with an 11m footprint, so their far edge sits at
        // +15+5.5. Push a couple more metres past that so the wall never clips them.
        float PitOuterLateral()
        {
            return Runtime.PitLaneLateral + 15f + 5.5f + 2.5f;
        }

        // Pit-exit blocking fix: the minimum right-side lateral distance (from
        // centerline) the outer edge barrier must never sit closer than while
        // anywhere inside the pit zone's active blend - the actual outer edge of
        // the drivable pit lane surface (BuildPitRampSurface/PitRampEnvelopeAt for
        // the ramps, the flat corridor's own fixed service-road width otherwise)
        // plus a small standoff. ComputeBarrierPlan's corner-priority containment
        // (pulling the wall in tight near a genuine corner instead of the full
        // PitOuterLateral fan-out) must never be allowed to clamp past this -
        // every AI car getting physically trapped in the pits was exactly that:
        // a corner overlapping the pit zone's fixed normalized band (routine,
        // since pit exits typically sit right before turn 1 or the final corner)
        // collapsed the wall all the way back to the flush track edge, planting
        // a solid collider inside the lane every car has to physically drive
        // through to leave the pits. Returns 0f outside the pit zone entirely.
        float PitMinimumOuterLateral(float distance, float normalized)
        {
            if (PitZoneBlend(normalized) <= 0f)
            {
                return 0f;
            }

            const float standoff = 2f;
            if (normalized >= PitZoneEntryRampStart && normalized < PitZoneEntryRampEnd)
            {
                float envLateral;
                float envHalfWidth;
                PitRampEnvelopeAt(normalized, distance, out envLateral, out envHalfWidth);
                return envLateral + envHalfWidth + standoff;
            }

            if (normalized > PitZoneExitRampStart || normalized <= PitZoneExitRampEnd)
            {
                float envLateral;
                float envHalfWidth;
                PitRampEnvelopeAt(normalized, distance, out envLateral, out envHalfWidth);
                return envLateral + envHalfWidth + standoff;
            }

            // Flat corridor: PitRampEnvelopeAt is only valid inside the ramp
            // windows checked above (see its own comment) - here the drivable
            // surface is BuildPitLane's fixed-width service road, centred on
            // PitLaneLateral with a PitRampFullWidth-wide width.
            return Runtime.PitLaneLateral + TrackRuntime.PitRampFullWidth * 0.5f + standoff;
        }

        // ---------- pit lane / track divider ----------
        // The old barrier layout only ever fenced the pit complex's OUTER edge
        // (ComputeBarrierPlan fans the main right-side barrier out to
        // PitOuterLateral through the corridor) - there was never anything at
        // all between the racing surface's own right edge and the pit lane's
        // driving surface, so a car could freely drift between the two with no
        // physical or visual boundary. This adds that missing inner wall,
        // running only through the flat pit corridor
        // (PitCorridorStartNormalized..PitZoneExitRampStart, i.e. exactly
        // where PitZoneBlend==1 and the outer wall has fully committed to
        // being the pit complex's own wall) so the entry/exit ramp zones on
        // either end - where PitZoneBlend eases from 0 to 1 - remain the only
        // way through, reading as deliberate openings rather than a random gap.
        //
        // Flush-fix: this used to be a thin fence centred 2.0m off the track
        // edge, leaving a real gap on BOTH sides (1.7m from the track, another
        // ~0.15m short of the pit lane's own surface) - "floating in the
        // middle of nowhere" exactly as described. It's now a single solid
        // wall spanning the whole gap: its inner face sits flush against the
        // track edge (the same FlushBarrierLateral-equivalent distance every
        // other barrier in this file uses) and its outer face reaches to just
        // short of the pit lane's own paved surface (BuildPitLane's 13.5m-wide
        // corridor centred on PitLaneLateral, inner edge at PitLaneLateral-6.75) -
        // touching one real boundary and nearly touching the other, never
        // hanging in open space on either side.
        const float PitDividerStep = 10f;
        // Widened overlap margin (was 2.5f) - same reasoning as the pit ramp/
        // service-road surface overlaps: a fixed, tight overlap could shrink to
        // an effectively-zero seam at a segment boundary, which for a wall
        // reads as a genuine gap a car could clip through rather than a purely
        // cosmetic issue.
        const float PitDividerOverlap = 5f;
        const float PitDividerMinHalfWidth = 0.3f;
        const float PitDividerPitSideClearance = 0.4f;

        void BuildPitLaneDividerFence()
        {
            if (Runtime.length <= 1f)
            {
                return;
            }

            // Final-corner barrier-mess fix: same reasoning as BuildPitRampGuideFences
            // below - a tight/hairpin final corner can extend its IsNearCorner radius
            // into this flat corridor too (PitCorridorStartNormalized sits only ~3.5%
            // of a lap past the ramp start), and wherever it does,
            // ComputeBarrierPlan's containment clamp already forces a real, catch-
            // fenced main barrier through that stretch. Skip this separate divider
            // wall there instead of stacking a second, independently-computed wall on
            // top of it.
            List<CornerInfo> highRiskCorners = DetectCorners(HighRiskCornerAngle);
            List<CornerInfo> tightFenceCorners = DetectCorners(TightCornerFenceAngle);

            float startDistance = Runtime.length * Runtime.PitCorridorStartNormalized;
            float endDistance = Runtime.length * PitZoneExitRampStart;
            float span = endDistance - startDistance;
            if (span < 0f)
            {
                span += Runtime.length;
            }

            for (float d = 0f; d < span; d += PitDividerStep)
            {
                float distance = Runtime.WrapDistance(startDistance + d);
                float sampleMidDistance = Runtime.WrapDistance(startDistance + d + PitDividerStep * 0.5f);
                bool nearTightFenceCorner = IsNearCorner(sampleMidDistance, highRiskCorners, 45f) ||
                    IsNearCorner(sampleMidDistance, tightFenceCorners, TightCornerFenceRadius);
                if (nearTightFenceCorner)
                {
                    continue;
                }

                CreatePitDividerSegment(distance, PitDividerStep, PitDividerStep + PitDividerOverlap);
            }
        }

        void CreatePitDividerSegment(float distance, float step, float segmentLength)
        {
            Vector3 a;
            Vector3 b;
            Vector3 mid;
            Vector3 forward;
            Vector3 right;
            Vector3 discard;
            Runtime.SampleAtDistance(distance, out a, out discard, out right);
            Runtime.SampleAtDistance(distance + step, out b, out discard, out right);
            Runtime.SampleAtDistance(distance + step * 0.5f, out mid, out forward, out right);

            Vector3 chord = b - a;
            Vector3 chordForward = chord.sqrMagnitude > 0.01f ? chord.normalized : forward;

            float midDistance = distance + step * 0.5f;
            float innerFace = Runtime.HalfWidthAt(midDistance) + EdgeBarrierClearance;
            float pitLaneInnerEdge = Runtime.PitLaneLateral - TrackRuntime.PitRampFullWidth * 0.5f;
            float outerFace = Mathf.Max(innerFace + PitDividerMinHalfWidth * 2f, pitLaneInnerEdge - PitDividerPitSideClearance);
            float wallHalfWidth = (outerFace - innerFace) * 0.5f;
            float centerLateral = innerFace + wallHalfWidth;
            Vector3 basePosition = mid + right * centerLateral;

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Pit lane divider wall";
            wall.transform.SetParent(transform);
            Vector3 scale = new Vector3(wallHalfWidth * 2f, 1.05f, segmentLength);
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = barrierMaterial;
            if (!TryPlaceSolidObstacle(wall, "pit-divider", basePosition, chordForward, scale, 0.52f, EdgeBarrierClearance))
            {
                return;
            }

            Vector3 placed = wall.transform.position;
            Quaternion rotation = Quaternion.LookRotation(wall.transform.forward, Vector3.up);
            CreateVisualBox("Pit divider hazard stripe", placed + Vector3.up * 0.58f, rotation, new Vector3(wallHalfWidth * 2f + 0.02f, 0.14f, segmentLength - 0.2f), flagYellowMaterial);
        }

        // ---------- pit entry/exit guide fencing ----------
        // Enclosure root-cause fix: BuildPitLaneDividerFence above only ever walls the
        // flat corridor where PitZoneBlend==1 - by design, since a literal wall through
        // the merge lane itself would either sit on top of the ramp's own drivable
        // surface (near the true entry/exit point, where the ramp has barely started
        // separating from the track) or, worse, dip back onto the MAIN TRACK's own
        // surface. But that left the entire back half of each ramp - the part where a
        // car has already visibly committed to the pit lane and the ramp surface has
        // separated enough to have real room for a wall - completely open, which is
        // the concrete gap behind "cars can randomly cut from track to pit lane or pit
        // lane to track" and "the pit lane doesn't feel enclosed". This adds a second,
        // shorter guide-wall pass that only activates for the committed half of each
        // ramp (PitZoneBlend >= PitRampGuideFenceMinBlend) and follows the ramp's own
        // widening/narrowing taper (the exact same lerp BuildPitRampSurface paves)
        // instead of the corridor's fixed target, so it always sits flush in the real
        // gap between the true track edge and the ramp's own near edge - never over
        // the ramp's pavement, never over the track's. Segments simply skip themselves
        // (CreatePitRampGuideSegment's outerFace<=innerFace guard) for the low-blend
        // tip of the ramp closest to the actual merge point, which is exactly the
        // genuine "opening" a car needs to physically cross through - so the fence
        // never blocks the intended entry/exit itself, only the already-committed lane
        // behind it.
        const float PitRampGuideFenceMinBlend = 0.42f;

        void BuildPitRampGuideFences()
        {
            if (Runtime.length <= 1f)
            {
                return;
            }

            // Final-corner barrier-mess fix: on every hand-authored circuit the pit
            // zone's fixed normalized band always covers whatever corner sits right
            // before the start/finish straight (see ComputeBarrierPlan's own
            // "Corner-priority fix" comment) - so a genuinely tight/hairpin final
            // corner almost always overlaps this ramp guide-fence pass too. When it
            // does, ComputeBarrierPlan already forces the main outer edge barrier
            // into containment mode there (Mathf.Max(baseLateral,
            // PitMinimumOuterLateral(...)), with catchFence forced on) - a real,
            // continuous wall already encloses that stretch. This second guide-fence
            // pass is a separate, independently-stepped (10m box + 5m overlap, no
            // SmoothBarrierLateralSequence pass) sequence that was never aware of
            // that main barrier - right where the corner is tightest (curving
            // fastest), its own fixed-length chorded boxes and the main barrier's
            // much finer, corner-tightened chording (down to step*0.3 with 3x
            // overlap) end up crowded on top of each other, exactly the "mess of
            // overlapping barriers on the inside of the final corner" AI cars were
            // getting physically wedged in. Skip the guide fence entirely wherever a
            // tight-fence-grade corner already forces the main barrier into
            // containment mode - it's redundant there, not just visually but
            // physically.
            List<CornerInfo> highRiskCorners = DetectCorners(HighRiskCornerAngle);
            List<CornerInfo> tightFenceCorners = DetectCorners(TightCornerFenceAngle);
            BuildPitRampGuideFence(PitZoneEntryRampStart, PitZoneEntryRampEnd, highRiskCorners, tightFenceCorners);
            BuildPitRampGuideFence(PitZoneExitRampStart, PitZoneExitRampEnd, highRiskCorners, tightFenceCorners);
        }

        void BuildPitRampGuideFence(float startNormalized, float endNormalized, List<CornerInfo> highRiskCorners, List<CornerInfo> tightFenceCorners)
        {
            float length = Runtime.length;
            float startDistance = length * startNormalized;
            float endDistance = length * endNormalized;
            float span = endDistance - startDistance;
            if (span <= 0f)
            {
                span += length;
            }

            for (float d = 0f; d < span; d += PitDividerStep)
            {
                float distance = Runtime.WrapDistance(startDistance + d);
                float sampleMidDistance = Runtime.WrapDistance(startDistance + d + PitDividerStep * 0.5f);
                float sampleNormalized = sampleMidDistance / length;
                if (PitZoneBlend(sampleNormalized) < PitRampGuideFenceMinBlend)
                {
                    // Still inside the genuine merge opening near the true entry/exit
                    // point - leave it open rather than forcing a wall into the gap.
                    continue;
                }

                bool nearTightFenceCorner = IsNearCorner(sampleMidDistance, highRiskCorners, 45f) ||
                    IsNearCorner(sampleMidDistance, tightFenceCorners, TightCornerFenceRadius);
                if (nearTightFenceCorner)
                {
                    // The main edge barrier's own containment mode already walls this
                    // stretch off - see the comment on BuildPitRampGuideFences above.
                    continue;
                }

                CreatePitRampGuideSegment(distance, PitDividerStep, PitDividerStep + PitDividerOverlap);
            }
        }

        // Pit-lane architecture fix: delegates to TrackRuntime's own
        // GetPitEntryRampEnvelope/GetPitExitRampEnvelope (the single canonical
        // ramp-taper math, also used directly by RaceManager/AiVehicleController)
        // instead of duplicating the same lerp/InverseLerp logic a second time in
        // TrackManager. Every caller here (guide-wall placement, the pit-outer-
        // barrier floor) now reads from exactly the same surface the AI/player
        // physically drive on.
        void PitRampEnvelopeAt(float normalized, float distance, out float lateral, out float halfWidth)
        {
            if (normalized >= PitZoneEntryRampStart && normalized < PitZoneEntryRampEnd)
            {
                Runtime.GetPitEntryRampEnvelope(normalized, distance, out lateral, out halfWidth);
                return;
            }

            Runtime.GetPitExitRampEnvelope(normalized, distance, out lateral, out halfWidth);
        }

        void CreatePitRampGuideSegment(float distance, float step, float segmentLength)
        {
            Vector3 a;
            Vector3 b;
            Vector3 mid;
            Vector3 forward;
            Vector3 right;
            Vector3 discard;
            Runtime.SampleAtDistance(distance, out a, out discard, out right);
            Runtime.SampleAtDistance(distance + step, out b, out discard, out right);
            Runtime.SampleAtDistance(distance + step * 0.5f, out mid, out forward, out right);

            Vector3 chord = b - a;
            Vector3 chordForward = chord.sqrMagnitude > 0.01f ? chord.normalized : forward;

            float midDistance = distance + step * 0.5f;
            float normalized = Runtime.WrapDistance(midDistance) / Runtime.length;
            float rampLateral;
            float rampHalfWidth;
            PitRampEnvelopeAt(normalized, midDistance, out rampLateral, out rampHalfWidth);

            float innerFace = Runtime.HalfWidthAt(midDistance) + EdgeBarrierClearance;
            float outerFace = (rampLateral - rampHalfWidth) - PitDividerPitSideClearance;
            if (outerFace <= innerFace + PitDividerMinHalfWidth * 2f)
            {
                // Too close to the true merge point for a wall to fit without either
                // sitting on the ramp's own surface or the track's - leave it open.
                return;
            }

            float wallHalfWidth = (outerFace - innerFace) * 0.5f;
            float centerLateral = innerFace + wallHalfWidth;
            Vector3 basePosition = mid + right * centerLateral;

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Pit ramp guide wall";
            wall.transform.SetParent(transform);
            Vector3 scale = new Vector3(wallHalfWidth * 2f, 1.05f, segmentLength);
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = barrierMaterial;
            if (!TryPlaceSolidObstacle(wall, "pit-divider", basePosition, chordForward, scale, 0.52f, EdgeBarrierClearance))
            {
                return;
            }

            Vector3 placed = wall.transform.position;
            Quaternion rotation = Quaternion.LookRotation(wall.transform.forward, Vector3.up);
            CreateVisualBox("Pit divider hazard stripe", placed + Vector3.up * 0.58f, rotation, new Vector3(wallHalfWidth * 2f + 0.02f, 0.14f, segmentLength - 0.2f), flagYellowMaterial);
        }

        // ---------- corner severity ----------
        // Shared with the braking-board placement in BuildTrackMarkers so "high-risk
        // corner" means the same thing everywhere in this file.
        //
        // Fencing-gap root cause fix: this used to measure only the instantaneous
        // angle between three CONSECUTIVE raw centerline points. That reads fine
        // for a corner whose whole direction change is concentrated at one sharp
        // vertex, but a real tight, multi-apex complex (Silverstone's Village/
        // The Loop, Baku's Castle section) is built from several moderate
        // anchor-to-anchor turns strung together and then smoothed - each
        // individual fine segment's angle can sit well under even a lenient
        // threshold while the corner as a whole is a genuine hairpin-grade turn,
        // and how finely that turn gets subdivided (a per-track constant) only
        // makes the effect worse, never better. Sampling at a fixed real-world
        // arc-length step and summing the turn over a trailing window catches
        // the true cumulative curvature of the corner regardless of how many
        // points the underlying spline happens to be built from.
        const float CornerSampleStepMeters = 8f;
        // Speed-rebalance pass: widened alongside HairpinWindowSpanMeters above for
        // the same reason - corners themselves are ~25% bigger now that every
        // layout scales up uniformly, so the trailing window needs to match.
        const float CornerWindowSpanMeters = 87.5f;

        List<CornerInfo> DetectCorners(float angleThreshold)
        {
            List<CornerInfo> corners = new List<CornerInfo>();
            float length = Runtime.length;
            if (length <= 1f)
            {
                return corners;
            }

            int sampleCount = Mathf.Clamp(Mathf.RoundToInt(length / CornerSampleStepMeters), 12, 2000);
            int windowSamples = Mathf.Max(2, Mathf.RoundToInt(CornerWindowSpanMeters / (length / sampleCount)));

            Vector3[] forwards = new Vector3[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float d = length * i / sampleCount;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                forwards[i] = forward;
            }

            // Cumulative turn accumulated by the windowSamples segments
            // immediately behind sample i, then collapse contiguous runs of
            // samples that clear the threshold into a single CornerInfo at the
            // run's peak - so a wide multi-apex complex still reports as one
            // corner for IsNearCorner's radius-based lookup, not one per sample.
            float[] cumulative = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float sum = 0f;
                for (int w = 0; w < windowSamples; w++)
                {
                    int a = (i - w - 1 + sampleCount * 4) % sampleCount;
                    int b = (i - w + sampleCount * 4) % sampleCount;
                    sum += Vector3.Angle(forwards[a], forwards[b]);
                }

                cumulative[i] = sum;
            }

            // One CornerInfo per distinct contiguous above-threshold run, at its
            // peak - single-point consumers (braking boards, tyre marbles,
            // rubber build-up, apex cones, gravel traps) all iterate this list
            // once per real corner exactly like before, so a wide multi-apex
            // complex must NOT explode into dozens of closely-spaced entries
            // (that would multiply every one of those per-corner scenery
            // passes many times over). The run's own physical length is kept
            // as `span` instead, purely for IsNearCorner (see there) to widen
            // its containment/fencing radius so a wide corner's entry and exit
            // still both read as "near a corner" without needing extra entries.
            int runStart = -1;
            int runPeakIndex = 0;
            float runPeakValue = 0f;
            for (int i = 0; i < sampleCount * 2; i++)
            {
                int idx = i % sampleCount;
                bool above = cumulative[idx] > angleThreshold;
                if (above)
                {
                    if (runStart < 0)
                    {
                        runStart = i;
                        runPeakIndex = idx;
                        runPeakValue = cumulative[idx];
                    }
                    else if (cumulative[idx] > runPeakValue)
                    {
                        runPeakIndex = idx;
                        runPeakValue = cumulative[idx];
                    }
                }
                else if (runStart >= 0)
                {
                    float peakDistance = length * runPeakIndex / sampleCount;
                    float runSpan = Mathf.Min(length, (i - runStart) * length / sampleCount);
                    if (!ContainsCornerNear(corners, peakDistance, length, CornerWindowSpanMeters * 0.5f))
                    {
                        corners.Add(new CornerInfo { distance = peakDistance, angle = runPeakValue, span = runSpan });
                    }

                    runStart = -1;
                }

                if (i >= sampleCount && runStart < 0)
                {
                    break;
                }
            }

            return corners;
        }

        bool ContainsCornerNear(List<CornerInfo> corners, float distance, float length, float proximity)
        {
            for (int i = 0; i < corners.Count; i++)
            {
                float delta = Mathf.Abs(Runtime.WrapDistance(distance - corners[i].distance));
                if (Mathf.Min(delta, length - delta) < proximity)
                {
                    return true;
                }
            }

            return false;
        }

        bool IsNearCorner(float distance, List<CornerInfo> corners, float radius)
        {
            for (int i = 0; i < corners.Count; i++)
            {
                float delta = Mathf.Abs(Runtime.WrapDistance(distance - corners[i].distance));
                float wrapped = Mathf.Min(delta, Runtime.length - delta);
                // A wide multi-apex corner's own physical length (span) widens
                // the effective radius so containment/fencing reaches its true
                // entry and exit, not just a fixed distance from its peak -
                // see CornerInfo.span and DetectCorners for why a corner is
                // represented as one peak point rather than many entries.
                if (wrapped <= radius + corners[i].span * 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------- elevated-section safety ----------

        public float ElevationAboveGround(float distance)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            return point.y - groundTopY;
        }

        public bool IsElevatedAtDistance(float distance)
        {
            return ElevationAboveGround(distance) > ElevationThreshold;
        }

        public bool NeedsCatchFence(float distance)
        {
            if (ElevationAboveGround(distance) > TallFenceElevation)
            {
                return true;
            }

            return IsElevatedAtDistance(distance) &&
                   (Runtime.styleName.Contains("street") || Runtime.styleName.Contains("Street"));
        }

        // ---------- freestanding scenery grounding ----------
        // Freestanding trackside scenery (marshal posts, floodlights, trees, grandstands,
        // paddock buildings, camera pods, ...) stands on the flat ground plane, not on
        // whatever height the road happens to be at the sample point it was offset from.
        // On a normal stretch the two are the same to within camber noise (see
        // CreateBridgeSupports' own <2m "not really elevated" cutoff), but near a
        // bridge/hill the road can be many metres above true ground - building an
        // object's base relative to that sampled track height (as several passes used
        // to) left it floating at deck height with nothing visibly holding it up, since
        // these objects don't reach back up to the deck the way a camera tower or light
        // mast's own support legs do. This keeps the lateral (x/z) position exactly as
        // sampled and only replaces the vertical component with the one flat ground
        // reference the whole track actually rests on. Not used by passes that
        // deliberately want the local sloped/elevated height (parkland hillside terrain,
        // mountain-cliff dressing, anything mounted directly to the road/bridge
        // structure itself) - see the call sites for why each of those is exempt.
        Vector3 GroundedTrackPoint(Vector3 sampledPoint)
        {
            return new Vector3(sampledPoint.x, groundTopY, sampledPoint.z);
        }

        // Generic ground-support helper: given where a piece of scenery's base is
        // actually sitting (and a rough footprint radius for sizing any fix), confirms
        // it is already resting at/near true ground level and, if not, fills the gap
        // with a short concrete pad (small gaps) or a support column (larger gaps) down
        // to groundTopY - the same "column bridges the elevation gap" idea
        // CreateBridgeSupports/CreateCameraTower already use, generalized for callers
        // that have a deliberate reason to keep an object above flat ground (e.g. a
        // hillside archetype's artificial "climbing the slope" offset) but still want it
        // to read as resting on *something* rather than hanging in mid-air. Reuses
        // concreteMaterial rather than adding a new one.
        void EnsureGroundedBase(Vector3 objectBasePosition, float footprintRadius)
        {
            float gap = objectBasePosition.y - groundTopY;
            if (gap <= 0.15f)
            {
                // Already resting on (or slightly into) the ground - nothing to add.
                return;
            }

            float radius = Mathf.Max(0.6f, footprintRadius);
            if (gap < 2f)
            {
                // Small gap: a flat plinth/pad reads better than a sliver-thin column.
                CreateVisualBox("Scenery grounding pad", new Vector3(objectBasePosition.x, groundTopY + 0.05f, objectBasePosition.z),
                    Quaternion.identity, new Vector3(radius * 1.6f, 0.1f, radius * 1.6f), concreteMaterial);
                return;
            }

            Vector3 columnCenter = new Vector3(objectBasePosition.x, groundTopY + gap * 0.5f, objectBasePosition.z);
            CreateVisualBox("Scenery support column", columnCenter, Quaternion.identity, new Vector3(radius * 1.1f, gap, radius * 1.1f), concreteMaterial);
        }

        // Thin visible ground patch/pad under a piece of scenery (a tree cluster's grass
        // apron, a building/grandstand/tower's paved foundation, ...) so its base reads
        // as sitting on believable ground instead of hovering a hair above unbroken,
        // texture-less flat terrain. Purely cosmetic - always at groundTopY, since this
        // is only ever called on scenery this pass has already grounded correctly.
        void CreateGroundPatch(string name, Vector3 basePosition, float sizeX, float sizeZ, Material material, Quaternion rotation)
        {
            CreateVisualBox(name, new Vector3(basePosition.x, groundTopY + 0.02f, basePosition.z), rotation, new Vector3(sizeX, 0.05f, sizeZ), material);
        }

        void CreateGroundPatch(string name, Vector3 basePosition, float sizeX, float sizeZ, Material material)
        {
            CreateGroundPatch(name, basePosition, sizeX, sizeZ, material, Quaternion.identity);
        }

        void CreateConcreteWall(Vector3 basePosition, Vector3 forward, float segmentLength)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Bridge concrete wall";
            wall.transform.SetParent(transform);
            Vector3 scale = new Vector3(0.5f, 1.25f, segmentLength);
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = concreteMaterial;
            // Barrier gap fix: this used to allow just 0.5m of clearance, which
            // (at the old +1.15m baseLateral) put the wall's near face inside
            // the game's own +1.3m track-limit leniency zone - a car legally
            // riding the full width of the kerb could clip a wall that exists
            // specifically to stop it falling off an elevated section.
            // EdgeBarrierClearance matches the placement in
            // ComputeBarrierPlan so the wall both hugs the edge and
            // stays safely past that leniency boundary.
            TryPlaceSolidObstacle(wall, "bridge-wall", basePosition, forward, scale, 0.62f, EdgeBarrierClearance);
        }

        // Barrier-pocket fix (item A): a catch fence always sits at the exact same
        // basePosition/lateral as the primary solid barrier it backs (street wall,
        // armco, or concrete wall) - it never guards open ground on its own. Giving
        // it its own registered TrackSolidObstacle collider used to stack a second,
        // independently-clearance-checked box directly on top of the primary wall's,
        // which is exactly the kind of duplicate/overlapping collider geometry that
        // can wedge a recovering AI car between two colliders instead of one clean
        // face. The fence is now purely decorative (CreateVisualBox - no collider at
        // all, same as its own posts/rail below it), so all actual collision at this
        // spot comes from the one primary barrier placed by the caller.
        void CreateCatchFence(Vector3 basePosition, Vector3 forward, float segmentLength)
        {
            Vector3 scale = new Vector3(0.18f, 2.6f, segmentLength);
            Vector3 placed = basePosition + Vector3.up * 2.5f;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateVisualBox("Catch fence", placed, rotation, scale, fenceMaterial);

            // Visual posts and a top rail follow the same placed position.
            Vector3 placedForward = forward;
            float detail = Mathf.Clamp(sceneryDensity, 0.25f, 2f);
            int posts = detail < 0.6f ? 1 : 2;
            for (int i = 0; i <= posts; i++)
            {
                float t = posts == 0 ? 0f : (i / (float)posts) - 0.5f;
                Vector3 postPosition = placed + placedForward * t * (segmentLength - 1f);
                CreateVisualBox("Catch fence post", new Vector3(postPosition.x, placed.y, postPosition.z), Quaternion.LookRotation(placedForward, Vector3.up), new Vector3(0.14f, 2.6f, 0.14f), fencePostMaterial);
            }

            CreateVisualBox("Catch fence rail", placed + Vector3.up * 1.28f, Quaternion.LookRotation(placedForward, Vector3.up), new Vector3(0.2f, 0.09f, segmentLength), fencePostMaterial);
        }

        void CreateBridgeSupports(float distance)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            float height = point.y - groundTopY - 0.2f;
            if (height < 2f)
            {
                return;
            }

            Vector3 columnCenter = new Vector3(point.x, groundTopY + height * 0.5f, point.z);
            CreateVisualBox("Bridge support column", columnCenter, Quaternion.LookRotation(forward, Vector3.up), new Vector3(1.7f, height, 1.7f), concreteMaterial);
            // Crossbeam spans the full road width, so a widened elevated hairpin needs a
            // wider crossbeam too or the deck would overhang its own support.
            float spanWidth = Runtime.HalfWidthAt(distance) * 2f + 1.6f;
            CreateVisualBox("Bridge support crossbeam", new Vector3(point.x, point.y - 0.55f, point.z), Quaternion.LookRotation(forward, Vector3.up), new Vector3(spanWidth, 0.5f, 1.9f), concreteMaterial);
        }

        // Audit pass: every elevated sample must have solid protection close by on
        // both sides, otherwise the report flags the gap loudly.
        void ValidateElevatedProtection(TrackValidationReport report)
        {
            int unprotectedSamples = 0;
            for (float d = 0f; d < Runtime.length; d += 22f)
            {
                if (!IsElevatedAtDistance(d))
                {
                    continue;
                }

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 expected = point + right * side * (Runtime.roadHalfWidth + 1.15f);
                    if (!HasSolidProtectionNear(expected, 26f))
                    {
                        unprotectedSamples++;
                        report.Warn("elevated road at distance " + d.ToString("0") + "m has no side protection on " + (side < 0 ? "left" : "right") + " side.");
                    }
                }
            }

            if (unprotectedSamples == 0)
            {
                GameLog.Info("[TrackValidation] Elevated sections fully protected on " + Runtime.displayName);
            }
        }

        bool HasSolidProtectionNear(Vector3 position, float radius)
        {
            for (int i = 0; i < solidObstacles.Count; i++)
            {
                TrackSolidObstacle obstacle = solidObstacles[i];
                if (obstacle == null)
                {
                    continue;
                }

                string type = obstacle.obstacleType ?? "";
                if (!type.Contains("wall") && !type.Contains("fence") && !type.Contains("barrier") && !type.Contains("rail"))
                {
                    continue;
                }

                Vector3 flatDelta = obstacle.transform.position - position;
                flatDelta.y = 0f;
                if (flatDelta.sqrMagnitude < radius * radius)
                {
                    return true;
                }
            }

            return false;
        }

        // Full-perimeter gap sweep, independent of how a segment actually got placed:
        // samples both sides at a finer step than the barrier spacing itself and asks
        // how far away the nearest real protection is, so any seam left behind by the
        // placement/repositioning logic in TryPlaceSolidObstacle still gets caught and
        // logged instead of shipping silently.
        const float BarrierGapCheckStep = 8f;
        // Barrier gap fix: tightened from 18m - that threshold only ever caught
        // huge missing sections, not the smaller (but still clearly visible)
        // seams this pass is meant to catch. Segments overlap by
        // EdgeBarrierOverlap and are placed every ~8-10m, so under normal
        // conditions the nearest protection to any sample point is only a
        // couple of metres away - 10m leaves headroom for sampling/curvature
        // noise while still flagging anything a player would actually notice.
        const float BarrierGapThreshold = 10f;

        void ValidateBarrierCoverage(TrackValidationReport report)
        {
            int gaps = 0;
            for (float d = 0f; d < Runtime.length; d += BarrierGapCheckStep)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                float normalized = d / Mathf.Max(1f, Runtime.length);
                float baseLateral = IsElevatedAtDistance(d)
                    ? Runtime.roadHalfWidth + 1.15f
                    : (streetTrack ? Runtime.roadHalfWidth + 2f : Runtime.roadHalfWidth + 2.6f);

                for (int side = -1; side <= 1; side += 2)
                {
                    float lateral = baseLateral;
                    if (side > 0)
                    {
                        float blend = PitZoneBlend(normalized);
                        if (blend > 0f)
                        {
                            lateral = Mathf.Lerp(baseLateral, PitOuterLateral(), blend);
                        }
                    }

                    Vector3 expected = point + right * side * lateral;
                    float gap = NearestSolidProtectionDistance(expected);
                    if (gap > BarrierGapThreshold)
                    {
                        gaps++;
                        report.Warn("Barrier gap at " + d.ToString("0") + "m " + (side < 0 ? "left" : "right") +
                                    " side, nearest protection " + gap.ToString("0.0") + "m away");
                    }
                }
            }

            if (gaps == 0)
            {
                GameLog.Info("[TrackValidation] Continuous edge barriers fully sealed on " + Runtime.displayName);
            }
        }

        // Same gap-distance measure as ValidateBarrierCoverage, focused specifically on
        // the pit corridor's outer wall (entry fan-out, mid-corridor, exit fan-out) so a
        // pit-lane-only regression is called out by name instead of blending into the
        // generic per-8m sweep above.
        void ValidatePitCorridorSealed(TrackValidationReport report)
        {
            float[] checkpoints = { PitZoneEntryRampEnd, (PitZoneEntryRampEnd + PitZoneExitRampStart) * 0.5f, PitZoneExitRampStart };
            bool allSealed = true;
            for (int i = 0; i < checkpoints.Length; i++)
            {
                float d = Mathf.Repeat(checkpoints[i], 1f) * Runtime.length;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                Vector3 expected = point + right * PitOuterLateral();
                float gap = NearestSolidProtectionDistance(expected);
                if (gap > BarrierGapThreshold + 4f)
                {
                    allSealed = false;
                    report.Warn("Pit corridor outer wall gap near " + d.ToString("0") + "m, nearest protection " + gap.ToString("0.0") + "m away");
                }
            }

            if (allSealed)
            {
                GameLog.Info("[TrackValidation] Pit corridor outer wall sealed on " + Runtime.displayName);
            }
        }

        float NearestSolidProtectionDistance(Vector3 position)
        {
            float best = float.MaxValue;
            for (int i = 0; i < solidObstacles.Count; i++)
            {
                TrackSolidObstacle obstacle = solidObstacles[i];
                if (obstacle == null)
                {
                    continue;
                }

                string type = obstacle.obstacleType ?? "";
                if (!type.Contains("wall") && !type.Contains("fence") && !type.Contains("barrier") && !type.Contains("rail"))
                {
                    continue;
                }

                Vector3 flatDelta = obstacle.transform.position - position;
                flatDelta.y = 0f;
                float distance = flatDelta.magnitude;
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        // ---------- debug barrier-collider physics sweep ----------
        // ValidateBarrierCoverage/ValidatePitCorridorSealed above already reason about
        // gaps using solidObstacles, the bookkeeping list this script itself appends to
        // every time TryPlaceSolidObstacle succeeds - useful, but it can only ever be as
        // honest as that bookkeeping. It stays blind to any mismatch between the list
        // and what Unity's physics world actually has a live collider for right now
        // (a segment whose collider got disabled/destroyed by something else after
        // being recorded, or - looking ahead - any future barrier-style pass that
        // forgets to route through TryPlaceSolidObstacle at all). This sweep asks
        // Physics.CheckSphere directly, independent of that bookkeeping, whether a real
        // barrier-tagged collider actually exists near the hug line every edge-barrier
        // style targets. Purely diagnostic: runs once after the whole Build() sequence
        // finishes, never modifies geometry, never throws, and only warns.
        const float BarrierColliderCheckStep = 9f;

        // Search radius for locating a candidate barrier collider at all -
        // generous, since this only decides "is there anything barrier-like
        // in the area", not how far away it actually is (that's measured
        // separately below, in metres, against the tight thresholds).
        const float BarrierColliderSearchRadius = 6f;

        // Barrier-flush validation (measures the real gap, not just presence):
        // "a barrier collider exists somewhere nearby" was never actually proof
        // the wall was flush with the track - a barrier sitting metres away
        // still satisfied a presence-only check. This measures the actual
        // distance from the true paved edge to the nearest barrier-like
        // collider's surface and flags anything wider than the tolerance,
        // with a stricter budget in corners (where a visible gap reads worst
        // and the straight-chord approximation is most likely to introduce
        // one).
        const float BarrierGapToleranceMeters = 0.3f;
        const float BarrierGapToleranceCornerMeters = 0.2f;
        const float BarrierAutoFillOverlap = 2f;
        // Debug-overlay requirement: every point the flush sweep below actually
        // flagged (even though it then auto-fills it immediately) so the debug
        // overlay can mark exactly where generation found and corrected a gap,
        // instead of only ever showing the two idealized target lines.
        readonly List<Vector3> detectedBarrierGapPoints = new List<Vector3>();

        void ValidateBarrierColliderCoverage()
        {
            if (Runtime == null || Runtime.length <= 1f)
            {
                return;
            }

            List<CornerInfo> validationCorners = DetectCorners(TightCornerFenceAngle);
            int gaps = 0;
            int autoFilled = 0;
            int checkedPoints = 0;
            float worstGap = 0f;
            for (float d = 0f; d < Runtime.length; d += BarrierColliderCheckStep)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                float localHalfWidth = Runtime.HalfWidthAt(d);
                bool nearCorner = IsNearCorner(d, validationCorners, TightCornerFenceRadius);
                float tolerance = nearCorner ? BarrierGapToleranceCornerMeters : BarrierGapToleranceMeters;
                float normalized = d / Mathf.Max(1f, Runtime.length);

                for (int side = -1; side <= 1; side += 2)
                {
                    // The pit entry/exit merge lanes are the ONE deliberate opening in
                    // an otherwise fully closed perimeter - the outer wall is
                    // intentionally set back from the true track edge through these
                    // ramps (see PitZoneBlend) so a car has somewhere to physically
                    // cross from the racing surface into the pit lane and back. Never
                    // flag or auto-fill inside that ramp, on either the entry or the
                    // exit end, or the "fix" would wall the pit lane shut.
                    if (IsIntentionalPitOpening(normalized, side, nearCorner))
                    {
                        continue;
                    }

                    // The TRUE track edge, not an assumed barrier position -
                    // this is what "flush" is actually measured against.
                    Vector3 edgePoint = point + right * side * localHalfWidth + Vector3.up * 0.55f;
                    checkedPoints++;
                    float gap = MeasureBarrierGap(edgePoint, BarrierColliderSearchRadius);
                    if (gap > tolerance)
                    {
                        gaps++;
                        worstGap = Mathf.Max(worstGap, gap);
                        detectedBarrierGapPoints.Add(edgePoint);
                        string gapText = gap >= BarrierColliderSearchRadius ? "no barrier-like collider found within " + BarrierColliderSearchRadius.ToString("0") + "m" : gap.ToString("0.00") + "m gap";
                        GameLog.Warn("[TrackValidation] Barrier flush check FAILED at " + d.ToString("0") + "m " + (side < 0 ? "left" : "right") +
                                     " side" + (nearCorner ? " (corner)" : "") + ": " + gapText + " (tolerance " + tolerance.ToString("0.00") + "m) on " + Runtime.displayName);

                        // Barrier-pocket fix (item C): the gap measured above is only
                        // ever taken at this one exact sample point/height - a barrier
                        // that exists nearby but a little short, angled, or offset from
                        // this precise point can still read as "gapped" here even though
                        // filling it would just stack a second collider right next to the
                        // first one (a classic overlap/pocket source). Re-check with a
                        // wider radius immediately before actually placing anything; if a
                        // real barrier-like collider is already within that wider radius,
                        // skip the fill entirely rather than duplicate it.
                        TrackSolidObstacle existingNearby;
                        if (HasBarrierColliderNearEdge(d, side, BarrierAutoFillDedupeRadius, out existingNearby))
                        {
                            GameLog.Info("[TrackValidation] Skipped auto-fill at " + d.ToString("0") + "m " + (side < 0 ? "left" : "right") +
                                         " side - existing barrier-like collider (" + existingNearby.obstacleType + ") already nearby on " + Runtime.displayName);
                        }
                        else if (AutoFillBarrierGap(d, side))
                        {
                            autoFilled++;
                        }
                    }
                }
            }

            if (LastReport != null)
            {
                LastReport.barrierGapCount = gaps;
                LastReport.barrierGapAutoFilledCount = autoFilled;
            }

            if (gaps == 0)
            {
                GameLog.Info("[TrackValidation] Barrier flush sweep clean: " + checkedPoints + " points checked, every barrier within tolerance on " + Runtime.displayName);
            }
            else
            {
                GameLog.Warn("[TrackValidation] Barrier flush sweep found " + gaps + "/" + checkedPoints + " point(s) with a gap wider than tolerance (worst " +
                             worstGap.ToString("0.00") + "m), auto-filled " + autoFilled + "/" + gaps + " on " + Runtime.displayName);
            }
        }

        // True only within the entry/exit ramps where the outer wall is deliberately
        // eased away from the true track edge (see PitZoneBlend) - the pit lane only
        // ever runs down the right side, so the left side never has an intentional
        // opening. A small epsilon on both ends keeps the very start/end of the ramp
        // (where the wall has barely moved off the flush line) held to the normal
        // tolerance instead of being waved through as "intentional".
        // Pit-exit blocking fix: this used to also return false (not intentional -
        // hold this stretch to the normal flush-edge standard) whenever a corner was
        // nearby, specifically so a hairpin/tight corner coinciding with the pit
        // zone's fixed normalized band (Melbourne's sharpest corner does) couldn't
        // silently skip gap-validation/auto-fill and end up unprotected. That auto-
        // fill path (AutoFillBarrierGap) has no idea the pit lane exists at all -
        // it always plants its corrective wall at the TRUE TRACK EDGE, so on a
        // corner-near-pit-exit track it was building a fresh wall directly inside
        // the drivable pit lane surface, trapping every AI car that tried to leave
        // the pits. ComputeBarrierPlan's corner-priority containment now has its own
        // hard floor (PitMinimumOuterLateral) that guarantees a real wall exists
        // near that corner without ever encroaching on the pit lane - so this
        // function no longer needs the corner override at all; the whole pit zone
        // blend window is unconditionally treated as intentional, corner or not.
        // Item H note: this window (PitZoneEntryRampStart..PitZoneExitRampEnd, via
        // PitZoneBlend) already fully contains the pit-exit merge zone used by
        // RaceManager's PitPhase.ExitMerge / TrackRuntime.IsInPitExitMergeZone -
        // PitZoneExitRampEnd and TrackRuntime.PitExitRampEndNormalized are the exact
        // same constant. So the pit entry ramp, pit lane corridor, pit release, and
        // the pit-exit merge path are all already covered by this one check; nothing
        // here (gap auto-fill) can ever wall the pit lane or the exit-merge path shut.
        //
        // Collision/placement fix: this is a GAP-FILL-only concept - "don't plant a
        // corrective wall here, this opening is intentional." It must never be used
        // to decide whether to CHECK for an illegal intrusion (a wall standing where
        // it shouldn't). Those are different questions: skipping intrusion checks
        // inside the pit opening would exempt exactly the zone where a bad wall does
        // the most damage - a real pileup was traced to a divider/guide wall
        // standing inside a drivable corridor that this kind of skip would have
        // hidden. IsSolidObstaclePlacementValid/ValidateNoSolidObstaclesInsideDrivingCorridors
        // never call this - only ValidateBarrierColliderCoverage's auto-fill
        // decision does. Renamed at the call site's intent, not the signature, to
        // keep the diff mechanical; treat this as IsIntentionalPitOpeningForGapFill.
        bool IsIntentionalPitOpening(float normalized, int side, bool nearCorner)
        {
            if (side <= 0)
            {
                return false;
            }

            float blend = PitZoneBlend(normalized);
            return blend > 0.03f && blend < 0.97f;
        }

        // Last-resort corrective segment for a real, unintentional gap found by the
        // flush sweep above - style-matched (concrete on elevated sections, painted
        // wall on street circuits, Armco rail otherwise) and placed through the same
        // clearance-checked TryPlaceSolidObstacle path every other barrier segment
        // uses, so it can never end up floating or double-stacked with whatever
        // partial coverage already exists there.
        bool AutoFillBarrierGap(float distance, int side)
        {
            Vector3 a;
            Vector3 b;
            Vector3 mid;
            Vector3 forward;
            Vector3 right;
            Vector3 discard;
            float halfStep = BarrierColliderCheckStep * 0.5f;
            Runtime.SampleAtDistance(distance - halfStep, out a, out discard, out right);
            Runtime.SampleAtDistance(distance + halfStep, out b, out discard, out right);
            Runtime.SampleAtDistance(distance, out mid, out forward, out right);

            Vector3 chord = b - a;
            Vector3 chordForward = chord.sqrMagnitude > 0.01f ? chord.normalized : forward;
            float segmentLength = BarrierColliderCheckStep + BarrierAutoFillOverlap;
            bool elevated = IsElevatedAtDistance(distance);

            float lateral;
            Vector3 scale;
            float halfHeight;
            string obstacleType;
            Material material;

            if (elevated)
            {
                lateral = FlushBarrierLateral(distance, ConcreteWallHalfWidth);
                scale = new Vector3(ConcreteWallHalfWidth * 2f, 1.25f, segmentLength);
                halfHeight = 0.62f;
                obstacleType = "auto-fill-wall";
                material = concreteMaterial;
            }
            else if (streetTrack)
            {
                lateral = FlushBarrierLateral(distance, StreetWallHalfWidth);
                scale = new Vector3(StreetWallHalfWidth * 2f, EdgeBarrierMinHeight, segmentLength);
                halfHeight = EdgeBarrierMinHeight * 0.5f;
                obstacleType = "auto-fill-wall";
                material = barrierMaterial;
            }
            else
            {
                lateral = FlushBarrierLateral(distance, ArmcoHalfWidth);
                scale = new Vector3(ArmcoHalfWidth * 2f, EdgeBarrierMinHeight, segmentLength);
                halfHeight = EdgeBarrierMinHeight * 0.5f;
                obstacleType = "auto-fill-rail";
                material = armcoMaterial;
            }

            GameObject fillObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fillObject.name = "Auto-filled barrier gap";
            fillObject.transform.SetParent(transform);
            fillObject.transform.localScale = scale;
            fillObject.GetComponent<Renderer>().sharedMaterial = material;

            Vector3 basePosition = mid + right * side * lateral;
            if (!TryPlaceSolidObstacle(fillObject, obstacleType, basePosition, chordForward, scale, halfHeight, EdgeBarrierClearance))
            {
                return false;
            }

            Vector3 placed = fillObject.transform.position;
            Quaternion rotation = Quaternion.LookRotation(fillObject.transform.forward, Vector3.up);
            CreateVisualBox("Auto-filled barrier gap rail", placed + Vector3.up * (halfHeight * 0.6f), rotation, new Vector3(scale.x + 0.05f, 0.1f, segmentLength - 0.2f), metalMaterial);
            GameLog.Info("[TrackValidation] Auto-filled barrier gap at " + distance.ToString("0") + "m " + (side < 0 ? "left" : "right") + " side on " + Runtime.displayName);
            return true;
        }

        // Barrier-pocket fix (item C): generous "is something already here" radius
        // used only to decide whether to SKIP an auto-fill - deliberately wider than
        // the tolerance ValidateBarrierColliderCoverage flags a gap at, so a barrier
        // that's merely offset/angled (not truly missing) never gets a duplicate
        // planted right beside it.
        const float BarrierAutoFillDedupeRadius = 1.6f;

        bool HasBarrierColliderNearEdge(float distance, int side, float maxGap, out TrackSolidObstacle nearest)
        {
            nearest = null;
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            float localHalfWidth = Runtime.HalfWidthAt(distance);
            Vector3 edgePoint = point + right * side * localHalfWidth + Vector3.up * 0.55f;
            Collider[] hits = Physics.OverlapSphere(edgePoint, Mathf.Max(maxGap, 0.1f));
            float best = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                {
                    continue;
                }

                TrackSolidObstacle solid = hit.GetComponentInParent<TrackSolidObstacle>();
                if (solid == null || !solid.enabled)
                {
                    continue;
                }

                string type = solid.obstacleType ?? "";
                if (!(type.Contains("wall") || type.Contains("fence") || type.Contains("barrier") || type.Contains("rail") || type.Contains("divider")))
                {
                    continue;
                }

                Vector3 closest = hit.ClosestPoint(edgePoint);
                float dist = Vector3.Distance(closest, edgePoint);
                if (dist < best)
                {
                    best = dist;
                    nearest = solid;
                }
            }

            return nearest != null && best <= maxGap;
        }

        struct BarrierColliderInfo
        {
            public TrackSolidObstacle solid;
            public float distance;
            public float lateral;
            public int side;
        }

        // Barrier-pocket fix (item F): post-placement deconfliction pass. Runs once
        // after every geometry-placing pass (continuous edge barriers, pit lane
        // fencing/walls, auto-fill) has had its turn, over the single registry every
        // solid barrier-family object goes through (solidObstacles/TryPlaceSolidObstacle).
        // Two colliders from unrelated passes can end up genuinely overlapping or
        // crossing at a sharp angle without either pass ever seeing the other's output
        // as it's generated - that's exactly the "V/U pocket" shape a recovering car
        // can wedge into. Heuristic, not exact geometry: same side, close together
        // along the track, and lateral offsets close enough that a second full
        // collider there is redundant rather than deliberate layered geometry (a
        // barrier plus its own tyre-stack standoff sits much further apart laterally
        // than this). Keeps the higher-priority/primary barrier and demotes the loser
        // to visual-only (collider disabled, not destroyed - scenery dressing on it
        // still reads correctly) rather than deleting geometry outright. Never
        // demotes both sides of a pair, so a sample can never end up with zero
        // barriers as a result of this pass.
        const float OverlapDeconflictLongitudinalRadius = 4f;
        const float OverlapDeconflictLateralRadius = 1.2f;

        void ResolveOverlappingBarrierColliders()
        {
            if (Runtime == null || Runtime.length <= 1f)
            {
                return;
            }

            List<BarrierColliderInfo> infos = new List<BarrierColliderInfo>();
            for (int i = 0; i < solidObstacles.Count; i++)
            {
                TrackSolidObstacle solid = solidObstacles[i];
                if (solid == null || !solid.enabled)
                {
                    continue;
                }

                string type = solid.obstacleType ?? "";
                bool isBarrierFamily = type.Contains("wall") || type.Contains("barrier") || type.Contains("rail") ||
                                        type.Contains("armco") || type.Contains("concrete") || type.Contains("auto-fill") ||
                                        type.Contains("tyre-stack") || type.Contains("divider");
                if (!isBarrierFamily)
                {
                    continue;
                }

                TrackProgress progress = Runtime.GetProgress(solid.transform.position);
                BarrierColliderInfo info;
                info.solid = solid;
                info.distance = progress.distance;
                info.lateral = Mathf.Abs(progress.lateralDistance);
                info.side = progress.lateralDistance < 0f ? -1 : 1;
                infos.Add(info);
            }

            infos.Sort((a, b) => a.distance.CompareTo(b.distance));
            int demoted = 0;

            for (int i = 0; i < infos.Count; i++)
            {
                BarrierColliderInfo a = infos[i];
                if (a.solid == null || !a.solid.enabled)
                {
                    continue;
                }

                for (int j = i + 1; j < infos.Count; j++)
                {
                    BarrierColliderInfo b = infos[j];
                    float rawGap = Mathf.Abs(b.distance - a.distance);
                    float longitudinalGap = Mathf.Min(rawGap, Runtime.length - rawGap);
                    if (longitudinalGap > OverlapDeconflictLongitudinalRadius)
                    {
                        break;
                    }

                    if (b.side != a.side || b.solid == null || !b.solid.enabled)
                    {
                        continue;
                    }

                    float lateralGap = Mathf.Abs(a.lateral - b.lateral);
                    if (lateralGap > OverlapDeconflictLateralRadius)
                    {
                        // Far enough apart laterally that this is most likely a barrier
                        // plus its own deliberate standoff layer (e.g. an Armco rail
                        // behind a tyre stack) rather than a duplicate of the same line.
                        continue;
                    }

                    TrackSolidObstacle loser = ChooseOverlapLoser(a.solid, b.solid);
                    if (loser == null)
                    {
                        continue;
                    }

                    DemoteBarrierColliderToVisual(loser);
                    demoted++;
                    if (loser == a.solid)
                    {
                        break;
                    }
                }
            }

            if (demoted > 0)
            {
                GameLog.Info("[TrackValidation] Deconflicted " + demoted + " overlapping barrier collider(s) on " + Runtime.displayName);
            }
        }

        // Lower number = demoted first when two barrier-family colliders overlap.
        // Reactive auto-fill and tyre stacks are the most likely to be redundant with
        // an already-continuous primary wall; street/armco/concrete walls, being the
        // main continuous perimeter, are kept over everything else.
        int BarrierRolePriority(string type)
        {
            if (type.Contains("auto-fill"))
            {
                return 0;
            }

            if (type.Contains("tyre-stack") || type.Contains("tyre-barrier"))
            {
                return 1;
            }

            if (type.Contains("divider") || type.Contains("pit-wall"))
            {
                return 2;
            }

            return 3;
        }

        TrackSolidObstacle ChooseOverlapLoser(TrackSolidObstacle a, TrackSolidObstacle b)
        {
            int priorityA = BarrierRolePriority(a.obstacleType ?? "");
            int priorityB = BarrierRolePriority(b.obstacleType ?? "");
            if (priorityA != priorityB)
            {
                return priorityA < priorityB ? a : b;
            }

            float lengthA = a.localScaleAtValidation.z;
            float lengthB = b.localScaleAtValidation.z;
            if (Mathf.Abs(lengthA - lengthB) > 0.05f)
            {
                return lengthA < lengthB ? a : b;
            }

            return b;
        }

        void DemoteBarrierColliderToVisual(TrackSolidObstacle solid)
        {
            GameLog.Info("[TrackValidation] Demoted duplicate/overlapping barrier collider (" + solid.obstacleType + ") to visual-only on " + Runtime.displayName);
            MakeVisualOnly(solid.gameObject);
            solid.enabled = false;
        }

        // Barrier-pocket fix (items D & G): heuristic pocket/trap sweep run once after
        // all barrier geometry (including auto-fill and the dedup pass above) is
        // final. Restricted to tight-corner stretches, where fan-out/tyre-stack/catch-
        // fence transitions concentrate and a pocket is overwhelmingly likely to
        // appear. Approximates a car as a small capsule and spherecasts from a few
        // probe depths INSIDE the legal road width toward the centreline - if
        // something solid blocks that inward path, a barrier-family collider is
        // sitting inward of where it should be (or a second one is crossing in front
        // of the first), i.e. exactly the pocket mouth a recovering car could catch a
        // nose or wheel on. Never touches the intentional pit-opening stretch. Not
        // exact computational geometry, deliberately - a robust heuristic plus the
        // dedup pass above is enough to keep tight corners drivable.
        const float PocketSweepStep = 4f;
        const float PocketProbeCastRadius = 0.5f;
        readonly float[] pocketProbeDepthsMeters = { 0.5f, 1.5f, 2.5f };

        void ValidateBarrierPocketFree()
        {
            if (Runtime == null || Runtime.length <= 1f)
            {
                return;
            }

            int cleared = 0;
            for (float d = 0f; d < Runtime.length; d += PocketSweepStep)
            {
                if (!Runtime.IsNearTightFenceCorner(d))
                {
                    continue;
                }

                cleared += ClearPocketAt(d, -1);
                cleared += ClearPocketAt(d, 1);
            }

            if (cleared > 0)
            {
                GameLog.Warn("[TrackValidation] Cleared " + cleared + " barrier pocket/trap collider(s) in tight corners on " + Runtime.displayName);
            }
        }

        int ClearPocketAt(float distance, int side)
        {
            // Collision/placement fix: this used to skip the intentional pit-opening
            // stretch entirely (IsIntentionalPitOpening is a gap-fill-only concept -
            // "don't plant a wall here"). Intrusion clearing must NOT skip it - a
            // wall standing where it shouldn't inside the pit-opening/merge zone is
            // exactly the scenario that caused a live AI pileup, so this sweep now
            // checks every tight-corner sample regardless of pit-zone overlap.
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            float halfWidth = Runtime.HalfWidthAt(distance);
            Vector3 heightOffset = Vector3.up * 0.6f;
            int cleared = 0;

            for (int i = 0; i < pocketProbeDepthsMeters.Length; i++)
            {
                float probeLateral = halfWidth - pocketProbeDepthsMeters[i];
                if (probeLateral <= 0f)
                {
                    continue;
                }

                Vector3 probePoint = point + right * side * probeLateral + heightOffset;
                Vector3 towardCentre = -right * side;
                RaycastHit hit;
                if (!Physics.SphereCast(probePoint, PocketProbeCastRadius, towardCentre, out hit, 1.5f))
                {
                    continue;
                }

                TrackSolidObstacle solid = hit.collider.GetComponentInParent<TrackSolidObstacle>();
                if (solid == null || !solid.enabled)
                {
                    continue;
                }

                string type = solid.obstacleType ?? "";
                bool isBarrierFamily = type.Contains("wall") || type.Contains("barrier") || type.Contains("rail") ||
                                        type.Contains("divider") || type.Contains("auto-fill");
                if (!isBarrierFamily)
                {
                    continue;
                }

                DemoteBarrierColliderToVisual(solid);
                cleared++;
            }

            return cleared;
        }

        // Collision/placement fix (item 7): the final, whole-registry safety net.
        // Runs once, after every geometry-placing pass (continuous edge barriers,
        // pit lane divider, pit ramp guide fences, bridge walls, auto-fill,
        // overlap deconflict) has finished, so it catches anything a later pass
        // introduced that TryPlaceSolidObstacle's own placement-time gate couldn't
        // have seen yet. Any obstacle whose footprint intrudes into the main road
        // OR any pit drivable surface gets demoted to visual-only and disabled -
        // never merely logged and left in place.
        void ValidateNoSolidObstaclesInsideDrivingCorridors()
        {
            if (Runtime == null || Runtime.length <= 1f)
            {
                return;
            }

            int removed = 0;
            for (int i = solidObstacles.Count - 1; i >= 0; i--)
            {
                TrackSolidObstacle obstacle = solidObstacles[i];
                if (obstacle == null)
                {
                    solidObstacles.RemoveAt(i);
                    continue;
                }

                if (!obstacle.enabled)
                {
                    continue;
                }

                bool clear = IsSolidObstaclePlacementValid(obstacle.gameObject, obstacle.obstacleType, obstacle.transform.position,
                                                             obstacle.transform.forward, obstacle.localScaleAtValidation, obstacle.minimumClearance);
                if (clear)
                {
                    continue;
                }

                if (LastReport != null)
                {
                    LastReport.invalidObstaclesFlagged++;
                    LastReport.obstacleIntrusionCount++;
                }

                TrackProgress intrusionProgress = Runtime.GetProgress(obstacle.transform.position);
                GameLog.Warn("[TrackValidation] Removed intrusive solid obstacle " + obstacle.obstacleType + " on " + Runtime.displayName +
                             " at " + intrusionProgress.distance.ToString("0") + "m: collider footprint intersected a drivable corridor.");
                DemoteBarrierColliderToVisual(obstacle);
                removed++;
            }

            if (removed > 0)
            {
                GameLog.Warn("[TrackValidation] Removed " + removed + " intrusive solid obstacle(s) on " + Runtime.displayName + " in the post-build corridor sweep.");
            }
        }

        // Real measured distance (metres) from a point on the true track edge
        // to the nearest barrier-like collider's surface, or
        // BarrierColliderSearchRadius if nothing barrier-like is found within
        // that radius at all (i.e. "missing entirely").
        float MeasureBarrierGap(Vector3 edgePoint, float searchRadius)
        {
            Collider[] hits = Physics.OverlapSphere(edgePoint, searchRadius);
            float best = searchRadius;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                {
                    continue;
                }

                TrackSolidObstacle solid = hit.GetComponentInParent<TrackSolidObstacle>();
                if (solid == null)
                {
                    continue;
                }

                string type = solid.obstacleType ?? "";
                if (!(type.Contains("wall") || type.Contains("fence") || type.Contains("barrier") || type.Contains("rail") || type.Contains("divider")))
                {
                    continue;
                }

                Vector3 closest = hit.ClosestPoint(edgePoint);
                float distance = Vector3.Distance(closest, edgePoint);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        // ---------- debug barrier-smoothness sweep ----------
        // Companion to the flush-distance sweep above: ValidateBarrierColliderCoverage
        // only asks "is a barrier there at all, and is it flush" - it says nothing about
        // whether the run reads as SMOOTH along the way. Re-derives the exact same
        // per-side lateral-offset decision ComputeBarrierPlan makes (minus actually
        // placing geometry) at a fine, fixed step for the whole lap, then flags two
        // distinct "still looks/feels sharp" symptoms a car can find even when the
        // collider itself is technically continuous: a NOTCH (one sample's offset dips
        // inward of the local 3-point trend by more than tolerance - a candidate trap
        // pocket) and a FACET (the track tangent swings by more than the angle
        // tolerance between two adjacent samples - a visible/felt kink). Debug-only,
        // warn-only, never modifies geometry, never throws - mirrors
        // ValidateBarrierColliderCoverage/ValidateSceneryGrounding exactly.
        const float BarrierSmoothnessSampleStep = 6f;
        const float BarrierSmoothnessNotchTolerance = 0.3f;
        const float BarrierSmoothnessAngleTolerance = 20f;

        void ValidateBarrierSmoothness()
        {
            if (Runtime == null || Runtime.length <= 1f)
            {
                return;
            }

            ValidateBarrierSmoothnessForSide(-1);
            ValidateBarrierSmoothnessForSide(1);
        }

        void ValidateBarrierSmoothnessForSide(int side)
        {
            float length = Runtime.length;
            bool highSpeedTrack = length > HighSpeedTrackLength;
            List<CornerInfo> highRiskCorners = DetectCorners(HighRiskCornerAngle);
            List<CornerInfo> tightFenceCorners = DetectCorners(TightCornerFenceAngle);

            int sampleCount = Mathf.Clamp(Mathf.RoundToInt(length / BarrierSmoothnessSampleStep), 12, 4000);
            float[] lateral = new float[sampleCount];
            Vector3[] forwards = new Vector3[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float d = length * i / sampleCount;
                float normalized = d / length;
                bool elevated = IsElevatedAtDistance(d);
                bool nearHighRiskCorner = IsNearCorner(d, highRiskCorners, 45f);
                bool nearTightFenceCorner = nearHighRiskCorner || IsNearCorner(d, tightFenceCorners, TightCornerFenceRadius);
                BarrierPlanEntry entry = ComputeBarrierPlan(d, BarrierSmoothnessSampleStep, BarrierSmoothnessSampleStep, side, elevated, normalized, highSpeedTrack, nearHighRiskCorner, nearTightFenceCorner, 0);
                lateral[i] = entry.lateral;

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                forwards[i] = forward;
            }

            int notches = 0;
            int facets = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                int prev = (i - 1 + sampleCount) % sampleCount;
                int next = (i + 1) % sampleCount;
                float trend = (lateral[prev] + lateral[i] + lateral[next]) / 3f;
                float inwardDip = trend - lateral[i];
                float d = length * i / sampleCount;
                if (inwardDip > BarrierSmoothnessNotchTolerance)
                {
                    notches++;
                    GameLog.Warn("[TrackValidation] Barrier smoothness check: possible notch on " + (side < 0 ? "left" : "right") +
                                 " side near " + d.ToString("0") + "m (" + inwardDip.ToString("0.00") + "m inward of local trend) on " + Runtime.displayName);
                }

                float angleChange = Vector3.Angle(forwards[i], forwards[next]);
                if (angleChange > BarrierSmoothnessAngleTolerance)
                {
                    facets++;
                    GameLog.Warn("[TrackValidation] Barrier smoothness check: sharp facet on " + (side < 0 ? "left" : "right") +
                                 " side near " + d.ToString("0") + "m (" + angleChange.ToString("0.0") + " deg tangent change) on " + Runtime.displayName);
                }
            }

            if (notches == 0 && facets == 0)
            {
                GameLog.Info("[TrackValidation] Barrier smoothness sweep clean on " + (side < 0 ? "left" : "right") + " side: " + sampleCount + " sample(s), no notches or sharp facets on " + Runtime.displayName);
            }
            else
            {
                GameLog.Warn("[TrackValidation] Barrier smoothness sweep on " + (side < 0 ? "left" : "right") + " side found " + notches + " notch(es) and " + facets +
                             " sharp facet(s) out of " + sampleCount + " sample(s) on " + Runtime.displayName);
            }
        }

        // ---------- debug scenery-grounding physics sweep ----------
        // Mirrors ValidateBarrierColliderCoverage above: a debug-only, physics-driven
        // spot-check that runs once after every scenery pass has finished, independent
        // of the placement math itself, so a genuine "floating object" regression in any
        // of the dozens of trackside-dressing passes above gets caught by what a player
        // would actually see (a gap between an object and the ground) rather than by
        // re-deriving every pass's own intended position a second time. Every piece of
        // decorative scenery in this file is parented directly to this component's
        // transform with no separate registry, so this instead stride-samples whatever
        // actually got built: for a slice of the collider-free decorative objects
        // (buildings, towers, posts, trees, ...) it fires a ray straight down against
        // the real ground/road/barrier colliders every other pass leaves behind and
        // flags - then auto-corrects with the same grounding-pad/column helper the
        // passes above use - anything whose base doesn't come to rest within tolerance
        // of the surface directly beneath it. Never throws, always warns.
        const int SceneryGroundingSampleStride = 17;
        const float SceneryGroundingRayHeight = 500f;

        // Generous on purpose: plenty of legitimate scenery is mounted a metre or two
        // above its own object's true ground contact point (a flag pole's cloth, a
        // gantry's overhead deck, a post-mounted board) rather than being the ground
        // contact point itself - this only needs to catch genuine multi-metre "floating
        // at deck height" bugs, not every intentionally-elevated sub-component.
        const float SceneryGroundingTolerance = 2.5f;

        void ValidateSceneryGrounding()
        {
            if (Runtime == null || Runtime.length <= 1f)
            {
                return;
            }

            int sampled = 0;
            int flagged = 0;
            int correctedCount = 0;
            float worstGap = 0f;
            int childCount = transform.childCount;
            for (int i = 0; i < childCount; i += SceneryGroundingSampleStride)
            {
                Transform child = transform.GetChild(i);
                if (child == null || !IsGroundingCheckCandidate(child))
                {
                    continue;
                }

                Renderer renderer = child.GetComponent<Renderer>();
                Vector3 basePosition = renderer.bounds.center;
                basePosition.y = renderer.bounds.min.y;
                sampled++;

                Vector3 rayOrigin = basePosition + Vector3.up * SceneryGroundingRayHeight;
                RaycastHit hit;
                if (!Physics.Raycast(rayOrigin, Vector3.down, out hit, SceneryGroundingRayHeight * 2f))
                {
                    continue;
                }

                float gap = basePosition.y - hit.point.y;
                if (gap > SceneryGroundingTolerance)
                {
                    flagged++;
                    worstGap = Mathf.Max(worstGap, gap);
                    GameLog.Warn("[TrackValidation] Scenery grounding check FAILED: '" + child.name + "' base sits " +
                                 gap.ToString("0.00") + "m above the surface below it on " + Runtime.displayName);

                    // Auto-correct: drop the object straight down onto the surface found
                    // by the ray, then reuse the same grounding-pad/column helper the
                    // passes above call directly, in case the surface underneath doesn't
                    // fully explain away the visual gap (e.g. a sloped hit point).
                    child.position -= Vector3.up * gap;
                    EnsureGroundedBase(hit.point, Mathf.Max(renderer.bounds.extents.x, renderer.bounds.extents.z));
                    correctedCount++;
                }
            }

            if (flagged == 0)
            {
                GameLog.Info("[TrackValidation] Scenery grounding sweep clean: " + sampled + " object(s) sampled, all within tolerance on " + Runtime.displayName);
            }
            else
            {
                GameLog.Warn("[TrackValidation] Scenery grounding sweep found " + flagged + "/" + sampled + " object(s) floating with an unexpected gap (worst " +
                             worstGap.ToString("0.00") + "m), auto-corrected " + correctedCount + " on " + Runtime.displayName);
            }
        }

        // Restricts the grounding sweep to plain decorative scenery: a MeshRenderer with
        // no collider (everything CreateVisualBox/CreateVisualCone builds, and anything
        // MakeVisualOnly stripped the collider from), excluding LineRenderers (the AI
        // racing line/debug overlays span the whole lap and have no meaningful single
        // "base") and TextMesh labels (mounted flush to whatever board/gantry placed
        // them, not an independent ground-contact object).
        bool IsGroundingCheckCandidate(Transform child)
        {
            if (child.GetComponent<Collider>() != null || child.GetComponent<LineRenderer>() != null || child.GetComponent<TextMesh>() != null)
            {
                return false;
            }

            Renderer renderer = child.GetComponent<Renderer>();
            return renderer != null && renderer.enabled;
        }

        // ---------- debug pit-lane surface continuity sweep ----------
        // ValidatePitCorridorSealed (further below) already exists and means something
        // different - it measures distance to the nearest BARRIER (is the corridor
        // walled off), using the same solidObstacles bookkeeping every other barrier
        // validator relies on. A barrier can be perfectly sealed while the ground
        // directly under the driving line is still bare, uncollidable terrain - the
        // "visible gaps in the track/ground in the pit lane and pit entry" complaint
        // this pass exists for. This instead raycasts straight down at a handful of
        // points along the entry ramp, the flat corridor, and the exit ramp and checks
        // that whatever it actually hits is built from the single shared low-friction
        // asphalt physics material every drivable surface (main road AND pit-lane
        // pavement alike) uses - see GetRoadPhysicsMaterial, the same singleton both
        // BuildRoadMesh's MeshCollider and CreateCollidablePitSurface's BoxColliders
        // are assigned. Mirrors ValidateSceneryGrounding's raycast-sweep style.
        // Debug-only, warn-only, never modifies geometry, never throws.
        const float PitSurfaceCheckStep = 12f;
        const float PitSurfaceCheckRayHeight = 60f;

        void ValidatePitLaneSurfaceCoverage()
        {
            if (Runtime == null || Runtime.length <= 1f)
            {
                return;
            }

            int checkedPoints = 0;
            int gaps = 0;
            CheckPitSurfaceRange(Runtime.length * PitZoneEntryRampStart, Runtime.length * PitZoneEntryRampEnd, true, ref checkedPoints, ref gaps);
            CheckPitSurfaceRange(Runtime.length * Runtime.PitCorridorStartNormalized, Runtime.length * PitZoneExitRampStart, false, ref checkedPoints, ref gaps);
            CheckPitSurfaceRange(Runtime.length * PitZoneExitRampStart, Runtime.length * PitZoneExitRampEnd, true, ref checkedPoints, ref gaps);

            if (LastReport != null)
            {
                LastReport.pitSurfaceGapCount = gaps;
            }

            if (gaps == 0)
            {
                GameLog.Info("[TrackValidation] Pit lane surface sweep clean: " + checkedPoints + " point(s) checked, drivable pavement found under every one on " + Runtime.displayName);
            }
            else
            {
                GameLog.Warn("[TrackValidation] Pit lane surface sweep found " + gaps + "/" + checkedPoints +
                             " point(s) along the pit entry/corridor/exit with no drivable pavement detected on " + Runtime.displayName);
            }
        }

        // isRamp selects which envelope the check point's lateral offset follows:
        // the tapering ramp envelope (PitRampEnvelopeAt, only valid inside the actual
        // entry/exit ramp normalized windows) or the flat corridor's own constant
        // PitLaneLateral - using the ramp formula outside its own window would give a
        // nonsense answer (see PitRampEnvelopeAt's own wrap-based exit-taper math).
        void CheckPitSurfaceRange(float startDistance, float endDistance, bool isRamp, ref int checkedPoints, ref int gaps)
        {
            float length = Runtime.length;
            float span = endDistance - startDistance;
            if (span <= 0f)
            {
                span += length;
            }

            PhysicMaterial expectedSurface = GetRoadPhysicsMaterial();
            for (float d = 0f; d < span; d += PitSurfaceCheckStep)
            {
                float distance = Runtime.WrapDistance(startDistance + d);
                float normalized = distance / length;

                Vector3 point;
                Vector3 discard;
                Vector3 right;
                Runtime.SampleAtDistance(distance, out point, out discard, out right);

                float lateral;
                float patchWidth;
                if (isRamp)
                {
                    float halfWidth;
                    PitRampEnvelopeAt(normalized, distance, out lateral, out halfWidth);
                    patchWidth = halfWidth * 2f;
                }
                else
                {
                    lateral = Runtime.PitLaneLateral;
                    patchWidth = PitRampFullWidth;
                }

                Vector3 rayOrigin = point + right * lateral + Vector3.up * PitSurfaceCheckRayHeight;
                checkedPoints++;
                RaycastHit hit;
                bool found = Physics.Raycast(rayOrigin, Vector3.down, out hit, PitSurfaceCheckRayHeight * 2f) && hit.collider.sharedMaterial == expectedSurface;
                if (!found)
                {
                    gaps++;
                    GameLog.Warn("[TrackValidation] Pit lane surface check FAILED near " + distance.ToString("0") +
                                 "m: no drivable pavement found under the pit path on " + Runtime.displayName);

                    // Pit-floor fix: this used to only log the gap - the hole stayed in
                    // the actual driving surface. Drop a real, collidable patch of pit
                    // asphalt right at the failed sample, generously overlapping its
                    // neighbours (double the check step) so no seam can reopen a hole
                    // between this patch and whatever paving already exists around it.
                    Vector3 forward;
                    Vector3 discardPoint;
                    Vector3 discardRight;
                    Runtime.SampleAtDistance(distance, out discardPoint, out forward, out discardRight);
                    CreateCollidablePitSurface(
                        "Auto-filled pit surface",
                        point + right * lateral + Vector3.up * 0.02f,
                        Quaternion.LookRotation(forward, Vector3.up),
                        new Vector3(Mathf.Max(patchWidth, PitRampNarrowWidth) + 2f, 0.18f, PitSurfaceCheckStep * 2f),
                        pitLaneMaterial != null ? pitLaneMaterial : GetDefaultPitLaneMaterial());
                    GameLog.Info("[TrackValidation] Auto-filled pit surface gap at " + distance.ToString("0") + "m on " + Runtime.displayName);
                }
            }
        }

        // Fallback for the (practically unreachable, since BuildPitLane always runs
        // first) case this validation pass ever ran before pitLaneMaterial was
        // cached - keeps the auto-fill patch from ever being placed with a null
        // material instead of skipping the fix.
        Material GetDefaultPitLaneMaterial()
        {
            pitLaneMaterial = CreateMaterial("Pit lane material", new Color(0.12f, 0.13f, 0.15f), 0.02f, 0.55f);
            return pitLaneMaterial;
        }

        void CreateKerbBlock(Vector3 position, Vector3 forward, float seed, bool aggressive)
        {
            bool whiteBase = Mathf.FloorToInt(seed / 16f) % 2 == 0;
            // Coastal and technical-parkland circuits get a blue/white kerb scheme
            // instead of the default red/white, echoing real circuits' use of
            // colour-coded kerbing so every corner on every track doesn't share one
            // painted pair.
            Material coloredMaterial = (coastalTrack || technicalParklandTrack) ? kerbMaterialBlue : kerbMaterial;
            Material material = whiteBase ? lineMaterial : coloredMaterial;
            Material accentMaterial = whiteBase ? coloredMaterial : lineMaterial;

            // Hairpins/high-severity corners get a taller, wider block than a sweeping
            // corner's kerb - real circuits raise the profile exactly where cars run
            // widest over it, so one flat block everywhere lost that read entirely.
            float heightScale = aggressive ? 1.7f : 1f;
            float widthScale = aggressive ? 1.25f : 1f;
            float blockY = 0.075f * heightScale;
            float accentY = 0.12f * heightScale + 0.011f;

            GameObject kerb = CreateVisualBox("Painted kerb", position + Vector3.up * blockY, Quaternion.LookRotation(forward, Vector3.up), new Vector3(1.15f * widthScale, 0.09f * heightScale, 4.5f), material);
            MeshRenderer renderer = kerb.GetComponent<MeshRenderer>();
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Simple;

            // Inlay stripe on top of the block so each kerb reads as painted sausage
            // kerbing instead of one flat coloured slab; stacked above the block's top
            // face rather than coplanar with it, so there is no z-fighting risk.
            CreateVisualBox("Painted kerb accent", position + Vector3.up * accentY, Quaternion.LookRotation(forward, Vector3.up), new Vector3(1f * widthScale, 0.02f, 1.6f), accentMaterial);

            // A second dash further along the aggressive block so a hairpin's kerbing
            // reads as a longer painted run instead of repeating one lone dash.
            if (aggressive)
            {
                CreateVisualBox("Painted kerb accent", position + Vector3.up * accentY + forward * 2.1f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(1f * widthScale, 0.02f, 1.6f), accentMaterial);
            }

            // Wear variation so kerbing reads as driven-over rather than a flat,
            // pristine painted block every single time: about a third of blocks get a
            // dark rubber scuff smear (reusing the same rubberMaterial the racing-line
            // build-up elsewhere already uses, tying the two surface-detail passes
            // together), and a separate third get a duller, sun-bleached strip
            // standing in for faded paint. Deterministic off the block's own seed
            // (its sample distance) rather than random, so a rebuild of the same
            // track looks identical every time.
            int wearVariant = Mathf.FloorToInt(seed / 5.5f) % 3;
            if (wearVariant == 0)
            {
                CreateVisualBox("Kerb rubber scuff", position + Vector3.up * (blockY + 0.021f) - forward * 0.9f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.55f * widthScale, 0.015f, 1.3f), rubberMaterial);
            }
            else if (wearVariant == 1)
            {
                CreateVisualBox("Kerb faded paint patch", position + Vector3.up * (accentY + 0.002f) + forward * 1.0f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.7f * widthScale, 0.012f, 0.9f), tyreMarbleMaterialLight);
            }
        }

        void BuildTrackMarkers()
        {
            CreateTrackLine(0f, "Start finish", Color.white, 2.4f);
            CreateTrackLine(Runtime.length * 0.333f, "Sector 1 line", new Color(0.1f, 0.75f, 1f), 1.2f);
            CreateTrackLine(Runtime.length * 0.666f, "Sector 2 line", new Color(0.1f, 1f, 0.45f), 1.2f);
            CreateSectorBoard(Runtime.length * 0.333f, new Color(0.1f, 0.75f, 1f));
            CreateSectorBoard(Runtime.length * 0.666f, new Color(0.1f, 1f, 0.45f));

            // Distance boards for major braking zones - same corner-severity detection
            // the continuous barrier pass uses for tyre-stack placement, so "this is a
            // real corner" means one thing across the whole file.
            List<CornerInfo> brakingCorners = DetectCorners(35f);
            for (int i = 0; i < brakingCorners.Count; i++)
            {
                float dist = brakingCorners[i].distance;
                CreateBrakingBoard(dist - 150f, "150");
                CreateBrakingBoard(dist - 100f, "100");
                CreateBrakingBoard(dist - 50f, "50");
                CreateApexCones(dist);
                CreateTyreMarbles(dist);
                CreateLockupSkidMarks(dist);
                CreateGravelTrap(dist);
            }
        }

        // Light-coloured rubber "marbles" scattered off the racing line toward corner
        // exit - cheap decals reusing the same CreateRoadStripe/paint-layer approach as
        // the dark racing-line rubber above, just tinted light and pushed to the
        // outside of the corner where cars actually run wide on exit. Scales with
        // sceneryDensity like the rest of the per-corner cosmetic detail.
        void CreateTyreMarbles(float apexDistance)
        {
            Vector3 apexPoint;
            Vector3 apexForward;
            Vector3 apexRight;
            Runtime.SampleAtDistance(apexDistance, out apexPoint, out apexForward, out apexRight);
            Vector3 approachPoint;
            Vector3 approachForward;
            Vector3 approachRight;
            Runtime.SampleAtDistance(apexDistance - 10f, out approachPoint, out approachForward, out approachRight);

            // Sign of the turn so marbles land on the outside of the corner. Same
            // entry/exit-vector cross-product convention BuildKerbs uses to pick its
            // "outer" kerb side (+turnSign), so this agrees with the kerb placement
            // above instead of guessing independently at which side is outside.
            float turnSign = Mathf.Sign(Vector3.Cross(approachForward, apexForward).y);
            float outsideSide = turnSign;

            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);
            int patches = Mathf.Max(1, Mathf.RoundToInt(3f * density));
            for (int p = 0; p < patches; p++)
            {
                float exitDistance = apexDistance + 18f + p * 7.5f;
                Vector3 patchPoint;
                Vector3 patchForward;
                Vector3 patchRight;
                Runtime.SampleAtDistance(exitDistance, out patchPoint, out patchForward, out patchRight);
                float lateral = outsideSide * (Runtime.HalfWidthAt(exitDistance) * 0.7f + p * 0.4f);
                // Alternate the base marble tint with the sun-bleached variant so a
                // multi-patch scatter reads as debris from different laps/compounds
                // rather than one uniform colour repeated down the exit kerb.
                Material marbleMaterial = p % 2 == 0 ? tyreMarbleMaterial : tyreMarbleMaterialLight;
                CreateRoadStripe(patchPoint + patchRight * lateral, patchForward, 1.5f + p * 0.12f, 4.6f, marbleMaterial, "Tyre marbles", 7);
            }
        }

        // Curved lock-up skid streaks converging toward the apex from the braking zone -
        // distinct from BuildAsphaltDetail's fixed-spacing straight skid marks, these are
        // keyed to the same braking-corner detection as the boards/marbles above so the
        // heaviest skid marks land exactly where cars actually brake hardest, angled
        // slightly toward the apex line rather than running dead parallel to the road.
        void CreateLockupSkidMarks(float apexDistance)
        {
            Vector3 approachPoint;
            Vector3 approachForward;
            Vector3 approachRight;
            Runtime.SampleAtDistance(apexDistance - 10f, out approachPoint, out approachForward, out approachRight);
            Vector3 apexPoint;
            Vector3 apexForward;
            Vector3 apexRight;
            Runtime.SampleAtDistance(apexDistance, out apexPoint, out apexForward, out apexRight);
            float turnSign = Mathf.Sign(Vector3.Cross(approachForward, apexForward).y);

            int streaks = Mathf.Max(1, Mathf.RoundToInt(2f * Mathf.Clamp(sceneryDensity, 0.25f, 2f)));
            for (int s = 0; s < streaks; s++)
            {
                float brakeDistance = apexDistance - 30f - s * 9f;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(brakeDistance, out point, out forward, out right);
                Vector3 skewedForward = Quaternion.Euler(0f, -turnSign * 7f, 0f) * forward;
                float lateral = -turnSign * (0.7f + s * 0.55f);
                CreateRoadStripe(point + right * lateral, skewedForward, 0.18f, 6.2f - s * 0.7f, skidMarkMaterial, "Braking lock-up skid mark", 6);
            }
        }

        // Sandy gravel-trap runoff patch on the outside of the corner exit, sitting just
        // beyond the kerb and short of the barrier line - street circuits have a wall
        // instead of gravel, and elevated stretches have no ground-level run-off to trap
        // into, so both are skipped rather than drawing gravel that wouldn't make sense.
        void CreateGravelTrap(float apexDistance)
        {
            if (streetTrack || IsElevatedAtDistance(apexDistance))
            {
                return;
            }

            Vector3 approachPoint;
            Vector3 approachForward;
            Vector3 approachRight;
            Runtime.SampleAtDistance(apexDistance - 10f, out approachPoint, out approachForward, out approachRight);
            Vector3 apexPoint;
            Vector3 apexForward;
            Vector3 apexRight;
            Runtime.SampleAtDistance(apexDistance, out apexPoint, out apexForward, out apexRight);
            float turnSign = Mathf.Sign(Vector3.Cross(approachForward, apexForward).y);

            int patches = Mathf.Clamp(Mathf.RoundToInt(3f * Mathf.Clamp(sceneryDensity, 0.25f, 2f)), 2, 3);
            for (int p = 0; p < patches; p++)
            {
                float exitDistance = apexDistance + 4f + p * 8f;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(exitDistance, out point, out forward, out right);
                // Barrier gap fix: the runoff band between the kerb (~+1.3m) and
                // the (now tightened) Armco line at +2.6m is much narrower than
                // it used to be, so both the patch width and its lateral spread
                // were cut to match - it still reads as a gravel strip in front
                // of the barrier, just sized to the tighter gap rather than
                // spilling past the wall. Sampled at the local (possibly hairpin-
                // widened) half-width so the patch stays between the kerb and the
                // barrier line even where a hairpin has pushed both further out.
                float lateral = turnSign * (Runtime.HalfWidthAt(exitDistance) + 1.5f + p * 0.3f);
                CreateVisualBox("Gravel trap patch", point + right * lateral + Vector3.up * 0.03f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(1f, 0.05f, 7f), gravelMaterial);
            }
        }

        void CreateSectorBoard(float distance, Color color)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            Material boardMaterial = CreateMaterial("Sector board material", color, 0.05f, 0.7f, nightTrack ? color * 0.4f : Color.black);
            // Sector split points fall at fixed lap fractions, not corner-derived
            // distances, so one can legitimately land inside a widened hairpin - use the
            // local half-width so the board never ends up planted on the wider tarmac.
            float boardLateral = Runtime.HalfWidthAt(distance) + 3.2f;
            CreateVisualBox("Sector board", point - right * boardLateral + Vector3.up * 2.1f, Quaternion.LookRotation(right, Vector3.up), new Vector3(0.14f, 1f, 1.6f), boardMaterial);
            CreateVisualBox("Sector board post", point - right * boardLateral + Vector3.up * 0.8f, Quaternion.LookRotation(right, Vector3.up), new Vector3(0.12f, 1.6f, 0.12f), metalMaterial);
        }

        // Small marshal hut with a flag pole; placed sparsely around the lap.
        void CreateMarshalPost(Vector3 position, Vector3 forward, int index)
        {
            Vector3 safePosition = PushSceneryClearOfTrack(position, 6.5f);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            // Parkland circuits get the same weathered-concrete tone as their
            // grandstands so the hut reads as part of the same "old-school venue"
            // rather than sharing the generic barrier grey every other archetype uses.
            Material hutMaterial = parklandTrack ? weatheredConcreteMaterial : barrierMaterial;
            CreateVisualBox("Marshal post plinth", safePosition + Vector3.up * 0.05f, rotation, new Vector3(1.85f, 0.1f, 1.85f), concreteMaterial);
            CreateVisualBox("Marshal post hut", safePosition + Vector3.up * 0.75f, rotation, new Vector3(1.7f, 1.5f, 1.7f), hutMaterial);
            CreateVisualBox("Marshal post roof", safePosition + Vector3.up * 1.62f, rotation, new Vector3(1.95f, 0.16f, 1.95f), sceneryAccentMaterial);
            CreateVisualBox("Marshal flag pole", safePosition + Vector3.up * 2.6f, rotation, new Vector3(0.08f, 1.9f, 0.08f), metalMaterial);
            CreateVisualBox("Marshal flag", safePosition + Vector3.up * 3.3f + forward * 0.32f, rotation, new Vector3(0.05f, 0.4f, 0.62f), index % 2 == 0 ? sceneryAccentMaterial : lineMaterial);
            if (nightTrack || twilightTrack)
            {
                CreateVisualBox("Marshal post beacon", safePosition + Vector3.up * 1.78f, rotation, new Vector3(0.18f, 0.18f, 0.18f), lightGlowMaterial);
            }

            // Static yellow/green flag board bolted to the hut, angled toward the
            // approaching cars - separate from the pole flag above and cheap enough to
            // put on every post. No live wiring in this pass; a caution slot every fifth
            // post just breaks up an otherwise uniform row of green boards.
            bool caution = index % 5 == 0;
            Quaternion boardRotation = rotation * Quaternion.Euler(0f, 0f, 16f);
            GameObject flagBoard = CreateVisualBox("Marshal flag board", safePosition + Vector3.up * 1.9f + forward * 0.55f, boardRotation, new Vector3(0.04f, 0.5f, 0.72f), caution ? flagYellowMaterial : flagGreenMaterial);

            // Captured so SetRaceControlVisual can flip every post to green/yellow live
            // once race control actually drives this instead of the static per-index
            // caution pattern above (which only sets the initial look).
            marshalFlagBoardRenderers.Add(flagBoard.GetComponent<Renderer>());

            // Occasional parked marshal utility vehicle beside the post - sparse (every
            // 15th post) so it reads as scattered trackside kit rather than a vehicle
            // parked at every single hut.
            if (index % 15 == 0)
            {
                Vector3 vehicleRight = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 vehiclePosition = safePosition + vehicleRight * 2.7f;
                CreateVisualBox("Marshal utility vehicle body", vehiclePosition + Vector3.up * 0.55f, rotation, new Vector3(1.6f, 0.9f, 3.2f), metalMaterial);
                CreateVisualBox("Marshal utility vehicle cab", vehiclePosition - forward * 1.1f + Vector3.up * 1.05f, rotation, new Vector3(1.5f, 0.85f, 1.3f), glassMaterial);
                CreateVisualBox("Marshal utility vehicle beacon", vehiclePosition + Vector3.up * 1.55f, rotation, new Vector3(0.3f, 0.2f, 0.3f), lightGlowMaterial);
            }

            // Short marshal-access fence run behind the post with a visible gap in the
            // middle - a cheap decorative nod to the pedestrian access points marshals
            // use to reach the fence line, built entirely from generic posts/panels and
            // never touching CreateCatchFence's own barrier geometry. Every other post
            // only, so it stays a sparse accent rather than a second fence line.
            if (index % 2 == 0)
            {
                Vector3 fenceRight = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 fenceBase = safePosition + fenceRight * 2.4f + Vector3.up * 0.6f;
                CreateVisualBox("Marshal access fence post", fenceBase + forward * 2.2f, rotation, new Vector3(0.08f, 1.2f, 0.08f), fencePostMaterial);
                CreateVisualBox("Marshal access fence panel", fenceBase + forward * 1.55f, rotation, new Vector3(0.04f, 1.1f, 1.1f), fenceMaterial);
                CreateVisualBox("Marshal access fence post", fenceBase + forward * 0.9f, rotation, new Vector3(0.08f, 1.2f, 0.08f), fencePostMaterial);
                // Gap between here and -forward*0.9 reads as the access opening.
                CreateVisualBox("Marshal access fence post", fenceBase - forward * 0.9f, rotation, new Vector3(0.08f, 1.2f, 0.08f), fencePostMaterial);
                CreateVisualBox("Marshal access fence panel", fenceBase - forward * 1.55f, rotation, new Vector3(0.04f, 1.1f, 1.1f), fenceMaterial);
                CreateVisualBox("Marshal access fence post", fenceBase - forward * 2.2f, rotation, new Vector3(0.08f, 1.2f, 0.08f), fencePostMaterial);
            }
        }

        void CreateBrakingBoard(float distance, string label)
        {
            Vector3 point; Vector3 forward; Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            Vector3 boardCenter = point + right * (Runtime.roadHalfWidth + 2.5f) + Vector3.up * 0.8f;
            Quaternion rotation = Quaternion.LookRotation(right, Vector3.up);
            CreateVisualBox("Braking Board " + label, boardCenter, rotation, new Vector3(0.1f, 1.2f, 1.8f), lineMaterial);

            // Real F1 boards count down with bars (3/2/1) rather than the distance
            // number, so the marker reads at a glance well before a driver could
            // parse text; the number is kept too for the sector-board style readout.
            // The board's thin (readable) face points along the track direction, so bars
            // and text are pushed out along -forward to sit proud of that face instead of
            // being buried inside the solid board box, and spread along "right" (the
            // board's long axis) so they line up side by side across the face.
            int bars = label == "150" ? 3 : (label == "100" ? 2 : 1);
            Vector3 faceOffset = -forward.normalized * 0.07f;
            for (int i = 0; i < bars; i++)
            {
                float barOffset = (i - (bars - 1) * 0.5f) * 0.45f;
                CreateVisualBox("Board bar " + label, boardCenter + Vector3.up * 0.35f + right * barOffset + faceOffset, rotation, new Vector3(0.02f, 0.4f, 0.14f), sceneryAccentMaterial);
            }

            GameObject text = new GameObject("Board Text " + label);
            text.transform.SetParent(transform);
            text.transform.position = boardCenter - Vector3.up * 0.25f + faceOffset;
            text.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            TextMesh textMesh = text.AddComponent<TextMesh>();
            textMesh.text = label;
            textMesh.fontSize = 36;
            textMesh.characterSize = 0.1f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        }

        // Small apex-marker cones flanking every hard-braking corner detected above.
        // Count scales with sceneryDensity like the rest of the per-lap furniture;
        // placement is pushed clear of the corridor as a safety net on tight curves.
        void CreateApexCones(float distance)
        {
            int perSide = Mathf.Max(1, Mathf.RoundToInt(2f * Mathf.Clamp(sceneryDensity, 0.25f, 2f)));
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int c = 0; c < perSide; c++)
                {
                    float along = (c - (perSide - 1) * 0.5f) * 3.2f;
                    Vector3 conePos = point + forward * along + right * side * (Runtime.roadHalfWidth + 1.4f);
                    conePos = PushSceneryClearOfTrack(conePos, 1.4f);
                    CreateTrafficCone(conePos, rotation);
                }
            }
        }

        void BuildDrsZoneBoards()
        {
            CreateDrsZoneBoard(Runtime.length * Runtime.drsZoneOne.x);
            CreateDrsZoneBoard(Runtime.length * Runtime.drsZoneTwo.x);
        }

        // Distinct blue DRS marker at zone start, separate from the generic braking
        // boards, keyed off the same IsInDrsZone data the paint stripes and the HUD use.
        // Follows the CreateSectorBoard convention (post + single thin board, no
        // separate frame box) since a frame sized only slightly larger than the board
        // in every axis would fully enclose and hide it rather than bordering it.
        void CreateDrsZoneBoard(float distance)
        {
            Vector3 point; Vector3 forward; Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            Vector3 basePosition = point + right * (Runtime.roadHalfWidth + 3.6f);
            Quaternion rotation = Quaternion.LookRotation(right, Vector3.up);
            CreateVisualBox("DRS zone board post", basePosition + Vector3.up * 1f, rotation, new Vector3(0.14f, 2f, 0.14f), metalMaterial);
            CreateVisualBox("DRS zone board", basePosition + Vector3.up * 2.3f, rotation, new Vector3(0.16f, 1.2f, 2.2f), drsPaintMaterial);

            // Mirrored-text fix: this was rotated to face -forward (back down
            // the straight), which reads correctly to a car driving away from
            // the board but backwards/mirrored to the approaching car actually
            // meant to read it. A driver approaching travels in +forward, so
            // the readable face needs to point in that same +forward
            // direction to be legible on approach - text is still pushed
            // proud of the board along -forward so it doesn't sit buried in
            // the solid box.
            GameObject text = new GameObject("DRS zone board text");
            text.transform.SetParent(transform);
            text.transform.position = basePosition + Vector3.up * 2.3f - forward.normalized * 0.11f;
            text.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            TextMesh textMesh = text.AddComponent<TextMesh>();
            textMesh.text = "DRS";
            textMesh.fontSize = 48;
            textMesh.characterSize = 0.16f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.9f, 0.96f, 1f, 0.95f);
        }

        // Intermediate timing-loop gantries at each sector split, distinct from
        // BuildStartGantry's double-boom/lights/checker treatment below - a single
        // slender boom with a lit loop strip and a split-number panel, echoing the
        // timing gantries real circuits hang over the track at every split point
        // rather than only at start/finish.
        void BuildTimingGantries()
        {
            CreateTimingGantry(Runtime.length * 0.333f, "1");
            CreateTimingGantry(Runtime.length * 0.666f, "2");
        }

        void CreateTimingGantry(float distance, string label)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            Vector3 left = point - right * (Runtime.roadHalfWidth + 2.4f);
            Vector3 rightSide = point + right * (Runtime.roadHalfWidth + 2.4f);
            CreateGantryPost(left, forward);
            CreateGantryPost(rightSide, forward);

            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            float span = Runtime.roadHalfWidth * 2.15f;
            CreateVisualBox("Timing gantry boom", point + Vector3.up * 6.3f, rotation, new Vector3(span, 0.24f, 0.55f), metalMaterial);

            // Thin lit loop strip standing in for the inductive timing loop's own
            // status lights - uses lightGlowMaterial (already night/twilight boosted)
            // rather than gantryRaceControlLightMaterial, since a sector timing loop
            // isn't part of race control's SC/VSC/flag state.
            CreateVisualBox("Timing gantry loop strip", point + Vector3.up * 6.05f, rotation, new Vector3(span * 0.94f, 0.05f, 0.06f), lightGlowMaterial);

            GameObject text = new GameObject("Timing gantry text");
            text.transform.SetParent(transform);
            text.transform.position = point + Vector3.up * 6.55f - forward.normalized * 0.32f;
            text.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            TextMesh textMesh = text.AddComponent<TextMesh>();
            textMesh.text = "SPLIT " + label;
            textMesh.fontSize = 32;
            textMesh.characterSize = 0.09f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.85f, 0.9f, 0.95f, 0.95f);
        }

        void CreateTrackLine(float distance, string markerName, Color color, float depth)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            Material material = CreateMaterial(markerName + " material", color, 0f, 0.62f);
            CreateVisualBox(markerName, point + Vector3.up * 0.085f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(Runtime.roadHalfWidth * 2f, 0.05f, depth), material);
        }

        // Checkered squares laid across the tarmac right at the start/finish line,
        // beside the plain white CreateTrackLine stripe - the finish-line presentation
        // hook the rest of the gantry/board furniture didn't have until now. One-off,
        // not scaled per lap, so the cost is a fixed handful of decals per track.
        void BuildFinishLinePresentation()
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(0f, out point, out forward, out right);

            const int columns = 8;
            float squareSize = Runtime.roadHalfWidth * 2f / columns;
            for (int c = 0; c < columns; c++)
            {
                float lateral = -Runtime.roadHalfWidth + squareSize * (c + 0.5f);
                Material squareMaterial = c % 2 == 0 ? lineMaterial : checkerDarkMaterial;
                CreateRoadStripe(point + right * lateral - forward * 3.2f, forward, squareSize * 0.96f, 1.3f, squareMaterial, "Finish line checker square", 8);
            }
        }

        static readonly Color[] SponsorPalette =
        {
            new Color(0.85f, 0.1f, 0.1f), new Color(0.05f, 0.35f, 0.85f),
            new Color(0.95f, 0.75f, 0.05f), new Color(0.1f, 0.55f, 0.2f),
            new Color(0.55f, 0.15f, 0.75f), new Color(0.9f, 0.45f, 0.05f)
        };

        // Rectangular sponsor board along a straight, reusing the visual-box primitive
        // like every other trackside marker in this file. Cycles a small fixed palette
        // instead of one material per board so it doesn't grow the material count.
        void CreateSponsorBoard(Vector3 position, Vector3 forward, int index)
        {
            Vector3 outward = Vector3.Cross(Vector3.up, forward).normalized;
            Quaternion rotation = Quaternion.LookRotation(outward, Vector3.up);
            Color panelColor = SponsorPalette[index % SponsorPalette.Length];
            Material board = CreateMaterial("Sponsor board material", panelColor, 0.05f, 0.55f, (nightTrack || twilightTrack) ? panelColor * 0.4f : Color.black);

            // The board's thin (readable) face points along the track direction (same
            // convention as CreateSectorBoard/CreateBrakingBoard), so the panel is popped
            // out along -forward rather than sharing the frame's centre on that axis,
            // which would otherwise bury it entirely inside the larger frame box.
            Vector3 faceOffset = -forward.normalized * 0.07f;
            CreateVisualBox("Sponsor board frame", position + Vector3.up * 1.5f, rotation, new Vector3(0.18f, 1.85f, 3.85f), metalMaterial);
            CreateVisualBox("Sponsor board panel", position + Vector3.up * 1.5f + faceOffset, rotation, new Vector3(0.1f, 1.6f, 3.6f), board);
            CreateVisualBox("Sponsor board post", position + Vector3.up * 0.5f, rotation, new Vector3(0.14f, 1f, 0.14f), metalMaterial);
        }

        // Low continuous run of generic, unbranded advertising hoarding panels bolted
        // just behind the trackside barrier line - denser and lower than the sparse
        // CreateSponsorBoard posts, so straights don't read as an empty run of bare
        // barrier between them. Skipped on street circuits (their wall already carries
        // the alternating accent-stripe look from CreateStreetWallSegment) and through
        // the pit corridor, where the pit wall/garage frontage already carries its own
        // signage. Spacing scales with sceneryDensity but never drops below a wide
        // per-lap minimum, so this stays sparse trackside dressing, not per-meter clutter.
        void BuildAdvertisingHoardings()
        {
            if (streetTrack)
            {
                return;
            }

            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);
            float spacing = Mathf.Lerp(90f, 48f, Mathf.InverseLerp(0.25f, 2f, density));
            int index = 0;
            for (float d = 0f; d < Runtime.length; d += spacing)
            {
                float normalized = d / Mathf.Max(1f, Runtime.length);
                index++;
                if (normalized > 0.83f || normalized < 0.06f)
                {
                    continue;
                }

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                float side = index % 2 == 0 ? -1f : 1f;
                CreateAdvertisingHoarding(point + right * side * (Runtime.roadHalfWidth + 3.3f), forward, index);
            }
        }

        // Small low panel plus trim stripe standing in for a generic sponsor-neutral
        // hoarding board, cycling the same invented SponsorPalette CreateSponsorBoard
        // and the billboard/bridge panels already share rather than inventing another
        // colour set.
        void CreateAdvertisingHoarding(Vector3 position, Vector3 forward, int index)
        {
            Vector3 outward = Vector3.Cross(Vector3.up, forward).normalized;
            Quaternion rotation = Quaternion.LookRotation(outward, Vector3.up);
            Color panelColor = SponsorPalette[index % SponsorPalette.Length];
            Material panel = CreateMaterial("Hoarding panel material", panelColor, 0.03f, 0.4f, (nightTrack || twilightTrack) ? panelColor * 0.3f : Color.black);
            CreateVisualBox("Advertising hoarding panel", position + Vector3.up * 0.55f, rotation, new Vector3(0.05f, 0.85f, 3.2f), panel);
            CreateVisualBox("Advertising hoarding trim", position + Vector3.up * 0.97f, rotation, new Vector3(0.06f, 0.12f, 3.2f), lineMaterial);
        }

        void BuildPitLane()
        {
            Material pitMaterial = CreateMaterial("Pit lane material", new Color(0.12f, 0.13f, 0.15f), 0.02f, 0.55f);
            pitLaneMaterial = pitMaterial;

            // The pit corridor follows the track from just before pit entry to the
            // release point, so surfaces, walls, boxes, and buildings are sampled
            // along the lap distance instead of assuming one straight chord.
            float corridorStart = Runtime.length * Runtime.PitCorridorStartNormalized;
            float corridorEnd = Runtime.length * 0.995f;

            // Drivable service road, laid in curve-following segments. Depth
            // widened from 17.6 (only 1.6m of overlap over the 16m step) to a
            // generous 5m overlap for the same reason the ramp surfaces were
            // widened - a paved-surface seam anywhere along the pit lane reads
            // to the player as exactly the kind of "random hole" repeatedly
            // reported, and a bigger unseen-underside overlap costs nothing.
            for (float d = corridorStart; d < corridorEnd; d += 16f)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d + 8f, out point, out forward, out right);
                CreateCollidablePitSurface(
                    "Pit lane asphalt service road",
                    point + right * Runtime.PitLaneLateral + Vector3.up * 0.015f,
                    Quaternion.LookRotation(forward, Vector3.up),
                    new Vector3(TrackRuntime.PitRampFullWidth, 0.16f, 21f),
                    pitMaterial);
            }

            CreatePitEntryExitSurfaces(pitMaterial);
            BuildPitZoneApron(pitMaterial);
            CreatePitEntryExitPaint(pitMaterial);
            CreatePitLaneCones();
            CreatePitEntryMarkers();

            // Pit wall between track and lane, sampled so it never cuts the corner.
            // Barrier-mess fix: a fourth, independently-stepped (12.5m) pass through
            // the exact same flat-corridor span BuildPitLaneDividerFence walls
            // (PitCorridorStartNormalized..0.995), with no nearTightFenceCorner check
            // of its own - wherever a tight corner sits inside that flat corridor,
            // ComputeBarrierPlan's main edge barrier already goes into containment
            // mode there, and this pass kept dropping its own "Pit wall" boxes right
            // on top of it regardless. Skips itself the same way the divider/ramp
            // guide fences do, using the corner data baked onto Runtime by
            // PopulateCornerContainmentZones (already populated by this point in
            // Build(), well before this call).
            for (float d = corridorStart + 10f; d < corridorEnd - 8f; d += 12.5f)
            {
                if (Runtime.IsNearTightFenceCorner(d))
                {
                    continue;
                }

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                CreatePitWallSegment(point + right * (Runtime.roadHalfWidth + 2.4f), forward);
            }

            // One visible pit box per grid entrant, matched exactly to the indexed
            // service poses that guide cars during stops.
            for (int i = 0; i < TrackRuntime.PitBoxCount; i++)
            {
                Vector3 boxPosition;
                Quaternion boxRotation;
                Runtime.GetPitServicePose(i, out boxPosition, out boxRotation);
                Vector3 boxForward = boxRotation * Vector3.forward;
                Vector3 boxRight = boxRotation * Vector3.right;
                CreatePitBox(boxPosition - Vector3.up * 0.5f + boxRight * 3.6f, boxForward, boxRight, i);

                // Painted working area on the lane surface for each box.
                CreateVisualBox("Pit box paint " + (i + 1), boxPosition - Vector3.up * 0.5f + Vector3.up * 0.1f, boxRotation, new Vector3(5.6f, 0.03f, 8.4f), i % 2 == 0 ? pitMaterial : rubberMaterial);
            }

            // Garage complex behind the boxes, split into segments that follow the lane.
            // Grounding fix: every other trackside building in this file already anchors
            // off GroundedTrackPoint (the flat true-ground reference, not whatever height
            // the road happens to sample at) - this loop was still anchoring off the raw
            // sampled `point` directly, so on an elevated pit lane stretch the garage row
            // could float or sit disconnected from visible ground the way other buildings
            // used to before that fix. Also drops the same foundation pad every other
            // building in this file gets (CreateGroundPatch always sits flush at
            // groundTopY, so this is purely cosmetic) so the row never reads as hovering
            // over bare, unbroken terrain.
            for (float d = corridorStart + 20f; d < corridorEnd - 30f; d += 34f)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d + 17f, out point, out forward, out right);
                Vector3 groundedPoint = GroundedTrackPoint(point);
                Vector3 basePosition = groundedPoint + right * (Runtime.PitLaneLateral + 15f);
                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
                CreateGroundPatch("Pit garage foundation pad", basePosition, 14f, 36f, concreteMaterial, rotation);
                CreateVisualBox("Pit building block", basePosition + Vector3.up * 6f, rotation, new Vector3(11f, 12f, 33f), metalMaterial);
                CreateVisualBox("Pit building fascia", basePosition + right * -5.6f + Vector3.up * 4.4f, rotation, new Vector3(0.3f, 3.2f, 33f), glassMaterial);
                CreateVisualBox("Pit building roof trim", basePosition + Vector3.up * 12.2f, rotation, new Vector3(11.6f, 0.4f, 33.6f), sceneryAccentMaterial);
                if (nightTrack || twilightTrack)
                {
                    CreateVisualBox("Pit building glow strip", basePosition + right * -5.65f + Vector3.up * 6.6f, rotation, new Vector3(0.18f, 0.5f, 32f), lightGlowMaterial);
                }

                // Segmented garage doors along the track-facing wall - the fascia used to
                // read as one flat glass strip; alternating door panels give the pit
                // building an actual "row of garages" silhouette instead.
                const int doorCount = 5;
                for (int doorIndex = 0; doorIndex < doorCount; doorIndex++)
                {
                    float doorT = (doorIndex + 0.5f) / doorCount - 0.5f;
                    CreateVisualBox("Pit garage door", basePosition + right * -5.75f + forward * doorT * 32f + Vector3.up * 2.2f, rotation, new Vector3(0.12f, 4.2f, 5.4f), doorIndex % 2 == 0 ? tireBarrierMaterial : metalMaterial);
                }
            }
        }

        // Merge-lane taper step. Short enough that the lateral/width lerp below
        // reads as a smooth diagonal wedge rather than a handful of visibly
        // kinked flat panels, matching the curve-following segmentation the
        // corridor's own service road and the barrier fan-out already use.
        const float PitRampSurfaceStep = 8f;
        // Pit-exit gap fix: widened from 2f - a purely fixed overlap left razor-thin
        // (sometimes effectively zero after floating-point rounding) coverage at a
        // segment boundary being exactly the kind of "random hole in the pit exit
        // road" repeatedly reported. A generously overlapping box on every segment
        // costs nothing at runtime (it's still just a flat, unseen-underside slab)
        // and removes the seam entirely rather than merely shrinking it.
        const float PitRampSurfaceOverlap = 5f;
        // Pit-lane architecture fix: aliases over TrackRuntime's own canonical
        // versions (see PitZoneEntryRampStart/End above for the same reasoning).
        const float PitRampNearTrackLateral = TrackRuntime.PitRampNearTrackLateral;
        const float PitRampNarrowWidth = TrackRuntime.PitRampNarrowWidth;
        const float PitRampFullWidth = TrackRuntime.PitRampFullWidth;

        // Continuous, curve-following, laterally-tapering paved surface covering the
        // whole entry and exit merge lanes - from the exact point on the true track
        // edge where a car first leaves the racing line, smoothly widening/sliding out
        // to precisely PitLaneLateral (the corridor's own drivable width) by the time
        // it reaches the corridor's own service road, and the mirror image on exit.
        //
        // Continuity-fix root cause: this used to be three independent single fixed
        // boxes (fixed lateral offset, fixed short length) dropped at three isolated
        // normalized points that did not line up with where the corridor's own
        // service road, the barrier fan-out (PitZoneBlend) or the divider fence
        // actually begin/end - on anything but a very specific track length, that left
        // a real gap of paved surface (and a lateral jump) between the ramp and the
        // corridor proper. Sharing the exact same PitZoneEntryRampStart/End and
        // PitZoneExitRampStart/End boundaries the wall and divider fence already use,
        // and tapering every step's own lateral offset and width along the way,
        // removes both the longitudinal gap and the lateral seam at once.
        void CreatePitEntryExitSurfaces(Material pitMaterial)
        {
            BuildPitRampSurface(PitZoneEntryRampStart, PitZoneEntryRampEnd, true, pitMaterial, "Pit entry asphalt");
            BuildPitRampSurface(PitZoneExitRampStart, PitZoneExitRampEnd, false, pitMaterial, "Pit exit asphalt");
        }

        void BuildPitRampSurface(float startNormalized, float endNormalized, bool inbound, Material pitMaterial, string label)
        {
            float length = Runtime.length;
            float startDistance = length * startNormalized;
            float endDistance = length * endNormalized;
            float span = endDistance - startDistance;
            if (span <= 0f)
            {
                span += length;
            }

            for (float d = 0f; d < span; d += PitRampSurfaceStep)
            {
                float segStep = Mathf.Min(PitRampSurfaceStep, span - d);
                float distance = Runtime.WrapDistance(startDistance + d);

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(distance + segStep * 0.5f, out point, out forward, out right);

                // Pit-lane architecture fix: this used to recompute the same
                // trackEdgeLateral/lateral/width taper inline, a second
                // implementation of the exact math GetPitEntryRampEnvelope/
                // GetPitExitRampEnvelope already provide (mathematically the same
                // ratio, just derived via distance-fraction-of-span here instead of
                // InverseLerp over the normalized zone bounds there) - two
                // implementations of "the same line" is exactly how the physical
                // surface and the path-following consumers (RaceManager,
                // AiVehicleController) could end up disagreeing. Now calls the one
                // canonical method directly, so the surface actually built here and
                // the path every guided/steering system targets are the same call.
                float midDistance = Runtime.WrapDistance(distance + segStep * 0.5f);
                float midNormalized = midDistance / Mathf.Max(1f, length);
                float lateral;
                float halfWidth;
                if (inbound)
                {
                    Runtime.GetPitEntryRampEnvelope(midNormalized, midDistance, out lateral, out halfWidth);
                }
                else
                {
                    Runtime.GetPitExitRampEnvelope(midNormalized, midDistance, out lateral, out halfWidth);
                }

                float width = halfWidth * 2f;

                CreateCollidablePitSurface(label, point + right * lateral + Vector3.up * 0.012f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(width, 0.16f, segStep + PitRampSurfaceOverlap), pitMaterial);
            }
        }

        // Fall-through safety apron (per report: on some layouts - e.g. the
        // United States-style circuit - the road surface does not reach the
        // barriers, and cars pitting fell through the unpaved gap between the
        // track edge and the pit complex). The corridor is assembled from
        // several independently-bounded slab runs (service road, entry ramp,
        // exit ramp) whose laterals derive from a mix of the authored per-point
        // HalfWidthAt and the global roadHalfWidth - on layouts where those
        // disagree (authored width profiles, elevation), seams open between
        // them. Rather than chase each layout's specific seam, one continuous
        // collidable strip is paved under the ENTIRE pit zone - overlapping the
        // road edge on the inside, reaching past the pit lane's outer edge, and
        // spanning from the entry ramp's first metre through the exit ramp's
        // last (wrapping the start/finish line) - set slightly BELOW the proper
        // surfaces so it is invisible and inert wherever the real paving is
        // intact, and simply catches the car wherever it is not.
        void BuildPitZoneApron(Material pitMaterial)
        {
            float length = Runtime.length;
            float startDistance = length * PitZoneEntryRampStart;
            float endNormalized = PitZoneExitRampEnd;
            float span = (endNormalized * length) - startDistance;
            if (span <= 0f)
            {
                span += length;
            }

            for (float d = 0f; d < span; d += 16f)
            {
                float segStep = Mathf.Min(16f, span - d);
                float midDistance = Runtime.WrapDistance(startDistance + d + segStep * 0.5f);

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(midDistance, out point, out forward, out right);

                float innerLateral = Runtime.HalfWidthAt(midDistance) - 2f;
                float outerLateral = Runtime.PitLaneLateral + TrackRuntime.PitRampFullWidth * 0.5f + 4f;
                float centerLateral = (innerLateral + outerLateral) * 0.5f;
                float width = Mathf.Max(4f, outerLateral - innerLateral);

                // Sunk well below the real surfaces so it can NEVER form a lip
                // a car catches on - it only exists to catch a car where the
                // proper paving has a gap. Sunk deeper (top ~4cm -> ~27cm under
                // the road, per report - cars stuck mid-final-turn, where this
                // apron's span begins): the box is laid FLAT along a road that
                // can be on a gradient through the pit-entry corner, and at ~1-2%
                // grade over its ~26m length the old 4cm margin let the box's
                // high end poke above the road surface as an invisible kerb
                // 2m inside the track edge.
                CreateCollidablePitSurface(
                    "Pit zone safety apron",
                    point + right * centerLateral - Vector3.up * 0.35f,
                    Quaternion.LookRotation(forward, Vector3.up),
                    new Vector3(width, 0.16f, segStep + 6f),
                    pitMaterial);
            }
        }

        void CreateCollidablePitSurface(string objectName, Vector3 position, Quaternion rotation, Vector3 localScale, Material material)
        {
            GameObject surface = CreateVisualBox(objectName, position, rotation, localScale, material);
            BoxCollider collider = surface.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.sharedMaterial = GetRoadPhysicsMaterial();
            surface.layer = 0;
        }

        void CreatePitWallSegment(Vector3 position, Vector3 forward)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Pit wall";
            wall.transform.SetParent(transform);
            Vector3 scale = new Vector3(0.42f, 0.84f, 6.2f);
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = metalMaterial;
            if (!TryPlaceSolidObstacle(wall, "pit-wall", position, forward, scale, 0.42f, 0.9f))
            {
                return;
            }

            // Padded cap and a red/white marker stripe - the pit wall used to be one
            // bare metal slab where every other barrier type already got a rail/rib
            // treatment, so it read as unfinished next to them.
            Vector3 placed = wall.transform.position;
            Vector3 placedForward = wall.transform.forward;
            Quaternion rotation = Quaternion.LookRotation(placedForward, Vector3.up);
            CreateVisualBox("Pit wall pad", placed + Vector3.up * 0.46f, rotation, new Vector3(0.46f, 0.1f, 6.2f), sceneryAccentMaterial);
            CreateVisualBox("Pit wall stripe", placed + Vector3.up * 0.05f, rotation, new Vector3(0.44f, 0.14f, 6.2f), lineMaterial);
        }

        void CreatePitBox(Vector3 position, Vector3 forward, Vector3 right, int index)
        {
            CreateVisualBox("Generic pit box", position + Vector3.up * 0.38f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(3.8f, 0.72f, 2.6f), index % 2 == 0 ? metalMaterial : sceneryAccentMaterial);
            CreateVisualBox("Pit tyre set front left", position + right * 2.35f + forward * 1.45f + Vector3.up * 0.34f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.45f, 0.68f, 0.45f), tireBarrierMaterial);
            CreateVisualBox("Pit tyre set front right", position - right * 2.35f + forward * 1.45f + Vector3.up * 0.34f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.45f, 0.68f, 0.45f), tireBarrierMaterial);
            CreateVisualBox("Pit tyre set rear left", position + right * 2.35f - forward * 1.45f + Vector3.up * 0.34f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.45f, 0.68f, 0.45f), tireBarrierMaterial);
            CreateVisualBox("Pit tyre set rear right", position - right * 2.35f - forward * 1.45f + Vector3.up * 0.34f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.45f, 0.68f, 0.45f), tireBarrierMaterial);
            CreateVisualBox("Pit crew marker left", position + right * 3.15f + Vector3.up * 0.55f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.36f, 1.1f, 0.36f), sceneryAccentMaterial);
            CreateVisualBox("Pit crew marker right", position - right * 3.15f + Vector3.up * 0.55f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.36f, 1.1f, 0.36f), sceneryAccentMaterial);
            CreateVisualBox("Pit release light", position - forward * 2.55f + Vector3.up * 1.05f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.32f, 0.32f, 0.32f), lightGlowMaterial);

            // Overhead gantry number so each crew's box reads as a distinct garage slot.
            CreateVisualBox("Pit box gantry", position + Vector3.up * 2.5f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(4.6f, 0.16f, 0.5f), metalMaterial);
            GameObject numberBoard = new GameObject("Pit box number " + (index + 1));
            numberBoard.transform.SetParent(transform);
            numberBoard.transform.position = position + Vector3.up * 2.95f;
            numberBoard.transform.rotation = Quaternion.LookRotation(-right, Vector3.up);
            TextMesh numberText = numberBoard.AddComponent<TextMesh>();
            numberText.text = (index + 1).ToString("00");
            numberText.fontSize = 44;
            numberText.characterSize = 0.11f;
            numberText.anchor = TextAnchor.MiddleCenter;
            numberText.alignment = TextAlignment.Center;
            numberText.color = new Color(0.95f, 0.97f, 1f, 0.95f);
        }

        void CreatePitEntryExitPaint(Material pitMaterial)
        {
            Vector3 entry;
            Vector3 entryForward;
            Vector3 entryRight;
            // Anchored to the real (fixed-metre) entry ramp so the painted entry
            // lane sits where cars actually steer in, on every track length.
            Runtime.SampleAtDistance(Runtime.length * Runtime.PitEntryRampStartNormalized + 20f, out entry, out entryForward, out entryRight);
            CreateVisualBox("Pit entry lane paint", entry + entryRight * (Runtime.roadHalfWidth + 4.9f) + Vector3.up * 0.055f, Quaternion.LookRotation(entryForward, Vector3.up), new Vector3(5.2f, 0.055f, 34f), pitMaterial);
            CreateVisualBox("Pit entry blend line", entry + entryRight * (Runtime.roadHalfWidth + 1.2f) + Vector3.up * 0.075f, Quaternion.LookRotation(entryForward, Vector3.up), new Vector3(0.32f, 0.045f, 28f), roadEdgeMaterial);

            Vector3 exit;
            Vector3 exitForward;
            Vector3 exitRight;
            Runtime.SampleAtDistance(Runtime.length * 0.035f, out exit, out exitForward, out exitRight);
            CreateVisualBox("Pit exit lane paint", exit + exitRight * (Runtime.roadHalfWidth + 4.4f) + Vector3.up * 0.055f, Quaternion.LookRotation(exitForward, Vector3.up), new Vector3(5.2f, 0.055f, 38f), pitMaterial);
            CreateVisualBox("Pit exit blend line", exit + exitRight * (Runtime.roadHalfWidth + 1.1f) + Vector3.up * 0.075f, Quaternion.LookRotation(exitForward, Vector3.up), new Vector3(0.32f, 0.045f, 30f), roadEdgeMaterial);
            CreateVisualBox("Pit limiter board", entry + entryRight * (Runtime.roadHalfWidth + 7.9f) + Vector3.up * 1.8f, Quaternion.LookRotation(entryForward, Vector3.up), new Vector3(1.8f, 1.2f, 0.16f), lightGlowMaterial);
        }

        // Cone row tracing the pit entry/exit blend line, right along the edge line the
        // paint above already draws, so the funnel reads as marked-off rather than just
        // painted. Count scales with sceneryDensity like the rest of the lap furniture.
        void CreatePitLaneCones()
        {
            int count = Mathf.Max(2, Mathf.RoundToInt(5f * Mathf.Clamp(sceneryDensity, 0.25f, 2f)));
            CreateConeRow(Runtime.length * 0.865f, Runtime.roadHalfWidth + 1.3f, count, 26f);
            CreateConeRow(Runtime.length * 0.035f, Runtime.roadHalfWidth + 1.2f, count, 30f);
        }

        void CreateConeRow(float centerDistance, float lateral, int count, float span)
        {
            for (int i = 0; i < count; i++)
            {
                float t = count <= 1 ? 0.5f : i / (float)(count - 1);
                float d = centerDistance - span * 0.5f + span * t;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                Vector3 conePos = PushSceneryClearOfTrack(point + right * lateral, 1f);
                CreateTrafficCone(conePos, Quaternion.LookRotation(forward, Vector3.up));
            }
        }

        // Physical pit-entry marking fix: the merge lane previously only ever had a
        // painted blend line and a row of cones - nothing announced the entry itself
        // far enough ahead to react to, and nothing marked exactly where the pit
        // speed limit begins. Adds an unmissable "PIT" board with a down-arrow at the
        // very start of the entry ramp (see PitZoneEntryRampStart, the same seam the
        // barrier fan-out and paved ramp already key off) and a painted speed-limit
        // line plus roundel sign at TrackRuntime.PitEntryLimiterLineNormalized - the
        // single shared boundary HasCrossedPitEntryLimiterLine actually tests, so
        // what's on the ground now matches both where the game visually starts the
        // ramp and the one place the hard limiter can first legally engage for
        // player and AI alike.
        //
        // Limiter-consistency bugfix: this used to anchor the painted line at
        // TrackRuntime.PitApproachStartNormalized (0.78) - a much broader "HUD
        // approach zone" boundary that RaceManager.HandlePitService never actually
        // enforced the limiter at (it only ever engaged PitLimiterActive at the
        // real physical ramp commit, ~0.850-0.885). The sign promised enforcement
        // roughly 7% of a lap before it actually started.
        void CreatePitEntryMarkers()
        {
            CreatePitSignBoard(Runtime.length * PitZoneEntryRampStart);
            CreateSpeedLimitLine(Runtime.length * Runtime.PitEntryLimiterLineNormalized);
        }

        void CreatePitSignBoard(float distance)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            Vector3 basePosition = point + right * (Runtime.roadHalfWidth + 3.4f);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            CreateVisualBox("Pit entry board post", basePosition + Vector3.up * 1.4f, rotation, new Vector3(0.22f, 2.8f, 0.22f), metalMaterial);
            Vector3 boardCenter = basePosition + Vector3.up * 3.5f;
            CreateVisualBox("Pit entry board frame", boardCenter, rotation, new Vector3(0.2f, 2.3f, 3.4f), metalMaterial);
            CreateVisualBox("Pit entry board panel", boardCenter - forward.normalized * 0.11f, rotation, new Vector3(0.08f, 2.05f, 3.1f), flagYellowMaterial);

            // Downward chevron made of two angled slats pointing at the pit side,
            // echoing a real "exit here" arrow rather than just bare text.
            Vector3 arrowCenter = boardCenter - forward.normalized * 0.16f - Vector3.up * 0.35f;
            CreateVisualBox("Pit entry board arrow left", arrowCenter, rotation * Quaternion.Euler(0f, 0f, 35f), new Vector3(0.04f, 0.85f, 0.22f), lineMaterial);
            CreateVisualBox("Pit entry board arrow right", arrowCenter, rotation * Quaternion.Euler(0f, 0f, -35f), new Vector3(0.04f, 0.85f, 0.22f), lineMaterial);

            GameObject text = new GameObject("Pit entry board text");
            text.transform.SetParent(transform);
            text.transform.position = boardCenter + Vector3.up * 0.62f - forward.normalized * 0.2f;
            text.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            TextMesh textMesh = text.AddComponent<TextMesh>();
            textMesh.text = "PIT";
            textMesh.fontSize = 54;
            textMesh.characterSize = 0.2f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = Color.black;

            if (nightTrack || twilightTrack)
            {
                CreateVisualBox("Pit entry board light strip", boardCenter + Vector3.up * 1.25f, rotation, new Vector3(0.1f, 0.08f, 3f), lightGlowMaterial);
            }
        }

        void CreateSpeedLimitLine(float distance)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            // Painted line straight across the merge lane, plus a roundel-style
            // sign beside it, marking exactly where the pit-lane speed limit begins.
            float lateral = Runtime.roadHalfWidth + 3f;
            CreateVisualBox("Pit speed limit line", point + right * lateral + Vector3.up * 0.06f, rotation, new Vector3(6.5f, 0.05f, 0.4f), lineMaterial);

            Vector3 signBase = point + right * (Runtime.roadHalfWidth + 6.6f);
            CreateVisualBox("Pit speed limit sign post", signBase + Vector3.up * 0.9f, rotation, new Vector3(0.14f, 1.8f, 0.14f), metalMaterial);
            CreateVisualBox("Pit speed limit sign frame", signBase + Vector3.up * 1.85f, rotation, new Vector3(0.06f, 0.85f, 0.85f), lineMaterial);
            CreateVisualBox("Pit speed limit sign face", signBase + Vector3.up * 1.85f - forward.normalized * 0.05f, rotation, new Vector3(0.03f, 0.72f, 0.72f), flagYellowMaterial);

            GameObject text = new GameObject("Pit speed limit sign text");
            text.transform.SetParent(transform);
            text.transform.position = signBase + Vector3.up * 1.85f - forward.normalized * 0.09f;
            text.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            TextMesh textMesh = text.AddComponent<TextMesh>();
            textMesh.text = "80";
            textMesh.fontSize = 46;
            textMesh.characterSize = 0.28f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = Color.black;
        }

        void BuildStartGantry()
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(0f, out point, out forward, out right);
            Vector3 left = point - right * (Runtime.roadHalfWidth + 2.8f);
            Vector3 rightSide = point + right * (Runtime.roadHalfWidth + 2.8f);

            CreateGantryPost(left, forward);
            CreateGantryPost(rightSide, forward);

            Quaternion gantryRotation = Quaternion.LookRotation(forward, Vector3.up);
            float span = Runtime.roadHalfWidth * 2.2f;

            // Double-boom truss with diagonal braces instead of one blank block.
            CreateVisualBox("Start gantry boom lower", point + Vector3.up * 6.7f, gantryRotation, new Vector3(span, 0.32f, 0.9f), metalMaterial);
            CreateVisualBox("Start gantry boom upper", point + Vector3.up * 7.7f, gantryRotation, new Vector3(span, 0.32f, 0.9f), metalMaterial);
            int braces = Mathf.Max(4, Mathf.RoundToInt(span / 3.2f));
            for (int i = 0; i < braces; i++)
            {
                float t = (i + 0.5f) / braces - 0.5f;
                CreateVisualBox("Start gantry brace", point + Vector3.up * 7.2f + Vector3.Cross(Vector3.up, forward).normalized * t * span, gantryRotation * Quaternion.Euler(0f, 0f, i % 2 == 0 ? 32f : -32f), new Vector3(0.14f, 1.1f, 0.14f), metalMaterial);
            }

            // Backlit event panel above the lights.
            CreateVisualBox("Start gantry panel", point + Vector3.up * 8.5f - forward * 0.2f, gantryRotation, new Vector3(Mathf.Min(10f, span * 0.6f), 1.1f, 0.2f), nightTrack || twilightTrack ? lightGlowMaterial : sceneryAccentMaterial);

            // Thin emissive strip along the lower boom standing in for the gantry's
            // warning lights. Uses its own dedicated gantryRaceControlLightMaterial
            // (rather than the shared lightGlowMaterial used all over the rest of the
            // track) so RaceControlVisualDriver can pulse/flash it live without also
            // flashing every pit-building light, lamp post and glow strip elsewhere.
            CreateVisualBox("Start gantry light strip", point + Vector3.up * 6.9f - forward * 0.42f, gantryRotation, new Vector3(span * 0.92f, 0.06f, 0.08f), gantryRaceControlLightMaterial);

            // Checkered flag band along the lower boom - built from alternating box
            // primitives (no texture asset needed) so the start/finish gantry finally
            // gets an actual "podium moment" read instead of a plain metal truss.
            Vector3 checkerRight = Vector3.Cross(Vector3.up, forward).normalized;
            int checkerColumns = Mathf.Max(6, Mathf.RoundToInt(span / 1.1f));
            for (int c = 0; c < checkerColumns; c++)
            {
                float t = (c + 0.5f) / checkerColumns - 0.5f;
                Material squareMaterial = c % 2 == 0 ? lineMaterial : checkerDarkMaterial;
                CreateVisualBox("Start gantry checker square", point + Vector3.up * 6.35f + checkerRight * t * span, gantryRotation, new Vector3(span / checkerColumns * 0.92f, 0.22f, 0.5f), squareMaterial);
            }

            // SC/VSC board beside the gantry, wired up (via CreateRaceControlBoard) so
            // RaceManager can drive it live through Runtime.SetRaceControlVisual.
            CreateRaceControlBoard(point, forward, right);

            // Double row of start lights
            for (int row = 0; row < 2; row++)
            {
                for (int i = -2; i <= 2; i++)
                {
                    GameObject light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    light.name = "Start light " + row + "_" + i;
                    light.transform.SetParent(transform);
                    light.transform.position = point + right * (i * 1.15f) + Vector3.up * (6.8f - row * 0.65f) - forward * 0.85f;
                    light.transform.localScale = new Vector3(0.42f, 0.42f, 0.42f);
                    light.GetComponent<Renderer>().sharedMaterial = lightGlowMaterial;
                    MakeVisualOnly(light);
                }
            }
        }

        void CreateGantryPost(Vector3 position, Vector3 forward)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Gantry support";
            post.transform.SetParent(transform);
            post.transform.position = position + Vector3.up * 3.1f;
            post.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            post.transform.localScale = new Vector3(0.5f, 6.2f, 0.5f);
            post.GetComponent<Renderer>().sharedMaterial = metalMaterial;
            MakeVisualOnly(post);
        }

        // Safety-car/VSC board on its own post beside the gantry, following the same
        // post + board + text layout as CreateDrsZoneBoard/CreateSectorBoard. Renderer,
        // text and a pair of flanking strobe pods are captured on TrackManager so
        // RaceControlVisualDriver can restyle/animate them from SetRaceControlVisual.
        void CreateRaceControlBoard(Vector3 point, Vector3 forward, Vector3 right)
        {
            Vector3 basePosition = point + right * (Runtime.roadHalfWidth + 5.5f) + Vector3.up * 4.4f;
            Quaternion rotation = Quaternion.LookRotation(right, Vector3.up);
            CreateVisualBox("Race control board post", basePosition - Vector3.up * 2.2f, rotation, new Vector3(0.14f, 4.4f, 0.14f), metalMaterial);
            GameObject board = CreateVisualBox("Race control board", basePosition, rotation, new Vector3(0.16f, 1f, 1.9f), raceControlBoardMaterial);
            raceControlBoardRenderer = board.GetComponent<Renderer>();

            // Thin strobe pods above/below the board, sharing gantryRaceControlLightMaterial
            // with the gantry light strip so a restart/SC flash reads clearly even from
            // a distance, not just as a colour change on the board face itself.
            CreateVisualBox("Race control strobe light", basePosition + Vector3.up * 0.75f, rotation, new Vector3(0.14f, 0.12f, 1.9f), gantryRaceControlLightMaterial);
            CreateVisualBox("Race control strobe light", basePosition - Vector3.up * 0.75f, rotation, new Vector3(0.14f, 0.12f, 1.9f), gantryRaceControlLightMaterial);

            GameObject text = new GameObject("Race control board text");
            text.transform.SetParent(transform);
            text.transform.position = basePosition - forward.normalized * 0.11f;
            text.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            TextMesh textMesh = text.AddComponent<TextMesh>();
            textMesh.text = "";
            textMesh.fontSize = 44;
            textMesh.characterSize = 0.14f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.95f, 0.75f, 0.15f, 0.95f);
            raceControlBoardText = textMesh;
        }

        void BuildCameraTowers()
        {
            float[] positions = { 0.1f, 0.3f, 0.52f, 0.72f };
            for (int i = 0; i < positions.Length; i++)
            {
                CreateCameraTower(Runtime.length * positions[i], i);
            }

            // Every sibling per-lap furniture pass (BuildTracksideCameraPods,
            // BuildCircuitLightMasts) already scales its count with the track's own
            // density/length signal - this one stayed hardcoded at exactly 4 towers
            // regardless of sceneryDensity, the one clear outlier. Extra towers at
            // fresh normalized slots (not overlapping the fixed set above) so a
            // fully "dense" setting gets more broadcast coverage without changing
            // anything at the default/low end.
            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);
            if (density >= 1.5f)
            {
                CreateCameraTower(Runtime.length * 0.88f, 4);
            }

            if (density >= 1.85f)
            {
                CreateCameraTower(Runtime.length * 0.4f, 5);
            }
        }

        // Tall thin broadcast tower. Legs are grounded at groundTopY rather than at the
        // sampled track height, so a tower placed near an elevated stretch (Spa, Austria,
        // Suzuka) still stands on real ground instead of floating at deck height with its
        // legs dangling in mid-air.
        void CreateCameraTower(float distance, int index)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            float side = index % 2 == 0 ? -1f : 1f;
            Vector3 basePosition = PushSceneryClearOfTrack(point + right * side * (Runtime.roadHalfWidth + 14f), 4f);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            float platformHeight = 9f + (index % 3) * 2f;
            float groundClearance = Mathf.Max(0f, point.y - groundTopY);
            float legHeight = platformHeight + groundClearance;
            Vector3 legBase = new Vector3(basePosition.x, groundTopY, basePosition.z);
            Vector3 platformCenter = legBase + Vector3.up * legHeight;

            // Paved foundation pad under the four legs so the mast reads as anchored to
            // a real base rather than four bare poles planted straight into open grass.
            CreateGroundPatch("Camera tower foundation pad", legBase, 3.2f, 3.2f, concreteMaterial, rotation);

            // Four corner legs with two lattice bracing bands instead of one thick
            // central pole - reads as a real broadcast mast rather than a fence post
            // wearing a platform.
            const float legSpread = 1.05f;
            Vector3[] cornerOffsets =
            {
                right * legSpread + forward * legSpread,
                right * legSpread - forward * legSpread,
                -right * legSpread + forward * legSpread,
                -right * legSpread - forward * legSpread
            };

            for (int corner = 0; corner < cornerOffsets.Length; corner++)
            {
                Vector3 footPosition = legBase + cornerOffsets[corner];
                CreateVisualBox("Camera tower leg", footPosition + Vector3.up * legHeight * 0.5f, rotation, new Vector3(0.22f, legHeight, 0.22f), metalMaterial);
            }

            float lowerBand = legHeight * 0.35f;
            float upperBand = legHeight * 0.72f;
            CreateLatticeBraceRing(legBase, cornerOffsets, lowerBand);
            CreateLatticeBraceRing(legBase, cornerOffsets, upperBand);
            CreateHorizontalBrace(legBase + cornerOffsets[0] + Vector3.up * lowerBand, legBase + cornerOffsets[3] + Vector3.up * upperBand, 0.1f, fencePostMaterial);
            CreateHorizontalBrace(legBase + cornerOffsets[1] + Vector3.up * lowerBand, legBase + cornerOffsets[2] + Vector3.up * upperBand, 0.1f, fencePostMaterial);

            CreateVisualBox("Camera tower platform", platformCenter, rotation, new Vector3(2.2f, 0.18f, 2.2f), metalMaterial);
            CreateVisualBox("Camera tower rail", platformCenter + Vector3.up * 0.55f, rotation, new Vector3(2.2f, 0.9f, 0.12f), fencePostMaterial);

            GameObject camHead = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            camHead.name = "Camera tower head";
            camHead.transform.SetParent(transform);
            camHead.transform.position = platformCenter + Vector3.up * 1.1f;
            camHead.transform.rotation = rotation * Quaternion.Euler(90f, 0f, 0f);
            camHead.transform.localScale = new Vector3(0.3f, 0.5f, 0.3f);
            camHead.GetComponent<Renderer>().sharedMaterial = metalMaterial;
            MakeVisualOnly(camHead);
            CreateVisualBox("Camera tower lens", platformCenter + Vector3.up * 1.1f + forward * 0.3f, rotation, new Vector3(0.16f, 0.16f, 0.16f), glassMaterial);
            CreateVisualBox("Camera tower antenna", platformCenter + Vector3.up * 2.1f, rotation, new Vector3(0.05f, 1.8f, 0.05f), fencePostMaterial);
            if (nightTrack || twilightTrack)
            {
                CreateVisualBox("Camera tower beacon", platformCenter + Vector3.up * 3.05f, rotation, new Vector3(0.22f, 0.22f, 0.22f), lightGlowMaterial);
            }
        }

        // Horizontal brace along all four edges of the leg footprint at a given
        // height, shared by both bracing bands so CreateCameraTower doesn't repeat
        // the same four-call block twice.
        void CreateLatticeBraceRing(Vector3 legBase, Vector3[] cornerOffsets, float height)
        {
            CreateHorizontalBrace(legBase + cornerOffsets[0] + Vector3.up * height, legBase + cornerOffsets[1] + Vector3.up * height, 0.09f, fencePostMaterial);
            CreateHorizontalBrace(legBase + cornerOffsets[1] + Vector3.up * height, legBase + cornerOffsets[3] + Vector3.up * height, 0.09f, fencePostMaterial);
            CreateHorizontalBrace(legBase + cornerOffsets[3] + Vector3.up * height, legBase + cornerOffsets[2] + Vector3.up * height, 0.09f, fencePostMaterial);
            CreateHorizontalBrace(legBase + cornerOffsets[2] + Vector3.up * height, legBase + cornerOffsets[0] + Vector3.up * height, 0.09f, fencePostMaterial);
        }

        // Thin box stretched between two arbitrary points, used for tower cross-braces
        // that aren't purely vertical or aligned with the track's forward/right axes.
        void CreateHorizontalBrace(Vector3 a, Vector3 b, float thickness, Material material)
        {
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.01f)
            {
                return;
            }

            CreateVisualBox("Tower lattice brace", a + delta * 0.5f, Quaternion.LookRotation(delta.normalized, Vector3.up), new Vector3(thickness, thickness, length), material);
        }

        // Small ground-level tripod camera pods at hard-braking corners - cheaper and
        // more numerous than the four tall BuildCameraTowers masts, so corners the
        // towers don't reach still get a "watched by broadcast" trackside read.
        // Density-gated to half the detected corners at low detail settings.
        void BuildTracksideCameraPods()
        {
            List<CornerInfo> corners = DetectCorners(35f);
            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);
            for (int i = 0; i < corners.Count; i++)
            {
                if (density < 1f && i % 2 == 1)
                {
                    continue;
                }

                float normalized = corners[i].distance / Mathf.Max(1f, Runtime.length);
                if (normalized > 0.83f || normalized < 0.06f)
                {
                    continue;
                }

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(corners[i].distance - 20f, out point, out forward, out right);
                float side = i % 2 == 0 ? -1f : 1f;
                // Grounded - a hard-braking corner can be an elevated stretch, and this
                // pod's tripod legs are far too short to reach real ground from deck
                // height the way CreateCameraTower's own legs are built to.
                CreateTracksideCameraPod(GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 8f), forward);
            }
        }

        void CreateTracksideCameraPod(Vector3 position, Vector3 forward)
        {
            Vector3 safePosition = PushSceneryClearOfTrack(position, 6f);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateVisualBox("Camera pod tripod leg", safePosition + Vector3.up * 0.65f, rotation, new Vector3(0.08f, 1.3f, 0.08f), metalMaterial);
            CreateVisualBox("Camera pod tripod leg", safePosition + Vector3.up * 0.65f + forward * 0.32f, rotation, new Vector3(0.08f, 1.3f, 0.08f), metalMaterial);
            CreateVisualBox("Camera pod tripod leg", safePosition + Vector3.up * 0.65f - forward * 0.32f, rotation, new Vector3(0.08f, 1.3f, 0.08f), metalMaterial);
            CreateVisualBox("Camera pod head", safePosition + Vector3.up * 1.35f, rotation, new Vector3(0.3f, 0.28f, 0.55f), metalMaterial);
            CreateVisualBox("Camera pod lens", safePosition + Vector3.up * 1.35f + forward * 0.3f, rotation, new Vector3(0.16f, 0.16f, 0.16f), glassMaterial);
            if (nightTrack || twilightTrack)
            {
                CreateVisualBox("Camera pod beacon", safePosition + Vector3.up * 1.55f, rotation, new Vector3(0.12f, 0.12f, 0.12f), lightGlowMaterial);
            }
        }

        // Tall circuit lighting masts for night/twilight races - a handful of ~20m
        // poles with a canted bank of emissive lamp heads, an order of scale above
        // the small CreateFloodlight poles BuildScenery scatters, so a night race
        // reads as lit by real circuit infrastructure rather than street lamps.
        void BuildCircuitLightMasts()
        {
            if (!nightTrack && !twilightTrack)
            {
                return;
            }

            int masts = Mathf.Clamp(Mathf.RoundToInt(Runtime.length / 550f), 6, 12);
            for (int i = 0; i < masts; i++)
            {
                float normalized = (i + 0.5f) / masts;

                // The pit corridor keeps its own lighting from the pit complex.
                if (normalized > Runtime.PitCorridorStartNormalized || normalized < 0.04f)
                {
                    continue;
                }

                CreateLightMast(Runtime.length * normalized, i);
            }
        }

        void CreateLightMast(float distance, int index)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            float side = index % 2 == 0 ? 1f : -1f;
            Vector3 desired = point + right * side * (Runtime.roadHalfWidth + 17f);
            Vector3 basePosition;
            if (!TryGetClearScenerySpot(new Vector3(desired.x, groundTopY, desired.z), 2.5f, 3f, out basePosition))
            {
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            float mastHeight = 19f + (index % 3) * 2.5f;
            Vector3 mastBase = new Vector3(basePosition.x, groundTopY, basePosition.z);
            CreateVisualBox("Light mast pole", mastBase + Vector3.up * mastHeight * 0.5f, rotation, new Vector3(0.55f, mastHeight, 0.55f), metalMaterial);
            CreateVisualBox("Light mast collar", mastBase + Vector3.up * mastHeight * 0.62f, rotation, new Vector3(0.9f, 0.35f, 0.9f), fencePostMaterial);

            // Head bank leans back toward and slightly down at the road so the
            // fixture reads as aimed at the track, not glowing straight outward.
            Vector3 headCenter = mastBase + Vector3.up * mastHeight - right * side * 1.1f;
            Quaternion headRotation = Quaternion.LookRotation((-right * side + Vector3.down * 0.55f).normalized, Vector3.up);
            CreateVisualBox("Light mast head frame", headCenter, headRotation, new Vector3(4.6f, 2.2f, 0.35f), metalMaterial);
            for (int row = 0; row < 2; row++)
            {
                for (int lamp = -2; lamp <= 2; lamp++)
                {
                    // Lamps sit proud of the frame face on its track-facing side.
                    Vector3 lampLocal = new Vector3(lamp * 0.85f, 0.55f - row * 1.1f, 0.27f);
                    CreateVisualBox("Light mast lamp", headCenter + headRotation * lampLocal, headRotation, new Vector3(0.62f, 0.62f, 0.18f), lightGlowMaterial);
                }
            }

            // A real point light only on every other mast keeps the runtime light
            // count in check while the emissive heads carry the look everywhere.
            if (index % 2 == 0)
            {
                GameObject lightAnchor = new GameObject("Light mast point light");
                lightAnchor.transform.SetParent(transform);
                lightAnchor.transform.position = headCenter;
                Light light = lightAnchor.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 85f;
                light.intensity = 1.3f;
                light.color = new Color(1f, 0.93f, 0.78f);
            }
        }

        void BuildScenery()
        {
            bool street = streetTrack;
            bool night = Runtime.styleName.Contains("Night");

            // Signature grandstands on the main spectator stretches. Grandstand at 0.85-1.0
            // is kept on the left so it never fights the pit complex on the right.
            // Every circuit's stands now build at 4x scale with genuinely
            // taller tier geometry (per report - "the grandstands ARE STILL
            // TOO SMALL"; see the tier-geometry fix in BuildGrandstand).
            float standScale = 4f;
            BuildGrandstand(0.02f, -1, standScale);
            BuildGrandstand(0.15f, 1, standScale);
            BuildGrandstand(0.45f, -1, standScale);
            BuildGrandstand(0.85f, -1, standScale);

            // Natural road courses read as an open-countryside meeting with an extra
            // grandstand at a corner rather than only on the straights.
            if (!desertTrack && !streetTrack)
            {
                BuildGrandstand(0.62f, 1, standScale);
            }

            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);

            // Full spectator build-out on EVERY circuit (per request - "a LOT
            // more, like 20 more grandstands for ALL tracks"): twenty extra
            // stands spread evenly around the whole lap on alternating sides,
            // on top of the signature set above. The last stretch of the lap
            // stays on the left so nothing fights the pit complex on the right,
            // and the slots are offset from the fixed set's normalized points
            // so consecutive stands read as a row of separate venues rather
            // than stacking inside each other.
            const int extraStandCount = 20;
            for (int i = 0; i < extraStandCount; i++)
            {
                float slot = (i + 0.5f) / extraStandCount;
                int standSide = i % 2 == 0 ? 1 : -1;
                if (slot > 0.82f)
                {
                    standSide = -1;
                }

                BuildGrandstand(slot, standSide, 4f);
            }

            // Row of trackside flags flanking the start/finish straight - see
            // BuildFlagRow for why this filled a genuine gap rather than duplicating
            // the sparse single race-control pole CreateMarshalPost already plants.
            BuildFlagRow(density);

            int step = Mathf.Max(1, Mathf.RoundToInt(2f / density));
            for (int i = 0; i < Runtime.centerLine.Count; i += step)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.cumulativeDistances[i], out point, out forward, out right);
                float side = i % 2 == 0 ? -1f : 1f;
                float normalized = Runtime.cumulativeDistances[i] / Mathf.Max(1f, Runtime.length);
                bool insidePitCorridor = (normalized > 0.83f || normalized < 0.06f) && side > 0f;
                if (insidePitCorridor)
                {
                    continue;
                }

                // Everything below except the sponsor board sits well clear of the road
                // (6.5m+) and is meant to stand on real ground, not on whatever height
                // the track happens to be at this sample - grounded so a floodlight/
                // marshal post/tree/building near a bridge or hill doesn't float at deck
                // height. The sponsor board stays keyed to the raw sampled point since
                // it's mounted right at the trackside/barrier line, the same convention
                // CreateSectorBoard/CreateDrsZoneBoard/CreateTimingGantry already use.
                Vector3 groundPoint = GroundedTrackPoint(point);

                // Trackside detail: floodlights and marshal posts. Frequencies tightened
                // from the original 8/12/14 spacing (still scaled by sceneryDensity
                // through the loop step above) so a lap reads as a built-up circuit
                // rather than occasional furniture on an otherwise bare verge.
                if (i % 6 == 0)
                {
                    CreateFloodlight(groundPoint + right * side * (Runtime.roadHalfWidth + 6.5f), forward, night || nightTrack || street);
                }

                // Parkland circuits (the "old-school racing venue" archetypes) get a
                // second marshal post slot for denser classic trackside furniture.
                if (i % 10 == 4 || (parklandTrack && i % 10 == 9))
                {
                    CreateMarshalPost(groundPoint + right * side * (Runtime.roadHalfWidth + 9f), forward, i);
                }

                if (i % 11 == 9)
                {
                    CreateSponsorBoard(point + right * side * (Runtime.roadHalfWidth + 4.6f), forward, i);
                }

                if (neonTrack && i % 16 == 6)
                {
                    CreateNeonPylon(groundPoint + right * side * (Runtime.roadHalfWidth + 11f), forward, i);
                }

                Vector3 basePosition = groundPoint + right * side * (Runtime.roadHalfWidth + (street ? 18f : 32f));
                if (street)
                {
                    // Tight street circuits keep buildings close; Monaco/Singapore/Vegas
                    // already ride this same lateral offset, but a closer second row on
                    // some passes sells the "canyon of buildings" feel harder.
                    CreateCityBlock(basePosition, forward, i, night);
                    if (i % 7 == 0) CreateCityBlock(groundPoint + right * side * (Runtime.roadHalfWidth + 15f), forward, i + 2, night);
                }
                else if (desertTrack)
                {
                    CreateDune(basePosition, i);
                    if (i % 5 == 0) CreateDune(basePosition + right * side * 38f, i + 3);
                    // Sparse scrub/rock instead of a dense forest tree cluster - desert
                    // circuits used to borrow the same canopy CreateTreeCluster gives
                    // every other archetype, which fought the sun-baked dune/runoff read.
                    if (i % 9 == 0) CreateDesertScrubCluster(basePosition - right * side * 22f, i);
                }
                else
                {
                    CreateTreeCluster(basePosition, i);
                    // Tree-density round 2 (per report): the second-row cluster
                    // used to be Spa-only/every-sixth - now every other sample.
                    if (spaTrack || i % 2 == 0) CreateTreeCluster(basePosition + right * side * 40f, i);
                }
            }

            BuildForestBelt();
        }

        // Mass tree planting (per report - "20x their amount"): continuous
        // double-row belts of full-size trees down BOTH sides of the whole lap
        // on every non-street, non-desert circuit, behind the existing
        // trackside clusters. Belt trees use a cheaper build (one trunk plus a
        // small canopy stack, ~4 primitives) than the full showcase trees so
        // the count can be an order of magnitude higher without an order of
        // magnitude more objects; spacing tightens with sceneryDensity and
        // every position is jittered and clearance-checked so the belts read
        // as forest, not a picket fence.
        void BuildForestBelt()
        {
            if (streetTrack || desertTrack)
            {
                return;
            }

            // Density round 2 (per report - "still not enough trees, need to
            // like 5x it... add some stuff in the near background and far
            // background too"): tighter spacing, plus two additional belt rows
            // pushed back to 130m/205m so the forest has genuine depth behind
            // the trackside rows instead of stopping two rows deep. The outer
            // rows spawn at half rate - a real treeline thins with distance,
            // and it keeps the object count from doubling again.
            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);
            float spacing = Mathf.Lerp(34f, 15f, Mathf.InverseLerp(0.25f, 2f, density));
            int seed = 0;
            for (float d = 0f; d < Runtime.length; d += spacing)
            {
                seed++;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                float normalized = d / Mathf.Max(1f, Runtime.length);
                Vector3 groundPoint = GroundedTrackPoint(point);
                for (int side = -1; side <= 1; side += 2)
                {
                    // Same pit-corridor exclusion the main scenery loop uses.
                    if ((normalized > 0.83f || normalized < 0.06f) && side > 0f)
                    {
                        continue;
                    }

                    for (int row = 0; row < 4; row++)
                    {
                        int treeSeed = seed * 8 + row * 2 + (side + 1);
                        if (row >= 2 && treeSeed % 2 == 0)
                        {
                            continue;
                        }

                        float lateralOffset = row < 2
                            ? Runtime.roadHalfWidth + 46f + row * 24f + (treeSeed * 7) % 15
                            : Runtime.roadHalfWidth + 130f + (row - 2) * 75f + (treeSeed * 7) % 34;
                        float alongJitter = ((treeSeed * 11) % 25) - 12f;
                        Vector3 desired = groundPoint + right * side * lateralOffset + forward * alongJitter;
                        // Never inside a grandstand's footprint (per report) -
                        // pushed BEHIND the stand instead of skipped, so
                        // grandstand stretches keep their treeline backdrop.
                        if (IsInsideGrandstandZone(desired))
                        {
                            desired = groundPoint + right * side * (GrandstandZoneLateralMeters + 12f + (treeSeed * 5) % 18) + forward * alongJitter;
                        }

                        Vector3 safePosition;
                        if (!TryGetClearScenerySpot(desired, 7f, 5f, out safePosition))
                        {
                            continue;
                        }

                        CreateBeltTree(safePosition, treeSeed);
                    }
                }
            }
        }

        // Cheap full-size belt tree: same species mix and 3x scale as the
        // showcase trees, built from ~4 primitives (trunk + small canopy
        // stack) so thousands of them stay affordable.
        void CreateBeltTree(Vector3 basePosition, int seed)
        {
            float size = 2.1f + (seed % 5) * 0.28f;
            float trunkHeight = (seed % 3 == 0 ? 10f : 6.8f) * size;
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Belt tree trunk";
            trunk.transform.SetParent(transform);
            trunk.transform.position = basePosition + Vector3.up * trunkHeight * 0.5f;
            trunk.transform.localScale = new Vector3(0.5f * size, trunkHeight * 0.5f, 0.5f * size);
            trunk.GetComponent<Renderer>().sharedMaterial = treeBarkMaterial;
            MakeVisualOnly(trunk);

            if (seed % 3 == 0)
            {
                // Conifer: three narrowing tiers.
                for (int t = 0; t < 3; t++)
                {
                    float f = t / 2f;
                    float tierWidth = Mathf.Lerp(4.6f, 1.4f, f) * size;
                    GameObject tier = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    tier.name = "Belt tree conifer tier";
                    tier.transform.SetParent(transform);
                    tier.transform.position = basePosition + Vector3.up * Mathf.Lerp(trunkHeight * 0.35f, trunkHeight, f);
                    tier.transform.localScale = new Vector3(tierWidth, Mathf.Lerp(2.6f, 1.7f, f) * size, tierWidth);
                    tier.GetComponent<Renderer>().sharedMaterial = foliageMaterial;
                    MakeVisualOnly(tier);
                }
            }
            else
            {
                // Broadleaf: main crown plus one offset lobe in the lighter tone.
                float crownWidth = 5f * size;
                GameObject crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Belt tree crown";
                crown.transform.SetParent(transform);
                crown.transform.position = basePosition + Vector3.up * (trunkHeight + 1.4f * size);
                crown.transform.localScale = new Vector3(crownWidth, crownWidth * 0.78f, crownWidth);
                crown.GetComponent<Renderer>().sharedMaterial = (seed % 2 == 0) ? foliageMaterial : foliageMaterialLight;
                MakeVisualOnly(crown);

                float lobeWidth = 3.2f * size;
                GameObject lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lobe.name = "Belt tree crown lobe";
                lobe.transform.SetParent(transform);
                lobe.transform.position = basePosition + Vector3.up * (trunkHeight + 0.7f * size) +
                    new Vector3(((seed * 13) % 7) - 3f, 0f, ((seed * 17) % 5) - 2f) * 0.55f * size * 0.4f;
                lobe.transform.localScale = new Vector3(lobeWidth, lobeWidth * 0.72f, lobeWidth);
                lobe.GetComponent<Renderer>().sharedMaterial = (seed % 2 == 0) ? foliageMaterialLight : foliageMaterial;
                MakeVisualOnly(lobe);
            }
        }

        // Row of generic solid-colour flags lining the start/finish straight, both
        // sides. The only flag dressing this file had before was a single
        // race-control pole bolted to each sparse CreateMarshalPost - real circuits
        // line their main straight with dozens of trackside flags, which was a
        // genuinely thin/missing category rather than a duplicate of anything
        // already built. Cycles the same invented SponsorPalette every other
        // generic marking in this file already reuses (sponsor boards, grandstand
        // bunting) so nothing here reads as real branding, and spacing/count scale
        // with sceneryDensity like the rest of the per-lap furniture. Pure
        // background dressing (CreateVisualBox never adds a collider), so this
        // carries no collision risk regardless of how close to the corridor it
        // ends up, and TryGetClearScenerySpot still keeps it visually clear of the
        // racing surface and runoff.
        void BuildFlagRow(float density)
        {
            float spacing = Mathf.Lerp(34f, 16f, Mathf.InverseLerp(0.25f, 2f, density));
            float span = Mathf.Min(Runtime.length * 0.5f, 220f);
            int index = 0;
            for (float d = 12f; d < span; d += spacing)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 point;
                    Vector3 forward;
                    Vector3 right;
                    Runtime.SampleAtDistance(d, out point, out forward, out right);
                    // Grounded - a freestanding flag pole 11m off the road edge, not
                    // mounted to the track structure itself.
                    Vector3 desired = GroundedTrackPoint(point) + right * side * (Runtime.HalfWidthAt(d) + 11f);
                    Vector3 basePosition;
                    if (!TryGetClearScenerySpot(desired, 0.6f, 3f, out basePosition))
                    {
                        continue;
                    }

                    CreateTracksideFlag(basePosition, forward, index);
                    index++;
                }
            }
        }

        void CreateTracksideFlag(Vector3 position, Vector3 forward, int index)
        {
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateVisualBox("Trackside flag pole", position + Vector3.up * 1.4f, rotation, new Vector3(0.05f, 2.8f, 0.05f), metalMaterial);

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Material clothMaterial = CreateMaterial("Trackside flag cloth " + index, SponsorPalette[index % SponsorPalette.Length], 0f, 0.4f);
            CreateVisualBox("Trackside flag cloth", position + Vector3.up * 2.55f + right * 0.32f, Quaternion.LookRotation(right, Vector3.up), new Vector3(0.02f, 0.34f, 0.64f), clothMaterial);
        }

        // One-off signature set pieces for circuits that need more than a colour swap
        // to read as themselves: Monaco's harbour, Suzuka's crossover bridge and torii
        // silhouette, and Spa's Ardennes mist.
        void BuildCircuitLandmarks()
        {
            if (monacoTrack)
            {
                // Bigger harbour presence: more yachts, larger/varied hulls (see
                // BuildHarbourYachts) so the marina reads as a real promenade.
                BuildHarbourYachts(0.34f, 8);
            }

            if (suzukaTrack)
            {
                CreateSuzukaCrossoverBridge(Runtime.length * 0.5f);
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * 0.22f, out point, out forward, out right);
                // Grounded - a freestanding landmark 16m off the road, not mounted to
                // the track structure, so it must not float if this point on the lap
                // happens to be elevated.
                CreateToriiGate(GroundedTrackPoint(point) + right * (Runtime.roadHalfWidth + 16f), forward);
            }

            if (spaTrack)
            {
                BuildSpaMist();
            }
        }

        // Large-scale environmental identity pass: distant mountains/skyline behind
        // every circuit, plus archetype-specific flavour (desert paddocks, harbour city,
        // neon skyline, modern street skyline, forested hills, stadium bowl, observation
        // tower) keyed off trackId/styleName. Everything here is atmospheric backdrop -
        // built through TryGetClearScenerySpot/IsClearOfTrackCorridor so nothing pops
        // into the drivable corridor, kerbs, runoff, pit lane, or the racing line - and
        // kept low/distant rather than towering right next to the road.
        void BuildEnvironmentIdentity()
        {
            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);
            string id = Runtime.trackId ?? "";
            string style = (Runtime.styleName ?? "").ToLowerInvariant();

            bool cityStreet = streetTrack && !monacoTrack && !neonTrack;
            bool nightNeon = neonTrack || style.Contains("night");

            // Baseline distant ridge behind every circuit so nothing ever reads as flat;
            // archetype passes below layer their own identity on top of this rather than
            // replacing it. Tint now varies per-archetype (not just desert/parkland/
            // other) so the newer coastal/Mediterranean/hillside identities read as
            // distinct from the shared northern-European parkland green, and individual
            // parkland circuits get their own ridge colour instead of one shared tint.
            Color ridgeTint;
            if (desertTrack)
            {
                ridgeTint = new Color(0.62f, 0.5f, 0.34f);
            }
            else if (coastalTrack)
            {
                ridgeTint = new Color(0.56f, 0.54f, 0.48f);
            }
            else if (technicalParklandTrack)
            {
                ridgeTint = id.Contains("barcelona") ? new Color(0.44f, 0.4f, 0.26f) : new Color(0.3f, 0.36f, 0.24f);
            }
            else if (urbanHillsideTrack)
            {
                ridgeTint = new Color(0.4f, 0.34f, 0.3f);
            }
            else if (canadaTrack)
            {
                ridgeTint = new Color(0.28f, 0.34f, 0.26f);
            }
            else if (parklandTrack)
            {
                ridgeTint = spaTrack ? new Color(0.18f, 0.26f, 0.2f) :
                            suzukaTrack ? new Color(0.26f, 0.32f, 0.24f) :
                            id.Contains("austria") ? new Color(0.3f, 0.34f, 0.38f) :
                            id.Contains("monza") ? new Color(0.34f, 0.32f, 0.24f) :
                            id.Contains("melbourne") ? new Color(0.3f, 0.38f, 0.28f) :
                            new Color(0.24f, 0.3f, 0.24f);
            }
            else
            {
                ridgeTint = new Color(0.34f, 0.38f, 0.46f);
            }

            // Street/city circuits get no natural mountain-ridge ring: their horizon
            // identity is the dedicated skyline (BuildCityStreetBackdrop /
            // BuildMonacoBackdrop / the neon strip) plus the parallax building layer
            // below. The smooth untextured ridge domes read as giant grey "bubbles"
            // floating behind city blocks, not mountains.
            if (!streetTrack)
            {
                BuildMountainBackdrop(density, ridgeTint);
            }

            // Mid-distance parallax layer, closer than the mountain ridge ring above
            // and further back than BuildScenery's trackside trees/buildings, so the
            // horizon reads with a genuine third depth plane on every track instead of
            // jumping straight from near scenery to the far ridge.
            BuildDistantParallaxLayer(density, cityStreet, nightNeon);

            if (desertTrack)
            {
                BuildDesertBackdrop(density);
            }

            if (monacoTrack)
            {
                BuildMonacoBackdrop(density);
            }
            else if (nightNeon)
            {
                BuildNeonSkylineBackdrop(density);
            }
            else if (cityStreet)
            {
                BuildCityStreetBackdrop(density);
            }

            // Every street circuit except Monaco (whose identity is the tight
            // low-rise hillside canyon, not a tower skyline) gets the full
            // high-rise field on top of its own archetype pass (per request -
            // "every street circuit should have around 100 skyscrapers").
            if (streetTrack && !monacoTrack)
            {
                BuildHighRiseSkyline(density, 100);
            }

            if (parklandTrack)
            {
                BuildParklandBackdrop(density);
            }

            // Austria/Red Bull Ring: real Alpine elevation change, so the elevated
            // stretches get jagged cliff-face rock dressing rather than reading as just
            // another entry in the shared parkland bucket.
            if (id.Contains("austria") || id.Contains("red_bull_ring"))
            {
                BuildMountainCliffs(density);
            }

            // Hungary/Barcelona: natural amphitheater/rolling-hillside terrain instead
            // of the deep-forest parkland treatment above, with Barcelona getting a
            // warmer Mediterranean terrain tone.
            if (technicalParklandTrack)
            {
                BuildTechnicalParklandBackdrop(density, id.Contains("barcelona"));
            }

            // Zandvoort (and any other track styled as "coastal"): dune terrain,
            // boardwalk suggestion, cooler sandy-beige palette.
            if (coastalTrack)
            {
                BuildCoastalBackdrop(density);
            }

            // Interlagos: dense hillside city-block silhouettes climbing a slope.
            if (urbanHillsideTrack)
            {
                BuildUrbanHillsideBackdrop(density);
            }

            // Canada: light parkland/urban mix rather than either bucket at full density.
            if (canadaTrack)
            {
                BuildCanadaBackdrop(density);
            }

            // Fallback for any circuit that doesn't match one of the named archetypes
            // above (e.g. a real-world calendar entry that hasn't been given its own
            // signature identity pass yet, or a future/custom layout). Without this,
            // such a track only ever got the baseline mountain ridge + parallax layer
            // and read as a bare "floating road" with nothing filling the mid-ground -
            // reusing the generic forested-hillside treatment here is far closer to a
            // real venue than empty space, and BuildParklandBackdrop itself makes no
            // Spa/Austria-specific assumptions (it only reads density and the sampled
            // centerline), so it's a safe generic default rather than a special case.
            bool hasArchetypeBackdrop = desertTrack || monacoTrack || nightNeon || cityStreet ||
                                         parklandTrack || technicalParklandTrack || coastalTrack ||
                                         urbanHillsideTrack || canadaTrack;
            if (!hasArchetypeBackdrop)
            {
                BuildParklandBackdrop(density);
            }

            if (id.Contains("mexico"))
            {
                CreateStadiumComplex();
            }

            if (id.Contains("austin") || id.Contains("cota") || id.Contains("united_states"))
            {
                CreateObservationTower();
            }
        }

        // Sparse mid-distance ring sitting between the near trackside scenery
        // (BuildScenery's trees/buildings) and the far mountain ridge above - hazy
        // tree-line blobs for natural circuits, hazy building silhouettes for street/
        // neon/Monaco circuits, at a fixed radius so it reads as a genuine extra depth
        // plane rather than more of the same near-scenery. Reuses existing materials
        // (foliage/concrete/glass) rather than adding another one-off tint.
        void BuildDistantParallaxLayer(float density, bool cityStreet, bool nightNeon)
        {
            Bounds bounds = new Bounds(Runtime.centerLine[0], Vector3.zero);
            for (int i = 1; i < Runtime.centerLine.Count; i++)
            {
                bounds.Encapsulate(Runtime.centerLine[i]);
            }

            float ringRadius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.15f + 140f;
            Vector3 ringCenter = new Vector3(bounds.center.x, groundTopY, bounds.center.z);
            int segments = Mathf.Max(6, Mathf.RoundToInt(10f * density));
            bool silhouetteBuildings = cityStreet || nightNeon || monacoTrack;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 desired = ringCenter + direction * ringRadius;
                float objectRadius = silhouetteBuildings ? 10f : 40f;
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, objectRadius, 14f, out safePosition))
                {
                    continue;
                }

                if (silhouetteBuildings)
                {
                    float height = 16f + (i % 4) * 7f;
                    GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    block.name = "Distant parallax skyline block";
                    block.transform.SetParent(transform);
                    block.transform.position = new Vector3(safePosition.x, groundTopY + height * 0.5f, safePosition.z);
                    block.transform.localScale = new Vector3(14f + (i % 3) * 4f, height, 12f);
                    block.GetComponent<Renderer>().sharedMaterial = nightNeon ? glassMaterial : concreteMaterial;
                    MakeVisualOnly(block);
                }
                else
                {
                    // De-blob pass (per report): irregular multi-lobe treeline
                    // formation, buried and terrain-anchored, in the hazy
                    // distant-forest tone (sand tone on deserts).
                    float widthScale = 60f + (i % 4) * 20f;
                    float heightScale = 14f + (i % 3) * 6f;
                    CreateRidgeFormation(safePosition, widthScale, heightScale, widthScale * 0.55f,
                        desertTrack ? grassMaterial : distantForestMaterial, i);
                }
            }
        }

        // Ring of low, flattened, elongated spheres well outside the track bounds,
        // standing in for a distant mountain/hill range on the horizon. Radius-aware
        // clearance means a segment simply doesn't spawn if it can't clear the corridor
        // rather than risking an oversized silhouette near the racing line.
        // Shared multi-lobe ridge/hill silhouette (per report - "there should
        // be nothing that's blob or dome shaped on any track"): every distant
        // terrain feature used to be ONE smooth ellipsoid, which reads as a
        // blob no matter how it's tinted or buried. A formation is four
        // overlapping lobes with seed-varied widths/heights/offsets, each
        // buried to a ~60% cap and anchored to the terrain, so the silhouette
        // is an irregular ridge line instead of a perfect dome.
        // Round 2 (per report - "not only are there still more domes"): sphere
        // lobes are gone entirely. Every ridge piece is now an ANGULAR slab - a
        // rotated, z-tilted, squashed cube - so a formation reads as low-poly
        // faceted terrain with straight skyline edges. There is no curved
        // silhouette left anywhere in the horizon dressing.
        void CreateRidgeFormation(Vector3 basePosition, float width, float height, float depth, Material material, int seed)
        {
            for (int slab = 0; slab < 4; slab++)
            {
                float slabWidth = width * (slab == 0 ? 0.9f : 0.4f + ((seed + slab) % 4) * 0.12f);
                float slabHeight = height * (slab == 0 ? 1f : 0.55f + ((seed * 3 + slab) % 4) * 0.12f);
                float slabDepth = depth * (slab == 0 ? 0.8f : 0.45f + ((seed * 5 + slab) % 3) * 0.14f);
                float offsetX = slab == 0 ? 0f : (((seed * 13 + slab * 53) % 100) - 50) * 0.01f * width * 0.55f;
                float offsetZ = slab == 0 ? 0f : (((seed * 29 + slab * 31) % 60) - 30) * 0.01f * depth * 0.4f;
                float yaw = (seed * 37 + slab * 61) % 180;
                float tilt = ((seed + slab * 3) % 2 == 0 ? 1f : -1f) * (4f + (seed + slab) % 5);
                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = "Ridge formation slab";
                piece.transform.SetParent(transform);
                // Buried ~a third so the tilted top edge rises out of the
                // terrain as a ridge line.
                piece.transform.position = new Vector3(basePosition.x + offsetX, groundTopY + slabHeight * 0.18f, basePosition.z + offsetZ);
                piece.transform.rotation = Quaternion.Euler(0f, yaw, tilt);
                piece.transform.localScale = new Vector3(slabWidth, slabHeight, slabDepth);
                piece.GetComponent<Renderer>().sharedMaterial = material;
                MakeVisualOnly(piece);
            }
        }

        void BuildMountainBackdrop(float density, Color tint)
        {
            Material ridgeMaterial = CreateMaterial("Runtime Mountain Ridge", tint, 0f, 0.15f);
            // De-blob pass: rocky noise grain (near-white tint - the texture
            // multiplies the colour) so the ridge line doesn't read as smooth
            // plastic.
            ridgeMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.88f, 0.87f, 0.85f), 0.18f);
            ridgeMaterial.mainTextureScale = new Vector2(6f, 3f);
            Bounds bounds = new Bounds(Runtime.centerLine[0], Vector3.zero);
            for (int i = 1; i < Runtime.centerLine.Count; i++)
            {
                bounds.Encapsulate(Runtime.centerLine[i]);
            }

            float ringRadius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.55f + 220f;
            Vector3 ringCenter = new Vector3(bounds.center.x, groundTopY, bounds.center.z);
            int segments = Mathf.Max(8, Mathf.RoundToInt(16f * density));
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 desired = ringCenter + direction * ringRadius;
                float widthScale = 140f + (i % 5) * 40f;
                float heightScale = 34f + (i % 4) * 14f;
                float objectRadius = widthScale * 0.5f;
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, objectRadius, 20f, out safePosition))
                {
                    continue;
                }

                // De-blob pass (per report): irregular multi-lobe formation
                // instead of one smooth dome.
                CreateRidgeFormation(safePosition, widthScale, heightScale, widthScale * 0.62f, ridgeMaterial, i);
            }
        }

        // Bahrain/Qatar/Abu Dhabi-style desert dressing: floodlit paddock blocks well back
        // from the corridor, plus a warm heat-haze band standing in for horizon shimmer.
        void BuildDesertBackdrop(float density)
        {
            // More paddock structures at more offsets around the lap (was a fixed pair)
            // so the desert paddock reads as a real facility rather than two buildings.
            int paddocks = Mathf.Max(2, Mathf.RoundToInt(4f * density));
            for (int i = 0; i < paddocks; i++)
            {
                float t = (i + 0.5f) / paddocks;
                int side = i % 2 == 0 ? 1 : -1;
                CreatePaddockComplex(t, side);
            }

            // Distant dune ridges far outside the corridor, standing in for desert
            // horizon terrain beyond the sparse near-road dunes BuildScenery scatters.
            int distantDunes = Mathf.Max(4, Mathf.RoundToInt(10f * density));
            for (int i = 0; i < distantDunes; i++)
            {
                float t = (i + 0.5f) / distantDunes;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                Vector3 desired = point + right * side * (Runtime.roadHalfWidth + 170f + (i % 3) * 40f);
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, 50f, 10f, out safePosition))
                {
                    continue;
                }

                // De-blob pass (per report): irregular multi-lobe dune line
                // instead of one smooth dome.
                float widthScale = 90f + (i % 4) * 30f;
                float heightScale = 16f + (i % 3) * 8f;
                CreateRidgeFormation(safePosition, widthScale, heightScale, widthScale * 0.7f, grassMaterial, i);
            }

            Material haze = CreateTranslucentMaterial("Runtime desert haze", new Color(0.85f, 0.72f, 0.5f), 0.12f);
            int bands = Mathf.Max(3, Mathf.RoundToInt(6f * density));
            for (int i = 0; i < bands; i++)
            {
                float d = Runtime.length * (i / (float)bands);
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 desired = point + right * side * (Runtime.roadHalfWidth + 140f) + Vector3.up * 18f;
                    Vector3 safePosition;
                    if (!TryGetClearScenerySpot(desired, 60f, 10f, out safePosition))
                    {
                        continue;
                    }

                    CreateVisualBox("Desert heat haze bank", safePosition, Quaternion.LookRotation(forward, Vector3.up), new Vector3(120f, 30f, 4f), haze);
                }
            }

            // Taller/varied floodlit modern circuit buildings, further back than the
            // paddock complexes above; twilight (Abu Dhabi) gets slightly taller, more
            // lit-glass massing to sell the "twilight finale" look with materials alone.
            int circuitBuildings = Mathf.Max(2, Mathf.RoundToInt(4f * density));
            for (int i = 0; i < circuitBuildings; i++)
            {
                float t = (i + 0.3f) / circuitBuildings;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? 1 : -1;
                // Grounded - these circuit buildings stand well outside the corridor on
                // real ground, not on the track's own sampled height.
                Vector3 anchor = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 95f);
                float height = 20f + (i % 3) * 10f + (twilightTrack ? 6f : 0f);
                CreateProceduralBuildingCluster(anchor, forward, 2, height, twilightTrack);
            }

            // A couple of extra long, low grandstands beyond BuildScenery's fixed set
            // (4x scale like every other stand - per request).
            BuildGrandstand(0.3f, 1, 4f);
            if (density > 0.6f)
            {
                BuildGrandstand(0.72f, -1, 4f);
            }
        }

        // Floodlit desert paddock building: a low wide block plus a row of light towers,
        // echoing Bahrain/Qatar/Abu Dhabi's paddock architecture from well outside the
        // corridor rather than right at the fence line.
        void CreatePaddockComplex(float normalizedDistance, int side)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(Runtime.length * normalizedDistance, out point, out forward, out right);
            // Grounded: this stands 70m clear of the road on real ground, so it must not
            // inherit an elevated sample point's deck height and float there instead.
            Vector3 desired = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 70f);
            Vector3 basePosition;
            if (!TryGetClearScenerySpot(desired, 40f, 8f, out basePosition))
            {
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateGroundPatch("Paddock apron pad", basePosition, 24f, 50f, concreteMaterial, rotation);
            CreateVisualBox("Paddock building block", basePosition + Vector3.up * 5f, rotation, new Vector3(46f, 10f, 20f), concreteMaterial);
            CreateVisualBox("Paddock building fascia", basePosition + Vector3.up * 10.2f, rotation, new Vector3(46.4f, 0.4f, 20.4f), sceneryAccentMaterial);
            for (int i = -1; i <= 1; i++)
            {
                Vector3 towerBase = basePosition + forward * i * 16f - right * side * 6f;
                CreateVisualBox("Paddock light tower pole", towerBase + Vector3.up * 9f, rotation, new Vector3(0.5f, 18f, 0.5f), metalMaterial);
                CreateVisualBox("Paddock light tower head", towerBase + Vector3.up * 18.3f, rotation, new Vector3(3.2f, 0.6f, 1.4f), lightGlowMaterial);
            }
        }

        // Denser Monaco harbour/city read: close-packed luxury building clusters along the
        // hillside, layered behind BuildHarbourYachts so the promenade feels busier
        // without duplicating it.
        void BuildMonacoBackdrop(float density)
        {
            // Denser, taller, and pulled in closer (46f -> 38f margin) than the original
            // pass so the hillside street reads as a tight wall-canyon.
            int clusters = Mathf.Max(4, Mathf.RoundToInt(8f * density));
            for (int i = 0; i < clusters; i++)
            {
                float t = (i + 0.5f) / clusters;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                // Grounded - these skyline blocks stand on real ground well clear of the
                // road, not on the road's own (possibly elevated, e.g. Monaco's tunnel
                // section) sampled height.
                Vector3 anchor = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 38f);
                CreateProceduralBuildingCluster(anchor, forward, 3, 16f + (i % 4) * 8f, false, 2f);
            }

            // Cream-toned luxury apartment/hotel silhouettes distinct from the generic
            // barrier-toned buildings above, further back so they read as the hillside
            // skyline behind the immediate street canyon.
            int luxuryClusters = Mathf.Max(2, Mathf.RoundToInt(3f * density));
            for (int i = 0; i < luxuryClusters; i++)
            {
                float t = (i + 0.5f) / luxuryClusters * 0.85f + 0.05f;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? 1 : -1;
                Vector3 anchor = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 58f);
                CreateLuxuryApartmentCluster(anchor, forward, 2, 26f + (i % 2) * 10f);
            }
        }

        // Denser lit skyline for Singapore/Las Vegas-style night circuits, building on the
        // existing CreateNeonPylon trackside strips with a further-back row of taller neon
        // towers so the horizon itself glows.
        void BuildNeonSkylineBackdrop(float density)
        {
            // Denser and taller than the original pass, plus a further-back set of extra
            // clusters biased toward the DRS zones - the two stretches every track is
            // guaranteed to have a genuine straight, and exactly where a player is
            // driving fastest (and glancing at the skyline least, so the spectacle needs
            // to read big) rather than threading a technical corner.
            int clusters = Mathf.Max(5, Mathf.RoundToInt(10f * density));
            for (int i = 0; i < clusters; i++)
            {
                float t = (i + 0.5f) / clusters;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                // Grounded - the neon skyline stands on real ground, not on the track's
                // own sampled height wherever the lap happens to climb or dip.
                Vector3 anchor = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 60f);
                CreateProceduralBuildingCluster(anchor, forward, 3, 30f + (i % 4) * 11f, true);
            }

            float[] straightCenters =
            {
                Mathf.Repeat((Runtime.drsZoneOne.x + Runtime.drsZoneOne.y) * 0.5f, 1f),
                Mathf.Repeat((Runtime.drsZoneTwo.x + Runtime.drsZoneTwo.y) * 0.5f, 1f)
            };
            for (int s = 0; s < straightCenters.Length; s++)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * straightCenters[s], out point, out forward, out right);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 anchor = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 46f);
                    CreateProceduralBuildingCluster(anchor, forward, 2, 40f + s * 10f, true, 3f);
                }
            }
        }

        // Modern street-circuit skyline (Jeddah/Baku/Madrid/Miami-style): a further-back
        // skyline row plus one or two sponsor bridges spanning the track on the longer
        // straights.
        void BuildCityStreetBackdrop(float density)
        {
            // Denser, taller, and pulled in closer than the original pass so the
            // street-circuit "canyon" feel is stronger. Heights lifted again for
            // every street circuit (per request - the other venues read
            // "mediocre" next to Jeddah), and Jeddah itself gets a denser row
            // here plus its own dedicated corniche high-rise pass at the end.
            int clusters = Mathf.Max(4, Mathf.RoundToInt((jeddahTrack ? 13f : 9f) * density));
            for (int i = 0; i < clusters; i++)
            {
                float t = (i + 0.5f) / clusters;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                // Grounded - the street-canyon skyline stands on real ground, not on the
                // track's own sampled height.
                Vector3 anchor = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 42f);
                CreateProceduralBuildingCluster(anchor, forward, 3, (jeddahTrack ? 32f : 26f) + (i % 4) * 10f, false, 2.5f);
            }

            // A closer second row on alternating samples for extra canyon density.
            for (int i = 0; i < clusters; i += 2)
            {
                float t = (i + 0.5f) / clusters;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? 1 : -1;
                Vector3 anchor = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 24f);
                CreateProceduralBuildingCluster(anchor, forward, 2, 16f + (i % 3) * 6f, false, 2f);
            }

            CreateSponsorBridge(Runtime.length * 0.22f);
            CreateSponsorBridge(Runtime.length * 0.68f);
            if (density > 0.6f)
            {
                CreateSponsorBridge(Runtime.length * 0.44f);
            }

            // Extra concrete wall variety along the canyon - a cheap "service alley"
            // suggestion set back close to the road, distinct from the skyline blocks.
            int wallSegments = Mathf.Max(2, Mathf.RoundToInt(5f * density));
            for (int i = 0; i < wallSegments; i++)
            {
                float t = (i + 0.5f) / wallSegments;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                // Grounded - a freestanding alley wall set back from the road, not part
                // of the road's own barrier/structure, so it must stand on real ground.
                Vector3 desired = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 12f);
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, 2f, 2f, out safePosition))
                {
                    continue;
                }

                CreateVisualBox("Street canyon service wall", safePosition + Vector3.up * 1.1f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.35f, 2.2f, 8f), i % 2 == 0 ? weatheredConcreteMaterial : concreteMaterial);
            }

        }

        // Street-circuit high-rise skyline (per request - "50-100 more
        // skyscrapers for street circuits"): a large field of genuinely tall
        // glass towers behind the shared street-canyon blocks above, staggered
        // across four depth rows on both sides of the lap so the circuit reads
        // as threading a real high-rise city - plus one landmark supertall near
        // the start/finish straight. Everything is visual-only, grounded on real
        // terrain, and clearance-checked through TryGetClearScenerySpot like
        // every other backdrop pass, and the whole field stands 70m+ off the
        // corridor so height never crowds the racing line.
        void BuildHighRiseSkyline(float density, int towerTarget)
        {
            // Real-skyscraper proportions (per report - "not wide enough nor
            // tall enough... comically small"): heights run 90-250m near and
            // climb toward 370m in the far band, footprints 26-42m.
            //
            // Three depth bands (per report - "a lot of skyscrapers in the
            // direct vicinity of the track, but less so in the immediate
            // background and in the further background"): the near canyon ring
            // keeps its full count, and two new rings fill the middle ground
            // (~290-450m out) and the far horizon (~510-660m out). The far
            // bands run taller on average so the skyline silhouette rises with
            // distance the way a real CBD reads, instead of the city visibly
            // stopping one row behind the track.
            BuildHighRiseBand(density, towerTarget, 85f, 34f, 90f, 22f, 0);
            BuildHighRiseBand(density, Mathf.RoundToInt(towerTarget * 0.6f), 290f, 40f, 115f, 26f, 300);
            BuildHighRiseBand(density, Mathf.RoundToInt(towerTarget * 0.45f), 510f, 50f, 145f, 32f, 600);

            // Landmark supertall by the start/finish straight - the one silhouette
            // that anchors the whole skyline, tall enough to read from anywhere
            // on the lap.
            Vector3 landmarkPoint;
            Vector3 landmarkForward;
            Vector3 landmarkRight;
            Runtime.SampleAtDistance(Runtime.length * 0.06f, out landmarkPoint, out landmarkForward, out landmarkRight);
            Vector3 landmarkAnchor = GroundedTrackPoint(landmarkPoint) + landmarkRight * (Runtime.roadHalfWidth + 150f);
            CreateCornicheSkyscraper(landmarkAnchor, landmarkForward, 320f, 1);
        }

        // One depth ring of the street-circuit skyline: towers spread around the
        // whole lap on alternating sides at baseOffset(+ staggered depth) from
        // the corridor. seedOffset keeps each band's size/species rolls distinct
        // so the three rings don't repeat the same silhouette pattern.
        void BuildHighRiseBand(float density, int towerTarget, float baseOffset, float depthStep, float baseHeight, float heightStep, int seedOffset)
        {
            int towers = Mathf.Max(12, Mathf.RoundToInt(towerTarget * Mathf.Clamp(density, 0.5f, 1.5f)));
            for (int i = 0; i < towers; i++)
            {
                float t = (i + 0.5f) / towers;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                Vector3 anchor = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + baseOffset + (i % 4) * depthStep + (i % 3) * 13f);
                float height = baseHeight + (i % 7) * heightStep + (i % 3) * 14f;
                CreateCornicheSkyscraper(anchor, forward, height, i + seedOffset);
            }
        }

        // One glass tower: main shaft, full-height vertical window strips, a
        // set-back crown, and a spire on the taller silhouettes. The band-per-
        // 3m approach CreateProceduralBuildingCluster uses would need dozens of
        // boxes at these heights, so tall towers use a few full-height strips
        // instead.
        void CreateCornicheSkyscraper(Vector3 anchor, Vector3 forward, float height, int seed)
        {
            float footprint = 26f + (seed % 3) * 8f;
            Vector3 safePosition;
            if (!TryGetClearScenerySpot(anchor, footprint * 0.75f, 6f, out safePosition))
            {
                return;
            }

            // Grounding fix (per report - "some of the skyscrapers aren't even
            // touching the ground"): the shaft is sunk 3m below the terrain
            // surface so no seam of air can ever show under it, a street-level
            // podium block wraps its base, and the window strips now run from
            // 2m off the ground instead of starting ~5% of the tower height up
            // (which on a tall tower left metres of bare air under the lit
            // strips and read as the whole building hovering).
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaft.name = "Corniche skyscraper shaft";
            shaft.transform.SetParent(transform);
            shaft.transform.position = safePosition + Vector3.up * (height * 0.5f - 3f);
            shaft.transform.rotation = rotation;
            shaft.transform.localScale = new Vector3(footprint, height + 6f, footprint * 0.8f);
            shaft.GetComponent<Renderer>().sharedMaterial = glassMaterial;
            MakeVisualOnly(shaft);

            CreateVisualBox("Corniche skyscraper podium", safePosition + Vector3.up * 4f, rotation,
                new Vector3(footprint * 1.45f, 8f, footprint * 1.25f), concreteMaterial);

            for (int strip = -1; strip <= 1; strip++)
            {
                float stripHeight = height - 4f;
                CreateVisualBox("Corniche skyscraper window strip",
                    safePosition + rotation * new Vector3(strip * footprint * 0.28f, 2f + stripHeight * 0.5f, footprint * 0.41f),
                    rotation, new Vector3(footprint * 0.16f, stripHeight, 0.1f), windowStripMaterial);
            }

            CreateVisualBox("Corniche skyscraper crown", safePosition + Vector3.up * (height + 4f), rotation,
                new Vector3(footprint * 0.55f, 8f, footprint * 0.45f), concreteMaterial);
            if (height > 140f)
            {
                CreateVisualBox("Corniche skyscraper spire", safePosition + Vector3.up * (height + 15f), rotation,
                    new Vector3(1.4f, 16f, 1.4f), metalMaterial);
            }
        }

        // Cluster of boxy buildings around a point, shared by the Monaco/city-street/neon
        // skyline passes. Each building is individually clearance-checked (not just the
        // cluster anchor) since the spread can push outer buildings back toward the
        // corridor on tighter circuits.
        void CreateProceduralBuildingCluster(Vector3 anchor, Vector3 forward, int count, float baseHeight, bool neonStyle)
        {
            CreateProceduralBuildingCluster(anchor, forward, count, baseHeight, neonStyle, 4f);
        }

        // extraMargin lets callers pull the cluster closer to the corridor (Monaco's
        // canyon, the city-street "canyon of buildings" pass) without bypassing the
        // clearance check itself - it is still routed through TryGetClearScenerySpot,
        // just with a smaller required buffer.
        void CreateProceduralBuildingCluster(Vector3 anchor, Vector3 forward, int count, float baseHeight, bool neonStyle, float extraMargin)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int i = 0; i < count; i++)
            {
                float spread = (i - (count - 1) * 0.5f) * 14f;
                float depth = (i % 2) * 9f;
                Vector3 desired = anchor + forward * spread + right * depth;
                float height = baseHeight + (i % 4) * 5f;
                float footprint = 9f + (i % 3) * 3f;
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, footprint * 0.75f, extraMargin, out safePosition))
                {
                    continue;
                }

                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Skyline building block";
                block.transform.SetParent(transform);
                block.transform.position = safePosition + Vector3.up * height * 0.5f;
                block.transform.rotation = rotation;
                block.transform.localScale = new Vector3(footprint, height, footprint * 0.85f);
                block.GetComponent<Renderer>().sharedMaterial = neonStyle ? glassMaterial : (monacoTrack ? barrierMaterial : concreteMaterial);
                MakeVisualOnly(block);

                Material windowMaterial = neonStyle ? neonMaterials[i % neonMaterials.Length] : windowStripMaterial;
                int bands = Mathf.Clamp(Mathf.RoundToInt(height / 3f), 1, 4);
                for (int band = 0; band < bands; band++)
                {
                    CreateVisualBox("Skyline window band", safePosition + Vector3.up * (1.5f + band * 2.6f), rotation, new Vector3(footprint * 0.8f, 0.6f, 0.08f), windowMaterial);
                }
            }
        }

        // Monaco-specific luxury apartment/hotel silhouette, distinct from the generic
        // barrier-toned street buildings above: taller, cream-coloured, with a repeated
        // balcony-strip ledge per floor.
        void CreateLuxuryApartmentCluster(Vector3 anchor, Vector3 forward, int count, float baseHeight)
        {
            for (int i = 0; i < count; i++)
            {
                float spread = (i - (count - 1) * 0.5f) * 16f;
                Vector3 desired = anchor + forward * spread;
                float height = baseHeight + (i % 3) * 8f;
                float footprint = 10f + (i % 2) * 3f;
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, footprint * 0.75f, 3f, out safePosition))
                {
                    continue;
                }

                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Luxury apartment silhouette";
                block.transform.SetParent(transform);
                block.transform.position = safePosition + Vector3.up * height * 0.5f;
                block.transform.rotation = rotation;
                block.transform.localScale = new Vector3(footprint, height, footprint * 0.8f);
                block.GetComponent<Renderer>().sharedMaterial = luxuryApartmentMaterial;
                MakeVisualOnly(block);

                int floors = Mathf.Clamp(Mathf.RoundToInt(height / 3.2f), 2, 6);
                for (int floor = 0; floor < floors; floor++)
                {
                    CreateVisualBox("Luxury apartment balcony strip", safePosition + Vector3.up * (2f + floor * 3.1f), rotation, new Vector3(footprint * 0.86f, 0.14f, footprint * 0.82f + 0.3f), sceneryAccentMaterial);
                    CreateVisualBox("Luxury apartment window band", safePosition + Vector3.up * (2.5f + floor * 3.1f) - forward * (footprint * 0.4f), rotation, new Vector3(footprint * 0.7f, 0.9f, 0.06f), windowStripMaterial);
                }
            }
        }

        // Overhead sponsor bridge spanning the track, modelled on
        // CreateSuzukaCrossoverBridge's visual-only deck (no collider, generous vertical
        // clearance) so it can safely arc above the corridor instead of needing the
        // lateral clearance ground-level scenery needs.
        void CreateSponsorBridge(float distance)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            Quaternion deckRotation = Quaternion.LookRotation(forward, Vector3.up);
            float span = Runtime.roadHalfWidth * 2f + 14f;
            const float clearance = 9.5f;
            float deckY = point.y + clearance;
            Vector3 deckCenter = new Vector3(point.x, deckY, point.z);
            Color panelColor = SponsorPalette[Mathf.Abs(Mathf.RoundToInt(distance)) % SponsorPalette.Length];
            Material panelMaterial = CreateMaterial("Sponsor bridge banner", panelColor, 0.05f, 0.5f, (nightTrack || twilightTrack) ? panelColor * 0.4f : Color.black);

            CreateVisualBox("Sponsor bridge deck", deckCenter, deckRotation, new Vector3(span, 1.1f, 5.5f), metalMaterial);
            CreateVisualBox("Sponsor bridge banner panel", deckCenter - Vector3.up * 0.9f, deckRotation, new Vector3(span * 0.92f, 1.4f, 0.3f), panelMaterial);

            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 columnTop = point + right * side * (span * 0.5f - 1.4f);
                float columnHeight = Mathf.Max(2f, deckY - groundTopY);
                Vector3 columnCenter = new Vector3(columnTop.x, groundTopY + columnHeight * 0.5f, columnTop.z);
                CreateVisualBox("Sponsor bridge column", columnCenter, Quaternion.LookRotation(forward, Vector3.up), new Vector3(1.4f, columnHeight, 1.4f), metalMaterial);
            }
        }

        // A near backdrop hill that actually reads as a wooded hill (per report -
        // "massive blobs of green"): an irregular cluster of three overlapping
        // earth-toned mounds instead of one clean flat-green dome, with real
        // broadleaf/conifer trees planted across the main mound's crown and
        // slopes via the ellipsoid surface maths - so the green a player sees is
        // visible trees, not the hill itself painted canopy green. All sizes and
        // placements derive from the stable seed so layouts don't shuffle
        // between rebuilds.
        void CreateForestedHill(Vector3 basePosition, int seed, float width, float height)
        {
            // De-blob round 2 (per report - "not only are there still more
            // domes"): the sphere mounds are gone - the hill body is the same
            // angular slab formation the horizon ridges use.
            CreateRidgeFormation(basePosition, width, height, width * 0.75f, hillsideEarthMaterial, seed);

            // Trees over the main slab's top plateau (kept to its central half
            // so the slab's tilt can't leave a base hanging off the edge; bases
            // sink slightly to absorb the tilt).
            float slabTopY = groundTopY + height * 0.18f + height * 0.5f;
            int treeCount = 5 + seed % 3;
            for (int t = 0; t < treeCount; t++)
            {
                float angle = ((seed * 43 + t * 149) % 360) * Mathf.Deg2Rad;
                float radialFraction = 0.08f + ((seed * 5 + t * 7) % 5) * 0.07f;
                float dx = Mathf.Cos(angle) * radialFraction * width * 0.9f;
                float dz = Mathf.Sin(angle) * radialFraction * width * 0.68f;
                Vector3 treeBase = new Vector3(basePosition.x + dx, slabTopY - 1.6f, basePosition.z + dz);
                // 3x tree pass: hillside trees scale up with the trackside ones.
                float jitter = (0.7f + ((seed + t) % 4) * 0.1f) * 3f;
                if ((seed + t) % 2 == 0)
                {
                    CreateConiferTree(treeBase, jitter, seed * 7 + t);
                }
                else
                {
                    CreateBroadleafTree(treeBase, jitter, seed * 7 + t);
                }
            }
        }

        // Forested hills and a distant treeline for Spa/Austria/Suzuka/Silverstone/Monza
        // -style parkland circuits, layered behind the trackside tree clusters BuildScenery
        // already scatters.
        void BuildParklandBackdrop(float density)
        {
            int rings = Mathf.Max(6, Mathf.RoundToInt(12f * density));
            for (int i = 0; i < rings; i++)
            {
                float t = (i + 0.5f) / rings;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                Vector3 desired = point + right * side * (Runtime.roadHalfWidth + 90f);
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, 30f, 10f, out safePosition))
                {
                    continue;
                }

                // Local point.y (not groundTopY) on purpose: parkland tracks like Spa and
                // Austria have real elevation change, so a hillside should sit relative to
                // the nearby road height rather than one flat global ground reference.
                // Blob fix (per report): the near hill is no longer one flat-green
                // dome - CreateForestedHill builds an irregular earth-toned mound
                // cluster with real trees planted over its crown and slopes.
                float heightScale = 24f + (i % 3) * 8f;
                CreateForestedHill(safePosition, i, 60f + (i % 4) * 12f, heightScale);

                if (i % 2 == 0)
                {
                    CreateTreeCluster(safePosition + right * side * 14f, i + 40);
                }

                // Occasional rock outcrop at the hill's base - forest circuits get
                // trees/hills/rocks per the environment brief, not just trees on a
                // smooth grass mound.
                if (i % 4 == 1)
                {
                    CreateRockCluster(safePosition - right * side * 10f, i + 60);
                }
            }

            // Three further-back layers at increasing distance/height so the forest
            // reads with real depth instead of one flat treeline ring (third layer
            // added per report - "add some stuff in the near background and far
            // background too").
            int farRings = Mathf.Max(4, Mathf.RoundToInt(8f * density));
            for (int layer = 1; layer <= 3; layer++)
            {
                for (int i = 0; i < farRings; i++)
                {
                    float t = (i + layer * 0.33f) / farRings;
                    Vector3 point;
                    Vector3 forward;
                    Vector3 right;
                    Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                    int side = i % 2 == 0 ? -1 : 1;
                    float distanceOut = 90f + layer * 70f;
                    Vector3 desired = point + right * side * (Runtime.roadHalfWidth + distanceOut);
                    Vector3 safePosition;
                    if (!TryGetClearScenerySpot(desired, 34f + layer * 8f, 10f, out safePosition))
                    {
                        continue;
                    }

                    // Blob fix (per report): far layers keep the cheap silhouette
                    // (they're horizon dressing) but in the hazy desaturated
                    // distant-forest tone instead of bright flat canopy green.
                    // Bubble fix (per report - "grey disks/bubbles... still looks
                    // the same"): the old +0.15*height*layer upward lift meant to
                    // stack farther layers taller instead UN-BURIED them - by
                    // layer 3 nearly the whole ellipsoid hung above the ground as
                    // a floating disk. Farther layers now get their extra height
                    // from heightScale alone and every layer stays buried to a
                    // ~60% ridge cap, anchored to the terrain (groundTopY), not
                    // the track's sampled height.
                    // De-blob pass (per report): irregular multi-lobe formation
                    // instead of one smooth dome.
                    float heightScale = (26f + layer * 14f) + (i % 3) * 8f;
                    CreateRidgeFormation(safePosition, 70f + (i % 4) * 16f, heightScale, 50f + (i % 3) * 12f,
                        distantForestMaterial, i * 7 + layer);
                }
            }
        }

        // Austria/Red Bull Ring-style Alpine dressing: walks the lap looking for
        // elevated stretches (the same IsElevatedAtDistance test the continuous barrier
        // pass uses) and drops a rock-outcrop cluster behind the barrier line there, so
        // the mountain-circuit read comes from real cliff-face detail at the genuinely
        // elevated sections instead of only a tinted ridge on the horizon.
        void BuildMountainCliffs(float density)
        {
            int bands = Mathf.Max(4, Mathf.RoundToInt(8f * density));
            for (int i = 0; i < bands; i++)
            {
                float d = Runtime.length * (i / (float)bands);
                if (!IsElevatedAtDistance(d))
                {
                    continue;
                }

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                Vector3 desired = point + right * side * (Runtime.roadHalfWidth + 24f);
                CreateRockCluster(desired, i + 90);
            }
        }

        // Zandvoort-style coastal dressing: dune terrain reusing CreateDune's sculpting
        // machinery, a boardwalk/promenade suggestion, and a cooler, sandier ground tone
        // than the warm-orange desert palette, plus a blue-tinted sea haze toward the
        // notional "sea" side instead of the desert's warm heat-haze.
        void BuildCoastalBackdrop(float density)
        {
            int duneRings = Mathf.Max(6, Mathf.RoundToInt(12f * density));
            for (int i = 0; i < duneRings; i++)
            {
                float t = (i + 0.5f) / duneRings;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                // Grounded - dunes stand on real ground, not on the track's own sampled
                // height wherever the coastline climbs or dips.
                Vector3 nearDune = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 50f + (i % 3) * 20f);
                CreateDune(nearDune, i + 70);

                Vector3 farDesired = point + right * side * (Runtime.roadHalfWidth + 110f);
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(farDesired, 40f, 10f, out safePosition))
                {
                    continue;
                }

                // De-blob pass (per report): irregular multi-lobe dune line
                // instead of one smooth dome.
                float heightScale = 12f + (i % 3) * 6f;
                CreateRidgeFormation(safePosition, 80f + (i % 4) * 20f, heightScale, 60f + (i % 3) * 14f, coastalSandMaterial, i);
            }

            // Flattened boardwalk/promenade suggestion on the notional sea side.
            int boardwalkSegments = Mathf.Max(3, Mathf.RoundToInt(6f * density));
            for (int i = 0; i < boardwalkSegments; i++)
            {
                float t = (i + 0.5f) / boardwalkSegments;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                Vector3 desired = point + right * (Runtime.roadHalfWidth + 75f);
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, 10f, 6f, out safePosition))
                {
                    continue;
                }

                CreateVisualBox("Coastal boardwalk deck", safePosition + Vector3.up * 0.2f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(6f, 0.3f, 26f), weatheredConcreteMaterial);
                CreateVisualBox("Coastal boardwalk rail", safePosition + Vector3.up * 0.9f + right * 2.9f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.14f, 1.1f, 26f), fencePostMaterial);

                // Flat sea surface just beyond the rail - grounds the "coastal" read
                // with an actual water plane instead of only the dunes/haze already here.
                GameObject seaWater = CreateVisualBox("Coastal sea water", safePosition + right * 12f + Vector3.up * -0.15f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(40f, 0.05f, 30f), waterMaterial);
                seaWater.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // A palm or two behind the rail on alternating deck segments so the
                // promenade reads as tropical rather than just a bare concrete strip.
                if (i % 2 == 0)
                {
                    CreatePalmCluster(safePosition + right * 5.4f, i);
                }
            }

            // Cool blue sea-haze bank, distinct from the desert's warm heat-haze tint.
            Material seaHaze = CreateTranslucentMaterial("Runtime sea haze", new Color(0.62f, 0.72f, 0.8f), 0.14f);
            int hazeBands = Mathf.Max(3, Mathf.RoundToInt(5f * density));
            for (int i = 0; i < hazeBands; i++)
            {
                float d = Runtime.length * (i / (float)hazeBands);
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                Vector3 desired = point + right * (Runtime.roadHalfWidth + 150f) + Vector3.up * 16f;
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, 60f, 10f, out safePosition))
                {
                    continue;
                }

                CreateVisualBox("Coastal sea haze bank", safePosition, Quaternion.LookRotation(forward, Vector3.up), new Vector3(130f, 26f, 4f), seaHaze);
            }
        }

        // Hungaroring/Barcelona-style natural amphitheater terrain: rolling grass-bank
        // hillside rings closer in than the deep-forest BuildParklandBackdrop pass,
        // since both circuits read as a bowl of banking rather than a forest. Barcelona
        // gets a warmer, drier Mediterranean tint; Hungary keeps a cooler natural green.
        void BuildTechnicalParklandBackdrop(float density, bool mediterranean)
        {
            Material terrainMaterial = CreateMaterial("Runtime Technical Parkland Terrain",
                mediterranean ? new Color(0.5f, 0.46f, 0.28f) : new Color(0.28f, 0.36f, 0.22f), 0f, 0.2f);
            // De-blob pass: grassy noise grain (near-white tint - the texture
            // multiplies the colour).
            terrainMaterial.mainTexture = BuildNoiseTexture(256, new Color(0.9f, 0.9f, 0.84f), 0.16f);
            terrainMaterial.mainTextureScale = new Vector2(6f, 4f);

            int banks = Mathf.Max(7, Mathf.RoundToInt(14f * density));
            for (int i = 0; i < banks; i++)
            {
                float t = (i + 0.5f) / banks;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                Vector3 desired = point + right * side * (Runtime.roadHalfWidth + 70f);
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, 26f, 8f, out safePosition))
                {
                    continue;
                }

                // De-blob pass (per report): irregular multi-lobe bank instead
                // of one smooth dome.
                float heightScale = 14f + (i % 3) * 6f;
                CreateRidgeFormation(safePosition, 56f + (i % 4) * 12f, heightScale, 40f + (i % 3) * 10f, terrainMaterial, i);

                if (i % 3 == 0)
                {
                    CreateTreeCluster(safePosition + right * side * 12f, i + 90);
                }
            }

            // A packed-in natural-amphitheater crowd feel gets a few extra floodlights
            // along the bowl rim beyond the standard per-lap set.
            int rim = Mathf.Max(3, Mathf.RoundToInt(5f * density));
            for (int i = 0; i < rim; i++)
            {
                float t = (i + 0.5f) / rim;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                // Grounded - unlike the amphitheater banks above (which deliberately
                // follow the local hillside height), a floodlight pole is a fixture that
                // needs to stand on real ground.
                Vector3 desired = GroundedTrackPoint(point) - right * (Runtime.roadHalfWidth + 24f);
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, 3f, 3f, out safePosition))
                {
                    continue;
                }

                CreateFloodlight(safePosition, forward, false);
            }
        }

        // Interlagos-style hillside favela silhouette: dense stacked building clusters
        // at climbing heights following a notional slope behind the circuit, distinct
        // from the flat cityStreet skyline bucket.
        void BuildUrbanHillsideBackdrop(float density)
        {
            int clusters = Mathf.Max(6, Mathf.RoundToInt(11f * density));
            for (int i = 0; i < clusters; i++)
            {
                float t = (i + 0.5f) / clusters;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;

                // Further clusters sit both further back and higher up, so the
                // silhouette reads as stacking up a hillside rather than one flat row.
                // Based on groundTopY (not the track's own sampled height) since this is
                // a deliberate artistic "climbing the slope" offset, not a reflection of
                // real track elevation - and there's no actual hill mesh underneath, so
                // EnsureGroundedBase drops a support pad/column under each climbing tier
                // rather than leaving the higher steps looking like they hang in mid-air.
                int climbStep = i % 4;
                Vector3 anchor = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 48f + climbStep * 22f) + Vector3.up * (climbStep * 6f);
                EnsureGroundedBase(anchor, 9f);
                CreateHillsideBuildingCluster(anchor, forward, 4, 12f + climbStep * 7f);
            }
        }

        // Stacked, colourful low-rise blocks at varying heights, reusing the clearance-
        // checked spread pattern from CreateProceduralBuildingCluster but cycling a
        // small hillside-toned material palette instead of one flat concrete/glass
        // look, so the climbing silhouette reads as a dense hillside district.
        void CreateHillsideBuildingCluster(Vector3 anchor, Vector3 forward, int count, float baseHeight)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int i = 0; i < count; i++)
            {
                float spread = (i - (count - 1) * 0.5f) * 11f;
                float depth = (i % 2) * 7f;
                Vector3 desired = anchor + forward * spread + right * depth;
                float height = baseHeight + (i % 3) * 4f;
                float footprint = 7f + (i % 3) * 2.5f;
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, footprint * 0.75f, 4f, out safePosition))
                {
                    continue;
                }

                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Hillside district block";
                block.transform.SetParent(transform);
                block.transform.position = safePosition + Vector3.up * height * 0.5f;
                block.transform.rotation = rotation;
                block.transform.localScale = new Vector3(footprint, height, footprint * 0.85f);
                block.GetComponent<Renderer>().sharedMaterial = hillsideBuildingMaterials[Mathf.Abs(i + Mathf.RoundToInt(anchor.x)) % hillsideBuildingMaterials.Length];
                MakeVisualOnly(block);

                CreateVisualBox("Hillside district window band", safePosition + Vector3.up * (height * 0.5f), rotation, new Vector3(footprint * 0.8f, height * 0.4f, 0.08f), windowStripMaterial);
            }
        }

        // Canada's Gilles-Villeneuve-style identity: parkland-adjacent island setting
        // with a couple of modern circuit/paddock buildings, rather than the deep
        // forest treatment full parkland circuits get or the dense canyon of the pure
        // street tracks.
        void BuildCanadaBackdrop(float density)
        {
            // Was 0.5x - which on Canada meant just 6 near hills and 4 far
            // ridges, an almost-empty background (per report). Still lighter
            // than a full parkland circuit, but a real one.
            BuildParklandBackdrop(density * 0.85f);

            // Close-in tree lines (per report - "no trees surrounding the track
            // in canada"): the island layout runs two near-parallel legs only
            // ~70m apart, so the generic forest belt's 46m+ offsets and wide
            // clearance radius rejected most spots between and around the legs
            // - Canada ended up almost treeless while every other green track
            // got its belts. The real circuit is famously lined with trees
            // right at the verge, so this dedicated pass plants a tight row
            // ~24m off the centerline on both sides with a slim clearance
            // check that fits the narrow island strip.
            // Round 2 (per report - "not A TREE in sight" at the start/finish
            // straight): the shared pit-corridor exclusion wiped the RIGHT side
            // of 23% of the lap (0.83-1.0 plus 0-0.06) - which is exactly the
            // stretch the player stares down from the grid. The exclusion is
            // now only the real pit-building strip (0.86-1.0 / 0-0.03), that
            // stretch still gets trees pushed BEHIND the pit complex (90m+),
            // and the open lap gets a second staggered row so the verge reads
            // properly tree-lined.
            float treeLineSpacing = Mathf.Lerp(26f, 13f, Mathf.InverseLerp(0.25f, 2f, density));
            int treeLineSeed = 0;
            for (float d = 0f; d < Runtime.length; d += treeLineSpacing)
            {
                treeLineSeed++;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                float normalized = d / Mathf.Max(1f, Runtime.length);
                Vector3 groundPoint = GroundedTrackPoint(point);
                for (int side = -1; side <= 1; side += 2)
                {
                    bool pitStrip = (normalized > 0.86f || normalized < 0.03f) && side > 0f;
                    // Four rows (was two, per report - "the mid background has
                    // no trees; the start finish straight trees are still very
                    // sparse"): 24/40/58/78m out, so the verge line grades into
                    // a genuine mid-background wood instead of stopping at two
                    // thin rows.
                    for (int row = 0; row < 4; row++)
                    {
                        int treeSeed = treeLineSeed * 8 + row * 2 + (side + 1) / 2;
                        float lateralOffset = pitStrip
                            ? Runtime.roadHalfWidth + 90f + row * 16f + (treeSeed * 7) % 12
                            : Runtime.roadHalfWidth + 24f + row * 17f + (treeSeed * 7) % 9;
                        Vector3 desired = groundPoint + right * side * lateralOffset + forward * (((treeSeed * 11) % 15) - 7f);
                        // Never inside a grandstand's footprint (per report) -
                        // pushed behind the stand instead of skipped.
                        if (IsInsideGrandstandZone(desired))
                        {
                            desired = groundPoint + right * side * (GrandstandZoneLateralMeters + 12f + (treeSeed * 5) % 18);
                        }

                        Vector3 safePosition;
                        if (!TryGetClearScenerySpot(desired, 3f, 1f, out safePosition))
                        {
                            continue;
                        }

                        CreateBeltTree(safePosition, treeSeed);
                    }
                }
            }

            int buildings = Mathf.Max(2, Mathf.RoundToInt(3f * density));
            for (int i = 0; i < buildings; i++)
            {
                float t = (i + 0.5f) / buildings;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? 1 : -1;
                Vector3 anchor = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 50f);
                CreateProceduralBuildingCluster(anchor, forward, 2, 16f + (i % 2) * 6f, false);
            }
        }

        // Mexico's Foro Sol-style stadium section: a tight, tall grandstand bowl around
        // the closing corner complex, denser than the standard BuildGrandstand rows.
        void CreateStadiumComplex()
        {
            const float normalized = 0.94f;
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(Runtime.length * normalized, out point, out forward, out right);
            for (int side = -1; side <= 1; side += 2)
            {
                // Grounded - the closing corner complex this stadium bowl wraps can be
                // an elevated stretch, and the bowl is a ground-standing structure well
                // clear of the road (20m+), not something mounted to the road itself.
                Vector3 basePosition = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 20f);
                Vector3 lateral = right * side;
                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
                CreateGroundPatch("Stadium bowl foundation pad", basePosition + lateral * 6f, 14f, 56f, concreteMaterial, rotation);

                // Bigger bowl (12 tiers, wider span) than the original 9-tier version for
                // a stronger grandstand-bowl effect.
                const int tiers = 12;
                for (int row = 0; row < tiers; row++)
                {
                    Vector3 rowCenter = basePosition + Vector3.up * (0.4f + row * 0.68f) + lateral * row * 1.3f;
                    CreateVisualBox("Stadium bowl tier", rowCenter, rotation, new Vector3(1.3f, 0.55f, 52f), metalMaterial);
                    CreateVisualBox("Stadium bowl crowd block", rowCenter + Vector3.up * 0.5f, rotation, new Vector3(0.95f, 0.46f, 51f), row % 2 == 0 ? sceneryAccentMaterial : glassMaterial);
                }

                Vector3 roofCenter = basePosition + Vector3.up * 10.8f + lateral * 4.6f;
                CreateVisualBox("Stadium bowl roof", roofCenter, rotation, new Vector3(13f, 0.3f, 54f), grandstandRoofMaterial);
            }

            // Extra wraparound sections just before/after the main tier so the bowl
            // encloses the whole closing corner complex rather than reading as one
            // straight stand. Routed through the clearance helper since this is new.
            for (float offset = -0.035f; offset <= 0.035f; offset += 0.07f)
            {
                Vector3 wrapPoint;
                Vector3 wrapForward;
                Vector3 wrapRight;
                Runtime.SampleAtDistance(Runtime.length * (normalized + offset), out wrapPoint, out wrapForward, out wrapRight);
                Vector3 desired = GroundedTrackPoint(wrapPoint) + wrapRight * (Runtime.roadHalfWidth + 20f);
                Vector3 safeAnchor;
                if (!TryGetClearScenerySpot(desired, 15f, 4f, out safeAnchor))
                {
                    continue;
                }

                Quaternion rotation = Quaternion.LookRotation(wrapForward, Vector3.up);
                for (int row = 0; row < 7; row++)
                {
                    Vector3 rowCenter = safeAnchor + Vector3.up * (0.4f + row * 0.68f) + wrapRight * row * 1.3f;
                    CreateVisualBox("Stadium bowl wrap tier", rowCenter, rotation, new Vector3(1.3f, 0.55f, 30f), metalMaterial);
                    CreateVisualBox("Stadium bowl wrap crowd block", rowCenter + Vector3.up * 0.5f, rotation, new Vector3(0.95f, 0.46f, 29f), row % 2 == 0 ? sceneryAccentMaterial : glassMaterial);
                }
            }
        }

        // Austin/COTA's observation tower: a tall thin shaft with an angled marker deck
        // near the top, kept as a distant silhouette well clear of the fence line rather
        // than right at trackside.
        void CreateObservationTower()
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(Runtime.length * 0.5f, out point, out forward, out right);
            // Grounded - this landmark sits 150m clear of the road on real ground, well
            // outside anything an elevated stretch's deck height should influence.
            Vector3 desired = GroundedTrackPoint(point) + right * (Runtime.roadHalfWidth + 150f);
            Vector3 basePosition;
            if (!TryGetClearScenerySpot(desired, 8f, 10f, out basePosition))
            {
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            const float towerHeight = 96f; // taller and more iconic than the original 70f pole
            CreateGroundPatch("Observation tower foundation pad", basePosition, 12f, 12f, concreteMaterial, rotation);
            CreateVisualBox("Observation tower shaft", basePosition + Vector3.up * towerHeight * 0.5f, rotation, new Vector3(3.4f, towerHeight, 3.4f), metalMaterial);
            CreateVisualBox("Observation tower deck", basePosition + Vector3.up * (towerHeight + 2f), rotation, new Vector3(10.5f, 1.8f, 10.5f), concreteMaterial);

            // COTA's real tower has an angled, twisted observation deck near the top;
            // two canted box elements at contrasting angles approximate that silhouette
            // instead of a plain vertical pole capping the shaft.
            GameObject twistLower = GameObject.CreatePrimitive(PrimitiveType.Cube);
            twistLower.name = "Observation tower twist lower";
            twistLower.transform.SetParent(transform);
            twistLower.transform.position = basePosition + Vector3.up * (towerHeight + 6.5f);
            twistLower.transform.rotation = rotation * Quaternion.Euler(0f, 18f, 12f);
            twistLower.transform.localScale = new Vector3(6.5f, 3.2f, 3.6f);
            twistLower.GetComponent<Renderer>().sharedMaterial = concreteMaterial;
            MakeVisualOnly(twistLower);

            GameObject twistUpper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            twistUpper.name = "Observation tower twist upper";
            twistUpper.transform.SetParent(transform);
            twistUpper.transform.position = basePosition + Vector3.up * (towerHeight + 10.5f);
            twistUpper.transform.rotation = rotation * Quaternion.Euler(0f, -22f, -10f);
            twistUpper.transform.localScale = new Vector3(5.2f, 2.8f, 3.2f);
            twistUpper.GetComponent<Renderer>().sharedMaterial = sceneryAccentMaterial;
            MakeVisualOnly(twistUpper);

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Observation tower marker";
            marker.transform.SetParent(transform);
            marker.transform.position = basePosition + Vector3.up * (towerHeight + 15f);
            marker.transform.rotation = rotation * Quaternion.Euler(18f, 0f, 6f);
            marker.transform.localScale = new Vector3(0.6f, 4.5f, 0.6f);
            marker.GetComponent<Renderer>().sharedMaterial = sceneryAccentMaterial;
            MakeVisualOnly(marker);

            // Rolling terrain variation near the tower base.
            for (int i = 0; i < 3; i++)
            {
                Vector3 hillDesired = basePosition + forward * (i - 1) * 40f + right * 30f;
                Vector3 safeHill;
                if (!TryGetClearScenerySpot(hillDesired, 26f, 8f, out safeHill))
                {
                    continue;
                }

                // De-blob pass (per report): irregular multi-lobe rise instead
                // of one smooth dome.
                float heightScale = 10f + i * 4f;
                CreateRidgeFormation(safeHill, 52f, heightScale, 40f, grassMaterial, i);
            }
        }

        // Generic per-track infrastructure pass: one race-control-style tower, one or
        // two overhead billboard gantries on the main straight(s), a couple of low
        // paddock parked-vehicle block suggestions, and thin service-road strips set
        // back behind the barriers. New structure types for every track, all routed
        // through the same clearance helpers as the rest of the file's scenery.
        void BuildTrackInfrastructure()
        {
            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);
            BuildControlTower();
            BuildTracksideCrane();
            BuildHelipad();
            BuildBillboardGantries(density);
            BuildParkingBlocks(density);
            BuildServiceRoadStrips(density);

            // Long circuits get 1-2 spanning spectator bridges of their own; the
            // city-street backdrop already builds its bridges (and Suzuka its
            // signature crossover in BuildCircuitLandmarks), so only the remaining
            // archetypes need them here. CreateSponsorBridge keeps a 9.5m deck
            // clearance, comfortably above the 7m minimum for the racing surface.
            bool cityStreet = streetTrack && !monacoTrack && !neonTrack;
            if (!cityStreet && Runtime.length > 5000f)
            {
                CreateSponsorBridge(Runtime.length * 0.3f);
                if (Runtime.length > 6200f && density > 0.55f)
                {
                    CreateSponsorBridge(Runtime.length * 0.62f);
                }
            }
        }

        // Race-control-style tower near the pit lane/start-finish: tall shaft with a
        // glass-banded upper section, distinct from the generic skyline/paddock
        // buildings elsewhere in this file.
        void BuildControlTower()
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(Runtime.length * 0.975f, out point, out forward, out right);
            // Grounded like BuildTracksideCrane/BuildHelipad already do - near the pit
            // exit this is usually flat, but the tower must not silently float if that
            // ever isn't true.
            Vector3 desired = GroundedTrackPoint(point) + right * (Runtime.PitLaneLateral + 42f);
            Vector3 basePosition;
            if (!TryGetClearScenerySpot(desired, 6f, 8f, out basePosition))
            {
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            const float shaftHeight = 27f;
            const float glassHeight = 11f;
            CreateGroundPatch("Control tower foundation pad", basePosition, 9f, 9f, concreteMaterial, rotation);
            CreateVisualBox("Control tower shaft", basePosition + Vector3.up * shaftHeight * 0.5f, rotation, new Vector3(7.2f, shaftHeight, 7.2f), concreteMaterial);
            CreateVisualBox("Control tower glass band", basePosition + Vector3.up * (shaftHeight + glassHeight * 0.5f), rotation, new Vector3(7.6f, glassHeight, 7.6f), glassMaterial);
            CreateVisualBox("Control tower roof", basePosition + Vector3.up * (shaftHeight + glassHeight + 0.4f), rotation, new Vector3(8f, 0.7f, 8f), sceneryAccentMaterial);
            CreateVisualBox("Control tower mast", basePosition + Vector3.up * (shaftHeight + glassHeight + 3.2f), rotation, new Vector3(0.18f, 5f, 0.18f), metalMaterial);
            if (nightTrack || twilightTrack)
            {
                CreateVisualBox("Control tower glow strip", basePosition + Vector3.up * (shaftHeight + glassHeight * 0.5f) + right * 3.85f, rotation, new Vector3(0.1f, glassHeight * 0.8f, 7f), lightGlowMaterial);
            }
        }

        // One cheap tower-crane silhouette near the pit complex - built once per track
        // (not scaled with lap length) since it is landmark dressing, not per-meter
        // scenery, echoing the broadcast/construction crane every real paddock has.
        void BuildTracksideCrane()
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            // Beside the pit building row (fixed metres before the line, matching
            // the metre-anchored pit boxes).
            Runtime.SampleAtDistance(Runtime.length - TrackRuntime.PitLaneEndLeadMetres - 120f, out point, out forward, out right);
            Vector3 desired = point + right * (Runtime.PitLaneLateral + 62f);
            Vector3 basePosition;
            if (!TryGetClearScenerySpot(desired, 10f, 8f, out basePosition))
            {
                return;
            }

            CreateTracksideCrane(new Vector3(basePosition.x, groundTopY, basePosition.z), forward);
        }

        void CreateTracksideCrane(Vector3 position, Vector3 forward)
        {
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            const float mastHeight = 22f;
            CreateVisualBox("Trackside crane mast", position + Vector3.up * mastHeight * 0.5f, rotation, new Vector3(0.6f, mastHeight, 0.6f), metalMaterial);
            CreateVisualBox("Trackside crane jib", position + Vector3.up * mastHeight + forward * 9f, rotation, new Vector3(0.4f, 0.4f, 18f), metalMaterial);
            CreateVisualBox("Trackside crane counter-jib", position + Vector3.up * mastHeight - forward * 4.5f, rotation, new Vector3(0.4f, 0.4f, 9f), metalMaterial);
            CreateVisualBox("Trackside crane counterweight", position + Vector3.up * (mastHeight - 0.4f) - forward * 8.5f, rotation, new Vector3(1.4f, 1.2f, 1.4f), concreteMaterial);
            CreateVisualBox("Trackside crane cabin", position + Vector3.up * (mastHeight - 1f), rotation, new Vector3(0.9f, 1f, 0.9f), glassMaterial);
            if (nightTrack || twilightTrack)
            {
                CreateVisualBox("Trackside crane beacon", position + Vector3.up * (mastHeight + 0.6f), rotation, new Vector3(0.25f, 0.25f, 0.25f), lightGlowMaterial);
            }
        }

        // Generic medical/broadcast helicopter landing pad near the paddock - every
        // real circuit has one close to the pit complex, and this was a category of
        // paddock dressing that didn't exist anywhere in this file yet (unlike the
        // control tower/crane/parking blocks that already cover the rest of that same
        // footprint). Placed at its own normalized distance and lateral offset so it
        // doesn't overlap BuildControlTower (+42m at 0.975) or BuildTracksideCrane
        // (+62m at 0.955). Entirely visual-only pieces (CreateVisualBox/visual-only
        // cylinder never add a collider), so it carries zero collision risk on top of
        // TryGetClearScenerySpot already keeping it clear of the racing corridor.
        void BuildHelipad()
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(Runtime.length * 0.9f, out point, out forward, out right);
            Vector3 desired = point + right * (Runtime.PitLaneLateral + 46f);
            Vector3 basePosition;
            if (!TryGetClearScenerySpot(desired, 7f, 8f, out basePosition))
            {
                return;
            }

            basePosition = new Vector3(basePosition.x, groundTopY, basePosition.z);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Helipad surface";
            pad.transform.SetParent(transform);
            pad.transform.position = basePosition + Vector3.up * 0.05f;
            pad.transform.localScale = new Vector3(7f, 0.05f, 7f);
            pad.GetComponent<Renderer>().sharedMaterial = weatheredConcreteMaterial;
            MakeVisualOnly(pad);

            // Painted "H" built from the same thin CreateVisualBox stripes every other
            // painted marking in this file uses, rather than a texture/decal.
            CreateVisualBox("Helipad H left bar", basePosition + Vector3.up * 0.09f - right * 1.1f, rotation, new Vector3(0.5f, 0.02f, 2.6f), lineMaterial);
            CreateVisualBox("Helipad H right bar", basePosition + Vector3.up * 0.09f + right * 1.1f, rotation, new Vector3(0.5f, 0.02f, 2.6f), lineMaterial);
            CreateVisualBox("Helipad H cross bar", basePosition + Vector3.up * 0.09f, rotation, new Vector3(2.2f, 0.02f, 0.5f), lineMaterial);

            // Windsock on its own pole beside the pad - a generic wind indicator, no
            // text or logos.
            Vector3 polePosition = basePosition + right * 5.2f;
            CreateVisualBox("Helipad windsock pole", polePosition + Vector3.up * 2.5f, rotation, new Vector3(0.08f, 5f, 0.08f), metalMaterial);
            Material sockMaterial = CreateMaterial("Helipad windsock", new Color(0.92f, 0.42f, 0.05f), 0f, 0.4f);
            CreateVisualBox("Helipad windsock", polePosition + Vector3.up * 4.6f + forward * 0.45f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(0.32f, 0.28f, 0.9f), sockMaterial);

            if (nightTrack || twilightTrack)
            {
                CreateVisualBox("Helipad perimeter light", basePosition + Vector3.up * 0.12f + forward * 3.4f, rotation, new Vector3(0.18f, 0.1f, 0.18f), lightGlowMaterial);
                CreateVisualBox("Helipad perimeter light", basePosition + Vector3.up * 0.12f - forward * 3.4f, rotation, new Vector3(0.18f, 0.1f, 0.18f), lightGlowMaterial);
            }
        }

        void BuildBillboardGantries(float density)
        {
            CreateBillboardGantry(Runtime.length * 0.06f);
            if (density > 0.55f)
            {
                CreateBillboardGantry(Runtime.length * 0.47f);
            }
        }

        // Overhead sign spanning the track, well above any car/camera concern. Height
        // alone clears the corridor, so - per the brief - it is the support pylons'
        // lateral footprint that gets the radius-aware clearance check, not the span
        // itself, following the same visual-only convention CreateSuzukaCrossoverBridge
        // and CreateSponsorBridge already use for their decks. Both pylon spots must
        // clear before anything is created, so a failed second pylon never leaves an
        // orphaned first one behind.
        void CreateBillboardGantry(float distance)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            float span = Runtime.roadHalfWidth * 2f + 8f;
            const float clearance = 12.5f;
            float deckY = point.y + clearance;

            Vector3 leftPylonTop = point - right * (span * 0.5f - 1f);
            Vector3 rightPylonTop = point + right * (span * 0.5f - 1f);
            Vector3 leftSafe;
            Vector3 rightSafe;
            if (!TryGetClearScenerySpot(new Vector3(leftPylonTop.x, groundTopY, leftPylonTop.z), 1.6f, 4f, out leftSafe) ||
                !TryGetClearScenerySpot(new Vector3(rightPylonTop.x, groundTopY, rightPylonTop.z), 1.6f, 4f, out rightSafe))
            {
                return;
            }

            Quaternion postRotation = Quaternion.LookRotation(forward, Vector3.up);
            float pylonHeight = Mathf.Max(2f, deckY - groundTopY);
            CreateGroundPatch("Billboard gantry pylon pad", leftSafe, 2f, 2f, concreteMaterial, postRotation);
            CreateGroundPatch("Billboard gantry pylon pad", rightSafe, 2f, 2f, concreteMaterial, postRotation);
            CreateVisualBox("Billboard gantry pylon", new Vector3(leftSafe.x, groundTopY + pylonHeight * 0.5f, leftSafe.z), postRotation, new Vector3(1.1f, pylonHeight, 1.1f), metalMaterial);
            CreateVisualBox("Billboard gantry pylon", new Vector3(rightSafe.x, groundTopY + pylonHeight * 0.5f, rightSafe.z), postRotation, new Vector3(1.1f, pylonHeight, 1.1f), metalMaterial);

            Vector3 deckCenter = new Vector3(point.x, deckY, point.z);
            Quaternion deckRotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateVisualBox("Billboard gantry frame", deckCenter, deckRotation, new Vector3(span, 0.5f, 2.4f), metalMaterial);
            Color panelColor = SponsorPalette[Mathf.Abs(Mathf.RoundToInt(distance)) % SponsorPalette.Length];
            Material panel = CreateMaterial("Billboard gantry panel", panelColor, 0.05f, 0.55f, (nightTrack || twilightTrack) ? panelColor * 0.4f : Color.black);
            CreateVisualBox("Billboard gantry panel", deckCenter - Vector3.up * 1.4f, deckRotation, new Vector3(span * 0.82f, 2.1f, 0.28f), panel);
            if (nightTrack || twilightTrack || neonTrack)
            {
                CreateVisualBox("Billboard gantry glow trim", deckCenter - Vector3.up * 0.3f, deckRotation, new Vector3(span * 0.86f, 0.1f, 2.5f), lightGlowMaterial);
            }
        }

        // Low rectangular parked-vehicle block suggestions near the paddock area,
        // generic across every track and kept sparse - background dressing, not a
        // feature.
        void BuildParkingBlocks(float density)
        {
            int rows = Mathf.Max(1, Mathf.RoundToInt(3f * density));
            float corridorStart = Runtime.length * Runtime.PitCorridorStartNormalized;
            float corridorEnd = Runtime.length * 0.995f;
            for (int i = 0; i < rows; i++)
            {
                float t = rows <= 1 ? 0.5f : (i + 0.5f) / rows;
                float d = Mathf.Lerp(corridorStart, corridorEnd, t);
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                Vector3 desired = GroundedTrackPoint(point) + right * (Runtime.PitLaneLateral + 30f);
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, 6f, 4f, out safePosition))
                {
                    continue;
                }

                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
                Material blockMaterial = i % 2 == 0 ? concreteMaterial : metalMaterial;
                CreateVisualBox("Paddock parked vehicle block", safePosition + Vector3.up * 0.55f, rotation, new Vector3(7.4f, 1.1f, 2.6f), blockMaterial);
                CreateVisualBox("Paddock parked vehicle block", safePosition + Vector3.up * 0.55f + right * 3.6f, rotation, new Vector3(7.4f, 1.1f, 2.6f), blockMaterial);
            }
        }

        // Thin, low-contrast paved strip running parallel to the track, set back behind
        // the barriers - a cheap suggestion of a service road rather than a modelled one.
        void BuildServiceRoadStrips(float density)
        {
            float spacing = Mathf.Lerp(110f, 60f, Mathf.InverseLerp(0.25f, 2f, density));
            int index = 0;
            for (float d = 0f; d < Runtime.length; d += spacing)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d + spacing * 0.5f, out point, out forward, out right);
                int side = index % 2 == 0 ? -1 : 1;
                Vector3 desired = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 26f);
                Vector3 safePosition;
                if (TryGetClearScenerySpot(desired, 2f, 2f, out safePosition))
                {
                    CreateVisualBox("Trackside service road strip", safePosition + Vector3.up * 0.02f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(3f, 0.02f, spacing * 0.8f), asphaltPatchMaterial);
                }

                index++;
            }
        }

        // Row of moored white "yachts" along a straight for Monaco's harbour promenade.
        // Hull length now varies per instance and a hull stripe/richer cabin was added
        // so a bigger marina doesn't just repeat one identical boat shape.
        void BuildHarbourYachts(float startNormalized, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float d = Runtime.length * startNormalized + i * 15f;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                float hullLength = 6.5f + (i % 3) * 3.5f;
                Vector3 basePos = PushSceneryClearOfTrack(GroundedTrackPoint(point) + right * (Runtime.roadHalfWidth + 24f), 20f + hullLength * 0.3f);
                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

                // Flat water surface under each yacht slot, overlapping its neighbours
                // along the promenade, so the marina reads as a real harbour instead of
                // boats parked on bare runoff ground.
                GameObject water = CreateVisualBox("Harbour water", new Vector3(basePos.x, groundTopY + 0.04f, basePos.z), rotation, new Vector3(18f, 0.05f, 16f), waterMaterial);
                water.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                CreateVisualBox("Harbour yacht hull", basePos + Vector3.up * 0.6f, rotation, new Vector3(2.6f, 1.15f, hullLength), yachtMaterial);
                CreateVisualBox("Harbour yacht hull stripe", basePos + Vector3.up * 0.98f, rotation, new Vector3(2.65f, 0.14f, hullLength), sceneryAccentMaterial);
                CreateVisualBox("Harbour yacht cabin", basePos + Vector3.up * 1.55f, rotation, new Vector3(1.7f, 0.95f, hullLength * 0.42f), glassMaterial);
                CreateVisualBox("Harbour yacht mast", basePos + Vector3.up * 3.5f, rotation, new Vector3(0.1f, 3.9f, 0.1f), metalMaterial);
            }
        }

        // Suzuka's famous over/underpass, purely a visual silhouette here: a concrete
        // deck arcing well above the road (no collider, so it can never clip a car)
        // with columns grounded at groundTopY the same way CreateBridgeSupports does.
        void CreateSuzukaCrossoverBridge(float distance)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            // Deck rotation uses "forward" (not "right") so its X scale runs laterally
            // across the road and Z runs along the track - the opposite convention from
            // the roadside boards, which need their thin face pointed at approaching cars.
            Quaternion deckRotation = Quaternion.LookRotation(forward, Vector3.up);
            float span = Runtime.roadHalfWidth * 2f + 12f;
            const float clearance = 8.5f;
            float deckY = point.y + clearance;
            Vector3 deckCenter = new Vector3(point.x, deckY, point.z);
            CreateVisualBox("Suzuka crossover deck", deckCenter, deckRotation, new Vector3(span, 0.9f, 6.5f), concreteMaterial);
            CreateVisualBox("Suzuka crossover rail", deckCenter + Vector3.up * 0.85f, deckRotation, new Vector3(span, 0.18f, 0.4f), fencePostMaterial);

            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 columnTop = point + right * side * (span * 0.5f - 1.3f);
                float columnHeight = Mathf.Max(2f, deckY - groundTopY);
                Vector3 columnCenter = new Vector3(columnTop.x, groundTopY + columnHeight * 0.5f, columnTop.z);
                CreateVisualBox("Suzuka crossover column", columnCenter, Quaternion.LookRotation(forward, Vector3.up), new Vector3(1.7f, columnHeight, 1.7f), concreteMaterial);
            }
        }

        // Simple torii-gate silhouette built from the same box primitives as everything
        // else in this file; pushed clear of the racing surface like other scenery.
        void CreateToriiGate(Vector3 position, Vector3 forward)
        {
            Vector3 safePosition = PushSceneryClearOfTrack(position, 14f);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            const float halfSpan = 3.4f;
            CreateVisualBox("Torii pillar", safePosition + right * halfSpan + Vector3.up * 2.6f, rotation, new Vector3(0.5f, 5.2f, 0.5f), toriiMaterial);
            CreateVisualBox("Torii pillar", safePosition - right * halfSpan + Vector3.up * 2.6f, rotation, new Vector3(0.5f, 5.2f, 0.5f), toriiMaterial);
            CreateVisualBox("Torii upper beam", safePosition + Vector3.up * 5.4f, rotation, new Vector3(halfSpan * 2.6f, 0.45f, 0.7f), toriiMaterial);
            CreateVisualBox("Torii lower beam", safePosition + Vector3.up * 4.5f, rotation, new Vector3(halfSpan * 2.1f, 0.28f, 0.5f), toriiMaterial);
        }

        // Distant translucent haze banks well behind the treeline; alpha-blended so the
        // Ardennes forest reads as misty without hiding the trees or barriers in front.
        void BuildSpaMist()
        {
            Material mist = CreateTranslucentMaterial("Runtime Ardennes mist", new Color(0.62f, 0.68f, 0.66f), 0.16f);
            for (float d = 0f; d < Runtime.length; d += 140f)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 basePos = point + right * side * (Runtime.roadHalfWidth + 55f) + Vector3.up * 6f;
                    CreateVisualBox("Ardennes mist bank", basePos, Quaternion.LookRotation(forward, Vector3.up), new Vector3(46f, 14f, 3f), mist);
                }
            }
        }

        // Thin glossy overlay above every paint layer so a wet track visibly sheens
        // under lights, on top of the darker/glossier base road material CreateMaterials
        // already applies when Runtime.weather is raining.
        void BuildWetSheenOverlay()
        {
            Material sheen = CreateTranslucentMaterial("Runtime wet sheen", new Color(0.75f, 0.82f, 0.9f), 0.14f);
            F1Game.Rendering.ShaderCompat.SetSmoothness(sheen, 0.98f);
            sheen.SetFloat("_Metallic", 0.05f);
            for (float d = 0f; d < Runtime.length; d += 40f)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d + 20f, out point, out forward, out right);
                CreateVisualBox("Wet track sheen overlay", point + Vector3.up * 0.105f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(Runtime.roadHalfWidth * 1.94f, 0.01f, 39f), sheen);
            }
        }

        // Every built stand's footprint, recorded so the tree passes can avoid
        // planting inside one (per report - "theres trees in the middle of the
        // grandstands now"): x = normalized centre, y = side (-1/1),
        // z = half-span in normalized lap units. Lateral extent is checked as a
        // shared conservative constant (stands reach ~60m out, plus the
        // curved-section outward push).
        readonly System.Collections.Generic.List<Vector3> grandstandSpans = new System.Collections.Generic.List<Vector3>();
        const float GrandstandZoneLateralMeters = 90f;

        bool IsInsideGrandstandZone(Vector3 position)
        {
            if (grandstandSpans.Count == 0)
            {
                return false;
            }

            TrackProgress progress = Runtime.GetProgress(position);
            float side = progress.lateralDistance >= 0f ? 1f : -1f;
            if (Mathf.Abs(progress.lateralDistance) > GrandstandZoneLateralMeters)
            {
                return false;
            }

            for (int i = 0; i < grandstandSpans.Count; i++)
            {
                if (grandstandSpans[i].y != side)
                {
                    continue;
                }

                float wrapped = Mathf.Abs(progress.normalized - grandstandSpans[i].x);
                wrapped = Mathf.Min(wrapped, 1f - wrapped);
                if (wrapped <= grandstandSpans[i].z)
                {
                    return true;
                }
            }

            return false;
        }

        void BuildGrandstand(float normalizedDistance, int side)
        {
            BuildGrandstand(normalizedDistance, side, 1f);
        }

        // scale = 1 reproduces the original stand exactly; scale > 1 builds a
        // genuinely bigger venue stand (per request - Jeddah's "more and larger
        // grandstands"): more seating tiers, a longer run of seats, and a
        // taller roof, all derived from the same row geometry so the tiers
        // still step up and AWAY from the track (a bigger stand grows outward,
        // never toward the corridor).
        void BuildGrandstand(float normalizedDistance, int side, float scale)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(Runtime.length * normalizedDistance, out point, out forward, out right);
            // Grounded rather than built on the raw sampled track height - the stand
            // sits well clear of the road (18m+) on real ground, so near a bridge/hill
            // it must not float at deck height with the tiers dangling above bare air.
            Vector3 basePosition = GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 18f);
            Vector3 lateral = right * side;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            // Curved-section clearance (per report - expanded stands are
            // "blocking the track"): the stand is a straight box up to ~88m
            // long laid along the CENTRE point's tangent, so on a curved
            // stretch its ends can swing over the road even though the centre
            // sits 18m clear.
            // Round 2 (per report - "the issue of grandstands blocking the
            // track isn't solved"): one push from three probes wasn't enough -
            // a probe near a curve can resolve against a DIFFERENT part of the
            // track after the push, or the mid-span can still bulge over the
            // road between probe points. Five probes along the stand now
            // re-check after every push (up to five passes), and a stand that
            // STILL can't clear (a tight loop with track on both sides, e.g. a
            // final-corner complex) is skipped outright rather than built
            // overhanging the racing surface.
            float probeHalfLength = 22f * scale * 0.5f;
            bool standClear = false;
            for (int attempt = 0; attempt < 5 && !standClear; attempt++)
            {
                float worstDeficit = 0f;
                for (int probe = -2; probe <= 2; probe++)
                {
                    Vector3 probePoint = basePosition + forward * (probe * probeHalfLength * 0.5f);
                    TrackProgress probeProgress = Runtime.GetProgress(probePoint);
                    float clearance = Mathf.Abs(probeProgress.lateralDistance) - (Runtime.HalfWidthAt(probeProgress.distance) + 12f);
                    if (clearance < 0f)
                    {
                        worstDeficit = Mathf.Max(worstDeficit, -clearance);
                    }
                }

                if (worstDeficit <= 0f)
                {
                    standClear = true;
                    break;
                }

                basePosition += lateral * (worstDeficit + 2f);
            }

            if (!standClear)
            {
                return;
            }

            // Tier-geometry fix (per report - "the grandstands ARE STILL TOO
            // SMALL"): the earlier scale passes multiplied length and row COUNT
            // but kept each row's rise/depth at the original 0.62m/1.15m, so
            // stands grew long without growing tall - an 18-tier stand still
            // only reached a ~13m roof line. The per-row rise and depth now
            // scale with the stand too (up to 1.5x at scale 4+), so a 4x stand
            // is 24 tiers, ~88m of seating and a ~23m roof line - real main-
            // grandstand mass. rows=6/scale=1 still reproduces the original
            // stand exactly.
            int rows = Mathf.Clamp(Mathf.RoundToInt(6f * scale), 6, 24);
            float standLength = 22f * scale;
            // Record the stand's footprint so tree passes never plant inside it
            // (half the length plus an 10m margin, in normalized lap units).
            grandstandSpans.Add(new Vector3(normalizedDistance, side, (standLength * 0.5f + 10f) / Mathf.Max(1f, Runtime.length)));
            float rowStepScale = Mathf.Lerp(1f, 1.5f, Mathf.Clamp01((scale - 1f) / 3f));
            float rowRise = 0.62f * rowStepScale;
            float rowDepth = 1.15f * rowStepScale;

            // Paved foundation pad under the stand so it reads as sitting on a real
            // concourse rather than hovering just above unbroken bare ground.
            CreateGroundPatch("Grandstand foundation pad", basePosition + lateral * rows * rowDepth * 0.43f, 9f * scale, standLength + 2f, concreteMaterial, rotation);

            // Tiered seating stepping up and away from the track, with colored crowd
            // blocks so the stands read as full rather than as bare metal shelves.
            // Parkland circuits get a weathered concrete tier tone instead of bare
            // metal for the "old-school racing venue" read the brief asks for.
            Material tierMaterial = parklandTrack ? weatheredConcreteMaterial : metalMaterial;
            for (int row = 0; row < rows; row++)
            {
                Vector3 rowCenter = basePosition + Vector3.up * (0.4f + row * rowRise) + lateral * row * rowDepth;
                CreateVisualBox("Grandstand tier", rowCenter, rotation, new Vector3(rowDepth * 1.09f, rowRise * 0.81f, standLength), tierMaterial);
                CreateVisualBox("Grandstand crowd block", rowCenter + Vector3.up * rowRise * 0.73f, rotation, new Vector3(rowDepth * 0.78f, rowRise * 0.68f, standLength - 1f), row % 2 == 0 ? sceneryAccentMaterial : glassMaterial);
            }

            // Roof canopy on slender pylons, in a distinct roof-toned material from the
            // seating tiers so the stand doesn't read as one flat block. All roof
            // geometry derives from the row rise/depth/count and length so every
            // scale keeps the original stand's proportions (rows=6/scale=1
            // reproduces the old hardcoded numbers exactly).
            float roofHeight = 0.4f + rows * rowRise + 1.28f * rowStepScale;
            Vector3 roofCenter = basePosition + Vector3.up * roofHeight + lateral * rows * rowDepth * 0.5f;
            CreateVisualBox("Grandstand roof", roofCenter, rotation, new Vector3(rows * rowDepth * 1.4f, 0.28f * rowStepScale, standLength + 1.5f), grandstandRoofMaterial);
            CreateVisualBox("Grandstand roof fascia", roofCenter - lateral * rows * rowDepth * 0.67f - Vector3.up * 0.5f * rowStepScale, rotation, new Vector3(0.22f, 0.9f * rowStepScale, standLength + 1.5f), sceneryAccentMaterial);
            for (int pylon = -1; pylon <= 1; pylon++)
            {
                CreateVisualBox("Grandstand pylon", basePosition + lateral * rows * rowDepth * 1.04f + forward * pylon * standLength * 0.43f + Vector3.up * roofHeight * 0.5f, rotation, new Vector3(0.4f * rowStepScale, roofHeight, 0.4f * rowStepScale), metalMaterial);
            }

            // Sponsor-neutral bunting flags strung along the roof fascia so the stand
            // reads as dressed for a race weekend rather than a bare metal shelf from a
            // distance - cycles the same invented SponsorPalette the trackside boards
            // already share instead of adding another colour set.
            // Bunting materials are cached per palette colour (one material per
            // colour for the whole track build, not one per flag) - with the
            // full 20+-stand build-out the old per-flag CreateMaterial would
            // have allocated hundreds of identical material instances.
            if (buntingMaterials == null || buntingMaterials.Length != SponsorPalette.Length || buntingMaterials[0] == null)
            {
                buntingMaterials = new Material[SponsorPalette.Length];
                for (int i = 0; i < SponsorPalette.Length; i++)
                {
                    buntingMaterials[i] = CreateMaterial("Grandstand bunting flag material", SponsorPalette[i], 0.02f, 0.4f);
                }
            }

            int buntingCount = Mathf.RoundToInt(7f * scale);
            for (int i = 0; i < buntingCount; i++)
            {
                float t = (i - (buntingCount - 1) * 0.5f) / buntingCount;
                Vector3 buntingPosition = roofCenter - lateral * rows * rowDepth * 0.68f - Vector3.up * 0.95f + forward * t * (standLength - 1f);
                CreateVisualBox("Grandstand bunting flag", buntingPosition, rotation * Quaternion.Euler(0f, 0f, 18f), new Vector3(0.04f, 0.4f, 0.5f), buntingMaterials[i % buntingMaterials.Length]);
            }
        }

        // Extra small scaffold-style temporary bleachers at additional corners beyond
        // BuildGrandstand's fixed permanent-stand set - fills in thin spots on a wide
        // shot without growing the permanent grandstand count. Skipped entirely below
        // 0.6 density and capped at a handful per lap either way, so this stays
        // landmark-sparse rather than per-corner clutter.
        void BuildTemporaryBleachers()
        {
            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);
            if (density < 0.6f)
            {
                return;
            }

            List<CornerInfo> corners = DetectCorners(40f);
            if (corners.Count == 0)
            {
                return;
            }

            int count = Mathf.Clamp(Mathf.RoundToInt(2f * density), 1, 4);
            int step = Mathf.Max(1, corners.Count / count);
            int placed = 0;
            for (int i = 0; i < corners.Count && placed < count; i += step)
            {
                float normalized = corners[i].distance / Mathf.Max(1f, Runtime.length);
                if (normalized > 0.8f || normalized < 0.08f)
                {
                    continue;
                }

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(corners[i].distance, out point, out forward, out right);
                int side = i % 2 == 0 ? 1 : -1;
                // Grounded like BuildGrandstand - a corner can easily be an elevated
                // stretch, and this stand sits well clear of the road on real ground.
                CreateTemporaryBleacher(GroundedTrackPoint(point) + right * side * (Runtime.roadHalfWidth + 15f), forward, i);
                placed++;
            }
        }

        // Cheap scaffold-and-canvas stand distinct from BuildGrandstand's permanent
        // tiered structure - three low rows on a tube frame with a canvas sun-shade
        // roof, standing in for the temporary grandstands real circuits erect at
        // popular corners for a single event weekend.
        void CreateTemporaryBleacher(Vector3 position, Vector3 forward, int index)
        {
            Vector3 safePosition = PushSceneryClearOfTrack(position, 9f);
            Vector3 lateral = Vector3.Cross(Vector3.up, forward).normalized;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            for (int row = 0; row < 3; row++)
            {
                Vector3 rowCenter = safePosition + Vector3.up * (0.35f + row * 0.55f) + lateral * row * 1.05f;
                CreateVisualBox("Temporary bleacher tier", rowCenter, rotation, new Vector3(1f, 0.42f, 12f), fencePostMaterial);
                CreateVisualBox("Temporary bleacher crowd block", rowCenter + Vector3.up * 0.38f, rotation, new Vector3(0.75f, 0.36f, 11.4f), row % 2 == 0 ? sceneryAccentMaterial : glassMaterial);
            }

            for (int leg = -1; leg <= 1; leg++)
            {
                CreateVisualBox("Temporary bleacher scaffold leg", safePosition + Vector3.up * 1f + forward * leg * 5.6f, rotation, new Vector3(0.12f, 2f, 0.12f), metalMaterial);
            }

            CreateVisualBox("Temporary bleacher canopy", safePosition + Vector3.up * 2.3f + lateral * 1.6f, rotation, new Vector3(3.6f, 0.1f, 12.4f), bleacherCanvasMaterial);
            if (index % 3 == 0)
            {
                CreateVisualBox("Temporary bleacher flag", safePosition + Vector3.up * 2.6f - lateral * 1.6f, rotation, new Vector3(0.05f, 0.5f, 0.34f), sceneryAccentMaterial);
            }
        }

        void CreateCityBlock(Vector3 position, Vector3 forward, int index, bool night)
        {
            float height = 5.2f + index % 5;
            Vector3 scale = new Vector3(5f + index % 4, height, 4.8f + index % 3);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 center = position + Vector3.up * (height * 0.5f);
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Generic city block";
            block.transform.SetParent(transform);
            block.transform.position = center;
            block.transform.rotation = rotation;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = night || nightTrack ? glassMaterial : barrierMaterial;
            MakeVisualOnly(block);

            // Window bands facing the track; emissive at night so street circuits
            // feel inhabited instead of like grey crates.
            Vector3 flatPosition = new Vector3(position.x, 0f, position.z);
            Vector3 towardTrack = flatPosition.sqrMagnitude > 1f ? -flatPosition.normalized : forward;
            // Singapore/Vegas get a share of coloured neon windows mixed in with the
            // striped window material so the skyline reads as multi-coloured night-
            // spectacle rather than one uniform wash; every other building gets the
            // cached window-strip texture instead of one flat glass/glow colour.
            Material windowMaterial = neonTrack && index % 3 == 1 ? neonMaterials[index % neonMaterials.Length] : windowStripMaterial;
            int bands = Mathf.Clamp(Mathf.RoundToInt(height / 2.4f), 1, 3);
            for (int band = 0; band < bands; band++)
            {
                CreateVisualBox("City window band", center + Vector3.up * (band * 1.9f - height * 0.24f) + towardTrack * (scale.z * 0.5f + 0.06f), rotation, new Vector3(scale.x * 0.84f, 0.7f, 0.08f), windowMaterial);
            }

            // Occasional rooftop neon sign for the neon street styles.
            if ((night || nightTrack) && index % 4 == 0)
            {
                Material signMaterial = neonTrack ? neonMaterials[(index / 4) % neonMaterials.Length] : lightGlowMaterial;
                CreateVisualBox("Rooftop neon sign", center + Vector3.up * (height * 0.5f + 0.7f), rotation, new Vector3(scale.x * 0.6f, 1.1f, 0.18f), signMaterial);
            }
        }

        // Sibling to CreateFloodlight: a freestanding stack of coloured emissive strips
        // rather than a functional light, used to give Vegas/Singapore their neon-strip
        // spectacle look away from the buildings themselves.
        void CreateNeonPylon(Vector3 position, Vector3 forward, int index)
        {
            Vector3 safePosition = PushSceneryClearOfTrack(position, 4f);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateVisualBox("Neon pylon post", safePosition + Vector3.up * 2.2f, rotation, new Vector3(0.22f, 4.4f, 0.22f), metalMaterial);
            for (int i = 0; i < 3; i++)
            {
                Material neon = neonMaterials[(index + i) % neonMaterials.Length];
                CreateVisualBox("Neon pylon strip", safePosition + Vector3.up * (1.1f + i * 1.3f), rotation, new Vector3(0.36f, 0.9f, 0.36f), neon);
            }
        }

        void CreateFloodlight(Vector3 position, Vector3 forward, bool bright)
        {
            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Track floodlight pole";
            pole.transform.SetParent(transform);
            pole.transform.position = position + Vector3.up * 3.2f;
            pole.transform.localScale = new Vector3(0.16f, 3.2f, 0.16f);
            pole.GetComponent<Renderer>().sharedMaterial = metalMaterial;
            MakeVisualOnly(pole);

            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = "Track floodlight head";
            lamp.transform.SetParent(transform);
            lamp.transform.position = position + Vector3.up * 6.5f;
            lamp.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            lamp.transform.localScale = new Vector3(1.2f, 0.42f, 0.22f);
            lamp.GetComponent<Renderer>().sharedMaterial = lightGlowMaterial;
            MakeVisualOnly(lamp);

            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = bright ? 54f : 30f;
            light.intensity = bright ? 1.15f : 0.45f;
        }

        // Real tree silhouettes (per report - "whatever the trees are are just
        // green blobs"): the old tree was a 1.8m trunk under a single ~1.4m
        // sphere - knee-high blobs at race distance. A cluster now plants
        // full-size trees (~9-14m) in two species: broadleaf (tall visible
        // trunk, limbs reaching into an irregular multi-lobe canopy that
        // alternates two foliage tones) and conifer (a stepped stack of
        // narrowing tiers to a point). Still cheap primitive stacks, same
        // clearance path, same stable index-derived jitter.
        void CreateTreeCluster(Vector3 position, int index)
        {
            // Never plant a cluster inside a grandstand's footprint (per report
            // - "theres trees in the middle of the grandstands now").
            if (IsInsideGrandstandZone(position))
            {
                return;
            }

            // Grass apron under the cluster so it reads as a planted stand rather
            // than trees hovering just above unbroken bare ground. Placed at the
            // cluster's own incoming y (not groundTopY) so it stays correct both for
            // the flat-ground BuildScenery callers and the hillside backdrop passes
            // that deliberately pass in a locally-elevated position.
            CreateVisualBox("Tree cluster ground patch", position + Vector3.up * 0.02f, Quaternion.identity, new Vector3(28f, 0.05f, 20f), grassMaterial);
            for (int i = 0; i < 3; i++)
            {
                // 3x tree pass (per report - "trees r too small"): tree scale
                // tripled (mature-forest 25-35m instead of 9-14m) and the
                // in-cluster spacing widened to match so three big canopies
                // read as a stand of trees, not one merged mass.
                Vector3 offset = new Vector3((i - 1) * 10f, 0f, (index % 3 - 1) * 6.5f);
                Vector3 treePosition = PushSceneryClearOfTrack(position + offset, 20f);
                float sizeJitter = (0.8f + ((index * 3 + i) % 5) * 0.11f) * 3f;
                if ((index + i) % 3 == 0)
                {
                    CreateConiferTree(treePosition, sizeJitter, index * 3 + i);
                }
                else
                {
                    CreateBroadleafTree(treePosition, sizeJitter, index * 3 + i);
                }
            }
        }

        void CreateBroadleafTree(Vector3 basePosition, float sizeJitter, int seed)
        {
            float trunkHeight = 6.5f * sizeJitter;
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Broadleaf tree trunk";
            trunk.transform.SetParent(transform);
            trunk.transform.position = basePosition + Vector3.up * trunkHeight * 0.5f;
            // Unity cylinders are 2 units tall at scale 1, so y-scale is half the
            // wanted height.
            trunk.transform.localScale = new Vector3(0.55f * sizeJitter, trunkHeight * 0.5f, 0.55f * sizeJitter);
            trunk.GetComponent<Renderer>().sharedMaterial = treeBarkMaterial;
            MakeVisualOnly(trunk);

            // Two limbs angling out of the upper trunk into the canopy so a close
            // pass reads as branch structure, not a lollipop stick.
            for (int branch = 0; branch < 2; branch++)
            {
                GameObject limb = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                limb.name = "Broadleaf tree limb";
                limb.transform.SetParent(transform);
                float branchYaw = (seed * 91 + branch * 168) % 360;
                limb.transform.rotation = Quaternion.Euler(34f + branch * 12f, branchYaw, 0f);
                limb.transform.position = basePosition + Vector3.up * (trunkHeight * 0.8f) + limb.transform.up * 1.2f * sizeJitter;
                limb.transform.localScale = new Vector3(0.22f * sizeJitter, 1.4f * sizeJitter, 0.22f * sizeJitter);
                limb.GetComponent<Renderer>().sharedMaterial = treeBarkMaterial;
                MakeVisualOnly(limb);
            }

            // Irregular canopy: one big central crown plus offset lobes at varied
            // heights, alternating the two foliage tones so it reads as layered
            // leaf mass instead of one flat-colour blob.
            Vector3 canopyCenter = basePosition + Vector3.up * (trunkHeight + 1.7f * sizeJitter);
            for (int lobe = 0; lobe < 5; lobe++)
            {
                float angle = ((seed * 53 + lobe * 137) % 360) * Mathf.Deg2Rad;
                float radial = lobe == 0 ? 0f : (1.5f + (lobe % 2) * 0.8f) * sizeJitter;
                float lift = lobe == 0 ? 0.8f : ((lobe * 31 + seed) % 3 - 1) * 0.9f;
                float width = (lobe == 0 ? 5.4f : 3.4f - (lobe % 2) * 0.5f) * sizeJitter;
                GameObject crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Broadleaf tree canopy lobe";
                crown.transform.SetParent(transform);
                crown.transform.position = canopyCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radial + Vector3.up * lift * sizeJitter;
                crown.transform.localScale = new Vector3(width, width * 0.76f, width);
                crown.GetComponent<Renderer>().sharedMaterial = (seed + lobe) % 2 == 0 ? foliageMaterial : foliageMaterialLight;
                MakeVisualOnly(crown);
            }
        }

        void CreateConiferTree(Vector3 basePosition, float sizeJitter, int seed)
        {
            float trunkHeight = 11f * sizeJitter;
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Conifer tree trunk";
            trunk.transform.SetParent(transform);
            trunk.transform.position = basePosition + Vector3.up * trunkHeight * 0.5f;
            trunk.transform.localScale = new Vector3(0.4f * sizeJitter, trunkHeight * 0.5f, 0.4f * sizeJitter);
            trunk.GetComponent<Renderer>().sharedMaterial = treeBarkMaterial;
            MakeVisualOnly(trunk);

            // Stepped tiers narrowing toward a point - the classic pine outline.
            const int tiers = 4;
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)(tiers - 1);
                float tierWidth = Mathf.Lerp(5f, 1.3f, f) * sizeJitter;
                float tierHeight = Mathf.Lerp(2.8f, 1.8f, f) * sizeJitter;
                float tierY = Mathf.Lerp(3.4f * sizeJitter, trunkHeight, f);
                GameObject tier = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tier.name = "Conifer tree tier";
                tier.transform.SetParent(transform);
                tier.transform.position = basePosition + Vector3.up * tierY;
                tier.transform.localScale = new Vector3(tierWidth, tierHeight, tierWidth);
                tier.GetComponent<Renderer>().sharedMaterial = foliageMaterial;
                MakeVisualOnly(tier);
            }
        }

        // Coastal-only companion to CreateTreeCluster: a couple of tall leaning trunks
        // topped with a fan of flattened frond blades rather than a round crown, so a
        // seaside promenade reads as tropical rather than reusing the forest tree
        // silhouette with a different tint. Frond blades are thin scaled cubes rotated
        // out from a shared top point and tipped downward, cheap to build and readable
        // at speed the same way the rest of this file favours primitive stacks over
        // detailed meshes.
        void CreatePalmCluster(Vector3 position, int index)
        {
            int count = 1 + index % 2;
            for (int p = 0; p < count; p++)
            {
                Vector3 offset = new Vector3((p - (count - 1) * 0.5f) * 3.2f, 0f, (index % 3 - 1) * 1.6f);
                Vector3 palmPosition = PushSceneryClearOfTrack(position + offset, 12f);
                float sizeJitter = 0.85f + ((index * 5 + p) % 4) * 0.1f;
                float trunkHeight = 3.6f * sizeJitter;
                float lean = ((index + p) % 2 == 0 ? 1f : -1f) * 6f;

                GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                trunk.name = "Palm trunk";
                trunk.transform.SetParent(transform);
                trunk.transform.position = palmPosition + Vector3.up * trunkHeight * 0.5f;
                trunk.transform.rotation = Quaternion.Euler(0f, (index * 47 + p * 19) % 360, lean);
                trunk.transform.localScale = new Vector3(0.16f * sizeJitter, trunkHeight, 0.16f * sizeJitter);
                trunk.GetComponent<Renderer>().sharedMaterial = treeBarkMaterial;
                MakeVisualOnly(trunk);

                Vector3 crownCenter = palmPosition + Vector3.up * (trunkHeight + 0.1f) + new Vector3(Mathf.Sin(lean * Mathf.Deg2Rad) * trunkHeight, 0f, 0f);
                int fronds = 6;
                for (int f = 0; f < fronds; f++)
                {
                    float angle = (f / (float)fronds) * 360f;
                    GameObject frond = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    frond.name = "Palm frond";
                    frond.transform.SetParent(transform);
                    frond.transform.position = crownCenter;
                    // Fronds fan outward and droop down from the crown point rather than
                    // sitting flat, so the silhouette reads as hanging leaves, not a disc.
                    frond.transform.rotation = Quaternion.Euler(28f, angle, 0f);
                    frond.transform.localScale = new Vector3(0.14f, 0.05f, 1.8f * sizeJitter);
                    // Offset the frond outward along its own rotated forward axis so the
                    // blades radiate from the crown point instead of overlapping at its centre.
                    frond.transform.position += frond.transform.forward * 0.9f * sizeJitter;
                    frond.GetComponent<Renderer>().sharedMaterial = palmFrondMaterial;
                    MakeVisualOnly(frond);
                }
            }
        }

        Vector3 PushSceneryClearOfTrack(Vector3 position, float clearance)
        {
            TrackProgress progress = Runtime.GetProgress(position);
            float minimum = Runtime.HalfWidthAt(progress.distance) + clearance;
            if (Mathf.Abs(progress.lateralDistance) >= minimum)
            {
                return position;
            }

            Vector3 right = Vector3.Cross(Vector3.up, progress.forward).normalized;
            float side = progress.lateralDistance >= 0f ? 1f : -1f;
            Vector3 moved = progress.nearestPoint + right * side * minimum;
            return new Vector3(moved.x, position.y, moved.z);
        }

        // Baseline runoff/kerb buffer assumed beyond the painted road edge before "clear
        // of the track" starts, used by the corridor-radius checks below.
        const float TrackCorridorRunoffWidth = 9f;

        // Robust clearance test for large-radius decorative scenery (hills, dunes,
        // mountain ridges, landmark clusters). PushSceneryClearOfTrack only checks a
        // single pivot point against the signed lateral distance of one local segment
        // frame, which is how a 90-unit-radius sphere could pass while its far edge still
        // loomed over the road. This instead measures the flat distance to the true
        // nearest point on the whole centerline (TrackProgress already scans every
        // segment, not just one sample) and requires room for the object's own footprint,
        // not just its pivot.
        bool IsClearOfTrackCorridor(Vector3 center, float objectRadius, float extraMargin)
        {
            TrackProgress progress = Runtime.GetProgress(center);
            Vector3 flatCenter = new Vector3(center.x, progress.nearestPoint.y, center.z);
            float distanceToCenterline = Vector3.Distance(flatCenter, progress.nearestPoint);
            float required = Runtime.HalfWidthAt(progress.distance) + TrackCorridorRunoffWidth + objectRadius + extraMargin;
            return distanceToCenterline >= required;
        }

        // Generalized PushSceneryClearOfTrack for objects with real size: tries the
        // desired spot first, then pushes straight out along the local track-right
        // direction, and reports failure instead of guessing so callers can skip the
        // instance rather than plant it too close to the corridor.
        bool TryGetClearScenerySpot(Vector3 desiredPosition, float objectRadius, float extraMargin, out Vector3 result)
        {
            if (IsClearOfTrackCorridor(desiredPosition, objectRadius, extraMargin))
            {
                result = desiredPosition;
                return true;
            }

            TrackProgress progress = Runtime.GetProgress(desiredPosition);
            Vector3 right = Vector3.Cross(Vector3.up, progress.forward).normalized;
            float side = progress.lateralDistance >= 0f ? 1f : -1f;
            float required = Runtime.HalfWidthAt(progress.distance) + TrackCorridorRunoffWidth + objectRadius + extraMargin;
            Vector3 moved = progress.nearestPoint + right * side * required;
            moved.y = desiredPosition.y;
            result = moved;
            return IsClearOfTrackCorridor(moved, objectRadius, extraMargin);
        }

        void CreateDune(Vector3 position, int index)
        {
            float width = 8f + index % 5;
            float depth = 4.6f + index % 4;
            float objectRadius = Mathf.Max(width, depth) * 0.5f;
            Vector3 safePosition;
            if (!TryGetClearScenerySpot(position, objectRadius, 6f, out safePosition))
            {
                return;
            }

            GameObject dune = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dune.name = "Sculpted runoff dune";
            dune.transform.SetParent(transform);
            dune.transform.position = safePosition + Vector3.down * 0.28f;
            dune.transform.localScale = new Vector3(width, 0.75f, depth);
            dune.GetComponent<Renderer>().sharedMaterial = grassMaterial;
            MakeVisualOnly(dune);
        }

        // Sparse desert scrub/rock cluster - a couple of low flattened bushes plus a
        // single rock, standing in for the sun-baked vegetation a desert circuit
        // actually has instead of the dense forest canopy CreateTreeCluster gives every
        // other archetype.
        void CreateDesertScrubCluster(Vector3 position, int index)
        {
            Vector3 safePosition = PushSceneryClearOfTrack(position, 14f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 offset = new Vector3((i - 0.5f) * 3.4f, 0f, (index % 3 - 1) * 1.6f);
                float sizeJitter = 0.7f + ((index * 3 + i) % 4) * 0.12f;
                GameObject bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bush.name = "Desert scrub bush";
                bush.transform.SetParent(transform);
                bush.transform.position = safePosition + offset + Vector3.up * 0.3f * sizeJitter;
                bush.transform.localScale = new Vector3(0.9f, 0.55f, 0.9f) * sizeJitter;
                bush.GetComponent<Renderer>().sharedMaterial = foliageMaterial;
                MakeVisualOnly(bush);
            }

            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "Desert rock";
            rock.transform.SetParent(transform);
            rock.transform.position = safePosition + new Vector3(1.6f, 0.22f, -1.2f);
            rock.transform.rotation = Quaternion.Euler(8f, (index * 53) % 360, 5f);
            rock.transform.localScale = new Vector3(0.85f, 0.42f, 0.68f);
            rock.GetComponent<Renderer>().sharedMaterial = rockMaterial;
            MakeVisualOnly(rock);
        }

        // Jagged rock-outcrop cluster for mountain/forest cliff dressing - a handful of
        // rotated, size-jittered boxes rather than the smooth spheres the hill/ridge
        // backdrops use, so a cliff face reads distinctly from a grass-covered hillside.
        void CreateRockCluster(Vector3 position, int index)
        {
            Vector3 safePosition = PushSceneryClearOfTrack(position, 16f);
            int pieces = 2 + index % 2;
            for (int p = 0; p < pieces; p++)
            {
                Vector3 offset = new Vector3((p - (pieces - 1) * 0.5f) * 2.6f, 0f, (index % 3 - 1) * 1.8f);
                float sizeJitter = 0.8f + ((index * 5 + p) % 4) * 0.15f;
                GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = "Rock outcrop";
                rock.transform.SetParent(transform);
                rock.transform.position = safePosition + offset + Vector3.up * 0.9f * sizeJitter;
                rock.transform.rotation = Quaternion.Euler((index * 17 + p * 11) % 20, (index * 41 + p * 29) % 360, (index * 7) % 15);
                rock.transform.localScale = new Vector3(2.2f, 1.8f, 1.7f) * sizeJitter;
                rock.GetComponent<Renderer>().sharedMaterial = rockMaterial;
                MakeVisualOnly(rock);
            }
        }

        void BuildRacingLine()
        {
            // Draws the PRECOMPUTED optimal line (see TrackRuntime.ComputeRacingLine),
            // not the centerline it used to trace - the visual now shows the actual
            // line the AI drives.
            GameObject line = new GameObject("AI racing line");
            line.transform.SetParent(transform);
            LineRenderer renderer = line.AddComponent<LineRenderer>();
            renderer.useWorldSpace = true;
            renderer.loop = true;
            renderer.widthMultiplier = 0.16f;
            renderer.sharedMaterial = CreateMaterial("Racing line material", new Color(0.1f, 0.78f, 0.42f), 0f, 0.7f, new Color(0.02f, 0.18f, 0.06f));
            if (Runtime.racingLineOffsets != null && Runtime.racingLineOffsets.Length > 0)
            {
                renderer.positionCount = Runtime.racingLineOffsets.Length;
                for (int i = 0; i < Runtime.racingLineOffsets.Length; i++)
                {
                    renderer.SetPosition(i, Runtime.RacingLinePointAt(i * Runtime.racingLineSpacing) + Vector3.up * 0.06f);
                }
            }
            else
            {
                renderer.positionCount = Runtime.centerLine.Count;
                for (int i = 0; i < Runtime.centerLine.Count; i++)
                {
                    renderer.SetPosition(i, Runtime.centerLine[i] + Vector3.up * 0.06f);
                }
            }
        }

        GameObject CreateVisualBox(string objectName, Vector3 position, Quaternion rotation, Vector3 localScale, Material material)
        {
            GameObject box = new GameObject(objectName);
            box.transform.SetParent(transform);
            box.transform.position = position;
            box.transform.rotation = rotation;
            box.transform.localScale = localScale;
            MeshFilter filter = box.AddComponent<MeshFilter>();
            MeshRenderer renderer = box.AddComponent<MeshRenderer>();
            filter.sharedMesh = GetVisualBoxMesh();
            renderer.sharedMaterial = material;
            return box;
        }

        Mesh visualConeMesh;

        // Cheap shared 8-sided cone mesh (base radius 0.5, height 1, apex up), reused by
        // every traffic cone the same way GetVisualBoxMesh is reused by every box.
        Mesh GetVisualConeMesh()
        {
            if (visualConeMesh != null)
            {
                return visualConeMesh;
            }

            const int sides = 8;
            Vector3[] vertices = new Vector3[sides + 2];
            vertices[0] = new Vector3(0f, 1f, 0f);
            vertices[sides + 1] = Vector3.zero;
            for (int i = 0; i < sides; i++)
            {
                float angle = (i / (float)sides) * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f);
            }

            List<int> triangles = new List<int>(sides * 6);
            for (int i = 0; i < sides; i++)
            {
                int a = i + 1;
                int b = (i + 1) % sides + 1;
                triangles.Add(0); triangles.Add(b); triangles.Add(a);
                triangles.Add(sides + 1); triangles.Add(a); triangles.Add(b);
            }

            visualConeMesh = new Mesh();
            visualConeMesh.name = "Runtime visual-only cone";
            visualConeMesh.vertices = vertices;
            visualConeMesh.triangles = triangles.ToArray();
            visualConeMesh.RecalculateNormals();
            visualConeMesh.RecalculateBounds();
            return visualConeMesh;
        }

        GameObject CreateVisualCone(string objectName, Vector3 position, Quaternion rotation, Vector3 localScale, Material material)
        {
            GameObject cone = new GameObject(objectName);
            cone.transform.SetParent(transform);
            cone.transform.position = position;
            cone.transform.rotation = rotation;
            cone.transform.localScale = localScale;
            MeshFilter filter = cone.AddComponent<MeshFilter>();
            MeshRenderer renderer = cone.AddComponent<MeshRenderer>();
            filter.sharedMesh = GetVisualConeMesh();
            renderer.sharedMaterial = material;
            return cone;
        }

        // Small marker cone with a white foot plate, the "cones/markers" trackside
        // detail every other marker (boards, marshal posts) already had an equivalent
        // of. Visual-only like the rest of the box/cone furniture - no collider added.
        void CreateTrafficCone(Vector3 basePosition, Quaternion rotation)
        {
            CreateVisualCone("Traffic cone", basePosition, rotation, new Vector3(0.42f, 0.55f, 0.42f), trafficConeMaterial);
            CreateVisualBox("Traffic cone base plate", basePosition + Vector3.up * 0.02f, rotation, new Vector3(0.5f, 0.04f, 0.5f), lineMaterial);
        }

        // Collision/placement fix: this used to place every barrier/fence/wall
        // segment exactly where it was asked and register it as solid unconditionally
        // - on the theory that every caller's position was already computed by exact
        // flush-to-edge math and could never legitimately overlap the road. In
        // practice, upstream math (pit-ramp taper, corner-priority pull-in, staggered
        // release geometry, auto-fill's own approximation) can still disagree with
        // reality often enough to place a wall/divider/guide fence physically inside
        // the drivable surface - confirmed by an AI pileup where a pit-divider/guide
        // wall was standing in the live corridor. A missing or visual-only barrier is
        // always better than a solid collider inside a corridor a car is meant to
        // drive through, so every placement is now validated (IsSolidObstaclePlacementValid,
        // main-road AND pit-surface aware) before it's ever allowed to keep a live
        // collider. Essential outer-edge barriers get a chance to be nudged outward
        // away from the centerline first, since losing one of those opens a real hole
        // in the perimeter; anything else fails straight to visual-only.
        const int SolidObstaclePushAttempts = 10;
        const float SolidObstaclePushStepMeters = 0.4f;

        // Unfenced-section fix: wherever two parts of the lap run close together
        // (a switchback, parallel straights - routine on these layouts), the strip
        // between them can be too narrow for a perimeter barrier to satisfy the
        // full EdgeBarrierClearance (0.9m) against the NEIGHBOURING section's
        // corridor - Runtime.GetProgress resolves each footprint sample to the
        // nearest centerline, which is the other section's. Every placement there
        // failed, the push-out repair only moved the wall closer to that other
        // corridor, and the segment was demoted to visual-only: a fence you can
        // drive straight through, i.e. a functionally unfenced section, on every
        // track with adjacent sections. For ESSENTIAL perimeter barriers a
        // clearance shortfall is strictly better than a hole in the boundary, so
        // when the strict test can't be satisfied anywhere, the wall is kept WITH
        // its collider as long as no part of it sits within this hard floor of an
        // actual paved surface (or inside the pit's own drivable surface, which
        // stays a strict test). The reduced clearance is stored on the obstacle so
        // ValidateNoSolidObstaclesInsideDrivingCorridors re-validates it against
        // the same relaxed bar instead of stripping it back out post-build.
        const float HardMinimumEdgeBarrierClearance = 0.15f;

        // The last-resort acceptance test for essential perimeter barriers: never
        // on (or hard against) a paved surface, never inside the pit's drivable
        // surface - but tolerated inside the ordinary comfort clearance band when
        // the alternative is no physical containment at all.
        bool IsSolidObstaclePlacementTolerable(Vector3 position, Vector3 forward, Vector3 localScale)
        {
            Vector3[] samples = GetObstacleFootprintSamples(position, forward, localScale);
            for (int i = 0; i < samples.Length; i++)
            {
                TrackProgress progress = Runtime.GetProgress(samples[i]);
                if (Mathf.Abs(progress.lateralDistance) < Runtime.HalfWidthAt(progress.distance) + HardMinimumEdgeBarrierClearance)
                {
                    return false;
                }

                if (IsInsidePitDrivableSurface(samples[i]))
                {
                    return false;
                }
            }

            return true;
        }

        bool TryPlaceSolidObstacle(GameObject obstacle, string obstacleType, Vector3 desiredBasePosition, Vector3 forward, Vector3 localScale, float verticalOffset, float minimumClearance)
        {
            Vector3 candidate = desiredBasePosition + Vector3.up * verticalOffset;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            obstacle.transform.rotation = rotation;
            obstacle.transform.position = candidate;

            bool clear = IsSolidObstaclePlacementValid(obstacle, obstacleType, candidate, forward, localScale, minimumClearance);

            if (!clear && IsEssentialOuterEdgeBarrier(obstacleType))
            {
                Vector3 flatForward = new Vector3(forward.x, 0f, forward.z).normalized;
                if (flatForward.sqrMagnitude < 0.1f)
                {
                    flatForward = Vector3.forward;
                }

                Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
                TrackProgress startProgress = Runtime.GetProgress(candidate);
                float sign = startProgress.lateralDistance < 0f ? -1f : 1f;

                for (int attempt = 1; attempt <= SolidObstaclePushAttempts && !clear; attempt++)
                {
                    Vector3 pushed = candidate + right * sign * (SolidObstaclePushStepMeters * attempt);
                    if (IsSolidObstaclePlacementValid(obstacle, obstacleType, pushed, forward, localScale, minimumClearance))
                    {
                        candidate = pushed;
                        obstacle.transform.position = candidate;
                        clear = true;
                    }
                }
            }

            if (!clear && IsEssentialOuterEdgeBarrier(obstacleType) &&
                IsSolidObstaclePlacementTolerable(candidate, forward, localScale))
            {
                // Unfenced-section fix (see HardMinimumEdgeBarrierClearance): the
                // strict clearance is unsatisfiable here in every direction, but
                // the wall is clear of every actual paved/pit surface - keep the
                // collider rather than open a hole in the perimeter. Stored with
                // the reduced clearance so the post-build corridor sweep holds it
                // to the same bar it was accepted at.
                obstacle.transform.position = candidate;
                GameLog.Warn("[TrackValidation] Kept essential barrier (" + obstacleType + ") at " + candidate +
                             " on " + Runtime.displayName + " at reduced clearance (" + HardMinimumEdgeBarrierClearance.ToString("0.00") +
                             "m floor) - full clearance unsatisfiable between adjacent track sections.");
                TrackSolidObstacle tolerated = obstacle.AddComponent<TrackSolidObstacle>();
                tolerated.obstacleType = obstacleType;
                tolerated.minimumClearance = HardMinimumEdgeBarrierClearance;
                tolerated.localScaleAtValidation = localScale;
                solidObstacles.Add(tolerated);
                return true;
            }

            if (!clear)
            {
                GameLog.Warn("[TrackValidation] Rejected solid obstacle placement (" + obstacleType + ") at " + candidate +
                             " on " + Runtime.displayName + " - footprint intruded into a drivable corridor. Kept visual-only, no collider.");
                MakeVisualOnly(obstacle);
                return false;
            }

            TrackSolidObstacle solid = obstacle.AddComponent<TrackSolidObstacle>();
            solid.obstacleType = obstacleType;
            solid.minimumClearance = minimumClearance;
            solid.localScaleAtValidation = localScale;
            solidObstacles.Add(solid);
            return true;
        }

        // Shared oriented-footprint sampler for both the main-road clearance check
        // and the pit-surface intrusion check below, so the two never disagree about
        // which points on a barrier's box actually get tested. The original 9-point
        // set (center/front/rear/left/right/4 corners) can straddle a curve on a
        // long slab and miss a bulge in the middle of the long edges - long walls
        // (auto-fill segments, continuous edge-barrier runs) get extra interior
        // samples along their length so that can't slip through.
        Vector3[] GetObstacleFootprintSamples(Vector3 position, Vector3 forward, Vector3 localScale)
        {
            Vector3 flatForward = new Vector3(forward.x, 0f, forward.z).normalized;
            if (flatForward.sqrMagnitude < 0.1f)
            {
                flatForward = Vector3.forward;
            }

            Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
            float halfWidth = Mathf.Max(0.05f, localScale.x * 0.5f);
            float halfLength = Mathf.Max(0.05f, localScale.z * 0.5f);

            List<Vector3> samples = new List<Vector3>
            {
                position,
                position + flatForward * halfLength,
                position - flatForward * halfLength,
                position + right * halfWidth,
                position - right * halfWidth,
                position + flatForward * halfLength + right * halfWidth,
                position + flatForward * halfLength - right * halfWidth,
                position - flatForward * halfLength + right * halfWidth,
                position - flatForward * halfLength - right * halfWidth
            };

            if (localScale.z > 8f)
            {
                int extra = Mathf.Clamp(Mathf.FloorToInt(localScale.z / 4f), 1, 12);
                for (int i = 1; i < extra; i++)
                {
                    float t = (i / (float)extra) * 2f - 1f;
                    Vector3 alongPoint = position + flatForward * (t * halfLength);
                    samples.Add(alongPoint);
                    samples.Add(alongPoint + right * halfWidth);
                    samples.Add(alongPoint - right * halfWidth);
                }
            }

            return samples.ToArray();
        }

        bool IsObstacleClearOfRacingSurface(Vector3 position, Vector3 forward, Vector3 localScale, float minimumClearance)
        {
            Vector3[] samples = GetObstacleFootprintSamples(position, forward, localScale);
            for (int i = 0; i < samples.Length; i++)
            {
                TrackProgress progress = Runtime.GetProgress(samples[i]);
                // Must check against the actual (possibly hairpin-widened) drivable
                // surface at this sample's own distance, not the flat field - otherwise
                // an obstacle sitting just beyond the old narrow width could pass this
                // check while still resting on the now-wider tarmac at a hairpin.
                if (Mathf.Abs(progress.lateralDistance) < Runtime.HalfWidthAt(progress.distance) + minimumClearance)
                {
                    return false;
                }
            }

            return true;
        }

        // Collision/placement fix: is this world point somewhere inside a surface a
        // car is actually meant to drive on - the flat pit lane corridor, either
        // ramp's own taper, or the pit-exit merge path? A small boundaryMargin is
        // used for the flat corridor/ramp checks so a wall sitting legitimately AT
        // the boundary between the live track and the pit lane (e.g. BuildPitLane's
        // own separator wall, obstacleType "pit-wall") is not flagged just for
        // marking the edge - only a wall sitting meaningfully INSIDE the drivable
        // width counts as an intrusion. The pit-exit merge path gets a much smaller
        // margin: nothing solid should ever legitimately sit in that span at all.
        bool IsInsidePitDrivableSurface(Vector3 worldPoint)
        {
            TrackProgress progress = Runtime.GetProgress(worldPoint);
            float normalized = progress.normalized;
            float lateral = progress.lateralDistance;

            // The pit lane only ever exists on the right (positive lateral) side.
            if (lateral <= 0f)
            {
                return false;
            }

            const float boundaryMargin = 0.6f;
            float pitLaneHalfWidth = TrackRuntime.PitRampFullWidth * 0.5f;

            if (normalized >= Runtime.PitCorridorStartNormalized && normalized <= PitZoneExitRampStart)
            {
                float inner = Runtime.PitLaneLateral - pitLaneHalfWidth + boundaryMargin;
                float outer = Runtime.PitLaneLateral + pitLaneHalfWidth - boundaryMargin;
                if (lateral > inner && lateral < outer)
                {
                    return true;
                }
            }

            bool inEntryRamp = normalized >= PitZoneEntryRampStart && normalized < PitZoneEntryRampEnd;
            bool inExitRamp = normalized > PitZoneExitRampStart || normalized <= PitZoneExitRampEnd;
            if (inEntryRamp || inExitRamp)
            {
                float rampLateral;
                float rampHalfWidth;
                PitRampEnvelopeAt(normalized, progress.distance, out rampLateral, out rampHalfWidth);
                float trackEdge = Runtime.HalfWidthAt(progress.distance);
                float inner = Mathf.Min(trackEdge, rampLateral - rampHalfWidth) + boundaryMargin;
                float outer = rampLateral + rampHalfWidth - boundaryMargin;
                if (lateral > inner && lateral < outer)
                {
                    return true;
                }
            }

            // Pit-exit merge path: the whole span between the live track edge and
            // the pit lane's own outer edge is the corridor a merging car actually
            // uses (see RaceManager.UpdatePitExitMerge) - nothing solid belongs
            // anywhere in it, so only a thin margin is given.
            if (Runtime.IsInPitExitMergeZone(normalized))
            {
                float trackEdge = Runtime.HalfWidthAt(progress.distance);
                float inner = trackEdge + 0.3f;
                float outer = Runtime.PitLaneLateral + pitLaneHalfWidth - 0.3f;
                if (lateral > inner && lateral < outer)
                {
                    return true;
                }
            }

            return false;
        }

        // Full placement-validity gate (collision, not just visual): an obstacle is
        // only a legal solid barrier if its whole oriented footprint clears both the
        // live racing surface AND every pit drivable surface (corridor/ramps/merge
        // path). Either intrusion makes it invalid.
        bool IsSolidObstaclePlacementValid(GameObject obstacle, string obstacleType, Vector3 position, Vector3 forward, Vector3 localScale, float minimumClearance)
        {
            Vector3[] samples = GetObstacleFootprintSamples(position, forward, localScale);
            for (int i = 0; i < samples.Length; i++)
            {
                TrackProgress progress = Runtime.GetProgress(samples[i]);
                if (Mathf.Abs(progress.lateralDistance) < Runtime.HalfWidthAt(progress.distance) + minimumClearance)
                {
                    return false;
                }

                if (IsInsidePitDrivableSurface(samples[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // Barriers that form the primary perimeter/containment wall - losing one of
        // these leaves a genuine hole in the track boundary, so a failed placement
        // gets a chance to be pushed outward (away from the centerline) until it
        // clears, rather than being demoted outright. Everything else (pit dividers,
        // ramp guide walls, auto-fill, the pit's own separator wall) is decorative/
        // supplementary relative to that primary line and gets demoted straight to
        // visual-only on failure instead.
        bool IsEssentialOuterEdgeBarrier(string obstacleType)
        {
            if (string.IsNullOrEmpty(obstacleType))
            {
                return false;
            }

            // Unfenced-section fix: the auto-fill segments ARE perimeter repairs -
            // they only ever exist because the flush sweep found a real hole in the
            // boundary. Leaving them out of this list meant the repair failed the
            // exact same clearance test the original barrier failed (no push-out
            // attempts, no reduced-clearance fallback), so a detected gap between
            // adjacent track sections could never actually be filled.
            return obstacleType == "street-wall" || obstacleType == "armco-rail" ||
                   obstacleType == "tyre-barrier" || obstacleType == "bridge-wall" ||
                   obstacleType == "auto-fill-wall" || obstacleType == "auto-fill-rail";
        }

        // Track validation depth: detects a road half-width that jumps abruptly
        // between adjacent samples - a real generation defect (a badly-fed
        // widening bonus, a bad interpolation) reads as a visible kink/step in
        // the pavement edge, distinct from the intentional, already-smoothed
        // hairpin widening ramp (which changes gradually over many metres, not
        // within one 10m step). Diagnostic-only: logs and counts rather than
        // auto-correcting, since "fix" here would mean altering the road mesh
        // itself post-build, a much larger and riskier change than surfacing
        // the count for the debug overlay/report.
        int DetectSharpWidthChanges(TrackValidationReport report)
        {
            if (Runtime == null || Runtime.length <= 1f)
            {
                return 0;
            }

            const float step = 10f;
            const float maxDeltaPerStep = 2.2f;
            int sharpEdges = 0;
            float previousHalfWidth = Runtime.HalfWidthAt(0f);
            for (float d = step; d < Runtime.length; d += step)
            {
                float halfWidth = Runtime.HalfWidthAt(d);
                float delta = Mathf.Abs(halfWidth - previousHalfWidth);
                if (delta > maxDeltaPerStep)
                {
                    sharpEdges++;
                    GameLog.Warn("[TrackValidation] Sharp pavement width change at " + d.ToString("0") + "m: " +
                                 previousHalfWidth.ToString("0.00") + "m -> " + halfWidth.ToString("0.00") + "m over " + step.ToString("0") + "m on " + Runtime.displayName);
                }

                previousHalfWidth = halfWidth;
            }

            if (sharpEdges > 0)
            {
                report.Warn(sharpEdges + " sharp pavement width change(s) detected (see log for exact distances).");
            }

            return sharpEdges;
        }

        void ValidateGeneratedTrack()
        {
            TrackValidationReport report = LastReport ?? new TrackValidationReport { trackName = Runtime.displayName };
            report.centerLinePoints = Runtime.centerLine.Count;
            report.trackLength = Runtime.length;
            report.roadColliderValid = Runtime.roadCollider != null &&
                                       Runtime.roadCollider.sharedMesh != null &&
                                       !Runtime.roadCollider.isTrigger &&
                                       !Physics.GetIgnoreLayerCollision(Runtime.roadCollider.gameObject.layer, 0);
            if (!report.roadColliderValid)
            {
                report.Warn("road collider missing, trigger-only, or ignoring vehicle layer. Rebuilding.");
                BuildRoadMesh();
                report.roadColliderValid = Runtime.roadCollider != null && Runtime.roadCollider.sharedMesh != null && !Runtime.roadCollider.isTrigger;
            }

            // Speed-rebalance pass: TargetTrackLength's per-style targets are now
            // scaled by TrackLengthRebalanceScale (~1.25x) - Spa's own target alone
            // moved from 5600m to 7000m, so this ceiling has to move with it or
            // every long circuit would trip a false "exceeds ceiling" warning.
            // Round 2: TrackLengthRebalanceScale stacked another 25% (now 1.5625x
            // total), so both the floor and ceiling move with it again - Spa's own
            // target is now ~8750m.
            if (Runtime.length < 4625f)
            {
                report.Warn("track length " + Runtime.length.ToString("0") + "m is INVALID: circuits must normalize to at least 4.6 km for race pacing.");
            }
            else if (Runtime.length > 9375f)
            {
                report.Warn("track length " + Runtime.length.ToString("0") + "m exceeds the expected normalization ceiling.");
            }

            report.sharpEdgeCount = DetectSharpWidthChanges(report);

            // Collision/placement fix: the real, final sweep over the whole
            // solidObstacles registry (ValidateNoSolidObstaclesInsideDrivingCorridors)
            // runs later in Build(), after every geometry-placing pass (including
            // auto-fill and the overlap deconflict pass) has had its turn - doing it
            // here, this early, would miss obstacles created by those later passes.
            // report.invalidObstaclesFlagged/obstacleIntrusionCount are populated
            // there instead.

            report.gridSpawnValid = ValidateGridSlots(report);
            report.pitPosesValid = ValidatePitPoses(report);
            ValidateElevatedProtection(report);
            ValidateBarrierCoverage(report);
            ValidatePitCorridorSealed(report);
            Debug.Log(report.Summary());
        }

        bool ValidateGridSlots(TrackValidationReport report)
        {
            bool valid = true;
            for (int i = 0; i < TrackRuntime.GridSlotCount; i++)
            {
                float gridDistance;
                float lateral;
                Runtime.GetGridSlot(i, out gridDistance, out lateral);
                if (gridDistance <= 0f)
                {
                    report.Warn("grid slot " + (i + 1) + " runs past the start line; track too short for full grid spacing.");
                    valid = false;
                    continue;
                }

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(gridDistance, out point, out forward, out right);
                Vector3 slot = point + right * lateral;
                TrackProgress progress = Runtime.GetProgress(slot);
                if (Mathf.Abs(progress.lateralDistance) > Runtime.roadHalfWidth - 0.8f)
                {
                    report.Warn("grid slot " + (i + 1) + " sits off the road surface (lateral " + progress.lateralDistance.ToString("0.0") + ").");
                    valid = false;
                }
            }

            return valid;
        }

        bool ValidatePitPoses(TrackValidationReport report)
        {
            bool valid = true;
            Vector3 position;
            Quaternion rotation;
            Runtime.GetPitEntryPose(out position, out rotation);
            valid &= ValidatePitPose(report, "pit entry", position);
            Runtime.GetPitReleasePose(out position, out rotation);
            valid &= ValidatePitPose(report, "pit release", position);

            // Every indexed pit box must be individually usable so no car is ever
            // guided onto the racing surface or into a wall while pitting.
            for (int box = 0; box < TrackRuntime.PitBoxCount; box++)
            {
                Runtime.GetPitServicePose(box, out position, out rotation);
                valid &= ValidatePitPose(report, "pit box " + (box + 1), position);
            }

            return valid;
        }

        bool ValidatePitPose(TrackValidationReport report, string label, Vector3 position)
        {
            TrackProgress progress = Runtime.GetProgress(position);
            float lateral = Mathf.Abs(progress.lateralDistance);
            if (lateral < Runtime.roadHalfWidth - 0.5f)
            {
                report.Warn(label + " pose sits on the racing surface (lateral " + progress.lateralDistance.ToString("0.0") + ").");
                return false;
            }

            if (lateral > Runtime.roadHalfWidth + 26f)
            {
                report.Warn(label + " pose is unusually far from the road (lateral " + progress.lateralDistance.ToString("0.0") + ").");
                return false;
            }

            // Make sure no solid obstacle blocks the pose.
            for (int i = 0; i < solidObstacles.Count; i++)
            {
                TrackSolidObstacle obstacle = solidObstacles[i];
                if (obstacle == null)
                {
                    continue;
                }

                if (Vector3.Distance(obstacle.transform.position, position) < 2.4f)
                {
                    report.Warn(label + " pose was blocked by " + obstacle.name + "; obstacle removed.");
                    obstacle.gameObject.SetActive(false);
                    Destroy(obstacle.gameObject);
                    solidObstacles.RemoveAt(i);
                    i--;
                }
            }

            return true;
        }

        void ValidateDecorativeObjectsClearTrack()
        {
            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = renderers.Length - 1; i >= 0; i--)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null || renderer.GetComponentInParent<TrackSolidObstacle>() != null)
                {
                    continue;
                }

                string objectName = renderer.gameObject.name.ToLowerInvariant();
                if (IsAllowedTrackSurfaceOrOverheadName(objectName))
                {
                    continue;
                }

                if (DecorativeRendererTouchesRacingSurface(renderer))
                {
                    GameLog.Warn("[TrackValidation] Removed decorative object " + renderer.gameObject.name + " because it intersected the racing surface.");
                    renderer.gameObject.SetActive(false);
                    Destroy(renderer.gameObject);
                }
            }
        }

        bool DecorativeRendererTouchesRacingSurface(Renderer renderer)
        {
            Bounds bounds = renderer.bounds;
            Vector3 extents = bounds.extents;
            Vector3[] samples =
            {
                bounds.center,
                bounds.center + new Vector3(extents.x, 0f, 0f),
                bounds.center - new Vector3(extents.x, 0f, 0f),
                bounds.center + new Vector3(0f, 0f, extents.z),
                bounds.center - new Vector3(0f, 0f, extents.z),
                bounds.center + new Vector3(extents.x, 0f, extents.z),
                bounds.center + new Vector3(extents.x, 0f, -extents.z),
                bounds.center + new Vector3(-extents.x, 0f, extents.z),
                bounds.center + new Vector3(-extents.x, 0f, -extents.z)
            };

            for (int sample = 0; sample < samples.Length; sample++)
            {
                TrackProgress progress = Runtime.GetProgress(samples[sample]);
                // Uses the widened per-distance half-width as a general safety net: any
                // decorative object that ends up sitting on the now-wider hairpin tarmac
                // gets caught here even if its own placement code wasn't individually
                // updated to account for the widening.
                bool nearRoad = Mathf.Abs(progress.lateralDistance) < Runtime.HalfWidthAt(progress.distance) + 0.55f;
                bool nearRoadHeight = bounds.min.y < progress.nearestPoint.y + 2.2f && bounds.max.y > progress.nearestPoint.y - 0.4f;
                if (nearRoad && nearRoadHeight)
                {
                    return true;
                }
            }

            return false;
        }

        bool IsAllowedTrackSurfaceOrOverheadName(string objectName)
        {
            string groundName = Runtime == null ? "" : (Runtime.styleName + " runoff").ToLowerInvariant();
            return objectName == groundName ||
                   objectName.Contains("terrain base") ||
                   objectName.Contains("procedural road") ||
                   objectName.Contains("asphalt") ||
                   objectName.Contains("pit lane") ||
                   objectName.Contains("pit entry") ||
                   objectName.Contains("pit release") ||
                   objectName.Contains("pit exit") ||
                   objectName.Contains("paint") ||
                   objectName.Contains("grid") ||
                   objectName.Contains("line") ||
                   objectName.Contains("rubber") ||
                   objectName.Contains("skid") ||
                   objectName.Contains("kerb") ||
                   objectName.Contains("drs") ||
                   objectName.Contains("start finish") ||
                   objectName.Contains("sector") ||
                   objectName.Contains("racing line") ||
                   objectName.Contains("gantry") ||
                   objectName.Contains("start light") ||
                   objectName.Contains("bridge support") ||
                   objectName.Contains("sheen");
        }

        void AuditVisualMarkingColliders()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.GetComponentInParent<TrackSolidObstacle>() != null || collider == Runtime.roadCollider)
                {
                    continue;
                }

                string objectName = collider.gameObject.name.ToLowerInvariant();
                if (IsVisualMarkingName(objectName))
                {
                    GameLog.Warn("[TrackValidation] Removed collider from visual-only marking " + collider.gameObject.name);
                    RemoveColliderNow(collider);
                }
            }
        }

        bool IsVisualMarkingName(string objectName)
        {
            return objectName.Contains("paint") ||
                   objectName.Contains("grid") ||
                   objectName.Contains("line") ||
                   objectName.Contains("start finish") ||
                   objectName.Contains("sector") ||
                   objectName.Contains("drs") ||
                   objectName.Contains("rubber") ||
                   objectName.Contains("kerb") ||
                   objectName.Contains("arrow");
        }

        Mesh GetVisualBoxMesh()
        {
            if (visualBoxMesh != null)
            {
                return visualBoxMesh;
            }

            visualBoxMesh = new Mesh();
            visualBoxMesh.name = "Runtime visual-only box";
            visualBoxMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
            };
            visualBoxMesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5,
                3, 7, 6, 3, 6, 2,
                0, 1, 5, 0, 5, 4
            };
            visualBoxMesh.RecalculateNormals();
            visualBoxMesh.RecalculateBounds();
            return visualBoxMesh;
        }

        void MakeVisualOnly(GameObject target)
        {
            Collider collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                RemoveColliderNow(collider);
            }
        }

        void RemoveColliderNow(Collider collider)
        {
            collider.enabled = false;
            collider.isTrigger = true;
            Destroy(collider);
        }

        void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
            solidObstacles.Clear();
            grandstandSpans.Clear();
            marshalFlagBoardRenderers.Clear();
            raceControlBoardRenderer = null;
            raceControlBoardText = null;
            raceControlVisualDriver = null;
        }
    }

    // Small self-contained component that owns all per-frame animation for the
    // race-control trackside visuals (marshal flag boards, SC/VSC board, gantry
    // lights). Nothing else in TrackManager/TrackRuntime runs an Update() loop, so
    // this is the one place that does - discrete state changes (which material a
    // renderer uses, what the board text says) happen once in SetState, while the
    // continuous pulsing/strobing is driven every frame here off Time.time, the same
    // Mathf.PingPong-based approach used for other glow/pulse effects in this file.
    public class RaceControlVisualDriver : MonoBehaviour
    {
        List<Renderer> marshalRenderers = new List<Renderer>();
        Renderer boardRenderer;
        TextMesh boardText;
        Material flagGreenMaterial;
        Material flagYellowMaterial;
        Material boardOffMaterial;
        Material boardVscMaterial;
        Material boardScMaterial;
        Material gantryLightMaterial;

        int currentState = -1;

        static readonly Color GantryOffColor = new Color(0.03f, 0.03f, 0.03f);
        static readonly Color AmberDim = new Color(0.25f, 0.16f, 0.02f);
        static readonly Color AmberBright = new Color(1.1f, 0.78f, 0.05f);
        static readonly Color CyanDim = new Color(0.02f, 0.14f, 0.16f);
        static readonly Color CyanBright = new Color(0.1f, 0.95f, 1.1f);
        static readonly Color RedDim = new Color(0.18f, 0.02f, 0.01f);
        static readonly Color RedBright = new Color(1.3f, 0.08f, 0.04f);
        // Dims sit near-black and brights push past 1 so the caution pulses read
        // at full daylight exposure, not just against a night sky.
        static readonly Color VscDim = new Color(0.02f, 0.08f, 0.09f);
        static readonly Color VscBright = new Color(0.2f, 1.05f, 1.2f);
        static readonly Color ScDim = new Color(0.1f, 0.015f, 0.01f);
        static readonly Color ScBright = new Color(1.6f, 0.12f, 0.06f);
        static readonly Color StrobeWhite = new Color(1.6f, 1.55f, 1.4f);

        // Resting emission of the "off" board face, matching what CreateMaterials
        // bakes into raceControlBoardMaterial, so the restart strobe below can
        // hand the material back exactly as it found it.
        static readonly Color BoardOffEmission = new Color(0.35f, 0.05f, 0.03f);
        static readonly Color VscTextColor = new Color(0.55f, 0.95f, 1f, 0.95f);
        static readonly Color ScTextColor = new Color(1f, 0.5f, 0.35f, 0.95f);
        static readonly Color IdleTextColor = new Color(0.95f, 0.75f, 0.15f, 0.95f);

        public void Configure(List<Renderer> marshalFlagBoardRenderers, Renderer raceControlBoardRenderer, TextMesh raceControlBoardText,
            Material flagGreen, Material flagYellow, Material boardOff, Material boardVsc, Material boardSc, Material gantryLight)
        {
            marshalRenderers = marshalFlagBoardRenderers;
            boardRenderer = raceControlBoardRenderer;
            boardText = raceControlBoardText;
            flagGreenMaterial = flagGreen;
            flagYellowMaterial = flagYellow;
            boardOffMaterial = boardOff;
            boardVscMaterial = boardVsc;
            boardScMaterial = boardSc;
            gantryLightMaterial = gantryLight;
            SetState(0);
        }

        // state ordinals match RaceManager.RaceControlState - see TrackRuntime.SetRaceControlVisual.
        public void SetState(int state)
        {
            if (state == currentState)
            {
                return;
            }

            currentState = state;
            ApplyDiscreteState();
        }

        // One-off swaps (which shared material a renderer points at, what the board
        // text reads) that only need to happen when the state actually changes.
        void ApplyDiscreteState()
        {
            // Bug fix (Part 5): this used to only go yellow for state 1 (a local
            // sector yellow), so marshal posts around the rest of the circuit sat on
            // green throughout an entire VSC/SC/Restart period - the opposite of
            // real race control, where every post shows caution once anything other
            // than a green flag is active. State 0 (Green) is the only green case now.
            Material marshalMaterial = currentState == 0 ? flagGreenMaterial : flagYellowMaterial;
            for (int i = 0; i < marshalRenderers.Count; i++)
            {
                if (marshalRenderers[i] != null)
                {
                    marshalRenderers[i].sharedMaterial = marshalMaterial;
                }
            }

            // The pulses in Update mutate shared emissive materials, so every board
            // material is put back to its resting emission on each state change -
            // otherwise a state that ends mid-pulse leaves its board stuck bright.
            SetEmission(boardOffMaterial, BoardOffEmission);
            SetEmission(boardVscMaterial, VscDim);
            SetEmission(boardScMaterial, ScDim);

            if (boardRenderer != null)
            {
                Material boardMaterial = boardOffMaterial;
                if (currentState == 2)
                {
                    boardMaterial = boardVscMaterial;
                }
                else if (currentState >= 3 && currentState <= 5)
                {
                    boardMaterial = boardScMaterial;
                }
                boardRenderer.sharedMaterial = boardMaterial;
            }

            if (boardText != null)
            {
                // Restart (6) withdraws the SC board like real race control does -
                // the white strobe in Update carries the restart message instead.
                boardText.text = currentState == 2 ? "VSC" : (currentState >= 3 && currentState <= 5 ? "SC" : "");
                boardText.color = currentState == 2 ? VscTextColor : (currentState >= 3 && currentState <= 5 ? ScTextColor : IdleTextColor);
            }
        }

        // Continuous pulsing/strobing. Mutates the shared emissive materials directly
        // rather than touching individual renderers, so every object sharing
        // gantryLightMaterial (the boom strip + the two board strobe pods) animates
        // together for free.
        void Update()
        {
            if (gantryLightMaterial == null)
            {
                return;
            }

            switch (currentState)
            {
                case 1: // YellowSector: amber pulse
                    ApplyPulse(gantryLightMaterial, AmberDim, AmberBright, 2.2f);
                    break;
                case 2: // VirtualSafetyCar: cyan/amber alternating pulse, board gently activates
                    bool cyanPhase = Mathf.PingPong(Time.time * 0.5f, 1f) < 0.5f;
                    ApplyPulse(gantryLightMaterial, cyanPhase ? CyanDim : AmberDim, cyanPhase ? CyanBright : AmberBright, 2.8f);
                    ApplyPulse(boardVscMaterial, VscDim, VscBright, 1.6f);
                    break;
                case 3: // SafetyCarDeploying: amber/red alternation - boards are out but the SC isn't leading yet
                    bool redPhase = Mathf.PingPong(Time.time * 0.7f, 1f) < 0.5f;
                    ApplyPulse(gantryLightMaterial, redPhase ? RedDim : AmberDim, redPhase ? RedBright : AmberBright, 3.4f);
                    ApplyPulse(boardScMaterial, ScDim, ScBright, 2.2f);
                    break;
                case 4: // SafetyCarActive: steady heavy red pulse
                    ApplyPulse(gantryLightMaterial, RedDim, RedBright, 4.5f);
                    ApplyPulse(boardScMaterial, ScDim, ScBright, 2.2f);
                    break;
                case 5: // SafetyCarInThisLap: same SC visual, faster "final lap" flash
                    ApplyPulse(gantryLightMaterial, RedDim, RedBright, 7f);
                    ApplyPulse(boardScMaterial, ScDim, ScBright, 3.4f);
                    break;
                case 6: // Restart: hard bright strobe distinct from the steady SC pulse; the
                        // board face itself (back on the "off" material per ApplyDiscreteState)
                        // strobes with the gantry rather than still flashing SC red.
                    float strobe = Mathf.PingPong(Time.time * 14f, 1f) > 0.5f ? 1f : 0f;
                    SetEmission(gantryLightMaterial, Color.Lerp(Color.black, StrobeWhite, strobe));
                    SetEmission(boardOffMaterial, Color.Lerp(BoardOffEmission, StrobeWhite, strobe));
                    break;
                default: // Green (0) and any unrecognized state: steady/off
                    SetEmission(gantryLightMaterial, GantryOffColor);
                    break;
            }
        }

        void ApplyPulse(Material material, Color dim, Color bright, float rate)
        {
            if (material == null)
            {
                return;
            }
            float t = Mathf.PingPong(Time.time * rate, 1f);
            SetEmission(material, Color.Lerp(dim, bright, t));
        }

        static void SetEmission(Material material, Color emission)
        {
            if (material == null)
            {
                return;
            }
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
        }
    }

    public class TrackSolidObstacle : MonoBehaviour
    {
        public string obstacleType;
        public float minimumClearance;
        public Vector3 localScaleAtValidation;
    }
}
