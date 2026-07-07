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

        public bool IsOnRoad(Vector3 worldPosition)
        {
            TrackProgress progress = GetProgress(worldPosition);
            return Mathf.Abs(progress.lateralDistance) <= roadHalfWidth;
        }

        public bool IsOnKerb(Vector3 worldPosition)
        {
            TrackProgress progress = GetProgress(worldPosition);
            float lateral = Mathf.Abs(progress.lateralDistance);
            return lateral >= kerbStart && lateral <= roadHalfWidth + 1.3f;
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

        public bool IsInPitApproach(float normalizedProgress)
        {
            return normalizedProgress > 0.78f && normalizedProgress < 0.955f;
        }

        public bool IsInPitEntryZone(float normalizedProgress)
        {
            return normalizedProgress > 0.865f && normalizedProgress < 0.955f;
        }

        public bool IsInPitExitLimiterZone(float normalizedProgress)
        {
            return normalizedProgress > 0.955f || normalizedProgress < 0.115f;
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
            SampleAtDistance(PitBoxDistance(pitBoxIndex) - Mathf.Max(4f, holdBackMeters), out point, out forward, out right);
            position = point + right * PitLaneLateral + Vector3.up * 0.58f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void GetPitEntryPose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(length * 0.885f, out point, out forward, out right);
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
        Material asphaltPatchMaterial;
        Material skidMarkMaterial;
        Material barrierMaterial;
        Material tireBarrierMaterial;
        Material concreteMaterial;
        Material fenceMaterial;
        Material fencePostMaterial;
        Material foliageMaterial;
        Material metalMaterial;
        Material glassMaterial;
        Material lightGlowMaterial;
        Material sceneryAccentMaterial;
        Material trafficConeMaterial;
        PhysicMaterial roadPhysicsMaterial;
        PhysicMaterial runoffPhysicsMaterial;
        Mesh visualBoxMesh;
        readonly List<TrackSolidObstacle> solidObstacles = new List<TrackSolidObstacle>();

        // World Y of the flat terrain surface; road above this by more than the
        // threshold counts as elevated (bridge/overpass/hillside) and gets full
        // side containment instead of sparse runoff markers.
        float groundTopY;
        const float ElevationThreshold = 1.35f;
        const float TallFenceElevation = 3f;
        const float SafetyBarrierSpacing = 9f;

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
        Material edgeGlowMaterial;
        Material[] neonMaterials;
        Material yachtMaterial;
        Material toriiMaterial;

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
            nightTrack = trackId.Contains("singapore") || trackId.Contains("las_vegas") || trackId.Contains("qatar");
            twilightTrack = trackId.Contains("abu_dhabi");
            desertTrack = trackId.Contains("bahrain") || trackId.Contains("qatar") || trackId.Contains("abu_dhabi");
            streetTrack = Runtime.styleName.Contains("street") || Runtime.styleName.Contains("Street");
            monacoTrack = trackId.Contains("monaco");
            suzukaTrack = trackId.Contains("suzuka");
            spaTrack = trackId.Contains("spa");
            neonTrack = trackId.Contains("singapore") || trackId.Contains("las_vegas");
            wetTrack = Runtime.weather == WeatherState.LightRain || Runtime.weather == WeatherState.HeavyRain;
            CreateMaterials();
            BuildGround();
            BuildContinuousSafetyFloor();
            BuildRoadMesh();
            BuildRoadPaint();
            BuildAsphaltDetail();
            BuildGridPaint();
            BuildKerbs();
            BuildBarriers();
            BuildSafetyBarriers();
            BuildTrackMarkers();
            BuildDrsZoneBoards();
            BuildPitLane();
            BuildStartGantry();
            BuildScenery();
            BuildCircuitLandmarks();
            BuildEnvironmentIdentity();
            BuildCameraTowers();
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
            return Runtime;
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
            asphaltPatchMaterial = CreateMaterial("Runtime Asphalt Patch", new Color(0.033f, 0.036f, 0.039f), 0f, rain ? 0.72f : 0.5f);
            skidMarkMaterial = CreateMaterial("Runtime Skid Mark", new Color(0.001f, 0.001f, 0.001f, 0.92f), 0f, 0.16f);
            barrierMaterial = CreateMaterial("Runtime Barrier", monacoTrack ? new Color(0.86f, 0.85f, 0.8f) : new Color(0.68f, 0.72f, 0.74f), 0.12f, monacoTrack ? 0.55f : 0.62f);
            barrierMaterial.mainTexture = BuildNoiseTexture(64, new Color(0.87f, 0.87f, 0.87f), 0.1f);
            barrierMaterial.mainTextureScale = new Vector2(4f, 1.5f);
            tireBarrierMaterial = CreateMaterial("Runtime Tyre Barrier", new Color(0.015f, 0.016f, 0.017f), 0.02f, 0.28f);
            concreteMaterial = CreateMaterial("Runtime Concrete Wall", desertTrack ? new Color(0.72f, 0.66f, 0.5f) : new Color(0.56f, 0.58f, 0.59f), 0.04f, desertTrack ? 0.5f : 0.32f, desertTrack ? new Color(0.06f, 0.05f, 0.02f) : Color.black);
            concreteMaterial.mainTexture = BuildNoiseTexture(64, new Color(0.85f, 0.85f, 0.85f), 0.09f);
            concreteMaterial.mainTextureScale = new Vector2(3f, 1f);
            fenceMaterial = CreateMaterial("Runtime Catch Fence", new Color(0.14f, 0.16f, 0.18f), 0.42f, 0.44f);
            fencePostMaterial = CreateMaterial("Runtime Fence Post", new Color(0.4f, 0.44f, 0.47f), 0.55f, 0.66f);
            foliageMaterial = CreateMaterial("Runtime Foliage", spaTrack ? new Color(0.05f, 0.22f, 0.14f) : new Color(0.04f, 0.32f, 0.12f), 0f, 0.42f);
            metalMaterial = CreateMaterial("Runtime Brushed Metal", new Color(0.52f, 0.56f, 0.58f), 0.42f, 0.78f);
            glassMaterial = CreateMaterial("Runtime Glass", new Color(0.12f, 0.28f, 0.38f, 0.85f), 0.1f, 0.95f);
            lightGlowMaterial = CreateMaterial("Runtime Light Glow", new Color(1f, 0.85f, 0.4f), 0f, 0.92f, new Color(1f, 0.62f, 0.15f));
            sceneryAccentMaterial = CreateMaterial("Runtime Scenery Accent", new Color(0.92f, 0.03f, 0.025f), 0.05f, 0.65f);
            trafficConeMaterial = CreateMaterial("Runtime Traffic Cone", new Color(0.95f, 0.42f, 0.03f), 0f, 0.5f);
            edgeGlowMaterial = nightTrack || twilightTrack
                ? CreateMaterial("Runtime Edge Glow", new Color(0.85f, 0.95f, 1f), 0.05f, 0.85f, new Color(0.32f, 0.42f, 0.6f))
                : roadEdgeMaterial;

            // Vegas/Singapore neon palette; kept small and shared rather than one
            // material per building so the neon look doesn't balloon the material count.
            neonMaterials = new[]
            {
                CreateMaterial("Runtime Neon Cyan", new Color(0.05f, 0.85f, 0.92f), 0f, 0.9f, new Color(0.15f, 1.7f, 1.85f)),
                CreateMaterial("Runtime Neon Magenta", new Color(0.9f, 0.05f, 0.8f), 0f, 0.9f, new Color(1.7f, 0.1f, 1.5f)),
                CreateMaterial("Runtime Neon Amber", new Color(0.95f, 0.55f, 0.05f), 0f, 0.9f, new Color(1.8f, 0.9f, 0.05f))
            };
            yachtMaterial = CreateMaterial("Runtime Yacht Hull", new Color(0.94f, 0.94f, 0.92f), 0.15f, 0.88f);
            toriiMaterial = CreateMaterial("Runtime Torii Red", new Color(0.62f, 0.13f, 0.06f), 0.02f, 0.4f);
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
                vertices[i * 2] = point - right * Runtime.roadHalfWidth + Vector3.up * 0.015f;
                vertices[i * 2 + 1] = point + right * Runtime.roadHalfWidth + Vector3.up * 0.015f;
                float v = Runtime.cumulativeDistances[i] / 12f; // Tiled UV for asphalt detail
                uvs[i * 2] = new Vector2(0f, v);
                uvs[i * 2 + 1] = new Vector2(Runtime.roadHalfWidth * 0.5f, v);

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

                // Edge lines; emissive at night so the circuit reads under floodlights.
                CreateRoadStripe(point - right * (Runtime.roadHalfWidth - 0.45f), forward, 0.25f, spacing * 0.95f, edgeGlowMaterial, "Left edge line", 0);
                CreateRoadStripe(point + right * (Runtime.roadHalfWidth - 0.45f), forward, 0.25f, spacing * 0.95f, edgeGlowMaterial, "Right edge line", 0);

                // Racing line rubbering
                if (Mathf.FloorToInt(d / spacing) % 2 == 0)
                {
                    float lateralOffset = Mathf.Sin(d * 0.02f) * (Runtime.roadHalfWidth * 0.35f);
                    CreateRoadStripe(point + right * lateralOffset, forward, 4.2f, spacing * 1.1f, rubberMaterial, "Rubbered racing line", 1);
                    CreateRoadStripe(point + right * (lateralOffset + 0.15f), forward, 1.2f, spacing * 0.5f, rubberMaterial, "Rubbered skid mark", 2);
                }

                float normalized = d / Mathf.Max(1f, Runtime.length);
                if (Runtime.IsInDrsZone(normalized) && Mathf.FloorToInt(d / spacing) % 2 == 0)
                {
                    CreateRoadStripe(point - right * (Runtime.roadHalfWidth - 1.5f), forward, 0.8f, 8f, drsPaintMaterial, "DRS zone paint", 3);
                    CreateRoadStripe(point + right * (Runtime.roadHalfWidth - 1.5f), forward, 0.8f, 8f, drsPaintMaterial, "DRS zone paint", 3);
                }
            }
        }

        void BuildAsphaltDetail()
        {
            float spacing = 24f;
            for (float d = 0f; d < Runtime.length; d += spacing)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                float normalized = d / Mathf.Max(1f, Runtime.length);
                float laneBias = Mathf.Sin(normalized * Mathf.PI * 10f) * 0.34f;
                CreateRoadStripe(point + right * laneBias, forward, Runtime.roadHalfWidth * 0.82f, spacing * 0.76f, asphaltPatchMaterial, "Asphalt grain variation", 4);
                CreateRoadStripe(point + right * (laneBias * 0.45f), forward, Runtime.roadHalfWidth * 0.42f, spacing * 0.82f, rubberMaterial, "Dark racing line rubber", 5);

                if (Mathf.FloorToInt(d / spacing) % 4 == 1)
                {
                    CreateRoadStripe(point - right * 1.05f, forward, 0.16f, 7.6f, skidMarkMaterial, "Heavy braking skid mark", 6);
                    CreateRoadStripe(point + right * 1.25f, forward, 0.14f, 6.8f, skidMarkMaterial, "Heavy braking skid mark", 6);
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

                for (float offset = -kerbLength * 0.5f; offset <= kerbLength * 0.5f; offset += 5.5f)
                {
                    Vector3 point;
                    Vector3 forward;
                    Vector3 right;
                    Runtime.SampleAtDistance(Runtime.cumulativeDistances[i] + offset, out point, out forward, out right);

                    // Outer kerb (Apex or Exit)
                    Vector3 outer = point + right * turnSign * (Runtime.roadHalfWidth + 0.35f);
                    CreateKerbBlock(outer, forward, Runtime.cumulativeDistances[i] + offset);

                    // Inner kerb (if sharp turn)
                    if (angle > 35f)
                    {
                        Vector3 inner = point - right * turnSign * (Runtime.roadHalfWidth + 0.25f);
                        CreateKerbBlock(inner, forward, Runtime.cumulativeDistances[i] + offset + 2f);
                    }
                }
            }
        }

        void BuildBarriers()
        {
            bool streetCircuit = Runtime.roadHalfWidth < 8f || Runtime.styleName.Contains("street") || Runtime.styleName.Contains("Street");
            float spacing = streetCircuit ? 18f : 30f;
            for (float d = 0f; d < Runtime.length; d += spacing)
            {
                // Elevated stretches get continuous walls and fences from
                // BuildSafetyBarriers instead of these sparse markers.
                if (IsElevatedAtDistance(d) || IsElevatedAtDistance(d + spacing * 0.5f))
                {
                    continue;
                }

                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                bool street = streetCircuit;
                int stripeIndex = Mathf.FloorToInt(d / spacing);
                CreateBarrier(point - right * (Runtime.roadHalfWidth + (street ? 2f : 5.5f)), forward, street, stripeIndex);

                // Leave the right side clear where the pit lane runs (entry through exit),
                // otherwise barrier segments spawn straight across the pit corridor.
                float normalized = d / Mathf.Max(1f, Runtime.length);
                bool insidePitCorridor = normalized > 0.83f || normalized < 0.06f;
                if (!insidePitCorridor)
                {
                    CreateBarrier(point + right * (Runtime.roadHalfWidth + (street ? 2f : 5.5f)), forward, street, stripeIndex + 1);
                }
            }
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

        // Continuous concrete wall + catch fence along every elevated stretch, both
        // sides, chord-following so curves stay sealed. Respawn remains only as a
        // fallback; these are the primary containment.
        void BuildSafetyBarriers()
        {
            bool previousElevated = IsElevatedAtDistance(-SafetyBarrierSpacing);
            for (float d = 0f; d < Runtime.length; d += SafetyBarrierSpacing)
            {
                bool elevated = IsElevatedAtDistance(d) || IsElevatedAtDistance(d + SafetyBarrierSpacing * 0.5f) || IsElevatedAtDistance(d + SafetyBarrierSpacing);
                if (elevated)
                {
                    float normalized = d / Mathf.Max(1f, Runtime.length);
                    bool insidePitCorridor = normalized > 0.83f || normalized < 0.06f;

                    CreateBridgeFenceSegment(d, -1f, Runtime.roadHalfWidth + 1.15f);

                    // Elevated safety overrides the pit-lane visual gap, but the wall
                    // moves outward past the whole pit complex (service road, boxes,
                    // crew) so the corridor stays usable while the drop stays sealed.
                    float rightLateral = insidePitCorridor ? Mathf.Max(Runtime.roadHalfWidth + 17f, 30f) : Runtime.roadHalfWidth + 1.15f;
                    CreateBridgeFenceSegment(d, 1f, rightLateral);

                    if (ElevationAboveGround(d) > 4f && Mathf.FloorToInt(d / SafetyBarrierSpacing) % 3 == 0)
                    {
                        CreateBridgeSupports(d);
                    }
                }

                // Soften the transition into and out of an elevated stretch with tyre
                // barrier stacks so run-off areas funnel cars back before the drop.
                if (elevated != previousElevated)
                {
                    CreateTransitionTyreStacks(d);
                }

                previousElevated = elevated;
            }
        }

        // One chord-aligned protection segment on one side: low concrete wall plus a
        // tall collidable catch fence with visual posts and rails.
        void CreateBridgeFenceSegment(float distance, float side, float lateral)
        {
            Vector3 a;
            Vector3 b;
            Vector3 mid;
            Vector3 forward;
            Vector3 right;
            Vector3 discard;
            Runtime.SampleAtDistance(distance, out a, out discard, out right);
            Runtime.SampleAtDistance(distance + SafetyBarrierSpacing, out b, out discard, out right);
            Runtime.SampleAtDistance(distance + SafetyBarrierSpacing * 0.5f, out mid, out forward, out right);

            Vector3 chord = b - a;
            float segmentLength = Mathf.Max(4f, chord.magnitude) + 1.6f;
            Vector3 chordForward = chord.sqrMagnitude > 0.01f ? chord.normalized : forward;
            Vector3 basePosition = mid + right * side * lateral;

            CreateConcreteWall(basePosition, chordForward, segmentLength);
            if (NeedsCatchFence(distance))
            {
                CreateCatchFence(basePosition, chordForward, segmentLength);
            }
        }

        void CreateConcreteWall(Vector3 basePosition, Vector3 forward, float segmentLength)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Bridge concrete wall";
            wall.transform.SetParent(transform);
            Vector3 scale = new Vector3(0.5f, 1.25f, segmentLength);
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = concreteMaterial;
            TryPlaceSolidObstacle(wall, "bridge-wall", basePosition, forward, scale, 0.62f, 0.5f);
        }

        void CreateCatchFence(Vector3 basePosition, Vector3 forward, float segmentLength)
        {
            GameObject fence = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fence.name = "Catch fence";
            fence.transform.SetParent(transform);
            Vector3 scale = new Vector3(0.18f, 2.6f, segmentLength);
            fence.transform.localScale = scale;
            fence.GetComponent<Renderer>().sharedMaterial = fenceMaterial;
            if (!TryPlaceSolidObstacle(fence, "catch-fence", basePosition, forward, scale, 2.5f, 0.5f))
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
            CreateVisualBox("Bridge support crossbeam", new Vector3(point.x, point.y - 0.55f, point.z), Quaternion.LookRotation(forward, Vector3.up), new Vector3(Runtime.roadHalfWidth * 2f + 1.6f, 0.5f, 1.9f), concreteMaterial);
        }

        void CreateTransitionTyreStacks(float distance)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject stack = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stack.name = "Runoff tyre barrier stack";
                stack.transform.SetParent(transform);
                Vector3 scale = new Vector3(1.2f, 1f, 4.6f);
                stack.transform.localScale = scale;
                stack.GetComponent<Renderer>().sharedMaterial = tireBarrierMaterial;
                TryPlaceSolidObstacle(stack, "tyre-barrier", point + right * side * (Runtime.roadHalfWidth + 2.6f), forward, scale, 0.5f, 0.7f);
            }
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

        void CreateBarrier(Vector3 position, Vector3 forward, bool street, int stripeIndex)
        {
            GameObject barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrier.name = street ? "Street wall" : "Runoff marker";
            barrier.transform.SetParent(transform);
            Vector3 scale = street ? new Vector3(0.45f, 1.1f, 7.5f) : new Vector3(0.35f, 0.35f, 4.6f);
            barrier.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            barrier.transform.localScale = scale;

            // Alternating painted segments so walls and markers read as trackside
            // furniture instead of one endless grey slab.
            Material segmentMaterial = street
                ? (stripeIndex % 2 == 0 ? barrierMaterial : sceneryAccentMaterial)
                : (stripeIndex % 3 == 0 ? sceneryAccentMaterial : tireBarrierMaterial);
            barrier.GetComponent<Renderer>().sharedMaterial = segmentMaterial;
            if (!TryPlaceSolidObstacle(barrier, street ? "street-wall" : "runoff-barrier", position, forward, scale, street ? 0.55f : 0.18f, street ? 1.15f : 6.25f))
            {
                return;
            }

            // Low rub-rail strip on street walls for depth.
            if (street)
            {
                Vector3 placed = barrier.transform.position;
                Vector3 placedForward = barrier.transform.forward;
                CreateVisualBox("Street wall rub rail", placed + Vector3.up * 0.62f, Quaternion.LookRotation(placedForward, Vector3.up), new Vector3(0.5f, 0.12f, 7.5f), metalMaterial);

                if (nightTrack || twilightTrack)
                {
                    CreateVisualBox("Street wall light strip", placed + Vector3.up * 1.05f, Quaternion.LookRotation(placedForward, Vector3.up), new Vector3(0.4f, 0.08f, 7.2f), lightGlowMaterial);
                }
            }
            else if (nightTrack || twilightTrack)
            {
                Vector3 placed = barrier.transform.position;
                Vector3 placedForward = barrier.transform.forward;
                CreateVisualBox("Runoff marker light strip", placed + Vector3.up * 0.55f, Quaternion.LookRotation(placedForward, Vector3.up), new Vector3(0.34f, 0.07f, 4.4f), lightGlowMaterial);
            }
        }

        void CreateKerbBlock(Vector3 position, Vector3 forward, float seed)
        {
            bool whiteBase = Mathf.FloorToInt(seed / 16f) % 2 == 0;
            Material material = whiteBase ? lineMaterial : kerbMaterial;
            Material accentMaterial = whiteBase ? kerbMaterial : lineMaterial;

            GameObject kerb = CreateVisualBox("Painted kerb", position + Vector3.up * 0.075f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(1.15f, 0.09f, 4.5f), material);
            MeshRenderer renderer = kerb.GetComponent<MeshRenderer>();
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Simple;

            // Inlay stripe on top of the block so each kerb reads as painted sausage
            // kerbing instead of one flat coloured slab; stacked above the block's top
            // face rather than coplanar with it, so there is no z-fighting risk.
            CreateVisualBox("Painted kerb accent", position + Vector3.up * 0.131f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(1f, 0.02f, 1.6f), accentMaterial);
        }

        void BuildTrackMarkers()
        {
            CreateTrackLine(0f, "Start finish", Color.white, 2.4f);
            CreateTrackLine(Runtime.length * 0.333f, "Sector 1 line", new Color(0.1f, 0.75f, 1f), 1.2f);
            CreateTrackLine(Runtime.length * 0.666f, "Sector 2 line", new Color(0.1f, 1f, 0.45f), 1.2f);
            CreateSectorBoard(Runtime.length * 0.333f, new Color(0.1f, 0.75f, 1f));
            CreateSectorBoard(Runtime.length * 0.666f, new Color(0.1f, 1f, 0.45f));

            // Distance boards for major braking zones
            for (int i = 0; i < Runtime.centerLine.Count; i++)
            {
                Vector3 current = Runtime.centerLine[i];
                Vector3 next = Runtime.centerLine[(i + 1) % Runtime.centerLine.Count];
                if (Vector3.Angle((next - current).normalized, (Runtime.centerLine[(i + 2) % Runtime.centerLine.Count] - next).normalized) > 35f)
                {
                    float dist = Runtime.cumulativeDistances[i];
                    CreateBrakingBoard(dist - 150f, "150");
                    CreateBrakingBoard(dist - 100f, "100");
                    CreateBrakingBoard(dist - 50f, "50");
                    CreateApexCones(dist);
                }
            }
        }

        void CreateSectorBoard(float distance, Color color)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            Material boardMaterial = CreateMaterial("Sector board material", color, 0.05f, 0.7f, nightTrack ? color * 0.4f : Color.black);
            CreateVisualBox("Sector board", point - right * (Runtime.roadHalfWidth + 3.2f) + Vector3.up * 2.1f, Quaternion.LookRotation(right, Vector3.up), new Vector3(0.14f, 1f, 1.6f), boardMaterial);
            CreateVisualBox("Sector board post", point - right * (Runtime.roadHalfWidth + 3.2f) + Vector3.up * 0.8f, Quaternion.LookRotation(right, Vector3.up), new Vector3(0.12f, 1.6f, 0.12f), metalMaterial);
        }

        // Small marshal hut with a flag pole; placed sparsely around the lap.
        void CreateMarshalPost(Vector3 position, Vector3 forward, int index)
        {
            Vector3 safePosition = PushSceneryClearOfTrack(position, 6.5f);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            CreateVisualBox("Marshal post hut", safePosition + Vector3.up * 0.75f, rotation, new Vector3(1.7f, 1.5f, 1.7f), barrierMaterial);
            CreateVisualBox("Marshal post roof", safePosition + Vector3.up * 1.62f, rotation, new Vector3(1.95f, 0.16f, 1.95f), sceneryAccentMaterial);
            CreateVisualBox("Marshal flag pole", safePosition + Vector3.up * 2.6f, rotation, new Vector3(0.08f, 1.9f, 0.08f), metalMaterial);
            CreateVisualBox("Marshal flag", safePosition + Vector3.up * 3.3f + forward * 0.32f, rotation, new Vector3(0.05f, 0.4f, 0.62f), index % 2 == 0 ? sceneryAccentMaterial : lineMaterial);
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

            // The board's readable face points along the track direction (same as the
            // braking boards), so the text is pushed proud along -forward rather than
            // buried inside the solid board.
            GameObject text = new GameObject("DRS zone board text");
            text.transform.SetParent(transform);
            text.transform.position = basePosition + Vector3.up * 2.3f - forward.normalized * 0.11f;
            text.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            TextMesh textMesh = text.AddComponent<TextMesh>();
            textMesh.text = "DRS";
            textMesh.fontSize = 48;
            textMesh.characterSize = 0.16f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.9f, 0.96f, 1f, 0.95f);
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

        static readonly Color[] SponsorPalette =
        {
            new Color(0.85f, 0.1f, 0.1f), new Color(0.05f, 0.35f, 0.85f),
            new Color(0.95f, 0.75f, 0.05f), new Color(0.1f, 0.55f, 0.2f)
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

        void BuildPitLane()
        {
            Material pitMaterial = CreateMaterial("Pit lane material", new Color(0.12f, 0.13f, 0.15f), 0.02f, 0.55f);

            // The pit corridor follows the track from just before pit entry to the
            // release point, so surfaces, walls, boxes, and buildings are sampled
            // along the lap distance instead of assuming one straight chord.
            float corridorStart = Runtime.length * 0.885f;
            float corridorEnd = Runtime.length * 0.995f;

            // Drivable service road, laid in curve-following segments.
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
                    new Vector3(13.5f, 0.16f, 17.6f),
                    pitMaterial);
            }

            CreatePitEntryExitSurfaces(pitMaterial);
            CreatePitEntryExitPaint(pitMaterial);
            CreatePitLaneCones();

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
            }
        }

        void CreatePitEntryExitSurfaces(Material pitMaterial)
        {
            Vector3 entry;
            Vector3 entryForward;
            Vector3 entryRight;
            Runtime.SampleAtDistance(Runtime.length * 0.865f, out entry, out entryForward, out entryRight);
            CreateCollidablePitSurface("Pit entry asphalt", entry + entryRight * (Runtime.roadHalfWidth + 5.1f) + Vector3.up * 0.012f, Quaternion.LookRotation(entryForward, Vector3.up), new Vector3(7.6f, 0.16f, 42f), pitMaterial);

            Vector3 exit;
            Vector3 exitForward;
            Vector3 exitRight;
            Runtime.SampleAtDistance(Runtime.length * 0.992f, out exit, out exitForward, out exitRight);
            CreateCollidablePitSurface("Pit release asphalt", exit + exitRight * (Runtime.roadHalfWidth + 4.5f) + Vector3.up * 0.012f, Quaternion.LookRotation(exitForward, Vector3.up), new Vector3(7.4f, 0.16f, 42f), pitMaterial);

            Runtime.SampleAtDistance(Runtime.length * 0.035f, out exit, out exitForward, out exitRight);
            CreateCollidablePitSurface("Pit exit asphalt", exit + exitRight * (Runtime.roadHalfWidth + 4.7f) + Vector3.up * 0.012f, Quaternion.LookRotation(exitForward, Vector3.up), new Vector3(7.4f, 0.16f, 48f), pitMaterial);
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
            wall.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 scale = new Vector3(0.42f, 0.84f, 6.2f);
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = metalMaterial;
            TryPlaceSolidObstacle(wall, "pit-wall", position, forward, scale, 0.42f, 0.9f);
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

        void BuildCameraTowers()
        {
            float[] positions = { 0.1f, 0.3f, 0.52f, 0.72f };
            for (int i = 0; i < positions.Length; i++)
            {
                CreateCameraTower(Runtime.length * positions[i], i);
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

            CreateVisualBox("Camera tower leg", legBase + Vector3.up * legHeight * 0.5f, rotation, new Vector3(0.6f, legHeight, 0.6f), metalMaterial);
            CreateVisualBox("Camera tower platform", platformCenter, rotation, new Vector3(2.2f, 0.18f, 2.2f), metalMaterial);
            CreateVisualBox("Camera tower rail", platformCenter + Vector3.up * 0.55f, rotation, new Vector3(2.2f, 0.9f, 0.12f), fencePostMaterial);

            GameObject camHead = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            camHead.name = "Camera tower head";
            camHead.transform.SetParent(transform);
            camHead.transform.position = platformCenter + Vector3.up * 1.1f;
            camHead.transform.rotation = rotation * Quaternion.Euler(90f, 0f, 0f);
            camHead.transform.localScale = new Vector3(0.3f, 0.5f, 0.3f);
            camHead.GetComponent<Renderer>().sharedMaterial = concreteMaterial;
            MakeVisualOnly(camHead);
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

                // Trackside detail: floodlights and marshal posts.
                if (i % 8 == 0)
                {
                    CreateFloodlight(point + right * side * (Runtime.roadHalfWidth + 6.5f), forward, night || nightTrack || street);
                }

                if (i % 12 == 4)
                {
                    CreateMarshalPost(point + right * side * (Runtime.roadHalfWidth + 9f), forward, i);
                }

                if (i % 14 == 9)
                {
                    CreateSponsorBoard(point + right * side * (Runtime.roadHalfWidth + 4.6f), forward, i);
                }

                if (neonTrack && i % 16 == 6)
                {
                    CreateNeonPylon(point + right * side * (Runtime.roadHalfWidth + 11f), forward, i);
                }

                Vector3 basePosition = point + right * side * (Runtime.roadHalfWidth + (street ? 18f : 32f));
                if (street)
                {
                    // Tight street circuits keep buildings close; Monaco/Singapore/Vegas
                    // already ride this same lateral offset, but a closer second row on
                    // some passes sells the "canyon of buildings" feel harder.
                    CreateCityBlock(basePosition, forward, i, night);
                    if (i % 7 == 0) CreateCityBlock(point + right * side * (Runtime.roadHalfWidth + 15f), forward, i + 2, night);
                }
                else if (desertTrack)
                {
                    CreateDune(basePosition, i);
                    if (i % 5 == 0) CreateDune(basePosition + right * side * 38f, i + 3);
                    if (i % 9 == 0) CreateTreeCluster(basePosition - right * side * 22f, i);
                }
                else
                {
                    CreateTreeCluster(basePosition, i);
                    if (spaTrack || i % 6 == 0) CreateTreeCluster(basePosition + right * side * 40f, i);
                }
            }
        }

        // One-off signature set pieces for circuits that need more than a colour swap
        // to read as themselves: Monaco's harbour, Suzuka's crossover bridge and torii
        // silhouette, and Spa's Ardennes mist.
        void BuildCircuitLandmarks()
        {
            if (monacoTrack)
            {
                BuildHarbourYachts(0.34f, 5);
            }

            if (suzukaTrack)
            {
                CreateSuzukaCrossoverBridge(Runtime.length * 0.5f);
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * 0.22f, out point, out forward, out right);
                CreateToriiGate(point + right * (Runtime.roadHalfWidth + 16f), forward);
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

            bool parkland = id.Contains("spa") || id.Contains("austria") || id.Contains("red_bull_ring") ||
                            id.Contains("suzuka") || id.Contains("silverstone") || id.Contains("monza");
            bool cityStreet = streetTrack && !monacoTrack && !neonTrack;
            bool nightNeon = neonTrack || style.Contains("night");

            // Baseline distant ridge behind every circuit so nothing ever reads as flat;
            // archetype passes below layer their own identity on top of this rather than
            // replacing it.
            Color ridgeTint = desertTrack ? new Color(0.62f, 0.5f, 0.34f) : (parkland ? new Color(0.24f, 0.3f, 0.24f) : new Color(0.34f, 0.38f, 0.46f));
            BuildMountainBackdrop(density, ridgeTint);

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

            if (parkland)
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
            CreatePaddockComplex(0.08f, 1);
            CreatePaddockComplex(0.58f, -1);

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
            Vector3 desired = point + right * side * (Runtime.roadHalfWidth + 70f);
            Vector3 basePosition;
            if (!TryGetClearScenerySpot(desired, 40f, 8f, out basePosition))
            {
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
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
            int clusters = Mathf.Max(3, Mathf.RoundToInt(6f * density));
            for (int i = 0; i < clusters; i++)
            {
                float t = (i + 0.5f) / clusters;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                Vector3 anchor = point + right * side * (Runtime.roadHalfWidth + 46f);
                CreateProceduralBuildingCluster(anchor, forward, 3, 14f + (i % 3) * 6f, false);
            }
        }

        // Denser lit skyline for Singapore/Las Vegas-style night circuits, building on the
        // existing CreateNeonPylon trackside strips with a further-back row of taller neon
        // towers so the horizon itself glows.
        void BuildNeonSkylineBackdrop(float density)
        {
            int clusters = Mathf.Max(4, Mathf.RoundToInt(8f * density));
            for (int i = 0; i < clusters; i++)
            {
                float t = (i + 0.5f) / clusters;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                Vector3 anchor = point + right * side * (Runtime.roadHalfWidth + 60f);
                CreateProceduralBuildingCluster(anchor, forward, 3, 26f + (i % 3) * 8f, true);
            }
        }

        // Modern street-circuit skyline (Jeddah/Baku/Madrid/Miami-style): a further-back
        // skyline row plus one or two sponsor bridges spanning the track on the longer
        // straights.
        void BuildCityStreetBackdrop(float density)
        {
            int clusters = Mathf.Max(3, Mathf.RoundToInt(6f * density));
            for (int i = 0; i < clusters; i++)
            {
                float t = (i + 0.5f) / clusters;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.length * t, out point, out forward, out right);
                int side = i % 2 == 0 ? -1 : 1;
                Vector3 anchor = point + right * side * (Runtime.roadHalfWidth + 55f);
                CreateProceduralBuildingCluster(anchor, forward, 3, 20f + (i % 3) * 7f, false);
            }

            CreateSponsorBridge(Runtime.length * 0.22f);
            if (density > 0.6f)
            {
                CreateSponsorBridge(Runtime.length * 0.68f);
            }
        }

        // Cluster of boxy buildings around a point, shared by the Monaco/city-street/neon
        // skyline passes. Each building is individually clearance-checked (not just the
        // cluster anchor) since the spread can push outer buildings back toward the
        // corridor on tighter circuits.
        void CreateProceduralBuildingCluster(Vector3 anchor, Vector3 forward, int count, float baseHeight, bool neonStyle)
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
                if (!TryGetClearScenerySpot(desired, footprint * 0.75f, 4f, out safePosition))
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

                Material windowMaterial = neonStyle ? neonMaterials[i % neonMaterials.Length] : (nightTrack || twilightTrack ? lightGlowMaterial : glassMaterial);
                int bands = Mathf.Clamp(Mathf.RoundToInt(height / 3f), 1, 4);
                for (int band = 0; band < bands; band++)
                {
                    CreateVisualBox("Skyline window band", safePosition + Vector3.up * (1.5f + band * 2.6f), rotation, new Vector3(footprint * 0.8f, 0.6f, 0.08f), windowMaterial);
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
                Vector3 basePosition = point + right * side * (Runtime.roadHalfWidth + 20f);
                Vector3 lateral = right * side;
                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
                for (int row = 0; row < 9; row++)
                {
                    Vector3 rowCenter = basePosition + Vector3.up * (0.4f + row * 0.68f) + lateral * row * 1.3f;
                    CreateVisualBox("Stadium bowl tier", rowCenter, rotation, new Vector3(1.3f, 0.55f, 46f), metalMaterial);
                    CreateVisualBox("Stadium bowl crowd block", rowCenter + Vector3.up * 0.5f, rotation, new Vector3(0.95f, 0.46f, 45f), row % 2 == 0 ? sceneryAccentMaterial : glassMaterial);
                }

                Vector3 roofCenter = basePosition + Vector3.up * 8.2f + lateral * 4.6f;
                CreateVisualBox("Stadium bowl roof", roofCenter, rotation, new Vector3(13f, 0.3f, 48f), metalMaterial);
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
            Vector3 desired = point + right * (Runtime.roadHalfWidth + 150f);
            Vector3 basePosition;
            if (!TryGetClearScenerySpot(desired, 8f, 10f, out basePosition))
            {
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            const float towerHeight = 70f;
            CreateVisualBox("Observation tower shaft", basePosition + Vector3.up * towerHeight * 0.5f, rotation, new Vector3(3.2f, towerHeight, 3.2f), metalMaterial);
            CreateVisualBox("Observation tower deck", basePosition + Vector3.up * (towerHeight + 2f), rotation, new Vector3(9f, 1.6f, 9f), concreteMaterial);
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Observation tower marker";
            marker.transform.SetParent(transform);
            marker.transform.position = basePosition + Vector3.up * (towerHeight + 8f);
            marker.transform.rotation = rotation * Quaternion.Euler(18f, 0f, 6f);
            marker.transform.localScale = new Vector3(0.6f, 4.5f, 0.6f);
            marker.GetComponent<Renderer>().sharedMaterial = sceneryAccentMaterial;
            MakeVisualOnly(marker);
        }

        // Row of moored white "yachts" along a straight for Monaco's harbour promenade.
        void BuildHarbourYachts(float startNormalized, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float d = Runtime.length * startNormalized + i * 14f;
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                Vector3 basePos = PushSceneryClearOfTrack(point + right * (Runtime.roadHalfWidth + 24f), 22f);
                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
                CreateVisualBox("Harbour yacht hull", basePos + Vector3.up * 0.6f, rotation, new Vector3(2.4f, 1.1f, 7.5f), yachtMaterial);
                CreateVisualBox("Harbour yacht cabin", basePos + Vector3.up * 1.5f, rotation, new Vector3(1.6f, 0.9f, 3.2f), glassMaterial);
                CreateVisualBox("Harbour yacht mast", basePos + Vector3.up * 3.4f, rotation, new Vector3(0.1f, 3.8f, 0.1f), metalMaterial);
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
            Vector3 basePosition = point + right * side * (Runtime.roadHalfWidth + 18f);
            Vector3 lateral = right * side;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            // Tiered seating stepping up and away from the track, with colored crowd
            // blocks so the stands read as full rather than as bare metal shelves.
            for (int row = 0; row < 6; row++)
            {
                Vector3 rowCenter = basePosition + Vector3.up * (0.4f + row * 0.62f) + lateral * row * 1.15f;
                CreateVisualBox("Grandstand tier", rowCenter, rotation, new Vector3(1.25f, 0.5f, 22f), metalMaterial);
                CreateVisualBox("Grandstand crowd block", rowCenter + Vector3.up * 0.45f, rotation, new Vector3(0.9f, 0.42f, 21f), row % 2 == 0 ? sceneryAccentMaterial : glassMaterial);
            }

            // Roof canopy on slender pylons.
            Vector3 roofCenter = basePosition + Vector3.up * 5.4f + lateral * 3.4f;
            CreateVisualBox("Grandstand roof", roofCenter, rotation, new Vector3(9.6f, 0.28f, 23.5f), metalMaterial);
            CreateVisualBox("Grandstand roof fascia", roofCenter - lateral * 4.6f - Vector3.up * 0.5f, rotation, new Vector3(0.22f, 0.9f, 23.5f), sceneryAccentMaterial);
            for (int pylon = -1; pylon <= 1; pylon++)
            {
                CreateVisualBox("Grandstand pylon", basePosition + lateral * 7.2f + forward * pylon * 9.5f + Vector3.up * 2.7f, rotation, new Vector3(0.4f, 5.4f, 0.4f), metalMaterial);
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
            // Singapore/Vegas get a share of coloured neon windows mixed in with the plain
            // glow so the skyline reads as multi-coloured night-spectacle rather than one
            // uniform amber wash.
            Material windowMaterial = neonTrack && index % 3 == 1 ? neonMaterials[index % neonMaterials.Length] : (night || nightTrack ? lightGlowMaterial : glassMaterial);
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
            for (int i = 0; i < 3; i++)
            {
                Vector3 offset = new Vector3((i - 1) * 2.3f, 0f, (index % 3 - 1) * 1.4f);
                Vector3 treePosition = PushSceneryClearOfTrack(position + offset, 20f);
                GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                trunk.name = "Generic tree trunk";
                trunk.transform.SetParent(transform);
                trunk.transform.position = treePosition + Vector3.up * 0.9f;
                trunk.transform.localScale = new Vector3(0.18f, 0.9f, 0.18f);
                trunk.GetComponent<Renderer>().sharedMaterial = sceneryAccentMaterial;
                MakeVisualOnly(trunk);

                GameObject crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Generic tree crown";
                crown.transform.SetParent(transform);
                crown.transform.position = treePosition + Vector3.up * 2.05f;
                crown.transform.localScale = new Vector3(1.45f, 1.15f, 1.45f);
                crown.GetComponent<Renderer>().sharedMaterial = foliageMaterial;
                MakeVisualOnly(crown);
            }
        }

        Vector3 PushSceneryClearOfTrack(Vector3 position, float clearance)
        {
            TrackProgress progress = Runtime.GetProgress(position);
            float minimum = Runtime.roadHalfWidth + clearance;
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
            float required = Runtime.roadHalfWidth + TrackCorridorRunoffWidth + objectRadius + extraMargin;
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
            float required = Runtime.roadHalfWidth + TrackCorridorRunoffWidth + objectRadius + extraMargin;
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

                bool repaired = false;
                for (int step = 0; step < 10; step++)
                {
                    float lateral = Runtime.roadHalfWidth + minimumClearance + localScale.x * 0.5f + step * 1.5f;
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
                if (Mathf.Abs(progress.lateralDistance) < Runtime.roadHalfWidth + minimumClearance)
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
                bool nearRoad = Mathf.Abs(progress.lateralDistance) < Runtime.roadHalfWidth + 0.55f;
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
        }
    }

    public class TrackSolidObstacle : MonoBehaviour
    {
        public string obstacleType;
        public float minimumClearance;
        public Vector3 localScaleAtValidation;
    }
}
