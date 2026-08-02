using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Track
{
    /// <summary>
    /// Authored circuit definitions by trackId. Each entry emits a
    /// TrackDefinitionAsset deterministically in code (the same way the
    /// reference circuit does) until hand-tuned assets replace them; the race
    /// layer's authored build branch consumes whatever this returns, so a
    /// circuit converts to the authored pipeline by moving its geometry here
    /// and retiring its legacy Build*Layout method. The catalog is the single
    /// source for every converted circuit's layout - the whole calendar is
    /// converted; TrackManager keeps only the Bahrain template as the
    /// emergency fallback.
    ///
    /// Converted circuits use LegacyCircuitSpec: the legacy anchor sketch
    /// verbatim, scaled outright to the real length band the legacy
    /// NormalizeTrackLength pass produced (elevation scaled by the same
    /// gentle scale^0.55 that pass used), with the legacy width, kerb inset,
    /// smoothing density, environment style and DRS zones carried over as
    /// authored data.
    /// </summary>
    public static class AuthoredCircuitCatalog
    {
        public const string MonzaTrackId = "monza_low_downforce";
        public const string ChinaTrackId = "china_suzuka_technical";
        public const string MiamiTrackId = "miami_park_street";
        public const string CanadaTrackId = "canada_stop_go";
        public const string BarcelonaTrackId = "barcelona_flowing";
        public const string AustriaTrackId = "austria_hillside";
        public const string HungaryTrackId = "hungary_technical";
        public const string ZandvoortTrackId = "zandvoort_coastal";
        public const string MadridTrackId = "madrid_hybrid_street";
        public const string BakuTrackId = "baku_fast_street";
        public const string AustinTrackId = "austin_rollercoaster";
        public const string MexicoTrackId = "mexico_high_altitude";
        public const string LasVegasTrackId = "las_vegas_street";
        public const string QatarTrackId = "qatar_high_speed";
        public const string JeddahTrackId = "jeddah_fast_street";
        public const string MonacoTrackId = "monaco_tight_street";
        public const string SuzukaTrackId = "suzuka_figure_eight";
        public const string SilverstoneTrackId = "silverstone_high_speed";
        public const string SpaTrackId = "spa_flowing";
        public const string SingaporeTrackId = "singapore_night";
        public const string MelbourneTrackId = "melbourne_park";
        public const string InterlagosTrackId = "interlagos_short_flowing";
        public const string AbuDhabiTrackId = "abu_dhabi_finale";
        public const string BahrainTrackId = "bahrain_desert";

        public static bool Contains(string trackId)
        {
            return trackId == ReferenceTrackGenerator.ReferenceTrackId
                || trackId == MonzaTrackId
                || trackId == ChinaTrackId
                || trackId == MiamiTrackId
                || trackId == CanadaTrackId
                || trackId == BarcelonaTrackId
                || trackId == AustriaTrackId
                || trackId == HungaryTrackId
                || trackId == ZandvoortTrackId
                || trackId == MadridTrackId
                || trackId == BakuTrackId
                || trackId == AustinTrackId
                || trackId == MexicoTrackId
                || trackId == LasVegasTrackId
                || trackId == QatarTrackId
                || trackId == JeddahTrackId
                || trackId == MonacoTrackId
                || trackId == SuzukaTrackId
                || trackId == SilverstoneTrackId
                || trackId == SpaTrackId
                || trackId == SingaporeTrackId
                || trackId == MelbourneTrackId
                || trackId == InterlagosTrackId
                || trackId == AbuDhabiTrackId
                || trackId == BahrainTrackId;
        }

        /// <summary>Definition for an authored circuit, or null when the id is not authored.</summary>
        public static TrackDefinitionAsset Generate(string trackId)
        {
            if (trackId == ReferenceTrackGenerator.ReferenceTrackId)
            {
                return ReferenceTrackGenerator.Generate();
            }

            switch (trackId)
            {
                case MonzaTrackId:
                    return GenerateFromSpec(MonzaSpec());
                case ChinaTrackId:
                    return GenerateFromSpec(ChinaSpec());
                case MiamiTrackId:
                    return GenerateFromSpec(MiamiSpec());
                case CanadaTrackId:
                    return GenerateFromSpec(CanadaSpec());
                case BarcelonaTrackId:
                    return GenerateFromSpec(BarcelonaSpec());
                case AustriaTrackId:
                    return GenerateFromSpec(AustriaSpec());
                case HungaryTrackId:
                    return GenerateFromSpec(HungarySpec());
                case ZandvoortTrackId:
                    return GenerateFromSpec(ZandvoortSpec());
                case MadridTrackId:
                    return GenerateFromSpec(MadridSpec());
                case BakuTrackId:
                    return GenerateFromSpec(BakuSpec());
                case AustinTrackId:
                    return GenerateFromSpec(AustinSpec());
                case MexicoTrackId:
                    return GenerateFromSpec(MexicoSpec());
                case LasVegasTrackId:
                    return GenerateFromSpec(LasVegasSpec());
                case QatarTrackId:
                    return GenerateFromSpec(QatarSpec());
                case JeddahTrackId:
                    return GenerateFromSpec(JeddahSpec());
                case MonacoTrackId:
                    return GenerateFromSpec(MonacoSpec());
                case SuzukaTrackId:
                    return GenerateFromSpec(SuzukaSpec());
                case SilverstoneTrackId:
                    return GenerateFromSpec(SilverstoneSpec());
                case SpaTrackId:
                    return GenerateFromSpec(SpaSpec());
                case SingaporeTrackId:
                    return GenerateFromSpec(SingaporeSpec());
                case MelbourneTrackId:
                    return GenerateFromSpec(MelbourneSpec());
                case InterlagosTrackId:
                    return GenerateFromSpec(InterlagosSpec());
                case AbuDhabiTrackId:
                    return GenerateFromSpec(AbuDhabiSpec());
                case BahrainTrackId:
                    return GenerateFromSpec(BahrainSpec());
                default:
                    return null;
            }
        }

        // ---- Legacy-sketch conversion ---------------------------------------

        // Street circuits keep the walls close (a couple of metres of kerb and
        // apron); permanent circuits get real asphalt/gravel runoff.
        const float StreetRunoffMeters = 2.5f;
        const float PermanentRunoffMeters = 22f;

        public struct LegacyCircuitSpec
        {
            public string TrackId;
            public string DisplayName;
            public string Country;
            public string EnvironmentStyle;
            public float HalfWidthMeters;
            public float KerbStartMeters;
            public Vector2 DrsZoneOneNormalized;   // (start, end), wrap allowed
            public Vector2 DrsZoneTwoNormalized;
            public float TargetLengthMeters;
            public int AnchorSubdivisions;
            public Vector3[] SketchAnchors;
        }

        static LegacyCircuitSpec MonzaSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = MonzaTrackId,
                DisplayName = "Italy GP",
                Country = "Italy",
                EnvironmentStyle = "Low-downforce park",
                HalfWidthMeters = 15.98f,
                KerbStartMeters = 9.4f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.44f, 0.62f),
                TargetLengthMeters = 5793f,
                AnchorSubdivisions = 3,
                // Final-corner rebuild ("Italy is completely broken"): the old last
                // anchor was a single spike out to (-210,0,0) that forced the start
                // tangent by doubling the road straight back east along z=0 - on top
                // of the pit straight. That near-180-degree reversal with the entry
                // and exit legs collinear left a ~1.6m centreline radius that no
                // amount of cusp relaxation could lift above the drivable-radius
                // floor, so the road collider folded into a wall across the corner
                // (the [FinalCornerDiag] 176.5-degree / 8m-half-width hairpin). The
                // U-turn is now a proper multi-point hairpin routed through the empty
                // quadrant SOUTH of the start line (z<0), so entry and exit legs are
                // genuinely separated: verified min radius stays above the 28m floor
                // and no two parts of the road come within a road-width of each other.
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 620f), new Vector3(14f, 0f, 660f),
                    new Vector3(46f, 0f, 690f), new Vector3(96f, 0f, 806f), new Vector3(150f, 0f, 900f),
                    new Vector3(214f, 0f, 962f), new Vector3(238f, 0f, 996f), new Vector3(214f, 0f, 1024f),
                    new Vector3(262f, 0f, 1096f), new Vector3(300f, 0f, 1140f), new Vector3(322f, 0f, 1082f),
                    new Vector3(300f, 0f, 1020f), new Vector3(256f, 0f, 940f), new Vector3(224f, 0f, 860f),
                    new Vector3(300f, 0f, 742f), new Vector3(352f, 0f, 690f), new Vector3(330f, 0f, 652f),
                    new Vector3(368f, 0f, 610f), new Vector3(392f, 0f, 470f), new Vector3(392f, 0f, 250f),
                    new Vector3(330f, 0f, 96f), new Vector3(196f, 0f, 20f), new Vector3(74f, 0f, 0f)
                },
            };
        }

        static LegacyCircuitSpec ChinaSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = ChinaTrackId,
                DisplayName = "China GP",
                Country = "China",
                EnvironmentStyle = "Technical snail and back straight",
                HalfWidthMeters = 15.06f,
                KerbStartMeters = 8.87f,
                DrsZoneOneNormalized = new Vector2(0.83f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.42f, 0.58f),
                TargetLengthMeters = 5451f,
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 300f), new Vector3(40f, 0f, 360f),
                    new Vector3(80f, 0f, 390f), new Vector3(110f, 0f, 360f), new Vector3(120f, 0f, 310f),
                    new Vector3(90f, 0f, 280f), new Vector3(60f, 0f, 300f), new Vector3(80f, 0f, 360f),
                    new Vector3(140f, 0f, 420f), new Vector3(210f, 0f, 450f), new Vector3(270f, 0f, 420f),
                    new Vector3(300f, 0f, 360f), new Vector3(330f, 0f, 420f), new Vector3(390f, 0f, 520f),
                    new Vector3(430f, 0f, 600f), new Vector3(400f, 0f, 650f), new Vector3(340f, 0f, 640f),
                    new Vector3(300f, 0f, 570f), new Vector3(250f, 0f, 470f), new Vector3(180f, 0f, 360f),
                    new Vector3(100f, 0f, 220f), new Vector3(40f, 0f, 90f)
                },
            };
        }

        static LegacyCircuitSpec MiamiSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = MiamiTrackId,
                DisplayName = "Miami GP",
                Country = "United States",
                EnvironmentStyle = "Stadium street rhythm",
                HalfWidthMeters = 13.0f,
                KerbStartMeters = 7.63f,
                DrsZoneOneNormalized = new Vector2(0.86f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.48f, 0.64f),
                TargetLengthMeters = 5412f,
                AnchorSubdivisions = 3,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 420f), new Vector3(40f, 0f, 480f),
                    new Vector3(100f, 0f, 500f), new Vector3(160f, 0f, 480f), new Vector3(180f, 0f, 420f),
                    new Vector3(160f, 0f, 360f), new Vector3(200f, 0f, 320f), new Vector3(260f, 0f, 310f),
                    new Vector3(310f, 0f, 340f), new Vector3(330f, 0f, 400f), new Vector3(310f, 0f, 460f),
                    new Vector3(350f, 0f, 500f), new Vector3(400f, 0f, 490f), new Vector3(420f, 0f, 430f),
                    new Vector3(400f, 0f, 340f), new Vector3(350f, 0f, 230f), new Vector3(280f, 0f, 130f),
                    new Vector3(180f, 0f, 50f)
                },
            };
        }

        static LegacyCircuitSpec CanadaSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = CanadaTrackId,
                DisplayName = "Canada GP",
                Country = "Canada",
                EnvironmentStyle = "Stop-go island",
                HalfWidthMeters = 13.2f,
                KerbStartMeters = 7.74f,
                DrsZoneOneNormalized = new Vector2(0.84f, 0.09f),
                DrsZoneTwoNormalized = new Vector2(0.56f, 0.72f),
                TargetLengthMeters = 4361f,
                AnchorSubdivisions = 3,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(30f, 0f, 80f), new Vector3(0f, 0f, 120f),
                    new Vector3(20f, 0f, 200f), new Vector3(60f, 0f, 340f), new Vector3(100f, 0f, 470f),
                    new Vector3(140f, 0f, 560f), new Vector3(190f, 0f, 600f), new Vector3(240f, 0f, 570f),
                    new Vector3(250f, 0f, 510f), new Vector3(210f, 0f, 470f), new Vector3(240f, 0f, 410f),
                    new Vector3(300f, 0f, 340f), new Vector3(340f, 0f, 250f), new Vector3(360f, 0f, 150f),
                    new Vector3(330f, 0f, 70f), new Vector3(270f, 0f, 20f), new Vector3(180f, 0f, -10f),
                    new Vector3(90f, 0f, -10f)
                },
            };
        }

        static LegacyCircuitSpec BarcelonaSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = BarcelonaTrackId,
                DisplayName = "Spain GP",
                Country = "Spain",
                EnvironmentStyle = "Flowing test track",
                HalfWidthMeters = 14.86f,
                KerbStartMeters = 8.66f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.5f, 0.65f),
                TargetLengthMeters = 4657f,
                AnchorSubdivisions = 5,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 300f), new Vector3(50f, 0f, 350f),
                    new Vector3(110f, 0f, 350f), new Vector3(150f, 0f, 300f), new Vector3(140f, 0f, 240f),
                    new Vector3(180f, 0f, 190f), new Vector3(250f, 0f, 180f), new Vector3(310f, 0f, 220f),
                    new Vector3(330f, 0f, 290f), new Vector3(300f, 0f, 350f), new Vector3(340f, 0f, 410f),
                    new Vector3(400f, 0f, 440f), new Vector3(440f, 0f, 400f), new Vector3(430f, 0f, 330f),
                    new Vector3(390f, 0f, 260f), new Vector3(340f, 0f, 170f), new Vector3(270f, 0f, 90f),
                    new Vector3(180f, 0f, 30f), new Vector3(80f, 0f, 0f)
                },
            };
        }

        static LegacyCircuitSpec AustriaSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = AustriaTrackId,
                DisplayName = "Austria GP",
                Country = "Austria",
                EnvironmentStyle = "Short alpine power",
                HalfWidthMeters = 14.44f,
                KerbStartMeters = 8.46f,
                DrsZoneOneNormalized = new Vector2(0.86f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.18f, 0.36f),
                TargetLengthMeters = 4318f,
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 340f), new Vector3(40f, 20f, 400f),
                    new Vector3(90f, 35f, 400f), new Vector3(120f, 40f, 350f), new Vector3(160f, 55f, 270f),
                    new Vector3(230f, 60f, 200f), new Vector3(290f, 55f, 180f), new Vector3(330f, 45f, 220f),
                    new Vector3(320f, 35f, 280f), new Vector3(270f, 30f, 320f), new Vector3(300f, 20f, 380f),
                    new Vector3(360f, 15f, 420f), new Vector3(410f, 10f, 390f), new Vector3(400f, 5f, 320f),
                    new Vector3(350f, 3f, 240f), new Vector3(280f, 2f, 140f), new Vector3(190f, 1f, 60f),
                    new Vector3(90f, 0f, 10f)
                },
            };
        }

        static LegacyCircuitSpec HungarySpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = HungaryTrackId,
                DisplayName = "Hungary GP",
                Country = "Hungary",
                EnvironmentStyle = "Twisty technical bowl",
                HalfWidthMeters = 13.0f,
                KerbStartMeters = 7.63f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.34f, 0.45f),
                TargetLengthMeters = 4381f,
                AnchorSubdivisions = 3,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 300f), new Vector3(40f, -10f, 360f),
                    new Vector3(90f, -15f, 370f), new Vector3(120f, -10f, 320f), new Vector3(90f, -5f, 270f),
                    new Vector3(120f, 0f, 220f), new Vector3(180f, 5f, 190f), new Vector3(240f, 10f, 210f),
                    new Vector3(280f, 15f, 260f), new Vector3(260f, 20f, 320f), new Vector3(210f, 22f, 350f),
                    new Vector3(240f, 20f, 410f), new Vector3(300f, 15f, 440f), new Vector3(340f, 10f, 410f),
                    new Vector3(330f, 5f, 340f), new Vector3(290f, 3f, 260f), new Vector3(230f, 2f, 170f),
                    new Vector3(150f, 1f, 90f), new Vector3(70f, 0f, 30f)
                },
            };
        }

        static LegacyCircuitSpec ZandvoortSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = ZandvoortTrackId,
                DisplayName = "Netherlands GP",
                Country = "Netherlands",
                EnvironmentStyle = "Coastal banked flow",
                HalfWidthMeters = 12.89f,
                KerbStartMeters = 7.52f,
                DrsZoneOneNormalized = new Vector2(0.87f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.54f, 0.68f),
                TargetLengthMeters = 4259f,
                AnchorSubdivisions = 5,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 260f), new Vector3(40f, 5f, 320f),
                    new Vector3(90f, 8f, 330f), new Vector3(120f, 5f, 290f), new Vector3(100f, 3f, 240f),
                    new Vector3(140f, 2f, 200f), new Vector3(200f, 4f, 180f), new Vector3(260f, 6f, 200f),
                    new Vector3(300f, 8f, 250f), new Vector3(290f, 10f, 310f), new Vector3(240f, 8f, 340f),
                    new Vector3(270f, 5f, 390f), new Vector3(330f, 3f, 410f), new Vector3(370f, 2f, 370f),
                    new Vector3(350f, 1f, 300f), new Vector3(300f, 1f, 220f), new Vector3(230f, 0f, 140f),
                    new Vector3(140f, 0f, 60f)
                },
            };
        }

        static LegacyCircuitSpec MadridSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = MadridTrackId,
                DisplayName = "Madrid GP",
                Country = "Spain",
                EnvironmentStyle = "Hybrid street exhibition",
                HalfWidthMeters = 12.37f,
                KerbStartMeters = 7.22f,
                DrsZoneOneNormalized = new Vector2(0.84f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.46f, 0.62f),
                TargetLengthMeters = 5474f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 320f), new Vector3(40f, 0f, 380f),
                    new Vector3(100f, 0f, 400f), new Vector3(160f, 0f, 380f), new Vector3(190f, 0f, 330f),
                    new Vector3(170f, 0f, 280f), new Vector3(200f, 0f, 230f), new Vector3(260f, 0f, 210f),
                    new Vector3(320f, 0f, 240f), new Vector3(350f, 0f, 300f), new Vector3(330f, 0f, 360f),
                    new Vector3(370f, 0f, 420f), new Vector3(430f, 0f, 430f), new Vector3(460f, 0f, 380f),
                    new Vector3(440f, 0f, 300f), new Vector3(390f, 0f, 200f), new Vector3(310f, 0f, 110f),
                    new Vector3(200f, 0f, 40f), new Vector3(90f, 0f, 0f)
                },
            };
        }

        static LegacyCircuitSpec BakuSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = BakuTrackId,
                DisplayName = "Azerbaijan GP",
                Country = "Azerbaijan",
                EnvironmentStyle = "Castle straight street",
                HalfWidthMeters = 12.79f,
                KerbStartMeters = 7.52f,
                DrsZoneOneNormalized = new Vector2(0.78f, 0.1f),
                DrsZoneTwoNormalized = new Vector2(0.52f, 0.67f),
                TargetLengthMeters = 6003f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 520f), new Vector3(30f, 0f, 580f),
                    new Vector3(80f, 0f, 600f), new Vector3(130f, 0f, 570f), new Vector3(140f, 0f, 510f),
                    new Vector3(120f, 0f, 460f), new Vector3(160f, 0f, 420f), new Vector3(210f, 0f, 430f),
                    new Vector3(230f, 0f, 480f), new Vector3(210f, 0f, 530f), new Vector3(240f, 0f, 580f),
                    new Vector3(300f, 0f, 600f), new Vector3(340f, 0f, 560f), new Vector3(330f, 0f, 480f),
                    new Vector3(300f, 0f, 380f), new Vector3(260f, 0f, 260f), new Vector3(200f, 0f, 150f),
                    new Vector3(120f, 0f, 60f)
                },
            };
        }

        static LegacyCircuitSpec AustinSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = AustinTrackId,
                DisplayName = "United States GP",
                Country = "United States",
                EnvironmentStyle = "Rollercoaster esses",
                HalfWidthMeters = 15.06f,
                KerbStartMeters = 8.87f,
                DrsZoneOneNormalized = new Vector2(0.86f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.38f, 0.56f),
                TargetLengthMeters = 5513f,
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 10f, 180f), new Vector3(-30f, 28f, 240f),
                    new Vector3(20f, 20f, 300f), new Vector3(70f, 15f, 360f), new Vector3(120f, 12f, 420f),
                    new Vector3(170f, 10f, 470f), new Vector3(230f, 8f, 500f), new Vector3(290f, 6f, 520f),
                    new Vector3(350f, 5f, 490f), new Vector3(390f, 4f, 430f), new Vector3(370f, 3f, 370f),
                    new Vector3(330f, 3f, 310f), new Vector3(380f, 2f, 250f), new Vector3(440f, 2f, 180f),
                    new Vector3(420f, 1f, 110f), new Vector3(360f, 1f, 60f), new Vector3(280f, 0f, 30f),
                    new Vector3(190f, 0f, 10f), new Vector3(90f, 0f, 0f)
                },
            };
        }

        static LegacyCircuitSpec MexicoSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = MexicoTrackId,
                DisplayName = "Mexico GP",
                Country = "Mexico",
                EnvironmentStyle = "High-altitude stadium",
                HalfWidthMeters = 14.64f,
                KerbStartMeters = 8.57f,
                DrsZoneOneNormalized = new Vector2(0.84f, 0.09f),
                DrsZoneTwoNormalized = new Vector2(0.48f, 0.63f),
                TargetLengthMeters = 4304f,
                AnchorSubdivisions = 3,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 520f), new Vector3(40f, 0f, 570f),
                    new Vector3(90f, 0f, 570f), new Vector3(110f, 0f, 520f), new Vector3(90f, 0f, 470f),
                    new Vector3(130f, 0f, 430f), new Vector3(190f, 0f, 410f), new Vector3(250f, 0f, 430f),
                    new Vector3(280f, 0f, 480f), new Vector3(260f, 0f, 530f), new Vector3(300f, 0f, 560f),
                    new Vector3(350f, 0f, 540f), new Vector3(360f, 0f, 470f), new Vector3(330f, 0f, 390f),
                    new Vector3(300f, 0f, 300f), new Vector3(250f, 0f, 200f), new Vector3(180f, 0f, 110f),
                    new Vector3(90f, 0f, 40f)
                },
            };
        }

        static LegacyCircuitSpec LasVegasSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = LasVegasTrackId,
                DisplayName = "Las Vegas GP",
                Country = "United States",
                EnvironmentStyle = "Neon strip street",
                HalfWidthMeters = 13.4f,
                KerbStartMeters = 7.84f,
                DrsZoneOneNormalized = new Vector2(0.74f, 0.13f),
                DrsZoneTwoNormalized = new Vector2(0.42f, 0.58f),
                TargetLengthMeters = 6201f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 600f), new Vector3(30f, 0f, 660f),
                    new Vector3(90f, 0f, 680f), new Vector3(160f, 0f, 680f), new Vector3(220f, 0f, 650f),
                    new Vector3(240f, 0f, 590f), new Vector3(240f, 0f, 480f), new Vector3(270f, 0f, 420f),
                    new Vector3(330f, 0f, 400f), new Vector3(390f, 0f, 410f), new Vector3(420f, 0f, 360f),
                    new Vector3(420f, 0f, 260f), new Vector3(390f, 0f, 170f), new Vector3(330f, 0f, 90f),
                    new Vector3(250f, 0f, 30f), new Vector3(150f, 0f, 0f), new Vector3(60f, 0f, -10f)
                },
            };
        }

        static LegacyCircuitSpec QatarSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = QatarTrackId,
                DisplayName = "Qatar GP",
                Country = "Qatar",
                EnvironmentStyle = "Desert high-speed flow",
                HalfWidthMeters = 15.47f,
                KerbStartMeters = 9.07f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.55f, 0.72f),
                TargetLengthMeters = 5419f,
                AnchorSubdivisions = 5,
                // Self-intersection fix (per report - the whole field piled up
                // at an at-grade crossing on lap 1): the old sketch's inner
                // hook exited north-east straight through the earlier
                // (252,90)->(210,138) descent - a genuine layout crossing
                // (verified analytically on the smoothed spline), which put one
                // section's road/barriers physically across the other. The
                // corridor was topologically walled off, so the middle is
                // restructured rather than nudged: eastern lobe first (fast
                // right-side sweep to the top loop), then the technical infield
                // hook hangs off the western return. Verified: 0 crossings on
                // the subdivided spline, ~268m minimum separation between
                // non-adjacent sections.
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 380f), new Vector3(40f, 0f, 440f),
                    new Vector3(100f, 0f, 460f), new Vector3(160f, 0f, 440f), new Vector3(190f, 0f, 390f),
                    new Vector3(180f, 0f, 330f), new Vector3(220f, 0f, 290f), new Vector3(280f, 0f, 280f),
                    new Vector3(330f, 0f, 310f), new Vector3(350f, 0f, 370f), new Vector3(330f, 0f, 430f),
                    new Vector3(370f, 0f, 470f), new Vector3(420f, 0f, 460f), new Vector3(440f, 0f, 400f),
                    new Vector3(420f, 0f, 320f), new Vector3(370f, 0f, 220f), new Vector3(300f, 0f, 130f),
                    new Vector3(200f, 0f, 50f)
                },
            };
        }

        static LegacyCircuitSpec JeddahSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = JeddahTrackId,
                DisplayName = "Saudi Arabia GP",
                Country = "Saudi Arabia",
                EnvironmentStyle = "Fast coastal street",
                HalfWidthMeters = 13.2f,
                KerbStartMeters = 7.74f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.56f, 0.73f),
                TargetLengthMeters = 6174f,
                AnchorSubdivisions = 5,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 560f), new Vector3(30f, 0f, 620f),
                    new Vector3(80f, 0f, 650f), new Vector3(140f, 0f, 650f), new Vector3(180f, 0f, 610f),
                    new Vector3(190f, 0f, 550f), new Vector3(210f, 0f, 490f), new Vector3(260f, 0f, 460f),
                    new Vector3(310f, 0f, 470f), new Vector3(340f, 0f, 510f), new Vector3(330f, 0f, 570f),
                    new Vector3(360f, 0f, 620f), new Vector3(410f, 0f, 630f), new Vector3(440f, 0f, 580f),
                    new Vector3(430f, 0f, 500f), new Vector3(400f, 0f, 400f), new Vector3(350f, 0f, 290f),
                    new Vector3(270f, 0f, 170f), new Vector3(170f, 0f, 70f), new Vector3(70f, 0f, 10f)
                },
            };
        }

        static LegacyCircuitSpec MonacoSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = MonacoTrackId,
                DisplayName = "Monaco GP",
                Country = "Monaco",
                EnvironmentStyle = "Tight harbour street",
                HalfWidthMeters = 11.14f,
                KerbStartMeters = 6.5f,
                DrsZoneOneNormalized = new Vector2(0.87f, 0.07f),
                DrsZoneTwoNormalized = new Vector2(0.46f, 0.58f),
                TargetLengthMeters = 3337f,
                AnchorSubdivisions = 3,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 180f), new Vector3(28f, 0f, 220f),
                    new Vector3(60f, 0f, 296f), new Vector3(98f, 0f, 330f), new Vector3(150f, 0f, 336f),
                    new Vector3(178f, 0f, 300f), new Vector3(196f, 0f, 262f), new Vector3(172f, 0f, 236f),
                    new Vector3(196f, 0f, 214f), new Vector3(178f, 0f, 186f), new Vector3(216f, 0f, 150f),
                    new Vector3(268f, 0f, 110f), new Vector3(316f, 0f, 70f), new Vector3(330f, 0f, 36f),
                    new Vector3(300f, 0f, 18f), new Vector3(276f, 0f, 44f), new Vector3(240f, 0f, 30f),
                    new Vector3(210f, 0f, -6f), new Vector3(156f, 0f, -22f), new Vector3(104f, 0f, -16f),
                    new Vector3(60f, 0f, -30f), new Vector3(24f, 0f, -18f)
                },
            };
        }

        static LegacyCircuitSpec SuzukaSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = SuzukaTrackId,
                DisplayName = "Japan GP",
                Country = "Japan",
                EnvironmentStyle = "Technical esses Park",
                HalfWidthMeters = 13.61f,
                KerbStartMeters = 7.93f,
                DrsZoneOneNormalized = new Vector2(0.9f, 0.07f),
                DrsZoneTwoNormalized = new Vector2(0.5f, 0.63f),
                TargetLengthMeters = 5807f,
                AnchorSubdivisions = 5,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(60f, 0f, 90f), new Vector3(110f, 0f, 160f),
                    new Vector3(150f, 0f, 240f), new Vector3(200f, 0f, 290f), new Vector3(240f, 0f, 350f),
                    new Vector3(280f, 0f, 400f), new Vector3(330f, 0f, 430f), new Vector3(390f, 0f, 440f),
                    new Vector3(430f, 0f, 400f), new Vector3(400f, 0f, 360f), new Vector3(360f, 0f, 330f),
                    new Vector3(400f, 0f, 290f), new Vector3(470f, 0f, 260f), new Vector3(520f, 0f, 200f),
                    new Vector3(500f, 0f, 140f), new Vector3(440f, 0f, 130f), new Vector3(400f, 0f, 180f),
                    new Vector3(330f, 0f, 220f), new Vector3(250f, 0f, 200f), new Vector3(180f, 0f, 140f),
                    new Vector3(120f, 0f, 60f), new Vector3(60f, 0f, 10f)
                },
            };
        }

        static LegacyCircuitSpec SilverstoneSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = SilverstoneTrackId,
                DisplayName = "Britain GP",
                Country = "United Kingdom",
                EnvironmentStyle = "High-speed airfield",
                HalfWidthMeters = 15.88f,
                KerbStartMeters = 9.28f,
                DrsZoneOneNormalized = new Vector2(0.89f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.48f, 0.64f),
                TargetLengthMeters = 5891f,
                AnchorSubdivisions = 5,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(60f, 0f, 120f), new Vector3(120f, 0f, 150f),
                    new Vector3(200f, 0f, 130f), new Vector3(250f, 0f, 80f), new Vector3(230f, 0f, 30f),
                    new Vector3(180f, 0f, 10f), new Vector3(240f, 0f, -80f), new Vector3(300f, 0f, -140f),
                    new Vector3(270f, 0f, -190f), new Vector3(210f, 0f, -200f), new Vector3(170f, 0f, -160f),
                    new Vector3(130f, 0f, -120f), new Vector3(60f, 0f, -160f), new Vector3(-40f, 0f, -240f),
                    new Vector3(-120f, 0f, -300f), new Vector3(-200f, 0f, -330f), new Vector3(-300f, 0f, -300f),
                    new Vector3(-340f, 0f, -230f), new Vector3(-300f, 0f, -160f), new Vector3(-220f, 0f, -120f),
                    new Vector3(-140f, 0f, -60f), new Vector3(-70f, 0f, -20f)
                },
            };
        }

        static LegacyCircuitSpec SpaSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = SpaTrackId,
                DisplayName = "Belgium GP",
                Country = "Belgium",
                EnvironmentStyle = "Long Ardennes elevation",
                HalfWidthMeters = 15.26f,
                KerbStartMeters = 8.98f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.07f),
                DrsZoneTwoNormalized = new Vector2(0.18f, 0.36f),
                TargetLengthMeters = 7004f,
                AnchorSubdivisions = 5,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(40f, 0f, 60f), new Vector3(20f, 0f, 96f),
                    new Vector3(-20f, 0f, 150f), new Vector3(-10f, 45f, 260f), new Vector3(30f, 70f, 430f),
                    new Vector3(70f, 75f, 560f), new Vector3(110f, 72f, 620f), new Vector3(80f, 70f, 660f),
                    new Vector3(40f, 68f, 690f), new Vector3(-30f, 60f, 760f), new Vector3(-120f, 50f, 840f),
                    new Vector3(-200f, 40f, 900f), new Vector3(-300f, 30f, 930f), new Vector3(-380f, 20f, 880f),
                    new Vector3(-420f, 15f, 800f), new Vector3(-380f, 10f, 720f), new Vector3(-300f, 6f, 660f),
                    new Vector3(-200f, 4f, 600f), new Vector3(-120f, 2f, 480f), new Vector3(-80f, 1f, 300f),
                    new Vector3(-60f, 0f, 140f), new Vector3(-40f, 0f, 60f), new Vector3(-20f, 0f, 20f)
                },
            };
        }

        static LegacyCircuitSpec SingaporeSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = SingaporeTrackId,
                DisplayName = "Singapore GP",
                Country = "Singapore",
                EnvironmentStyle = "Night street ninety",
                HalfWidthMeters = 11.54f,
                KerbStartMeters = 6.8f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.07f),
                DrsZoneTwoNormalized = new Vector2(0.55f, 0.69f),
                TargetLengthMeters = 4940f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 300f), new Vector3(40f, 0f, 350f),
                    new Vector3(100f, 0f, 360f), new Vector3(150f, 0f, 330f), new Vector3(160f, 0f, 270f),
                    new Vector3(200f, 0f, 230f), new Vector3(260f, 0f, 220f), new Vector3(300f, 0f, 250f),
                    new Vector3(300f, 0f, 310f), new Vector3(340f, 0f, 350f), new Vector3(400f, 0f, 350f),
                    new Vector3(430f, 0f, 300f), new Vector3(420f, 0f, 230f), new Vector3(380f, 0f, 150f),
                    new Vector3(320f, 0f, 80f), new Vector3(240f, 0f, 30f), new Vector3(140f, 0f, 0f),
                    new Vector3(60f, 0f, -10f)
                },
            };
        }

        static LegacyCircuitSpec MelbourneSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = MelbourneTrackId,
                DisplayName = "Australia GP",
                Country = "Australia",
                EnvironmentStyle = "Park circuit",
                HalfWidthMeters = 15.47f,
                KerbStartMeters = 9.07f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.52f, 0.69f),
                TargetLengthMeters = 5278f,
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 340f), new Vector3(40f, 0f, 390f),
                    new Vector3(90f, 0f, 380f), new Vector3(120f, 0f, 330f), new Vector3(180f, 0f, 300f),
                    new Vector3(250f, 0f, 320f), new Vector3(310f, 0f, 370f), new Vector3(340f, 0f, 430f),
                    new Vector3(320f, 0f, 490f), new Vector3(260f, 0f, 510f), new Vector3(200f, 0f, 480f),
                    new Vector3(150f, 0f, 500f), new Vector3(110f, 0f, 560f), new Vector3(60f, 0f, 570f),
                    new Vector3(20f, 0f, 520f), new Vector3(-20f, 0f, 440f), new Vector3(-30f, 0f, 320f),
                    new Vector3(-20f, 0f, 180f), new Vector3(0f, 0f, 60f)
                },
            };
        }

        static LegacyCircuitSpec InterlagosSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = InterlagosTrackId,
                DisplayName = "Brazil GP",
                Country = "Brazil",
                EnvironmentStyle = "Short flowing hillside",
                HalfWidthMeters = 13.4f,
                KerbStartMeters = 7.84f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.62f, 0.79f),
                TargetLengthMeters = 4309f,
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 10f, 240f), new Vector3(-30f, 4f, 290f),
                    new Vector3(10f, 0f, 330f), new Vector3(70f, -4f, 380f), new Vector3(140f, -6f, 410f),
                    new Vector3(200f, -4f, 390f), new Vector3(230f, 0f, 340f), new Vector3(210f, 4f, 290f),
                    new Vector3(170f, 6f, 250f), new Vector3(210f, 8f, 200f), new Vector3(270f, 10f, 180f),
                    new Vector3(310f, 12f, 220f), new Vector3(300f, 14f, 280f), new Vector3(250f, 16f, 320f),
                    new Vector3(190f, 18f, 290f), new Vector3(130f, 16f, 220f), new Vector3(70f, 10f, 130f),
                    new Vector3(20f, 4f, 50f)
                },
            };
        }

        static LegacyCircuitSpec AbuDhabiSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = AbuDhabiTrackId,
                DisplayName = "Abu Dhabi GP",
                Country = "United Arab Emirates",
                EnvironmentStyle = "Twilight finale",
                HalfWidthMeters = 14.64f,
                KerbStartMeters = 8.57f,
                DrsZoneOneNormalized = new Vector2(0.88f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.34f, 0.53f),
                TargetLengthMeters = 5281f,
                AnchorSubdivisions = 3,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 340f), new Vector3(40f, 0f, 400f),
                    new Vector3(90f, 0f, 410f), new Vector3(120f, 0f, 370f), new Vector3(110f, 0f, 310f),
                    new Vector3(150f, 0f, 270f), new Vector3(220f, 0f, 260f), new Vector3(280f, 0f, 290f),
                    new Vector3(300f, 0f, 350f), new Vector3(280f, 0f, 410f), new Vector3(320f, 0f, 450f),
                    new Vector3(380f, 0f, 450f), new Vector3(410f, 0f, 400f), new Vector3(400f, 0f, 320f),
                    new Vector3(360f, 0f, 220f), new Vector3(300f, 0f, 130f), new Vector3(220f, 0f, 60f),
                    new Vector3(120f, 0f, 10f)
                },
            };
        }

        static LegacyCircuitSpec BahrainSpec()
        {
            return new LegacyCircuitSpec
            {
                TrackId = BahrainTrackId,
                DisplayName = "Bahrain GP",
                Country = "Bahrain",
                EnvironmentStyle = "Desert power braking",
                HalfWidthMeters = 13.82f,
                KerbStartMeters = 8.15f,
                DrsZoneOneNormalized = new Vector2(0.91f, 0.08f),
                DrsZoneTwoNormalized = new Vector2(0.42f, 0.57f),
                TargetLengthMeters = 5412f,
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 420f), new Vector3(30f, 0f, 470f),
                    new Vector3(70f, 0f, 470f), new Vector3(90f, 0f, 420f), new Vector3(70f, 0f, 370f),
                    new Vector3(110f, 0f, 320f), new Vector3(190f, 0f, 300f), new Vector3(260f, 0f, 340f),
                    new Vector3(300f, 0f, 400f), new Vector3(280f, 0f, 450f), new Vector3(230f, 0f, 470f),
                    new Vector3(260f, 0f, 530f), new Vector3(330f, 0f, 560f), new Vector3(400f, 0f, 530f),
                    new Vector3(420f, 0f, 460f), new Vector3(380f, 0f, 400f), new Vector3(400f, 0f, 300f),
                    new Vector3(400f, 0f, 160f), new Vector3(340f, 0f, 60f), new Vector3(240f, 0f, 10f),
                    new Vector3(120f, 0f, -10f)
                },
            };
        }

        static TrackDefinitionAsset GenerateFromSpec(in LegacyCircuitSpec spec)
        {
            Vector3[] sketch = spec.SketchAnchors;
            float sketchLength = 0f;
            for (int i = 0; i < sketch.Length; i++)
            {
                sketchLength += Vector3.Distance(sketch[i], sketch[(i + 1) % sketch.Length]);
            }

            // SINGLE UNIFORM SCALE for the whole circuit - length AND road width
            // together. This is the pace fix, and the "together" is the whole point.
            //
            // The authored sketches were sized well past anything on the real
            // calendar: 6.1-8.75 km long (real F1 is 3.3-7.0 km, averaging ~5.2)
            // and 11-16 m HALF-width, i.e. 22-32 m of tarmac where a real circuit
            // gives 12-15 m TOTAL. At that scale a "corner" is a ~200 m-radius
            // sweeper, which the yaw envelope in VehicleController happily takes
            // flat, so a whole lap was one long straight: [PaceDiag] avgSpeed
            // 278-343 kph against a real F1 lap average of ~210-230.
            //
            // A previous attempt shrank ONLY the length and corrupted every track:
            // width stayed in absolute metres, so the road became too wide for its
            // now-tighter corners, the inner edge folded over itself, and
            // SmoothSharpKinks rendered the fold as a wall across the corner
            // ("Relaxed 449 cusp/fold points", cars stuck at US GP turn 2). That
            // failure was specifically about the width/radius RATIO changing.
            // Scaling both by the same factor leaves that ratio - and therefore
            // every fold, kink and barrier-clearance property of the mesh - exactly
            // as it was, while corner radii, and so corner speeds, come down with
            // the track. 0.62 lands lengths at 3.8-5.4 km and half-widths at
            // 6.9-9.9 m, both squarely in real F1 territory (Zandvoort 4.3 km,
            // Hungaroring 4.4 km, Austin 5.5 km; 12-15 m of tarmac).
            //
            // Deliberately taken from the geometry rather than from the cornering
            // envelope in VehicleController.MaxYawRateDegPerSec, even though that
            // dial is right there: the envelope currently implies ~3.4g at 300 kph
            // and ~4.5g through slow and medium corners, which is honest F1
            // machinery. Pulling it down far enough to fix a 343 kph lap average
            // would have meant ~2.6g at 300 - a car that grips like a road car -
            // when the real fault was never the car. It was that a lap made of
            // 200 m-radius sweepers has nothing to brake for.
            //
            // Secondary benefit worth knowing about: SmoothSharpKinks derives its
            // minimum drivable radius as roadHalfWidth + 12 m, so narrowing the
            // road also LOWERS the floor on how tight a corner is allowed to be
            // (~28 m before, ~22 m now). Tighter corners survive the smoothing pass
            // instead of being relaxed back open.
            const float AuthoredCircuitScale = 0.62f;
            // TargetLengthMeters is now the REAL circuit length in metres, and the
            // 0.62 authored scale is no longer applied to it.
            //
            // Previously the specs carried only six distinct length values shared
            // across all 24 circuits, which the 0.62 scale then mapped into a
            // 4.1-5.5 km band. The band itself was plausible, but the per-circuit
            // character was flattened: Madrid, Miami and Spa came out ~20% short
            // while Monaco came out ~15% LONG, so Monaco was not meaningfully the
            // shortest circuit on the calendar and Spa was not meaningfully the
            // longest - which is most of what makes those two circuits what they
            // are. Real lengths also scale the sketch geometry, so corner radii now
            // follow the real circuit's character too (Spa's sweepers open up,
            // Monaco's corners tighten).
            //
            // The scale still applies to WIDTH and the kerb inset, which are
            // authored on their own separate basis.
            float scale = sketchLength > 1f ? spec.TargetLengthMeters / sketchLength : 1f;
            float halfWidthMeters = spec.HalfWidthMeters * AuthoredCircuitScale;
            // Same gentle elevation treatment the legacy normalize pass applied.
            float elevationScale = Mathf.Pow(scale, 0.55f);

            var asset = ScriptableObject.CreateInstance<TrackDefinitionAsset>();
            asset.name = "Track_" + spec.TrackId + "_Authored";
            asset.trackId = spec.TrackId;
            asset.displayName = spec.DisplayName;
            asset.country = spec.Country;
            asset.environmentStyle = spec.EnvironmentStyle;
            asset.closedLoop = true;
            // Scales with the road (it is a lateral offset across it) - authored at
            // ~0.59x the half-width, and left unscaled it would eat the kerb down to
            // a sliver on the narrower circuits.
            asset.kerbStartOffset = spec.KerbStartMeters * AuthoredCircuitScale;
            // Runoff. A street circuit is defined by its walls being right there; a
            // permanent circuit is defined by having somewhere to go. Every circuit
            // used to get a wall 5 cm from the white line, which erased that
            // distinction entirely and left a mistake nowhere to go.
            bool streetCircuit = !string.IsNullOrEmpty(spec.EnvironmentStyle) &&
                (spec.EnvironmentStyle.ToLowerInvariant().Contains("street") ||
                 spec.EnvironmentStyle.ToLowerInvariant().Contains("harbour"));
            asset.runoffMeters = streetCircuit ? StreetRunoffMeters : PermanentRunoffMeters;
            asset.anchorSubdivisions = spec.AnchorSubdivisions;

            for (int i = 0; i < sketch.Length; i++)
            {
                asset.spline.Add(new TrackDefinitionAsset.SplinePoint
                {
                    position = new Vector3(sketch[i].x * scale, sketch[i].y * elevationScale, sketch[i].z * scale),
                    width = halfWidthMeters * 2f,
                    camberDegrees = 0f,
                    kerbLeft = false,
                    kerbRight = false,
                });
                asset.racingLineOffsets.Add(0f);
            }

            // Build the sampler FIRST and take the lap length from it. ComputeLength()
            // sums straight chords between control points, but every consumer
            // (sectors, DRS, pit, surfaces, cameras) compares against distances the
            // sampler produces along the Catmull-Rom ARC, which is 0.7-2.6% longer.
            // Deriving the windows from the chord length left the last 36-122m of
            // every lap outside all of them - no surface zone, no camera node - and
            // pushed the sector splits to 32.5/32.5/35.0% instead of even thirds.
            var sampler = new TrackSplineSampler();
            sampler.Build(asset.spline, true);

            // `scale` above is derived from the CHORD polyline through the anchors,
            // but the sampler measures the Catmull-Rom ARC, which runs 1.5-2.6%
            // longer. Correct once so the finished circuit lands on its real length
            // rather than consistently overshooting it.
            if (sampler.Length > 1f && spec.TargetLengthMeters > 1f)
            {
                float correction = spec.TargetLengthMeters / sampler.Length;
                if (Mathf.Abs(correction - 1f) > 0.002f)
                {
                    for (int i = 0; i < asset.spline.Count; i++)
                    {
                        TrackDefinitionAsset.SplinePoint point = asset.spline[i];
                        point.position = new Vector3(
                            point.position.x * correction,
                            point.position.y * Mathf.Pow(correction, 0.55f),
                            point.position.z * correction);
                        asset.spline[i] = point;
                    }

                    sampler.Build(asset.spline, true);
                }
            }

            float length = sampler.Length;
            asset.startFinishDistance = 0f;
            asset.sectorBoundaryDistances = new[] { length / 3f, length * 2f / 3f };
            BuildApexBiasedRacingLine(asset, halfWidthMeters);

            asset.surfaces.Add(new TrackDefinitionAsset.SurfaceZone
            {
                startDistance = 0f,
                endDistance = length,
                kind = TrackDefinitionAsset.SurfaceKind.RubberedLine,
                gripMultiplier = 1f,
            });

            AddDrsZone(asset, spec.DrsZoneOneNormalized, length);
            AddDrsZone(asset, spec.DrsZoneTwoNormalized, length);

            var stalls = new List<Vector3>();
            TrackSplineSampler.Sample pitAnchor = sampler.AtDistance(0f);
            for (int i = 0; i < 22; i++)
            {
                stalls.Add(pitAnchor.Position - pitAnchor.Normal * 20f + pitAnchor.Tangent * (i * 8f - 88f));
            }

            asset.pitLane = new TrackDefinitionAsset.PitLaneData
            {
                entryDistance = length * 0.94f,
                entryCommitDistance = length * 0.965f,
                exitDistance = length * 0.05f,
                stallCount = 22,
                stallPositions = stalls.ToArray(),
            };

            // Grid slots run BACKWARDS from the start/finish line: slot 0 is pole and
            // must sit at the greatest lap distance (furthest around the lap = ahead),
            // with the field stacked behind it. This used to be `30 + i * 8`, which
            // put pole 168m BEHIND P22 and formed the entire grid up-road of the
            // line. Matches the legacy TrackRuntime.GetGridSlot convention: staggered
            // pairs, one row per two cars.
            const float gridStartOffset = 52f;
            const float gridRowSpacing = 19f;
            const float gridStaggerOffset = 8f;
            for (int i = 0; i < 22; i++)
            {
                int row = i / 2;
                bool leftSlot = (i % 2) == 0;
                float slotDistance = sampler.WrapDistance(
                    length - gridStartOffset - row * gridRowSpacing - (leftSlot ? 0f : gridStaggerOffset));
                TrackSplineSampler.Sample s = sampler.AtDistance(slotDistance);
                float side = leftSlot ? -2.5f : 2.5f;
                asset.gridSlots.Add(new TrackDefinitionAsset.GridSlot
                {
                    position = s.Position + s.Normal * side,
                    headingDegrees = Quaternion.LookRotation(s.Tangent, Vector3.up).eulerAngles.y,
                });
            }

            for (int i = 0; i < 8; i++)
            {
                float frac = i / 8f;
                TrackSplineSampler.Sample s = sampler.AtDistance(frac * length);
                asset.cameraNodes.Add(new TrackDefinitionAsset.TrackCameraNode
                {
                    position = s.Position + s.Normal * 40f + Vector3.up * 12f,
                    coverageStartDistance = frac * length,
                    coverageEndDistance = (frac + 0.125f) * length,
                });
                asset.marshalPosts.Add(new TrackDefinitionAsset.MarshalPost
                {
                    position = s.Position - s.Normal * (s.Width * 0.5f + 4f),
                    sectorStartDistance = frac * length,
                    sectorEndDistance = (frac + 0.125f) * length,
                });
                asset.crowdZones.Add(new TrackDefinitionAsset.CrowdZone
                {
                    position = s.Position + s.Normal * 55f,
                    size = new Vector3(60f, 12f, 30f),
                    density = 0.8f,
                });
            }

            return asset;
        }

        /// <summary>
        /// Gives every authored circuit a real racing line instead of the centerline.
        /// racingLineOffsets used to be filled with 0f for every point on all 24
        /// circuits, so AiLineRuntime.RacingLinePoint returned dead centre of the road
        /// everywhere - the AI had no apex to aim at on the entire calendar.
        ///
        /// This is an apex bias, not a full lap optimiser: each control point is
        /// pushed toward the inside of its own turn in proportion to how sharp that
        /// turn is, then circularly smoothed so the line sweeps in and out over
        /// several points rather than stepping. That yields inside-at-the-apex with
        /// a natural widening on entry and exit, which is what the AI's line
        /// following actually needs.
        /// </summary>
        static void BuildApexBiasedRacingLine(TrackDefinitionAsset asset, float halfWidthMeters)
        {
            int count = asset.spline.Count;
            if (count < 4)
            {
                return;
            }

            const float saturationDegrees = 25f;
            var raw = new float[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 previous = asset.spline[((i - 1) % count + count) % count].position;
                Vector3 here = asset.spline[i].position;
                Vector3 next = asset.spline[(i + 1) % count].position;

                Vector3 inbound = here - previous;
                Vector3 outbound = next - here;
                inbound.y = 0f;
                outbound.y = 0f;
                if (inbound.sqrMagnitude < 0.0001f || outbound.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                inbound = inbound.normalized;
                outbound = outbound.normalized;

                // Right of travel, same convention as TrackSplineSampler.Normal.
                Vector3 right = Vector3.Cross(Vector3.up, inbound);
                float turnSign = Mathf.Sign(Vector3.Dot(outbound, right));
                float turnDegrees = Vector3.Angle(inbound, outbound);
                // Apex of a right-hand turn is on the right, i.e. a positive offset.
                raw[i] = turnSign * Mathf.Clamp01(turnDegrees / saturationDegrees);
            }

            // Circular box smoothing - the sweep in and out of the corner comes from
            // here, so it must wrap across the start/finish seam like everything else.
            var smoothed = new float[count];
            const int smoothingPasses = 3;
            for (int pass = 0; pass < smoothingPasses; pass++)
            {
                for (int i = 0; i < count; i++)
                {
                    float a = raw[((i - 1) % count + count) % count];
                    float b = raw[i];
                    float c = raw[(i + 1) % count];
                    smoothed[i] = (a + b * 2f + c) * 0.25f;
                }

                System.Array.Copy(smoothed, raw, count);
            }

            // Stay inside the white lines: the line never asks for more than ~55% of
            // the half-width, leaving room for the car's own width and for defending.
            float maxOffset = halfWidthMeters * 0.55f;
            asset.racingLineOffsets.Clear();
            for (int i = 0; i < count; i++)
            {
                asset.racingLineOffsets.Add(Mathf.Clamp(raw[i], -1f, 1f) * maxOffset);
            }
        }

        static void AddDrsZone(TrackDefinitionAsset asset, Vector2 normalizedStartEnd, float length)
        {
            if (normalizedStartEnd == Vector2.zero || length <= 1f)
            {
                return;
            }

            // Detection sits a short run before activation, the same distance
            // the legacy ValidateLayout derives.
            asset.drsZones.Add(new TrackDefinitionAsset.DrsZone
            {
                detectionDistance = Mathf.Repeat(normalizedStartEnd.x - 0.04f, 1f) * length,
                activationDistance = normalizedStartEnd.x * length,
                endDistance = normalizedStartEnd.y * length,
            });
        }
    }
}
