using System.Collections.Generic;
using UnityEngine;

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
        public float roadHalfWidth = 9f;
        public float kerbStart = 8.2f;
        public Vector2 drsZoneOne = new Vector2(0.13f, 0.29f);
        public Vector2 drsZoneTwo = new Vector2(0.64f, 0.82f);
        public WeatherState weather = WeatherState.Clear;
        public MeshCollider roadCollider;

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
            return normalizedProgress > PitApproachStartNormalized && normalizedProgress < 0.955f;
        }

        public bool IsInPitEntryZone(float normalizedProgress)
        {
            return normalizedProgress > 0.865f && normalizedProgress < 0.955f;
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
            return normalizedProgress > 0.985f || normalizedProgress < 0.018f;
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
        public const float HairpinExtraHalfWidth = 4.5f;
        public const float HairpinBlendDistance = 24f;

        readonly List<float> hairpinCenters = new List<float>();

        public IReadOnlyList<float> HairpinCenters { get { return hairpinCenters; } }

        // Recomputes the hairpin center list from the FINAL centerline (after layout
        // repair/scaling), so it stays in lock-step with cumulativeDistances. Call
        // once right after RecalculateDistances(); nothing else mutates centerLine
        // after that point during Build().
        public void RecalculateHairpinWidening()
        {
            hairpinCenters.Clear();
            if (centerLine.Count < 4)
            {
                return;
            }

            for (int i = 0; i < centerLine.Count; i++)
            {
                Vector3 previous = centerLine[(i - 1 + centerLine.Count) % centerLine.Count];
                Vector3 current = centerLine[i];
                Vector3 next = centerLine[(i + 1) % centerLine.Count];
                Vector3 entry = (current - previous).normalized;
                Vector3 exit = (next - current).normalized;
                float angle = Vector3.Angle(entry, exit);
                if (angle > HairpinCornerAngleThreshold)
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
            return roadHalfWidth + HairpinWidthBonus(distance);
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
        const float PitLaneStartNormalized = 0.9f;
        // Where the pit corridor's own drivable surface and outer wall begin (shared
        // with TrackManager's PitZoneEntryRampEnd/BuildPitLane so both classes agree
        // on the same seam instead of drifting apart behind two separate literals).
        public const float PitCorridorStartNormalized = 0.885f;
        // A held-back queue pose must never land before the pit corridor's own
        // surface begins - on the shortest calendar tracks PitLaneStartNormalized
        // leaves under 60m of margin ahead of PitCorridorStartNormalized, and the
        // caller's holdback can be that large, which used to push the queue point
        // off the built pit lane surface entirely.
        const float PitQueueCorridorMargin = 10f;

        public float PitLaneLateral
        {
            get { return roadHalfWidth + 9.2f; }
        }

        public float PitBoxDistance(int pitBoxIndex)
        {
            int index = Mathf.Clamp(pitBoxIndex, 0, PitBoxCount - 1);
            return length * PitLaneStartNormalized + index * PitBoxSpacing;
        }

        public void GetPitServicePose(int pitBoxIndex, out Vector3 position, out Quaternion rotation)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(PitBoxDistance(pitBoxIndex), out point, out forward, out right);
            position = point + right * PitLaneLateral + Vector3.up * 0.58f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void GetPitServicePose(out Vector3 position, out Quaternion rotation)
        {
            GetPitServicePose(0, out position, out rotation);
        }

        // Queue pose short of a pit box, used while a car waits for its slot or a
        // safe release gap so two cars are never guided into the same spot.
        public void GetPitQueuePose(int pitBoxIndex, float holdBackMeters, out Vector3 position, out Quaternion rotation)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            float minDistance = length * PitCorridorStartNormalized + PitQueueCorridorMargin;
            float desired = PitBoxDistance(pitBoxIndex) - Mathf.Max(4f, holdBackMeters);
            SampleAtDistance(Mathf.Max(minDistance, desired), out point, out forward, out right);
            position = point + right * PitLaneLateral + Vector3.up * 0.58f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
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

        public void GetPitEntryPose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(length * PitCorridorStartNormalized, out point, out forward, out right);
            position = point + right * (roadHalfWidth + 5.6f) + Vector3.up * 0.58f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void GetPitReleasePose(int staggerSlot, out Vector3 position, out Quaternion rotation)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(length * 0.992f - Mathf.Max(0, staggerSlot) * 7.5f, out point, out forward, out right);
            position = point + right * (roadHalfWidth + 4.8f) + Vector3.up * 0.62f;
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
        public int invalidObstaclesRemoved;
        public bool gridSpawnValid = true;
        public bool pitPosesValid = true;
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
                   " | obstaclesRemoved=" + invalidObstaclesRemoved +
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
        Material fenceMaterial;
        Material fencePostMaterial;
        Material foliageMaterial;
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
        Material edgeGlowMaterial;
        Material[] neonMaterials;
        Material yachtMaterial;
        Material toriiMaterial;
        Material windowStripMaterial;
        Material grandstandRoofMaterial;
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
            CreateMaterials();
            BuildGround();
            BuildContinuousSafetyFloor();
            BuildRoadMesh();
            BuildRoadPaint();
            BuildAsphaltDetail();
            BuildRubberBuildup();
            BuildGridPaint();
            BuildKerbs();
            BuildContinuousEdgeBarriers();
            BuildTrackMarkers();
            BuildDrsZoneBoards();
            BuildTimingGantries();
            BuildAdvertisingHoardings();
            BuildPitLane();
            BuildPitLaneDividerFence();
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
            ValidateSceneryGrounding();
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

            float corridorStart = Runtime.length * TrackRuntime.PitCorridorStartNormalized;
            float corridorEnd = Runtime.length * PitZoneExitRampStart;
            CreateBoundaryDebugLine(overlay.transform, "Pit lane inner edge", Color.yellow, d => Runtime.PitLaneLateral - 6.75f, corridorStart, corridorEnd);
            CreateBoundaryDebugLine(overlay.transform, "Pit lane outer edge", Color.yellow, d => Runtime.PitLaneLateral + 6.75f, corridorStart, corridorEnd);
            CreateBoundaryDebugLine(overlay.transform, "Pit/track separator", Color.green, d => Runtime.HalfWidthAt(d) + EdgeBarrierClearance, corridorStart, corridorEnd);

            // The two intentional perimeter openings, called out explicitly in a
            // distinct colour so they read as "meant to be here" rather than being
            // mistaken for one of the gap markers below.
            CreateBoundaryDebugLine(overlay.transform, "Pit entry opening (intentional)", new Color(1f, 0.55f, 0f), d => Runtime.HalfWidthAt(d) + EdgeBarrierClearance,
                Runtime.length * PitZoneEntryRampStart, Runtime.length * PitZoneEntryRampEnd);
            CreateBoundaryDebugLine(overlay.transform, "Pit exit opening (intentional)", new Color(1f, 0.55f, 0f), d => Runtime.HalfWidthAt(d) + EdgeBarrierClearance,
                Runtime.length * PitZoneExitRampStart, Runtime.length * PitZoneExitRampEnd);

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
                roadHalfWidth = 13.4f,
                kerbStart = 7.9f,
                weather = DetermineWeather(eventData == null ? "clear_hot" : eventData.weatherProfile)
            };

            AddLayoutPoints(runtime);
            runtime.RecalculateDistances();
            // Hairpin centers are derived from the FINAL, fully-repaired/scaled
            // centerline (AddLayoutPoints already ran repair + NormalizeTrackLength +
            // a second repair pass above), so this must run after that and after
            // RecalculateDistances so cumulativeDistances line up with centerLine.
            runtime.RecalculateHairpinWidening();
            return runtime;
        }

        void AddLayoutPoints(TrackRuntime runtime)
        {
            string id = string.IsNullOrEmpty(runtime.trackId) ? "bahrain_desert" : runtime.trackId;
            if (id.Contains("china"))
            {
                BuildChinaLayout(runtime);
            }
            else if (id.Contains("miami"))
            {
                BuildMiamiLayout(runtime);
            }
            else if (id.Contains("canada"))
            {
                BuildCanadaLayout(runtime);
            }
            else if (id.Contains("barcelona"))
            {
                BuildBarcelonaLayout(runtime);
            }
            else if (id.Contains("austria"))
            {
                BuildAustriaLayout(runtime);
            }
            else if (id.Contains("hungary"))
            {
                BuildHungaryLayout(runtime);
            }
            else if (id.Contains("zandvoort"))
            {
                BuildZandvoortLayout(runtime);
            }
            else if (id.Contains("madrid"))
            {
                BuildMadridLayout(runtime);
            }
            else if (id.Contains("baku"))
            {
                BuildBakuLayout(runtime);
            }
            else if (id.Contains("austin"))
            {
                BuildAustinLayout(runtime);
            }
            else if (id.Contains("mexico"))
            {
                BuildMexicoLayout(runtime);
            }
            else if (id.Contains("las_vegas"))
            {
                BuildLasVegasLayout(runtime);
            }
            else if (id.Contains("qatar"))
            {
                BuildQatarLayout(runtime);
            }
            else if (id.Contains("jeddah"))
            {
                BuildJeddahLayout(runtime);
            }
            else if (id.Contains("monaco"))
            {
                BuildMonacoLayout(runtime);
            }
            else if (id.Contains("suzuka"))
            {
                BuildSuzukaLayout(runtime);
            }
            else if (id.Contains("silverstone"))
            {
                BuildSilverstoneLayout(runtime);
            }
            else if (id.Contains("monza"))
            {
                BuildMonzaLayout(runtime);
            }
            else if (id.Contains("spa"))
            {
                BuildSpaLayout(runtime);
            }
            else if (id.Contains("singapore"))
            {
                BuildSingaporeLayout(runtime);
            }
            else if (id.Contains("melbourne"))
            {
                BuildMelbourneLayout(runtime);
            }
            else if (id.Contains("interlagos"))
            {
                BuildInterlagosLayout(runtime);
            }
            else if (id.Contains("abu_dhabi"))
            {
                BuildAbuDhabiLayout(runtime);
            }
            else
            {
                BuildBahrainLayout(runtime);
            }

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

        float TargetTrackLength(TrackRuntime runtime)
        {
            string id = runtime.trackId ?? "";
            string style = runtime.styleName ?? "";

            // High-speed / long circuits. Recalibrated back down from an earlier +50%
            // pass that made laps take too long - lands in the 4.8-5.6km band for a
            // ~1:15-1:30 lap instead of several minutes.
            if (id.Contains("spa"))
            {
                return 5600f;
            }

            if (id.Contains("monza") || id.Contains("silverstone") || id.Contains("jeddah") ||
                id.Contains("las_vegas") || id.Contains("baku") || id.Contains("qatar") || id.Contains("suzuka"))
            {
                return 5300f;
            }

            // Tight / street layouts stay shorter but never kart-track short.
            if (id.Contains("monaco"))
            {
                return 3900f;
            }

            if (id.Contains("singapore") || id.Contains("hungary") || id.Contains("zandvoort") ||
                id.Contains("interlagos") || id.Contains("madrid") || id.Contains("monaco") ||
                style.ToLowerInvariant().Contains("street"))
            {
                return 4200f;
            }

            // Standard circuits.
            return 4650f;
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

        void BuildBahrainLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Desert power braking";
            runtime.roadHalfWidth = 13.4f;
            runtime.kerbStart = 7.9f;
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

        void BuildJeddahLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Fast coastal street";
            runtime.roadHalfWidth = 12.8f;
            runtime.kerbStart = 7.5f;
            runtime.drsZoneOne = new Vector2(0.88f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.56f, 0.73f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(150f, 0f, 0f), new Vector3(238f, 0f, 24f),
                new Vector3(318f, 0f, 72f), new Vector3(336f, 0f, 122f), new Vector3(302f, 0f, 156f),
                new Vector3(226f, 0f, 168f), new Vector3(152f, 0f, 150f), new Vector3(92f, 0f, 172f),
                new Vector3(34f, 0f, 150f), new Vector3(-18f, 0f, 102f), new Vector3(-24f, 0f, 50f),
                new Vector3(-76f, 0f, 20f), new Vector3(-164f, 0f, 10f)
            }, 5);
        }

        void BuildMonacoLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Tight harbour street";
            runtime.roadHalfWidth = 10.8f;
            runtime.kerbStart = 6.3f;
            runtime.drsZoneOne = new Vector2(0.87f, 0.07f);
            runtime.drsZoneTwo = new Vector2(0.46f, 0.58f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(58f, 1.2f, 0f), new Vector3(82f, 4.5f, 30f),
                new Vector3(72f, 7.2f, 68f), new Vector3(36f, 8.4f, 92f), new Vector3(8f, 7.7f, 78f),
                new Vector3(-14f, 5.1f, 45f), new Vector3(-38f, 2.8f, 44f), new Vector3(-54f, 1.2f, 82f),
                new Vector3(-32f, 0.4f, 126f), new Vector3(24f, 0f, 138f), new Vector3(78f, 0f, 120f),
                new Vector3(94f, 0f, 76f), new Vector3(58f, 0f, 52f), new Vector3(14f, 0f, 38f),
                new Vector3(-52f, 0f, 12f), new Vector3(-104f, 0f, 4f)
            }, 3);
        }

        void BuildSuzukaLayout(TrackRuntime runtime)
        {
            // Rebuilt at the same world scale as every other layout in this file. The old
            // figure-eight anchors spanned ~3km, self-intersected at ground level, and broke
            // progress tracking, AI navigation, and object budgets.
            runtime.styleName = "Technical esses Park";
            runtime.roadHalfWidth = 13.2f;
            runtime.kerbStart = 7.7f;
            runtime.drsZoneOne = new Vector2(0.9f, 0.07f);
            runtime.drsZoneTwo = new Vector2(0.5f, 0.63f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(158f, 0f, 0f),
                new Vector3(216f, 1f, 30f), new Vector3(232f, 3f, 84f),
                new Vector3(186f, 4f, 120f), new Vector3(124f, 5f, 108f),
                new Vector3(92f, 6f, 148f), new Vector3(128f, 7f, 188f),
                new Vector3(188f, 7f, 212f), new Vector3(204f, 6f, 266f),
                new Vector3(156f, 5f, 300f), new Vector3(92f, 4f, 290f),
                new Vector3(56f, 3f, 326f), new Vector3(70f, 2f, 372f),
                new Vector3(108f, 1f, 394f), new Vector3(96f, 1f, 416f),
                new Vector3(30f, 1f, 404f), new Vector3(-70f, 0.5f, 368f),
                new Vector3(-108f, 0f, 296f),
                new Vector3(-86f, -1f, 228f), new Vector3(-118f, -1f, 156f),
                new Vector3(-90f, 0f, 86f), new Vector3(-150f, 0f, 12f)
            }, 5);
        }

        void BuildSilverstoneLayout(TrackRuntime runtime)
        {
            runtime.styleName = "High-speed airfield";
            runtime.roadHalfWidth = 15.4f;
            runtime.kerbStart = 9.0f;
            runtime.drsZoneOne = new Vector2(0.89f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.48f, 0.64f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(162f, 0f, 0f), new Vector3(230f, 0f, 36f),
                new Vector3(252f, 0f, 92f), new Vector3(206f, 0f, 146f), new Vector3(118f, 0f, 158f),
                new Vector3(42f, 0f, 132f), new Vector3(-18f, 0f, 158f), new Vector3(-88f, 0f, 134f),
                new Vector3(-116f, 0f, 82f), new Vector3(-76f, 0f, 42f), new Vector3(-14f, 0f, 52f),
                new Vector3(48f, 0f, 88f), new Vector3(120f, 0f, 80f), new Vector3(158f, 0f, 28f),
                new Vector3(82f, 0f, -22f), new Vector3(-148f, 0f, -8f)
            }, 5);
        }

        void BuildMonzaLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Low-downforce park";
            runtime.roadHalfWidth = 15.5f;
            runtime.kerbStart = 9.1f;
            runtime.drsZoneOne = new Vector2(0.88f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.44f, 0.62f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(230f, 0f, 0f), new Vector3(272f, 0f, 26f),
                new Vector3(246f, 0f, 58f), new Vector3(194f, 0f, 48f), new Vector3(238f, 0f, 92f),
                new Vector3(252f, 0f, 148f), new Vector3(196f, 0f, 184f), new Vector3(92f, 0f, 190f),
                new Vector3(20f, 0f, 164f), new Vector3(-42f, 0f, 174f), new Vector3(-86f, 0f, 132f),
                new Vector3(-48f, 0f, 86f), new Vector3(62f, 0f, 76f), new Vector3(112f, 0f, 42f),
                new Vector3(74f, 0f, 14f), new Vector3(-210f, 0f, 0f)
            }, 3);
        }

        void BuildSpaLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Long Ardennes elevation";
            runtime.roadHalfWidth = 14.8f;
            runtime.kerbStart = 8.7f;
            runtime.drsZoneOne = new Vector2(0.88f, 0.07f);
            runtime.drsZoneTwo = new Vector2(0.18f, 0.36f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(124f, 0.5f, 0f), new Vector3(170f, 4.5f, 34f),
                new Vector3(196f, 13f, 94f), new Vector3(260f, 19f, 142f), new Vector3(352f, 17f, 158f),
                new Vector3(414f, 10f, 122f), new Vector3(388f, 5f, 72f), new Vector3(302f, 2f, 72f),
                new Vector3(242f, -1f, 112f), new Vector3(164f, -4f, 126f), new Vector3(80f, -6f, 106f),
                new Vector3(26f, -8f, 146f), new Vector3(-54f, -7f, 126f), new Vector3(-104f, -4f, 70f),
                new Vector3(-84f, -1f, 22f), new Vector3(-162f, 0f, 4f)
            }, 5);
        }

        void BuildSingaporeLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Night street ninety";
            runtime.roadHalfWidth = 11.2f;
            runtime.kerbStart = 6.6f;
            runtime.drsZoneOne = new Vector2(0.88f, 0.07f);
            runtime.drsZoneTwo = new Vector2(0.55f, 0.69f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(108f, 0f, 0f), new Vector3(128f, 0f, 28f),
                new Vector3(96f, 0f, 54f), new Vector3(124f, 0f, 86f), new Vector3(96f, 0f, 120f),
                new Vector3(36f, 0f, 118f), new Vector3(24f, 0f, 158f), new Vector3(-24f, 0f, 164f),
                new Vector3(-62f, 0f, 130f), new Vector3(-42f, 0f, 92f), new Vector3(-86f, 0f, 70f),
                new Vector3(-72f, 0f, 32f), new Vector3(-112f, 0f, 4f)
            }, 2);
        }

        void BuildMelbourneLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Park circuit";
            runtime.roadHalfWidth = 15.0f;
            runtime.kerbStart = 8.8f;
            runtime.drsZoneOne = new Vector2(0.88f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.52f, 0.69f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(188f, 0f, 0f), new Vector3(260f, 0f, 36f),
                new Vector3(246f, 0f, 104f), new Vector3(306f, 0f, 162f), new Vector3(248f, 0f, 232f),
                new Vector3(132f, 0f, 236f), new Vector3(54f, 0f, 196f), new Vector3(-46f, 0f, 214f),
                new Vector3(-144f, 0f, 164f), new Vector3(-170f, 0f, 96f), new Vector3(-118f, 0f, 52f),
                new Vector3(-28f, 0f, 48f), new Vector3(-108f, 0f, 18f), new Vector3(-224f, 0f, 6f)
            }, 4);
        }

        void BuildInterlagosLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Short flowing hillside";
            runtime.roadHalfWidth = 13.0f;
            runtime.kerbStart = 7.6f;
            runtime.drsZoneOne = new Vector2(0.88f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.62f, 0.79f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(116f, -1f, 0f), new Vector3(144f, -4f, 34f),
                new Vector3(102f, -7f, 70f), new Vector3(42f, -8f, 54f), new Vector3(12f, -6f, 92f),
                new Vector3(52f, -2f, 128f), new Vector3(118f, 2f, 118f), new Vector3(154f, 4f, 72f),
                new Vector3(102f, 3f, 32f), new Vector3(34f, 2f, 42f), new Vector3(-52f, 1f, 24f),
                new Vector3(-136f, 0f, 4f)
            }, 4);
        }

        void BuildAbuDhabiLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Twilight finale";
            runtime.roadHalfWidth = 14.2f;
            runtime.kerbStart = 8.3f;
            runtime.drsZoneOne = new Vector2(0.88f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.34f, 0.53f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(126f, 0f, 0f), new Vector3(166f, 0f, 26f),
                new Vector3(150f, 0f, 70f), new Vector3(206f, 0f, 102f), new Vector3(284f, 0f, 96f),
                new Vector3(320f, 0f, 132f), new Vector3(286f, 0f, 176f), new Vector3(202f, 0f, 174f),
                new Vector3(152f, 0f, 138f), new Vector3(88f, 0f, 150f), new Vector3(34f, 0f, 116f),
                new Vector3(62f, 0f, 76f), new Vector3(22f, 0f, 42f), new Vector3(-126f, 0f, 4f)
            }, 3);
        }

        void BuildChinaLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Technical snail and back straight";
            runtime.roadHalfWidth = 14.6f;
            runtime.kerbStart = 8.6f;
            runtime.drsZoneOne = new Vector2(0.83f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.42f, 0.58f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(138f, 0f, 0f), new Vector3(206f, 0f, 22f),
                new Vector3(224f, 0f, 72f), new Vector3(184f, 0f, 124f), new Vector3(114f, 0f, 114f),
                new Vector3(72f, 0f, 68f), new Vector3(78f, 0f, 28f), new Vector3(140f, 0f, 48f),
                new Vector3(220f, 0f, 88f), new Vector3(334f, 0f, 92f), new Vector3(382f, 0f, 128f),
                new Vector3(352f, 0f, 174f), new Vector3(262f, 0f, 184f), new Vector3(164f, 0f, 156f),
                new Vector3(66f, 0f, 132f), new Vector3(-62f, 0f, 54f), new Vector3(-152f, 0f, 8f)
            }, 4);
        }

        void BuildMiamiLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Stadium street rhythm";
            runtime.roadHalfWidth = 12.6f;
            runtime.kerbStart = 7.4f;
            runtime.drsZoneOne = new Vector2(0.86f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.48f, 0.64f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(152f, 0f, 0f), new Vector3(210f, 0f, 34f),
                new Vector3(184f, 0f, 78f), new Vector3(126f, 0f, 88f), new Vector3(176f, 0f, 126f),
                new Vector3(276f, 0f, 130f), new Vector3(330f, 0f, 170f), new Vector3(294f, 0f, 212f),
                new Vector3(198f, 0f, 204f), new Vector3(122f, 0f, 166f), new Vector3(44f, 0f, 178f),
                new Vector3(-24f, 0f, 132f), new Vector3(-52f, 0f, 78f), new Vector3(-102f, 0f, 34f),
                new Vector3(-164f, 0f, 6f)
            }, 3);
        }

        void BuildCanadaLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Stop-go island";
            runtime.roadHalfWidth = 12.8f;
            runtime.kerbStart = 7.5f;
            runtime.drsZoneOne = new Vector2(0.84f, 0.09f);
            runtime.drsZoneTwo = new Vector2(0.56f, 0.72f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(136f, 0f, 0f), new Vector3(182f, 0f, 26f),
                new Vector3(150f, 0f, 62f), new Vector3(84f, 0f, 52f), new Vector3(38f, 0f, 88f),
                new Vector3(92f, 0f, 126f), new Vector3(186f, 0f, 126f), new Vector3(260f, 0f, 166f),
                new Vector3(232f, 0f, 210f), new Vector3(136f, 0f, 214f), new Vector3(62f, 0f, 176f),
                new Vector3(-28f, 0f, 152f), new Vector3(-84f, 0f, 104f), new Vector3(-54f, 0f, 54f),
                new Vector3(-136f, 0f, 10f)
            }, 3);
        }

        void BuildBarcelonaLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Flowing test track";
            runtime.roadHalfWidth = 14.4f;
            runtime.kerbStart = 8.4f;
            runtime.drsZoneOne = new Vector2(0.88f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.5f, 0.65f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(176f, 0f, 0f), new Vector3(238f, 0f, 34f),
                new Vector3(220f, 0f, 92f), new Vector3(154f, 0f, 120f), new Vector3(82f, 0f, 110f),
                new Vector3(34f, 0f, 144f), new Vector3(82f, 0f, 184f), new Vector3(178f, 0f, 190f),
                new Vector3(230f, 0f, 150f), new Vector3(196f, 0f, 104f), new Vector3(122f, 0f, 88f),
                new Vector3(40f, 0f, 58f), new Vector3(-44f, 0f, 78f), new Vector3(-108f, 0f, 38f),
                new Vector3(-164f, 0f, 8f)
            }, 5);
        }

        void BuildAustriaLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Short alpine power";
            runtime.roadHalfWidth = 14.0f;
            runtime.kerbStart = 8.2f;
            runtime.drsZoneOne = new Vector2(0.86f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.18f, 0.36f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(160f, 7f, 0f), new Vector3(226f, 14f, 34f),
                new Vector3(194f, 18f, 82f), new Vector3(104f, 16f, 96f), new Vector3(34f, 10f, 76f),
                new Vector3(-22f, 5f, 108f), new Vector3(26f, 1f, 148f), new Vector3(126f, -2f, 142f),
                new Vector3(174f, -5f, 98f), new Vector3(118f, -4f, 42f), new Vector3(34f, -2f, 38f),
                new Vector3(-104f, 0f, 8f)
            }, 4);
        }

        void BuildHungaryLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Twisty technical bowl";
            runtime.roadHalfWidth = 12.6f;
            runtime.kerbStart = 7.4f;
            runtime.drsZoneOne = new Vector2(0.88f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.34f, 0.45f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(116f, 0f, 0f), new Vector3(146f, 0f, 36f),
                new Vector3(104f, 0f, 68f), new Vector3(48f, 0f, 56f), new Vector3(18f, 0f, 92f),
                new Vector3(76f, 0f, 124f), new Vector3(142f, 0f, 106f), new Vector3(178f, 0f, 144f),
                new Vector3(132f, 0f, 178f), new Vector3(58f, 0f, 162f), new Vector3(8f, 0f, 196f),
                new Vector3(-54f, 0f, 166f), new Vector3(-26f, 0f, 118f), new Vector3(-88f, 0f, 82f),
                new Vector3(-72f, 0f, 34f), new Vector3(-136f, 0f, 8f)
            }, 3);
        }

        void BuildZandvoortLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Coastal banked flow";
            runtime.roadHalfWidth = 12.5f;
            runtime.kerbStart = 7.3f;
            runtime.drsZoneOne = new Vector2(0.87f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.54f, 0.68f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(126f, 2f, 0f), new Vector3(168f, 4f, 38f),
                new Vector3(128f, 7f, 82f), new Vector3(58f, 8f, 74f), new Vector3(24f, 5f, 116f),
                new Vector3(78f, 2f, 154f), new Vector3(156f, 0f, 150f), new Vector3(210f, -1f, 108f),
                new Vector3(168f, -2f, 62f), new Vector3(90f, -1f, 48f), new Vector3(30f, 0f, 72f),
                new Vector3(-46f, 1f, 48f), new Vector3(-116f, 0f, 8f)
            }, 5);
        }

        void BuildMadridLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Hybrid street exhibition";
            runtime.roadHalfWidth = 12.0f;
            runtime.kerbStart = 7.0f;
            runtime.drsZoneOne = new Vector2(0.84f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.46f, 0.62f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(154f, 0f, 0f), new Vector3(210f, 0f, 38f),
                new Vector3(190f, 0f, 78f), new Vector3(238f, 0f, 118f), new Vector3(306f, 0f, 112f),
                new Vector3(342f, 0f, 154f), new Vector3(300f, 0f, 190f), new Vector3(210f, 0f, 176f),
                new Vector3(146f, 0f, 138f), new Vector3(82f, 0f, 154f), new Vector3(34f, 0f, 112f),
                new Vector3(58f, 0f, 72f), new Vector3(-18f, 0f, 46f), new Vector3(-104f, 0f, 28f),
                new Vector3(-168f, 0f, 6f)
            }, 2);
        }

        void BuildBakuLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Castle straight street";
            runtime.roadHalfWidth = 12.4f;
            runtime.kerbStart = 7.3f;
            runtime.drsZoneOne = new Vector2(0.78f, 0.1f);
            runtime.drsZoneTwo = new Vector2(0.52f, 0.67f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(220f, 0f, 0f), new Vector3(346f, 0f, 18f),
                new Vector3(382f, 0f, 56f), new Vector3(340f, 0f, 92f), new Vector3(260f, 0f, 84f),
                new Vector3(224f, 0f, 124f), new Vector3(250f, 0f, 160f), new Vector3(204f, 0f, 194f),
                new Vector3(148f, 0f, 166f), new Vector3(118f, 0f, 112f), new Vector3(62f, 0f, 116f),
                new Vector3(28f, 0f, 160f), new Vector3(-48f, 0f, 142f), new Vector3(-86f, 0f, 86f),
                new Vector3(-48f, 0f, 42f), new Vector3(-178f, 0f, 8f)
            }, 2);
        }

        void BuildAustinLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Rollercoaster esses";
            runtime.roadHalfWidth = 14.6f;
            runtime.kerbStart = 8.6f;
            runtime.drsZoneOne = new Vector2(0.86f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.38f, 0.56f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(142f, 18f, 0f), new Vector3(178f, 24f, 44f),
                new Vector3(126f, 20f, 78f), new Vector3(62f, 14f, 62f), new Vector3(104f, 8f, 28f),
                new Vector3(172f, 2f, 56f), new Vector3(238f, -2f, 104f), new Vector3(342f, -4f, 108f),
                new Vector3(392f, -2f, 150f), new Vector3(340f, 3f, 192f), new Vector3(230f, 5f, 182f),
                new Vector3(152f, 8f, 136f), new Vector3(78f, 6f, 154f), new Vector3(24f, 2f, 112f),
                new Vector3(-42f, 0f, 56f), new Vector3(-150f, 0f, 8f)
            }, 4);
        }

        void BuildMexicoLayout(TrackRuntime runtime)
        {
            runtime.styleName = "High-altitude stadium";
            runtime.roadHalfWidth = 14.2f;
            runtime.kerbStart = 8.3f;
            runtime.drsZoneOne = new Vector2(0.84f, 0.09f);
            runtime.drsZoneTwo = new Vector2(0.48f, 0.63f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(210f, 0f, 0f), new Vector3(268f, 0f, 34f),
                new Vector3(236f, 0f, 70f), new Vector3(176f, 0f, 58f), new Vector3(218f, 0f, 110f),
                new Vector3(292f, 0f, 142f), new Vector3(252f, 0f, 184f), new Vector3(172f, 0f, 174f),
                new Vector3(126f, 0f, 132f), new Vector3(78f, 0f, 158f), new Vector3(38f, 0f, 122f),
                new Vector3(74f, 0f, 82f), new Vector3(14f, 0f, 52f), new Vector3(-72f, 0f, 30f),
                new Vector3(-168f, 0f, 6f)
            }, 3);
        }

        void BuildLasVegasLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Neon strip street";
            runtime.roadHalfWidth = 13.0f;
            runtime.kerbStart = 7.6f;
            runtime.drsZoneOne = new Vector2(0.74f, 0.13f);
            runtime.drsZoneTwo = new Vector2(0.42f, 0.58f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(260f, 0f, 0f), new Vector3(388f, 0f, 22f),
                new Vector3(428f, 0f, 66f), new Vector3(380f, 0f, 102f), new Vector3(278f, 0f, 94f),
                new Vector3(222f, 0f, 134f), new Vector3(272f, 0f, 174f), new Vector3(358f, 0f, 168f),
                new Vector3(404f, 0f, 206f), new Vector3(350f, 0f, 240f), new Vector3(218f, 0f, 222f),
                new Vector3(106f, 0f, 170f), new Vector3(14f, 0f, 154f), new Vector3(-64f, 0f, 92f),
                new Vector3(-34f, 0f, 44f), new Vector3(-184f, 0f, 6f)
            }, 2);
        }

        void BuildQatarLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Desert high-speed flow";
            runtime.roadHalfWidth = 15.0f;
            runtime.kerbStart = 8.8f;
            runtime.drsZoneOne = new Vector2(0.88f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.55f, 0.72f);
            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(170f, 0f, 0f), new Vector3(232f, 0f, 36f),
                new Vector3(252f, 0f, 90f), new Vector3(210f, 0f, 138f), new Vector3(132f, 0f, 144f),
                new Vector3(70f, 0f, 108f), new Vector3(104f, 0f, 68f), new Vector3(188f, 0f, 78f),
                new Vector3(276f, 0f, 118f), new Vector3(318f, 0f, 168f), new Vector3(248f, 0f, 202f),
                new Vector3(136f, 0f, 190f), new Vector3(42f, 0f, 150f), new Vector3(-38f, 0f, 92f),
                new Vector3(-98f, 0f, 36f), new Vector3(-166f, 0f, 6f)
            }, 5);
        }

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

            if (runtime.roadHalfWidth < 9f || runtime.roadHalfWidth > 17f)
            {
                if (LastReport != null)
                {
                    LastReport.Warn("road half width " + runtime.roadHalfWidth.ToString("0.0") + " out of range, clamping.");
                }

                runtime.roadHalfWidth = Mathf.Clamp(runtime.roadHalfWidth, 9f, 17f);
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

        WeatherState DetermineWeather(string profile)
        {
            if (string.IsNullOrEmpty(profile))
            {
                return WeatherState.Clear;
            }

            if (profile.Contains("wet"))
            {
                return WeatherState.LightRain;
            }

            if (profile.Contains("mixed"))
            {
                return WeatherState.LightRain;
            }

            if (profile.Contains("cloud"))
            {
                return WeatherState.Cloudy;
            }

            return WeatherState.Clear;
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
            kerbMaterial = CreateMaterial("Runtime Kerb", new Color(0.94f, 0.04f, 0.03f), 0.02f, 0.64f);
            kerbMaterial.mainTexture = BuildNoiseTexture(64, new Color(0.92f, 0.92f, 0.92f), 0.08f);
            kerbMaterial.mainTextureScale = new Vector2(6f, 1.5f);
            // Blue/white kerb-paint scheme used at coastal and technical-parkland
            // circuits (see CreateKerbBlock) instead of the default red/white, so not
            // every corner on every track shares one painted colour pair.
            kerbMaterialBlue = CreateMaterial("Runtime Kerb Blue", new Color(0.04f, 0.15f, 0.5f), 0.02f, 0.64f);
            kerbMaterialBlue.mainTexture = BuildNoiseTexture(64, new Color(0.9f, 0.9f, 0.94f), 0.08f);
            kerbMaterialBlue.mainTextureScale = new Vector2(6f, 1.5f);
            grassMaterial = CreateMaterial("Runtime Runoff", runoff, 0.01f, runoffSmoothness, runoffEmission);
            // Grass/runoff covers the most screen space of anything in the scene, so a
            // tiled neutral-grey noise texture (multiplied against the tinted runoff
            // colour) does the most to kill the flat-plastic ground read.
            grassMaterial.mainTexture = BuildNoiseTexture(128, new Color(0.83f, 0.85f, 0.8f), 0.18f);
            grassMaterial.mainTextureScale = new Vector2(70f, 70f);
            lineMaterial = CreateMaterial("Runtime Track Line", new Color(0.95f, 0.98f, 1f), 0.05f, 0.78f);
            roadEdgeMaterial = CreateMaterial("Runtime Painted Edge", new Color(1f, 0.98f, 0.9f), 0.04f, 0.76f);
            drsPaintMaterial = CreateMaterial("Runtime DRS Paint", new Color(0.02f, 0.32f, 0.95f), 0.06f, 0.82f, new Color(0.01f, 0.05f, 0.18f));
            rubberMaterial = CreateMaterial("Runtime Rubber", new Color(0.003f, 0.003f, 0.003f), 0.01f, 0.24f);

            // Light-coloured tyre marbles that build up off the racing line rather than
            // rubbered-in (dark) - speckled the same way concrete/kerb/grass are, just
            // tan/grey and tiled tighter so it reads as scattered debris, not a smooth patch.
            tyreMarbleMaterial = CreateMaterial("Runtime Tyre Marbles", new Color(0.63f, 0.59f, 0.49f), 0f, 0.18f);
            tyreMarbleMaterial.mainTexture = BuildNoiseTexture(64, new Color(0.68f, 0.63f, 0.5f), 0.24f);
            tyreMarbleMaterial.mainTextureScale = new Vector2(5f, 2f);
            // Sun-bleached companion tint mixed into corner-exit marble patches for
            // extra variety instead of every patch sharing one flat marble colour.
            tyreMarbleMaterialLight = CreateMaterial("Runtime Tyre Marbles Light", new Color(0.74f, 0.68f, 0.55f), 0f, 0.14f);
            tyreMarbleMaterialLight.mainTexture = BuildNoiseTexture(64, new Color(0.78f, 0.72f, 0.57f), 0.22f);
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
            barrierMaterial.mainTexture = BuildNoiseTexture(64, new Color(0.87f, 0.87f, 0.87f), 0.1f);
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
            barrierWeatherMaterial.mainTexture = BuildNoiseTexture(48, new Color(0.2f, 0.19f, 0.16f), 0.3f);
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
            fencePostMaterial.mainTexture = BuildNoiseTexture(32, new Color(0.44f, 0.48f, 0.51f), 0.1f);
            fencePostMaterial.mainTextureScale = new Vector2(2f, 4f);
            foliageMaterial = CreateMaterial("Runtime Foliage", spaTrack ? new Color(0.05f, 0.22f, 0.14f) : new Color(0.04f, 0.32f, 0.12f), 0f, 0.42f);
            // Trees used to borrow the bright red scenery-accent material for their
            // trunks (fine for kerb-style trim, glaring as bark) - a dedicated dull
            // brown fixes that without touching the accent colour anywhere else it's used.
            treeBarkMaterial = CreateMaterial("Runtime Tree Bark", desertTrack ? new Color(0.42f, 0.32f, 0.22f) : new Color(0.32f, 0.24f, 0.18f), 0f, 0.32f);
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
            luxuryApartmentMaterial.mainTexture = BuildNoiseTexture(64, new Color(0.9f, 0.87f, 0.8f), 0.06f);
            luxuryApartmentMaterial.mainTextureScale = new Vector2(2f, 3f);

            // Weathered concrete tone for "old-school racing venue" grandstands and the
            // Zandvoort boardwalk deck; duller and less glossy than the bridge/wall
            // concreteMaterial.
            weatheredConcreteMaterial = CreateMaterial("Runtime Weathered Concrete", new Color(0.5f, 0.5f, 0.46f), 0.03f, 0.22f);
            weatheredConcreteMaterial.mainTexture = BuildNoiseTexture(64, new Color(0.6f, 0.6f, 0.56f), 0.14f);
            weatheredConcreteMaterial.mainTextureScale = new Vector2(3f, 1f);

            // Cooler, sandier ground tone for Zandvoort's coastal dunes than the warm
            // orange desert palette above.
            coastalSandMaterial = CreateMaterial("Runtime Coastal Sand", new Color(0.82f, 0.76f, 0.6f), 0.02f, 0.28f);
            coastalSandMaterial.mainTexture = BuildNoiseTexture(128, new Color(0.86f, 0.83f, 0.72f), 0.14f);
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
            gravelMaterial.mainTexture = BuildNoiseTexture(96, new Color(0.62f, 0.56f, 0.46f), 0.3f);
            gravelMaterial.mainTextureScale = new Vector2(24f, 24f);

            // Flat, smooth, reflective water for the Monaco marina and coastal sea planes -
            // dark and glossy enough to read as water rather than another ground slab.
            waterMaterial = CreateMaterial("Runtime Water", new Color(0.04f, 0.16f, 0.26f), 0.35f, 0.92f);

            // Grey angular rock-outcrop tone for mountain/forest cliff dressing, kept
            // distinct from concreteMaterial/barrierMaterial so a rock face doesn't just
            // read as another retaining wall.
            rockMaterial = CreateMaterial("Runtime Rock Outcrop", new Color(0.42f, 0.4f, 0.38f), 0.02f, 0.22f);
            rockMaterial.mainTexture = BuildNoiseTexture(64, new Color(0.48f, 0.46f, 0.44f), 0.22f);
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
            Material material = new Material(Shader.Find("Standard"));
            material.name = materialName;
            material.color = new Color(color.r, color.g, color.b, alpha);
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
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

            const int size = 256;
            asphaltNoiseTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            asphaltNoiseTexture.name = "Runtime asphalt noise";
            asphaltNoiseTexture.wrapMode = TextureWrapMode.Repeat;
            asphaltNoiseTexture.filterMode = FilterMode.Trilinear;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float coarse = Mathf.PerlinNoise(x * 0.035f, y * 0.035f);
                    float fine = Mathf.PerlinNoise(x * 0.21f + 51.7f, y * 0.21f + 17.3f);
                    float grain = Mathf.PerlinNoise(x * 0.62f + 133.7f, y * 0.62f + 71.9f);
                    float value = 0.62f + coarse * 0.2f + fine * 0.14f + grain * 0.12f;

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

            const int size = 64;
            windowStripTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            windowStripTexture.name = "Runtime window strip";
            windowStripTexture.wrapMode = TextureWrapMode.Repeat;
            windowStripTexture.filterMode = FilterMode.Point;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cell = x / 8;
                    bool lit = cell % 2 == 0;
                    float noise = Mathf.PerlinNoise(cell * 3.1f, 0.5f);
                    float value = lit ? 0.75f + noise * 0.25f : 0.12f + noise * 0.08f;
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

            const int size = 32;
            chainLinkTexture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            chainLinkTexture.name = "Runtime chain link";
            chainLinkTexture.wrapMode = TextureWrapMode.Repeat;
            chainLinkTexture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            int cell = Mathf.Max(4, size / 4);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int diagA = ((x + y) % cell + cell) % cell;
                    int diagB = ((x - y) % cell + cell) % cell;
                    bool strand = diagA <= 1 || diagB <= 1;
                    pixels[y * size + x] = strand ? new Color(0.85f, 0.87f, 0.88f, 1f) : new Color(0f, 0f, 0f, 0f);
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

            const int size = 128;
            asphaltWearTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            asphaltWearTexture.name = "Runtime asphalt wear";
            asphaltWearTexture.wrapMode = TextureWrapMode.Repeat;
            asphaltWearTexture.filterMode = FilterMode.Trilinear;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float patch = Mathf.PerlinNoise(x * 0.045f + 4.1f, y * 0.045f + 8.3f);
                    float grain = Mathf.PerlinNoise(x * 0.3f + 61f, y * 0.3f + 19f);
                    float value = Mathf.Clamp01(0.5f + patch * 0.32f + grain * 0.1f);
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

            const int size = 64;
            armcoCorrugationTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            armcoCorrugationTexture.name = "Runtime armco corrugation";
            armcoCorrugationTexture.wrapMode = TextureWrapMode.Repeat;
            armcoCorrugationTexture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float wave = Mathf.Sin(y / (float)size * Mathf.PI * 6f) * 0.5f + 0.5f;
                    float grain = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.08f;
                    int localY = y % size;
                    bool bolt = x % 16 < 2 && (localY < 3 || (localY > size / 2 - 2 && localY < size / 2 + 1));
                    float value = Mathf.Clamp01(0.72f + wave * 0.22f + grain - (bolt ? 0.18f : 0f));
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

            const int size = 128;
            concretePanelTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            concretePanelTexture.name = "Runtime concrete panel";
            concretePanelTexture.wrapMode = TextureWrapMode.Repeat;
            concretePanelTexture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float stain = Mathf.PerlinNoise(x * 0.02f, y * 0.06f) * 0.14f;
                    float grain = Mathf.PerlinNoise(x * 0.2f + 9f, y * 0.2f + 4f) * 0.08f;
                    bool seam = x % 32 < 1 || y % 64 < 1;
                    float value = Mathf.Clamp01(0.82f - stain + grain - (seam ? 0.22f : 0f));
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

            const int size = 64;
            tyreTreadTexture = new Texture2D(size, size, TextureFormat.RGB24, true);
            tyreTreadTexture.name = "Runtime tyre tread";
            tyreTreadTexture.wrapMode = TextureWrapMode.Repeat;
            tyreTreadTexture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int ring = y % 16;
                    float rim = ring < 2 ? 0.06f : 0f;
                    float grain = Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.03f;
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
            material.SetFloat("_Mode", 1f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetFloat("_Cutoff", cutoff);
            material.renderQueue = 2450;
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
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float coarse = Mathf.PerlinNoise(x * 0.045f, y * 0.045f);
                    float fine = Mathf.PerlinNoise(x * 0.22f + 91.3f, y * 0.22f + 42.1f);
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
            Material material = new Material(Shader.Find("Standard"));
            material.name = materialName;
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
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
            Vector3 size = new Vector3(Mathf.Max(1200f, bounds.size.x * 1.5f), 1.0f, Mathf.Max(1200f, bounds.size.z * 1.5f));
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
            int hillClusters = Mathf.Max(3, Mathf.RoundToInt(6f * Mathf.Clamp(sceneryDensity, 0.25f, 2f)));
            for (int i = 0; i < hillClusters; i++)
            {
                Vector3 hillPos = new Vector3(center.x + Random.Range(-size.x, size.x) * 0.48f, groundTopY, center.z + Random.Range(-size.z, size.z) * 0.48f);
                CreateDistantHillCluster(hillPos, i);
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
            int pieces = 3 + seed % 3;
            for (int p = 0; p < pieces; p++)
            {
                float widthScale = 55f + (seed * 7 + p * 23) % 55;
                float heightScale = 26f + (seed * 3 + p * 7) % 20;
                Vector3 offset = new Vector3((p - (pieces - 1) * 0.5f) * widthScale * 0.6f, 0f, ((seed + p) % 3 - 1) * 20f);
                Vector3 desired = center + offset;
                float objectRadius = widthScale * 0.5f;
                Vector3 safePosition;
                if (!TryGetClearScenerySpot(desired, objectRadius, 12f, out safePosition))
                {
                    continue;
                }

                float hillCenterY = groundTopY - heightScale * 0.5f + heightScale * 0.55f;
                GameObject hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = "Distant hill ridge";
                hill.transform.SetParent(transform);
                hill.transform.position = new Vector3(safePosition.x, hillCenterY, safePosition.z);
                hill.transform.localScale = new Vector3(widthScale, heightScale, widthScale * 0.6f);
                hill.GetComponent<Renderer>().sharedMaterial = grassMaterial;
                MakeVisualOnly(hill);
            }
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
            const float halfWidth = 70f;
            const float thickness = 1.4f;
            const float depthBelowRoad = 2.6f;

            for (float d = 0f; d < Runtime.length; d += spacing)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d + spacing * 0.5f, out point, out forward, out right);
                float floorTopY = Mathf.Max(groundTopY + 0.05f, point.y - depthBelowRoad);

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
            int count = Runtime.centerLine.Count;
            Vector3[] vertices = new Vector3[count * 2];
            Vector2[] uvs = new Vector2[count * 2];
            int[] triangles = new int[count * 6];

            for (int i = 0; i < count; i++)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.cumulativeDistances[i], out point, out forward, out right);
                // The physical drivable surface (and its MeshCollider) is the ultimate
                // ground truth for how wide the track actually is, so it must sample the
                // same widened HalfWidthAt used by kerbs/barriers - otherwise a hairpin
                // could paint/fence wider than the tarmac cars can actually drive on.
                float localHalfWidth = Runtime.HalfWidthAt(Runtime.cumulativeDistances[i]);
                vertices[i * 2] = point - right * localHalfWidth + Vector3.up * 0.015f;
                vertices[i * 2 + 1] = point + right * localHalfWidth + Vector3.up * 0.015f;
                float v = Runtime.cumulativeDistances[i] / 12f; // Tiled UV for asphalt detail
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
        const float EdgeBarrierClearance = 0.15f;
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
        const float TightCornerFenceRadius = 35f;

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
            for (float d = 0f; d < Runtime.length;)
            {
                bool nearHighRiskCorner = IsNearCorner(d, highRiskCorners, 45f);
                // Union of the true hairpin/high-risk band with the broader tight-corner
                // band - drives containment (fencing + overlap) only, never the tyre-stack
                // or widening decisions which still key off nearHighRiskCorner alone.
                bool nearTightFenceCorner = nearHighRiskCorner || IsNearCorner(d, tightFenceCorners, TightCornerFenceRadius);

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
                float segmentLength = localStep + localOverlap;
                bool elevated = IsElevatedAtDistance(d) || IsElevatedAtDistance(d + localStep * 0.5f) || IsElevatedAtDistance(d + localStep);
                float normalized = d / Mathf.Max(1f, Runtime.length);

                BuildBarrierSegmentForSide(d, localStep, segmentLength, -1, elevated, normalized, highSpeedTrack, nearHighRiskCorner, nearTightFenceCorner, stripeIndex);
                BuildBarrierSegmentForSide(d, localStep, segmentLength, 1, elevated, normalized, highSpeedTrack, nearHighRiskCorner, nearTightFenceCorner, stripeIndex);

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

        // Decides style/offset for one side at one step, including the pit-corridor
        // fan-out on the right side, then hands off to the shared segment builder.
        void BuildBarrierSegmentForSide(float distance, float step, float segmentLength, int side, bool elevated, float normalized, bool highSpeedTrack, bool nearHighRiskCorner, bool nearTightFenceCorner, int stripeIndex)
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
            if (side > 0)
            {
                float pitBlend = PitZoneBlend(normalized);
                if (pitBlend > 0f)
                {
                    lateral = Mathf.Lerp(baseLateral, PitOuterLateral(), pitBlend);
                    style = EdgeBarrierStyle.StreetWall;
                    catchFence = false;
                    tyreStack = false;
                }
            }

            CreateEdgeBarrierSegment(distance, step, side, lateral, segmentLength, style, catchFence, tyreStack, stripeIndex);
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
            Vector3 discard;
            Runtime.SampleAtDistance(distance, out a, out discard, out right);
            Runtime.SampleAtDistance(distance + step, out b, out discard, out right);
            Runtime.SampleAtDistance(distance + step * 0.5f, out mid, out forward, out right);

            Vector3 chord = b - a;
            Vector3 chordForward = chord.sqrMagnitude > 0.01f ? chord.normalized : forward;
            Vector3 basePosition = mid + right * side * lateral;

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
                // rail's basePosition, which BuildBarrierSegmentForSide has
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
            // with (see BuildBarrierSegmentForSide), not some larger,
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
        const float PitZoneEntryRampStart = 0.85f;
        const float PitZoneEntryRampEnd = TrackRuntime.PitCorridorStartNormalized;
        const float PitZoneExitRampStart = 0.995f;
        const float PitZoneExitRampEnd = 0.045f;

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

        // ---------- pit lane / track divider ----------
        // The old barrier layout only ever fenced the pit complex's OUTER edge
        // (BuildBarrierSegmentForSide fans the main right-side barrier out to
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

            float startDistance = Runtime.length * TrackRuntime.PitCorridorStartNormalized;
            float endDistance = Runtime.length * PitZoneExitRampStart;
            float span = endDistance - startDistance;
            if (span < 0f)
            {
                span += Runtime.length;
            }

            for (float d = 0f; d < span; d += PitDividerStep)
            {
                float distance = Runtime.WrapDistance(startDistance + d);
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
            float pitLaneInnerEdge = Runtime.PitLaneLateral - 6.75f;
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
        const float CornerWindowSpanMeters = 56f;

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
            // BuildBarrierSegmentForSide so the wall both hugs the edge and
            // stays safely past that leniency boundary.
            TryPlaceSolidObstacle(wall, "bridge-wall", basePosition, forward, scale, 0.62f, EdgeBarrierClearance);
        }

        void CreateCatchFence(Vector3 basePosition, Vector3 forward, float segmentLength)
        {
            GameObject fence = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fence.name = "Catch fence";
            fence.transform.SetParent(transform);
            Vector3 scale = new Vector3(0.18f, 2.6f, segmentLength);
            fence.transform.localScale = scale;
            fence.GetComponent<Renderer>().sharedMaterial = fenceMaterial;
            // Flush-fix: minimumClearance used to be a flat 0.5m, well past
            // where basePosition (the same point the main barrier below it
            // sits at) actually lands relative to the track edge - meaning
            // this fence failed its OWN clearance check almost every time and
            // silently repaired itself further out than the wall directly
            // beneath it. Matching EdgeBarrierClearance keeps the catch fence
            // vertically stacked on the same line as the barrier it backs,
            // not drifting out on its own.
            if (!TryPlaceSolidObstacle(fence, "catch-fence", basePosition, forward, scale, 2.5f, EdgeBarrierClearance))
            {
                return;
            }

            // Visual posts and a top rail keyed off the placed fence so they follow
            // any lateral repair the placement pass applied.
            Vector3 placed = fence.transform.position;
            Vector3 placedForward = fence.transform.forward;
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
                    if (IsIntentionalPitOpening(normalized, side))
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

                        // Full-perimeter requirement: don't just log the gap, close it.
                        // Drops a short corrective barrier segment (style-matched to
                        // elevated/street/standard) right at the true edge of this exact
                        // sample point, reusing the same flush-distance math and
                        // clearance-checked placement every other barrier segment uses.
                        if (AutoFillBarrierGap(d, side))
                        {
                            autoFilled++;
                        }
                    }
                }
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
        bool IsIntentionalPitOpening(float normalized, int side)
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

            // The pit corridor follows the track from just before pit entry to the
            // release point, so surfaces, walls, boxes, and buildings are sampled
            // along the lap distance instead of assuming one straight chord.
            float corridorStart = Runtime.length * TrackRuntime.PitCorridorStartNormalized;
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
                    new Vector3(13.5f, 0.16f, 21f),
                    pitMaterial);
            }

            CreatePitEntryExitSurfaces(pitMaterial);
            CreatePitEntryExitPaint(pitMaterial);
            CreatePitLaneCones();
            CreatePitEntryMarkers();

            // Pit wall between track and lane, sampled so it never cuts the corner.
            for (float d = corridorStart + 10f; d < corridorEnd - 8f; d += 12.5f)
            {
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
            for (float d = corridorStart + 20f; d < corridorEnd - 30f; d += 34f)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d + 17f, out point, out forward, out right);
                Vector3 basePosition = point + right * (Runtime.PitLaneLateral + 15f);
                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
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
        // Where a car first commits off the racing line toward the pits / first
        // rejoins the racing line after the pits - just past the true track edge,
        // not the track edge itself, so the merge surface always overlaps the
        // main track surface rather than butting a seam exactly on it.
        const float PitRampNearTrackLateral = 1.6f;
        const float PitRampNarrowWidth = 6f;
        const float PitRampFullWidth = 13.5f;

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
                float t = span <= 0.01f ? (inbound ? 1f : 0f) : Mathf.Clamp01((d + segStep * 0.5f) / span);

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(distance + segStep * 0.5f, out point, out forward, out right);

                float trackEdgeLateral = Runtime.roadHalfWidth + PitRampNearTrackLateral;
                float lateral = inbound ? Mathf.Lerp(trackEdgeLateral, Runtime.PitLaneLateral, t) : Mathf.Lerp(Runtime.PitLaneLateral, trackEdgeLateral, t);
                float width = inbound ? Mathf.Lerp(PitRampNarrowWidth, PitRampFullWidth, t) : Mathf.Lerp(PitRampFullWidth, PitRampNarrowWidth, t);

                CreateCollidablePitSurface(label, point + right * lateral + Vector3.up * 0.012f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(width, 0.16f, segStep + PitRampSurfaceOverlap), pitMaterial);
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
            Runtime.SampleAtDistance(Runtime.length * 0.865f, out entry, out entryForward, out entryRight);
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
        // line plus roundel sign right where SetPitLimiter actually starts enforcing
        // the limiter for a car that has requested a stop (see
        // TrackRuntime.PitApproachStartNormalized) - so what's on the ground now
        // matches both where the game visually starts the ramp and where it actually
        // starts limiting speed.
        void CreatePitEntryMarkers()
        {
            CreatePitSignBoard(Runtime.length * PitZoneEntryRampStart);
            CreateSpeedLimitLine(Runtime.length * TrackRuntime.PitApproachStartNormalized);
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
                if (normalized > TrackRuntime.PitCorridorStartNormalized || normalized < 0.04f)
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
            BuildGrandstand(0.02f, -1);
            BuildGrandstand(0.15f, 1);
            BuildGrandstand(0.45f, -1);
            BuildGrandstand(0.85f, -1);

            // Natural road courses read as an open-countryside meeting with an extra
            // grandstand at a corner rather than only on the straights.
            if (!desertTrack && !streetTrack)
            {
                BuildGrandstand(0.62f, 1);
            }

            float density = Mathf.Clamp(sceneryDensity, 0.25f, 2f);

            // Extra permanent stands for higher scenery-density settings, at fresh
            // normalized slots that don't overlap the fixed set above, so a fully
            // built-up circuit (rather than only the fixed handful every track gets)
            // is available on the high end without costing anything at the low end.
            if (density >= 1.25f)
            {
                BuildGrandstand(0.3f, 1);
                BuildGrandstand(0.72f, -1);
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
                    if (spaTrack || i % 6 == 0) CreateTreeCluster(basePosition + right * side * 40f, i);
                }
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

            BuildMountainBackdrop(density, ridgeTint);

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
                    float widthScale = 60f + (i % 4) * 20f;
                    float heightScale = 14f + (i % 3) * 6f;
                    GameObject silhouette = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    silhouette.name = "Distant parallax tree line";
                    silhouette.transform.SetParent(transform);
                    silhouette.transform.position = new Vector3(safePosition.x, groundTopY + heightScale * 0.4f, safePosition.z);
                    silhouette.transform.localScale = new Vector3(widthScale, heightScale, widthScale * 0.55f);
                    silhouette.GetComponent<Renderer>().sharedMaterial = desertTrack ? grassMaterial : foliageMaterial;
                    MakeVisualOnly(silhouette);
                }
            }
        }

        // Ring of low, flattened, elongated spheres well outside the track bounds,
        // standing in for a distant mountain/hill range on the horizon. Radius-aware
        // clearance means a segment simply doesn't spawn if it can't clear the corridor
        // rather than risking an oversized silhouette near the racing line.
        void BuildMountainBackdrop(float density, Color tint)
        {
            Material ridgeMaterial = CreateMaterial("Runtime Mountain Ridge", tint, 0f, 0.15f);
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

                // Sit most of the sphere below groundTopY so only a rounded cap of
                // roughly 0.6x its height reads above the terrain as a ridge silhouette.
                float ridgeCenterY = groundTopY - heightScale * 0.5f + heightScale * 0.6f;
                GameObject ridge = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ridge.name = "Distant mountain ridge";
                ridge.transform.SetParent(transform);
                ridge.transform.position = new Vector3(safePosition.x, ridgeCenterY, safePosition.z);
                ridge.transform.localScale = new Vector3(widthScale, heightScale, widthScale * 0.62f);
                ridge.GetComponent<Renderer>().sharedMaterial = ridgeMaterial;
                MakeVisualOnly(ridge);
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

                float widthScale = 90f + (i % 4) * 30f;
                float heightScale = 16f + (i % 3) * 8f;
                GameObject duneRidge = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                duneRidge.name = "Distant desert dune ridge";
                duneRidge.transform.SetParent(transform);
                duneRidge.transform.position = new Vector3(safePosition.x, groundTopY - heightScale * 0.3f, safePosition.z);
                duneRidge.transform.localScale = new Vector3(widthScale, heightScale, widthScale * 0.7f);
                duneRidge.GetComponent<Renderer>().sharedMaterial = grassMaterial;
                MakeVisualOnly(duneRidge);
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

            // A couple of extra long, low grandstands beyond BuildScenery's fixed set.
            BuildGrandstand(0.3f, 1);
            if (density > 0.6f)
            {
                BuildGrandstand(0.72f, -1);
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
            // street-circuit "canyon" feel is stronger.
            int clusters = Mathf.Max(4, Mathf.RoundToInt(9f * density));
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
                CreateProceduralBuildingCluster(anchor, forward, 3, 22f + (i % 4) * 9f, false, 2.5f);
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
                float heightScale = 24f + (i % 3) * 8f;
                GameObject hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = "Forested hillside";
                hill.transform.SetParent(transform);
                hill.transform.position = safePosition + Vector3.down * (heightScale * 0.25f);
                hill.transform.localScale = new Vector3(60f + (i % 4) * 12f, heightScale, 44f + (i % 3) * 10f);
                hill.GetComponent<Renderer>().sharedMaterial = foliageMaterial;
                MakeVisualOnly(hill);

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

            // Two further-back layers at increasing distance/height so the forest reads
            // with real depth instead of one flat treeline ring.
            int farRings = Mathf.Max(4, Mathf.RoundToInt(8f * density));
            for (int layer = 1; layer <= 2; layer++)
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

                    float heightScale = (26f + layer * 10f) + (i % 3) * 8f;
                    GameObject farHill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    farHill.name = "Distant forested ridge layer " + layer;
                    farHill.transform.SetParent(transform);
                    farHill.transform.position = safePosition + Vector3.up * (heightScale * 0.15f * layer);
                    farHill.transform.localScale = new Vector3(70f + (i % 4) * 16f, heightScale, 50f + (i % 3) * 12f);
                    farHill.GetComponent<Renderer>().sharedMaterial = foliageMaterial;
                    MakeVisualOnly(farHill);
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

                float heightScale = 12f + (i % 3) * 6f;
                GameObject duneRidge = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                duneRidge.name = "Coastal dune ridge";
                duneRidge.transform.SetParent(transform);
                duneRidge.transform.position = new Vector3(safePosition.x, groundTopY - heightScale * 0.25f, safePosition.z);
                duneRidge.transform.localScale = new Vector3(80f + (i % 4) * 20f, heightScale, 60f + (i % 3) * 14f);
                duneRidge.GetComponent<Renderer>().sharedMaterial = coastalSandMaterial;
                MakeVisualOnly(duneRidge);
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

                float heightScale = 14f + (i % 3) * 6f;
                GameObject bank = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bank.name = "Amphitheater hillside bank";
                bank.transform.SetParent(transform);
                bank.transform.position = safePosition + Vector3.down * (heightScale * 0.3f);
                bank.transform.localScale = new Vector3(56f + (i % 4) * 12f, heightScale, 40f + (i % 3) * 10f);
                bank.GetComponent<Renderer>().sharedMaterial = terrainMaterial;
                MakeVisualOnly(bank);

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
            BuildParklandBackdrop(density * 0.5f);

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

                float heightScale = 10f + i * 4f;
                GameObject hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = "Austin rolling terrain rise";
                hill.transform.SetParent(transform);
                hill.transform.position = safeHill + Vector3.down * (heightScale * 0.3f);
                hill.transform.localScale = new Vector3(52f, heightScale, 40f);
                hill.GetComponent<Renderer>().sharedMaterial = grassMaterial;
                MakeVisualOnly(hill);
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
            Runtime.SampleAtDistance(Runtime.length * 0.955f, out point, out forward, out right);
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
            float corridorStart = Runtime.length * TrackRuntime.PitCorridorStartNormalized;
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
            sheen.SetFloat("_Glossiness", 0.98f);
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

        void BuildGrandstand(float normalizedDistance, int side)
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

            // Paved foundation pad under the stand so it reads as sitting on a real
            // concourse rather than hovering just above unbroken bare ground.
            CreateGroundPatch("Grandstand foundation pad", basePosition + lateral * 3f, 9f, 24f, concreteMaterial, rotation);

            // Tiered seating stepping up and away from the track, with colored crowd
            // blocks so the stands read as full rather than as bare metal shelves.
            // Parkland circuits get a weathered concrete tier tone instead of bare
            // metal for the "old-school racing venue" read the brief asks for.
            Material tierMaterial = parklandTrack ? weatheredConcreteMaterial : metalMaterial;
            for (int row = 0; row < 6; row++)
            {
                Vector3 rowCenter = basePosition + Vector3.up * (0.4f + row * 0.62f) + lateral * row * 1.15f;
                CreateVisualBox("Grandstand tier", rowCenter, rotation, new Vector3(1.25f, 0.5f, 22f), tierMaterial);
                CreateVisualBox("Grandstand crowd block", rowCenter + Vector3.up * 0.45f, rotation, new Vector3(0.9f, 0.42f, 21f), row % 2 == 0 ? sceneryAccentMaterial : glassMaterial);
            }

            // Roof canopy on slender pylons, in a distinct roof-toned material from the
            // seating tiers so the stand doesn't read as one flat block.
            Vector3 roofCenter = basePosition + Vector3.up * 5.4f + lateral * 3.4f;
            CreateVisualBox("Grandstand roof", roofCenter, rotation, new Vector3(9.6f, 0.28f, 23.5f), grandstandRoofMaterial);
            CreateVisualBox("Grandstand roof fascia", roofCenter - lateral * 4.6f - Vector3.up * 0.5f, rotation, new Vector3(0.22f, 0.9f, 23.5f), sceneryAccentMaterial);
            for (int pylon = -1; pylon <= 1; pylon++)
            {
                CreateVisualBox("Grandstand pylon", basePosition + lateral * 7.2f + forward * pylon * 9.5f + Vector3.up * 2.7f, rotation, new Vector3(0.4f, 5.4f, 0.4f), metalMaterial);
            }

            // Sponsor-neutral bunting flags strung along the roof fascia so the stand
            // reads as dressed for a race weekend rather than a bare metal shelf from a
            // distance - cycles the same invented SponsorPalette the trackside boards
            // already share instead of adding another colour set.
            const int buntingCount = 7;
            for (int i = 0; i < buntingCount; i++)
            {
                float t = (i - (buntingCount - 1) * 0.5f) / buntingCount;
                Material buntingMaterial = CreateMaterial("Grandstand bunting flag material", SponsorPalette[i % SponsorPalette.Length], 0.02f, 0.4f);
                Vector3 buntingPosition = roofCenter - lateral * 4.7f - Vector3.up * 0.95f + forward * t * 21f;
                CreateVisualBox("Grandstand bunting flag", buntingPosition, rotation * Quaternion.Euler(0f, 0f, 18f), new Vector3(0.04f, 0.4f, 0.5f), buntingMaterial);
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

        void CreateTreeCluster(Vector3 position, int index)
        {
            // Small grass apron under the cluster so it reads as a planted stand rather
            // than three trees hovering just above unbroken bare ground. Placed at the
            // cluster's own incoming y (not groundTopY) so it stays correct both for the
            // flat-ground BuildScenery callers and the hillside backdrop passes that
            // deliberately pass in a locally-elevated position for this same function.
            CreateVisualBox("Tree cluster ground patch", position + Vector3.up * 0.02f, Quaternion.identity, new Vector3(7f, 0.05f, 5f), grassMaterial);
            for (int i = 0; i < 3; i++)
            {
                Vector3 offset = new Vector3((i - 1) * 2.3f, 0f, (index % 3 - 1) * 1.4f);
                Vector3 treePosition = PushSceneryClearOfTrack(position + offset, 20f);
                // Cheap per-tree size jitter (a cluster of three identically-sized trees
                // used to read as one stamped prefab) keyed off index+i so it's stable
                // across rebuilds without needing a stored seed.
                float sizeJitter = 0.82f + ((index * 3 + i) % 5) * 0.09f;
                float trunkHeight = 0.9f * sizeJitter;

                GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                trunk.name = "Generic tree trunk";
                trunk.transform.SetParent(transform);
                trunk.transform.position = treePosition + Vector3.up * trunkHeight;
                trunk.transform.localScale = new Vector3(0.18f * sizeJitter, trunkHeight, 0.18f * sizeJitter);
                trunk.GetComponent<Renderer>().sharedMaterial = treeBarkMaterial;
                MakeVisualOnly(trunk);

                GameObject crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Generic tree crown";
                crown.transform.SetParent(transform);
                crown.transform.position = treePosition + Vector3.up * (trunkHeight * 2f + 0.25f);
                crown.transform.localScale = new Vector3(1.45f, 1.15f, 1.45f) * sizeJitter;
                crown.GetComponent<Renderer>().sharedMaterial = foliageMaterial;
                MakeVisualOnly(crown);

                // Second, smaller lobe offset to one side so the canopy silhouette breaks
                // out of "single perfect sphere" the moment the camera gets close.
                GameObject lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lobe.name = "Generic tree crown lobe";
                lobe.transform.SetParent(transform);
                lobe.transform.position = treePosition + Vector3.up * (trunkHeight * 2f - 0.1f) + new Vector3(0.55f * sizeJitter, 0f, 0.4f * sizeJitter) * (i % 2 == 0 ? 1f : -1f);
                lobe.transform.localScale = new Vector3(0.85f, 0.72f, 0.85f) * sizeJitter;
                lobe.GetComponent<Renderer>().sharedMaterial = foliageMaterial;
                MakeVisualOnly(lobe);
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
            GameObject line = new GameObject("AI racing line");
            line.transform.SetParent(transform);
            LineRenderer renderer = line.AddComponent<LineRenderer>();
            renderer.useWorldSpace = true;
            renderer.loop = true;
            renderer.widthMultiplier = 0.16f;
            renderer.positionCount = Runtime.centerLine.Count;
            renderer.sharedMaterial = CreateMaterial("Racing line material", new Color(0.1f, 0.78f, 0.42f), 0f, 0.7f, new Color(0.02f, 0.18f, 0.06f));
            for (int i = 0; i < Runtime.centerLine.Count; i++)
            {
                renderer.SetPosition(i, Runtime.centerLine[i] + Vector3.up * 0.06f);
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

        bool TryPlaceSolidObstacle(GameObject obstacle, string obstacleType, Vector3 desiredBasePosition, Vector3 forward, Vector3 localScale, float verticalOffset, float minimumClearance)
        {
            Vector3 candidate = desiredBasePosition + Vector3.up * verticalOffset;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            obstacle.transform.rotation = rotation;
            obstacle.transform.position = candidate;

            if (!IsObstacleClearOfRacingSurface(candidate, forward, localScale, minimumClearance))
            {
                TrackProgress progress = Runtime.GetProgress(desiredBasePosition);
                Vector3 trackRight = Vector3.Cross(Vector3.up, progress.forward).normalized;
                float side = Mathf.Sign(progress.lateralDistance);
                if (Mathf.Abs(side) < 0.1f)
                {
                    side = Mathf.Sign(Vector3.Dot(desiredBasePosition - progress.nearestPoint, trackRight));
                    if (Mathf.Abs(side) < 0.1f)
                    {
                        side = 1f;
                    }
                }

                // Barrier gap fix: search outward starting from how far the
                // obstacle was ORIGINALLY meant to sit (its own desired lateral
                // distance from the centerline), not from a small
                // minimumClearance-based fallback. The old fallback reset every
                // repaired segment to roughly roadHalfWidth+minimumClearance,
                // which on a barrier placed deliberately much further out (or on
                // a tight corner where curvature makes the naive chord position
                // read as falsely "too close") snapped it back to a much closer
                // distance than its straight-section neighbours - exactly the
                // kind of inconsistent kink/gap in an otherwise continuous wall
                // this pass exists to prevent. Nudging outward in small steps
                // from the original intent instead keeps a repaired segment
                // visually consistent with the rest of the barrier line.
                float desiredLateral = Mathf.Max(Runtime.HalfWidthAt(progress.distance) + minimumClearance + localScale.x * 0.5f, Mathf.Abs(progress.lateralDistance));
                bool repaired = false;
                for (int step = 0; step < 10; step++)
                {
                    float lateral = desiredLateral + step * 0.6f;
                    candidate = progress.nearestPoint + trackRight * side * lateral + Vector3.up * verticalOffset;
                    obstacle.transform.position = candidate;
                    if (IsObstacleClearOfRacingSurface(candidate, forward, localScale, minimumClearance))
                    {
                        repaired = true;
                        break;
                    }
                }

                if (!repaired)
                {
                    GameLog.Warn("[TrackValidation] Removed " + obstacle.name + " because it intersected the racing surface near " + desiredBasePosition);
                    obstacle.SetActive(false);
                    Destroy(obstacle);
                    return false;
                }

                GameLog.Warn("[TrackValidation] Repositioned " + obstacle.name + " away from racing surface to " + candidate);
            }

            TrackSolidObstacle solid = obstacle.AddComponent<TrackSolidObstacle>();
            solid.obstacleType = obstacleType;
            solid.minimumClearance = minimumClearance;
            solid.localScaleAtValidation = localScale;
            solidObstacles.Add(solid);
            return true;
        }

        bool IsObstacleClearOfRacingSurface(Vector3 position, Vector3 forward, Vector3 localScale, float minimumClearance)
        {
            Vector3 flatForward = new Vector3(forward.x, 0f, forward.z).normalized;
            if (flatForward.sqrMagnitude < 0.1f)
            {
                flatForward = Vector3.forward;
            }

            Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
            float halfWidth = Mathf.Max(0.05f, localScale.x * 0.5f);
            float halfLength = Mathf.Max(0.05f, localScale.z * 0.5f);
            Vector3[] samples =
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

            if (Runtime.length < 3700f)
            {
                report.Warn("track length " + Runtime.length.ToString("0") + "m is INVALID: circuits must normalize to at least 3.7 km for race pacing.");
            }
            else if (Runtime.length > 6000f)
            {
                report.Warn("track length " + Runtime.length.ToString("0") + "m exceeds the expected normalization ceiling.");
            }

            for (int i = solidObstacles.Count - 1; i >= 0; i--)
            {
                TrackSolidObstacle obstacle = solidObstacles[i];
                if (obstacle == null)
                {
                    solidObstacles.RemoveAt(i);
                    continue;
                }

                if (!IsObstacleClearOfRacingSurface(obstacle.transform.position, obstacle.transform.forward, obstacle.localScaleAtValidation, obstacle.minimumClearance))
                {
                    report.invalidObstaclesRemoved++;
                    obstacle.gameObject.SetActive(false);
                    Destroy(obstacle.gameObject);
                    solidObstacles.RemoveAt(i);
                }
            }

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
