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

        public void GetPitServicePose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(length * 0.935f, out point, out forward, out right);
            position = point + right * (roadHalfWidth + 8.4f) + Vector3.up * 0.58f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void GetPitEntryPose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(length * 0.885f, out point, out forward, out right);
            position = point + right * (roadHalfWidth + 5.2f) + Vector3.up * 0.58f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void GetPitReleasePose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            SampleAtDistance(length * 0.992f, out point, out forward, out right);
            position = point + right * (roadHalfWidth + 4.4f) + Vector3.up * 0.62f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
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

    public class TrackManager : MonoBehaviour
    {
        public TrackRuntime Runtime { get; private set; }

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
        Material foliageMaterial;
        Material metalMaterial;
        Material glassMaterial;
        Material lightGlowMaterial;
        Material sceneryAccentMaterial;
        PhysicMaterial roadPhysicsMaterial;
        PhysicMaterial runoffPhysicsMaterial;
        Mesh visualBoxMesh;
        readonly List<TrackSolidObstacle> solidObstacles = new List<TrackSolidObstacle>();

        public TrackRuntime Build(CalendarEventData eventData)
        {
            return Build(eventData, true);
        }

        public TrackRuntime Build(CalendarEventData eventData, bool showRacingLine)
        {
            ClearChildren();
            Runtime = CreateLayout(eventData);
            CreateMaterials();
            BuildGround();
            BuildRoadMesh();
            BuildRoadPaint();
            BuildAsphaltDetail();
            BuildGridPaint();
            BuildKerbs();
            BuildBarriers();
            BuildTrackMarkers();
            BuildPitLane();
            BuildStartGantry();
            BuildScenery();
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
                roadHalfWidth = 9f,
                kerbStart = 8.1f,
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

            ApplyTrackScale(runtime, 1.32f);
            ValidateLayout(runtime);
        }

        void ApplyTrackScale(TrackRuntime runtime, float scale)
        {
            for (int i = 0; i < runtime.centerLine.Count; i++)
            {
                Vector3 point = runtime.centerLine[i];
                runtime.centerLine[i] = new Vector3(point.x * scale, point.y, point.z * scale);
            }

            runtime.roadHalfWidth *= scale;
            runtime.kerbStart *= scale;
        }

        void BuildBahrainLayout(TrackRuntime runtime)
        {
            runtime.styleName = "Desert power braking";
            runtime.roadHalfWidth = 10.05f;
            runtime.kerbStart = 8.9f;
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
            runtime.roadHalfWidth = 8.1f;
            runtime.kerbStart = 7.25f;
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
            runtime.roadHalfWidth = 7.55f;
            runtime.kerbStart = 6.68f;
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
            runtime.styleName = "Technical figure-eight Park";
            runtime.roadHalfWidth = 14.5f;
            runtime.kerbStart = 12.2f;
            runtime.drsZoneOne = new Vector2(0.92f, 0.08f);
            runtime.drsZoneTwo = new Vector2(0.62f, 0.76f);

            AddSmoothedAnchors(runtime, new[]
            {
                new Vector3(-600f, 0f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(400f, 0f, 0f),
                new Vector3(750f, 0f, 100f),
                new Vector3(820f, -2f, 350f),
                new Vector3(650f, 2f, 600f),
                new Vector3(420f, 5f, 750f),
                new Vector3(280f, 8f, 1000f),
                new Vector3(400f, 7f, 1250f),
                new Vector3(680f, 4f, 1450f),
                new Vector3(1050f, 1f, 1350f),
                new Vector3(1250f, -2f, 950f),
                new Vector3(1120f, -5f, 650f),
                new Vector3(1350f, -8f, 500f),
                new Vector3(1650f, -12f, 650f),
                new Vector3(1850f, -14f, 1150f),
                new Vector3(1750f, -9f, 1550f),
                new Vector3(1580f, -8f, 1620f),
                new Vector3(1420f, -9f, 1520f),
                new Vector3(1150f, 2f, 1750f),
                new Vector3(850f, 8f, 2100f),
                new Vector3(450f, 15f, 2350f),
                new Vector3(50f, 18f, 2250f),
                new Vector3(-250f, 20f, 1950f),
                new Vector3(50f, 21f, 1600f),
                new Vector3(550f, 22f, 1200f),
                new Vector3(1200f, 28f, 750f),
                new Vector3(1850f, 22f, 450f),
                new Vector3(2200f, 12f, -150f),
                new Vector3(1850f, 6f, -650f),
                new Vector3(1350f, 2f, -450f),
                new Vector3(1050f, 0f, 50f),
                new Vector3(750f, 0f, -250f),
                new Vector3(250f, 0f, -120f),
                new Vector3(-1000f, 0f, 0f)
            }, 12);
        }

        void BuildSilverstoneLayout(TrackRuntime runtime)
        {
            runtime.styleName = "High-speed airfield";
            runtime.roadHalfWidth = 10.4f;
            runtime.kerbStart = 9.2f;
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
            runtime.roadHalfWidth = 10.2f;
            runtime.kerbStart = 9.0f;
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
            runtime.roadHalfWidth = 9.8f;
            runtime.kerbStart = 8.72f;
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
            runtime.roadHalfWidth = 8.0f;
            runtime.kerbStart = 7.1f;
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
            runtime.roadHalfWidth = 11.0f;
            runtime.kerbStart = 9.78f;
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
            runtime.roadHalfWidth = 9.0f;
            runtime.kerbStart = 7.95f;
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
            runtime.roadHalfWidth = 9.6f;
            runtime.kerbStart = 8.48f;
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
            runtime.roadHalfWidth = 10.15f;
            runtime.kerbStart = 9.0f;
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
            runtime.roadHalfWidth = 9.45f;
            runtime.kerbStart = 8.38f;
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
            runtime.roadHalfWidth = 9.35f;
            runtime.kerbStart = 8.25f;
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
            runtime.roadHalfWidth = 9.8f;
            runtime.kerbStart = 8.72f;
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
            runtime.roadHalfWidth = 9.9f;
            runtime.kerbStart = 8.78f;
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
            runtime.roadHalfWidth = 9.25f;
            runtime.kerbStart = 8.18f;
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
            runtime.roadHalfWidth = 8.7f;
            runtime.kerbStart = 7.7f;
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
            runtime.roadHalfWidth = 9.0f;
            runtime.kerbStart = 7.98f;
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
            runtime.roadHalfWidth = 8.55f;
            runtime.kerbStart = 7.58f;
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
            runtime.roadHalfWidth = 9.8f;
            runtime.kerbStart = 8.72f;
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
            runtime.roadHalfWidth = 9.8f;
            runtime.kerbStart = 8.7f;
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
            runtime.roadHalfWidth = 9.05f;
            runtime.kerbStart = 8.02f;
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
            runtime.roadHalfWidth = 10.0f;
            runtime.kerbStart = 8.86f;
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
            for (int i = runtime.centerLine.Count - 1; i >= 0; i--)
            {
                Vector3 current = runtime.centerLine[i];
                Vector3 previous = runtime.centerLine[(i - 1 + runtime.centerLine.Count) % runtime.centerLine.Count];
                if (Vector3.Distance(current, previous) < 3.5f)
                {
                    runtime.centerLine.RemoveAt(i);
                }
            }

            if (runtime.centerLine.Count < 18)
            {
                Debug.LogWarning("[TrackValidation] " + runtime.displayName + " generated too few points. Falling back to Bahrain-style template.");
                runtime.centerLine.Clear();
                BuildBahrainLayout(runtime);
                return;
            }

            for (int i = 0; i < runtime.centerLine.Count; i++)
            {
                Vector3 current = runtime.centerLine[i];
                Vector3 next = runtime.centerLine[(i + 1) % runtime.centerLine.Count];
                float segment = Vector3.Distance(current, next);
                if (segment < 4f || segment > 90f)
                {
                    Debug.LogWarning("[TrackValidation] " + runtime.displayName + " segment " + i + " length " + segment.ToString("0.0") + "m may need tuning.");
                }
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
            Color runoff = new Color(0.61f, 0.52f, 0.36f);
            if (Runtime.styleName.Contains("Park") || Runtime.styleName.Contains("Flowing"))
            {
                runoff = new Color(0.18f, 0.34f, 0.22f);
            }
            else if (Runtime.styleName.Contains("street") || Runtime.styleName.Contains("Street"))
            {
                runoff = new Color(0.12f, 0.13f, 0.14f);
            }

            bool rain = Runtime.weather == WeatherState.LightRain || Runtime.weather == WeatherState.HeavyRain;
            roadMaterial = CreateMaterial("Runtime Road", rain ? new Color(0.008f, 0.011f, 0.014f) : new Color(0.015f, 0.016f, 0.018f), 0.04f, rain ? 0.86f : 0.72f);
            kerbMaterial = CreateMaterial("Runtime Kerb", new Color(0.94f, 0.04f, 0.03f), 0.02f, 0.64f);
            grassMaterial = CreateMaterial("Runtime Runoff", runoff, 0.01f, 0.18f);
            lineMaterial = CreateMaterial("Runtime Track Line", new Color(0.95f, 0.98f, 1f), 0.05f, 0.78f);
            roadEdgeMaterial = CreateMaterial("Runtime Painted Edge", new Color(1f, 0.98f, 0.9f), 0.04f, 0.76f);
            drsPaintMaterial = CreateMaterial("Runtime DRS Paint", new Color(0.02f, 0.32f, 0.95f), 0.06f, 0.82f, new Color(0.01f, 0.05f, 0.18f));
            rubberMaterial = CreateMaterial("Runtime Rubber", new Color(0.003f, 0.003f, 0.003f), 0.01f, 0.24f);
            asphaltPatchMaterial = CreateMaterial("Runtime Asphalt Patch", new Color(0.033f, 0.036f, 0.039f), 0f, rain ? 0.72f : 0.5f);
            skidMarkMaterial = CreateMaterial("Runtime Skid Mark", new Color(0.001f, 0.001f, 0.001f, 0.92f), 0f, 0.16f);
            barrierMaterial = CreateMaterial("Runtime Barrier", new Color(0.68f, 0.72f, 0.74f), 0.12f, 0.62f);
            tireBarrierMaterial = CreateMaterial("Runtime Tyre Barrier", new Color(0.015f, 0.016f, 0.017f), 0.02f, 0.28f);
            foliageMaterial = CreateMaterial("Runtime Foliage", new Color(0.04f, 0.32f, 0.12f), 0f, 0.42f);
            metalMaterial = CreateMaterial("Runtime Brushed Metal", new Color(0.52f, 0.56f, 0.58f), 0.42f, 0.78f);
            glassMaterial = CreateMaterial("Runtime Glass", new Color(0.12f, 0.28f, 0.38f, 0.85f), 0.1f, 0.95f);
            lightGlowMaterial = CreateMaterial("Runtime Light Glow", new Color(1f, 0.85f, 0.4f), 0f, 0.92f, new Color(1f, 0.62f, 0.15f));
            sceneryAccentMaterial = CreateMaterial("Runtime Scenery Accent", new Color(0.92f, 0.03f, 0.025f), 0.05f, 0.65f);
        }

        Material CreateMaterial(string materialName, Color color)
        {
            return CreateMaterial(materialName, color, 0f, 0.35f);
        }

        Material CreateMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            return CreateMaterial(materialName, color, metallic, smoothness, Color.black);
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
            GameObject ground = CreateVisualBox(Runtime.styleName + " terrain base", center, Quaternion.identity, size, grassMaterial);
            ground.layer = 0;
            BoxCollider collider = ground.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.sharedMaterial = GetRunoffPhysicsMaterial();

            // Add decorative height variation to the terrain edges
            for (int i = 0; i < 12; i++)
            {
                Vector3 hillPos = center + new Vector3(Random.Range(-size.x, size.x) * 0.45f, 5f, Random.Range(-size.z, size.z) * 0.45f);
                if (Runtime.IsOnRoad(hillPos)) continue;
                GameObject hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.transform.SetParent(transform);
                hill.transform.position = hillPos;
                hill.transform.localScale = new Vector3(180f, 42f, 180f);
                hill.GetComponent<Renderer>().sharedMaterial = grassMaterial;
                MakeVisualOnly(hill);
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
            Debug.Log("[RoadPhysics] Road collider created=" + (collider != null) +
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

                // Edge lines
                CreateRoadStripe(point - right * (Runtime.roadHalfWidth - 0.45f), forward, 0.25f, spacing * 0.95f, roadEdgeMaterial, "Left edge line");
                CreateRoadStripe(point + right * (Runtime.roadHalfWidth - 0.45f), forward, 0.25f, spacing * 0.95f, roadEdgeMaterial, "Right edge line");

                // Racing line rubbering
                if (Mathf.FloorToInt(d / spacing) % 2 == 0)
                {
                    float lateralOffset = Mathf.Sin(d * 0.02f) * (Runtime.roadHalfWidth * 0.35f);
                    CreateRoadStripe(point + right * lateralOffset, forward, 4.2f, spacing * 1.1f, rubberMaterial, "Rubbered racing line");
                    CreateRoadStripe(point + right * (lateralOffset + 0.15f), forward, 1.2f, spacing * 0.5f, rubberMaterial, "Rubbered skid mark");
                }

                float normalized = d / Mathf.Max(1f, Runtime.length);
                if (Runtime.IsInDrsZone(normalized) && Mathf.FloorToInt(d / spacing) % 2 == 0)
                {
                    CreateRoadStripe(point - right * (Runtime.roadHalfWidth - 1.5f), forward, 0.8f, 8f, drsPaintMaterial, "DRS zone paint");
                    CreateRoadStripe(point + right * (Runtime.roadHalfWidth - 1.5f), forward, 0.8f, 8f, drsPaintMaterial, "DRS zone paint");
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
                CreateRoadStripe(point + right * laneBias, forward, Runtime.roadHalfWidth * 0.82f, spacing * 0.76f, asphaltPatchMaterial, "Asphalt grain variation");
                CreateRoadStripe(point + right * (laneBias * 0.45f), forward, Runtime.roadHalfWidth * 0.42f, spacing * 0.82f, rubberMaterial, "Dark racing line rubber");

                if (Mathf.FloorToInt(d / spacing) % 4 == 1)
                {
                    CreateRoadStripe(point - right * 1.05f, forward, 0.16f, 7.6f, skidMarkMaterial, "Heavy braking skid mark");
                    CreateRoadStripe(point + right * 1.25f, forward, 0.14f, 6.8f, skidMarkMaterial, "Heavy braking skid mark");
                }
            }
        }

        void CreateRoadStripe(Vector3 position, Vector3 forward, float width, float length, Material material, string objectName)
        {
            CreateVisualBox(objectName, position + Vector3.up * 0.065f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(width, 0.022f, length), material);
        }

        void BuildGridPaint()
        {
            int slots = 22;
            float laneWidth = Mathf.Min(5.5f, Runtime.roadHalfWidth * 0.38f);
            for (int i = 0; i < slots; i++)
            {
                int row = i / 2;
                bool leftSlot = i % 2 == 0;
                float gridDistance = Runtime.length - 65f - row * 22f - (leftSlot ? 0f : 11f);
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(gridDistance, out point, out forward, out right);
                Vector3 center = point + right * (leftSlot ? -laneWidth : laneWidth);

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
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(d, out point, out forward, out right);
                bool street = streetCircuit;
                CreateBarrier(point - right * (Runtime.roadHalfWidth + (street ? 2f : 5.5f)), forward, street);
                CreateBarrier(point + right * (Runtime.roadHalfWidth + (street ? 2f : 5.5f)), forward, street);
            }
        }

        void CreateBarrier(Vector3 position, Vector3 forward, bool street)
        {
            GameObject barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrier.name = street ? "Street wall" : "Runoff marker";
            barrier.transform.SetParent(transform);
            Vector3 scale = street ? new Vector3(0.45f, 1.1f, 7.5f) : new Vector3(0.35f, 0.35f, 4.6f);
            barrier.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            barrier.transform.localScale = scale;
            barrier.GetComponent<Renderer>().sharedMaterial = street ? barrierMaterial : tireBarrierMaterial;
            TryPlaceSolidObstacle(barrier, street ? "street-wall" : "runoff-barrier", position, forward, scale, street ? 0.55f : 0.18f, street ? 1.15f : 6.25f);
        }

        void CreateKerbBlock(Vector3 position, Vector3 forward, float seed)
        {
            Material material = kerbMaterial;
            if (Mathf.FloorToInt(seed / 16f) % 2 == 0)
            {
                material = lineMaterial;
            }

            GameObject kerb = CreateVisualBox("Painted kerb", position + Vector3.up * 0.075f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(1.15f, 0.09f, 4.5f), material);
            MeshRenderer renderer = kerb.GetComponent<MeshRenderer>();
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Simple;
        }

        void BuildTrackMarkers()
        {
            CreateTrackLine(0f, "Start finish", Color.white, 2.4f);
            CreateTrackLine(Runtime.length * 0.333f, "Sector 1 line", new Color(0.1f, 0.75f, 1f), 1.2f);
            CreateTrackLine(Runtime.length * 0.666f, "Sector 2 line", new Color(0.1f, 1f, 0.45f), 1.2f);

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
                }
            }
        }

        void CreateBrakingBoard(float distance, string label)
        {
            Vector3 point; Vector3 forward; Vector3 right;
            Runtime.SampleAtDistance(distance, out point, out forward, out right);
            GameObject board = CreateVisualBox("Braking Board " + label, point + right * (Runtime.roadHalfWidth + 2.5f) + Vector3.up * 0.8f, Quaternion.LookRotation(right, Vector3.up), new Vector3(0.1f, 1.2f, 1.8f), lineMaterial);
            // Label geometry placeholder
            CreateVisualBox("Board Text " + label, point + right * (Runtime.roadHalfWidth + 2.44f) + Vector3.up * 0.8f, Quaternion.LookRotation(right, Vector3.up), new Vector3(0.01f, 0.6f, 1.2f), rubberMaterial);
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

        void BuildPitLane()
        {
            Vector3 start;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(Runtime.length * 0.93f, out start, out forward, out right);
            Material pitMaterial = CreateMaterial("Pit lane material", new Color(0.12f, 0.13f, 0.15f), 0.02f, 0.55f);

            // Scaled up pit building
            GameObject pitBuilding = CreateVisualBox("Main Pit Building", start + right * 38f + Vector3.up * 8f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(12f, 16f, 120f), metalMaterial);

            CreateCollidablePitSurface("Pit lane asphalt service road", start + right * 18f + Vector3.up * 0.015f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(14f, 0.16f, 95f), pitMaterial);
            CreatePitEntryExitSurfaces(pitMaterial);
            CreatePitEntryExitPaint(pitMaterial);
            CreateVisualBox("Pit service box paint", start + right * 18f + Vector3.up * 0.12f, Quaternion.LookRotation(forward, Vector3.up), new Vector3(10f, 0.035f, 75f), pitMaterial);

            for (float wall = -42f; wall <= 42f; wall += 12f)
            {
                CreatePitWallSegment(start + right * (Runtime.roadHalfWidth + 2.2f) + forward * wall, forward);
            }

            for (int i = 0; i < 8; i++)
            {
                Vector3 bay = start + right * 24f - forward * (35f - i * 11f);
                CreatePitBox(bay, forward, right, i);
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

            GameObject crossbar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crossbar.name = "Start gantry crossbar";
            crossbar.transform.SetParent(transform);
            crossbar.transform.position = point + Vector3.up * 7.2f;
            crossbar.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            crossbar.transform.localScale = new Vector3(Runtime.roadHalfWidth * 2.2f, 0.85f, 1.4f);
            crossbar.GetComponent<Renderer>().sharedMaterial = metalMaterial;
            MakeVisualOnly(crossbar);

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

        void BuildScenery()
        {
            bool street = Runtime.styleName.Contains("street") || Runtime.styleName.Contains("Street");
            bool park = Runtime.styleName.Contains("Park") || Runtime.styleName.Contains("Flowing");
            bool night = Runtime.styleName.Contains("Night");

            // Signature grandstands
            BuildGrandstand(0.02f, -1);
            BuildGrandstand(0.15f, 1);
            BuildGrandstand(0.45f, -1);
            BuildGrandstand(0.85f, 1);

            for (int i = 0; i < Runtime.centerLine.Count; i++)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Runtime.SampleAtDistance(Runtime.cumulativeDistances[i], out point, out forward, out right);
                float side = i % 2 == 0 ? -1f : 1f;

                // Trackside detail: marshal posts and towers
                if (i % 8 == 0)
                {
                    CreateFloodlight(point + right * side * (Runtime.roadHalfWidth + 6.5f), forward, night || street);
                }

                Vector3 basePosition = point + right * side * (Runtime.roadHalfWidth + (street ? 18f : 32f));
                if (street)
                {
                    CreateCityBlock(basePosition, forward, i, night);
                }
                else if (park)
                {
                    CreateTreeCluster(basePosition, i);
                    if (i % 5 == 0) CreateDune(basePosition + right * side * 40f, i);
                }
                else
                {
                    CreateDune(basePosition, i);
                    if (i % 6 == 0) CreateTreeCluster(basePosition + right * side * 45f, i);
                }
            }
        }

        void BuildGrandstand(float normalizedDistance, int side)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Runtime.SampleAtDistance(Runtime.length * normalizedDistance, out point, out forward, out right);
            Vector3 basePosition = point + right * side * (Runtime.roadHalfWidth + 18f);
            for (int row = 0; row < 4; row++)
            {
                GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = "Generic grandstand row";
                step.transform.SetParent(transform);
                step.transform.position = basePosition + Vector3.up * (0.35f + row * 0.38f) - forward * row * 1.1f;
                step.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                step.transform.localScale = new Vector3(16f, 0.32f, 1.05f);
                step.GetComponent<Renderer>().sharedMaterial = row % 2 == 0 ? metalMaterial : glassMaterial;
                MakeVisualOnly(step);
            }
        }

        void CreateCityBlock(Vector3 position, Vector3 forward, int index, bool night)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Generic city block";
            block.transform.SetParent(transform);
            block.transform.position = position + Vector3.up * (2.6f + index % 3);
            block.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            block.transform.localScale = new Vector3(5f + index % 4, 5.2f + index % 5, 4.8f + index % 3);
            block.GetComponent<Renderer>().sharedMaterial = night ? glassMaterial : barrierMaterial;
            MakeVisualOnly(block);
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

        void CreateDune(Vector3 position, int index)
        {
            Vector3 safePosition = PushSceneryClearOfTrack(position, 15f);
            GameObject dune = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dune.name = "Sculpted runoff dune";
            dune.transform.SetParent(transform);
            dune.transform.position = safePosition + Vector3.down * 0.28f;
            dune.transform.localScale = new Vector3(8f + index % 5, 0.75f, 4.6f + index % 4);
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
                    Debug.LogWarning("[TrackValidation] Removed " + obstacle.name + " because it intersected the racing surface near " + desiredBasePosition);
                    obstacle.SetActive(false);
                    Destroy(obstacle);
                    return false;
                }

                Debug.LogWarning("[TrackValidation] Repositioned " + obstacle.name + " away from racing surface to " + candidate);
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
            if (Runtime.roadCollider == null || Runtime.roadCollider.sharedMesh == null || Runtime.roadCollider.isTrigger)
            {
                Debug.LogWarning("[TrackValidation] Invalid road collider on " + Runtime.displayName);
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
                    Debug.LogWarning("[TrackValidation] Removed " + obstacle.name + " after final validation because it overlapped the racing surface.");
                    obstacle.gameObject.SetActive(false);
                    Destroy(obstacle.gameObject);
                    solidObstacles.RemoveAt(i);
                }
            }

            Debug.Log("[TrackValidation] " + Runtime.displayName + " roadContinuous=" + (Runtime.centerLine.Count >= 18) +
                      " roadCollider=" + (Runtime.roadCollider != null && Runtime.roadCollider.sharedMesh != null) +
                      " solidObstacles=" + solidObstacles.Count);
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
                    Debug.LogWarning("[TrackValidation] Removed decorative object " + renderer.gameObject.name + " because it intersected the racing surface.");
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
                   objectName.Contains("start light");
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
                    Debug.LogWarning("[TrackValidation] Removed collider from visual-only marking " + collider.gameObject.name);
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
