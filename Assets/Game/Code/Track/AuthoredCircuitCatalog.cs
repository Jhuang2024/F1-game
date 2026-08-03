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
        // Runoff a permanent circuit gets beyond the white line, in metres.
        //
        // Started at 22, which is a realistic figure for a high-speed corner but put
        // the barrier line ~36 m from the centreline - well outside the 26 m catch
        // floor under the circuit, so barriers stood over a void and a car running
        // wide fell off the edge of the world. Widening the floor to match walked
        // straight into the invisible-wall bug that floor's own width exists to
        // avoid, and papering over it with an apron mesh at road level broke the
        // track surface itself.
        //
        // 8 m is the value that needs none of that: the barrier line lands around
        // 24 m even on a hairpin-widened section, comfortably INSIDE the untouched
        // 26 m floor. It is still the difference between "a mistake has somewhere
        // to go" and the 5 cm this used to be.
        const float PermanentRunoffMeters = 8f;

        /// <summary>
        /// Circuit centrelines.
        ///
        /// SketchAnchors used to be hand-drawn approximations - a few dozen points
        /// each, sketched to feel like the circuit. They did not: every layout was
        /// recognisably wrong, which is what "the circuit shapes are ALL completely
        /// wrong" meant.
        ///
        /// They are now the REAL centrelines, projected from the surveyed WGS84
        /// geometry of each circuit into local metres (equirectangular about the
        /// circuit's own mean latitude, so at this scale the distortion is
        /// centimetres), rotated so lap distance zero sits on the real start/finish
        /// line with the pit straight along +Z, and resampled adaptively - dense
        /// through corners, sparse down straights - so the shape survives at about
        /// a hundred anchors instead of several hundred. Every resulting lap length
        /// is within ~0.5% of the circuit's official distance, and every circuit runs
        /// in its real direction.
        ///
        /// Elevation is authored separately (the source geometry is 2D) and only for
        /// the circuits whose elevation IS part of the circuit - Spa, the Red Bull
        /// Ring, Interlagos, COTA, Monaco, Suzuka and a few others. The rest are left
        /// flat rather than given invented undulation.
        /// </summary>
        public struct LegacyCircuitSpec
        {
            public string TrackId;
            public string DisplayName;
            public string Country;
            public string EnvironmentStyle;
            public float HalfWidthMeters;
            public float KerbStartMeters;
            public Vector2 DrsZoneOneNormalized;   // (start, end), wrap allowed
            public Vector2 DrsZoneTwoNormalized;   // Vector2.zero = this circuit has only one
            public Vector2 DrsZoneThreeNormalized; // Vector2.zero = this circuit has fewer than three
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 70.0f), new Vector3(0.0f, 0.0f, 140.0f),
                    new Vector3(0.1f, 0.0f, 210.0f), new Vector3(0.3f, 0.0f, 280.0f), new Vector3(0.8f, 0.0f, 350.0f),
                    new Vector3(1.6f, 0.0f, 410.0f), new Vector3(3.8f, 0.0f, 417.6f), new Vector3(10.4f, 0.0f, 421.7f),
                    new Vector3(18.4f, 0.0f, 422.3f), new Vector3(26.4f, 0.0f, 422.2f), new Vector3(34.3f, 0.0f, 423.4f),
                    new Vector3(40.3f, 0.0f, 428.5f), new Vector3(43.3f, 0.0f, 435.8f), new Vector3(42.3f, 0.0f, 443.7f),
                    new Vector3(19.3f, 0.0f, 509.8f), new Vector3(8.1f, 0.0f, 544.0f), new Vector3(5.6f, 0.0f, 553.6f),
                    new Vector3(2.1f, 0.0f, 571.3f), new Vector3(0.5f, 0.0f, 583.2f), new Vector3(-0.4f, 0.0f, 595.2f),
                    new Vector3(-1.3f, 0.0f, 665.2f), new Vector3(-1.6f, 0.0f, 735.2f), new Vector3(-1.2f, 0.0f, 753.2f),
                    new Vector3(0.6f, 0.0f, 769.1f), new Vector3(2.7f, 0.0f, 782.9f), new Vector3(5.7f, 0.0f, 796.6f),
                    new Vector3(11.0f, 0.0f, 815.9f), new Vector3(18.0f, 0.0f, 836.7f), new Vector3(22.4f, 0.0f, 847.9f),
                    new Vector3(28.3f, 0.0f, 860.6f), new Vector3(35.8f, 0.0f, 874.7f), new Vector3(43.1f, 0.0f, 886.7f),
                    new Vector3(52.1f, 0.0f, 899.9f), new Vector3(64.3f, 0.0f, 915.7f), new Vector3(76.1f, 0.0f, 929.4f),
                    new Vector3(88.6f, 0.0f, 942.3f), new Vector3(101.8f, 0.0f, 954.5f), new Vector3(115.6f, 0.0f, 966.0f),
                    new Vector3(130.1f, 0.0f, 976.8f), new Vector3(153.5f, 0.0f, 992.2f), new Vector3(168.9f, 0.0f, 1001.4f),
                    new Vector3(183.1f, 0.0f, 1008.8f), new Vector3(203.3f, 0.0f, 1017.6f), new Vector3(229.4f, 0.0f, 1027.7f),
                    new Vector3(248.4f, 0.0f, 1033.8f), new Vector3(271.6f, 0.0f, 1039.9f), new Vector3(301.0f, 0.0f, 1045.9f),
                    new Vector3(370.0f, 0.0f, 1057.9f), new Vector3(439.0f, 0.0f, 1069.6f), new Vector3(508.2f, 0.0f, 1080.6f),
                    new Vector3(577.4f, 0.0f, 1090.9f), new Vector3(646.7f, 0.0f, 1101.1f), new Vector3(674.4f, 0.0f, 1105.4f),
                    new Vector3(681.7f, 0.0f, 1108.5f), new Vector3(686.9f, 0.0f, 1114.4f), new Vector3(689.3f, 0.0f, 1122.0f),
                    new Vector3(692.2f, 0.0f, 1135.7f), new Vector3(693.7f, 0.0f, 1141.5f), new Vector3(698.0f, 0.0f, 1148.1f),
                    new Vector3(705.1f, 0.0f, 1151.8f), new Vector3(718.5f, 0.0f, 1155.8f), new Vector3(751.1f, 0.0f, 1165.5f),
                    new Vector3(769.9f, 0.0f, 1172.4f), new Vector3(834.2f, 0.0f, 1199.9f), new Vector3(898.5f, 0.0f, 1227.7f),
                    new Vector3(962.8f, 0.0f, 1255.4f), new Vector3(981.3f, 0.0f, 1262.9f), new Vector3(987.0f, 0.0f, 1264.7f),
                    new Vector3(992.9f, 0.0f, 1265.9f), new Vector3(998.9f, 0.0f, 1266.6f), new Vector3(1004.9f, 0.0f, 1266.8f),
                    new Vector3(1010.9f, 0.0f, 1266.5f), new Vector3(1016.8f, 0.0f, 1265.8f), new Vector3(1022.7f, 0.0f, 1264.6f),
                    new Vector3(1028.4f, 0.0f, 1262.7f), new Vector3(1033.9f, 0.0f, 1260.4f), new Vector3(1042.8f, 0.0f, 1255.9f),
                    new Vector3(1047.9f, 0.0f, 1252.7f), new Vector3(1052.7f, 0.0f, 1249.0f), new Vector3(1058.6f, 0.0f, 1243.7f),
                    new Vector3(1062.8f, 0.0f, 1239.3f), new Vector3(1066.6f, 0.0f, 1234.7f), new Vector3(1069.9f, 0.0f, 1229.7f),
                    new Vector3(1072.7f, 0.0f, 1224.4f), new Vector3(1074.9f, 0.0f, 1218.8f), new Vector3(1076.7f, 0.0f, 1213.1f),
                    new Vector3(1080.3f, 0.0f, 1195.5f), new Vector3(1092.8f, 0.0f, 1126.6f), new Vector3(1105.5f, 0.0f, 1057.8f),
                    new Vector3(1118.2f, 0.0f, 988.9f), new Vector3(1121.0f, 0.0f, 971.1f), new Vector3(1121.3f, 0.0f, 965.1f),
                    new Vector3(1120.8f, 0.0f, 959.2f), new Vector3(1119.5f, 0.0f, 953.3f), new Vector3(1116.2f, 0.0f, 946.1f),
                    new Vector3(1110.6f, 0.0f, 940.3f), new Vector3(1106.0f, 0.0f, 936.5f), new Vector3(1071.2f, 0.0f, 913.0f),
                    new Vector3(1012.7f, 0.0f, 874.5f), new Vector3(954.5f, 0.0f, 835.6f), new Vector3(896.5f, 0.0f, 796.3f),
                    new Vector3(838.5f, 0.0f, 757.3f), new Vector3(814.0f, 0.0f, 739.9f), new Vector3(789.3f, 0.0f, 719.6f),
                    new Vector3(763.9f, 0.0f, 697.0f), new Vector3(714.8f, 0.0f, 647.1f), new Vector3(665.9f, 0.0f, 597.0f),
                    new Vector3(617.5f, 0.0f, 546.3f), new Vector3(569.6f, 0.0f, 495.3f), new Vector3(521.3f, 0.0f, 444.7f),
                    new Vector3(472.8f, 0.0f, 394.1f), new Vector3(424.4f, 0.0f, 343.6f), new Vector3(375.9f, 0.0f, 293.1f),
                    new Vector3(337.6f, 0.0f, 252.2f), new Vector3(333.9f, 0.0f, 247.5f), new Vector3(330.5f, 0.0f, 242.6f),
                    new Vector3(327.4f, 0.0f, 235.2f), new Vector3(327.0f, 0.0f, 227.2f), new Vector3(327.5f, 0.0f, 221.3f),
                    new Vector3(328.4f, 0.0f, 215.3f), new Vector3(332.7f, 0.0f, 195.8f), new Vector3(334.2f, 0.0f, 187.9f),
                    new Vector3(335.5f, 0.0f, 176.0f), new Vector3(336.0f, 0.0f, 168.0f), new Vector3(336.0f, 0.0f, 162.0f),
                    new Vector3(335.3f, 0.0f, 156.1f), new Vector3(334.2f, 0.0f, 150.2f), new Vector3(331.1f, 0.0f, 138.6f),
                    new Vector3(329.3f, 0.0f, 132.9f), new Vector3(326.9f, 0.0f, 127.3f), new Vector3(324.3f, 0.0f, 122.0f),
                    new Vector3(320.2f, 0.0f, 115.1f), new Vector3(315.8f, 0.0f, 108.4f), new Vector3(311.0f, 0.0f, 102.0f),
                    new Vector3(307.1f, 0.0f, 97.4f), new Vector3(300.2f, 0.0f, 90.2f), new Vector3(288.6f, 0.0f, 79.2f),
                    new Vector3(284.5f, 0.0f, 74.8f), new Vector3(280.1f, 0.0f, 68.1f), new Vector3(277.2f, 0.0f, 60.7f),
                    new Vector3(275.9f, 0.0f, 54.8f), new Vector3(275.0f, 0.0f, 48.9f), new Vector3(273.8f, 0.0f, 32.9f),
                    new Vector3(271.0f, 0.0f, -37.0f), new Vector3(268.8f, 0.0f, -107.0f), new Vector3(267.4f, 0.0f, -177.0f),
                    new Vector3(266.0f, 0.0f, -247.0f), new Vector3(264.7f, 0.0f, -317.0f), new Vector3(263.4f, 0.0f, -387.0f),
                    new Vector3(262.2f, 0.0f, -457.0f), new Vector3(260.9f, 0.0f, -527.0f), new Vector3(259.7f, 0.0f, -597.0f),
                    new Vector3(258.4f, 0.0f, -667.0f), new Vector3(257.1f, 0.0f, -736.9f), new Vector3(255.9f, 0.0f, -806.9f),
                    new Vector3(254.5f, 0.0f, -876.9f), new Vector3(253.6f, 0.0f, -908.9f), new Vector3(252.8f, 0.0f, -916.9f),
                    new Vector3(251.7f, 0.0f, -922.8f), new Vector3(250.0f, 0.0f, -928.5f), new Vector3(247.8f, 0.0f, -934.1f),
                    new Vector3(245.1f, 0.0f, -939.5f), new Vector3(241.9f, 0.0f, -944.6f), new Vector3(238.3f, 0.0f, -949.4f),
                    new Vector3(234.5f, 0.0f, -954.0f), new Vector3(230.3f, 0.0f, -958.3f), new Vector3(225.9f, 0.0f, -962.4f),
                    new Vector3(221.2f, 0.0f, -966.1f), new Vector3(216.3f, 0.0f, -969.6f), new Vector3(211.1f, 0.0f, -972.6f),
                    new Vector3(205.7f, 0.0f, -975.2f), new Vector3(200.1f, 0.0f, -977.4f), new Vector3(194.4f, 0.0f, -979.2f),
                    new Vector3(188.6f, 0.0f, -980.8f), new Vector3(182.7f, 0.0f, -981.9f), new Vector3(176.8f, 0.0f, -982.4f),
                    new Vector3(170.8f, 0.0f, -982.5f), new Vector3(164.8f, 0.0f, -982.1f), new Vector3(158.8f, 0.0f, -981.4f),
                    new Vector3(152.9f, 0.0f, -980.3f), new Vector3(145.2f, 0.0f, -978.4f), new Vector3(129.9f, 0.0f, -973.6f),
                    new Vector3(122.4f, 0.0f, -970.8f), new Vector3(117.0f, 0.0f, -968.3f), new Vector3(111.7f, 0.0f, -965.4f),
                    new Vector3(98.1f, 0.0f, -957.0f), new Vector3(93.2f, 0.0f, -953.5f), new Vector3(87.0f, 0.0f, -948.5f),
                    new Vector3(79.5f, 0.0f, -941.8f), new Vector3(72.5f, 0.0f, -934.7f), new Vector3(67.2f, 0.0f, -928.7f),
                    new Vector3(59.8f, 0.0f, -919.3f), new Vector3(54.0f, 0.0f, -911.1f), new Vector3(48.8f, 0.0f, -902.6f),
                    new Vector3(42.3f, 0.0f, -890.2f), new Vector3(33.8f, 0.0f, -872.0f), new Vector3(27.9f, 0.0f, -857.2f),
                    new Vector3(24.0f, 0.0f, -845.8f), new Vector3(21.3f, 0.0f, -836.2f), new Vector3(15.1f, 0.0f, -806.8f),
                    new Vector3(12.4f, 0.0f, -789.1f), new Vector3(5.0f, 0.0f, -719.4f), new Vector3(-2.5f, 0.0f, -649.8f),
                    new Vector3(-6.6f, 0.0f, -594.0f), new Vector3(-7.3f, 0.0f, -524.0f), new Vector3(-6.3f, 0.0f, -454.0f),
                    new Vector3(-5.0f, 0.0f, -384.0f), new Vector3(-3.5f, 0.0f, -314.0f), new Vector3(-1.5f, 0.0f, -244.0f),
                    new Vector3(-0.5f, 0.0f, -174.0f), new Vector3(-0.1f, 0.0f, -104.0f), new Vector3(0.0f, 0.0f, -34.0f)
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 54.0f), new Vector3(0.4f, 0.0f, 60.0f),
                    new Vector3(1.7f, 0.1f, 67.9f), new Vector3(3.7f, 0.1f, 77.7f), new Vector3(5.3f, 0.1f, 83.5f),
                    new Vector3(7.4f, 0.1f, 89.1f), new Vector3(10.0f, 0.1f, 94.5f), new Vector3(14.1f, 0.1f, 101.4f),
                    new Vector3(19.7f, 0.1f, 109.7f), new Vector3(23.4f, 0.2f, 114.4f), new Vector3(27.5f, 0.2f, 118.8f),
                    new Vector3(33.2f, 0.2f, 124.4f), new Vector3(39.3f, 0.2f, 129.6f), new Vector3(44.0f, 0.2f, 133.3f),
                    new Vector3(49.1f, 0.2f, 136.5f), new Vector3(54.3f, 0.3f, 139.4f), new Vector3(59.8f, 0.3f, 141.9f),
                    new Vector3(65.5f, 0.3f, 143.9f), new Vector3(71.2f, 0.3f, 145.5f), new Vector3(83.0f, 0.4f, 147.8f),
                    new Vector3(89.0f, 0.4f, 148.6f), new Vector3(95.0f, 0.4f, 148.9f), new Vector3(100.9f, 0.4f, 148.4f),
                    new Vector3(108.8f, 0.5f, 146.8f), new Vector3(120.4f, 0.5f, 143.8f), new Vector3(128.0f, 0.5f, 141.4f),
                    new Vector3(133.6f, 0.6f, 139.2f), new Vector3(139.0f, 0.6f, 136.6f), new Vector3(144.3f, 0.6f, 133.8f),
                    new Vector3(149.3f, 0.6f, 130.4f), new Vector3(153.8f, 0.7f, 126.5f), new Vector3(157.8f, 0.7f, 122.0f),
                    new Vector3(161.3f, 0.7f, 117.1f), new Vector3(164.1f, 0.8f, 111.8f), new Vector3(166.2f, 0.8f, 106.2f),
                    new Vector3(167.7f, 0.8f, 100.4f), new Vector3(168.8f, 0.8f, 94.5f), new Vector3(169.5f, 0.9f, 88.5f),
                    new Vector3(169.5f, 0.9f, 82.5f), new Vector3(168.8f, 0.9f, 76.6f), new Vector3(167.4f, 0.9f, 70.7f),
                    new Vector3(165.5f, 1.0f, 65.1f), new Vector3(162.8f, 1.0f, 59.7f), new Vector3(159.5f, 1.0f, 54.7f),
                    new Vector3(155.6f, 1.1f, 50.1f), new Vector3(149.0f, 1.1f, 45.7f), new Vector3(143.4f, 1.1f, 43.5f),
                    new Vector3(135.5f, 1.2f, 42.9f), new Vector3(129.6f, 1.2f, 44.1f), new Vector3(124.0f, 1.2f, 46.1f),
                    new Vector3(116.7f, 1.3f, 49.5f), new Vector3(95.6f, 1.4f, 60.9f), new Vector3(88.4f, 1.4f, 64.5f),
                    new Vector3(80.7f, 1.5f, 66.4f), new Vector3(72.9f, 1.5f, 65.1f), new Vector3(67.3f, 1.6f, 62.7f),
                    new Vector3(62.3f, 1.6f, 59.5f), new Vector3(57.9f, 1.6f, 55.4f), new Vector3(54.3f, 1.6f, 50.6f),
                    new Vector3(51.6f, 1.7f, 45.3f), new Vector3(50.0f, 1.7f, 39.5f), new Vector3(49.5f, 1.7f, 33.5f),
                    new Vector3(49.7f, 1.8f, 27.5f), new Vector3(50.8f, 1.8f, 21.6f), new Vector3(53.2f, 1.8f, 16.1f),
                    new Vector3(56.8f, 1.9f, 11.4f), new Vector3(64.1f, 1.9f, 4.6f), new Vector3(70.2f, 2.0f, -0.6f),
                    new Vector3(76.6f, 2.0f, -5.4f), new Vector3(81.6f, 2.0f, -8.7f), new Vector3(86.9f, 2.1f, -11.7f),
                    new Vector3(92.2f, 2.1f, -14.3f), new Vector3(97.8f, 2.1f, -16.6f), new Vector3(105.3f, 2.1f, -19.3f),
                    new Vector3(111.1f, 2.2f, -20.9f), new Vector3(117.0f, 2.2f, -22.0f), new Vector3(123.0f, 2.2f, -22.6f),
                    new Vector3(129.0f, 2.3f, -22.4f), new Vector3(134.9f, 2.3f, -21.5f), new Vector3(140.8f, 2.3f, -20.1f),
                    new Vector3(154.2f, 2.4f, -16.3f), new Vector3(159.8f, 2.4f, -14.2f), new Vector3(170.8f, 2.4f, -9.3f),
                    new Vector3(193.8f, 2.6f, 2.8f), new Vector3(248.1f, 2.8f, 32.8f), new Vector3(287.8f, 2.9f, 51.9f),
                    new Vector3(318.2f, 2.9f, 67.0f), new Vector3(379.7f, 3.0f, 100.6f), new Vector3(397.6f, 3.0f, 109.4f),
                    new Vector3(406.8f, 3.0f, 113.4f), new Vector3(412.5f, 3.0f, 115.3f), new Vector3(420.2f, 3.0f, 117.5f),
                    new Vector3(426.0f, 3.0f, 118.8f), new Vector3(431.9f, 3.0f, 119.8f), new Vector3(501.6f, 3.1f, 126.8f),
                    new Vector3(571.3f, 3.2f, 133.8f), new Vector3(627.0f, 3.3f, 139.2f), new Vector3(637.0f, 3.3f, 139.6f),
                    new Vector3(643.0f, 3.4f, 139.4f), new Vector3(650.1f, 3.4f, 136.0f), new Vector3(654.0f, 3.4f, 129.1f),
                    new Vector3(654.5f, 3.4f, 121.2f), new Vector3(652.9f, 3.4f, 115.4f), new Vector3(650.3f, 3.5f, 110.0f),
                    new Vector3(647.1f, 3.5f, 104.9f), new Vector3(643.2f, 3.5f, 100.4f), new Vector3(638.9f, 3.5f, 96.2f),
                    new Vector3(629.8f, 3.5f, 88.3f), new Vector3(621.9f, 3.6f, 82.2f), new Vector3(613.6f, 3.6f, 76.7f),
                    new Vector3(582.6f, 3.7f, 58.3f), new Vector3(573.8f, 3.7f, 53.6f), new Vector3(564.7f, 3.8f, 49.3f),
                    new Vector3(553.6f, 3.8f, 44.8f), new Vector3(525.2f, 3.9f, 35.3f), new Vector3(458.0f, 4.1f, 15.3f),
                    new Vector3(391.0f, 4.3f, -4.8f), new Vector3(375.8f, 4.4f, -10.0f), new Vector3(364.8f, 4.4f, -14.6f),
                    new Vector3(355.8f, 4.4f, -19.0f), new Vector3(345.3f, 4.4f, -24.8f), new Vector3(338.5f, 4.5f, -29.1f),
                    new Vector3(333.6f, 4.5f, -32.5f), new Vector3(329.0f, 4.5f, -36.4f), new Vector3(324.7f, 4.5f, -40.6f),
                    new Vector3(320.7f, 4.5f, -45.0f), new Vector3(315.6f, 4.6f, -51.3f), new Vector3(311.0f, 4.6f, -57.8f),
                    new Vector3(306.7f, 4.6f, -64.5f), new Vector3(302.7f, 4.6f, -71.5f), new Vector3(299.3f, 4.6f, -78.7f),
                    new Vector3(297.0f, 4.7f, -84.2f), new Vector3(295.1f, 4.7f, -89.9f), new Vector3(293.0f, 4.7f, -97.6f),
                    new Vector3(291.4f, 4.7f, -105.5f), new Vector3(290.5f, 4.7f, -111.4f), new Vector3(289.9f, 4.7f, -117.4f),
                    new Vector3(289.7f, 4.7f, -125.4f), new Vector3(290.0f, 4.8f, -133.4f), new Vector3(290.7f, 4.8f, -141.4f),
                    new Vector3(291.5f, 4.8f, -147.3f), new Vector3(292.8f, 4.8f, -153.2f), new Vector3(294.9f, 4.8f, -160.9f),
                    new Vector3(298.2f, 4.8f, -170.3f), new Vector3(302.9f, 4.9f, -181.4f), new Vector3(307.3f, 4.9f, -190.3f),
                    new Vector3(311.3f, 4.9f, -197.3f), new Vector3(315.7f, 4.9f, -204.0f), new Vector3(319.3f, 4.9f, -208.8f),
                    new Vector3(323.2f, 4.9f, -213.3f), new Vector3(329.0f, 4.9f, -218.9f), new Vector3(336.5f, 4.9f, -225.4f),
                    new Vector3(391.3f, 5.0f, -269.1f), new Vector3(403.4f, 5.0f, -279.5f), new Vector3(407.7f, 5.0f, -283.7f),
                    new Vector3(411.7f, 5.0f, -288.2f), new Vector3(415.4f, 5.0f, -292.9f), new Vector3(418.8f, 5.0f, -297.9f),
                    new Vector3(421.6f, 5.0f, -303.2f), new Vector3(424.0f, 5.0f, -308.7f), new Vector3(425.8f, 5.0f, -314.4f),
                    new Vector3(427.8f, 5.0f, -322.1f), new Vector3(429.8f, 5.0f, -331.9f), new Vector3(430.5f, 5.0f, -337.9f),
                    new Vector3(430.8f, 5.0f, -343.9f), new Vector3(430.4f, 5.0f, -349.9f), new Vector3(429.5f, 5.0f, -355.8f),
                    new Vector3(428.3f, 5.0f, -361.7f), new Vector3(426.7f, 5.0f, -367.5f), new Vector3(424.8f, 5.0f, -373.1f),
                    new Vector3(422.3f, 5.0f, -378.6f), new Vector3(419.3f, 4.9f, -383.8f), new Vector3(415.8f, 4.9f, -388.7f),
                    new Vector3(411.9f, 4.9f, -393.3f), new Vector3(407.7f, 4.9f, -397.5f), new Vector3(401.7f, 4.9f, -402.9f),
                    new Vector3(397.0f, 4.9f, -406.6f), new Vector3(392.1f, 4.9f, -410.0f), new Vector3(386.9f, 4.9f, -413.0f),
                    new Vector3(381.3f, 4.9f, -415.2f), new Vector3(358.2f, 4.8f, -421.7f), new Vector3(300.2f, 4.7f, -437.0f),
                    new Vector3(292.6f, 4.7f, -439.4f), new Vector3(287.0f, 4.7f, -441.7f), new Vector3(282.0f, 4.7f, -445.0f),
                    new Vector3(276.9f, 4.7f, -451.1f), new Vector3(274.5f, 4.6f, -456.6f), new Vector3(273.4f, 4.6f, -462.5f),
                    new Vector3(273.4f, 4.6f, -468.5f), new Vector3(274.0f, 4.6f, -474.5f), new Vector3(277.6f, 4.6f, -481.6f),
                    new Vector3(282.2f, 4.6f, -488.2f), new Vector3(298.3f, 4.5f, -508.5f), new Vector3(319.2f, 4.4f, -532.8f),
                    new Vector3(323.4f, 4.4f, -537.1f), new Vector3(328.3f, 4.4f, -540.5f), new Vector3(335.9f, 4.3f, -542.8f),
                    new Vector3(341.9f, 4.3f, -543.0f), new Vector3(347.8f, 4.3f, -541.8f), new Vector3(414.9f, 4.1f, -521.8f),
                    new Vector3(481.7f, 3.9f, -501.0f), new Vector3(548.5f, 3.7f, -479.8f), new Vector3(615.0f, 3.5f, -458.0f),
                    new Vector3(681.5f, 3.3f, -436.1f), new Vector3(748.0f, 3.2f, -414.2f), new Vector3(774.4f, 3.1f, -404.6f),
                    new Vector3(779.9f, 3.1f, -402.3f), new Vector3(786.4f, 3.1f, -397.9f), new Vector3(788.4f, 3.1f, -390.2f),
                    new Vector3(786.8f, 3.1f, -382.4f), new Vector3(784.1f, 3.1f, -377.0f), new Vector3(781.0f, 3.1f, -371.9f),
                    new Vector3(772.9f, 3.0f, -360.5f), new Vector3(766.9f, 3.0f, -352.5f), new Vector3(763.6f, 3.0f, -347.4f),
                    new Vector3(761.2f, 3.0f, -342.0f), new Vector3(759.6f, 3.0f, -336.2f), new Vector3(759.0f, 3.0f, -330.2f),
                    new Vector3(759.5f, 3.0f, -324.2f), new Vector3(761.3f, 3.0f, -318.6f), new Vector3(766.1f, 3.0f, -312.1f),
                    new Vector3(770.7f, 3.0f, -308.4f), new Vector3(777.4f, 3.0f, -304.0f), new Vector3(787.8f, 3.0f, -298.0f),
                    new Vector3(793.2f, 3.0f, -295.3f), new Vector3(798.7f, 3.0f, -292.9f), new Vector3(804.4f, 3.0f, -291.1f),
                    new Vector3(810.2f, 3.0f, -289.6f), new Vector3(816.1f, 3.0f, -288.6f), new Vector3(822.1f, 3.0f, -288.1f),
                    new Vector3(828.1f, 3.0f, -288.3f), new Vector3(834.1f, 3.0f, -288.8f), new Vector3(840.0f, 3.0f, -289.7f),
                    new Vector3(845.9f, 3.0f, -291.1f), new Vector3(851.6f, 3.0f, -292.8f), new Vector3(859.2f, 3.0f, -295.5f),
                    new Vector3(866.5f, 3.0f, -298.6f), new Vector3(871.9f, 3.0f, -301.3f), new Vector3(877.1f, 3.0f, -304.3f),
                    new Vector3(882.0f, 2.9f, -307.7f), new Vector3(886.5f, 2.9f, -311.8f), new Vector3(890.5f, 2.9f, -316.2f),
                    new Vector3(894.3f, 2.9f, -320.8f), new Vector3(897.8f, 2.9f, -325.7f), new Vector3(900.9f, 2.9f, -330.9f),
                    new Vector3(903.4f, 2.9f, -336.3f), new Vector3(905.4f, 2.9f, -342.0f), new Vector3(906.9f, 2.9f, -347.8f),
                    new Vector3(908.0f, 2.9f, -353.7f), new Vector3(908.7f, 2.9f, -359.6f), new Vector3(909.1f, 2.9f, -365.6f),
                    new Vector3(909.0f, 2.8f, -373.6f), new Vector3(908.4f, 2.8f, -383.6f), new Vector3(907.6f, 2.8f, -389.6f),
                    new Vector3(906.6f, 2.8f, -395.5f), new Vector3(904.6f, 2.8f, -403.2f), new Vector3(902.8f, 2.8f, -408.9f),
                    new Vector3(899.8f, 2.8f, -416.4f), new Vector3(895.5f, 2.7f, -425.4f), new Vector3(890.7f, 2.7f, -434.2f),
                    new Vector3(887.5f, 2.7f, -439.2f), new Vector3(884.0f, 2.7f, -444.1f), new Vector3(876.4f, 2.7f, -453.4f),
                    new Vector3(872.3f, 2.6f, -457.8f), new Vector3(867.9f, 2.6f, -461.9f), new Vector3(861.7f, 2.6f, -466.9f),
                    new Vector3(853.4f, 2.6f, -472.5f), new Vector3(846.6f, 2.6f, -476.7f), new Vector3(837.7f, 2.5f, -481.3f),
                    new Vector3(828.6f, 2.5f, -485.5f), new Vector3(763.0f, 2.3f, -509.9f), new Vector3(697.2f, 2.1f, -533.9f),
                    new Vector3(631.4f, 1.9f, -557.8f), new Vector3(565.6f, 1.7f, -581.7f), new Vector3(499.7f, 1.5f, -605.6f),
                    new Vector3(434.0f, 1.3f, -629.6f), new Vector3(368.2f, 1.2f, -653.7f), new Vector3(302.5f, 1.1f, -677.9f),
                    new Vector3(236.8f, 1.0f, -702.2f), new Vector3(171.1f, 1.0f, -726.4f), new Vector3(105.3f, 1.0f, -750.3f),
                    new Vector3(39.4f, 1.1f, -773.8f), new Vector3(-26.5f, 1.2f, -797.6f), new Vector3(-92.1f, 1.2f, -822.1f),
                    new Vector3(-157.6f, 1.3f, -846.9f), new Vector3(-223.0f, 1.4f, -872.0f), new Vector3(-288.6f, 1.6f, -896.5f),
                    new Vector3(-294.4f, 1.6f, -897.9f), new Vector3(-300.4f, 1.6f, -897.7f), new Vector3(-307.6f, 1.6f, -894.5f),
                    new Vector3(-311.8f, 1.6f, -887.9f), new Vector3(-310.8f, 1.6f, -880.0f), new Vector3(-308.0f, 1.6f, -874.7f),
                    new Vector3(-304.6f, 1.6f, -869.8f), new Vector3(-298.4f, 1.6f, -861.9f), new Vector3(-291.8f, 1.7f, -854.5f),
                    new Vector3(-287.5f, 1.7f, -850.2f), new Vector3(-273.9f, 1.7f, -838.4f), new Vector3(-269.0f, 1.7f, -835.0f),
                    new Vector3(-258.3f, 1.7f, -829.6f), new Vector3(-194.4f, 1.8f, -801.0f), new Vector3(-130.0f, 1.9f, -773.5f),
                    new Vector3(-65.6f, 1.9f, -745.9f), new Vector3(-10.7f, 2.0f, -721.6f), new Vector3(-4.1f, 2.0f, -717.3f),
                    new Vector3(-1.3f, 2.0f, -712.0f), new Vector3(0.3f, 2.0f, -704.2f), new Vector3(1.3f, 2.0f, -680.2f),
                    new Vector3(1.7f, 2.0f, -610.2f), new Vector3(1.7f, 1.8f, -540.2f), new Vector3(1.6f, 1.6f, -470.2f),
                    new Vector3(1.4f, 1.3f, -400.1f), new Vector3(1.3f, 1.0f, -330.1f), new Vector3(1.1f, 0.7f, -260.1f),
                    new Vector3(0.9f, 0.4f, -190.1f), new Vector3(0.7f, 0.2f, -120.0f), new Vector3(0.4f, 0.0f, -50.0f)
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
                // Real activation-zone count: THREE.
                DrsZoneThreeNormalized = new Vector2(0.24f, 0.34f),
                TargetLengthMeters = 5412f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 70.0f), new Vector3(1.1f, 0.0f, 120.0f),
                    new Vector3(1.7f, 0.0f, 128.0f), new Vector3(4.7f, 0.0f, 135.1f), new Vector3(11.5f, 0.0f, 139.1f),
                    new Vector3(19.4f, 0.0f, 140.6f), new Vector3(27.3f, 0.0f, 139.8f), new Vector3(33.1f, 0.0f, 138.2f),
                    new Vector3(46.3f, 0.0f, 133.7f), new Vector3(74.5f, 0.0f, 123.4f), new Vector3(82.2f, 0.0f, 121.1f),
                    new Vector3(88.0f, 0.0f, 119.7f), new Vector3(96.0f, 0.0f, 119.5f), new Vector3(103.8f, 0.0f, 121.0f),
                    new Vector3(109.5f, 0.0f, 123.0f), new Vector3(115.0f, 0.0f, 125.5f), new Vector3(120.2f, 0.0f, 128.3f),
                    new Vector3(125.2f, 0.0f, 131.6f), new Vector3(130.0f, 0.0f, 135.2f), new Vector3(137.8f, 0.0f, 141.5f),
                    new Vector3(142.7f, 0.0f, 145.0f), new Vector3(149.5f, 0.0f, 149.2f), new Vector3(156.5f, 0.0f, 153.0f),
                    new Vector3(163.8f, 0.0f, 156.4f), new Vector3(171.2f, 0.0f, 159.3f), new Vector3(176.9f, 0.0f, 161.1f),
                    new Vector3(182.8f, 0.0f, 162.5f), new Vector3(188.7f, 0.0f, 163.3f), new Vector3(196.7f, 0.0f, 163.1f),
                    new Vector3(202.6f, 0.0f, 162.2f), new Vector3(208.5f, 0.0f, 160.9f), new Vector3(218.1f, 0.0f, 158.2f),
                    new Vector3(227.6f, 0.0f, 154.9f), new Vector3(236.8f, 0.0f, 151.1f), new Vector3(244.0f, 0.0f, 147.6f),
                    new Vector3(251.0f, 0.0f, 143.7f), new Vector3(257.7f, 0.0f, 139.4f), new Vector3(265.8f, 0.0f, 133.4f),
                    new Vector3(282.7f, 0.0f, 119.4f), new Vector3(293.0f, 0.0f, 109.9f), new Vector3(298.6f, 0.0f, 104.2f),
                    new Vector3(303.8f, 0.0f, 98.1f), new Vector3(311.2f, 0.0f, 88.6f), new Vector3(320.2f, 0.0f, 75.4f),
                    new Vector3(326.4f, 0.0f, 65.2f), new Vector3(331.1f, 0.0f, 56.3f), new Vector3(335.3f, 0.0f, 47.3f),
                    new Vector3(338.2f, 0.0f, 39.8f), new Vector3(340.6f, 0.0f, 32.2f), new Vector3(343.1f, 0.0f, 22.5f),
                    new Vector3(345.0f, 0.0f, 12.7f), new Vector3(346.3f, 0.0f, 2.8f), new Vector3(348.6f, 0.0f, -33.2f),
                    new Vector3(350.8f, 0.0f, -103.1f), new Vector3(352.5f, 0.0f, -173.1f), new Vector3(354.0f, 0.0f, -243.1f),
                    new Vector3(354.9f, 0.0f, -275.1f), new Vector3(355.6f, 0.0f, -281.1f), new Vector3(356.8f, 0.0f, -286.9f),
                    new Vector3(359.7f, 0.0f, -294.4f), new Vector3(364.0f, 0.0f, -303.4f), new Vector3(367.0f, 0.0f, -308.6f),
                    new Vector3(370.3f, 0.0f, -313.6f), new Vector3(373.9f, 0.0f, -318.4f), new Vector3(377.9f, 0.0f, -322.9f),
                    new Vector3(383.6f, 0.0f, -328.5f), new Vector3(389.7f, 0.0f, -333.7f), new Vector3(394.4f, 0.0f, -337.4f),
                    new Vector3(399.3f, 0.0f, -340.8f), new Vector3(404.5f, 0.0f, -343.9f), new Vector3(409.8f, 0.0f, -346.7f),
                    new Vector3(417.1f, 0.0f, -350.0f), new Vector3(428.3f, 0.0f, -354.2f), new Vector3(441.4f, 0.0f, -359.2f),
                    new Vector3(448.7f, 0.0f, -362.4f), new Vector3(454.0f, 0.0f, -365.2f), new Vector3(459.0f, 0.0f, -368.6f),
                    new Vector3(463.5f, 0.0f, -372.5f), new Vector3(467.7f, 0.0f, -376.8f), new Vector3(471.6f, 0.0f, -381.4f),
                    new Vector3(475.2f, 0.0f, -386.2f), new Vector3(478.4f, 0.0f, -391.2f), new Vector3(481.3f, 0.0f, -396.5f),
                    new Vector3(483.8f, 0.0f, -401.9f), new Vector3(485.7f, 0.0f, -407.6f), new Vector3(487.1f, 0.0f, -413.5f),
                    new Vector3(488.0f, 0.0f, -419.4f), new Vector3(488.5f, 0.0f, -425.4f), new Vector3(488.7f, 0.0f, -431.4f),
                    new Vector3(488.5f, 0.0f, -437.4f), new Vector3(487.6f, 0.0f, -445.3f), new Vector3(484.2f, 0.0f, -465.0f),
                    new Vector3(480.6f, 0.0f, -482.7f), new Vector3(479.7f, 0.0f, -488.6f), new Vector3(479.2f, 0.0f, -494.6f),
                    new Vector3(479.1f, 0.0f, -500.6f), new Vector3(479.5f, 0.0f, -506.6f), new Vector3(480.3f, 0.0f, -512.5f),
                    new Vector3(481.4f, 0.0f, -518.4f), new Vector3(482.9f, 0.0f, -524.2f), new Vector3(484.8f, 0.0f, -529.9f),
                    new Vector3(487.1f, 0.0f, -535.4f), new Vector3(489.9f, 0.0f, -540.7f), new Vector3(493.2f, 0.0f, -545.8f),
                    new Vector3(496.8f, 0.0f, -550.5f), new Vector3(502.1f, 0.0f, -556.5f), new Vector3(510.5f, 0.0f, -565.1f),
                    new Vector3(519.4f, 0.0f, -573.2f), new Vector3(528.7f, 0.0f, -580.7f), new Vector3(536.8f, 0.0f, -586.6f),
                    new Vector3(543.5f, 0.0f, -591.0f), new Vector3(548.8f, 0.0f, -593.9f), new Vector3(554.2f, 0.0f, -596.4f),
                    new Vector3(559.8f, 0.0f, -598.5f), new Vector3(565.6f, 0.0f, -600.1f), new Vector3(571.5f, 0.0f, -601.2f),
                    new Vector3(577.5f, 0.0f, -602.0f), new Vector3(585.4f, 0.0f, -602.5f), new Vector3(591.4f, 0.0f, -602.5f),
                    new Vector3(597.4f, 0.0f, -602.2f), new Vector3(603.4f, 0.0f, -601.5f), new Vector3(609.3f, 0.0f, -600.3f),
                    new Vector3(615.1f, 0.0f, -598.7f), new Vector3(620.7f, 0.0f, -596.7f), new Vector3(626.2f, 0.0f, -594.4f),
                    new Vector3(631.6f, 0.0f, -591.8f), new Vector3(636.8f, 0.0f, -588.7f), new Vector3(641.8f, 0.0f, -585.4f),
                    new Vector3(646.5f, 0.0f, -581.7f), new Vector3(650.9f, 0.0f, -577.5f), new Vector3(654.7f, 0.0f, -572.9f),
                    new Vector3(657.9f, 0.0f, -567.9f), new Vector3(660.8f, 0.0f, -560.4f), new Vector3(661.7f, 0.0f, -552.5f),
                    new Vector3(661.5f, 0.0f, -546.5f), new Vector3(659.9f, 0.0f, -538.7f), new Vector3(656.7f, 0.0f, -531.4f),
                    new Vector3(653.5f, 0.0f, -526.3f), new Vector3(649.9f, 0.0f, -521.6f), new Vector3(644.6f, 0.0f, -515.5f),
                    new Vector3(637.8f, 0.0f, -508.2f), new Vector3(632.6f, 0.0f, -502.1f), new Vector3(628.0f, 0.0f, -495.6f),
                    new Vector3(624.7f, 0.0f, -490.5f), new Vector3(621.8f, 0.0f, -485.3f), new Vector3(617.5f, 0.0f, -476.3f),
                    new Vector3(609.9f, 0.0f, -457.8f), new Vector3(585.3f, 0.0f, -392.2f), new Vector3(570.5f, 0.0f, -355.1f),
                    new Vector3(559.4f, 0.0f, -331.5f), new Vector3(547.0f, 0.0f, -308.7f), new Vector3(532.3f, 0.0f, -284.9f),
                    new Vector3(493.1f, 0.0f, -226.9f), new Vector3(453.5f, 0.0f, -169.2f), new Vector3(422.8f, 0.0f, -122.3f),
                    new Vector3(410.9f, 0.0f, -101.5f), new Vector3(406.5f, 0.0f, -92.5f), new Vector3(403.4f, 0.0f, -85.1f),
                    new Vector3(401.4f, 0.0f, -79.4f), new Vector3(395.5f, 0.0f, -56.2f), new Vector3(379.9f, 0.0f, 12.1f),
                    new Vector3(363.4f, 0.0f, 80.1f), new Vector3(356.0f, 0.0f, 105.0f), new Vector3(348.0f, 0.0f, 127.6f),
                    new Vector3(338.8f, 0.0f, 149.8f), new Vector3(329.2f, 0.0f, 169.6f), new Vector3(317.6f, 0.0f, 190.6f),
                    new Vector3(306.9f, 0.0f, 207.5f), new Vector3(295.4f, 0.0f, 223.8f), new Vector3(281.6f, 0.0f, 241.0f),
                    new Vector3(265.6f, 0.0f, 258.9f), new Vector3(250.0f, 0.0f, 274.4f), new Vector3(235.0f, 0.0f, 287.6f),
                    new Vector3(180.0f, 0.0f, 330.9f), new Vector3(124.6f, 0.0f, 373.8f), new Vector3(69.0f, 0.0f, 416.3f),
                    new Vector3(12.8f, 0.0f, 458.0f), new Vector3(-8.8f, 0.0f, 472.6f), new Vector3(-69.0f, 0.0f, 508.2f),
                    new Vector3(-130.5f, 0.0f, 541.6f), new Vector3(-155.8f, 0.0f, 553.6f), new Vector3(-168.8f, 0.0f, 558.9f),
                    new Vector3(-174.4f, 0.0f, 560.9f), new Vector3(-182.2f, 0.0f, 562.7f), new Vector3(-190.2f, 0.0f, 562.2f),
                    new Vector3(-196.6f, 0.0f, 557.7f), new Vector3(-200.4f, 0.0f, 550.7f), new Vector3(-201.9f, 0.0f, 542.8f),
                    new Vector3(-202.3f, 0.0f, 536.8f), new Vector3(-202.3f, 0.0f, 518.8f), new Vector3(-202.7f, 0.0f, 504.8f),
                    new Vector3(-203.2f, 0.0f, 498.9f), new Vector3(-204.5f, 0.0f, 493.0f), new Vector3(-206.5f, 0.0f, 487.4f),
                    new Vector3(-209.2f, 0.0f, 482.0f), new Vector3(-212.6f, 0.0f, 477.1f), new Vector3(-216.5f, 0.0f, 472.5f),
                    new Vector3(-220.8f, 0.0f, 468.4f), new Vector3(-225.6f, 0.0f, 464.8f), new Vector3(-230.8f, 0.0f, 461.7f),
                    new Vector3(-236.2f, 0.0f, 459.1f), new Vector3(-241.8f, 0.0f, 457.0f), new Vector3(-247.6f, 0.0f, 455.4f),
                    new Vector3(-255.5f, 0.0f, 454.5f), new Vector3(-263.4f, 0.0f, 455.5f), new Vector3(-269.1f, 0.0f, 457.3f),
                    new Vector3(-274.5f, 0.0f, 459.9f), new Vector3(-280.9f, 0.0f, 464.7f), new Vector3(-284.7f, 0.0f, 469.3f),
                    new Vector3(-288.2f, 0.0f, 474.2f), new Vector3(-293.4f, 0.0f, 482.7f), new Vector3(-304.4f, 0.0f, 501.8f),
                    new Vector3(-309.9f, 0.0f, 510.2f), new Vector3(-313.5f, 0.0f, 515.0f), new Vector3(-317.4f, 0.0f, 519.5f),
                    new Vector3(-321.7f, 0.0f, 523.7f), new Vector3(-328.4f, 0.0f, 528.0f), new Vector3(-334.0f, 0.0f, 530.1f),
                    new Vector3(-341.7f, 0.0f, 532.2f), new Vector3(-347.6f, 0.0f, 533.3f), new Vector3(-353.6f, 0.0f, 533.9f),
                    new Vector3(-363.6f, 0.0f, 534.2f), new Vector3(-379.6f, 0.0f, 533.7f), new Vector3(-391.5f, 0.0f, 532.8f),
                    new Vector3(-397.5f, 0.0f, 532.0f), new Vector3(-405.2f, 0.0f, 529.9f), new Vector3(-408.8f, 0.0f, 523.2f),
                    new Vector3(-407.9f, 0.0f, 515.2f), new Vector3(-407.8f, 0.0f, 509.2f), new Vector3(-409.9f, 0.0f, 501.6f),
                    new Vector3(-415.6f, 0.0f, 496.1f), new Vector3(-422.9f, 0.0f, 492.8f), new Vector3(-434.2f, 0.0f, 489.0f),
                    new Vector3(-468.9f, 0.0f, 479.1f), new Vector3(-480.3f, 0.0f, 475.4f), new Vector3(-487.5f, 0.0f, 472.0f),
                    new Vector3(-491.9f, 0.0f, 465.5f), new Vector3(-491.5f, 0.0f, 457.6f), new Vector3(-489.5f, 0.0f, 452.0f),
                    new Vector3(-486.3f, 0.0f, 444.6f), new Vector3(-480.0f, 0.0f, 432.2f), new Vector3(-445.4f, 0.0f, 371.3f),
                    new Vector3(-410.4f, 0.0f, 310.6f), new Vector3(-375.3f, 0.0f, 250.1f), new Vector3(-340.1f, 0.0f, 189.5f),
                    new Vector3(-304.9f, 0.0f, 129.0f), new Vector3(-269.7f, 0.0f, 68.5f), new Vector3(-234.6f, 0.0f, 8.0f),
                    new Vector3(-199.5f, 0.0f, -52.6f), new Vector3(-164.4f, 0.0f, -113.2f), new Vector3(-129.4f, 0.0f, -173.9f),
                    new Vector3(-94.5f, 0.0f, -234.5f), new Vector3(-59.7f, 0.0f, -295.3f), new Vector3(-24.9f, 0.0f, -356.0f),
                    new Vector3(9.8f, 0.0f, -416.8f), new Vector3(44.5f, 0.0f, -477.6f), new Vector3(79.2f, 0.0f, -538.4f),
                    new Vector3(114.2f, 0.0f, -599.1f), new Vector3(149.9f, 0.0f, -659.3f), new Vector3(154.3f, 0.0f, -666.0f),
                    new Vector3(160.4f, 0.0f, -671.0f), new Vector3(168.2f, 0.0f, -671.3f), new Vector3(175.1f, 0.0f, -667.3f),
                    new Vector3(180.4f, 0.0f, -661.4f), new Vector3(183.9f, 0.0f, -654.2f), new Vector3(185.6f, 0.0f, -648.5f),
                    new Vector3(186.7f, 0.0f, -642.6f), new Vector3(189.3f, 0.0f, -622.7f), new Vector3(190.9f, 0.0f, -602.8f),
                    new Vector3(191.1f, 0.0f, -588.8f), new Vector3(190.6f, 0.0f, -576.8f), new Vector3(189.7f, 0.0f, -566.9f),
                    new Vector3(188.5f, 0.0f, -558.9f), new Vector3(186.9f, 0.0f, -551.1f), new Vector3(185.2f, 0.0f, -545.4f),
                    new Vector3(183.2f, 0.0f, -539.7f), new Vector3(180.8f, 0.0f, -534.2f), new Vector3(177.9f, 0.0f, -528.9f),
                    new Vector3(174.6f, 0.0f, -523.9f), new Vector3(170.9f, 0.0f, -519.2f), new Vector3(166.8f, 0.0f, -514.8f),
                    new Vector3(162.4f, 0.0f, -510.7f), new Vector3(156.3f, 0.0f, -505.6f), new Vector3(145.0f, 0.0f, -497.3f),
                    new Vector3(100.0f, 0.0f, -467.5f), new Vector3(83.7f, 0.0f, -456.0f), new Vector3(74.2f, 0.0f, -448.5f),
                    new Vector3(66.8f, 0.0f, -441.9f), new Vector3(58.3f, 0.0f, -433.3f), new Vector3(49.1f, 0.0f, -422.8f),
                    new Vector3(40.5f, 0.0f, -411.8f), new Vector3(33.6f, 0.0f, -402.0f), new Vector3(28.3f, 0.0f, -393.5f),
                    new Vector3(22.5f, 0.0f, -382.9f), new Vector3(18.2f, 0.0f, -373.9f), new Vector3(14.4f, 0.0f, -364.7f),
                    new Vector3(11.1f, 0.0f, -355.2f), new Vector3(8.4f, 0.0f, -345.6f), new Vector3(6.3f, 0.0f, -335.8f),
                    new Vector3(4.8f, 0.0f, -325.9f), new Vector3(3.2f, 0.0f, -306.0f), new Vector3(1.3f, 0.0f, -236.0f),
                    new Vector3(0.8f, 0.0f, -166.0f), new Vector3(0.5f, 0.0f, -96.0f), new Vector3(0.1f, 0.0f, -26.0f)
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
                // Real activation-zone count: THREE.
                DrsZoneThreeNormalized = new Vector2(0.24f, 0.33f),
                TargetLengthMeters = 4361f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 70.0f), new Vector3(0.3f, 0.2f, 140.0f),
                    new Vector3(2.2f, 0.3f, 184.0f), new Vector3(4.3f, 0.3f, 205.9f), new Vector3(6.9f, 0.4f, 223.7f),
                    new Vector3(9.4f, 0.4f, 235.4f), new Vector3(16.7f, 0.5f, 262.4f), new Vector3(38.3f, 0.8f, 329.0f),
                    new Vector3(52.2f, 1.0f, 372.9f), new Vector3(54.2f, 1.0f, 380.6f), new Vector3(54.9f, 1.0f, 388.5f),
                    new Vector3(52.9f, 1.0f, 396.2f), new Vector3(48.3f, 1.1f, 402.7f), new Vector3(43.9f, 1.1f, 406.8f),
                    new Vector3(36.1f, 1.1f, 413.1f), new Vector3(15.3f, 1.2f, 428.7f), new Vector3(10.8f, 1.3f, 432.6f),
                    new Vector3(6.3f, 1.3f, 439.2f), new Vector3(5.2f, 1.3f, 447.0f), new Vector3(6.5f, 1.3f, 454.9f),
                    new Vector3(9.9f, 1.4f, 462.1f), new Vector3(14.7f, 1.4f, 468.5f), new Vector3(21.0f, 1.4f, 473.4f),
                    new Vector3(28.3f, 1.5f, 476.7f), new Vector3(36.0f, 1.5f, 478.6f), new Vector3(44.0f, 1.5f, 478.9f),
                    new Vector3(51.9f, 1.5f, 477.8f), new Vector3(57.7f, 1.6f, 476.2f), new Vector3(63.4f, 1.6f, 474.3f),
                    new Vector3(68.9f, 1.6f, 472.0f), new Vector3(93.9f, 1.7f, 459.4f), new Vector3(134.2f, 1.8f, 437.2f),
                    new Vector3(144.3f, 1.8f, 430.7f), new Vector3(154.0f, 1.9f, 423.7f), new Vector3(164.8f, 1.9f, 414.8f),
                    new Vector3(180.9f, 1.9f, 399.8f), new Vector3(190.7f, 1.9f, 389.8f), new Vector3(204.9f, 2.0f, 373.0f),
                    new Vector3(247.4f, 2.0f, 317.3f), new Vector3(275.0f, 2.0f, 278.1f), new Vector3(281.4f, 2.0f, 267.9f),
                    new Vector3(284.9f, 2.1f, 260.7f), new Vector3(286.7f, 2.1f, 253.0f), new Vector3(286.3f, 2.1f, 245.0f),
                    new Vector3(284.0f, 2.1f, 237.3f), new Vector3(281.4f, 2.1f, 232.0f), new Vector3(274.6f, 2.1f, 219.7f),
                    new Vector3(271.9f, 2.1f, 214.4f), new Vector3(269.6f, 2.2f, 208.8f), new Vector3(267.8f, 2.2f, 201.0f),
                    new Vector3(267.9f, 2.2f, 193.1f), new Vector3(270.3f, 2.2f, 185.5f), new Vector3(301.7f, 2.4f, 122.9f),
                    new Vector3(325.4f, 2.6f, 78.9f), new Vector3(328.5f, 2.6f, 73.8f), new Vector3(333.4f, 2.6f, 67.4f),
                    new Vector3(349.3f, 2.7f, 49.5f), new Vector3(359.8f, 2.8f, 37.4f), new Vector3(363.5f, 2.8f, 32.6f),
                    new Vector3(366.8f, 2.8f, 27.6f), new Vector3(369.8f, 2.9f, 22.5f), new Vector3(373.4f, 2.9f, 15.3f),
                    new Vector3(375.8f, 2.9f, 9.8f), new Vector3(377.9f, 2.9f, 4.2f), new Vector3(379.7f, 2.9f, -1.5f),
                    new Vector3(381.1f, 3.0f, -7.4f), new Vector3(382.4f, 3.0f, -15.3f), new Vector3(383.8f, 3.0f, -27.2f),
                    new Vector3(384.5f, 3.1f, -39.2f), new Vector3(384.5f, 3.1f, -49.2f), new Vector3(384.3f, 3.2f, -55.1f),
                    new Vector3(383.6f, 3.2f, -61.1f), new Vector3(372.0f, 3.4f, -118.0f), new Vector3(357.1f, 3.6f, -186.3f),
                    new Vector3(351.2f, 3.7f, -213.7f), new Vector3(350.9f, 3.7f, -221.7f), new Vector3(353.4f, 3.7f, -229.2f),
                    new Vector3(358.1f, 3.8f, -235.6f), new Vector3(364.6f, 3.8f, -240.4f), new Vector3(371.9f, 3.8f, -243.6f),
                    new Vector3(379.7f, 3.8f, -245.1f), new Vector3(387.7f, 3.8f, -245.4f), new Vector3(399.7f, 3.9f, -245.4f),
                    new Vector3(407.6f, 3.9f, -246.2f), new Vector3(415.2f, 3.9f, -248.9f), new Vector3(422.1f, 3.9f, -252.8f),
                    new Vector3(428.4f, 3.9f, -257.7f), new Vector3(432.6f, 3.9f, -262.0f), new Vector3(436.5f, 3.9f, -266.6f),
                    new Vector3(439.9f, 4.0f, -271.5f), new Vector3(442.9f, 4.0f, -276.7f), new Vector3(445.6f, 4.0f, -282.1f),
                    new Vector3(447.9f, 4.0f, -287.7f), new Vector3(450.1f, 4.0f, -295.3f), new Vector3(451.5f, 4.0f, -303.2f),
                    new Vector3(452.9f, 4.0f, -315.1f), new Vector3(453.8f, 4.0f, -329.1f), new Vector3(454.4f, 4.0f, -365.1f),
                    new Vector3(454.0f, 4.0f, -375.1f), new Vector3(453.2f, 4.0f, -383.0f), new Vector3(444.9f, 3.9f, -440.4f),
                    new Vector3(434.7f, 3.8f, -509.7f), new Vector3(428.5f, 3.7f, -541.1f), new Vector3(411.9f, 3.4f, -609.1f),
                    new Vector3(396.0f, 3.2f, -662.8f), new Vector3(379.2f, 3.1f, -709.9f), new Vector3(371.6f, 3.0f, -728.4f),
                    new Vector3(341.5f, 2.7f, -791.6f), new Vector3(310.5f, 2.5f, -854.3f), new Vector3(295.7f, 2.4f, -882.7f),
                    new Vector3(291.0f, 2.3f, -889.1f), new Vector3(284.5f, 2.3f, -893.7f), new Vector3(276.8f, 2.3f, -895.6f),
                    new Vector3(268.8f, 2.3f, -895.6f), new Vector3(260.8f, 2.3f, -894.7f), new Vector3(254.8f, 2.2f, -894.3f),
                    new Vector3(246.9f, 2.2f, -894.8f), new Vector3(239.1f, 2.2f, -896.5f), new Vector3(231.6f, 2.2f, -899.4f),
                    new Vector3(226.4f, 2.2f, -902.4f), new Vector3(221.5f, 2.2f, -905.8f), new Vector3(216.8f, 2.1f, -909.6f),
                    new Vector3(212.5f, 2.1f, -913.7f), new Vector3(208.4f, 2.1f, -918.1f), new Vector3(204.6f, 2.1f, -922.8f),
                    new Vector3(201.1f, 2.1f, -927.7f), new Vector3(196.9f, 2.1f, -934.5f), new Vector3(165.4f, 2.0f, -997.0f),
                    new Vector3(135.1f, 2.0f, -1060.1f), new Vector3(106.7f, 1.9f, -1124.0f), new Vector3(89.5f, 1.9f, -1168.9f),
                    new Vector3(76.9f, 1.8f, -1208.9f), new Vector3(59.2f, 1.7f, -1276.6f), new Vector3(56.3f, 1.7f, -1290.3f),
                    new Vector3(54.3f, 1.7f, -1304.2f), new Vector3(53.6f, 1.7f, -1312.2f), new Vector3(52.1f, 1.5f, -1382.2f),
                    new Vector3(51.1f, 1.4f, -1452.1f), new Vector3(50.7f, 1.4f, -1464.1f), new Vector3(48.8f, 1.4f, -1471.9f),
                    new Vector3(43.5f, 1.3f, -1477.8f), new Vector3(36.0f, 1.3f, -1480.1f), new Vector3(28.1f, 1.3f, -1479.4f),
                    new Vector3(21.1f, 1.3f, -1475.7f), new Vector3(16.8f, 1.3f, -1469.0f), new Vector3(15.9f, 1.3f, -1461.1f),
                    new Vector3(23.4f, 1.2f, -1391.5f), new Vector3(25.2f, 1.1f, -1373.6f), new Vector3(25.2f, 1.1f, -1367.6f),
                    new Vector3(24.8f, 1.1f, -1361.7f), new Vector3(24.0f, 1.1f, -1355.7f), new Vector3(20.8f, 1.1f, -1340.0f),
                    new Vector3(6.1f, 1.0f, -1279.8f), new Vector3(-6.0f, 1.0f, -1210.9f), new Vector3(-17.2f, 1.0f, -1141.7f),
                    new Vector3(-28.1f, 1.1f, -1072.6f), new Vector3(-38.5f, 1.2f, -1003.4f), new Vector3(-39.2f, 1.2f, -993.4f),
                    new Vector3(-39.8f, 1.3f, -923.4f), new Vector3(-39.7f, 1.4f, -853.4f), new Vector3(-39.5f, 1.6f, -783.4f),
                    new Vector3(-39.1f, 1.7f, -713.4f), new Vector3(-38.7f, 1.8f, -643.4f), new Vector3(-38.3f, 1.9f, -573.4f),
                    new Vector3(-37.8f, 2.0f, -503.4f), new Vector3(-37.4f, 2.0f, -433.4f), new Vector3(-37.0f, 1.9f, -363.4f),
                    new Vector3(-36.3f, 1.7f, -303.4f), new Vector3(-35.2f, 1.6f, -295.5f), new Vector3(-31.1f, 1.6f, -288.7f),
                    new Vector3(-24.2f, 1.5f, -284.8f), new Vector3(-16.4f, 1.5f, -283.0f), new Vector3(-9.3f, 1.4f, -279.4f),
                    new Vector3(-4.0f, 1.4f, -273.5f), new Vector3(-1.4f, 1.3f, -266.0f), new Vector3(-0.9f, 1.3f, -258.0f),
                    new Vector3(-0.7f, 0.8f, -188.0f), new Vector3(-0.6f, 0.4f, -118.0f), new Vector3(-0.1f, 0.1f, -48.0f)
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.5f, 70.0f), new Vector3(-0.1f, 1.7f, 140.0f),
                    new Vector3(-0.4f, 3.4f, 210.0f), new Vector3(-0.6f, 5.2f, 280.0f), new Vector3(-0.4f, 6.8f, 350.0f),
                    new Vector3(-0.5f, 7.8f, 420.0f), new Vector3(-0.8f, 8.0f, 490.0f), new Vector3(-1.0f, 8.5f, 560.0f),
                    new Vector3(-0.5f, 8.6f, 578.0f), new Vector3(0.0f, 8.7f, 584.0f), new Vector3(1.1f, 8.8f, 589.9f),
                    new Vector3(2.6f, 8.8f, 595.7f), new Vector3(5.9f, 8.9f, 603.0f), new Vector3(11.4f, 9.0f, 608.7f),
                    new Vector3(16.1f, 9.1f, 612.4f), new Vector3(21.3f, 9.2f, 615.5f), new Vector3(28.6f, 9.3f, 618.8f),
                    new Vector3(36.4f, 9.4f, 620.5f), new Vector3(42.4f, 9.5f, 620.7f), new Vector3(70.4f, 9.9f, 620.8f),
                    new Vector3(76.3f, 10.0f, 621.2f), new Vector3(84.1f, 10.1f, 623.1f), new Vector3(89.8f, 10.2f, 625.0f),
                    new Vector3(95.3f, 10.3f, 627.4f), new Vector3(102.2f, 10.4f, 631.5f), new Vector3(106.9f, 10.5f, 635.2f),
                    new Vector3(111.3f, 10.6f, 639.3f), new Vector3(115.4f, 10.7f, 643.6f), new Vector3(119.8f, 10.8f, 650.3f),
                    new Vector3(125.2f, 11.0f, 663.2f), new Vector3(150.7f, 12.1f, 728.4f), new Vector3(156.2f, 12.3f, 741.3f),
                    new Vector3(160.2f, 12.5f, 748.2f), new Vector3(163.8f, 12.5f, 753.0f), new Vector3(173.8f, 12.8f, 765.5f),
                    new Vector3(177.9f, 12.8f, 769.8f), new Vector3(182.4f, 12.9f, 773.8f), new Vector3(187.1f, 13.0f, 777.5f),
                    new Vector3(192.1f, 13.1f, 780.9f), new Vector3(202.4f, 13.2f, 786.9f), new Vector3(211.4f, 13.3f, 791.5f),
                    new Vector3(216.9f, 13.4f, 793.9f), new Vector3(222.5f, 13.4f, 795.9f), new Vector3(228.3f, 13.5f, 797.4f),
                    new Vector3(234.2f, 13.6f, 798.5f), new Vector3(242.1f, 13.6f, 799.6f), new Vector3(250.1f, 13.7f, 800.2f),
                    new Vector3(256.1f, 13.7f, 800.1f), new Vector3(264.1f, 13.8f, 799.3f), new Vector3(275.9f, 13.9f, 797.4f),
                    new Vector3(283.6f, 13.9f, 795.2f), new Vector3(298.6f, 14.0f, 789.7f), new Vector3(307.8f, 14.0f, 785.8f),
                    new Vector3(313.2f, 14.0f, 783.0f), new Vector3(318.3f, 14.0f, 780.0f), new Vector3(325.0f, 14.0f, 775.5f),
                    new Vector3(331.3f, 14.0f, 770.7f), new Vector3(337.4f, 14.0f, 765.5f), new Vector3(341.8f, 14.0f, 761.4f),
                    new Vector3(345.9f, 14.0f, 757.1f), new Vector3(358.3f, 14.0f, 741.4f), new Vector3(363.0f, 14.0f, 734.8f),
                    new Vector3(368.3f, 13.9f, 726.4f), new Vector3(373.0f, 13.9f, 717.6f), new Vector3(377.2f, 13.9f, 708.5f),
                    new Vector3(381.6f, 13.9f, 697.3f), new Vector3(388.4f, 13.8f, 676.4f), new Vector3(393.1f, 13.8f, 659.0f),
                    new Vector3(396.0f, 13.7f, 645.3f), new Vector3(397.5f, 13.7f, 635.4f), new Vector3(398.5f, 13.6f, 625.5f),
                    new Vector3(398.8f, 13.6f, 617.5f), new Vector3(398.8f, 13.3f, 547.5f), new Vector3(398.8f, 13.0f, 477.5f),
                    new Vector3(398.6f, 12.7f, 407.5f), new Vector3(397.9f, 12.5f, 371.5f), new Vector3(396.4f, 12.5f, 363.7f),
                    new Vector3(393.1f, 12.5f, 356.4f), new Vector3(388.5f, 12.4f, 349.9f), new Vector3(384.5f, 12.4f, 345.4f),
                    new Vector3(380.2f, 12.4f, 341.2f), new Vector3(373.9f, 12.4f, 336.3f), new Vector3(366.7f, 12.3f, 332.8f),
                    new Vector3(361.1f, 12.3f, 330.8f), new Vector3(353.2f, 12.3f, 329.3f), new Vector3(347.2f, 12.3f, 328.9f),
                    new Vector3(341.2f, 12.3f, 329.0f), new Vector3(335.3f, 12.2f, 329.5f), new Vector3(329.3f, 12.2f, 330.3f),
                    new Vector3(323.4f, 12.2f, 331.4f), new Vector3(315.8f, 12.2f, 333.8f), new Vector3(310.4f, 12.2f, 336.4f),
                    new Vector3(303.5f, 12.1f, 340.4f), new Vector3(298.4f, 12.1f, 343.7f), new Vector3(293.6f, 12.1f, 347.3f),
                    new Vector3(287.7f, 12.1f, 352.7f), new Vector3(283.8f, 12.1f, 357.2f), new Vector3(278.9f, 12.1f, 363.5f),
                    new Vector3(271.1f, 12.0f, 375.1f), new Vector3(266.9f, 12.0f, 382.0f), new Vector3(264.2f, 12.0f, 387.3f),
                    new Vector3(261.9f, 12.0f, 392.8f), new Vector3(259.9f, 12.0f, 398.5f), new Vector3(258.2f, 12.0f, 404.3f),
                    new Vector3(257.0f, 12.0f, 410.1f), new Vector3(255.4f, 12.0f, 422.0f), new Vector3(254.2f, 12.0f, 436.0f),
                    new Vector3(253.8f, 11.7f, 506.0f), new Vector3(254.1f, 11.0f, 576.0f), new Vector3(253.5f, 10.5f, 614.0f),
                    new Vector3(252.3f, 10.3f, 621.8f), new Vector3(248.7f, 10.2f, 629.0f), new Vector3(243.6f, 10.1f, 635.1f),
                    new Vector3(237.3f, 10.0f, 640.1f), new Vector3(230.1f, 9.8f, 643.3f), new Vector3(222.3f, 9.7f, 645.0f),
                    new Vector3(214.3f, 9.6f, 644.6f), new Vector3(206.6f, 9.4f, 642.5f), new Vector3(199.6f, 9.3f, 638.7f),
                    new Vector3(193.8f, 9.1f, 633.2f), new Vector3(188.9f, 9.0f, 626.8f), new Vector3(150.2f, 7.7f, 568.5f),
                    new Vector3(112.1f, 6.5f, 509.8f), new Vector3(103.8f, 6.2f, 496.1f), new Vector3(101.1f, 6.1f, 490.8f),
                    new Vector3(96.3f, 5.9f, 479.8f), new Vector3(91.4f, 5.7f, 466.7f), new Vector3(87.3f, 5.5f, 453.3f),
                    new Vector3(84.0f, 5.3f, 439.7f), new Vector3(82.1f, 5.1f, 429.9f), new Vector3(81.4f, 5.1f, 423.9f),
                    new Vector3(80.6f, 4.9f, 407.9f), new Vector3(79.9f, 4.2f, 337.9f), new Vector3(80.3f, 4.1f, 311.9f),
                    new Vector3(80.9f, 4.1f, 306.0f), new Vector3(82.0f, 4.0f, 300.1f), new Vector3(84.7f, 4.0f, 292.6f),
                    new Vector3(89.6f, 4.0f, 286.3f), new Vector3(95.6f, 4.0f, 281.0f), new Vector3(102.5f, 4.0f, 277.0f),
                    new Vector3(110.0f, 4.0f, 274.3f), new Vector3(117.9f, 4.0f, 273.0f), new Vector3(123.9f, 4.0f, 272.7f),
                    new Vector3(139.9f, 3.9f, 272.8f), new Vector3(149.9f, 3.9f, 272.6f), new Vector3(155.9f, 3.9f, 272.2f),
                    new Vector3(163.8f, 3.8f, 271.1f), new Vector3(169.7f, 3.8f, 269.9f), new Vector3(175.4f, 3.7f, 268.1f),
                    new Vector3(181.0f, 3.7f, 266.0f), new Vector3(188.3f, 3.7f, 262.7f), new Vector3(195.3f, 3.6f, 258.8f),
                    new Vector3(252.8f, 3.0f, 218.8f), new Vector3(310.1f, 2.1f, 178.7f), new Vector3(347.4f, 1.5f, 151.7f),
                    new Vector3(352.0f, 1.4f, 147.9f), new Vector3(357.6f, 1.3f, 142.2f), new Vector3(361.3f, 1.2f, 137.5f),
                    new Vector3(364.6f, 1.2f, 132.5f), new Vector3(368.7f, 1.0f, 125.6f), new Vector3(371.4f, 1.0f, 120.2f),
                    new Vector3(375.2f, 0.8f, 111.0f), new Vector3(377.5f, 0.7f, 103.3f), new Vector3(378.5f, 0.6f, 95.4f),
                    new Vector3(378.9f, 0.4f, 83.4f), new Vector3(378.7f, 0.4f, 77.4f), new Vector3(377.9f, 0.3f, 71.5f),
                    new Vector3(376.5f, 0.2f, 63.6f), new Vector3(375.1f, 0.1f, 57.8f), new Vector3(372.4f, 0.0f, 50.2f),
                    new Vector3(364.7f, -0.2f, 36.2f), new Vector3(329.6f, -1.0f, -24.3f), new Vector3(294.0f, -1.7f, -84.7f),
                    new Vector3(258.4f, -2.0f, -144.9f), new Vector3(222.7f, -1.9f, -205.1f), new Vector3(187.0f, -1.5f, -265.4f),
                    new Vector3(151.4f, -0.8f, -325.6f), new Vector3(118.0f, 0.0f, -382.5f), new Vector3(114.7f, 0.1f, -389.8f),
                    new Vector3(112.8f, 0.2f, -397.6f), new Vector3(112.7f, 0.3f, -405.5f), new Vector3(114.9f, 0.4f, -413.2f),
                    new Vector3(119.3f, 0.5f, -419.8f), new Vector3(125.3f, 0.6f, -425.2f), new Vector3(132.2f, 0.7f, -429.2f),
                    new Vector3(139.8f, 0.8f, -431.7f), new Vector3(145.7f, 0.9f, -432.8f), new Vector3(151.6f, 1.0f, -433.4f),
                    new Vector3(157.6f, 1.1f, -433.6f), new Vector3(163.6f, 1.2f, -433.1f), new Vector3(171.5f, 1.3f, -432.0f),
                    new Vector3(177.4f, 1.4f, -430.8f), new Vector3(185.0f, 1.5f, -428.4f), new Vector3(192.4f, 1.6f, -425.4f),
                    new Vector3(201.5f, 1.7f, -421.1f), new Vector3(208.4f, 1.8f, -417.2f), new Vector3(215.2f, 1.9f, -412.9f),
                    new Vector3(220.0f, 2.0f, -409.3f), new Vector3(224.5f, 2.1f, -405.3f), new Vector3(228.6f, 2.2f, -401.0f),
                    new Vector3(232.4f, 2.2f, -396.3f), new Vector3(236.9f, 2.3f, -389.7f), new Vector3(244.1f, 2.5f, -377.7f),
                    new Vector3(247.9f, 2.6f, -370.6f), new Vector3(250.2f, 2.7f, -365.1f), new Vector3(252.9f, 2.8f, -357.6f),
                    new Vector3(258.4f, 3.0f, -340.4f), new Vector3(262.8f, 3.1f, -329.3f), new Vector3(266.1f, 3.2f, -322.0f),
                    new Vector3(269.1f, 3.2f, -316.8f), new Vector3(274.0f, 3.3f, -310.5f), new Vector3(280.0f, 3.4f, -305.2f),
                    new Vector3(284.8f, 3.4f, -301.6f), new Vector3(289.9f, 3.5f, -298.5f), new Vector3(297.3f, 3.5f, -295.3f),
                    new Vector3(305.1f, 3.6f, -293.7f), new Vector3(311.0f, 3.6f, -293.1f), new Vector3(317.0f, 3.7f, -293.1f),
                    new Vector3(323.0f, 3.7f, -293.5f), new Vector3(330.8f, 3.8f, -295.2f), new Vector3(338.2f, 3.8f, -298.2f),
                    new Vector3(343.5f, 3.8f, -301.1f), new Vector3(348.6f, 3.9f, -304.2f), new Vector3(354.8f, 3.9f, -309.3f),
                    new Vector3(360.1f, 3.9f, -315.3f), new Vector3(363.8f, 4.0f, -320.0f), new Vector3(367.5f, 4.0f, -327.1f),
                    new Vector3(369.6f, 4.0f, -332.7f), new Vector3(371.3f, 4.0f, -338.4f), new Vector3(372.5f, 4.0f, -344.3f),
                    new Vector3(372.9f, 4.0f, -352.3f), new Vector3(371.9f, 4.0f, -360.2f), new Vector3(370.4f, 4.0f, -366.0f),
                    new Vector3(348.7f, 4.1f, -432.6f), new Vector3(326.7f, 4.4f, -499.1f), new Vector3(324.1f, 4.4f, -506.6f),
                    new Vector3(321.7f, 4.4f, -512.1f), new Vector3(319.0f, 4.5f, -517.5f), new Vector3(315.9f, 4.5f, -522.6f),
                    new Vector3(312.3f, 4.5f, -527.4f), new Vector3(308.2f, 4.5f, -531.8f), new Vector3(303.9f, 4.6f, -536.0f),
                    new Vector3(297.7f, 4.6f, -541.1f), new Vector3(291.3f, 4.7f, -545.8f), new Vector3(286.2f, 4.7f, -549.0f),
                    new Vector3(280.9f, 4.7f, -551.8f), new Vector3(275.3f, 4.7f, -554.1f), new Vector3(269.6f, 4.8f, -555.9f),
                    new Vector3(263.8f, 4.8f, -557.2f), new Vector3(257.8f, 4.8f, -557.8f), new Vector3(247.8f, 4.9f, -558.2f),
                    new Vector3(177.8f, 5.3f, -557.1f), new Vector3(161.8f, 5.3f, -556.8f), new Vector3(91.8f, 5.7f, -556.9f),
                    new Vector3(79.8f, 5.7f, -556.5f), new Vector3(73.9f, 5.7f, -555.9f), new Vector3(68.0f, 5.8f, -554.7f),
                    new Vector3(62.2f, 5.8f, -553.0f), new Vector3(56.6f, 5.8f, -550.9f), new Vector3(51.1f, 5.8f, -548.5f),
                    new Vector3(45.9f, 5.8f, -545.6f), new Vector3(40.8f, 5.9f, -542.3f), new Vector3(36.1f, 5.9f, -538.7f),
                    new Vector3(30.0f, 5.9f, -533.4f), new Vector3(25.7f, 5.9f, -529.2f), new Vector3(21.7f, 5.9f, -524.8f),
                    new Vector3(18.1f, 5.9f, -520.0f), new Vector3(13.6f, 6.0f, -513.4f), new Vector3(9.6f, 6.0f, -506.5f),
                    new Vector3(6.9f, 6.0f, -501.1f), new Vector3(4.0f, 6.0f, -493.7f), new Vector3(2.5f, 6.0f, -487.9f),
                    new Vector3(1.4f, 6.0f, -482.0f), new Vector3(0.7f, 6.0f, -476.0f), new Vector3(0.2f, 6.0f, -468.0f),
                    new Vector3(-0.6f, 5.7f, -398.0f), new Vector3(-0.6f, 4.7f, -328.0f), new Vector3(-0.5f, 3.5f, -258.0f),
                    new Vector3(-0.3f, 2.1f, -188.0f), new Vector3(-0.1f, 1.0f, -118.0f), new Vector3(0.0f, 0.2f, -48.0f)
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
                // Real activation-zone count: THREE.
                DrsZoneThreeNormalized = new Vector2(0.66f, 0.76f),
                TargetLengthMeters = 4318f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 4.0f, 70.0f), new Vector3(-0.2f, 12.4f, 140.0f),
                    new Vector3(-0.3f, 20.0f, 210.0f), new Vector3(0.5f, 22.2f, 272.0f), new Vector3(1.6f, 22.4f, 279.9f),
                    new Vector3(6.0f, 22.8f, 286.5f), new Vector3(13.0f, 23.2f, 290.3f), new Vector3(20.6f, 23.8f, 292.7f),
                    new Vector3(34.2f, 24.9f, 296.0f), new Vector3(45.8f, 26.1f, 299.3f), new Vector3(74.1f, 29.6f, 309.2f),
                    new Vector3(139.4f, 40.0f, 334.3f), new Vector3(205.2f, 50.4f, 358.3f), new Vector3(271.7f, 57.1f, 380.0f),
                    new Vector3(339.0f, 58.2f, 399.3f), new Vector3(381.8f, 58.9f, 409.4f), new Vector3(450.6f, 60.5f, 422.4f),
                    new Vector3(519.6f, 62.3f, 434.2f), new Vector3(576.7f, 63.5f, 444.7f), new Vector3(601.8f, 63.8f, 451.2f),
                    new Vector3(622.8f, 64.0f, 457.8f), new Vector3(647.1f, 64.0f, 467.1f), new Vector3(674.5f, 63.5f, 479.2f),
                    new Vector3(711.9f, 62.3f, 498.3f), new Vector3(772.9f, 59.0f, 532.8f), new Vector3(833.6f, 55.4f, 567.7f),
                    new Vector3(863.4f, 53.9f, 584.0f), new Vector3(870.7f, 53.6f, 587.3f), new Vector3(878.6f, 53.3f, 587.3f),
                    new Vector3(885.3f, 53.1f, 583.4f), new Vector3(887.1f, 52.8f, 575.7f), new Vector3(886.9f, 52.6f, 567.7f),
                    new Vector3(882.4f, 52.1f, 534.0f), new Vector3(875.6f, 51.8f, 494.6f), new Vector3(864.2f, 50.2f, 445.9f),
                    new Vector3(851.6f, 47.7f, 401.7f), new Vector3(840.4f, 45.4f, 369.6f), new Vector3(821.3f, 41.7f, 323.4f),
                    new Vector3(791.6f, 36.4f, 260.0f), new Vector3(761.1f, 32.2f, 197.0f), new Vector3(730.9f, 30.1f, 133.8f),
                    new Vector3(703.2f, 29.4f, 71.7f), new Vector3(683.2f, 27.6f, 17.3f), new Vector3(661.4f, 24.6f, -49.2f),
                    new Vector3(639.9f, 21.2f, -115.9f), new Vector3(630.0f, 19.8f, -144.2f), new Vector3(626.7f, 19.5f, -151.4f),
                    new Vector3(621.5f, 19.1f, -157.5f), new Vector3(614.8f, 18.8f, -161.9f), new Vector3(607.2f, 18.5f, -164.3f),
                    new Vector3(599.3f, 18.2f, -164.5f), new Vector3(591.5f, 17.9f, -162.7f), new Vector3(584.3f, 17.6f, -159.2f),
                    new Vector3(578.1f, 17.4f, -154.2f), new Vector3(572.6f, 17.1f, -148.4f), new Vector3(559.7f, 16.6f, -133.1f),
                    new Vector3(549.9f, 16.3f, -120.4f), new Vector3(545.4f, 16.2f, -113.8f), new Vector3(540.3f, 16.1f, -105.2f),
                    new Vector3(533.8f, 16.0f, -92.8f), new Vector3(529.7f, 16.0f, -83.7f), new Vector3(526.0f, 16.0f, -74.4f),
                    new Vector3(523.6f, 16.1f, -66.8f), new Vector3(521.6f, 16.1f, -59.0f), new Vector3(519.6f, 16.2f, -49.2f),
                    new Vector3(517.8f, 16.4f, -37.4f), new Vector3(516.8f, 16.6f, -25.4f), new Vector3(516.3f, 16.8f, -11.4f),
                    new Vector3(516.7f, 17.1f, 2.6f), new Vector3(517.9f, 17.5f, 16.5f), new Vector3(519.3f, 17.8f, 26.4f),
                    new Vector3(521.3f, 18.0f, 36.2f), new Vector3(523.8f, 18.3f, 45.9f), new Vector3(526.9f, 18.6f, 55.4f),
                    new Vector3(538.0f, 19.6f, 83.3f), new Vector3(566.1f, 22.0f, 147.4f), new Vector3(593.8f, 24.2f, 211.6f),
                    new Vector3(598.7f, 24.6f, 224.8f), new Vector3(600.8f, 24.8f, 232.5f), new Vector3(601.6f, 25.0f, 240.4f),
                    new Vector3(601.7f, 25.1f, 246.4f), new Vector3(601.2f, 25.3f, 254.4f), new Vector3(599.8f, 25.4f, 262.3f),
                    new Vector3(597.5f, 25.6f, 270.0f), new Vector3(595.2f, 25.6f, 275.5f), new Vector3(592.6f, 25.7f, 280.9f),
                    new Vector3(589.7f, 25.8f, 286.2f), new Vector3(585.2f, 25.9f, 292.8f), new Vector3(579.9f, 25.9f, 298.8f),
                    new Vector3(574.0f, 26.0f, 304.1f), new Vector3(569.2f, 26.0f, 307.7f), new Vector3(564.1f, 26.0f, 310.9f),
                    new Vector3(558.9f, 26.0f, 313.8f), new Vector3(553.4f, 26.0f, 316.4f), new Vector3(547.9f, 26.1f, 318.6f),
                    new Vector3(542.1f, 26.1f, 320.4f), new Vector3(536.3f, 26.2f, 321.8f), new Vector3(530.4f, 26.3f, 322.8f),
                    new Vector3(524.4f, 26.5f, 323.4f), new Vector3(518.4f, 26.6f, 323.7f), new Vector3(510.5f, 26.8f, 323.2f),
                    new Vector3(498.7f, 27.2f, 320.7f), new Vector3(430.9f, 30.3f, 303.5f), new Vector3(363.4f, 34.5f, 284.9f),
                    new Vector3(350.1f, 35.4f, 280.6f), new Vector3(342.8f, 35.9f, 277.3f), new Vector3(336.1f, 36.4f, 272.9f),
                    new Vector3(331.4f, 36.8f, 269.2f), new Vector3(327.0f, 37.1f, 265.2f), new Vector3(321.5f, 37.6f, 259.3f),
                    new Vector3(316.8f, 38.1f, 252.9f), new Vector3(312.8f, 38.6f, 246.0f), new Vector3(309.5f, 39.0f, 238.7f),
                    new Vector3(307.1f, 39.5f, 231.0f), new Vector3(305.5f, 39.9f, 223.2f), new Vector3(304.8f, 40.3f, 215.2f),
                    new Vector3(304.9f, 40.7f, 207.2f), new Vector3(305.5f, 41.0f, 201.3f), new Vector3(306.4f, 41.3f, 195.3f),
                    new Vector3(307.7f, 41.6f, 189.5f), new Vector3(310.0f, 41.9f, 181.8f), new Vector3(313.4f, 42.3f, 174.6f),
                    new Vector3(316.3f, 42.5f, 169.4f), new Vector3(320.7f, 42.8f, 162.7f), new Vector3(339.8f, 43.6f, 136.9f),
                    new Vector3(346.4f, 43.8f, 127.0f), new Vector3(350.5f, 43.9f, 120.1f), new Vector3(353.3f, 44.0f, 114.8f),
                    new Vector3(355.8f, 44.0f, 109.3f), new Vector3(358.6f, 44.0f, 101.9f), new Vector3(364.1f, 44.0f, 84.7f),
                    new Vector3(366.7f, 43.9f, 75.0f), new Vector3(368.2f, 43.9f, 67.2f), new Vector3(369.6f, 43.9f, 57.3f),
                    new Vector3(370.6f, 43.8f, 45.3f), new Vector3(371.0f, 43.7f, 31.3f), new Vector3(370.8f, 43.6f, 23.3f),
                    new Vector3(370.2f, 43.6f, 17.4f), new Vector3(368.7f, 43.5f, 7.5f), new Vector3(353.5f, 42.6f, -60.9f),
                    new Vector3(337.0f, 41.7f, -128.9f), new Vector3(320.3f, 40.8f, -196.9f), new Vector3(303.7f, 40.2f, -264.9f),
                    new Vector3(287.1f, 39.9f, -332.9f), new Vector3(270.5f, 37.5f, -400.9f), new Vector3(262.6f, 35.7f, -431.9f),
                    new Vector3(259.7f, 35.2f, -439.4f), new Vector3(256.0f, 34.7f, -446.4f), new Vector3(251.4f, 34.1f, -453.0f),
                    new Vector3(246.1f, 33.6f, -459.0f), new Vector3(240.1f, 33.0f, -464.3f), new Vector3(233.6f, 32.4f, -468.9f),
                    new Vector3(228.4f, 32.0f, -471.9f), new Vector3(221.2f, 31.4f, -475.3f), new Vector3(215.5f, 30.9f, -477.3f),
                    new Vector3(209.8f, 30.5f, -478.9f), new Vector3(203.9f, 30.0f, -480.2f), new Vector3(196.0f, 29.4f, -481.3f),
                    new Vector3(154.0f, 26.2f, -483.4f), new Vector3(84.0f, 21.5f, -485.3f), new Vector3(48.0f, 19.7f, -485.7f),
                    new Vector3(40.2f, 19.3f, -484.3f), new Vector3(33.3f, 19.0f, -480.4f), new Vector3(28.0f, 18.8f, -474.4f),
                    new Vector3(24.4f, 18.5f, -467.3f), new Vector3(21.4f, 18.4f, -459.9f), new Vector3(18.1f, 18.2f, -450.4f),
                    new Vector3(13.9f, 18.0f, -435.0f), new Vector3(8.8f, 17.8f, -411.5f), new Vector3(4.8f, 16.9f, -385.9f),
                    new Vector3(2.5f, 15.7f, -364.0f), new Vector3(1.5f, 14.1f, -340.0f), new Vector3(0.8f, 8.5f, -270.0f),
                    new Vector3(0.4f, 4.5f, -200.0f), new Vector3(0.2f, 3.4f, -130.0f), new Vector3(0.0f, 1.1f, -60.0f)
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.7f, 70.0f), new Vector3(-0.1f, 2.4f, 140.0f),
                    new Vector3(0.0f, 4.6f, 206.1f), new Vector3(0.4f, 4.8f, 212.0f), new Vector3(2.3f, 5.0f, 219.8f),
                    new Vector3(5.1f, 5.3f, 225.1f), new Vector3(10.3f, 5.5f, 231.1f), new Vector3(15.4f, 5.7f, 234.3f),
                    new Vector3(23.1f, 6.0f, 236.4f), new Vector3(31.0f, 6.3f, 235.8f), new Vector3(36.6f, 6.5f, 233.7f),
                    new Vector3(41.6f, 6.7f, 230.5f), new Vector3(46.1f, 6.9f, 226.5f), new Vector3(50.2f, 7.0f, 222.0f),
                    new Vector3(66.2f, 7.8f, 201.6f), new Vector3(84.0f, 8.6f, 177.4f), new Vector3(91.5f, 9.0f, 165.6f),
                    new Vector3(97.3f, 9.2f, 155.1f), new Vector3(103.4f, 9.5f, 142.5f), new Vector3(108.8f, 9.7f, 129.6f),
                    new Vector3(115.4f, 9.9f, 110.7f), new Vector3(120.0f, 10.0f, 95.3f), new Vector3(122.6f, 10.0f, 83.7f),
                    new Vector3(124.9f, 10.0f, 69.8f), new Vector3(126.2f, 10.0f, 57.9f), new Vector3(126.7f, 10.1f, 45.9f),
                    new Vector3(127.4f, 10.4f, -24.1f), new Vector3(127.8f, 11.1f, -94.1f), new Vector3(129.0f, 11.8f, -164.1f),
                    new Vector3(129.5f, 11.9f, -170.1f), new Vector3(131.0f, 12.0f, -175.9f), new Vector3(133.2f, 12.1f, -181.5f),
                    new Vector3(136.1f, 12.1f, -186.8f), new Vector3(139.6f, 12.2f, -191.7f), new Vector3(143.7f, 12.3f, -196.0f),
                    new Vector3(148.6f, 12.3f, -199.5f), new Vector3(157.1f, 12.4f, -204.8f), new Vector3(162.5f, 12.5f, -207.3f),
                    new Vector3(168.3f, 12.6f, -208.7f), new Vector3(174.3f, 12.6f, -209.6f), new Vector3(180.3f, 12.7f, -209.8f),
                    new Vector3(186.3f, 12.8f, -209.4f), new Vector3(192.1f, 12.8f, -208.1f), new Vector3(197.8f, 12.9f, -206.2f),
                    new Vector3(203.1f, 13.0f, -203.3f), new Vector3(208.1f, 13.0f, -200.0f), new Vector3(212.7f, 13.1f, -196.2f),
                    new Vector3(216.5f, 13.1f, -191.6f), new Vector3(223.1f, 13.2f, -181.5f), new Vector3(225.8f, 13.3f, -176.2f),
                    new Vector3(245.7f, 13.7f, -126.0f), new Vector3(259.2f, 13.9f, -92.6f), new Vector3(261.8f, 13.9f, -87.2f),
                    new Vector3(264.9f, 13.9f, -82.0f), new Vector3(268.5f, 14.0f, -77.3f), new Vector3(272.7f, 14.0f, -73.0f),
                    new Vector3(277.6f, 14.0f, -69.5f), new Vector3(283.0f, 14.0f, -66.9f), new Vector3(297.7f, 14.0f, -60.8f),
                    new Vector3(303.4f, 14.0f, -58.8f), new Vector3(319.0f, 14.0f, -55.1f), new Vector3(387.4f, 13.5f, -40.3f),
                    new Vector3(455.9f, 12.6f, -25.5f), new Vector3(524.3f, 11.5f, -10.6f), new Vector3(592.7f, 10.3f, 4.1f),
                    new Vector3(643.7f, 9.5f, 14.3f), new Vector3(657.6f, 9.3f, 16.1f), new Vector3(671.6f, 9.1f, 17.1f),
                    new Vector3(695.6f, 8.8f, 17.3f), new Vector3(723.6f, 8.5f, 16.6f), new Vector3(729.6f, 8.4f, 16.8f),
                    new Vector3(735.5f, 8.3f, 17.7f), new Vector3(741.4f, 8.3f, 19.1f), new Vector3(746.7f, 8.2f, 21.8f),
                    new Vector3(751.3f, 8.2f, 25.6f), new Vector3(755.1f, 8.2f, 30.3f), new Vector3(796.8f, 8.0f, 86.5f),
                    new Vector3(838.6f, 7.3f, 142.7f), new Vector3(855.8f, 6.9f, 164.8f), new Vector3(859.9f, 6.8f, 169.2f),
                    new Vector3(864.6f, 6.7f, 172.9f), new Vector3(869.7f, 6.5f, 176.1f), new Vector3(875.0f, 6.4f, 179.0f),
                    new Vector3(880.5f, 6.3f, 181.4f), new Vector3(886.1f, 6.2f, 183.4f), new Vector3(891.9f, 6.0f, 184.8f),
                    new Vector3(897.9f, 5.9f, 185.3f), new Vector3(903.9f, 5.7f, 184.8f), new Vector3(909.8f, 5.6f, 183.8f),
                    new Vector3(915.6f, 5.5f, 182.3f), new Vector3(921.3f, 5.3f, 180.5f), new Vector3(926.9f, 5.2f, 178.2f),
                    new Vector3(932.1f, 5.0f, 175.3f), new Vector3(936.9f, 4.8f, 171.6f), new Vector3(941.1f, 4.7f, 167.4f),
                    new Vector3(944.8f, 4.5f, 162.7f), new Vector3(948.2f, 4.4f, 157.7f), new Vector3(951.1f, 4.2f, 152.4f),
                    new Vector3(953.5f, 4.0f, 146.9f), new Vector3(955.5f, 3.9f, 141.3f), new Vector3(957.2f, 3.7f, 135.5f),
                    new Vector3(958.5f, 3.5f, 129.7f), new Vector3(959.9f, 3.3f, 121.8f), new Vector3(961.9f, 2.7f, 101.9f),
                    new Vector3(963.4f, 1.8f, 71.9f), new Vector3(963.5f, 1.4f, 57.9f), new Vector3(962.8f, 1.1f, 47.9f),
                    new Vector3(957.4f, 0.0f, 8.3f), new Vector3(946.8f, -1.8f, -60.9f), new Vector3(940.3f, -2.6f, -98.4f),
                    new Vector3(938.9f, -2.8f, -104.2f), new Vector3(934.9f, -2.9f, -111.1f), new Vector3(930.8f, -3.0f, -115.4f),
                    new Vector3(923.7f, -3.1f, -119.1f), new Vector3(915.8f, -3.3f, -119.1f), new Vector3(903.9f, -3.4f, -117.7f),
                    new Vector3(896.0f, -3.5f, -118.7f), new Vector3(889.6f, -3.6f, -123.5f), new Vector3(885.5f, -3.7f, -127.8f),
                    new Vector3(878.1f, -3.8f, -137.3f), new Vector3(836.4f, -4.0f, -193.5f), new Vector3(807.3f, -4.2f, -234.2f),
                    new Vector3(804.2f, -4.2f, -239.3f), new Vector3(801.5f, -4.2f, -244.7f), new Vector3(799.6f, -4.2f, -250.3f),
                    new Vector3(797.9f, -4.3f, -258.2f), new Vector3(797.4f, -4.3f, -264.1f), new Vector3(797.9f, -4.3f, -270.1f),
                    new Vector3(799.9f, -4.4f, -275.8f), new Vector3(803.3f, -4.4f, -283.0f), new Vector3(819.0f, -4.7f, -310.9f),
                    new Vector3(837.7f, -5.0f, -344.0f), new Vector3(841.2f, -5.0f, -351.2f), new Vector3(843.1f, -5.1f, -356.9f),
                    new Vector3(844.4f, -5.1f, -362.7f), new Vector3(845.0f, -5.2f, -368.7f), new Vector3(845.1f, -5.2f, -374.7f),
                    new Vector3(844.6f, -5.3f, -380.7f), new Vector3(842.9f, -5.4f, -386.4f), new Vector3(840.5f, -5.4f, -391.9f),
                    new Vector3(837.0f, -5.5f, -396.8f), new Vector3(831.0f, -5.6f, -404.8f), new Vector3(826.8f, -5.6f, -409.1f),
                    new Vector3(822.1f, -5.7f, -412.8f), new Vector3(817.1f, -5.7f, -416.1f), new Vector3(756.7f, -6.4f, -451.5f),
                    new Vector3(707.1f, -7.0f, -481.7f), new Vector3(702.2f, -7.0f, -485.1f), new Vector3(695.9f, -7.1f, -490.1f),
                    new Vector3(691.5f, -7.1f, -494.1f), new Vector3(687.3f, -7.2f, -498.4f), new Vector3(683.4f, -7.2f, -503.0f),
                    new Vector3(679.9f, -7.3f, -507.8f), new Vector3(675.5f, -7.3f, -514.5f), new Vector3(672.8f, -7.4f, -519.9f),
                    new Vector3(662.8f, -7.6f, -546.1f), new Vector3(639.3f, -7.9f, -612.0f), new Vector3(630.1f, -8.0f, -636.3f),
                    new Vector3(627.6f, -8.0f, -641.8f), new Vector3(624.5f, -8.0f, -646.9f), new Vector3(620.9f, -8.0f, -651.7f),
                    new Vector3(616.6f, -8.0f, -656.0f), new Vector3(612.2f, -8.0f, -660.0f), new Vector3(607.5f, -8.0f, -663.7f),
                    new Vector3(602.4f, -8.0f, -666.9f), new Vector3(595.4f, -8.0f, -670.7f), new Vector3(589.9f, -8.0f, -673.1f),
                    new Vector3(584.1f, -7.9f, -674.7f), new Vector3(578.2f, -7.9f, -675.6f), new Vector3(572.2f, -7.9f, -675.8f),
                    new Vector3(502.2f, -7.3f, -672.5f), new Vector3(432.3f, -6.2f, -668.9f), new Vector3(362.3f, -5.0f, -665.7f),
                    new Vector3(292.4f, -3.6f, -663.0f), new Vector3(222.4f, -2.3f, -659.6f), new Vector3(210.5f, -2.1f, -658.5f),
                    new Vector3(204.6f, -2.0f, -657.3f), new Vector3(197.7f, -1.8f, -653.4f), new Vector3(193.7f, -1.7f, -648.9f),
                    new Vector3(190.6f, -1.6f, -641.6f), new Vector3(190.0f, -1.5f, -635.7f), new Vector3(190.5f, -1.4f, -629.7f),
                    new Vector3(202.3f, -0.5f, -560.7f), new Vector3(208.1f, -0.2f, -525.1f), new Vector3(208.7f, -0.2f, -519.2f),
                    new Vector3(209.5f, 0.0f, -467.2f), new Vector3(209.3f, 0.0f, -451.2f), new Vector3(208.7f, 0.0f, -445.2f),
                    new Vector3(207.3f, 0.0f, -439.3f), new Vector3(205.6f, 0.1f, -433.6f), new Vector3(203.0f, 0.1f, -428.2f),
                    new Vector3(199.8f, 0.1f, -423.1f), new Vector3(195.8f, 0.1f, -418.6f), new Vector3(191.0f, 0.2f, -415.1f),
                    new Vector3(185.7f, 0.2f, -412.2f), new Vector3(180.1f, 0.2f, -410.2f), new Vector3(174.1f, 0.3f, -409.4f),
                    new Vector3(168.1f, 0.3f, -409.5f), new Vector3(162.2f, 0.4f, -410.2f), new Vector3(156.3f, 0.4f, -411.5f),
                    new Vector3(150.7f, 0.5f, -413.7f), new Vector3(145.6f, 0.5f, -416.8f), new Vector3(141.0f, 0.6f, -420.6f),
                    new Vector3(136.8f, 0.7f, -424.9f), new Vector3(133.4f, 0.7f, -429.8f), new Vector3(130.8f, 0.8f, -435.2f),
                    new Vector3(128.9f, 0.9f, -440.9f), new Vector3(128.3f, 0.9f, -446.9f), new Vector3(127.2f, 1.9f, -516.9f),
                    new Vector3(126.2f, 3.0f, -584.9f), new Vector3(125.5f, 3.2f, -592.9f), new Vector3(124.5f, 3.3f, -598.8f),
                    new Vector3(122.6f, 3.4f, -604.5f), new Vector3(119.9f, 3.5f, -609.8f), new Vector3(116.6f, 3.6f, -614.8f),
                    new Vector3(112.8f, 3.7f, -619.5f), new Vector3(108.8f, 3.8f, -623.9f), new Vector3(104.4f, 3.9f, -628.0f),
                    new Vector3(99.6f, 4.0f, -631.6f), new Vector3(94.5f, 4.1f, -634.8f), new Vector3(89.0f, 4.2f, -637.3f),
                    new Vector3(83.3f, 4.3f, -639.1f), new Vector3(75.6f, 4.4f, -641.2f), new Vector3(69.7f, 4.5f, -642.2f),
                    new Vector3(63.7f, 4.6f, -642.4f), new Vector3(57.7f, 4.7f, -641.9f), new Vector3(51.9f, 4.8f, -640.5f),
                    new Vector3(46.2f, 4.9f, -638.7f), new Vector3(40.6f, 5.0f, -636.4f), new Vector3(35.2f, 5.1f, -633.8f),
                    new Vector3(30.0f, 5.2f, -630.8f), new Vector3(25.2f, 5.3f, -627.2f), new Vector3(20.7f, 5.4f, -623.2f),
                    new Vector3(16.7f, 5.5f, -618.8f), new Vector3(13.1f, 5.6f, -613.9f), new Vector3(10.0f, 5.6f, -608.8f),
                    new Vector3(7.3f, 5.7f, -603.4f), new Vector3(5.2f, 5.8f, -597.8f), new Vector3(3.6f, 5.9f, -592.1f),
                    new Vector3(2.5f, 6.0f, -586.2f), new Vector3(2.0f, 6.0f, -580.2f), new Vector3(1.3f, 6.7f, -510.2f),
                    new Vector3(1.0f, 7.0f, -440.1f), new Vector3(0.7f, 6.6f, -370.1f), new Vector3(0.5f, 5.4f, -300.1f),
                    new Vector3(0.3f, 3.8f, -230.1f), new Vector3(0.2f, 2.1f, -160.1f), new Vector3(0.0f, 0.8f, -90.0f),
                    new Vector3(0.0f, 0.0f, -20.0f)
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.7f, 70.0f), new Vector3(0.1f, 2.2f, 140.0f),
                    new Vector3(0.6f, 4.1f, 210.0f), new Vector3(1.9f, 5.0f, 250.0f), new Vector3(2.6f, 5.1f, 258.0f),
                    new Vector3(4.6f, 5.3f, 265.7f), new Vector3(7.5f, 5.4f, 270.9f), new Vector3(11.3f, 5.5f, 275.6f),
                    new Vector3(15.7f, 5.6f, 279.6f), new Vector3(20.7f, 5.7f, 283.0f), new Vector3(26.0f, 5.7f, 285.7f),
                    new Vector3(31.7f, 5.8f, 287.6f), new Vector3(39.6f, 5.9f, 288.6f), new Vector3(45.6f, 5.9f, 288.1f),
                    new Vector3(51.4f, 6.0f, 286.8f), new Vector3(57.1f, 6.0f, 284.8f), new Vector3(62.4f, 6.0f, 282.0f),
                    new Vector3(67.2f, 6.0f, 278.4f), new Vector3(71.4f, 6.0f, 274.1f), new Vector3(75.0f, 6.0f, 269.3f),
                    new Vector3(77.8f, 6.0f, 264.0f), new Vector3(80.1f, 6.1f, 256.4f), new Vector3(80.7f, 6.1f, 250.4f),
                    new Vector3(80.9f, 6.2f, 242.4f), new Vector3(80.9f, 7.3f, 172.4f), new Vector3(82.2f, 8.4f, 128.4f),
                    new Vector3(83.5f, 8.8f, 114.5f), new Vector3(85.5f, 9.2f, 100.6f), new Vector3(88.3f, 9.6f, 86.9f),
                    new Vector3(92.0f, 10.0f, 73.4f), new Vector3(96.4f, 10.4f, 60.1f), new Vector3(100.7f, 10.7f, 48.9f),
                    new Vector3(108.2f, 11.2f, 32.6f), new Vector3(112.2f, 11.5f, 23.4f), new Vector3(114.2f, 11.6f, 17.7f),
                    new Vector3(115.5f, 11.8f, 11.9f), new Vector3(115.9f, 11.9f, 5.9f), new Vector3(114.6f, 12.1f, -2.0f),
                    new Vector3(112.5f, 12.3f, -7.6f), new Vector3(109.9f, 12.4f, -13.0f), new Vector3(107.1f, 12.5f, -18.3f),
                    new Vector3(104.0f, 12.7f, -23.5f), new Vector3(100.5f, 12.8f, -28.3f), new Vector3(96.5f, 12.9f, -32.8f),
                    new Vector3(59.2f, 13.7f, -69.0f), new Vector3(51.0f, 13.9f, -77.8f), new Vector3(47.2f, 13.9f, -82.4f),
                    new Vector3(43.2f, 14.0f, -89.4f), new Vector3(42.0f, 14.0f, -97.2f), new Vector3(42.8f, 14.0f, -103.1f),
                    new Vector3(44.2f, 14.0f, -109.0f), new Vector3(46.2f, 14.0f, -114.6f), new Vector3(50.3f, 14.0f, -121.5f),
                    new Vector3(54.4f, 14.0f, -125.9f), new Vector3(60.9f, 14.0f, -130.5f), new Vector3(68.3f, 13.9f, -133.3f),
                    new Vector3(74.2f, 13.9f, -134.3f), new Vector3(82.2f, 13.9f, -133.9f), new Vector3(88.0f, 13.9f, -132.3f),
                    new Vector3(93.5f, 13.8f, -129.9f), new Vector3(98.6f, 13.8f, -126.7f), new Vector3(109.8f, 13.7f, -118.4f),
                    new Vector3(160.8f, 13.2f, -76.5f), new Vector3(184.6f, 12.9f, -58.2f), new Vector3(196.2f, 12.7f, -50.4f),
                    new Vector3(209.9f, 12.5f, -42.1f), new Vector3(222.3f, 12.4f, -35.6f), new Vector3(236.9f, 12.2f, -29.0f),
                    new Vector3(253.6f, 12.0f, -22.4f), new Vector3(270.7f, 11.8f, -16.8f), new Vector3(288.1f, 11.6f, -12.1f),
                    new Vector3(303.8f, 11.4f, -8.8f), new Vector3(313.7f, 11.3f, -7.2f), new Vector3(329.5f, 11.1f, -5.3f),
                    new Vector3(353.2f, 10.8f, -1.2f), new Vector3(368.8f, 10.7f, 2.4f), new Vector3(382.2f, 10.6f, 6.2f),
                    new Vector3(389.8f, 10.5f, 8.8f), new Vector3(395.4f, 10.5f, 11.1f), new Vector3(400.8f, 10.4f, 13.7f),
                    new Vector3(406.0f, 10.4f, 16.6f), new Vector3(415.9f, 10.3f, 23.4f), new Vector3(432.0f, 10.2f, 35.3f),
                    new Vector3(438.1f, 10.1f, 40.5f), new Vector3(445.3f, 10.1f, 47.4f), new Vector3(453.4f, 10.0f, 56.3f),
                    new Vector3(464.8f, 10.0f, 70.2f), new Vector3(482.4f, 10.0f, 94.5f), new Vector3(495.4f, 9.9f, 112.3f),
                    new Vector3(503.0f, 9.8f, 121.5f), new Vector3(511.3f, 9.7f, 130.2f), new Vector3(518.6f, 9.6f, 137.1f),
                    new Vector3(524.7f, 9.5f, 142.2f), new Vector3(531.2f, 9.4f, 146.9f), new Vector3(539.5f, 9.2f, 152.4f),
                    new Vector3(551.7f, 9.0f, 159.3f), new Vector3(564.2f, 8.8f, 165.5f), new Vector3(573.4f, 8.6f, 169.5f),
                    new Vector3(639.5f, 7.1f, 192.7f), new Vector3(685.2f, 6.0f, 207.5f), new Vector3(694.8f, 5.8f, 209.9f),
                    new Vector3(700.8f, 5.6f, 211.0f), new Vector3(706.7f, 5.5f, 211.6f), new Vector3(712.7f, 5.4f, 211.9f),
                    new Vector3(718.7f, 5.2f, 211.8f), new Vector3(724.7f, 5.1f, 211.4f), new Vector3(730.7f, 5.0f, 210.7f),
                    new Vector3(736.6f, 4.8f, 209.7f), new Vector3(742.4f, 4.7f, 208.2f), new Vector3(748.1f, 4.5f, 206.3f),
                    new Vector3(753.6f, 4.4f, 204.0f), new Vector3(759.0f, 4.3f, 201.3f), new Vector3(764.1f, 4.2f, 198.2f),
                    new Vector3(775.6f, 3.9f, 190.3f), new Vector3(780.4f, 3.7f, 186.6f), new Vector3(784.9f, 3.6f, 182.6f),
                    new Vector3(789.0f, 3.5f, 178.2f), new Vector3(792.7f, 3.4f, 173.6f), new Vector3(796.2f, 3.3f, 168.6f),
                    new Vector3(799.4f, 3.2f, 163.6f), new Vector3(803.2f, 3.1f, 156.5f), new Vector3(806.4f, 2.9f, 149.2f),
                    new Vector3(808.6f, 2.8f, 143.6f), new Vector3(810.4f, 2.7f, 137.9f), new Vector3(811.9f, 2.7f, 132.1f),
                    new Vector3(813.1f, 2.6f, 126.2f), new Vector3(813.9f, 2.5f, 120.3f), new Vector3(814.4f, 2.4f, 114.3f),
                    new Vector3(814.5f, 2.4f, 108.3f), new Vector3(814.2f, 2.3f, 102.3f), new Vector3(813.5f, 2.2f, 96.3f),
                    new Vector3(811.5f, 2.2f, 84.5f), new Vector3(795.7f, 1.9f, 16.3f), new Vector3(790.5f, 1.8f, -9.2f),
                    new Vector3(789.1f, 1.7f, -19.1f), new Vector3(786.8f, 1.5f, -49.0f), new Vector3(784.1f, 0.9f, -96.9f),
                    new Vector3(782.8f, 0.7f, -108.9f), new Vector3(781.6f, 0.6f, -116.8f), new Vector3(780.2f, 0.5f, -122.6f),
                    new Vector3(777.2f, 0.3f, -130.0f), new Vector3(771.6f, 0.2f, -135.6f), new Vector3(766.8f, 0.1f, -139.2f),
                    new Vector3(761.7f, 0.0f, -142.4f), new Vector3(754.8f, -0.1f, -146.4f), new Vector3(747.6f, -0.3f, -150.0f),
                    new Vector3(740.3f, -0.4f, -153.1f), new Vector3(732.7f, -0.5f, -155.8f), new Vector3(721.2f, -0.8f, -159.3f),
                    new Vector3(709.6f, -1.0f, -162.1f), new Vector3(699.7f, -1.1f, -163.9f), new Vector3(689.8f, -1.3f, -165.1f),
                    new Vector3(677.8f, -1.5f, -165.8f), new Vector3(663.8f, -1.8f, -165.9f), new Vector3(649.8f, -2.0f, -165.3f),
                    new Vector3(637.9f, -2.2f, -164.1f), new Vector3(626.1f, -2.4f, -162.2f), new Vector3(614.3f, -2.6f, -159.6f),
                    new Vector3(604.7f, -2.7f, -156.9f), new Vector3(597.1f, -2.9f, -154.3f), new Vector3(589.7f, -3.0f, -151.2f),
                    new Vector3(584.4f, -3.0f, -148.6f), new Vector3(579.2f, -3.1f, -145.6f), new Vector3(574.4f, -3.2f, -142.0f),
                    new Vector3(569.5f, -3.3f, -135.7f), new Vector3(567.0f, -3.4f, -128.2f), new Vector3(566.6f, -3.5f, -120.2f),
                    new Vector3(567.3f, -3.5f, -114.2f), new Vector3(568.6f, -3.6f, -108.4f), new Vector3(570.6f, -3.7f, -102.7f),
                    new Vector3(573.2f, -3.7f, -97.3f), new Vector3(576.5f, -3.7f, -92.3f), new Vector3(580.2f, -3.8f, -87.6f),
                    new Vector3(584.2f, -3.8f, -83.1f), new Vector3(589.9f, -3.9f, -77.5f), new Vector3(594.4f, -3.9f, -73.6f),
                    new Vector3(600.8f, -3.9f, -68.7f), new Vector3(610.7f, -4.0f, -61.9f), new Vector3(635.9f, -4.0f, -45.7f),
                    new Vector3(655.4f, -3.9f, -31.7f), new Vector3(669.4f, -3.8f, -20.4f), new Vector3(682.8f, -3.7f, -8.3f),
                    new Vector3(691.2f, -3.6f, 0.3f), new Vector3(696.4f, -3.5f, 6.3f), new Vector3(700.1f, -3.4f, 11.1f),
                    new Vector3(703.3f, -3.3f, 16.1f), new Vector3(706.1f, -3.3f, 21.4f), new Vector3(708.1f, -3.2f, 27.1f),
                    new Vector3(709.1f, -3.1f, 35.0f), new Vector3(708.8f, -3.0f, 41.0f), new Vector3(707.7f, -2.9f, 46.9f),
                    new Vector3(706.0f, -2.8f, 52.6f), new Vector3(703.5f, -2.8f, 58.1f), new Vector3(700.3f, -2.7f, 63.2f),
                    new Vector3(696.6f, -2.6f, 67.8f), new Vector3(692.2f, -2.5f, 71.9f), new Vector3(687.2f, -2.4f, 75.4f),
                    new Vector3(681.9f, -2.3f, 78.2f), new Vector3(676.4f, -2.2f, 80.4f), new Vector3(670.7f, -2.1f, 82.2f),
                    new Vector3(664.8f, -2.0f, 83.2f), new Vector3(656.8f, -1.9f, 82.8f), new Vector3(647.0f, -1.7f, 80.7f),
                    new Vector3(608.3f, -1.0f, 70.4f), new Vector3(576.0f, -0.4f, 60.0f), new Vector3(544.2f, 0.2f, 47.9f),
                    new Vector3(511.3f, 0.7f, 33.3f), new Vector3(475.7f, 1.3f, 15.0f), new Vector3(441.2f, 1.7f, -5.2f),
                    new Vector3(416.2f, 1.9f, -21.7f), new Vector3(374.5f, 2.0f, -52.9f), new Vector3(343.7f, 2.2f, -78.4f),
                    new Vector3(302.6f, 2.9f, -116.4f), new Vector3(275.9f, 3.6f, -143.4f), new Vector3(262.7f, 4.0f, -158.5f),
                    new Vector3(245.5f, 4.6f, -180.6f), new Vector3(237.9f, 4.9f, -189.9f), new Vector3(233.7f, 5.0f, -194.2f),
                    new Vector3(226.9f, 5.2f, -198.3f), new Vector3(219.1f, 5.4f, -198.0f), new Vector3(212.0f, 5.6f, -194.3f),
                    new Vector3(205.5f, 5.8f, -189.7f), new Vector3(197.6f, 6.0f, -183.5f), new Vector3(192.6f, 6.1f, -180.2f),
                    new Vector3(185.2f, 6.3f, -177.3f), new Vector3(177.3f, 6.5f, -176.6f), new Vector3(169.4f, 6.7f, -178.0f),
                    new Vector3(162.1f, 6.9f, -181.2f), new Vector3(157.2f, 7.0f, -184.6f), new Vector3(152.9f, 7.2f, -188.8f),
                    new Vector3(149.2f, 7.3f, -193.5f), new Vector3(145.7f, 7.5f, -200.7f), new Vector3(144.3f, 7.6f, -206.5f),
                    new Vector3(143.7f, 7.7f, -212.5f), new Vector3(144.5f, 7.9f, -220.4f), new Vector3(146.6f, 8.0f, -226.1f),
                    new Vector3(149.3f, 8.1f, -231.4f), new Vector3(186.3f, 9.4f, -290.8f), new Vector3(223.6f, 10.0f, -350.1f),
                    new Vector3(260.8f, 9.8f, -409.4f), new Vector3(279.5f, 9.6f, -440.1f), new Vector3(282.1f, 9.5f, -445.6f),
                    new Vector3(283.7f, 9.4f, -451.3f), new Vector3(284.7f, 9.4f, -457.3f), new Vector3(285.1f, 9.3f, -463.2f),
                    new Vector3(285.0f, 9.3f, -469.2f), new Vector3(284.4f, 9.2f, -475.2f), new Vector3(283.3f, 9.1f, -481.1f),
                    new Vector3(280.5f, 9.0f, -488.6f), new Vector3(277.6f, 8.9f, -493.8f), new Vector3(274.2f, 8.9f, -498.8f),
                    new Vector3(270.3f, 8.8f, -503.3f), new Vector3(266.0f, 8.7f, -507.4f), new Vector3(261.0f, 8.6f, -510.8f),
                    new Vector3(254.0f, 8.5f, -514.6f), new Vector3(221.2f, 8.0f, -529.6f), new Vector3(184.4f, 7.5f, -545.1f),
                    new Vector3(178.7f, 7.4f, -547.0f), new Vector3(167.1f, 7.2f, -550.2f), new Vector3(159.3f, 7.1f, -551.9f),
                    new Vector3(151.4f, 7.0f, -553.2f), new Vector3(141.4f, 6.9f, -554.2f), new Vector3(129.5f, 6.7f, -554.7f),
                    new Vector3(121.5f, 6.7f, -554.6f), new Vector3(115.5f, 6.6f, -554.0f), new Vector3(109.6f, 6.5f, -553.1f),
                    new Vector3(101.8f, 6.5f, -551.3f), new Vector3(92.1f, 6.4f, -548.6f), new Vector3(84.6f, 6.3f, -545.9f),
                    new Vector3(79.1f, 6.3f, -543.5f), new Vector3(73.7f, 6.2f, -540.8f), new Vector3(65.1f, 6.1f, -535.7f),
                    new Vector3(55.1f, 6.1f, -529.1f), new Vector3(48.7f, 6.1f, -524.4f), new Vector3(44.1f, 6.0f, -520.5f),
                    new Vector3(39.8f, 6.0f, -516.3f), new Vector3(34.4f, 6.0f, -510.4f), new Vector3(29.4f, 6.0f, -504.2f),
                    new Vector3(24.7f, 6.0f, -497.7f), new Vector3(21.5f, 6.0f, -492.6f), new Vector3(18.7f, 6.0f, -487.3f),
                    new Vector3(15.3f, 5.9f, -480.0f), new Vector3(11.0f, 5.8f, -468.8f), new Vector3(6.6f, 5.7f, -455.5f),
                    new Vector3(4.1f, 5.5f, -445.9f), new Vector3(2.6f, 5.4f, -438.0f), new Vector3(1.9f, 5.3f, -432.1f),
                    new Vector3(0.8f, 4.7f, -394.1f), new Vector3(0.3f, 3.3f, -324.1f), new Vector3(0.1f, 2.2f, -254.0f),
                    new Vector3(0.0f, 1.9f, -184.0f), new Vector3(0.0f, 1.1f, -114.0f), new Vector3(0.0f, 0.2f, -44.0f)
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
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.3f, 70.0f), new Vector3(-0.8f, 0.6f, 100.0f),
                    new Vector3(-1.4f, 0.6f, 106.0f), new Vector3(-6.0f, 0.7f, 112.2f), new Vector3(-11.4f, 0.8f, 114.8f),
                    new Vector3(-17.1f, 0.9f, 116.7f), new Vector3(-30.7f, 1.1f, 120.1f), new Vector3(-38.4f, 1.2f, 122.3f),
                    new Vector3(-44.0f, 1.3f, 124.2f), new Vector3(-49.7f, 1.4f, 129.4f), new Vector3(-51.1f, 1.5f, 137.2f),
                    new Vector3(-51.3f, 1.7f, 145.2f), new Vector3(-50.7f, 2.3f, 183.2f), new Vector3(-49.9f, 2.5f, 193.2f),
                    new Vector3(-48.8f, 2.7f, 201.1f), new Vector3(-47.3f, 2.8f, 209.0f), new Vector3(-45.8f, 2.9f, 214.8f),
                    new Vector3(-43.9f, 3.1f, 220.5f), new Vector3(-41.6f, 3.2f, 226.1f), new Vector3(-38.1f, 3.3f, 233.2f),
                    new Vector3(-34.1f, 3.5f, 240.2f), new Vector3(-29.7f, 3.7f, 246.9f), new Vector3(-25.0f, 3.8f, 253.3f),
                    new Vector3(-21.1f, 3.9f, 257.9f), new Vector3(-17.0f, 4.1f, 262.2f), new Vector3(-12.6f, 4.2f, 266.3f),
                    new Vector3(-8.0f, 4.3f, 270.2f), new Vector3(-3.0f, 4.4f, 273.6f), new Vector3(2.2f, 4.6f, 276.5f),
                    new Vector3(18.4f, 4.9f, 284.4f), new Vector3(82.8f, 6.4f, 311.9f), new Vector3(147.8f, 7.6f, 337.9f),
                    new Vector3(212.8f, 8.5f, 363.8f), new Vector3(277.8f, 9.0f, 389.9f), new Vector3(342.6f, 9.1f, 416.4f),
                    new Vector3(402.4f, 9.4f, 439.3f), new Vector3(423.4f, 9.6f, 446.0f), new Vector3(467.9f, 10.0f, 457.5f),
                    new Vector3(487.5f, 10.2f, 461.6f), new Vector3(497.4f, 10.3f, 463.0f), new Vector3(539.3f, 10.8f, 466.6f),
                    new Vector3(563.2f, 11.1f, 467.4f), new Vector3(587.2f, 11.4f, 467.0f), new Vector3(615.2f, 11.8f, 464.9f),
                    new Vector3(637.0f, 12.1f, 461.9f), new Vector3(652.7f, 12.3f, 458.9f), new Vector3(683.7f, 12.8f, 450.9f),
                    new Vector3(708.5f, 13.1f, 443.3f), new Vector3(721.7f, 13.3f, 438.4f), new Vector3(734.5f, 13.5f, 432.7f),
                    new Vector3(745.1f, 13.7f, 427.2f), new Vector3(760.7f, 13.9f, 418.1f), new Vector3(774.0f, 14.1f, 409.2f),
                    new Vector3(783.6f, 14.3f, 402.0f), new Vector3(797.2f, 14.5f, 390.3f), new Vector3(807.4f, 14.6f, 380.7f),
                    new Vector3(814.0f, 14.7f, 376.3f), new Vector3(821.6f, 14.8f, 377.5f), new Vector3(825.8f, 14.9f, 381.7f),
                    new Vector3(830.2f, 14.9f, 385.8f), new Vector3(837.1f, 15.0f, 389.7f), new Vector3(844.9f, 15.1f, 388.9f),
                    new Vector3(851.5f, 15.2f, 384.4f), new Vector3(855.9f, 15.2f, 380.3f), new Vector3(860.8f, 15.3f, 376.8f),
                    new Vector3(866.2f, 15.3f, 374.2f), new Vector3(872.0f, 15.4f, 372.6f), new Vector3(877.9f, 15.4f, 371.8f),
                    new Vector3(883.9f, 15.5f, 371.3f), new Vector3(891.9f, 15.5f, 371.2f), new Vector3(905.9f, 15.6f, 371.9f),
                    new Vector3(939.7f, 15.8f, 375.4f), new Vector3(959.5f, 15.9f, 378.5f), new Vector3(967.3f, 15.9f, 380.2f),
                    new Vector3(975.0f, 16.0f, 382.3f), new Vector3(982.6f, 16.0f, 384.9f), new Vector3(999.2f, 16.0f, 391.8f),
                    new Vector3(1011.9f, 16.0f, 397.6f), new Vector3(1017.5f, 16.0f, 399.9f), new Vector3(1023.2f, 16.0f, 401.7f),
                    new Vector3(1029.1f, 16.0f, 403.0f), new Vector3(1035.0f, 16.0f, 403.9f), new Vector3(1084.9f, 15.9f, 407.7f),
                    new Vector3(1128.7f, 15.8f, 411.3f), new Vector3(1138.6f, 15.7f, 412.7f), new Vector3(1144.5f, 15.7f, 413.9f),
                    new Vector3(1150.3f, 15.7f, 415.5f), new Vector3(1155.9f, 15.7f, 417.5f), new Vector3(1161.3f, 15.7f, 420.2f),
                    new Vector3(1166.6f, 15.6f, 423.1f), new Vector3(1174.3f, 15.6f, 424.7f), new Vector3(1180.9f, 15.6f, 420.6f),
                    new Vector3(1183.9f, 15.5f, 415.5f), new Vector3(1193.2f, 15.4f, 397.7f), new Vector3(1196.3f, 15.4f, 392.6f),
                    new Vector3(1200.0f, 15.4f, 387.8f), new Vector3(1204.1f, 15.3f, 383.5f), new Vector3(1208.8f, 15.3f, 379.8f),
                    new Vector3(1214.0f, 15.3f, 376.8f), new Vector3(1219.7f, 15.2f, 374.9f), new Vector3(1225.6f, 15.2f, 374.0f),
                    new Vector3(1231.6f, 15.2f, 373.8f), new Vector3(1237.6f, 15.1f, 374.2f), new Vector3(1243.5f, 15.1f, 375.1f),
                    new Vector3(1251.3f, 15.1f, 376.9f), new Vector3(1272.5f, 14.9f, 383.1f), new Vector3(1303.0f, 14.7f, 392.6f),
                    new Vector3(1312.7f, 14.6f, 394.9f), new Vector3(1318.7f, 14.6f, 395.8f), new Vector3(1324.7f, 14.6f, 395.6f),
                    new Vector3(1366.2f, 14.3f, 389.3f), new Vector3(1435.1f, 13.8f, 376.8f), new Vector3(1454.6f, 13.7f, 372.4f),
                    new Vector3(1462.3f, 13.6f, 370.1f), new Vector3(1467.9f, 13.6f, 368.1f), new Vector3(1473.4f, 13.5f, 365.7f),
                    new Vector3(1478.6f, 13.5f, 362.6f), new Vector3(1483.1f, 13.4f, 358.7f), new Vector3(1487.0f, 13.4f, 354.2f),
                    new Vector3(1490.5f, 13.4f, 349.3f), new Vector3(1494.7f, 13.3f, 342.5f), new Vector3(1513.6f, 13.1f, 307.2f),
                    new Vector3(1518.6f, 13.0f, 298.6f), new Vector3(1521.9f, 13.0f, 293.6f), new Vector3(1525.6f, 12.9f, 288.8f),
                    new Vector3(1529.6f, 12.9f, 284.3f), new Vector3(1534.0f, 12.8f, 280.3f), new Vector3(1538.9f, 12.8f, 276.8f),
                    new Vector3(1544.2f, 12.8f, 274.0f), new Vector3(1549.8f, 12.7f, 271.7f), new Vector3(1555.5f, 12.7f, 270.1f),
                    new Vector3(1561.4f, 12.7f, 269.0f), new Vector3(1571.4f, 12.6f, 267.8f), new Vector3(1577.3f, 12.6f, 267.3f),
                    new Vector3(1583.3f, 12.6f, 267.4f), new Vector3(1589.3f, 12.5f, 268.1f), new Vector3(1624.7f, 12.4f, 274.9f),
                    new Vector3(1675.7f, 12.2f, 284.9f), new Vector3(1689.6f, 12.1f, 286.6f), new Vector3(1695.6f, 12.1f, 287.0f),
                    new Vector3(1701.6f, 12.1f, 287.0f), new Vector3(1709.6f, 12.1f, 286.3f), new Vector3(1717.5f, 12.1f, 285.1f),
                    new Vector3(1725.3f, 12.1f, 283.5f), new Vector3(1731.1f, 12.0f, 282.0f), new Vector3(1736.8f, 12.0f, 280.2f),
                    new Vector3(1742.4f, 12.0f, 277.9f), new Vector3(1747.8f, 12.0f, 275.3f), new Vector3(1753.0f, 12.0f, 272.2f),
                    new Vector3(1757.9f, 12.0f, 268.8f), new Vector3(1762.6f, 12.0f, 265.1f), new Vector3(1768.6f, 12.0f, 259.9f),
                    new Vector3(1774.3f, 12.0f, 254.2f), new Vector3(1778.2f, 12.0f, 249.7f), new Vector3(1781.9f, 12.0f, 244.9f),
                    new Vector3(1785.2f, 12.0f, 239.9f), new Vector3(1788.0f, 12.0f, 234.6f), new Vector3(1790.4f, 12.0f, 229.1f),
                    new Vector3(1792.5f, 12.0f, 223.5f), new Vector3(1794.1f, 12.0f, 217.7f), new Vector3(1795.4f, 11.9f, 211.8f),
                    new Vector3(1796.4f, 11.9f, 205.9f), new Vector3(1797.0f, 11.9f, 199.9f), new Vector3(1797.3f, 11.9f, 193.9f),
                    new Vector3(1797.2f, 11.8f, 185.9f), new Vector3(1796.3f, 11.8f, 176.0f), new Vector3(1794.9f, 11.8f, 166.1f),
                    new Vector3(1792.9f, 11.7f, 156.3f), new Vector3(1791.3f, 11.7f, 150.5f), new Vector3(1789.2f, 11.6f, 144.9f),
                    new Vector3(1786.8f, 11.6f, 139.4f), new Vector3(1783.9f, 11.5f, 134.1f), new Vector3(1780.8f, 11.5f, 129.0f),
                    new Vector3(1777.3f, 11.5f, 124.1f), new Vector3(1772.2f, 11.4f, 117.9f), new Vector3(1766.8f, 11.3f, 112.0f),
                    new Vector3(1759.7f, 11.2f, 105.0f), new Vector3(1752.1f, 11.2f, 98.4f), new Vector3(1744.2f, 11.1f, 92.3f),
                    new Vector3(1737.6f, 11.0f, 87.8f), new Vector3(1730.8f, 10.9f, 83.6f), new Vector3(1723.7f, 10.8f, 79.9f),
                    new Vector3(1716.4f, 10.8f, 76.7f), new Vector3(1710.8f, 10.7f, 74.6f), new Vector3(1705.0f, 10.6f, 72.8f),
                    new Vector3(1699.2f, 10.6f, 71.4f), new Vector3(1693.3f, 10.5f, 70.4f), new Vector3(1687.3f, 10.4f, 69.7f),
                    new Vector3(1681.3f, 10.4f, 69.4f), new Vector3(1673.3f, 10.3f, 69.5f), new Vector3(1663.4f, 10.2f, 70.3f),
                    new Vector3(1653.4f, 10.0f, 71.5f), new Vector3(1643.6f, 9.9f, 73.4f), new Vector3(1635.9f, 9.8f, 75.3f),
                    new Vector3(1628.2f, 9.7f, 77.8f), new Vector3(1620.8f, 9.6f, 80.7f), new Vector3(1613.5f, 9.5f, 84.1f),
                    new Vector3(1606.5f, 9.4f, 87.9f), new Vector3(1596.2f, 9.2f, 94.2f), new Vector3(1584.7f, 9.1f, 102.1f),
                    new Vector3(1573.7f, 8.9f, 110.8f), new Vector3(1549.4f, 8.4f, 131.6f), new Vector3(1545.2f, 8.3f, 135.8f),
                    new Vector3(1498.6f, 7.4f, 188.1f), new Vector3(1452.2f, 6.4f, 240.6f), new Vector3(1412.2f, 5.7f, 282.6f),
                    new Vector3(1375.7f, 5.2f, 316.8f), new Vector3(1366.6f, 5.1f, 324.6f), new Vector3(1361.8f, 5.0f, 328.1f),
                    new Vector3(1354.3f, 4.9f, 331.0f), new Vector3(1348.4f, 4.9f, 331.8f), new Vector3(1342.4f, 4.8f, 331.6f),
                    new Vector3(1336.6f, 4.8f, 330.2f), new Vector3(1331.1f, 4.7f, 327.8f), new Vector3(1325.9f, 4.7f, 324.7f),
                    new Vector3(1321.1f, 4.6f, 321.2f), new Vector3(1311.9f, 4.5f, 313.5f), new Vector3(1271.4f, 4.2f, 274.8f),
                    new Vector3(1242.2f, 4.0f, 247.4f), new Vector3(1231.5f, 4.0f, 238.5f), new Vector3(1223.4f, 4.0f, 232.5f),
                    new Vector3(1216.6f, 4.0f, 228.2f), new Vector3(1211.4f, 4.0f, 225.4f), new Vector3(1205.9f, 4.0f, 223.0f),
                    new Vector3(1200.2f, 4.0f, 221.0f), new Vector3(1194.4f, 4.0f, 219.4f), new Vector3(1188.6f, 4.0f, 218.2f),
                    new Vector3(1182.6f, 4.0f, 217.4f), new Vector3(1176.6f, 4.0f, 217.4f), new Vector3(1168.6f, 4.0f, 218.0f),
                    new Vector3(1142.9f, 4.1f, 221.7f), new Vector3(1073.9f, 4.3f, 233.5f), new Vector3(1058.0f, 4.3f, 235.7f),
                    new Vector3(1052.0f, 4.4f, 236.1f), new Vector3(1046.0f, 4.4f, 236.0f), new Vector3(1040.1f, 4.4f, 235.4f),
                    new Vector3(1034.1f, 4.4f, 234.4f), new Vector3(1026.4f, 4.5f, 232.6f), new Vector3(1016.8f, 4.5f, 229.7f),
                    new Vector3(1005.5f, 4.6f, 225.7f), new Vector3(994.4f, 4.7f, 221.0f), new Vector3(983.6f, 4.7f, 215.7f),
                    new Vector3(974.9f, 4.8f, 210.8f), new Vector3(969.9f, 4.8f, 207.6f), new Vector3(965.0f, 4.8f, 204.0f),
                    new Vector3(960.4f, 4.9f, 200.2f), new Vector3(956.0f, 4.9f, 196.1f), new Vector3(951.9f, 5.0f, 191.7f),
                    new Vector3(946.8f, 5.0f, 185.6f), new Vector3(942.0f, 5.0f, 179.2f), new Vector3(937.6f, 5.1f, 172.5f),
                    new Vector3(934.7f, 5.1f, 167.3f), new Vector3(932.2f, 5.2f, 161.8f), new Vector3(929.6f, 5.2f, 154.2f),
                    new Vector3(925.0f, 5.3f, 136.8f), new Vector3(913.9f, 5.7f, 81.9f), new Vector3(906.7f, 6.0f, 36.5f),
                    new Vector3(905.2f, 6.1f, 22.6f), new Vector3(904.9f, 6.2f, 14.6f), new Vector3(905.1f, 6.2f, 8.6f),
                    new Vector3(906.3f, 6.3f, 2.7f), new Vector3(910.8f, 6.4f, -12.7f), new Vector3(915.7f, 6.5f, -27.9f),
                    new Vector3(917.2f, 6.5f, -33.7f), new Vector3(917.6f, 6.6f, -41.6f), new Vector3(913.3f, 6.6f, -48.2f),
                    new Vector3(908.2f, 6.7f, -51.4f), new Vector3(897.5f, 6.8f, -56.8f), new Vector3(859.0f, 7.0f, -73.5f),
                    new Vector3(793.3f, 7.4f, -97.9f), new Vector3(751.5f, 7.6f, -111.8f), new Vector3(740.0f, 7.7f, -115.0f),
                    new Vector3(732.4f, 7.7f, -117.6f), new Vector3(726.9f, 7.7f, -119.9f), new Vector3(721.6f, 7.7f, -122.7f),
                    new Vector3(716.6f, 7.8f, -126.1f), new Vector3(712.1f, 7.8f, -130.1f), new Vector3(707.9f, 7.8f, -134.4f),
                    new Vector3(704.1f, 7.8f, -139.0f), new Vector3(700.7f, 7.8f, -144.0f), new Vector3(697.9f, 7.9f, -149.2f),
                    new Vector3(695.8f, 7.9f, -154.9f), new Vector3(694.5f, 7.9f, -160.7f), new Vector3(693.1f, 7.9f, -170.6f),
                    new Vector3(691.0f, 8.0f, -196.5f), new Vector3(687.6f, 8.0f, -242.4f), new Vector3(685.8f, 8.0f, -256.3f),
                    new Vector3(684.3f, 8.0f, -264.2f), new Vector3(682.9f, 8.0f, -270.0f), new Vector3(680.6f, 8.0f, -277.6f),
                    new Vector3(677.7f, 8.0f, -285.1f), new Vector3(674.4f, 8.0f, -292.4f), new Vector3(670.6f, 8.0f, -299.5f),
                    new Vector3(667.5f, 8.0f, -304.6f), new Vector3(664.1f, 8.0f, -309.5f), new Vector3(660.3f, 7.9f, -314.2f),
                    new Vector3(656.2f, 7.9f, -318.6f), new Vector3(651.9f, 7.9f, -322.7f), new Vector3(647.2f, 7.9f, -326.4f),
                    new Vector3(640.6f, 7.9f, -331.0f), new Vector3(633.7f, 7.9f, -335.1f), new Vector3(628.4f, 7.9f, -337.8f),
                    new Vector3(622.9f, 7.9f, -340.2f), new Vector3(617.1f, 7.8f, -342.0f), new Vector3(611.2f, 7.8f, -342.9f),
                    new Vector3(595.3f, 7.8f, -344.1f), new Vector3(525.3f, 7.6f, -345.4f), new Vector3(455.2f, 7.3f, -345.3f),
                    new Vector3(385.2f, 7.1f, -344.9f), new Vector3(329.2f, 6.8f, -344.2f), new Vector3(321.2f, 6.8f, -344.4f),
                    new Vector3(315.2f, 6.8f, -345.1f), new Vector3(308.1f, 6.8f, -348.4f), new Vector3(304.4f, 6.7f, -355.4f),
                    new Vector3(304.5f, 6.7f, -363.3f), new Vector3(306.1f, 6.7f, -369.1f), new Vector3(308.5f, 6.7f, -374.6f),
                    new Vector3(311.8f, 6.6f, -381.9f), new Vector3(313.9f, 6.6f, -387.6f), new Vector3(315.3f, 6.6f, -393.4f),
                    new Vector3(315.7f, 6.6f, -399.4f), new Vector3(313.6f, 6.4f, -447.3f), new Vector3(311.2f, 6.3f, -477.2f),
                    new Vector3(309.6f, 6.3f, -489.1f), new Vector3(308.3f, 6.3f, -495.0f), new Vector3(304.7f, 6.2f, -501.9f),
                    new Vector3(297.1f, 6.2f, -504.4f), new Vector3(287.2f, 6.2f, -506.2f), new Vector3(255.5f, 6.1f, -509.9f),
                    new Vector3(185.6f, 6.0f, -515.0f), new Vector3(115.6f, 6.0f, -516.4f), new Vector3(45.6f, 5.6f, -515.2f),
                    new Vector3(29.6f, 5.5f, -514.2f), new Vector3(23.7f, 5.4f, -513.4f), new Vector3(17.9f, 5.4f, -511.8f),
                    new Vector3(12.5f, 5.3f, -509.2f), new Vector3(6.4f, 5.3f, -504.0f), new Vector3(2.9f, 5.2f, -499.2f),
                    new Vector3(0.2f, 5.1f, -493.8f), new Vector3(-1.5f, 5.1f, -488.1f), new Vector3(-2.3f, 5.0f, -482.1f),
                    new Vector3(-2.5f, 4.2f, -412.1f), new Vector3(-2.1f, 3.3f, -342.1f), new Vector3(-1.7f, 2.3f, -272.1f),
                    new Vector3(-1.3f, 1.4f, -202.1f), new Vector3(-0.8f, 0.6f, -132.0f), new Vector3(-0.4f, 0.2f, -62.0f)
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
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 70.0f), new Vector3(-0.5f, 0.0f, 88.0f),
                    new Vector3(-2.1f, 0.0f, 95.8f), new Vector3(-6.4f, 0.0f, 102.5f), new Vector3(-12.5f, 0.0f, 107.6f),
                    new Vector3(-19.9f, 0.0f, 110.6f), new Vector3(-27.7f, 0.0f, 112.1f), new Vector3(-59.7f, 0.0f, 114.1f),
                    new Vector3(-107.6f, 0.0f, 115.2f), new Vector3(-177.6f, 0.0f, 112.3f), new Vector3(-247.5f, 0.0f, 109.3f),
                    new Vector3(-317.5f, 0.0f, 108.2f), new Vector3(-329.5f, 0.0f, 107.5f), new Vector3(-337.2f, 0.0f, 105.9f),
                    new Vector3(-342.6f, 0.0f, 100.0f), new Vector3(-344.0f, 0.0f, 92.2f), new Vector3(-345.9f, 0.0f, 62.2f),
                    new Vector3(-348.3f, 0.0f, -7.7f), new Vector3(-350.7f, 0.0f, -77.7f), new Vector3(-353.4f, 0.0f, -147.6f),
                    new Vector3(-355.5f, 0.0f, -217.6f), new Vector3(-356.3f, 0.0f, -287.6f), new Vector3(-356.1f, 0.0f, -357.6f),
                    new Vector3(-355.3f, 0.0f, -427.6f), new Vector3(-354.3f, 0.0f, -497.6f), new Vector3(-353.2f, 0.0f, -567.6f),
                    new Vector3(-351.9f, 0.0f, -637.6f), new Vector3(-349.8f, 0.0f, -707.5f), new Vector3(-347.8f, 0.0f, -737.5f),
                    new Vector3(-346.7f, 0.0f, -745.4f), new Vector3(-343.6f, 0.0f, -752.7f), new Vector3(-338.1f, 0.0f, -758.4f),
                    new Vector3(-330.9f, 0.0f, -761.7f), new Vector3(-323.0f, 0.0f, -763.0f), new Vector3(-301.1f, 0.0f, -765.1f),
                    new Vector3(-231.2f, 0.0f, -768.9f), new Vector3(-161.3f, 0.0f, -772.4f), new Vector3(-137.4f, 0.0f, -774.3f),
                    new Vector3(-129.5f, 0.0f, -775.8f), new Vector3(-123.2f, 0.0f, -780.5f), new Vector3(-119.6f, 0.0f, -787.6f),
                    new Vector3(-118.3f, 0.0f, -795.5f), new Vector3(-116.7f, 0.0f, -813.4f), new Vector3(-113.9f, 0.0f, -883.3f),
                    new Vector3(-109.9f, 0.0f, -953.2f), new Vector3(-103.3f, 0.0f, -1010.8f), new Vector3(-92.5f, 0.0f, -1080.0f),
                    new Vector3(-88.3f, 0.0f, -1101.6f), new Vector3(-85.8f, 0.0f, -1109.1f), new Vector3(-79.1f, 0.0f, -1113.2f),
                    new Vector3(-71.1f, 0.0f, -1114.0f), new Vector3(-51.1f, 0.0f, -1114.1f), new Vector3(-43.1f, 0.0f, -1114.3f),
                    new Vector3(-35.2f, 0.0f, -1115.3f), new Vector3(-28.8f, 0.0f, -1119.8f), new Vector3(-25.7f, 0.0f, -1127.2f),
                    new Vector3(-14.3f, 0.0f, -1163.4f), new Vector3(3.6f, 0.0f, -1231.1f), new Vector3(20.9f, 0.0f, -1298.9f),
                    new Vector3(38.3f, 0.0f, -1366.7f), new Vector3(56.6f, 0.0f, -1434.3f), new Vector3(65.6f, 0.0f, -1462.9f),
                    new Vector3(68.3f, 0.0f, -1472.5f), new Vector3(69.4f, 0.0f, -1480.4f), new Vector3(68.2f, 0.0f, -1488.3f),
                    new Vector3(64.0f, 0.0f, -1495.0f), new Vector3(57.2f, 0.0f, -1499.3f), new Vector3(49.4f, 0.0f, -1500.5f),
                    new Vector3(41.4f, 0.0f, -1500.6f), new Vector3(3.4f, 0.0f, -1498.8f), new Vector3(-66.4f, 0.0f, -1493.4f),
                    new Vector3(-116.3f, 0.0f, -1490.3f), new Vector3(-124.3f, 0.0f, -1490.4f), new Vector3(-131.0f, 0.0f, -1494.6f),
                    new Vector3(-135.4f, 0.0f, -1501.2f), new Vector3(-139.0f, 0.0f, -1508.3f), new Vector3(-144.2f, 0.0f, -1514.4f),
                    new Vector3(-150.5f, 0.0f, -1519.4f), new Vector3(-156.8f, 0.0f, -1524.2f), new Vector3(-162.6f, 0.0f, -1529.7f),
                    new Vector3(-167.0f, 0.0f, -1536.4f), new Vector3(-170.3f, 0.0f, -1543.7f), new Vector3(-173.6f, 0.0f, -1553.1f),
                    new Vector3(-177.4f, 0.0f, -1560.1f), new Vector3(-183.1f, 0.0f, -1565.7f), new Vector3(-190.2f, 0.0f, -1569.2f),
                    new Vector3(-198.2f, 0.0f, -1569.7f), new Vector3(-208.2f, 0.0f, -1569.1f), new Vector3(-230.1f, 0.0f, -1567.0f),
                    new Vector3(-238.1f, 0.0f, -1566.8f), new Vector3(-245.7f, 0.0f, -1569.0f), new Vector3(-252.1f, 0.0f, -1573.7f),
                    new Vector3(-256.1f, 0.0f, -1580.6f), new Vector3(-257.6f, 0.0f, -1588.4f), new Vector3(-260.3f, 0.0f, -1612.2f),
                    new Vector3(-262.9f, 0.0f, -1652.2f), new Vector3(-263.2f, 0.0f, -1710.1f), new Vector3(-260.4f, 0.0f, -1780.1f),
                    new Vector3(-255.1f, 0.0f, -1849.9f), new Vector3(-246.2f, 0.0f, -1919.3f), new Vector3(-242.9f, 0.0f, -1939.0f),
                    new Vector3(-241.0f, 0.0f, -1946.8f), new Vector3(-238.3f, 0.0f, -1954.3f), new Vector3(-235.0f, 0.0f, -1961.6f),
                    new Vector3(-231.2f, 0.0f, -1968.6f), new Vector3(-226.8f, 0.0f, -1975.3f), new Vector3(-222.0f, 0.0f, -1981.7f),
                    new Vector3(-216.9f, 0.0f, -1987.9f), new Vector3(-211.3f, 0.0f, -1993.6f), new Vector3(-187.6f, 0.0f, -2015.1f),
                    new Vector3(-134.1f, 0.0f, -2060.3f), new Vector3(-82.9f, 0.0f, -2101.9f), new Vector3(-76.3f, 0.0f, -2106.4f),
                    new Vector3(-69.1f, 0.0f, -2109.8f), new Vector3(-3.5f, 0.0f, -2134.4f), new Vector3(62.4f, 0.0f, -2157.8f),
                    new Vector3(128.7f, 0.0f, -2180.2f), new Vector3(136.5f, 0.0f, -2182.1f), new Vector3(144.5f, 0.0f, -2181.8f),
                    new Vector3(152.2f, 0.0f, -2179.7f), new Vector3(159.0f, 0.0f, -2175.5f), new Vector3(186.3f, 0.0f, -2155.3f),
                    new Vector3(241.2f, 0.0f, -2111.9f), new Vector3(295.6f, 0.0f, -2067.8f), new Vector3(345.9f, 0.0f, -2025.0f),
                    new Vector3(373.3f, 0.0f, -1998.8f), new Vector3(382.8f, 0.0f, -1988.5f), new Vector3(387.9f, 0.0f, -1982.3f),
                    new Vector3(392.3f, 0.0f, -1975.6f), new Vector3(395.5f, 0.0f, -1968.3f), new Vector3(397.7f, 0.0f, -1960.6f),
                    new Vector3(398.6f, 0.0f, -1952.7f), new Vector3(397.9f, 0.0f, -1944.7f), new Vector3(395.9f, 0.0f, -1937.0f),
                    new Vector3(392.6f, 0.0f, -1929.7f), new Vector3(386.9f, 0.0f, -1921.5f), new Vector3(344.2f, 0.0f, -1866.0f),
                    new Vector3(305.0f, 0.0f, -1815.5f), new Vector3(299.4f, 0.0f, -1807.1f), new Vector3(295.5f, 0.0f, -1800.2f),
                    new Vector3(292.3f, 0.0f, -1792.8f), new Vector3(268.5f, 0.0f, -1727.0f), new Vector3(245.7f, 0.0f, -1660.8f),
                    new Vector3(222.2f, 0.0f, -1594.9f), new Vector3(216.3f, 0.0f, -1580.0f), new Vector3(212.6f, 0.0f, -1573.0f),
                    new Vector3(208.0f, 0.0f, -1566.4f), new Vector3(202.7f, 0.0f, -1560.4f), new Vector3(196.6f, 0.0f, -1555.3f),
                    new Vector3(181.7f, 0.0f, -1545.1f), new Vector3(122.7f, 0.0f, -1507.5f), new Vector3(109.5f, 0.0f, -1498.5f),
                    new Vector3(103.2f, 0.0f, -1493.5f), new Vector3(97.7f, 0.0f, -1487.7f), new Vector3(92.8f, 0.0f, -1481.3f),
                    new Vector3(88.6f, 0.0f, -1474.5f), new Vector3(85.1f, 0.0f, -1467.4f), new Vector3(82.3f, 0.0f, -1459.9f),
                    new Vector3(64.5f, 0.0f, -1392.2f), new Vector3(47.1f, 0.0f, -1324.4f), new Vector3(29.9f, 0.0f, -1256.5f),
                    new Vector3(13.0f, 0.0f, -1188.6f), new Vector3(4.5f, 0.0f, -1151.6f), new Vector3(1.8f, 0.0f, -1133.8f),
                    new Vector3(-1.2f, 0.0f, -1103.9f), new Vector3(-3.0f, 0.0f, -1068.0f), new Vector3(-2.7f, 0.0f, -998.0f),
                    new Vector3(-2.6f, 0.0f, -928.0f), new Vector3(-3.8f, 0.0f, -858.0f), new Vector3(-4.1f, 0.0f, -788.0f),
                    new Vector3(-3.9f, 0.0f, -718.0f), new Vector3(-3.6f, 0.0f, -648.0f), new Vector3(-3.7f, 0.0f, -578.0f),
                    new Vector3(-4.3f, 0.0f, -508.0f), new Vector3(-4.2f, 0.0f, -438.0f), new Vector3(-3.8f, 0.0f, -368.0f),
                    new Vector3(-3.2f, 0.0f, -298.0f), new Vector3(-2.5f, 0.0f, -228.0f), new Vector3(-1.6f, 0.0f, -158.0f),
                    new Vector3(-0.7f, 0.0f, -88.0f), new Vector3(-0.1f, 0.0f, -18.0f)
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 4.8f, 70.0f), new Vector3(-1.3f, 15.4f, 140.0f),
                    new Vector3(-4.1f, 21.1f, 175.9f), new Vector3(-8.4f, 25.5f, 207.6f), new Vector3(-9.8f, 26.4f, 215.5f),
                    new Vector3(-11.8f, 27.2f, 223.3f), new Vector3(-15.6f, 28.0f, 230.1f), new Vector3(-22.7f, 28.6f, 233.7f),
                    new Vector3(-30.6f, 29.1f, 233.3f), new Vector3(-37.3f, 29.5f, 229.1f), new Vector3(-47.3f, 30.0f, 214.2f),
                    new Vector3(-85.1f, 29.2f, 155.2f), new Vector3(-123.3f, 27.1f, 96.6f), new Vector3(-128.0f, 26.8f, 90.1f),
                    new Vector3(-131.9f, 26.6f, 85.5f), new Vector3(-136.1f, 26.3f, 81.2f), new Vector3(-140.5f, 26.1f, 77.2f),
                    new Vector3(-148.4f, 25.7f, 71.0f), new Vector3(-153.2f, 25.5f, 67.5f), new Vector3(-158.3f, 25.3f, 64.3f),
                    new Vector3(-163.6f, 25.0f, 61.5f), new Vector3(-172.7f, 24.7f, 57.3f), new Vector3(-180.2f, 24.3f, 54.4f),
                    new Vector3(-185.9f, 24.1f, 52.6f), new Vector3(-191.7f, 23.9f, 51.2f), new Vector3(-201.6f, 23.5f, 49.6f),
                    new Vector3(-211.5f, 23.1f, 48.5f), new Vector3(-219.5f, 22.9f, 48.2f), new Vector3(-227.5f, 22.6f, 48.5f),
                    new Vector3(-233.5f, 22.4f, 49.0f), new Vector3(-239.4f, 22.2f, 50.1f), new Vector3(-245.2f, 22.0f, 51.6f),
                    new Vector3(-260.4f, 21.5f, 56.6f), new Vector3(-326.3f, 20.1f, 80.3f), new Vector3(-392.2f, 19.7f, 103.9f),
                    new Vector3(-418.8f, 19.3f, 112.8f), new Vector3(-424.6f, 19.1f, 114.4f), new Vector3(-430.5f, 19.0f, 115.2f),
                    new Vector3(-436.5f, 18.9f, 115.4f), new Vector3(-452.5f, 18.5f, 114.9f), new Vector3(-458.4f, 18.3f, 114.1f),
                    new Vector3(-466.2f, 18.1f, 112.3f), new Vector3(-481.5f, 17.7f, 107.6f), new Vector3(-502.4f, 17.0f, 100.8f),
                    new Vector3(-508.3f, 16.8f, 99.4f), new Vector3(-514.2f, 16.6f, 98.6f), new Vector3(-526.2f, 16.2f, 98.4f),
                    new Vector3(-532.2f, 16.0f, 98.8f), new Vector3(-538.1f, 15.8f, 100.0f), new Vector3(-545.6f, 15.6f, 102.7f),
                    new Vector3(-551.1f, 15.4f, 105.0f), new Vector3(-558.2f, 15.1f, 108.7f), new Vector3(-575.7f, 14.4f, 118.5f),
                    new Vector3(-586.4f, 14.0f, 123.8f), new Vector3(-592.0f, 13.8f, 126.2f), new Vector3(-597.6f, 13.6f, 128.2f),
                    new Vector3(-603.4f, 13.4f, 129.6f), new Vector3(-609.4f, 13.2f, 130.5f), new Vector3(-615.4f, 13.0f, 130.5f),
                    new Vector3(-621.3f, 12.8f, 129.9f), new Vector3(-627.2f, 12.7f, 128.6f), new Vector3(-632.7f, 12.5f, 126.3f),
                    new Vector3(-659.0f, 11.6f, 111.8f), new Vector3(-671.1f, 11.3f, 104.8f), new Vector3(-680.0f, 11.1f, 100.3f),
                    new Vector3(-685.6f, 10.9f, 98.0f), new Vector3(-691.3f, 10.8f, 96.2f), new Vector3(-699.1f, 10.7f, 94.3f),
                    new Vector3(-705.0f, 10.6f, 93.3f), new Vector3(-710.9f, 10.5f, 92.6f), new Vector3(-718.9f, 10.3f, 92.2f),
                    new Vector3(-726.9f, 10.2f, 92.2f), new Vector3(-732.9f, 10.2f, 92.6f), new Vector3(-738.9f, 10.1f, 93.4f),
                    new Vector3(-748.7f, 10.1f, 95.4f), new Vector3(-758.4f, 10.0f, 97.9f), new Vector3(-766.0f, 10.0f, 100.3f),
                    new Vector3(-773.5f, 10.0f, 103.1f), new Vector3(-782.6f, 10.0f, 107.3f), new Vector3(-789.7f, 10.0f, 111.0f),
                    new Vector3(-794.8f, 9.9f, 114.0f), new Vector3(-799.8f, 9.9f, 117.4f), new Vector3(-804.5f, 9.9f, 121.2f),
                    new Vector3(-808.8f, 9.9f, 125.3f), new Vector3(-812.8f, 9.8f, 129.8f), new Vector3(-816.5f, 9.8f, 134.5f),
                    new Vector3(-819.7f, 9.8f, 139.6f), new Vector3(-822.2f, 9.7f, 145.0f), new Vector3(-824.3f, 9.7f, 150.7f),
                    new Vector3(-834.9f, 9.3f, 191.3f), new Vector3(-851.7f, 8.3f, 259.3f), new Vector3(-855.1f, 8.2f, 266.5f),
                    new Vector3(-860.6f, 8.1f, 272.3f), new Vector3(-867.7f, 8.0f, 275.9f), new Vector3(-878.9f, 7.8f, 280.2f),
                    new Vector3(-888.5f, 7.6f, 283.2f), new Vector3(-902.0f, 7.4f, 286.6f), new Vector3(-915.8f, 7.2f, 289.2f),
                    new Vector3(-925.7f, 7.0f, 290.6f), new Vector3(-935.7f, 6.9f, 291.4f), new Vector3(-947.7f, 6.7f, 291.5f),
                    new Vector3(-971.7f, 6.3f, 291.3f), new Vector3(-979.7f, 6.1f, 291.8f), new Vector3(-985.6f, 6.0f, 292.6f),
                    new Vector3(-991.5f, 6.0f, 293.9f), new Vector3(-997.1f, 5.9f, 295.8f), new Vector3(-1002.5f, 5.8f, 298.4f),
                    new Vector3(-1007.7f, 5.7f, 301.5f), new Vector3(-1012.5f, 5.6f, 305.1f), new Vector3(-1016.7f, 5.5f, 309.4f),
                    new Vector3(-1020.4f, 5.4f, 314.1f), new Vector3(-1023.5f, 5.3f, 319.2f), new Vector3(-1026.0f, 5.3f, 324.7f),
                    new Vector3(-1028.1f, 5.2f, 330.3f), new Vector3(-1029.6f, 5.1f, 336.1f), new Vector3(-1030.4f, 5.0f, 342.0f),
                    new Vector3(-1030.5f, 4.9f, 348.0f), new Vector3(-1029.9f, 4.9f, 354.0f), new Vector3(-1029.0f, 4.8f, 359.9f),
                    new Vector3(-1027.5f, 4.7f, 365.8f), new Vector3(-1025.6f, 4.7f, 371.4f), new Vector3(-1022.6f, 4.6f, 378.9f),
                    new Vector3(-1020.7f, 4.5f, 384.5f), new Vector3(-1019.8f, 4.5f, 390.5f), new Vector3(-1021.0f, 4.4f, 398.3f),
                    new Vector3(-1025.2f, 4.3f, 405.0f), new Vector3(-1041.9f, 4.2f, 419.3f), new Vector3(-1096.1f, 4.0f, 463.7f),
                    new Vector3(-1150.5f, 4.5f, 507.7f), new Vector3(-1166.3f, 4.7f, 520.0f), new Vector3(-1171.4f, 4.8f, 523.2f),
                    new Vector3(-1176.8f, 4.9f, 525.8f), new Vector3(-1184.6f, 5.0f, 527.3f), new Vector3(-1254.6f, 6.3f, 529.0f),
                    new Vector3(-1324.6f, 7.7f, 530.4f), new Vector3(-1394.6f, 9.2f, 531.8f), new Vector3(-1464.6f, 10.6f, 533.1f),
                    new Vector3(-1524.6f, 11.5f, 533.1f), new Vector3(-1538.6f, 11.6f, 532.2f), new Vector3(-1546.5f, 11.7f, 531.2f),
                    new Vector3(-1556.4f, 11.8f, 529.5f), new Vector3(-1562.2f, 11.8f, 528.2f), new Vector3(-1569.2f, 11.9f, 524.4f),
                    new Vector3(-1572.1f, 11.9f, 517.2f), new Vector3(-1571.9f, 11.9f, 511.2f), new Vector3(-1569.3f, 12.0f, 503.8f),
                    new Vector3(-1563.8f, 12.0f, 498.0f), new Vector3(-1559.0f, 12.0f, 494.4f), new Vector3(-1501.0f, 12.3f, 455.1f),
                    new Vector3(-1442.9f, 13.2f, 416.0f), new Vector3(-1385.3f, 14.5f, 376.2f), new Vector3(-1328.5f, 16.0f, 335.3f),
                    new Vector3(-1272.4f, 17.4f, 293.4f), new Vector3(-1217.1f, 18.8f, 250.4f), new Vector3(-1162.7f, 19.7f, 206.4f),
                    new Vector3(-1109.0f, 20.0f, 161.4f), new Vector3(-1055.9f, 19.7f, 115.8f), new Vector3(-1003.5f, 19.0f, 69.4f),
                    new Vector3(-952.0f, 18.0f, 21.9f), new Vector3(-901.5f, 16.9f, -26.5f), new Vector3(-851.8f, 15.8f, -75.9f),
                    new Vector3(-802.5f, 14.8f, -125.6f), new Vector3(-753.2f, 14.2f, -175.3f), new Vector3(-703.7f, 14.0f, -224.8f),
                    new Vector3(-683.5f, 13.8f, -244.2f), new Vector3(-674.3f, 13.7f, -251.9f), new Vector3(-667.2f, 13.6f, -255.2f),
                    new Vector3(-660.4f, 13.5f, -251.4f), new Vector3(-657.8f, 13.4f, -243.9f), new Vector3(-654.4f, 13.1f, -226.2f),
                    new Vector3(-646.3f, 12.4f, -193.2f), new Vector3(-627.3f, 10.5f, -125.8f), new Vector3(-614.4f, 9.2f, -85.8f),
                    new Vector3(-605.2f, 8.4f, -61.5f), new Vector3(-601.3f, 8.1f, -54.6f), new Vector3(-594.6f, 7.8f, -50.2f),
                    new Vector3(-587.0f, 7.6f, -47.8f), new Vector3(-579.1f, 7.3f, -48.7f), new Vector3(-572.0f, 7.1f, -52.2f),
                    new Vector3(-567.5f, 6.9f, -56.2f), new Vector3(-563.4f, 6.7f, -60.5f), new Vector3(-558.5f, 6.4f, -66.9f),
                    new Vector3(-538.3f, 5.3f, -96.7f), new Vector3(-533.1f, 5.0f, -105.2f), new Vector3(-530.6f, 4.8f, -112.7f),
                    new Vector3(-531.1f, 4.6f, -118.7f), new Vector3(-532.3f, 4.5f, -124.5f), new Vector3(-534.5f, 4.3f, -130.1f),
                    new Vector3(-538.3f, 4.1f, -137.2f), new Vector3(-554.8f, 3.4f, -164.6f), new Vector3(-557.5f, 3.2f, -170.0f),
                    new Vector3(-559.6f, 3.1f, -175.6f), new Vector3(-561.0f, 3.0f, -181.4f), new Vector3(-564.6f, 2.6f, -205.2f),
                    new Vector3(-567.9f, 2.2f, -233.0f), new Vector3(-567.5f, 2.2f, -240.9f), new Vector3(-565.5f, 2.1f, -246.6f),
                    new Vector3(-562.4f, 2.1f, -251.7f), new Vector3(-544.8f, 2.0f, -278.4f), new Vector3(-539.4f, 2.0f, -284.3f),
                    new Vector3(-531.7f, 2.0f, -286.0f), new Vector3(-524.2f, 2.1f, -283.7f), new Vector3(-519.5f, 2.1f, -277.4f),
                    new Vector3(-493.3f, 2.6f, -212.5f), new Vector3(-467.1f, 3.5f, -147.5f), new Vector3(-451.8f, 4.0f, -112.7f),
                    new Vector3(-447.3f, 4.2f, -103.8f), new Vector3(-444.1f, 4.3f, -98.7f), new Vector3(-440.1f, 4.4f, -94.3f),
                    new Vector3(-435.6f, 4.5f, -90.3f), new Vector3(-430.8f, 4.6f, -86.7f), new Vector3(-425.7f, 4.7f, -83.5f),
                    new Vector3(-420.4f, 4.8f, -80.7f), new Vector3(-411.3f, 4.9f, -76.5f), new Vector3(-402.0f, 5.1f, -72.9f),
                    new Vector3(-386.8f, 5.4f, -68.0f), new Vector3(-371.3f, 5.6f, -64.0f), new Vector3(-363.5f, 5.8f, -62.4f),
                    new Vector3(-357.5f, 5.8f, -61.6f), new Vector3(-351.5f, 5.9f, -61.9f), new Vector3(-345.6f, 6.0f, -63.0f),
                    new Vector3(-339.9f, 6.1f, -64.7f), new Vector3(-334.3f, 6.2f, -66.8f), new Vector3(-321.7f, 6.4f, -72.9f),
                    new Vector3(-273.8f, 7.1f, -97.9f), new Vector3(-268.6f, 7.2f, -101.0f), new Vector3(-263.9f, 7.3f, -104.7f),
                    new Vector3(-259.6f, 7.3f, -108.9f), new Vector3(-255.8f, 7.4f, -113.5f), new Vector3(-248.9f, 7.5f, -123.3f),
                    new Vector3(-240.4f, 7.7f, -136.9f), new Vector3(-236.5f, 7.7f, -143.9f), new Vector3(-233.9f, 7.8f, -149.3f),
                    new Vector3(-230.9f, 7.8f, -156.7f), new Vector3(-225.7f, 7.9f, -171.8f), new Vector3(-223.0f, 7.9f, -181.5f),
                    new Vector3(-221.3f, 8.0f, -189.3f), new Vector3(-219.8f, 8.0f, -199.2f), new Vector3(-218.9f, 8.0f, -209.1f),
                    new Vector3(-218.7f, 8.0f, -215.1f), new Vector3(-219.4f, 8.0f, -221.1f), new Vector3(-221.0f, 8.0f, -226.8f),
                    new Vector3(-233.4f, 8.1f, -267.0f), new Vector3(-252.4f, 8.3f, -334.4f), new Vector3(-268.9f, 8.6f, -402.4f),
                    new Vector3(-271.8f, 8.7f, -416.1f), new Vector3(-272.0f, 8.7f, -424.1f), new Vector3(-270.4f, 8.7f, -429.8f),
                    new Vector3(-268.0f, 8.8f, -435.3f), new Vector3(-264.8f, 8.8f, -440.4f), new Vector3(-260.7f, 8.8f, -444.8f),
                    new Vector3(-256.1f, 8.9f, -448.7f), new Vector3(-244.7f, 8.9f, -456.7f), new Vector3(-232.8f, 9.0f, -464.1f),
                    new Vector3(-171.1f, 9.4f, -497.2f), new Vector3(-109.2f, 9.7f, -530.0f), new Vector3(-47.2f, 9.9f, -562.4f),
                    new Vector3(-29.2f, 10.0f, -571.2f), new Vector3(-23.5f, 10.0f, -573.0f), new Vector3(-15.5f, 10.0f, -573.4f),
                    new Vector3(-9.7f, 10.0f, -572.0f), new Vector3(-3.2f, 10.0f, -567.6f), new Vector3(-0.6f, 10.0f, -560.1f),
                    new Vector3(0.1f, 10.0f, -554.1f), new Vector3(1.0f, 9.2f, -484.1f), new Vector3(1.2f, 7.4f, -414.1f),
                    new Vector3(1.0f, 5.2f, -344.1f), new Vector3(0.7f, 3.5f, -274.1f), new Vector3(0.3f, 3.0f, -204.0f),
                    new Vector3(0.1f, 2.0f, -134.0f), new Vector3(-0.1f, 0.6f, -64.0f)
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
                // Real activation-zone count: THREE.
                DrsZoneThreeNormalized = new Vector2(0.7f, 0.8f),
                TargetLengthMeters = 4304f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 70.0f), new Vector3(-0.6f, 0.0f, 88.0f),
                    new Vector3(-1.3f, 0.0f, 96.0f), new Vector3(-2.1f, 0.0f, 101.9f), new Vector3(-3.4f, 0.0f, 107.8f),
                    new Vector3(-5.0f, 0.0f, 113.6f), new Vector3(-7.0f, 0.0f, 119.2f), new Vector3(-9.4f, 0.0f, 124.7f),
                    new Vector3(-12.1f, 0.0f, 130.1f), new Vector3(-16.1f, 0.0f, 137.0f), new Vector3(-20.6f, 0.0f, 143.6f),
                    new Vector3(-25.4f, 0.0f, 150.0f), new Vector3(-29.3f, 0.0f, 154.6f), new Vector3(-33.5f, 0.0f, 158.9f),
                    new Vector3(-37.9f, 0.0f, 163.0f), new Vector3(-42.6f, 0.0f, 166.7f), new Vector3(-47.5f, 0.0f, 170.1f),
                    new Vector3(-52.6f, 0.0f, 173.3f), new Vector3(-63.3f, 0.0f, 178.8f), new Vector3(-76.0f, 0.0f, 184.7f),
                    new Vector3(-83.4f, 0.0f, 187.6f), new Vector3(-91.1f, 0.0f, 189.9f), new Vector3(-98.8f, 0.0f, 191.9f),
                    new Vector3(-108.7f, 0.0f, 193.7f), new Vector3(-116.6f, 0.0f, 194.8f), new Vector3(-122.6f, 0.0f, 195.1f),
                    new Vector3(-128.6f, 0.0f, 194.9f), new Vector3(-135.8f, 0.0f, 191.7f), new Vector3(-141.1f, 0.0f, 185.8f),
                    new Vector3(-143.1f, 0.0f, 178.1f), new Vector3(-143.3f, 0.0f, 172.1f), new Vector3(-142.5f, 0.0f, 130.1f),
                    new Vector3(-142.6f, 0.0f, 116.1f), new Vector3(-143.2f, 0.0f, 110.1f), new Vector3(-144.4f, 0.0f, 104.3f),
                    new Vector3(-149.4f, 0.0f, 87.0f), new Vector3(-150.6f, 0.0f, 81.1f), new Vector3(-150.0f, 0.0f, 73.2f),
                    new Vector3(-146.5f, 0.0f, 66.0f), new Vector3(-141.1f, 0.0f, 60.2f), new Vector3(-136.0f, 0.0f, 57.0f),
                    new Vector3(-127.1f, 0.0f, 52.5f), new Vector3(-121.8f, 0.0f, 49.6f), new Vector3(-116.8f, 0.0f, 46.3f),
                    new Vector3(-111.2f, 0.0f, 40.6f), new Vector3(-109.1f, 0.0f, 33.0f), new Vector3(-111.2f, 0.0f, 25.4f),
                    new Vector3(-116.3f, 0.0f, 19.4f), new Vector3(-123.7f, 0.0f, 16.3f), new Vector3(-164.2f, 0.0f, 5.4f),
                    new Vector3(-232.6f, 0.0f, -9.9f), new Vector3(-267.8f, 0.0f, -17.4f), new Vector3(-275.1f, 0.0f, -20.6f),
                    new Vector3(-280.7f, 0.0f, -26.2f), new Vector3(-282.2f, 0.0f, -34.0f), new Vector3(-282.7f, 0.0f, -104.0f),
                    new Vector3(-282.9f, 0.0f, -174.0f), new Vector3(-283.0f, 0.0f, -244.0f), new Vector3(-283.3f, 0.0f, -314.0f),
                    new Vector3(-284.6f, 0.0f, -384.0f), new Vector3(-285.5f, 0.0f, -396.0f), new Vector3(-287.3f, 0.0f, -409.9f),
                    new Vector3(-289.5f, 0.0f, -421.7f), new Vector3(-291.4f, 0.0f, -429.5f), new Vector3(-298.0f, 0.0f, -450.4f),
                    new Vector3(-321.7f, 0.0f, -516.3f), new Vector3(-338.8f, 0.0f, -561.2f), new Vector3(-342.2f, 0.0f, -568.5f),
                    new Vector3(-345.1f, 0.0f, -573.7f), new Vector3(-348.7f, 0.0f, -578.5f), new Vector3(-352.9f, 0.0f, -582.7f),
                    new Vector3(-357.7f, 0.0f, -586.4f), new Vector3(-362.8f, 0.0f, -589.5f), new Vector3(-403.9f, 0.0f, -610.2f),
                    new Vector3(-414.5f, 0.0f, -615.8f), new Vector3(-419.4f, 0.0f, -619.3f), new Vector3(-423.4f, 0.0f, -623.8f),
                    new Vector3(-426.8f, 0.0f, -628.7f), new Vector3(-429.5f, 0.0f, -634.0f), new Vector3(-431.2f, 0.0f, -639.8f),
                    new Vector3(-432.0f, 0.0f, -645.7f), new Vector3(-433.2f, 0.0f, -691.7f), new Vector3(-434.3f, 0.0f, -761.7f),
                    new Vector3(-435.4f, 0.0f, -785.7f), new Vector3(-436.0f, 0.0f, -791.7f), new Vector3(-437.3f, 0.0f, -797.5f),
                    new Vector3(-439.2f, 0.0f, -803.2f), new Vector3(-441.4f, 0.0f, -808.8f), new Vector3(-444.0f, 0.0f, -814.2f),
                    new Vector3(-447.0f, 0.0f, -819.4f), new Vector3(-450.5f, 0.0f, -824.3f), new Vector3(-454.4f, 0.0f, -828.9f),
                    new Vector3(-458.6f, 0.0f, -833.1f), new Vector3(-463.2f, 0.0f, -837.0f), new Vector3(-468.0f, 0.0f, -840.5f),
                    new Vector3(-474.7f, 0.0f, -844.9f), new Vector3(-490.3f, 0.0f, -853.9f), new Vector3(-498.7f, 0.0f, -859.3f),
                    new Vector3(-503.6f, 0.0f, -862.9f), new Vector3(-508.2f, 0.0f, -866.7f), new Vector3(-512.3f, 0.0f, -871.1f),
                    new Vector3(-517.1f, 0.0f, -877.5f), new Vector3(-523.9f, 0.0f, -887.4f), new Vector3(-526.9f, 0.0f, -892.5f),
                    new Vector3(-532.4f, 0.0f, -903.2f), new Vector3(-549.5f, 0.0f, -941.6f), new Vector3(-552.1f, 0.0f, -947.0f),
                    new Vector3(-555.3f, 0.0f, -952.1f), new Vector3(-559.3f, 0.0f, -956.5f), new Vector3(-564.1f, 0.0f, -960.2f),
                    new Vector3(-571.4f, 0.0f, -963.4f), new Vector3(-579.3f, 0.0f, -964.0f), new Vector3(-649.3f, 0.0f, -965.6f),
                    new Vector3(-719.3f, 0.0f, -966.8f), new Vector3(-789.4f, 0.0f, -968.0f), new Vector3(-859.4f, 0.0f, -969.4f),
                    new Vector3(-875.3f, 0.0f, -970.1f), new Vector3(-881.2f, 0.0f, -971.1f), new Vector3(-887.9f, 0.0f, -975.2f),
                    new Vector3(-890.9f, 0.0f, -980.4f), new Vector3(-893.3f, 0.0f, -985.9f), new Vector3(-895.5f, 0.0f, -991.5f),
                    new Vector3(-895.9f, 0.0f, -999.4f), new Vector3(-892.6f, 0.0f, -1006.7f), new Vector3(-887.5f, 0.0f, -1012.7f),
                    new Vector3(-838.1f, 0.0f, -1062.4f), new Vector3(-813.6f, 0.0f, -1086.0f), new Vector3(-806.9f, 0.0f, -1090.2f),
                    new Vector3(-799.0f, 0.0f, -1090.3f), new Vector3(-793.6f, 0.0f, -1084.8f), new Vector3(-780.5f, 0.0f, -1055.5f),
                    new Vector3(-773.8f, 0.0f, -1041.0f), new Vector3(-769.3f, 0.0f, -1034.5f), new Vector3(-762.3f, 0.0f, -1030.8f),
                    new Vector3(-754.3f, 0.0f, -1030.0f), new Vector3(-746.6f, 0.0f, -1031.8f), new Vector3(-741.0f, 0.0f, -1034.0f),
                    new Vector3(-677.1f, 0.0f, -1062.6f), new Vector3(-613.2f, 0.0f, -1091.3f), new Vector3(-549.3f, 0.0f, -1119.9f),
                    new Vector3(-485.1f, 0.0f, -1147.9f), new Vector3(-420.7f, 0.0f, -1175.2f), new Vector3(-356.0f, 0.0f, -1202.0f),
                    new Vector3(-291.1f, 0.0f, -1228.3f), new Vector3(-248.1f, 0.0f, -1244.7f), new Vector3(-230.9f, 0.0f, -1249.9f),
                    new Vector3(-209.5f, 0.0f, -1255.1f), new Vector3(-193.8f, 0.0f, -1258.1f), new Vector3(-177.9f, 0.0f, -1260.2f),
                    new Vector3(-158.0f, 0.0f, -1261.7f), new Vector3(-132.0f, 0.0f, -1262.1f), new Vector3(-114.0f, 0.0f, -1261.7f),
                    new Vector3(-106.4f, 0.0f, -1259.6f), new Vector3(-101.1f, 0.0f, -1253.7f), new Vector3(-98.9f, 0.0f, -1248.1f),
                    new Vector3(-89.8f, 0.0f, -1223.8f), new Vector3(-87.3f, 0.0f, -1218.3f), new Vector3(-81.5f, 0.0f, -1213.1f),
                    new Vector3(-73.6f, 0.0f, -1211.9f), new Vector3(-21.6f, 0.0f, -1211.7f), new Vector3(-9.6f, 0.0f, -1211.4f),
                    new Vector3(-2.1f, 0.0f, -1208.9f), new Vector3(3.6f, 0.0f, -1203.5f), new Vector3(6.2f, 0.0f, -1195.9f),
                    new Vector3(7.0f, 0.0f, -1190.0f), new Vector3(9.0f, 0.0f, -1160.1f), new Vector3(10.3f, 0.0f, -1090.1f),
                    new Vector3(10.8f, 0.0f, -1020.0f), new Vector3(11.1f, 0.0f, -950.0f), new Vector3(11.3f, 0.0f, -880.0f),
                    new Vector3(11.3f, 0.0f, -810.0f), new Vector3(11.0f, 0.0f, -740.0f), new Vector3(10.0f, 0.0f, -670.0f),
                    new Vector3(6.9f, 0.0f, -600.0f), new Vector3(3.8f, 0.0f, -530.1f), new Vector3(2.3f, 0.0f, -460.1f),
                    new Vector3(1.7f, 0.0f, -390.1f), new Vector3(1.3f, 0.0f, -320.1f), new Vector3(0.9f, 0.0f, -250.1f),
                    new Vector3(0.5f, 0.0f, -180.0f), new Vector3(0.2f, 0.0f, -110.0f), new Vector3(0.0f, 0.0f, -40.0f)
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
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 58.0f), new Vector3(-0.5f, 0.0f, 64.0f),
                    new Vector3(-1.4f, 0.0f, 69.9f), new Vector3(-3.6f, 0.0f, 77.6f), new Vector3(-7.1f, 0.0f, 84.8f),
                    new Vector3(-12.1f, 0.0f, 91.0f), new Vector3(-18.7f, 0.0f, 95.5f), new Vector3(-26.2f, 0.0f, 98.1f),
                    new Vector3(-34.1f, 0.0f, 99.1f), new Vector3(-42.1f, 0.0f, 98.1f), new Vector3(-47.9f, 0.0f, 96.6f),
                    new Vector3(-53.5f, 0.0f, 94.6f), new Vector3(-60.9f, 0.0f, 91.5f), new Vector3(-66.2f, 0.0f, 88.8f),
                    new Vector3(-71.4f, 0.0f, 85.8f), new Vector3(-76.4f, 0.0f, 82.4f), new Vector3(-81.2f, 0.0f, 78.8f),
                    new Vector3(-85.7f, 0.0f, 74.8f), new Vector3(-89.9f, 0.0f, 70.5f), new Vector3(-93.7f, 0.0f, 66.0f),
                    new Vector3(-97.3f, 0.0f, 61.1f), new Vector3(-101.3f, 0.0f, 54.2f), new Vector3(-103.7f, 0.0f, 48.7f),
                    new Vector3(-107.8f, 0.0f, 37.4f), new Vector3(-112.0f, 0.0f, 24.1f), new Vector3(-114.5f, 0.0f, 14.4f),
                    new Vector3(-116.4f, 0.0f, 4.6f), new Vector3(-118.1f, 0.0f, -7.3f), new Vector3(-120.2f, 0.0f, -33.2f),
                    new Vector3(-121.6f, 0.0f, -45.1f), new Vector3(-122.7f, 0.0f, -51.0f), new Vector3(-125.5f, 0.0f, -58.5f),
                    new Vector3(-128.3f, 0.0f, -63.8f), new Vector3(-131.5f, 0.0f, -68.9f), new Vector3(-137.4f, 0.0f, -77.0f),
                    new Vector3(-142.4f, 0.0f, -83.2f), new Vector3(-146.4f, 0.0f, -87.7f), new Vector3(-152.5f, 0.0f, -92.9f),
                    new Vector3(-159.3f, 0.0f, -97.0f), new Vector3(-166.5f, 0.0f, -100.5f), new Vector3(-177.6f, 0.0f, -105.1f),
                    new Vector3(-187.0f, 0.0f, -108.4f), new Vector3(-192.8f, 0.0f, -110.0f), new Vector3(-198.7f, 0.0f, -111.2f),
                    new Vector3(-204.6f, 0.0f, -112.0f), new Vector3(-210.6f, 0.0f, -112.5f), new Vector3(-216.6f, 0.0f, -112.5f),
                    new Vector3(-222.6f, 0.0f, -112.0f), new Vector3(-232.5f, 0.0f, -110.6f), new Vector3(-248.2f, 0.0f, -107.4f),
                    new Vector3(-257.8f, 0.0f, -104.8f), new Vector3(-265.4f, 0.0f, -102.3f), new Vector3(-271.0f, 0.0f, -100.1f),
                    new Vector3(-278.0f, 0.0f, -96.3f), new Vector3(-284.1f, 0.0f, -91.1f), new Vector3(-292.8f, 0.0f, -82.8f),
                    new Vector3(-341.0f, 0.0f, -32.0f), new Vector3(-388.4f, 0.0f, 19.4f), new Vector3(-435.6f, 0.0f, 71.1f),
                    new Vector3(-482.7f, 0.0f, 123.0f), new Vector3(-529.7f, 0.0f, 174.9f), new Vector3(-576.6f, 0.0f, 226.8f),
                    new Vector3(-623.5f, 0.0f, 278.8f), new Vector3(-670.3f, 0.0f, 330.8f), new Vector3(-717.1f, 0.0f, 382.9f),
                    new Vector3(-763.8f, 0.0f, 435.1f), new Vector3(-810.4f, 0.0f, 487.3f), new Vector3(-856.6f, 0.0f, 539.9f),
                    new Vector3(-868.1f, 0.0f, 553.8f), new Vector3(-871.6f, 0.0f, 560.8f), new Vector3(-871.3f, 0.0f, 568.7f),
                    new Vector3(-866.6f, 0.0f, 575.1f), new Vector3(-855.1f, 0.0f, 586.3f), new Vector3(-802.6f, 0.0f, 632.5f),
                    new Vector3(-749.4f, 0.0f, 678.1f), new Vector3(-706.0f, 0.0f, 716.5f), new Vector3(-696.2f, 0.0f, 726.5f),
                    new Vector3(-690.9f, 0.0f, 732.5f), new Vector3(-687.2f, 0.0f, 737.2f), new Vector3(-683.8f, 0.0f, 742.2f),
                    new Vector3(-680.8f, 0.0f, 747.4f), new Vector3(-677.9f, 0.0f, 754.8f), new Vector3(-676.8f, 0.0f, 762.7f),
                    new Vector3(-674.3f, 0.0f, 796.6f), new Vector3(-674.0f, 0.0f, 808.6f), new Vector3(-674.4f, 0.0f, 816.6f),
                    new Vector3(-675.2f, 0.0f, 824.6f), new Vector3(-676.9f, 0.0f, 834.4f), new Vector3(-679.2f, 0.0f, 844.2f),
                    new Vector3(-684.3f, 0.0f, 861.4f), new Vector3(-686.9f, 0.0f, 869.0f), new Vector3(-689.3f, 0.0f, 874.5f),
                    new Vector3(-693.0f, 0.0f, 881.6f), new Vector3(-699.1f, 0.0f, 891.9f), new Vector3(-710.3f, 0.0f, 908.5f),
                    new Vector3(-717.5f, 0.0f, 918.1f), new Vector3(-722.6f, 0.0f, 924.2f), new Vector3(-728.6f, 0.0f, 929.5f),
                    new Vector3(-736.2f, 0.0f, 931.3f), new Vector3(-743.7f, 0.0f, 928.4f), new Vector3(-748.8f, 0.0f, 925.4f),
                    new Vector3(-757.1f, 0.0f, 919.8f), new Vector3(-764.2f, 0.0f, 916.0f), new Vector3(-771.9f, 0.0f, 914.4f),
                    new Vector3(-779.8f, 0.0f, 915.6f), new Vector3(-787.2f, 0.0f, 918.6f), new Vector3(-793.9f, 0.0f, 923.0f),
                    new Vector3(-798.4f, 0.0f, 926.9f), new Vector3(-802.6f, 0.0f, 931.2f), new Vector3(-807.2f, 0.0f, 937.7f),
                    new Vector3(-809.9f, 0.0f, 945.2f), new Vector3(-812.5f, 0.0f, 954.9f), new Vector3(-819.4f, 0.0f, 986.1f),
                    new Vector3(-820.9f, 0.0f, 991.9f), new Vector3(-822.8f, 0.0f, 997.6f), new Vector3(-825.1f, 0.0f, 1005.3f),
                    new Vector3(-827.2f, 0.0f, 1013.0f), new Vector3(-830.3f, 0.0f, 1020.4f), new Vector3(-835.1f, 0.0f, 1026.7f),
                    new Vector3(-841.6f, 0.0f, 1031.4f), new Vector3(-849.1f, 0.0f, 1033.8f), new Vector3(-857.1f, 0.0f, 1033.9f),
                    new Vector3(-864.9f, 0.0f, 1032.1f), new Vector3(-872.3f, 0.0f, 1029.1f), new Vector3(-877.6f, 0.0f, 1026.3f),
                    new Vector3(-882.7f, 0.0f, 1023.1f), new Vector3(-890.8f, 0.0f, 1017.2f), new Vector3(-945.0f, 0.0f, 973.0f),
                    new Vector3(-998.7f, 0.0f, 928.0f), new Vector3(-1052.4f, 0.0f, 883.2f), new Vector3(-1107.0f, 0.0f, 839.3f),
                    new Vector3(-1150.2f, 0.0f, 807.0f), new Vector3(-1177.0f, 0.0f, 789.4f), new Vector3(-1195.9f, 0.0f, 778.2f),
                    new Vector3(-1206.5f, 0.0f, 772.7f), new Vector3(-1215.7f, 0.0f, 768.6f), new Vector3(-1223.2f, 0.0f, 766.0f),
                    new Vector3(-1231.0f, 0.0f, 764.2f), new Vector3(-1239.0f, 0.0f, 763.1f), new Vector3(-1246.9f, 0.0f, 762.5f),
                    new Vector3(-1256.9f, 0.0f, 762.4f), new Vector3(-1266.9f, 0.0f, 762.9f), new Vector3(-1276.9f, 0.0f, 763.9f),
                    new Vector3(-1286.7f, 0.0f, 765.6f), new Vector3(-1298.4f, 0.0f, 768.3f), new Vector3(-1311.8f, 0.0f, 772.3f),
                    new Vector3(-1326.9f, 0.0f, 777.8f), new Vector3(-1383.8f, 0.0f, 802.4f), new Vector3(-1447.4f, 0.0f, 831.6f),
                    new Vector3(-1466.0f, 0.0f, 839.0f), new Vector3(-1481.1f, 0.0f, 844.2f), new Vector3(-1488.8f, 0.0f, 846.3f),
                    new Vector3(-1496.6f, 0.0f, 847.9f), new Vector3(-1502.6f, 0.0f, 848.7f), new Vector3(-1512.6f, 0.0f, 849.3f),
                    new Vector3(-1524.6f, 0.0f, 849.4f), new Vector3(-1536.6f, 0.0f, 848.8f), new Vector3(-1546.5f, 0.0f, 847.8f),
                    new Vector3(-1556.4f, 0.0f, 846.1f), new Vector3(-1564.2f, 0.0f, 844.3f), new Vector3(-1571.9f, 0.0f, 842.2f),
                    new Vector3(-1579.4f, 0.0f, 839.6f), new Vector3(-1621.9f, 0.0f, 821.9f), new Vector3(-1647.2f, 0.0f, 809.9f),
                    new Vector3(-1673.5f, 0.0f, 795.6f), new Vector3(-1694.1f, 0.0f, 783.1f), new Vector3(-1699.1f, 0.0f, 779.8f),
                    new Vector3(-1704.6f, 0.0f, 774.1f), new Vector3(-1706.9f, 0.0f, 766.5f), new Vector3(-1705.4f, 0.0f, 758.7f),
                    new Vector3(-1703.1f, 0.0f, 753.1f), new Vector3(-1698.4f, 0.0f, 742.1f), new Vector3(-1696.4f, 0.0f, 736.5f),
                    new Vector3(-1678.5f, 0.0f, 668.8f), new Vector3(-1661.2f, 0.0f, 601.0f), new Vector3(-1644.7f, 0.0f, 532.9f),
                    new Vector3(-1629.4f, 0.0f, 464.6f), new Vector3(-1615.4f, 0.0f, 396.0f), new Vector3(-1606.7f, 0.0f, 361.1f),
                    new Vector3(-1591.6f, 0.0f, 311.3f), new Vector3(-1577.2f, 0.0f, 271.9f), new Vector3(-1552.9f, 0.0f, 217.0f),
                    new Vector3(-1523.1f, 0.0f, 153.7f), new Vector3(-1494.4f, 0.0f, 96.5f), new Vector3(-1466.2f, 0.0f, 48.1f),
                    new Vector3(-1427.9f, 0.0f, -10.5f), new Vector3(-1390.5f, 0.0f, -62.4f), new Vector3(-1346.4f, 0.0f, -116.8f),
                    new Vector3(-1300.3f, 0.0f, -169.4f), new Vector3(-1253.5f, 0.0f, -221.5f), new Vector3(-1206.7f, 0.0f, -273.6f),
                    new Vector3(-1160.0f, 0.0f, -325.8f), new Vector3(-1114.9f, 0.0f, -379.3f), new Vector3(-1070.2f, 0.0f, -433.2f),
                    new Vector3(-1025.7f, 0.0f, -487.2f), new Vector3(-981.1f, 0.0f, -541.2f), new Vector3(-936.3f, 0.0f, -595.0f),
                    new Vector3(-891.3f, 0.0f, -648.6f), new Vector3(-846.0f, 0.0f, -701.9f), new Vector3(-800.1f, 0.0f, -754.8f),
                    new Vector3(-754.4f, 0.0f, -807.8f), new Vector3(-715.4f, 0.0f, -853.5f), new Vector3(-709.9f, 0.0f, -859.2f),
                    new Vector3(-703.8f, 0.0f, -864.4f), new Vector3(-696.0f, 0.0f, -864.9f), new Vector3(-689.1f, 0.0f, -860.9f),
                    new Vector3(-683.0f, 0.0f, -855.7f), new Vector3(-676.6f, 0.0f, -850.9f), new Vector3(-669.4f, 0.0f, -847.5f),
                    new Vector3(-661.5f, 0.0f, -846.2f), new Vector3(-655.5f, 0.0f, -846.0f), new Vector3(-637.5f, 0.0f, -846.6f),
                    new Vector3(-627.5f, 0.0f, -846.8f), new Vector3(-619.5f, 0.0f, -846.5f), new Vector3(-611.6f, 0.0f, -845.7f),
                    new Vector3(-605.6f, 0.0f, -844.7f), new Vector3(-599.8f, 0.0f, -843.4f), new Vector3(-594.1f, 0.0f, -841.6f),
                    new Vector3(-588.5f, 0.0f, -839.5f), new Vector3(-583.0f, 0.0f, -837.0f), new Vector3(-577.7f, 0.0f, -834.3f),
                    new Vector3(-572.5f, 0.0f, -831.2f), new Vector3(-564.2f, 0.0f, -825.6f), new Vector3(-534.3f, 0.0f, -802.1f),
                    new Vector3(-480.8f, 0.0f, -757.0f), new Vector3(-428.1f, 0.0f, -711.0f), new Vector3(-375.9f, 0.0f, -664.3f),
                    new Vector3(-324.0f, 0.0f, -617.3f), new Vector3(-271.8f, 0.0f, -570.6f), new Vector3(-219.3f, 0.0f, -524.3f),
                    new Vector3(-166.9f, 0.0f, -478.0f), new Vector3(-115.1f, 0.0f, -430.8f), new Vector3(-92.8f, 0.0f, -407.9f),
                    new Vector3(-65.3f, 0.0f, -376.1f), new Vector3(-27.0f, 0.0f, -327.4f), new Vector3(-16.6f, 0.0f, -312.8f),
                    new Vector3(-12.3f, 0.0f, -306.0f), new Vector3(-8.8f, 0.0f, -298.8f), new Vector3(-5.9f, 0.0f, -291.3f),
                    new Vector3(-3.5f, 0.0f, -283.7f), new Vector3(-2.0f, 0.0f, -277.9f), new Vector3(-1.0f, 0.0f, -272.0f),
                    new Vector3(-0.3f, 0.0f, -264.0f), new Vector3(0.1f, 0.0f, -194.0f), new Vector3(0.1f, 0.0f, -124.0f),
                    new Vector3(0.0f, 0.0f, -54.0f)
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 70.0f), new Vector3(0.0f, 0.0f, 140.0f),
                    new Vector3(-0.1f, 0.0f, 210.1f), new Vector3(0.2f, 0.0f, 280.1f), new Vector3(1.2f, 0.0f, 298.1f),
                    new Vector3(1.8f, 0.0f, 304.0f), new Vector3(3.1f, 0.0f, 309.9f), new Vector3(5.2f, 0.0f, 315.5f),
                    new Vector3(7.9f, 0.0f, 320.9f), new Vector3(11.1f, 0.0f, 326.0f), new Vector3(14.6f, 0.0f, 330.8f),
                    new Vector3(18.6f, 0.0f, 335.3f), new Vector3(22.9f, 0.0f, 339.4f), new Vector3(27.7f, 0.0f, 343.1f),
                    new Vector3(32.9f, 0.0f, 346.1f), new Vector3(38.4f, 0.0f, 348.5f), new Vector3(44.1f, 0.0f, 350.3f),
                    new Vector3(50.0f, 0.0f, 351.4f), new Vector3(56.0f, 0.0f, 352.1f), new Vector3(62.0f, 0.0f, 352.3f),
                    new Vector3(68.0f, 0.0f, 352.0f), new Vector3(73.9f, 0.0f, 351.1f), new Vector3(79.7f, 0.0f, 349.6f),
                    new Vector3(85.3f, 0.0f, 347.5f), new Vector3(90.7f, 0.0f, 344.9f), new Vector3(95.8f, 0.0f, 341.7f),
                    new Vector3(100.5f, 0.0f, 337.9f), new Vector3(104.3f, 0.0f, 333.3f), new Vector3(110.0f, 0.0f, 325.1f),
                    new Vector3(145.5f, 0.0f, 267.1f), new Vector3(180.9f, 0.0f, 206.7f), new Vector3(199.8f, 0.0f, 176.1f),
                    new Vector3(203.4f, 0.0f, 171.2f), new Vector3(207.8f, 0.0f, 167.1f), new Vector3(212.6f, 0.0f, 163.5f),
                    new Vector3(217.6f, 0.0f, 160.3f), new Vector3(223.0f, 0.0f, 157.5f), new Vector3(228.5f, 0.0f, 155.3f),
                    new Vector3(234.3f, 0.0f, 153.6f), new Vector3(240.1f, 0.0f, 152.3f), new Vector3(246.1f, 0.0f, 151.7f),
                    new Vector3(252.1f, 0.0f, 151.9f), new Vector3(258.0f, 0.0f, 152.8f), new Vector3(263.9f, 0.0f, 154.2f),
                    new Vector3(269.6f, 0.0f, 156.1f), new Vector3(275.1f, 0.0f, 158.4f), new Vector3(280.4f, 0.0f, 161.2f),
                    new Vector3(285.4f, 0.0f, 164.5f), new Vector3(290.1f, 0.0f, 168.3f), new Vector3(294.2f, 0.0f, 172.6f),
                    new Vector3(297.8f, 0.0f, 177.4f), new Vector3(304.8f, 0.0f, 189.6f), new Vector3(337.3f, 0.0f, 251.6f),
                    new Vector3(366.5f, 0.0f, 306.3f), new Vector3(369.7f, 0.0f, 311.4f), new Vector3(373.4f, 0.0f, 316.1f),
                    new Vector3(377.4f, 0.0f, 320.5f), new Vector3(381.8f, 0.0f, 324.6f), new Vector3(388.0f, 0.0f, 329.7f),
                    new Vector3(394.4f, 0.0f, 334.5f), new Vector3(399.5f, 0.0f, 337.7f), new Vector3(404.8f, 0.0f, 340.4f),
                    new Vector3(410.4f, 0.0f, 342.6f), new Vector3(476.5f, 0.0f, 365.9f), new Vector3(542.6f, 0.0f, 389.0f),
                    new Vector3(608.7f, 0.0f, 412.1f), new Vector3(674.8f, 0.0f, 435.0f), new Vector3(731.7f, 0.0f, 454.0f),
                    new Vector3(737.6f, 0.0f, 455.2f), new Vector3(743.6f, 0.0f, 455.3f), new Vector3(749.6f, 0.0f, 454.5f),
                    new Vector3(755.4f, 0.0f, 453.1f), new Vector3(761.1f, 0.0f, 451.4f), new Vector3(766.8f, 0.0f, 449.3f),
                    new Vector3(772.2f, 0.0f, 446.8f), new Vector3(777.4f, 0.0f, 443.7f), new Vector3(782.2f, 0.0f, 440.1f),
                    new Vector3(786.6f, 0.0f, 436.0f), new Vector3(790.6f, 0.0f, 431.6f), new Vector3(794.2f, 0.0f, 426.8f),
                    new Vector3(797.1f, 0.0f, 421.5f), new Vector3(800.2f, 0.0f, 414.1f), new Vector3(813.0f, 0.0f, 376.3f),
                    new Vector3(824.5f, 0.0f, 337.9f), new Vector3(826.9f, 0.0f, 328.2f), new Vector3(827.5f, 0.0f, 322.2f),
                    new Vector3(827.0f, 0.0f, 316.3f), new Vector3(825.8f, 0.0f, 310.4f), new Vector3(823.9f, 0.0f, 304.7f),
                    new Vector3(821.7f, 0.0f, 299.1f), new Vector3(819.1f, 0.0f, 293.7f), new Vector3(815.9f, 0.0f, 288.6f),
                    new Vector3(812.4f, 0.0f, 283.8f), new Vector3(808.4f, 0.0f, 279.3f), new Vector3(803.9f, 0.0f, 275.3f),
                    new Vector3(799.1f, 0.0f, 271.8f), new Vector3(793.8f, 0.0f, 268.9f), new Vector3(778.9f, 0.0f, 263.2f),
                    new Vector3(712.8f, 0.0f, 239.9f), new Vector3(646.7f, 0.0f, 216.9f), new Vector3(586.5f, 0.0f, 195.2f),
                    new Vector3(581.0f, 0.0f, 192.7f), new Vector3(574.8f, 0.0f, 187.7f), new Vector3(571.4f, 0.0f, 182.8f),
                    new Vector3(568.9f, 0.0f, 177.3f), new Vector3(567.4f, 0.0f, 171.5f), new Vector3(567.0f, 0.0f, 165.5f),
                    new Vector3(568.1f, 0.0f, 159.6f), new Vector3(570.2f, 0.0f, 154.0f), new Vector3(573.1f, 0.0f, 148.8f),
                    new Vector3(577.1f, 0.0f, 144.3f), new Vector3(581.8f, 0.0f, 140.6f), new Vector3(587.0f, 0.0f, 137.6f),
                    new Vector3(592.7f, 0.0f, 135.9f), new Vector3(598.7f, 0.0f, 135.0f), new Vector3(668.2f, 0.0f, 127.1f),
                    new Vector3(737.8f, 0.0f, 118.7f), new Vector3(807.3f, 0.0f, 110.2f), new Vector3(876.6f, 0.0f, 100.6f),
                    new Vector3(882.5f, 0.0f, 99.5f), new Vector3(888.2f, 0.0f, 97.7f), new Vector3(893.5f, 0.0f, 94.8f),
                    new Vector3(898.6f, 0.0f, 91.6f), new Vector3(903.4f, 0.0f, 88.0f), new Vector3(907.9f, 0.0f, 84.1f),
                    new Vector3(912.1f, 0.0f, 79.8f), new Vector3(915.9f, 0.0f, 75.2f), new Vector3(919.1f, 0.0f, 70.1f),
                    new Vector3(921.7f, 0.0f, 64.7f), new Vector3(923.7f, 0.0f, 59.0f), new Vector3(925.0f, 0.0f, 53.2f),
                    new Vector3(925.7f, 0.0f, 47.2f), new Vector3(925.7f, 0.0f, 41.2f), new Vector3(925.2f, 0.0f, 35.2f),
                    new Vector3(924.2f, 0.0f, 29.3f), new Vector3(922.6f, 0.0f, 23.5f), new Vector3(920.6f, 0.0f, 17.9f),
                    new Vector3(918.1f, 0.0f, 12.4f), new Vector3(915.2f, 0.0f, 7.2f), new Vector3(911.6f, 0.0f, 2.3f),
                    new Vector3(907.5f, 0.0f, -2.0f), new Vector3(902.9f, 0.0f, -5.8f), new Vector3(897.9f, 0.0f, -9.2f),
                    new Vector3(892.8f, 0.0f, -12.3f), new Vector3(887.4f, 0.0f, -15.0f), new Vector3(881.9f, 0.0f, -17.3f),
                    new Vector3(876.1f, 0.0f, -18.9f), new Vector3(858.4f, 0.0f, -22.5f), new Vector3(850.7f, 0.0f, -24.3f),
                    new Vector3(839.2f, 0.0f, -27.9f), new Vector3(822.3f, 0.0f, -34.2f), new Vector3(805.8f, 0.0f, -41.4f),
                    new Vector3(791.6f, 0.0f, -48.6f), new Vector3(777.7f, 0.0f, -56.6f), new Vector3(767.7f, 0.0f, -63.2f),
                    new Vector3(756.5f, 0.0f, -71.6f), new Vector3(736.7f, 0.0f, -88.5f), new Vector3(727.9f, 0.0f, -96.7f),
                    new Vector3(719.6f, 0.0f, -105.4f), new Vector3(702.6f, 0.0f, -125.1f), new Vector3(695.8f, 0.0f, -132.4f),
                    new Vector3(691.5f, 0.0f, -136.5f), new Vector3(686.8f, 0.0f, -140.2f), new Vector3(681.7f, 0.0f, -143.4f),
                    new Vector3(676.3f, 0.0f, -146.0f), new Vector3(670.7f, 0.0f, -148.2f), new Vector3(664.9f, 0.0f, -149.8f),
                    new Vector3(659.0f, 0.0f, -150.7f), new Vector3(653.0f, 0.0f, -151.1f), new Vector3(647.0f, 0.0f, -150.9f),
                    new Vector3(641.1f, 0.0f, -150.0f), new Vector3(602.2f, 0.0f, -140.6f), new Vector3(534.4f, 0.0f, -122.7f),
                    new Vector3(497.5f, 0.0f, -113.9f), new Vector3(491.6f, 0.0f, -112.9f), new Vector3(485.6f, 0.0f, -112.7f),
                    new Vector3(479.6f, 0.0f, -113.4f), new Vector3(473.8f, 0.0f, -114.6f), new Vector3(468.3f, 0.0f, -117.0f),
                    new Vector3(463.4f, 0.0f, -120.4f), new Vector3(458.9f, 0.0f, -124.5f), new Vector3(454.8f, 0.0f, -128.9f),
                    new Vector3(451.2f, 0.0f, -133.7f), new Vector3(448.3f, 0.0f, -138.9f), new Vector3(445.8f, 0.0f, -144.4f),
                    new Vector3(443.9f, 0.0f, -150.1f), new Vector3(443.0f, 0.0f, -156.0f), new Vector3(442.8f, 0.0f, -162.0f),
                    new Vector3(443.4f, 0.0f, -167.9f), new Vector3(445.2f, 0.0f, -177.8f), new Vector3(449.1f, 0.0f, -193.3f),
                    new Vector3(453.1f, 0.0f, -206.7f), new Vector3(458.5f, 0.0f, -221.8f), new Vector3(467.0f, 0.0f, -242.1f),
                    new Vector3(476.7f, 0.0f, -261.9f), new Vector3(486.4f, 0.0f, -279.3f), new Vector3(494.9f, 0.0f, -292.9f),
                    new Vector3(504.2f, 0.0f, -305.9f), new Vector3(512.9f, 0.0f, -316.9f), new Vector3(524.9f, 0.0f, -330.3f),
                    new Vector3(539.0f, 0.0f, -344.5f), new Vector3(552.5f, 0.0f, -356.4f), new Vector3(568.1f, 0.0f, -368.9f),
                    new Vector3(582.8f, 0.0f, -379.4f), new Vector3(594.6f, 0.0f, -386.9f), new Vector3(606.9f, 0.0f, -393.6f),
                    new Vector3(621.3f, 0.0f, -400.5f), new Vector3(685.9f, 0.0f, -427.4f), new Vector3(750.7f, 0.0f, -454.1f),
                    new Vector3(815.2f, 0.0f, -481.3f), new Vector3(842.4f, 0.0f, -493.9f), new Vector3(847.7f, 0.0f, -496.8f),
                    new Vector3(852.5f, 0.0f, -500.3f), new Vector3(856.8f, 0.0f, -504.6f), new Vector3(860.7f, 0.0f, -509.1f),
                    new Vector3(865.6f, 0.0f, -515.4f), new Vector3(870.1f, 0.0f, -522.0f), new Vector3(873.1f, 0.0f, -527.2f),
                    new Vector3(875.9f, 0.0f, -532.5f), new Vector3(878.3f, 0.0f, -538.1f), new Vector3(880.2f, 0.0f, -543.7f),
                    new Vector3(881.7f, 0.0f, -549.6f), new Vector3(882.8f, 0.0f, -555.4f), new Vector3(883.6f, 0.0f, -561.4f),
                    new Vector3(884.4f, 0.0f, -575.4f), new Vector3(884.5f, 0.0f, -645.4f), new Vector3(883.4f, 0.0f, -675.4f),
                    new Vector3(882.8f, 0.0f, -681.4f), new Vector3(881.5f, 0.0f, -687.2f), new Vector3(879.5f, 0.0f, -692.9f),
                    new Vector3(876.9f, 0.0f, -698.3f), new Vector3(873.9f, 0.0f, -703.5f), new Vector3(870.6f, 0.0f, -708.4f),
                    new Vector3(866.9f, 0.0f, -713.2f), new Vector3(862.5f, 0.0f, -717.3f), new Vector3(857.8f, 0.0f, -721.0f),
                    new Vector3(851.1f, 0.0f, -725.3f), new Vector3(790.4f, 0.0f, -760.3f), new Vector3(729.2f, 0.0f, -794.3f),
                    new Vector3(716.7f, 0.0f, -800.6f), new Vector3(711.1f, 0.0f, -802.6f), new Vector3(705.1f, 0.0f, -803.4f),
                    new Vector3(699.1f, 0.0f, -803.3f), new Vector3(693.1f, 0.0f, -802.8f), new Vector3(687.2f, 0.0f, -801.9f),
                    new Vector3(681.3f, 0.0f, -800.6f), new Vector3(675.7f, 0.0f, -798.7f), new Vector3(670.3f, 0.0f, -796.0f),
                    new Vector3(665.2f, 0.0f, -792.8f), new Vector3(644.8f, 0.0f, -776.7f), new Vector3(590.9f, 0.0f, -732.1f),
                    new Vector3(537.2f, 0.0f, -687.1f), new Vector3(483.3f, 0.0f, -642.4f), new Vector3(464.4f, 0.0f, -627.5f),
                    new Vector3(459.4f, 0.0f, -624.2f), new Vector3(454.0f, 0.0f, -621.7f), new Vector3(448.3f, 0.0f, -619.7f),
                    new Vector3(442.5f, 0.0f, -618.3f), new Vector3(436.6f, 0.0f, -617.5f), new Vector3(430.6f, 0.0f, -617.4f),
                    new Vector3(424.6f, 0.0f, -617.8f), new Vector3(418.7f, 0.0f, -618.9f), new Vector3(412.9f, 0.0f, -620.5f),
                    new Vector3(405.7f, 0.0f, -624.1f), new Vector3(347.2f, 0.0f, -662.4f), new Vector3(289.1f, 0.0f, -701.5f),
                    new Vector3(231.1f, 0.0f, -740.7f), new Vector3(173.0f, 0.0f, -779.9f), new Vector3(114.6f, 0.0f, -818.5f),
                    new Vector3(89.2f, 0.0f, -834.5f), new Vector3(83.9f, 0.0f, -837.3f), new Vector3(78.2f, 0.0f, -839.2f),
                    new Vector3(72.4f, 0.0f, -840.5f), new Vector3(66.4f, 0.0f, -841.3f), new Vector3(60.4f, 0.0f, -841.3f),
                    new Vector3(54.5f, 0.0f, -840.5f), new Vector3(48.7f, 0.0f, -839.0f), new Vector3(43.0f, 0.0f, -837.1f),
                    new Vector3(37.5f, 0.0f, -834.8f), new Vector3(32.1f, 0.0f, -832.0f), new Vector3(27.1f, 0.0f, -828.8f),
                    new Vector3(22.4f, 0.0f, -825.0f), new Vector3(18.2f, 0.0f, -820.7f), new Vector3(14.4f, 0.0f, -816.1f),
                    new Vector3(11.1f, 0.0f, -811.1f), new Vector3(8.4f, 0.0f, -805.7f), new Vector3(6.3f, 0.0f, -800.1f),
                    new Vector3(5.2f, 0.0f, -792.2f), new Vector3(3.0f, 0.0f, -722.2f), new Vector3(2.1f, 0.0f, -652.2f),
                    new Vector3(1.6f, 0.0f, -582.2f), new Vector3(1.2f, 0.0f, -512.2f), new Vector3(0.9f, 0.0f, -442.1f),
                    new Vector3(0.7f, 0.0f, -372.1f), new Vector3(0.5f, 0.0f, -302.1f), new Vector3(0.3f, 0.0f, -232.1f),
                    new Vector3(0.2f, 0.0f, -162.1f), new Vector3(0.1f, 0.0f, -92.0f), new Vector3(0.0f, 0.0f, -22.0f)
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
                // Real activation-zone count: THREE.
                DrsZoneThreeNormalized = new Vector2(0.76f, 0.84f),
                TargetLengthMeters = 6174f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 70.0f), new Vector3(-0.2f, 0.0f, 140.0f),
                    new Vector3(-1.1f, 0.0f, 210.0f), new Vector3(-1.9f, 0.0f, 230.0f), new Vector3(-4.0f, 0.0f, 237.6f),
                    new Vector3(-10.5f, 0.0f, 242.0f), new Vector3(-16.4f, 0.0f, 242.7f), new Vector3(-32.4f, 0.0f, 243.3f),
                    new Vector3(-40.4f, 0.0f, 243.8f), new Vector3(-48.2f, 0.0f, 245.5f), new Vector3(-54.6f, 0.0f, 250.2f),
                    new Vector3(-59.3f, 0.0f, 256.6f), new Vector3(-61.4f, 0.0f, 264.3f), new Vector3(-61.2f, 0.0f, 272.3f),
                    new Vector3(-58.6f, 0.0f, 279.8f), new Vector3(-56.0f, 0.0f, 285.2f), new Vector3(-52.2f, 0.0f, 292.2f),
                    new Vector3(-44.7f, 0.0f, 304.1f), new Vector3(-7.1f, 0.0f, 355.9f), new Vector3(3.3f, 0.0f, 370.6f),
                    new Vector3(6.2f, 0.0f, 375.8f), new Vector3(8.6f, 0.0f, 381.3f), new Vector3(10.7f, 0.0f, 386.9f),
                    new Vector3(12.5f, 0.0f, 392.7f), new Vector3(15.6f, 0.0f, 406.3f), new Vector3(27.7f, 0.0f, 475.3f),
                    new Vector3(39.0f, 0.0f, 544.4f), new Vector3(50.6f, 0.0f, 613.4f), new Vector3(58.2f, 0.0f, 652.7f),
                    new Vector3(61.8f, 0.0f, 668.3f), new Vector3(62.7f, 0.0f, 674.2f), new Vector3(63.0f, 0.0f, 682.2f),
                    new Vector3(59.7f, 0.0f, 689.4f), new Vector3(55.9f, 0.0f, 694.0f), new Vector3(51.6f, 0.0f, 698.2f),
                    new Vector3(39.7f, 0.0f, 708.9f), new Vector3(33.9f, 0.0f, 714.4f), new Vector3(29.9f, 0.0f, 718.9f),
                    new Vector3(26.2f, 0.0f, 723.6f), new Vector3(22.8f, 0.0f, 728.5f), new Vector3(19.6f, 0.0f, 733.6f),
                    new Vector3(17.0f, 0.0f, 739.0f), new Vector3(15.0f, 0.0f, 744.7f), new Vector3(12.9f, 0.0f, 752.4f),
                    new Vector3(11.1f, 0.0f, 760.2f), new Vector3(9.8f, 0.0f, 768.1f), new Vector3(8.9f, 0.0f, 776.0f),
                    new Vector3(8.7f, 0.0f, 782.0f), new Vector3(8.8f, 0.0f, 788.0f), new Vector3(9.3f, 0.0f, 794.0f),
                    new Vector3(10.1f, 0.0f, 800.0f), new Vector3(11.7f, 0.0f, 807.8f), new Vector3(13.9f, 0.0f, 815.5f),
                    new Vector3(17.1f, 0.0f, 825.0f), new Vector3(20.2f, 0.0f, 832.3f), new Vector3(22.8f, 0.0f, 837.7f),
                    new Vector3(25.7f, 0.0f, 843.0f), new Vector3(29.0f, 0.0f, 848.0f), new Vector3(32.5f, 0.0f, 852.9f),
                    new Vector3(36.6f, 0.0f, 857.3f), new Vector3(42.6f, 0.0f, 862.6f), new Vector3(64.4f, 0.0f, 880.1f),
                    new Vector3(73.6f, 0.0f, 887.8f), new Vector3(77.9f, 0.0f, 892.0f), new Vector3(82.1f, 0.0f, 896.3f),
                    new Vector3(85.9f, 0.0f, 901.0f), new Vector3(89.4f, 0.0f, 905.8f), new Vector3(92.5f, 0.0f, 910.9f),
                    new Vector3(95.2f, 0.0f, 916.3f), new Vector3(97.4f, 0.0f, 921.9f), new Vector3(99.9f, 0.0f, 929.5f),
                    new Vector3(101.9f, 0.0f, 937.2f), new Vector3(104.1f, 0.0f, 949.0f), new Vector3(106.8f, 0.0f, 968.8f),
                    new Vector3(108.2f, 0.0f, 984.8f), new Vector3(108.5f, 0.0f, 994.8f), new Vector3(108.4f, 0.0f, 1000.8f),
                    new Vector3(107.8f, 0.0f, 1006.7f), new Vector3(106.8f, 0.0f, 1012.7f), new Vector3(105.3f, 0.0f, 1018.5f),
                    new Vector3(103.4f, 0.0f, 1024.2f), new Vector3(100.0f, 0.0f, 1031.4f), new Vector3(93.2f, 0.0f, 1041.3f),
                    new Vector3(78.1f, 0.0f, 1062.4f), new Vector3(75.0f, 0.0f, 1067.5f), new Vector3(72.3f, 0.0f, 1072.9f),
                    new Vector3(70.6f, 0.0f, 1080.7f), new Vector3(70.5f, 0.0f, 1086.7f), new Vector3(71.4f, 0.0f, 1094.7f),
                    new Vector3(72.7f, 0.0f, 1100.5f), new Vector3(76.5f, 0.0f, 1114.0f), new Vector3(95.1f, 0.0f, 1166.8f),
                    new Vector3(106.8f, 0.0f, 1196.6f), new Vector3(109.4f, 0.0f, 1202.0f), new Vector3(113.9f, 0.0f, 1208.6f),
                    new Vector3(118.1f, 0.0f, 1212.9f), new Vector3(122.7f, 0.0f, 1216.8f), new Vector3(127.5f, 0.0f, 1220.3f),
                    new Vector3(134.3f, 0.0f, 1224.6f), new Vector3(141.3f, 0.0f, 1228.5f), new Vector3(146.7f, 0.0f, 1231.0f),
                    new Vector3(152.3f, 0.0f, 1233.0f), new Vector3(158.2f, 0.0f, 1234.3f), new Vector3(164.1f, 0.0f, 1235.1f),
                    new Vector3(174.1f, 0.0f, 1235.8f), new Vector3(200.1f, 0.0f, 1236.6f), new Vector3(206.1f, 0.0f, 1237.0f),
                    new Vector3(213.9f, 0.0f, 1238.7f), new Vector3(220.8f, 0.0f, 1242.7f), new Vector3(225.6f, 0.0f, 1246.3f),
                    new Vector3(229.9f, 0.0f, 1250.4f), new Vector3(233.9f, 0.0f, 1254.9f), new Vector3(237.5f, 0.0f, 1259.7f),
                    new Vector3(241.9f, 0.0f, 1266.4f), new Vector3(259.5f, 0.0f, 1297.9f), new Vector3(271.7f, 0.0f, 1318.5f),
                    new Vector3(287.6f, 0.0f, 1341.6f), new Vector3(314.2f, 0.0f, 1376.6f), new Vector3(319.0f, 0.0f, 1383.0f),
                    new Vector3(325.7f, 0.0f, 1393.0f), new Vector3(334.7f, 0.0f, 1408.5f), new Vector3(349.2f, 0.0f, 1437.0f),
                    new Vector3(356.6f, 0.0f, 1453.5f), new Vector3(362.2f, 0.0f, 1468.4f), new Vector3(382.9f, 0.0f, 1535.3f),
                    new Vector3(403.2f, 0.0f, 1602.3f), new Vector3(422.9f, 0.0f, 1669.5f), new Vector3(441.9f, 0.0f, 1736.9f),
                    new Vector3(460.4f, 0.0f, 1804.4f), new Vector3(467.0f, 0.0f, 1831.6f), new Vector3(467.7f, 0.0f, 1839.6f),
                    new Vector3(466.8f, 0.0f, 1845.5f), new Vector3(465.4f, 0.0f, 1851.3f), new Vector3(463.5f, 0.0f, 1857.0f),
                    new Vector3(461.1f, 0.0f, 1862.5f), new Vector3(458.0f, 0.0f, 1867.6f), new Vector3(454.3f, 0.0f, 1872.3f),
                    new Vector3(450.1f, 0.0f, 1876.6f), new Vector3(445.6f, 0.0f, 1880.7f), new Vector3(440.9f, 0.0f, 1884.3f),
                    new Vector3(435.8f, 0.0f, 1887.5f), new Vector3(428.5f, 0.0f, 1890.6f), new Vector3(422.6f, 0.0f, 1892.1f),
                    new Vector3(416.7f, 0.0f, 1892.9f), new Vector3(410.7f, 0.0f, 1893.2f), new Vector3(404.7f, 0.0f, 1892.9f),
                    new Vector3(398.8f, 0.0f, 1892.0f), new Vector3(391.0f, 0.0f, 1890.3f), new Vector3(385.2f, 0.0f, 1888.7f),
                    new Vector3(378.0f, 0.0f, 1885.2f), new Vector3(373.1f, 0.0f, 1881.7f), new Vector3(368.6f, 0.0f, 1877.8f),
                    new Vector3(364.4f, 0.0f, 1873.5f), new Vector3(360.5f, 0.0f, 1869.0f), new Vector3(356.9f, 0.0f, 1864.2f),
                    new Vector3(352.5f, 0.0f, 1857.5f), new Vector3(348.6f, 0.0f, 1850.5f), new Vector3(345.0f, 0.0f, 1843.3f),
                    new Vector3(341.9f, 0.0f, 1836.0f), new Vector3(339.3f, 0.0f, 1828.4f), new Vector3(337.2f, 0.0f, 1820.7f),
                    new Vector3(335.5f, 0.0f, 1812.9f), new Vector3(334.2f, 0.0f, 1805.0f), new Vector3(333.7f, 0.0f, 1799.0f),
                    new Vector3(333.8f, 0.0f, 1793.0f), new Vector3(334.5f, 0.0f, 1785.0f), new Vector3(337.0f, 0.0f, 1767.2f),
                    new Vector3(347.2f, 0.0f, 1716.2f), new Vector3(350.7f, 0.0f, 1700.6f), new Vector3(351.9f, 0.0f, 1692.7f),
                    new Vector3(353.0f, 0.0f, 1682.7f), new Vector3(353.6f, 0.0f, 1666.8f), new Vector3(353.5f, 0.0f, 1648.8f),
                    new Vector3(352.8f, 0.0f, 1638.8f), new Vector3(351.3f, 0.0f, 1626.9f), new Vector3(348.0f, 0.0f, 1609.2f),
                    new Vector3(344.8f, 0.0f, 1595.6f), new Vector3(341.9f, 0.0f, 1586.0f), new Vector3(338.6f, 0.0f, 1576.5f),
                    new Vector3(329.2f, 0.0f, 1554.5f), new Vector3(324.0f, 0.0f, 1543.7f), new Vector3(320.0f, 0.0f, 1536.7f),
                    new Vector3(314.6f, 0.0f, 1528.3f), new Vector3(307.5f, 0.0f, 1518.6f), new Vector3(296.1f, 0.0f, 1504.7f),
                    new Vector3(282.5f, 0.0f, 1490.1f), new Vector3(252.6f, 0.0f, 1460.5f), new Vector3(237.5f, 0.0f, 1444.6f),
                    new Vector3(232.3f, 0.0f, 1438.4f), new Vector3(227.5f, 0.0f, 1432.1f), new Vector3(223.0f, 0.0f, 1425.4f),
                    new Vector3(217.9f, 0.0f, 1416.8f), new Vector3(212.4f, 0.0f, 1406.2f), new Vector3(208.4f, 0.0f, 1397.0f),
                    new Vector3(205.6f, 0.0f, 1389.5f), new Vector3(198.6f, 0.0f, 1368.6f), new Vector3(196.2f, 0.0f, 1363.2f),
                    new Vector3(191.7f, 0.0f, 1356.6f), new Vector3(187.4f, 0.0f, 1352.3f), new Vector3(182.8f, 0.0f, 1348.6f),
                    new Vector3(177.8f, 0.0f, 1345.3f), new Vector3(172.4f, 0.0f, 1342.6f), new Vector3(166.7f, 0.0f, 1340.7f),
                    new Vector3(160.8f, 0.0f, 1339.5f), new Vector3(154.9f, 0.0f, 1338.9f), new Vector3(146.9f, 0.0f, 1338.6f),
                    new Vector3(132.9f, 0.0f, 1338.9f), new Vector3(126.9f, 0.0f, 1338.8f), new Vector3(120.9f, 0.0f, 1338.3f),
                    new Vector3(115.0f, 0.0f, 1337.3f), new Vector3(109.1f, 0.0f, 1335.9f), new Vector3(103.4f, 0.0f, 1334.0f),
                    new Vector3(97.9f, 0.0f, 1331.8f), new Vector3(92.4f, 0.0f, 1329.2f), new Vector3(87.2f, 0.0f, 1326.3f),
                    new Vector3(82.2f, 0.0f, 1323.0f), new Vector3(77.5f, 0.0f, 1319.2f), new Vector3(73.2f, 0.0f, 1315.1f),
                    new Vector3(69.2f, 0.0f, 1310.6f), new Vector3(65.8f, 0.0f, 1305.7f), new Vector3(56.6f, 0.0f, 1290.2f),
                    new Vector3(50.0f, 0.0f, 1277.9f), new Vector3(44.1f, 0.0f, 1265.1f), new Vector3(33.3f, 0.0f, 1237.2f),
                    new Vector3(26.9f, 0.0f, 1218.2f), new Vector3(21.7f, 0.0f, 1198.9f), new Vector3(17.1f, 0.0f, 1177.4f),
                    new Vector3(15.6f, 0.0f, 1167.5f), new Vector3(14.5f, 0.0f, 1155.6f), new Vector3(14.0f, 0.0f, 1141.6f),
                    new Vector3(14.6f, 0.0f, 1123.6f), new Vector3(20.1f, 0.0f, 1061.8f), new Vector3(23.6f, 0.0f, 1013.9f),
                    new Vector3(23.9f, 0.0f, 991.9f), new Vector3(23.0f, 0.0f, 974.0f), new Vector3(20.9f, 0.0f, 954.1f),
                    new Vector3(18.0f, 0.0f, 936.3f), new Vector3(15.0f, 0.0f, 922.6f), new Vector3(11.2f, 0.0f, 909.2f),
                    new Vector3(6.6f, 0.0f, 895.9f), new Vector3(-5.6f, 0.0f, 866.4f), new Vector3(-10.8f, 0.0f, 853.4f),
                    new Vector3(-21.7f, 0.0f, 821.2f), new Vector3(-25.1f, 0.0f, 809.6f), new Vector3(-26.3f, 0.0f, 803.8f),
                    new Vector3(-29.7f, 0.0f, 784.1f), new Vector3(-30.7f, 0.0f, 776.1f), new Vector3(-31.6f, 0.0f, 764.2f),
                    new Vector3(-31.9f, 0.0f, 742.2f), new Vector3(-31.4f, 0.0f, 726.2f), new Vector3(-30.3f, 0.0f, 714.2f),
                    new Vector3(-28.0f, 0.0f, 698.4f), new Vector3(-17.5f, 0.0f, 649.5f), new Vector3(-8.4f, 0.0f, 606.5f),
                    new Vector3(-6.0f, 0.0f, 590.6f), new Vector3(-4.5f, 0.0f, 574.7f), new Vector3(-4.0f, 0.0f, 558.7f),
                    new Vector3(-4.6f, 0.0f, 534.7f), new Vector3(-6.5f, 0.0f, 508.8f), new Vector3(-8.6f, 0.0f, 492.9f),
                    new Vector3(-11.2f, 0.0f, 479.2f), new Vector3(-18.9f, 0.0f, 448.1f), new Vector3(-27.7f, 0.0f, 419.4f),
                    new Vector3(-35.9f, 0.0f, 396.9f), new Vector3(-44.6f, 0.0f, 376.7f), new Vector3(-59.0f, 0.0f, 348.1f),
                    new Vector3(-77.5f, 0.0f, 314.9f), new Vector3(-82.8f, 0.0f, 306.5f), new Vector3(-94.6f, 0.0f, 290.3f),
                    new Vector3(-98.4f, 0.0f, 283.2f), new Vector3(-100.7f, 0.0f, 275.6f), new Vector3(-101.6f, 0.0f, 269.7f),
                    new Vector3(-102.0f, 0.0f, 263.7f), new Vector3(-101.4f, 0.0f, 255.7f), new Vector3(-100.1f, 0.0f, 249.9f),
                    new Vector3(-98.4f, 0.0f, 244.1f), new Vector3(-96.2f, 0.0f, 238.5f), new Vector3(-93.5f, 0.0f, 233.2f),
                    new Vector3(-79.0f, 0.0f, 209.2f), new Vector3(-74.1f, 0.0f, 200.5f), new Vector3(-71.5f, 0.0f, 195.1f),
                    new Vector3(-69.1f, 0.0f, 189.6f), new Vector3(-67.2f, 0.0f, 183.9f), new Vector3(-65.1f, 0.0f, 176.2f),
                    new Vector3(-63.6f, 0.0f, 168.3f), new Vector3(-62.9f, 0.0f, 162.4f), new Vector3(-62.8f, 0.0f, 156.4f),
                    new Vector3(-63.3f, 0.0f, 142.4f), new Vector3(-67.5f, 0.0f, 102.6f), new Vector3(-71.5f, 0.0f, 74.9f),
                    new Vector3(-73.1f, 0.0f, 67.0f), new Vector3(-74.9f, 0.0f, 61.3f), new Vector3(-77.2f, 0.0f, 55.8f),
                    new Vector3(-80.1f, 0.0f, 50.5f), new Vector3(-83.4f, 0.0f, 45.5f), new Vector3(-87.0f, 0.0f, 40.7f),
                    new Vector3(-92.2f, 0.0f, 34.6f), new Vector3(-100.5f, 0.0f, 26.0f), new Vector3(-151.1f, 0.0f, -22.4f),
                    new Vector3(-201.2f, 0.0f, -71.3f), new Vector3(-217.8f, 0.0f, -88.7f), new Vector3(-226.8f, 0.0f, -99.4f),
                    new Vector3(-238.8f, 0.0f, -115.4f), new Vector3(-251.1f, 0.0f, -133.6f), new Vector3(-258.2f, 0.0f, -145.7f),
                    new Vector3(-265.5f, 0.0f, -159.9f), new Vector3(-272.0f, 0.0f, -174.5f), new Vector3(-274.8f, 0.0f, -182.0f),
                    new Vector3(-280.0f, 0.0f, -199.3f), new Vector3(-286.2f, 0.0f, -224.5f), new Vector3(-289.9f, 0.0f, -244.2f),
                    new Vector3(-292.3f, 0.0f, -264.0f), new Vector3(-297.4f, 0.0f, -333.9f), new Vector3(-300.2f, 0.0f, -403.8f),
                    new Vector3(-299.5f, 0.0f, -425.8f), new Vector3(-297.3f, 0.0f, -451.7f), new Vector3(-292.3f, 0.0f, -487.3f),
                    new Vector3(-286.3f, 0.0f, -518.8f), new Vector3(-282.5f, 0.0f, -534.3f), new Vector3(-269.9f, 0.0f, -574.4f),
                    new Vector3(-255.9f, 0.0f, -611.9f), new Vector3(-246.5f, 0.0f, -633.9f), new Vector3(-234.9f, 0.0f, -657.2f),
                    new Vector3(-221.0f, 0.0f, -681.5f), new Vector3(-208.0f, 0.0f, -701.7f), new Vector3(-166.6f, 0.0f, -758.1f),
                    new Vector3(-124.7f, 0.0f, -814.2f), new Vector3(-115.7f, 0.0f, -825.0f), new Vector3(-111.7f, 0.0f, -829.4f),
                    new Vector3(-107.2f, 0.0f, -833.4f), new Vector3(-100.7f, 0.0f, -838.0f), new Vector3(-93.2f, 0.0f, -840.8f),
                    new Vector3(-85.3f, 0.0f, -841.2f), new Vector3(-79.3f, 0.0f, -840.7f), new Vector3(-73.4f, 0.0f, -839.5f),
                    new Vector3(-66.0f, 0.0f, -836.6f), new Vector3(-59.4f, 0.0f, -832.2f), new Vector3(-53.9f, 0.0f, -826.3f),
                    new Vector3(-50.2f, 0.0f, -819.3f), new Vector3(-48.4f, 0.0f, -811.5f), new Vector3(-41.6f, 0.0f, -757.9f),
                    new Vector3(-33.4f, 0.0f, -688.4f), new Vector3(-24.9f, 0.0f, -618.9f), new Vector3(-16.3f, 0.0f, -549.4f),
                    new Vector3(-8.6f, 0.0f, -479.8f), new Vector3(-4.0f, 0.0f, -410.0f), new Vector3(-1.8f, 0.0f, -340.0f),
                    new Vector3(-0.9f, 0.0f, -270.0f), new Vector3(-0.4f, 0.0f, -200.0f), new Vector3(-0.2f, 0.0f, -130.0f),
                    new Vector3(-0.1f, 0.0f, -60.0f)
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
                // Real activation-zone count: ONE. This circuit has no second
                // activation zone, and inventing one changes what the lap is.
                DrsZoneTwoNormalized = Vector2.zero,
                TargetLengthMeters = 3337f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 8.0f), new Vector3(2.4f, 0.1f, 15.6f),
                    new Vector3(4.9f, 0.3f, 21.1f), new Vector3(7.6f, 0.4f, 26.4f), new Vector3(10.7f, 0.6f, 31.5f),
                    new Vector3(15.5f, 0.9f, 37.9f), new Vector3(58.3f, 4.8f, 93.3f), new Vector3(100.6f, 7.8f, 149.1f),
                    new Vector3(122.3f, 8.5f, 177.9f), new Vector3(128.7f, 8.9f, 182.4f), new Vector3(136.6f, 9.5f, 182.8f),
                    new Vector3(144.2f, 10.2f, 180.2f), new Vector3(150.5f, 11.0f, 175.4f), new Vector3(155.0f, 11.9f, 168.8f),
                    new Vector3(157.6f, 12.9f, 161.2f), new Vector3(158.2f, 13.7f, 155.3f), new Vector3(158.5f, 14.8f, 147.3f),
                    new Vector3(158.8f, 15.6f, 141.3f), new Vector3(160.9f, 16.8f, 133.6f), new Vector3(163.6f, 17.7f, 128.3f),
                    new Vector3(167.0f, 18.6f, 123.3f), new Vector3(176.1f, 21.1f, 110.2f), new Vector3(180.3f, 22.3f, 103.3f),
                    new Vector3(183.1f, 23.3f, 98.0f), new Vector3(185.5f, 24.1f, 92.5f), new Vector3(188.9f, 25.3f, 85.3f),
                    new Vector3(195.8f, 26.4f, 82.0f), new Vector3(203.2f, 27.4f, 84.8f), new Vector3(207.1f, 28.3f, 91.6f),
                    new Vector3(205.3f, 29.2f, 99.1f), new Vector3(199.5f, 30.0f, 104.6f), new Vector3(195.4f, 30.5f, 109.0f),
                    new Vector3(184.2f, 31.6f, 123.1f), new Vector3(176.0f, 32.0f, 134.4f), new Vector3(172.5f, 32.0f, 141.6f),
                    new Vector3(172.5f, 32.1f, 149.5f), new Vector3(175.6f, 32.3f, 156.8f), new Vector3(182.1f, 32.5f, 161.3f),
                    new Vector3(187.7f, 32.8f, 163.6f), new Vector3(193.5f, 33.1f, 165.3f), new Vector3(205.0f, 33.7f, 168.4f),
                    new Vector3(218.2f, 34.6f, 173.2f), new Vector3(229.5f, 35.4f, 177.1f), new Vector3(235.4f, 35.9f, 178.5f),
                    new Vector3(241.3f, 36.3f, 179.2f), new Vector3(249.3f, 36.9f, 178.7f), new Vector3(255.4f, 37.5f, 174.0f),
                    new Vector3(257.2f, 38.1f, 166.3f), new Vector3(257.4f, 38.7f, 158.3f), new Vector3(256.0f, 40.8f, 124.3f),
                    new Vector3(252.8f, 41.9f, 90.4f), new Vector3(246.8f, 41.5f, 50.9f), new Vector3(240.1f, 40.0f, 17.6f),
                    new Vector3(234.5f, 38.7f, -3.7f), new Vector3(229.7f, 37.8f, -19.0f), new Vector3(225.5f, 37.0f, -30.2f),
                    new Vector3(220.6f, 36.4f, -41.2f), new Vector3(214.2f, 35.6f, -53.6f), new Vector3(205.0f, 34.8f, -69.1f),
                    new Vector3(196.1f, 34.3f, -82.4f), new Vector3(186.4f, 34.0f, -95.1f), new Vector3(169.5f, 33.6f, -114.9f),
                    new Vector3(154.4f, 32.0f, -130.9f), new Vector3(147.1f, 31.1f, -137.7f), new Vector3(139.4f, 30.0f, -144.1f),
                    new Vector3(129.8f, 28.5f, -151.3f), new Vector3(118.0f, 26.7f, -158.9f), new Vector3(90.2f, 22.4f, -174.7f),
                    new Vector3(70.5f, 19.7f, -184.5f), new Vector3(10.2f, 15.9f, -211.3f), new Vector3(-8.5f, 15.1f, -218.3f),
                    new Vector3(-25.7f, 13.7f, -223.6f), new Vector3(-56.8f, 10.5f, -231.4f), new Vector3(-94.0f, 6.6f, -238.8f),
                    new Vector3(-129.6f, 4.2f, -244.3f), new Vector3(-135.5f, 4.1f, -245.6f), new Vector3(-141.8f, 4.0f, -250.0f),
                    new Vector3(-143.4f, 4.0f, -257.8f), new Vector3(-146.0f, 3.9f, -265.3f), new Vector3(-152.9f, 3.9f, -269.2f),
                    new Vector3(-158.7f, 3.8f, -270.5f), new Vector3(-166.6f, 3.7f, -270.3f), new Vector3(-173.9f, 3.5f, -267.0f),
                    new Vector3(-181.3f, 3.4f, -264.1f), new Vector3(-189.3f, 3.2f, -264.6f), new Vector3(-258.9f, 1.0f, -272.4f),
                    new Vector3(-328.3f, -1.1f, -281.2f), new Vector3(-395.7f, -2.0f, -290.7f), new Vector3(-402.9f, -2.0f, -293.9f),
                    new Vector3(-407.7f, -2.0f, -300.3f), new Vector3(-411.6f, -2.0f, -307.2f), new Vector3(-420.5f, -2.1f, -325.2f),
                    new Vector3(-426.0f, -2.2f, -338.1f), new Vector3(-430.1f, -2.2f, -349.3f), new Vector3(-432.4f, -2.3f, -357.0f),
                    new Vector3(-434.0f, -2.3f, -364.8f), new Vector3(-436.2f, -2.4f, -378.6f), new Vector3(-437.3f, -2.5f, -390.6f),
                    new Vector3(-438.2f, -2.7f, -424.6f), new Vector3(-438.2f, -2.8f, -430.6f), new Vector3(-436.9f, -2.9f, -438.4f),
                    new Vector3(-432.8f, -2.9f, -445.2f), new Vector3(-428.5f, -3.0f, -449.4f), new Vector3(-420.6f, -3.1f, -455.7f),
                    new Vector3(-414.5f, -3.1f, -460.8f), new Vector3(-410.4f, -3.2f, -465.1f), new Vector3(-406.6f, -3.2f, -472.2f),
                    new Vector3(-405.3f, -3.3f, -478.0f), new Vector3(-394.6f, -3.8f, -547.2f), new Vector3(-389.8f, -3.9f, -574.8f),
                    new Vector3(-387.5f, -3.9f, -584.5f), new Vector3(-387.5f, -4.0f, -592.4f), new Vector3(-392.2f, -4.0f, -598.7f),
                    new Vector3(-397.1f, -4.0f, -602.2f), new Vector3(-402.4f, -4.0f, -605.1f), new Vector3(-408.1f, -4.0f, -610.4f),
                    new Vector3(-408.3f, -4.0f, -618.3f), new Vector3(-407.2f, -4.0f, -624.2f), new Vector3(-404.5f, -4.0f, -633.9f),
                    new Vector3(-397.4f, -4.0f, -654.7f), new Vector3(-392.2f, -4.0f, -667.7f), new Vector3(-388.1f, -3.9f, -676.8f),
                    new Vector3(-384.3f, -3.9f, -683.8f), new Vector3(-378.0f, -3.9f, -694.1f), new Vector3(-372.3f, -3.9f, -702.3f),
                    new Vector3(-367.4f, -3.9f, -708.6f), new Vector3(-362.2f, -3.8f, -714.7f), new Vector3(-356.6f, -3.8f, -720.4f),
                    new Vector3(-352.2f, -3.8f, -724.5f), new Vector3(-347.6f, -3.8f, -728.3f), new Vector3(-342.6f, -3.8f, -731.6f),
                    new Vector3(-333.8f, -3.7f, -736.4f), new Vector3(-317.6f, -3.7f, -744.2f), new Vector3(-311.8f, -3.6f, -749.5f),
                    new Vector3(-309.5f, -3.6f, -755.1f), new Vector3(-308.0f, -3.6f, -760.9f), new Vector3(-309.4f, -3.6f, -768.6f),
                    new Vector3(-315.9f, -3.5f, -772.9f), new Vector3(-321.6f, -3.5f, -774.8f), new Vector3(-341.0f, -3.5f, -779.6f),
                    new Vector3(-352.8f, -3.4f, -782.0f), new Vector3(-360.7f, -3.4f, -783.0f), new Vector3(-366.7f, -3.4f, -783.5f),
                    new Vector3(-372.7f, -3.3f, -783.4f), new Vector3(-378.6f, -3.3f, -782.8f), new Vector3(-386.1f, -3.3f, -780.1f),
                    new Vector3(-392.1f, -3.3f, -774.8f), new Vector3(-393.7f, -3.3f, -767.1f), new Vector3(-394.2f, -3.2f, -761.1f),
                    new Vector3(-395.5f, -3.2f, -755.3f), new Vector3(-398.5f, -3.2f, -747.9f), new Vector3(-401.8f, -3.2f, -742.9f),
                    new Vector3(-410.3f, -3.1f, -731.7f), new Vector3(-417.1f, -3.1f, -721.9f), new Vector3(-431.8f, -3.1f, -698.0f),
                    new Vector3(-434.6f, -3.0f, -692.7f), new Vector3(-437.8f, -3.0f, -685.4f), new Vector3(-441.3f, -3.0f, -676.0f),
                    new Vector3(-454.3f, -3.0f, -631.9f), new Vector3(-470.9f, -2.9f, -568.0f), new Vector3(-474.3f, -2.9f, -550.3f),
                    new Vector3(-481.3f, -2.7f, -496.8f), new Vector3(-485.1f, -2.5f, -448.9f), new Vector3(-485.7f, -2.4f, -424.9f),
                    new Vector3(-484.8f, -2.3f, -396.9f), new Vector3(-482.2f, -2.2f, -369.1f), new Vector3(-480.6f, -2.2f, -357.2f),
                    new Vector3(-479.0f, -2.2f, -349.3f), new Vector3(-476.3f, -2.1f, -339.7f), new Vector3(-470.0f, -2.1f, -320.7f),
                    new Vector3(-467.7f, -2.1f, -313.1f), new Vector3(-466.3f, -2.1f, -307.2f), new Vector3(-465.4f, -2.0f, -301.3f),
                    new Vector3(-465.2f, -2.0f, -295.3f), new Vector3(-465.5f, -2.0f, -281.3f), new Vector3(-463.2f, -2.0f, -273.8f),
                    new Vector3(-456.3f, -2.0f, -269.9f), new Vector3(-450.5f, -2.0f, -268.3f), new Vector3(-442.7f, -2.0f, -266.6f),
                    new Vector3(-436.8f, -2.0f, -265.7f), new Vector3(-430.8f, -2.0f, -265.3f), new Vector3(-416.8f, -2.0f, -264.3f),
                    new Vector3(-365.2f, -1.9f, -258.2f), new Vector3(-353.4f, -1.9f, -256.0f), new Vector3(-324.3f, -1.8f, -248.7f),
                    new Vector3(-256.7f, -1.5f, -230.5f), new Vector3(-239.1f, -1.5f, -226.8f), new Vector3(-217.3f, -1.4f, -223.4f),
                    new Vector3(-199.4f, -1.3f, -221.6f), new Vector3(-193.5f, -1.3f, -221.0f), new Vector3(-187.6f, -1.3f, -219.9f),
                    new Vector3(-181.7f, -1.3f, -218.4f), new Vector3(-176.1f, -1.2f, -216.5f), new Vector3(-146.7f, -1.1f, -203.7f),
                    new Vector3(-106.4f, -1.0f, -186.1f), new Vector3(-98.9f, -1.0f, -183.4f), new Vector3(-89.2f, -1.0f, -180.7f),
                    new Vector3(-73.6f, -1.0f, -177.2f), new Vector3(-53.9f, -1.0f, -173.8f), new Vector3(-16.3f, -0.9f, -168.4f),
                    new Vector3(-8.5f, -0.9f, -166.8f), new Vector3(-2.7f, -0.9f, -165.3f), new Vector3(3.0f, -0.9f, -163.3f),
                    new Vector3(8.5f, -0.8f, -160.9f), new Vector3(13.8f, -0.8f, -158.0f), new Vector3(18.9f, -0.8f, -154.9f),
                    new Vector3(23.7f, -0.8f, -151.4f), new Vector3(28.4f, -0.7f, -147.6f), new Vector3(32.8f, -0.7f, -143.5f),
                    new Vector3(38.3f, -0.7f, -137.6f), new Vector3(42.1f, -0.6f, -133.0f), new Vector3(45.6f, -0.6f, -128.2f),
                    new Vector3(48.7f, -0.6f, -123.0f), new Vector3(51.2f, -0.5f, -117.6f), new Vector3(52.9f, -0.5f, -111.8f),
                    new Vector3(53.9f, -0.5f, -105.9f), new Vector3(54.2f, -0.4f, -99.9f), new Vector3(54.0f, -0.4f, -93.9f),
                    new Vector3(53.3f, -0.4f, -88.0f), new Vector3(52.4f, -0.3f, -82.1f), new Vector3(51.0f, -0.3f, -76.2f),
                    new Vector3(49.3f, -0.3f, -70.5f), new Vector3(46.5f, -0.2f, -63.0f), new Vector3(43.3f, -0.2f, -55.6f),
                    new Vector3(40.5f, -0.2f, -50.3f), new Vector3(35.6f, -0.1f, -44.1f), new Vector3(31.0f, -0.1f, -40.1f),
                    new Vector3(23.5f, -0.1f, -33.5f), new Vector3(11.9f, 0.0f, -22.6f), new Vector3(7.7f, 0.0f, -18.3f),
                    new Vector3(3.6f, 0.0f, -11.4f)
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
                // Real activation-zone count: ONE. This circuit has no second
                // activation zone, and inventing one changes what the lap is.
                DrsZoneTwoNormalized = Vector2.zero,
                TargetLengthMeters = 5807f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.0f, 70.0f), new Vector3(0.2f, 3.5f, 140.0f),
                    new Vector3(0.6f, 6.5f, 210.0f), new Vector3(1.2f, 9.0f, 280.0f), new Vector3(2.3f, 10.0f, 336.0f),
                    new Vector3(2.9f, 10.0f, 342.0f), new Vector3(3.9f, 10.0f, 347.9f), new Vector3(5.4f, 10.0f, 353.7f),
                    new Vector3(7.3f, 10.0f, 359.4f), new Vector3(10.2f, 10.1f, 366.8f), new Vector3(13.6f, 10.2f, 374.1f),
                    new Vector3(16.5f, 10.2f, 379.3f), new Vector3(19.8f, 10.3f, 384.3f), new Vector3(23.5f, 10.4f, 389.1f),
                    new Vector3(28.9f, 10.6f, 395.0f), new Vector3(33.2f, 10.7f, 399.1f), new Vector3(39.5f, 10.9f, 404.0f),
                    new Vector3(68.0f, 12.0f, 422.6f), new Vector3(83.5f, 12.6f, 431.7f), new Vector3(88.9f, 12.9f, 434.4f),
                    new Vector3(96.4f, 13.2f, 437.0f), new Vector3(104.3f, 13.6f, 438.3f), new Vector3(110.3f, 13.8f, 438.5f),
                    new Vector3(116.3f, 14.1f, 438.2f), new Vector3(124.1f, 14.5f, 436.5f), new Vector3(129.7f, 14.8f, 434.4f),
                    new Vector3(135.1f, 15.0f, 431.7f), new Vector3(141.9f, 15.4f, 427.6f), new Vector3(148.1f, 15.8f, 422.5f),
                    new Vector3(153.4f, 16.2f, 416.5f), new Vector3(156.9f, 16.5f, 411.6f), new Vector3(160.7f, 16.9f, 404.6f),
                    new Vector3(163.3f, 17.4f, 397.1f), new Vector3(165.0f, 17.8f, 389.2f), new Vector3(165.6f, 18.2f, 381.3f),
                    new Vector3(165.4f, 18.5f, 375.3f), new Vector3(164.2f, 19.2f, 361.3f), new Vector3(156.2f, 22.6f, 291.8f),
                    new Vector3(148.9f, 25.1f, 222.2f), new Vector3(147.5f, 25.5f, 200.2f), new Vector3(147.5f, 25.7f, 194.2f),
                    new Vector3(148.7f, 25.8f, 186.3f), new Vector3(151.3f, 25.9f, 178.8f), new Vector3(155.4f, 25.9f, 171.9f),
                    new Vector3(160.3f, 26.0f, 165.5f), new Vector3(165.4f, 26.0f, 159.4f), new Vector3(171.0f, 26.0f, 153.7f),
                    new Vector3(182.9f, 26.0f, 143.0f), new Vector3(197.9f, 26.1f, 129.7f), new Vector3(203.5f, 26.2f, 124.1f),
                    new Vector3(207.5f, 26.3f, 119.5f), new Vector3(212.1f, 26.3f, 113.0f), new Vector3(215.0f, 26.4f, 107.8f),
                    new Vector3(218.2f, 26.5f, 100.4f), new Vector3(220.2f, 26.6f, 92.7f), new Vector3(221.1f, 26.6f, 86.8f),
                    new Vector3(221.5f, 26.7f, 80.8f), new Vector3(221.3f, 26.8f, 74.8f), new Vector3(220.2f, 26.9f, 66.9f),
                    new Vector3(217.7f, 27.0f, 59.3f), new Vector3(195.6f, 27.9f, 10.0f), new Vector3(184.5f, 28.4f, -15.7f),
                    new Vector3(182.4f, 28.6f, -21.3f), new Vector3(180.4f, 28.7f, -29.0f), new Vector3(179.4f, 28.8f, -35.0f),
                    new Vector3(178.8f, 28.9f, -40.9f), new Vector3(178.8f, 29.1f, -46.9f), new Vector3(179.1f, 29.2f, -52.9f),
                    new Vector3(180.0f, 29.3f, -58.9f), new Vector3(181.4f, 29.4f, -64.7f), new Vector3(183.1f, 29.5f, -70.4f),
                    new Vector3(185.3f, 29.6f, -76.0f), new Vector3(187.7f, 29.7f, -81.5f), new Vector3(190.5f, 29.9f, -86.9f),
                    new Vector3(193.6f, 30.0f, -92.0f), new Vector3(197.1f, 30.1f, -96.8f), new Vector3(202.4f, 30.2f, -102.8f),
                    new Vector3(211.1f, 30.4f, -111.1f), new Vector3(235.0f, 31.0f, -132.3f), new Vector3(245.1f, 31.2f, -142.0f),
                    new Vector3(249.2f, 31.2f, -146.4f), new Vector3(253.0f, 31.3f, -151.1f), new Vector3(256.5f, 31.4f, -155.9f),
                    new Vector3(259.7f, 31.5f, -161.0f), new Vector3(263.1f, 31.6f, -168.3f), new Vector3(265.2f, 31.6f, -173.9f),
                    new Vector3(266.8f, 31.7f, -179.7f), new Vector3(268.2f, 31.7f, -185.5f), new Vector3(269.0f, 31.8f, -191.4f),
                    new Vector3(269.3f, 31.8f, -197.4f), new Vector3(269.0f, 31.9f, -203.4f), new Vector3(268.3f, 31.9f, -209.4f),
                    new Vector3(267.3f, 31.9f, -215.3f), new Vector3(265.9f, 32.0f, -221.1f), new Vector3(264.1f, 32.0f, -226.9f),
                    new Vector3(262.0f, 32.0f, -232.5f), new Vector3(259.5f, 32.0f, -237.9f), new Vector3(256.7f, 32.0f, -243.2f),
                    new Vector3(253.5f, 32.0f, -248.3f), new Vector3(249.9f, 32.0f, -253.1f), new Vector3(246.0f, 31.9f, -257.7f),
                    new Vector3(241.7f, 31.9f, -261.9f), new Vector3(235.4f, 31.8f, -266.8f), new Vector3(213.3f, 31.4f, -280.5f),
                    new Vector3(166.9f, 29.9f, -308.1f), new Vector3(160.5f, 29.7f, -312.9f), new Vector3(156.1f, 29.4f, -316.9f),
                    new Vector3(152.0f, 29.2f, -321.3f), new Vector3(148.2f, 29.0f, -326.0f), new Vector3(143.9f, 28.7f, -332.7f),
                    new Vector3(141.4f, 28.5f, -338.2f), new Vector3(138.5f, 28.1f, -345.6f), new Vector3(136.7f, 27.9f, -351.3f),
                    new Vector3(135.2f, 27.6f, -357.2f), new Vector3(134.3f, 27.4f, -363.1f), new Vector3(133.9f, 27.0f, -371.1f),
                    new Vector3(134.9f, 26.7f, -379.0f), new Vector3(136.3f, 26.4f, -384.8f), new Vector3(139.2f, 26.0f, -394.4f),
                    new Vector3(144.0f, 25.3f, -407.5f), new Vector3(158.2f, 23.7f, -440.6f), new Vector3(166.3f, 22.9f, -456.7f),
                    new Vector3(173.2f, 22.3f, -468.9f), new Vector3(177.6f, 22.0f, -475.6f), new Vector3(183.5f, 21.6f, -483.6f),
                    new Vector3(192.5f, 21.0f, -494.4f), new Vector3(199.4f, 20.7f, -501.6f), new Vector3(206.6f, 20.3f, -508.5f),
                    new Vector3(223.5f, 19.6f, -522.6f), new Vector3(233.0f, 19.3f, -529.9f), new Vector3(238.0f, 19.1f, -533.2f),
                    new Vector3(243.2f, 18.9f, -536.2f), new Vector3(248.7f, 18.8f, -538.8f), new Vector3(256.1f, 18.6f, -541.8f),
                    new Vector3(263.6f, 18.5f, -544.4f), new Vector3(294.4f, 18.1f, -553.2f), new Vector3(308.1f, 18.0f, -556.1f),
                    new Vector3(316.0f, 18.0f, -557.3f), new Vector3(325.9f, 18.0f, -558.3f), new Vector3(339.9f, 17.9f, -558.9f),
                    new Vector3(355.9f, 17.7f, -558.6f), new Vector3(369.9f, 17.4f, -557.6f), new Vector3(383.8f, 17.1f, -555.7f),
                    new Vector3(452.6f, 14.9f, -543.2f), new Vector3(521.4f, 12.0f, -529.9f), new Vector3(537.1f, 11.3f, -526.8f),
                    new Vector3(544.9f, 10.9f, -528.0f), new Vector3(551.9f, 10.5f, -531.7f), new Vector3(610.0f, 7.5f, -570.8f),
                    new Vector3(665.7f, 5.2f, -609.8f), new Vector3(671.2f, 5.0f, -615.5f), new Vector3(673.8f, 4.8f, -623.0f),
                    new Vector3(673.1f, 4.6f, -630.9f), new Vector3(670.1f, 4.5f, -638.3f), new Vector3(644.3f, 4.0f, -690.3f),
                    new Vector3(611.4f, 3.1f, -752.1f), new Vector3(576.9f, 1.2f, -813.0f), new Vector3(542.1f, -1.4f, -873.8f),
                    new Vector3(533.0f, -2.1f, -889.3f), new Vector3(529.7f, -2.3f, -894.2f), new Vector3(526.0f, -2.5f, -899.0f),
                    new Vector3(522.0f, -2.8f, -903.5f), new Vector3(516.3f, -3.1f, -909.1f), new Vector3(508.8f, -3.4f, -915.6f),
                    new Vector3(504.0f, -3.7f, -919.3f), new Vector3(499.0f, -3.9f, -922.6f), new Vector3(488.6f, -4.3f, -928.5f),
                    new Vector3(426.2f, -6.6f, -960.4f), new Vector3(421.0f, -6.7f, -963.3f), new Vector3(415.5f, -6.9f, -969.0f),
                    new Vector3(413.3f, -7.1f, -976.6f), new Vector3(414.1f, -7.2f, -984.5f), new Vector3(417.5f, -7.4f, -991.7f),
                    new Vector3(422.9f, -7.5f, -997.5f), new Vector3(430.4f, -7.7f, -1000.3f), new Vector3(438.3f, -7.8f, -1000.2f),
                    new Vector3(446.1f, -7.8f, -998.3f), new Vector3(512.0f, -8.1f, -974.6f), new Vector3(567.1f, -8.7f, -956.7f),
                    new Vector3(584.5f, -8.9f, -952.1f), new Vector3(594.3f, -9.1f, -950.1f), new Vector3(602.2f, -9.2f, -949.0f),
                    new Vector3(608.2f, -9.3f, -948.5f), new Vector3(616.2f, -9.4f, -948.2f), new Vector3(638.2f, -9.8f, -948.6f),
                    new Vector3(646.2f, -9.9f, -949.3f), new Vector3(654.1f, -10.1f, -950.4f), new Vector3(662.0f, -10.2f, -951.9f),
                    new Vector3(669.7f, -10.4f, -954.0f), new Vector3(677.3f, -10.6f, -956.6f), new Vector3(682.8f, -10.7f, -958.8f),
                    new Vector3(690.0f, -10.8f, -962.3f), new Vector3(702.3f, -11.1f, -969.0f), new Vector3(721.0f, -11.5f, -980.6f),
                    new Vector3(737.4f, -11.9f, -992.0f), new Vector3(753.2f, -12.3f, -1004.3f), new Vector3(763.7f, -12.5f, -1013.5f),
                    new Vector3(770.9f, -12.7f, -1020.5f), new Vector3(779.0f, -12.9f, -1029.4f), new Vector3(790.3f, -13.1f, -1043.4f),
                    new Vector3(809.1f, -13.5f, -1069.3f), new Vector3(823.1f, -13.8f, -1091.1f), new Vector3(840.0f, -14.0f, -1120.7f),
                    new Vector3(848.1f, -14.0f, -1136.7f), new Vector3(853.8f, -14.0f, -1149.5f), new Vector3(858.7f, -13.9f, -1162.6f),
                    new Vector3(862.8f, -13.8f, -1176.0f), new Vector3(866.3f, -13.7f, -1189.6f), new Vector3(869.4f, -13.5f, -1205.3f),
                    new Vector3(871.7f, -13.3f, -1221.1f), new Vector3(873.3f, -13.0f, -1239.0f), new Vector3(873.9f, -12.6f, -1257.0f),
                    new Vector3(873.6f, -12.4f, -1269.0f), new Vector3(872.6f, -12.1f, -1281.0f), new Vector3(870.5f, -11.7f, -1296.8f),
                    new Vector3(869.0f, -11.6f, -1304.7f), new Vector3(866.6f, -11.3f, -1314.4f), new Vector3(854.5f, -10.3f, -1352.5f),
                    new Vector3(832.7f, -8.5f, -1419.0f), new Vector3(825.5f, -7.9f, -1446.1f), new Vector3(823.5f, -7.7f, -1455.9f),
                    new Vector3(822.6f, -7.5f, -1461.8f), new Vector3(822.4f, -7.4f, -1469.8f), new Vector3(823.3f, -7.2f, -1477.7f),
                    new Vector3(825.4f, -7.1f, -1485.5f), new Vector3(828.6f, -6.9f, -1492.8f), new Vector3(831.6f, -6.8f, -1498.0f),
                    new Vector3(835.0f, -6.7f, -1502.9f), new Vector3(838.7f, -6.6f, -1507.6f), new Vector3(842.8f, -6.5f, -1512.0f),
                    new Vector3(847.1f, -6.5f, -1516.2f), new Vector3(853.2f, -6.4f, -1521.4f), new Vector3(862.8f, -6.2f, -1528.6f),
                    new Vector3(871.2f, -6.2f, -1534.0f), new Vector3(885.0f, -6.1f, -1542.1f), new Vector3(893.9f, -6.0f, -1546.6f),
                    new Vector3(901.2f, -6.0f, -1549.8f), new Vector3(908.8f, -6.0f, -1552.6f), new Vector3(914.5f, -6.0f, -1554.3f),
                    new Vector3(920.3f, -6.0f, -1555.7f), new Vector3(926.3f, -5.9f, -1556.6f), new Vector3(932.3f, -5.9f, -1557.1f),
                    new Vector3(940.2f, -5.8f, -1556.9f), new Vector3(948.1f, -5.7f, -1555.6f), new Vector3(955.7f, -5.6f, -1553.1f),
                    new Vector3(962.9f, -5.5f, -1549.6f), new Vector3(969.5f, -5.4f, -1545.1f), new Vector3(975.3f, -5.2f, -1539.5f),
                    new Vector3(979.1f, -5.1f, -1534.9f), new Vector3(982.6f, -5.0f, -1530.0f), new Vector3(985.8f, -4.9f, -1524.9f),
                    new Vector3(988.5f, -4.7f, -1519.6f), new Vector3(990.7f, -4.6f, -1514.0f), new Vector3(993.3f, -4.4f, -1506.5f),
                    new Vector3(994.9f, -4.2f, -1500.7f), new Vector3(996.0f, -4.0f, -1492.8f), new Vector3(996.0f, -3.8f, -1484.8f),
                    new Vector3(994.2f, -3.2f, -1466.9f), new Vector3(989.8f, -2.2f, -1437.2f), new Vector3(979.8f, 0.0f, -1380.1f),
                    new Vector3(972.0f, 1.5f, -1344.9f), new Vector3(957.0f, 3.8f, -1291.0f), new Vector3(943.9f, 5.4f, -1251.1f),
                    new Vector3(935.0f, 6.3f, -1228.8f), new Vector3(908.0f, 8.4f, -1168.5f), new Vector3(898.6f, 8.9f, -1148.7f),
                    new Vector3(883.1f, 9.5f, -1120.7f), new Vector3(847.2f, 10.0f, -1060.5f), new Vector3(812.6f, 10.6f, -999.7f),
                    new Vector3(777.2f, 12.0f, -939.3f), new Vector3(740.4f, 13.9f, -879.8f), new Vector3(702.7f, 16.0f, -820.8f),
                    new Vector3(666.4f, 18.1f, -761.0f), new Vector3(631.5f, 20.0f, -700.2f), new Vector3(621.2f, 20.4f, -683.1f),
                    new Vector3(617.8f, 20.6f, -678.2f), new Vector3(614.0f, 20.7f, -673.5f), new Vector3(609.9f, 20.8f, -669.2f),
                    new Vector3(605.5f, 20.9f, -665.1f), new Vector3(600.8f, 21.0f, -661.4f), new Vector3(595.8f, 21.1f, -658.0f),
                    new Vector3(590.7f, 21.2f, -654.9f), new Vector3(580.0f, 21.4f, -649.4f), new Vector3(570.9f, 21.6f, -645.2f),
                    new Vector3(557.9f, 21.7f, -640.1f), new Vector3(535.1f, 21.9f, -632.6f), new Vector3(515.8f, 22.0f, -627.3f),
                    new Vector3(494.3f, 22.0f, -622.8f), new Vector3(476.5f, 21.9f, -620.0f), new Vector3(460.6f, 21.8f, -618.4f),
                    new Vector3(444.6f, 21.7f, -617.7f), new Vector3(420.6f, 21.4f, -618.1f), new Vector3(350.7f, 20.2f, -622.1f),
                    new Vector3(280.8f, 18.6f, -626.1f), new Vector3(250.8f, 17.9f, -627.1f), new Vector3(234.8f, 17.4f, -626.6f),
                    new Vector3(211.0f, 16.8f, -624.4f), new Vector3(203.0f, 16.6f, -623.3f), new Vector3(195.6f, 16.4f, -620.7f),
                    new Vector3(191.6f, 16.2f, -613.9f), new Vector3(189.9f, 16.0f, -606.1f), new Vector3(186.2f, 15.5f, -584.4f),
                    new Vector3(183.8f, 15.3f, -576.8f), new Vector3(179.0f, 15.1f, -570.5f), new Vector3(172.0f, 14.9f, -566.7f),
                    new Vector3(164.1f, 14.7f, -565.4f), new Vector3(158.1f, 14.6f, -565.2f), new Vector3(144.1f, 14.2f, -565.5f),
                    new Vector3(132.1f, 14.0f, -565.4f), new Vector3(122.1f, 13.8f, -564.8f), new Vector3(116.2f, 13.7f, -564.0f),
                    new Vector3(110.3f, 13.5f, -562.9f), new Vector3(104.5f, 13.4f, -561.4f), new Vector3(98.8f, 13.3f, -559.6f),
                    new Vector3(93.2f, 13.2f, -557.2f), new Vector3(87.9f, 13.1f, -554.5f), new Vector3(82.8f, 13.0f, -551.3f),
                    new Vector3(74.7f, 12.8f, -545.5f), new Vector3(66.9f, 12.7f, -539.3f), new Vector3(59.5f, 12.6f, -532.5f),
                    new Vector3(52.5f, 12.4f, -525.4f), new Vector3(46.0f, 12.3f, -517.8f), new Vector3(39.9f, 12.2f, -509.9f),
                    new Vector3(34.3f, 12.2f, -501.5f), new Vector3(28.3f, 12.1f, -491.1f), new Vector3(20.1f, 12.0f, -475.1f),
                    new Vector3(16.1f, 12.0f, -466.0f), new Vector3(12.6f, 12.0f, -456.6f), new Vector3(8.5f, 11.9f, -443.2f),
                    new Vector3(5.5f, 11.8f, -431.6f), new Vector3(3.3f, 11.7f, -419.8f), new Vector3(1.2f, 11.4f, -403.9f),
                    new Vector3(-0.4f, 11.0f, -382.0f), new Vector3(-1.0f, 9.0f, -312.0f), new Vector3(-0.7f, 6.4f, -242.0f),
                    new Vector3(-0.3f, 3.7f, -172.0f), new Vector3(-0.1f, 1.5f, -102.0f), new Vector3(0.0f, 0.2f, -32.0f)
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.1f, 70.0f), new Vector3(1.0f, 0.3f, 108.0f),
                    new Vector3(1.8f, 0.4f, 116.0f), new Vector3(3.0f, 0.4f, 123.8f), new Vector3(4.8f, 0.5f, 131.6f),
                    new Vector3(6.5f, 0.5f, 137.4f), new Vector3(8.5f, 0.5f, 143.1f), new Vector3(10.9f, 0.6f, 148.6f),
                    new Vector3(13.5f, 0.6f, 154.0f), new Vector3(16.5f, 0.7f, 159.2f), new Vector3(21.1f, 0.7f, 165.7f),
                    new Vector3(26.5f, 0.8f, 171.6f), new Vector3(31.0f, 0.8f, 175.6f), new Vector3(37.2f, 0.9f, 180.6f),
                    new Vector3(43.8f, 1.0f, 185.2f), new Vector3(48.9f, 1.0f, 188.4f), new Vector3(54.1f, 1.1f, 191.3f),
                    new Vector3(59.5f, 1.1f, 193.9f), new Vector3(66.9f, 1.2f, 196.9f), new Vector3(76.4f, 1.3f, 200.1f),
                    new Vector3(88.0f, 1.4f, 203.3f), new Vector3(111.4f, 1.7f, 208.3f), new Vector3(180.4f, 2.4f, 220.5f),
                    new Vector3(221.8f, 2.8f, 227.2f), new Vector3(249.7f, 3.1f, 229.9f), new Vector3(303.6f, 3.7f, 232.3f),
                    new Vector3(373.6f, 4.3f, 231.3f), new Vector3(443.6f, 4.7f, 228.6f), new Vector3(507.5f, 5.0f, 226.0f),
                    new Vector3(517.5f, 5.0f, 226.3f), new Vector3(523.5f, 5.0f, 226.8f), new Vector3(529.4f, 5.0f, 227.7f),
                    new Vector3(535.3f, 5.0f, 228.9f), new Vector3(543.0f, 5.0f, 231.0f), new Vector3(554.4f, 5.0f, 234.9f),
                    new Vector3(574.7f, 5.0f, 243.2f), new Vector3(602.4f, 5.1f, 254.9f), new Vector3(611.8f, 5.1f, 258.3f),
                    new Vector3(617.6f, 5.1f, 259.9f), new Vector3(625.4f, 5.1f, 261.2f), new Vector3(633.4f, 5.2f, 261.2f),
                    new Vector3(639.4f, 5.2f, 260.3f), new Vector3(645.2f, 5.2f, 259.1f), new Vector3(651.0f, 5.2f, 257.6f),
                    new Vector3(658.6f, 5.2f, 254.9f), new Vector3(686.1f, 5.4f, 242.9f), new Vector3(728.0f, 5.6f, 224.0f),
                    new Vector3(741.1f, 5.7f, 219.0f), new Vector3(750.6f, 5.7f, 216.0f), new Vector3(758.4f, 5.8f, 214.2f),
                    new Vector3(764.3f, 5.8f, 213.4f), new Vector3(772.3f, 5.9f, 212.8f), new Vector3(778.3f, 5.9f, 212.7f),
                    new Vector3(784.3f, 5.9f, 212.9f), new Vector3(790.3f, 6.0f, 213.6f), new Vector3(796.2f, 6.0f, 214.7f),
                    new Vector3(802.0f, 6.0f, 216.3f), new Vector3(807.6f, 6.1f, 218.3f), new Vector3(813.1f, 6.1f, 220.7f),
                    new Vector3(825.5f, 6.2f, 227.3f), new Vector3(854.9f, 6.5f, 244.3f), new Vector3(865.5f, 6.5f, 249.9f),
                    new Vector3(871.0f, 6.6f, 252.4f), new Vector3(878.5f, 6.6f, 255.0f), new Vector3(886.4f, 6.7f, 256.5f),
                    new Vector3(894.3f, 6.8f, 257.0f), new Vector3(900.3f, 6.8f, 256.9f), new Vector3(906.3f, 6.8f, 256.4f),
                    new Vector3(912.2f, 6.9f, 255.4f), new Vector3(918.1f, 6.9f, 254.0f), new Vector3(925.6f, 7.0f, 251.4f),
                    new Vector3(932.6f, 7.0f, 247.5f), new Vector3(939.0f, 7.1f, 242.7f), new Vector3(944.9f, 7.2f, 237.3f),
                    new Vector3(949.1f, 7.2f, 233.0f), new Vector3(953.0f, 7.3f, 228.4f), new Vector3(956.6f, 7.3f, 223.7f),
                    new Vector3(960.0f, 7.3f, 218.7f), new Vector3(966.1f, 7.4f, 208.4f), new Vector3(976.3f, 7.6f, 188.9f),
                    new Vector3(995.5f, 7.9f, 149.3f), new Vector3(999.6f, 7.9f, 142.4f), new Vector3(1002.9f, 8.0f, 137.4f),
                    new Vector3(1006.5f, 8.0f, 132.6f), new Vector3(1010.5f, 8.1f, 128.1f), new Vector3(1014.7f, 8.1f, 123.9f),
                    new Vector3(1020.7f, 8.1f, 118.6f), new Vector3(1030.2f, 8.2f, 111.2f), new Vector3(1075.1f, 8.5f, 81.2f),
                    new Vector3(1133.8f, 8.8f, 43.1f), new Vector3(1192.5f, 9.0f, 5.0f), new Vector3(1251.1f, 9.0f, -33.3f),
                    new Vector3(1309.8f, 8.9f, -71.5f), new Vector3(1368.3f, 8.7f, -109.9f), new Vector3(1426.7f, 8.4f, -148.5f),
                    new Vector3(1484.9f, 8.0f, -187.4f), new Vector3(1542.6f, 7.7f, -227.1f), new Vector3(1598.8f, 7.3f, -268.8f),
                    new Vector3(1642.7f, 7.0f, -303.6f), new Vector3(1651.6f, 6.9f, -311.6f), new Vector3(1658.6f, 6.9f, -318.7f),
                    new Vector3(1662.6f, 6.8f, -323.2f), new Vector3(1666.2f, 6.8f, -328.0f), new Vector3(1669.5f, 6.8f, -333.0f),
                    new Vector3(1672.5f, 6.8f, -338.2f), new Vector3(1675.0f, 6.7f, -343.7f), new Vector3(1677.2f, 6.7f, -349.2f),
                    new Vector3(1679.1f, 6.7f, -354.9f), new Vector3(1680.6f, 6.6f, -360.7f), new Vector3(1681.7f, 6.6f, -366.6f),
                    new Vector3(1682.5f, 6.6f, -374.6f), new Vector3(1682.2f, 6.5f, -382.6f), new Vector3(1681.0f, 6.5f, -390.5f),
                    new Vector3(1679.2f, 6.5f, -398.3f), new Vector3(1676.9f, 6.4f, -406.0f), new Vector3(1674.2f, 6.4f, -413.5f),
                    new Vector3(1671.9f, 6.4f, -419.0f), new Vector3(1669.2f, 6.4f, -424.4f), new Vector3(1666.2f, 6.3f, -429.5f),
                    new Vector3(1662.7f, 6.3f, -434.4f), new Vector3(1658.9f, 6.3f, -439.0f), new Vector3(1653.2f, 6.3f, -444.7f),
                    new Vector3(1646.8f, 6.2f, -449.5f), new Vector3(1641.7f, 6.2f, -452.7f), new Vector3(1629.4f, 6.2f, -459.4f),
                    new Vector3(1622.1f, 6.2f, -462.6f), new Vector3(1577.2f, 6.1f, -479.6f), new Vector3(1553.5f, 6.0f, -490.2f),
                    new Vector3(1535.7f, 6.0f, -499.3f), new Vector3(1518.4f, 6.0f, -509.4f), new Vector3(1491.6f, 6.0f, -526.9f),
                    new Vector3(1465.9f, 5.9f, -545.9f), new Vector3(1430.1f, 5.8f, -574.8f), new Vector3(1373.6f, 5.5f, -616.2f),
                    new Vector3(1330.0f, 5.2f, -648.0f), new Vector3(1324.4f, 5.1f, -653.6f), new Vector3(1321.8f, 5.0f, -661.1f),
                    new Vector3(1322.5f, 5.0f, -669.1f), new Vector3(1326.1f, 4.9f, -676.2f), new Vector3(1331.8f, 4.9f, -684.4f),
                    new Vector3(1344.9f, 4.7f, -702.1f), new Vector3(1348.2f, 4.7f, -707.1f), new Vector3(1351.6f, 4.6f, -714.3f),
                    new Vector3(1352.4f, 4.5f, -722.2f), new Vector3(1350.6f, 4.5f, -730.0f), new Vector3(1347.0f, 4.4f, -737.1f),
                    new Vector3(1341.6f, 4.3f, -742.9f), new Vector3(1326.4f, 4.2f, -756.0f), new Vector3(1320.1f, 4.1f, -760.9f),
                    new Vector3(1310.2f, 4.0f, -767.7f), new Vector3(1301.7f, 3.9f, -773.0f), new Vector3(1292.9f, 3.8f, -777.7f),
                    new Vector3(1283.8f, 3.8f, -781.9f), new Vector3(1272.6f, 3.7f, -786.2f), new Vector3(1261.2f, 3.6f, -789.8f),
                    new Vector3(1245.6f, 3.4f, -793.6f), new Vector3(1228.0f, 3.3f, -797.0f), new Vector3(1218.1f, 3.2f, -798.4f),
                    new Vector3(1210.1f, 3.1f, -798.6f), new Vector3(1202.2f, 3.0f, -797.2f), new Vector3(1194.8f, 3.0f, -794.2f),
                    new Vector3(1188.2f, 2.9f, -789.8f), new Vector3(1175.3f, 2.8f, -780.2f), new Vector3(1122.7f, 2.2f, -734.1f),
                    new Vector3(1070.6f, 1.7f, -687.3f), new Vector3(1018.7f, 1.4f, -640.4f), new Vector3(966.7f, 1.1f, -593.5f),
                    new Vector3(914.8f, 1.0f, -546.5f), new Vector3(863.0f, 1.0f, -499.4f), new Vector3(826.4f, 1.1f, -465.3f),
                    new Vector3(821.3f, 1.2f, -459.2f), new Vector3(817.9f, 1.2f, -454.3f), new Vector3(814.9f, 1.2f, -449.0f),
                    new Vector3(811.7f, 1.2f, -441.7f), new Vector3(809.4f, 1.2f, -434.1f), new Vector3(808.3f, 1.3f, -428.2f),
                    new Vector3(807.3f, 1.3f, -420.3f), new Vector3(807.0f, 1.3f, -414.3f), new Vector3(807.2f, 1.3f, -408.3f),
                    new Vector3(807.9f, 1.3f, -400.3f), new Vector3(809.6f, 1.4f, -388.4f), new Vector3(816.1f, 1.5f, -357.1f),
                    new Vector3(830.7f, 1.8f, -292.7f), new Vector3(833.2f, 1.9f, -276.9f), new Vector3(834.0f, 1.9f, -269.0f),
                    new Vector3(834.4f, 1.9f, -261.0f), new Vector3(834.3f, 2.0f, -251.0f), new Vector3(833.3f, 2.1f, -237.0f),
                    new Vector3(831.7f, 2.1f, -225.1f), new Vector3(830.6f, 2.1f, -219.2f), new Vector3(829.2f, 2.2f, -213.4f),
                    new Vector3(827.4f, 2.2f, -207.7f), new Vector3(825.2f, 2.2f, -202.1f), new Vector3(821.7f, 2.3f, -194.9f),
                    new Vector3(809.1f, 2.4f, -174.5f), new Vector3(770.6f, 2.8f, -116.0f), new Vector3(731.7f, 3.1f, -57.8f),
                    new Vector3(715.3f, 3.2f, -32.7f), new Vector3(711.6f, 3.3f, -25.6f), new Vector3(709.2f, 3.3f, -20.1f),
                    new Vector3(707.2f, 3.3f, -12.4f), new Vector3(707.4f, 3.4f, -4.4f), new Vector3(710.0f, 3.4f, 3.1f),
                    new Vector3(715.1f, 3.4f, 9.2f), new Vector3(721.5f, 3.5f, 14.0f), new Vector3(729.1f, 3.5f, 16.6f),
                    new Vector3(787.7f, 3.7f, 29.1f), new Vector3(811.1f, 3.8f, 34.5f), new Vector3(816.9f, 3.8f, 36.1f),
                    new Vector3(824.1f, 3.8f, 39.5f), new Vector3(830.2f, 3.8f, 44.6f), new Vector3(834.5f, 3.9f, 51.3f),
                    new Vector3(835.9f, 3.9f, 59.2f), new Vector3(834.6f, 3.9f, 67.0f), new Vector3(831.3f, 3.9f, 74.3f),
                    new Vector3(826.1f, 3.9f, 80.3f), new Vector3(819.8f, 3.9f, 85.2f), new Vector3(812.8f, 4.0f, 89.2f),
                    new Vector3(783.8f, 4.0f, 102.8f), new Vector3(759.9f, 4.0f, 112.9f), new Vector3(744.8f, 4.0f, 118.3f),
                    new Vector3(733.3f, 4.0f, 121.6f), new Vector3(717.7f, 4.0f, 125.1f), new Vector3(701.9f, 4.0f, 127.9f),
                    new Vector3(694.0f, 4.0f, 128.9f), new Vector3(684.0f, 4.1f, 129.5f), new Vector3(676.0f, 4.1f, 129.5f),
                    new Vector3(670.0f, 4.1f, 129.1f), new Vector3(662.2f, 4.1f, 127.4f), new Vector3(655.1f, 4.1f, 123.8f),
                    new Vector3(648.7f, 4.1f, 119.0f), new Vector3(597.0f, 4.3f, 71.8f), new Vector3(545.7f, 4.6f, 24.2f),
                    new Vector3(494.4f, 4.9f, -23.4f), new Vector3(443.2f, 5.2f, -71.2f), new Vector3(392.0f, 5.6f, -118.9f),
                    new Vector3(340.8f, 5.9f, -166.6f), new Vector3(289.7f, 6.3f, -214.5f), new Vector3(238.6f, 6.6f, -262.3f),
                    new Vector3(187.8f, 6.8f, -310.5f), new Vector3(180.8f, 6.8f, -317.6f), new Vector3(176.0f, 6.8f, -324.0f),
                    new Vector3(170.6f, 6.9f, -332.4f), new Vector3(166.9f, 6.9f, -339.5f), new Vector3(164.1f, 6.9f, -347.0f),
                    new Vector3(162.7f, 6.9f, -352.8f), new Vector3(161.7f, 6.9f, -358.7f), new Vector3(161.1f, 6.9f, -364.7f),
                    new Vector3(160.9f, 6.9f, -370.7f), new Vector3(161.4f, 6.9f, -378.7f), new Vector3(162.9f, 7.0f, -386.5f),
                    new Vector3(164.6f, 7.0f, -392.3f), new Vector3(166.7f, 7.0f, -397.9f), new Vector3(169.2f, 7.0f, -403.4f),
                    new Vector3(173.3f, 7.0f, -410.2f), new Vector3(179.2f, 7.0f, -415.6f), new Vector3(186.1f, 7.0f, -419.7f),
                    new Vector3(193.6f, 7.0f, -422.4f), new Vector3(209.1f, 7.0f, -426.3f), new Vector3(261.9f, 6.8f, -437.8f),
                    new Vector3(271.5f, 6.8f, -440.4f), new Vector3(279.0f, 6.7f, -443.1f), new Vector3(285.8f, 6.6f, -447.3f),
                    new Vector3(291.7f, 6.6f, -452.8f), new Vector3(296.4f, 6.5f, -459.2f), new Vector3(299.9f, 6.4f, -466.4f),
                    new Vector3(302.4f, 6.4f, -474.0f), new Vector3(303.8f, 6.3f, -481.8f), new Vector3(304.1f, 6.2f, -489.8f),
                    new Vector3(303.0f, 6.1f, -497.8f), new Vector3(300.7f, 6.0f, -505.4f), new Vector3(297.5f, 5.9f, -512.8f),
                    new Vector3(293.4f, 5.8f, -519.6f), new Vector3(288.2f, 5.7f, -525.7f), new Vector3(282.1f, 5.6f, -530.9f),
                    new Vector3(275.3f, 5.5f, -535.1f), new Vector3(270.0f, 5.4f, -537.8f), new Vector3(262.5f, 5.3f, -540.5f),
                    new Vector3(254.6f, 5.2f, -541.8f), new Vector3(246.6f, 5.1f, -541.5f), new Vector3(238.7f, 5.0f, -540.0f),
                    new Vector3(231.1f, 4.9f, -537.5f), new Vector3(225.6f, 4.8f, -535.1f), new Vector3(216.7f, 4.7f, -530.5f),
                    new Vector3(156.6f, 3.7f, -494.7f), new Vector3(99.2f, 2.8f, -458.3f), new Vector3(89.5f, 2.7f, -451.3f),
                    new Vector3(84.8f, 2.7f, -447.5f), new Vector3(78.9f, 2.6f, -442.1f), new Vector3(65.3f, 2.4f, -427.5f),
                    new Vector3(55.0f, 2.3f, -415.2f), new Vector3(40.6f, 2.1f, -396.0f), new Vector3(30.5f, 2.1f, -381.1f),
                    new Vector3(25.4f, 2.0f, -372.5f), new Vector3(21.7f, 2.0f, -365.4f), new Vector3(18.5f, 2.0f, -358.1f),
                    new Vector3(15.1f, 2.0f, -348.7f), new Vector3(9.4f, 2.0f, -329.5f), new Vector3(7.0f, 2.0f, -319.8f),
                    new Vector3(5.8f, 1.9f, -311.9f), new Vector3(4.3f, 1.9f, -298.0f), new Vector3(2.4f, 1.5f, -242.0f),
                    new Vector3(1.5f, 1.0f, -172.0f), new Vector3(0.8f, 0.4f, -102.0f), new Vector3(0.2f, 0.0f, -32.0f)
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.6f, 70.0f), new Vector3(0.8f, 3.7f, 120.0f),
                    new Vector3(2.4f, 4.0f, 127.8f), new Vector3(8.5f, 4.3f, 132.7f), new Vector3(16.3f, 4.6f, 133.4f),
                    new Vector3(23.7f, 4.9f, 130.4f), new Vector3(29.3f, 5.1f, 124.8f), new Vector3(35.3f, 5.4f, 116.8f),
                    new Vector3(74.8f, 3.9f, 59.0f), new Vector3(113.7f, -10.9f, 0.7f), new Vector3(124.8f, -16.6f, -18.3f),
                    new Vector3(139.3f, -23.9f, -46.8f), new Vector3(157.8f, -29.8f, -88.9f), new Vector3(171.4f, -29.1f, -124.4f),
                    new Vector3(178.0f, -27.6f, -145.4f), new Vector3(182.5f, -25.9f, -162.8f), new Vector3(196.1f, -18.2f, -231.5f),
                    new Vector3(208.8f, -14.0f, -300.3f), new Vector3(221.5f, -5.6f, -369.2f), new Vector3(234.4f, 9.9f, -438.0f),
                    new Vector3(239.0f, 13.9f, -459.5f), new Vector3(240.9f, 14.8f, -465.2f), new Vector3(243.2f, 15.6f, -470.7f),
                    new Vector3(250.6f, 17.3f, -484.9f), new Vector3(269.8f, 18.3f, -522.3f), new Vector3(273.8f, 18.6f, -531.5f),
                    new Vector3(275.8f, 18.8f, -537.1f), new Vector3(277.5f, 19.0f, -542.9f), new Vector3(278.8f, 19.3f, -548.7f),
                    new Vector3(279.7f, 19.6f, -554.7f), new Vector3(280.4f, 20.0f, -562.6f), new Vector3(280.7f, 20.6f, -572.6f),
                    new Vector3(280.4f, 21.3f, -582.6f), new Vector3(279.7f, 21.9f, -590.6f), new Vector3(278.5f, 22.5f, -598.5f),
                    new Vector3(276.9f, 23.1f, -606.3f), new Vector3(274.8f, 23.8f, -614.1f), new Vector3(270.9f, 24.8f, -625.4f),
                    new Vector3(258.2f, 27.9f, -657.0f), new Vector3(254.1f, 29.1f, -668.2f), new Vector3(252.4f, 29.6f, -674.0f),
                    new Vector3(251.1f, 30.2f, -679.9f), new Vector3(250.2f, 30.8f, -685.8f), new Vector3(249.6f, 31.5f, -693.8f),
                    new Vector3(249.5f, 32.3f, -701.8f), new Vector3(251.9f, 37.7f, -771.7f), new Vector3(254.8f, 40.0f, -841.7f),
                    new Vector3(257.7f, 39.8f, -911.6f), new Vector3(259.4f, 39.3f, -975.6f), new Vector3(258.9f, 39.1f, -989.6f),
                    new Vector3(256.4f, 38.8f, -1015.5f), new Vector3(254.4f, 38.7f, -1029.4f), new Vector3(249.5f, 38.4f, -1052.9f),
                    new Vector3(233.1f, 37.5f, -1120.9f), new Vector3(216.6f, 36.8f, -1189.0f), new Vector3(200.1f, 36.2f, -1257.0f),
                    new Vector3(183.7f, 36.0f, -1325.1f), new Vector3(167.4f, 35.6f, -1393.2f), new Vector3(151.0f, 34.7f, -1461.2f),
                    new Vector3(134.7f, 33.4f, -1529.3f), new Vector3(118.3f, 31.9f, -1597.4f), new Vector3(101.9f, 30.4f, -1665.5f),
                    new Vector3(85.2f, 29.1f, -1733.5f), new Vector3(75.4f, 28.6f, -1770.2f), new Vector3(73.4f, 28.5f, -1775.8f),
                    new Vector3(70.7f, 28.5f, -1781.2f), new Vector3(67.5f, 28.4f, -1786.2f), new Vector3(63.7f, 28.4f, -1790.9f),
                    new Vector3(59.4f, 28.3f, -1795.0f), new Vector3(54.4f, 28.2f, -1798.3f), new Vector3(48.9f, 28.2f, -1800.8f),
                    new Vector3(43.2f, 28.2f, -1802.7f), new Vector3(37.4f, 28.1f, -1804.1f), new Vector3(31.4f, 28.1f, -1804.9f),
                    new Vector3(25.4f, 28.1f, -1805.2f), new Vector3(17.4f, 28.0f, -1805.2f), new Vector3(11.5f, 28.0f, -1805.7f),
                    new Vector3(5.6f, 28.0f, -1807.0f), new Vector3(-0.1f, 28.0f, -1809.0f), new Vector3(-5.4f, 28.0f, -1811.7f),
                    new Vector3(-10.3f, 28.0f, -1815.1f), new Vector3(-15.0f, 28.0f, -1818.9f), new Vector3(-19.2f, 28.0f, -1823.2f),
                    new Vector3(-22.9f, 27.9f, -1827.9f), new Vector3(-25.8f, 27.9f, -1833.2f), new Vector3(-28.0f, 27.9f, -1838.7f),
                    new Vector3(-47.5f, 27.1f, -1899.7f), new Vector3(-57.0f, 26.5f, -1928.2f), new Vector3(-60.0f, 26.3f, -1935.6f),
                    new Vector3(-62.9f, 26.2f, -1940.8f), new Vector3(-66.3f, 26.0f, -1945.8f), new Vector3(-70.1f, 25.9f, -1950.4f),
                    new Vector3(-74.5f, 25.7f, -1954.5f), new Vector3(-79.4f, 25.6f, -1957.9f), new Vector3(-84.7f, 25.4f, -1960.8f),
                    new Vector3(-90.2f, 25.3f, -1963.2f), new Vector3(-95.9f, 25.1f, -1965.1f), new Vector3(-101.7f, 24.9f, -1966.4f),
                    new Vector3(-125.6f, 24.3f, -1969.0f), new Vector3(-195.3f, 22.0f, -1975.4f), new Vector3(-265.1f, 19.6f, -1981.6f),
                    new Vector3(-334.8f, 17.3f, -1987.5f), new Vector3(-404.7f, 15.1f, -1992.5f), new Vector3(-410.6f, 15.0f, -1992.2f),
                    new Vector3(-416.5f, 14.8f, -1990.8f), new Vector3(-422.1f, 14.7f, -1988.6f), new Vector3(-427.3f, 14.5f, -1985.6f),
                    new Vector3(-432.0f, 14.3f, -1982.0f), new Vector3(-436.4f, 14.2f, -1977.9f), new Vector3(-440.2f, 14.0f, -1973.2f),
                    new Vector3(-443.4f, 13.9f, -1968.2f), new Vector3(-445.9f, 13.8f, -1962.7f), new Vector3(-447.7f, 13.6f, -1957.0f),
                    new Vector3(-448.7f, 13.5f, -1951.1f), new Vector3(-449.0f, 13.4f, -1945.1f), new Vector3(-448.6f, 13.3f, -1939.1f),
                    new Vector3(-447.4f, 13.1f, -1933.3f), new Vector3(-445.6f, 13.0f, -1927.6f), new Vector3(-443.2f, 12.9f, -1922.0f),
                    new Vector3(-440.3f, 12.8f, -1916.8f), new Vector3(-436.5f, 12.7f, -1912.1f), new Vector3(-432.1f, 12.6f, -1908.0f),
                    new Vector3(-427.3f, 12.6f, -1904.4f), new Vector3(-422.3f, 12.5f, -1901.2f), new Vector3(-416.9f, 12.4f, -1898.5f),
                    new Vector3(-411.3f, 12.3f, -1896.5f), new Vector3(-405.4f, 12.3f, -1895.3f), new Vector3(-389.4f, 12.1f, -1894.2f),
                    new Vector3(-319.5f, 11.9f, -1891.2f), new Vector3(-277.6f, 11.3f, -1888.7f), new Vector3(-271.6f, 11.2f, -1888.0f),
                    new Vector3(-265.8f, 11.1f, -1886.6f), new Vector3(-260.2f, 11.0f, -1884.5f), new Vector3(-254.9f, 10.9f, -1881.6f),
                    new Vector3(-250.1f, 10.7f, -1878.0f), new Vector3(-245.8f, 10.6f, -1873.8f), new Vector3(-242.1f, 10.4f, -1869.1f),
                    new Vector3(-239.0f, 10.3f, -1864.0f), new Vector3(-236.4f, 10.1f, -1858.6f), new Vector3(-234.7f, 10.0f, -1852.8f),
                    new Vector3(-228.5f, 9.2f, -1825.5f), new Vector3(-214.9f, 6.8f, -1756.8f), new Vector3(-202.6f, 4.6f, -1700.2f),
                    new Vector3(-192.4f, 3.3f, -1665.6f), new Vector3(-170.4f, 0.9f, -1599.1f), new Vector3(-147.7f, -1.0f, -1532.9f),
                    new Vector3(-124.1f, -1.9f, -1467.0f), new Vector3(-107.1f, -2.1f, -1420.0f), new Vector3(-105.5f, -2.1f, -1414.2f),
                    new Vector3(-104.4f, -2.1f, -1408.3f), new Vector3(-103.7f, -2.2f, -1402.3f), new Vector3(-103.4f, -2.2f, -1396.3f),
                    new Vector3(-103.5f, -2.3f, -1390.3f), new Vector3(-104.0f, -2.4f, -1384.4f), new Vector3(-105.0f, -2.4f, -1378.4f),
                    new Vector3(-106.7f, -2.5f, -1370.6f), new Vector3(-109.0f, -2.6f, -1363.0f), new Vector3(-111.0f, -2.7f, -1357.3f),
                    new Vector3(-113.3f, -2.8f, -1351.8f), new Vector3(-116.0f, -2.9f, -1346.4f), new Vector3(-118.9f, -3.0f, -1341.2f),
                    new Vector3(-122.2f, -3.1f, -1336.2f), new Vector3(-125.8f, -3.2f, -1331.3f), new Vector3(-129.7f, -3.3f, -1326.8f),
                    new Vector3(-133.8f, -3.5f, -1322.4f), new Vector3(-138.3f, -3.6f, -1318.4f), new Vector3(-144.6f, -3.7f, -1313.5f),
                    new Vector3(-179.4f, -4.7f, -1289.9f), new Vector3(-198.1f, -5.2f, -1278.3f), new Vector3(-206.9f, -5.5f, -1273.5f),
                    new Vector3(-212.3f, -5.6f, -1271.0f), new Vector3(-219.8f, -5.9f, -1268.1f), new Vector3(-227.4f, -6.1f, -1265.7f),
                    new Vector3(-235.1f, -6.3f, -1263.7f), new Vector3(-244.9f, -6.5f, -1261.7f), new Vector3(-256.8f, -6.9f, -1260.0f),
                    new Vector3(-264.8f, -7.1f, -1259.4f), new Vector3(-272.8f, -7.3f, -1259.3f), new Vector3(-278.8f, -7.5f, -1259.5f),
                    new Vector3(-286.7f, -7.7f, -1260.4f), new Vector3(-296.6f, -7.9f, -1262.1f), new Vector3(-306.4f, -8.2f, -1264.3f),
                    new Vector3(-314.1f, -8.4f, -1266.4f), new Vector3(-321.6f, -8.6f, -1269.1f), new Vector3(-327.1f, -8.8f, -1271.5f),
                    new Vector3(-336.0f, -9.0f, -1276.0f), new Vector3(-348.1f, -9.4f, -1283.1f), new Vector3(-356.5f, -9.6f, -1288.6f),
                    new Vector3(-402.5f, -10.8f, -1323.8f), new Vector3(-457.3f, -11.8f, -1367.4f), new Vector3(-512.1f, -12.0f, -1411.0f),
                    new Vector3(-566.9f, -12.3f, -1454.6f), new Vector3(-606.7f, -12.6f, -1484.9f), new Vector3(-611.7f, -12.7f, -1488.2f),
                    new Vector3(-616.9f, -12.7f, -1491.2f), new Vector3(-622.3f, -12.8f, -1493.8f), new Vector3(-627.9f, -12.8f, -1496.0f),
                    new Vector3(-633.6f, -12.9f, -1497.7f), new Vector3(-639.5f, -12.9f, -1498.8f), new Vector3(-645.5f, -13.0f, -1499.4f),
                    new Vector3(-651.5f, -13.1f, -1499.6f), new Vector3(-657.5f, -13.1f, -1499.3f), new Vector3(-663.4f, -13.2f, -1498.6f),
                    new Vector3(-669.3f, -13.2f, -1497.5f), new Vector3(-675.1f, -13.3f, -1495.9f), new Vector3(-680.8f, -13.4f, -1493.9f),
                    new Vector3(-686.2f, -13.4f, -1491.4f), new Vector3(-691.5f, -13.5f, -1488.5f), new Vector3(-696.4f, -13.5f, -1485.1f),
                    new Vector3(-701.1f, -13.6f, -1481.3f), new Vector3(-705.4f, -13.7f, -1477.1f), new Vector3(-709.4f, -13.7f, -1472.7f),
                    new Vector3(-713.1f, -13.8f, -1467.9f), new Vector3(-716.3f, -13.9f, -1462.9f), new Vector3(-722.2f, -14.0f, -1452.4f),
                    new Vector3(-729.9f, -14.2f, -1438.4f), new Vector3(-733.1f, -14.2f, -1433.3f), new Vector3(-736.7f, -14.3f, -1428.5f),
                    new Vector3(-740.6f, -14.4f, -1423.9f), new Vector3(-744.9f, -14.4f, -1419.7f), new Vector3(-749.5f, -14.5f, -1415.9f),
                    new Vector3(-754.3f, -14.5f, -1412.4f), new Vector3(-759.5f, -14.6f, -1409.3f), new Vector3(-764.9f, -14.7f, -1406.7f),
                    new Vector3(-770.4f, -14.7f, -1404.4f), new Vector3(-776.1f, -14.8f, -1402.5f), new Vector3(-781.9f, -14.9f, -1401.0f),
                    new Vector3(-787.8f, -14.9f, -1400.0f), new Vector3(-793.8f, -15.0f, -1399.5f), new Vector3(-799.8f, -15.0f, -1399.6f),
                    new Vector3(-805.8f, -15.1f, -1400.2f), new Vector3(-811.7f, -15.1f, -1401.4f), new Vector3(-817.4f, -15.2f, -1403.2f),
                    new Vector3(-832.1f, -15.3f, -1409.5f), new Vector3(-876.6f, -15.7f, -1432.3f), new Vector3(-938.6f, -16.0f, -1464.9f),
                    new Vector3(-1000.9f, -15.8f, -1496.9f), new Vector3(-1006.3f, -15.8f, -1499.3f), new Vector3(-1014.1f, -15.7f, -1501.3f),
                    new Vector3(-1020.0f, -15.6f, -1501.8f), new Vector3(-1026.0f, -15.6f, -1501.5f), new Vector3(-1031.9f, -15.5f, -1500.5f),
                    new Vector3(-1037.7f, -15.4f, -1498.8f), new Vector3(-1043.2f, -15.3f, -1496.5f), new Vector3(-1048.5f, -15.2f, -1493.5f),
                    new Vector3(-1053.3f, -15.1f, -1490.1f), new Vector3(-1057.7f, -15.0f, -1485.9f), new Vector3(-1061.3f, -14.9f, -1481.2f),
                    new Vector3(-1064.3f, -14.8f, -1476.0f), new Vector3(-1077.3f, -14.2f, -1448.9f), new Vector3(-1106.1f, -12.6f, -1385.1f),
                    new Vector3(-1114.8f, -12.0f, -1364.9f), new Vector3(-1117.4f, -11.8f, -1357.3f), new Vector3(-1118.9f, -11.6f, -1351.5f),
                    new Vector3(-1119.9f, -11.4f, -1343.6f), new Vector3(-1120.9f, -11.1f, -1331.6f), new Vector3(-1120.9f, -11.0f, -1325.6f),
                    new Vector3(-1120.6f, -10.8f, -1319.6f), new Vector3(-1119.8f, -10.6f, -1313.7f), new Vector3(-1118.6f, -10.5f, -1307.8f),
                    new Vector3(-1116.6f, -10.3f, -1300.1f), new Vector3(-1114.8f, -10.1f, -1294.4f), new Vector3(-1112.5f, -9.9f, -1288.8f),
                    new Vector3(-1110.0f, -9.8f, -1283.4f), new Vector3(-1107.0f, -9.6f, -1278.1f), new Vector3(-1103.8f, -9.5f, -1273.1f),
                    new Vector3(-1099.1f, -9.3f, -1266.6f), new Vector3(-1095.3f, -9.1f, -1262.0f), new Vector3(-1091.2f, -9.0f, -1257.6f),
                    new Vector3(-1086.8f, -8.8f, -1253.5f), new Vector3(-1039.4f, -7.4f, -1213.5f), new Vector3(-1028.1f, -7.2f, -1205.2f),
                    new Vector3(-1014.8f, -6.9f, -1196.3f), new Vector3(-994.1f, -6.5f, -1184.2f), new Vector3(-974.6f, -6.3f, -1174.0f),
                    new Vector3(-956.3f, -6.1f, -1165.9f), new Vector3(-930.1f, -6.0f, -1155.9f), new Vector3(-892.0f, -5.9f, -1143.9f),
                    new Vector3(-824.3f, -5.2f, -1126.1f), new Vector3(-796.9f, -4.7f, -1120.4f), new Vector3(-777.1f, -4.4f, -1117.5f),
                    new Vector3(-759.2f, -4.0f, -1115.9f), new Vector3(-745.2f, -3.7f, -1115.5f), new Vector3(-675.2f, -1.9f, -1117.3f),
                    new Vector3(-605.2f, 0.2f, -1119.7f), new Vector3(-535.2f, 2.3f, -1121.3f), new Vector3(-523.2f, 2.6f, -1120.7f),
                    new Vector3(-507.3f, 3.1f, -1118.9f), new Vector3(-491.5f, 3.5f, -1116.3f), new Vector3(-475.9f, 4.0f, -1112.9f),
                    new Vector3(-462.4f, 4.4f, -1109.0f), new Vector3(-449.2f, 4.7f, -1104.4f), new Vector3(-427.0f, 5.3f, -1095.4f),
                    new Vector3(-417.9f, 5.6f, -1091.1f), new Vector3(-403.9f, 5.9f, -1083.4f), new Vector3(-376.8f, 6.6f, -1066.3f),
                    new Vector3(-319.9f, 7.6f, -1025.6f), new Vector3(-263.6f, 8.0f, -984.0f), new Vector3(-252.6f, 8.0f, -975.3f),
                    new Vector3(-246.6f, 8.0f, -970.0f), new Vector3(-241.0f, 8.0f, -964.3f), new Vector3(-235.7f, 8.0f, -958.3f),
                    new Vector3(-230.8f, 8.0f, -952.0f), new Vector3(-225.2f, 7.9f, -943.7f), new Vector3(-221.1f, 7.9f, -936.8f),
                    new Vector3(-217.4f, 7.9f, -929.7f), new Vector3(-214.2f, 7.9f, -922.4f), new Vector3(-212.2f, 7.9f, -916.8f),
                    new Vector3(-210.6f, 7.8f, -911.0f), new Vector3(-207.7f, 7.8f, -897.3f), new Vector3(-197.0f, 7.5f, -828.1f),
                    new Vector3(-186.4f, 7.2f, -758.9f), new Vector3(-174.9f, 6.8f, -689.8f), new Vector3(-170.8f, 6.7f, -670.2f),
                    new Vector3(-165.4f, 6.6f, -651.0f), new Vector3(-154.2f, 6.4f, -616.8f), new Vector3(-140.2f, 6.3f, -581.4f),
                    new Vector3(-110.7f, 6.1f, -517.9f), new Vector3(-80.2f, 6.0f, -454.9f), new Vector3(-68.3f, 5.8f, -431.8f),
                    new Vector3(-63.3f, 5.7f, -425.7f), new Vector3(-55.9f, 5.6f, -422.9f), new Vector3(-48.2f, 5.5f, -424.6f),
                    new Vector3(-43.1f, 5.4f, -427.8f), new Vector3(-36.7f, 5.3f, -432.5f), new Vector3(-31.6f, 5.2f, -435.7f),
                    new Vector3(-26.4f, 5.1f, -438.7f), new Vector3(-20.9f, 5.0f, -441.2f), new Vector3(-13.2f, 4.9f, -443.0f),
                    new Vector3(-5.5f, 4.7f, -441.0f), new Vector3(0.3f, 4.6f, -435.6f), new Vector3(2.7f, 4.4f, -428.1f),
                    new Vector3(3.0f, 4.3f, -422.1f), new Vector3(1.5f, 2.8f, -352.1f), new Vector3(0.6f, 1.6f, -282.1f),
                    new Vector3(0.4f, 1.0f, -212.0f), new Vector3(0.3f, 0.8f, -142.0f), new Vector3(0.2f, 0.3f, -72.0f)
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
                // Real activation-zone count: THREE.
                DrsZoneThreeNormalized = new Vector2(0.72f, 0.82f),
                TargetLengthMeters = 4940f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 70.0f), new Vector3(1.9f, 0.0f, 140.0f),
                    new Vector3(4.0f, 0.0f, 204.0f), new Vector3(3.6f, 0.0f, 210.0f), new Vector3(2.4f, 0.0f, 215.9f),
                    new Vector3(0.2f, 0.0f, 221.4f), new Vector3(-5.1f, 0.0f, 227.4f), new Vector3(-12.1f, 0.0f, 231.0f),
                    new Vector3(-18.0f, 0.0f, 232.3f), new Vector3(-24.0f, 0.0f, 232.9f), new Vector3(-32.0f, 0.0f, 233.5f),
                    new Vector3(-37.9f, 0.0f, 234.4f), new Vector3(-43.7f, 0.0f, 235.9f), new Vector3(-49.3f, 0.0f, 238.1f),
                    new Vector3(-54.7f, 0.0f, 240.7f), new Vector3(-61.7f, 0.0f, 244.6f), new Vector3(-70.1f, 0.0f, 250.0f),
                    new Vector3(-76.6f, 0.0f, 254.7f), new Vector3(-81.1f, 0.0f, 258.6f), new Vector3(-85.2f, 0.0f, 263.0f),
                    new Vector3(-88.7f, 0.0f, 267.9f), new Vector3(-94.6f, 0.0f, 278.3f), new Vector3(-101.0f, 0.0f, 288.5f),
                    new Vector3(-104.7f, 0.0f, 293.2f), new Vector3(-111.3f, 0.0f, 297.6f), new Vector3(-117.2f, 0.0f, 298.9f),
                    new Vector3(-125.0f, 0.0f, 298.3f), new Vector3(-130.3f, 0.0f, 295.5f), new Vector3(-135.0f, 0.0f, 291.7f),
                    new Vector3(-139.2f, 0.0f, 287.4f), new Vector3(-142.9f, 0.0f, 282.7f), new Vector3(-146.0f, 0.0f, 277.6f),
                    new Vector3(-148.6f, 0.0f, 272.2f), new Vector3(-150.8f, 0.0f, 266.6f), new Vector3(-153.8f, 0.0f, 257.0f),
                    new Vector3(-158.7f, 0.0f, 237.6f), new Vector3(-164.3f, 0.0f, 210.2f), new Vector3(-165.7f, 0.0f, 200.3f),
                    new Vector3(-166.3f, 0.0f, 192.3f), new Vector3(-166.5f, 0.0f, 184.3f), new Vector3(-166.3f, 0.0f, 178.3f),
                    new Vector3(-165.7f, 0.0f, 172.3f), new Vector3(-164.7f, 0.0f, 166.4f), new Vector3(-162.4f, 0.0f, 156.7f),
                    new Vector3(-157.9f, 0.0f, 141.3f), new Vector3(-143.2f, 0.0f, 77.0f), new Vector3(-137.2f, 0.0f, 43.5f),
                    new Vector3(-134.8f, 0.0f, 23.6f), new Vector3(-133.5f, 0.0f, -0.3f), new Vector3(-133.4f, 0.0f, -20.3f),
                    new Vector3(-134.4f, 0.0f, -38.3f), new Vector3(-135.4f, 0.0f, -48.3f), new Vector3(-136.4f, 0.0f, -54.2f),
                    new Vector3(-137.9f, 0.0f, -60.0f), new Vector3(-140.2f, 0.0f, -65.5f), new Vector3(-143.1f, 0.0f, -70.8f),
                    new Vector3(-146.6f, 0.0f, -75.7f), new Vector3(-150.6f, 0.0f, -80.1f), new Vector3(-155.2f, 0.0f, -84.0f),
                    new Vector3(-160.3f, 0.0f, -87.1f), new Vector3(-165.7f, 0.0f, -89.7f), new Vector3(-171.3f, 0.0f, -92.0f),
                    new Vector3(-178.8f, 0.0f, -94.6f), new Vector3(-184.6f, 0.0f, -96.2f), new Vector3(-190.5f, 0.0f, -97.3f),
                    new Vector3(-198.5f, 0.0f, -97.2f), new Vector3(-267.5f, 0.0f, -85.4f), new Vector3(-336.4f, 0.0f, -72.7f),
                    new Vector3(-405.2f, 0.0f, -59.7f), new Vector3(-474.0f, 0.0f, -46.6f), new Vector3(-542.7f, 0.0f, -33.0f),
                    new Vector3(-572.0f, 0.0f, -26.5f), new Vector3(-577.7f, 0.0f, -24.7f), new Vector3(-585.1f, 0.0f, -21.8f),
                    new Vector3(-596.1f, 0.0f, -16.9f), new Vector3(-606.8f, 0.0f, -11.4f), new Vector3(-613.6f, 0.0f, -7.3f),
                    new Vector3(-641.6f, 0.0f, 12.0f), new Vector3(-697.9f, 0.0f, 53.6f), new Vector3(-753.7f, 0.0f, 96.0f),
                    new Vector3(-809.4f, 0.0f, 138.4f), new Vector3(-865.0f, 0.0f, 181.0f), new Vector3(-873.1f, 0.0f, 186.9f),
                    new Vector3(-879.8f, 0.0f, 191.3f), new Vector3(-885.1f, 0.0f, 194.0f), new Vector3(-892.9f, 0.0f, 194.8f),
                    new Vector3(-899.5f, 0.0f, 190.5f), new Vector3(-903.6f, 0.0f, 186.0f), new Vector3(-946.8f, 0.0f, 130.9f),
                    new Vector3(-987.9f, 0.0f, 74.3f), new Vector3(-1015.1f, 0.0f, 34.7f), new Vector3(-1021.1f, 0.0f, 29.6f),
                    new Vector3(-1028.7f, 0.0f, 31.3f), new Vector3(-1034.2f, 0.0f, 37.1f), new Vector3(-1076.4f, 0.0f, 93.0f),
                    new Vector3(-1111.7f, 0.0f, 139.0f), new Vector3(-1118.2f, 0.0f, 146.6f), new Vector3(-1122.4f, 0.0f, 150.9f),
                    new Vector3(-1135.9f, 0.0f, 162.8f), new Vector3(-1140.6f, 0.0f, 166.6f), new Vector3(-1145.6f, 0.0f, 169.8f),
                    new Vector3(-1153.2f, 0.0f, 172.1f), new Vector3(-1159.2f, 0.0f, 172.3f), new Vector3(-1165.2f, 0.0f, 171.6f),
                    new Vector3(-1172.8f, 0.0f, 169.2f), new Vector3(-1182.7f, 0.0f, 162.4f), new Vector3(-1187.4f, 0.0f, 158.7f),
                    new Vector3(-1191.7f, 0.0f, 154.5f), new Vector3(-1195.6f, 0.0f, 150.0f), new Vector3(-1201.7f, 0.0f, 142.1f),
                    new Vector3(-1243.2f, 0.0f, 85.7f), new Vector3(-1284.8f, 0.0f, 29.3f), new Vector3(-1326.4f, 0.0f, -27.0f),
                    new Vector3(-1368.0f, 0.0f, -83.3f), new Vector3(-1409.9f, 0.0f, -139.4f), new Vector3(-1414.7f, 0.0f, -145.9f),
                    new Vector3(-1417.9f, 0.0f, -150.9f), new Vector3(-1420.6f, 0.0f, -156.3f), new Vector3(-1422.3f, 0.0f, -164.1f),
                    new Vector3(-1422.6f, 0.0f, -172.0f), new Vector3(-1422.4f, 0.0f, -180.0f), new Vector3(-1421.8f, 0.0f, -186.0f),
                    new Vector3(-1420.6f, 0.0f, -191.9f), new Vector3(-1418.4f, 0.0f, -197.5f), new Vector3(-1415.3f, 0.0f, -202.6f),
                    new Vector3(-1411.6f, 0.0f, -207.3f), new Vector3(-1404.9f, 0.0f, -214.7f), new Vector3(-1394.8f, 0.0f, -224.5f),
                    new Vector3(-1375.3f, 0.0f, -241.7f), new Vector3(-1370.5f, 0.0f, -245.3f), new Vector3(-1365.6f, 0.0f, -248.6f),
                    new Vector3(-1360.3f, 0.0f, -251.6f), new Vector3(-1342.4f, 0.0f, -260.5f), new Vector3(-1336.9f, 0.0f, -266.1f),
                    new Vector3(-1337.8f, 0.0f, -273.8f), new Vector3(-1341.0f, 0.0f, -278.9f), new Vector3(-1343.8f, 0.0f, -284.2f),
                    new Vector3(-1345.9f, 0.0f, -289.8f), new Vector3(-1347.6f, 0.0f, -295.6f), new Vector3(-1348.6f, 0.0f, -301.5f),
                    new Vector3(-1348.7f, 0.0f, -307.5f), new Vector3(-1348.2f, 0.0f, -313.4f), new Vector3(-1346.8f, 0.0f, -323.3f),
                    new Vector3(-1345.1f, 0.0f, -331.2f), new Vector3(-1343.4f, 0.0f, -336.9f), new Vector3(-1341.0f, 0.0f, -342.4f),
                    new Vector3(-1337.9f, 0.0f, -347.5f), new Vector3(-1334.4f, 0.0f, -352.4f), new Vector3(-1315.4f, 0.0f, -375.6f),
                    new Vector3(-1293.8f, 0.0f, -399.3f), new Vector3(-1266.4f, 0.0f, -428.4f), new Vector3(-1262.6f, 0.0f, -433.1f),
                    new Vector3(-1259.7f, 0.0f, -438.3f), new Vector3(-1257.5f, 0.0f, -443.9f), new Vector3(-1254.2f, 0.0f, -453.3f),
                    new Vector3(-1251.9f, 0.0f, -458.9f), new Vector3(-1248.4f, 0.0f, -466.1f), new Vector3(-1239.5f, 0.0f, -481.8f),
                    new Vector3(-1236.3f, 0.0f, -486.8f), new Vector3(-1230.8f, 0.0f, -492.4f), new Vector3(-1224.9f, 0.0f, -493.8f),
                    new Vector3(-1218.9f, 0.0f, -494.3f), new Vector3(-1213.0f, 0.0f, -493.7f), new Vector3(-1205.7f, 0.0f, -490.5f),
                    new Vector3(-1201.0f, 0.0f, -486.7f), new Vector3(-1196.8f, 0.0f, -480.0f), new Vector3(-1174.2f, 0.0f, -413.7f),
                    new Vector3(-1152.0f, 0.0f, -347.3f), new Vector3(-1129.9f, 0.0f, -280.9f), new Vector3(-1107.9f, 0.0f, -214.4f),
                    new Vector3(-1086.0f, 0.0f, -147.9f), new Vector3(-1064.4f, 0.0f, -85.5f), new Vector3(-1052.7f, 0.0f, -57.9f),
                    new Vector3(-1038.8f, 0.0f, -29.0f), new Vector3(-1034.0f, 0.0f, -20.3f), new Vector3(-1028.8f, 0.0f, -14.3f),
                    new Vector3(-1023.7f, 0.0f, -11.2f), new Vector3(-1018.2f, 0.0f, -8.7f), new Vector3(-1012.3f, 0.0f, -7.4f),
                    new Vector3(-1006.4f, 0.0f, -7.1f), new Vector3(-1000.4f, 0.0f, -7.8f), new Vector3(-993.0f, 0.0f, -10.7f),
                    new Vector3(-987.2f, 0.0f, -16.1f), new Vector3(-940.3f, 0.0f, -68.1f), new Vector3(-893.8f, 0.0f, -120.6f),
                    new Vector3(-862.5f, 0.0f, -154.3f), new Vector3(-853.8f, 0.0f, -162.6f), new Vector3(-849.2f, 0.0f, -166.4f),
                    new Vector3(-844.3f, 0.0f, -169.8f), new Vector3(-837.4f, 0.0f, -173.9f), new Vector3(-830.3f, 0.0f, -177.6f),
                    new Vector3(-823.0f, 0.0f, -180.8f), new Vector3(-813.6f, 0.0f, -184.2f), new Vector3(-800.2f, 0.0f, -188.2f),
                    new Vector3(-782.6f, 0.0f, -192.2f), new Vector3(-713.9f, 0.0f, -205.4f), new Vector3(-645.0f, 0.0f, -218.1f),
                    new Vector3(-576.1f, 0.0f, -230.7f), new Vector3(-507.2f, 0.0f, -243.3f), new Vector3(-438.4f, 0.0f, -256.1f),
                    new Vector3(-373.4f, 0.0f, -267.6f), new Vector3(-365.5f, 0.0f, -269.2f), new Vector3(-358.2f, 0.0f, -272.2f),
                    new Vector3(-356.2f, 0.0f, -279.7f), new Vector3(-357.1f, 0.0f, -285.6f), new Vector3(-358.1f, 0.0f, -295.5f),
                    new Vector3(-358.4f, 0.0f, -301.5f), new Vector3(-358.0f, 0.0f, -307.5f), new Vector3(-357.1f, 0.0f, -313.5f),
                    new Vector3(-355.7f, 0.0f, -319.3f), new Vector3(-353.8f, 0.0f, -325.0f), new Vector3(-351.1f, 0.0f, -330.3f),
                    new Vector3(-347.4f, 0.0f, -335.1f), new Vector3(-343.2f, 0.0f, -339.3f), new Vector3(-338.4f, 0.0f, -342.9f),
                    new Vector3(-333.1f, 0.0f, -345.8f), new Vector3(-327.6f, 0.0f, -348.2f), new Vector3(-320.0f, 0.0f, -350.5f),
                    new Vector3(-252.0f, 0.0f, -367.5f), new Vector3(-183.7f, 0.0f, -383.0f), new Vector3(-115.0f, 0.0f, -396.3f),
                    new Vector3(-79.5f, 0.0f, -402.7f), new Vector3(-73.6f, 0.0f, -403.1f), new Vector3(-67.6f, 0.0f, -402.5f),
                    new Vector3(-60.7f, 0.0f, -398.7f), new Vector3(-56.5f, 0.0f, -394.4f), new Vector3(-10.9f, 0.0f, -341.2f),
                    new Vector3(-7.2f, 0.0f, -336.5f), new Vector3(-4.0f, 0.0f, -331.4f), new Vector3(-1.7f, 0.0f, -325.9f),
                    new Vector3(-0.4f, 0.0f, -320.0f), new Vector3(0.4f, 0.0f, -314.1f), new Vector3(0.8f, 0.0f, -306.1f),
                    new Vector3(1.1f, 0.0f, -236.1f), new Vector3(1.0f, 0.0f, -166.1f), new Vector3(0.7f, 0.0f, -96.0f),
                    new Vector3(0.2f, 0.0f, -26.0f)
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
                // Real activation-zone count: THREE.
                DrsZoneThreeNormalized = new Vector2(0.73f, 0.82f),
                TargetLengthMeters = 5278f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.1f, 70.0f), new Vector3(0.5f, 0.2f, 140.0f),
                    new Vector3(1.2f, 0.5f, 210.1f), new Vector3(1.9f, 0.8f, 264.1f), new Vector3(4.0f, 0.8f, 271.7f),
                    new Vector3(7.0f, 0.9f, 276.9f), new Vector3(13.4f, 0.9f, 281.5f), new Vector3(22.7f, 0.9f, 285.1f),
                    new Vector3(28.4f, 1.0f, 287.1f), new Vector3(36.0f, 1.0f, 289.4f), new Vector3(41.7f, 1.1f, 291.6f),
                    new Vector3(47.0f, 1.1f, 294.2f), new Vector3(52.2f, 1.1f, 297.2f), new Vector3(58.9f, 1.2f, 301.6f),
                    new Vector3(68.5f, 1.2f, 308.9f), new Vector3(76.1f, 1.3f, 315.3f), new Vector3(80.5f, 1.3f, 319.5f),
                    new Vector3(84.5f, 1.4f, 323.9f), new Vector3(91.9f, 1.4f, 333.4f), new Vector3(98.8f, 1.5f, 343.2f),
                    new Vector3(101.9f, 1.5f, 348.3f), new Vector3(104.7f, 1.6f, 353.6f), new Vector3(107.1f, 1.6f, 359.1f),
                    new Vector3(109.1f, 1.6f, 364.8f), new Vector3(111.3f, 1.7f, 372.5f), new Vector3(112.6f, 1.7f, 378.3f),
                    new Vector3(113.6f, 1.7f, 384.3f), new Vector3(114.1f, 1.8f, 390.2f), new Vector3(114.6f, 1.9f, 408.2f),
                    new Vector3(113.3f, 2.2f, 462.2f), new Vector3(112.7f, 2.5f, 532.2f), new Vector3(114.9f, 2.8f, 602.2f),
                    new Vector3(116.2f, 2.8f, 622.2f), new Vector3(123.8f, 3.0f, 691.8f), new Vector3(130.3f, 3.0f, 737.3f),
                    new Vector3(137.5f, 3.0f, 772.6f), new Vector3(155.1f, 3.1f, 840.4f), new Vector3(173.3f, 3.3f, 903.8f),
                    new Vector3(179.4f, 3.4f, 920.8f), new Vector3(182.5f, 3.4f, 928.1f), new Vector3(185.4f, 3.4f, 933.4f),
                    new Vector3(190.9f, 3.4f, 939.1f), new Vector3(198.2f, 3.5f, 942.3f), new Vector3(206.1f, 3.5f, 941.6f),
                    new Vector3(211.6f, 3.5f, 939.3f), new Vector3(216.8f, 3.5f, 936.2f), new Vector3(238.3f, 3.6f, 921.6f),
                    new Vector3(296.7f, 4.0f, 883.0f), new Vector3(301.8f, 4.0f, 879.9f), new Vector3(307.3f, 4.0f, 877.5f),
                    new Vector3(315.2f, 4.1f, 876.4f), new Vector3(322.7f, 4.1f, 878.9f), new Vector3(328.1f, 4.1f, 881.6f),
                    new Vector3(333.2f, 4.1f, 884.8f), new Vector3(337.9f, 4.2f, 888.4f), new Vector3(343.7f, 4.2f, 894.0f),
                    new Vector3(391.8f, 4.6f, 944.8f), new Vector3(439.7f, 4.9f, 996.0f), new Vector3(467.3f, 5.1f, 1024.9f),
                    new Vector3(474.6f, 5.1f, 1027.7f), new Vector3(482.5f, 5.2f, 1028.5f), new Vector3(490.5f, 5.2f, 1028.8f),
                    new Vector3(496.5f, 5.2f, 1028.6f), new Vector3(502.5f, 5.3f, 1028.0f), new Vector3(571.6f, 5.6f, 1016.9f),
                    new Vector3(638.6f, 5.8f, 1005.0f), new Vector3(644.4f, 5.8f, 1003.6f), new Vector3(652.0f, 5.8f, 1001.1f),
                    new Vector3(700.3f, 5.9f, 981.8f), new Vector3(764.7f, 6.0f, 954.3f), new Vector3(788.5f, 6.0f, 943.7f),
                    new Vector3(799.7f, 6.0f, 939.4f), new Vector3(811.1f, 6.0f, 935.9f), new Vector3(857.7f, 6.0f, 924.1f),
                    new Vector3(863.5f, 5.9f, 922.4f), new Vector3(869.1f, 5.9f, 920.3f), new Vector3(874.2f, 5.9f, 917.3f),
                    new Vector3(878.9f, 5.9f, 913.5f), new Vector3(884.2f, 5.9f, 907.5f), new Vector3(886.5f, 5.9f, 900.0f),
                    new Vector3(886.8f, 5.9f, 894.0f), new Vector3(886.3f, 5.9f, 886.0f), new Vector3(886.1f, 5.9f, 878.0f),
                    new Vector3(886.4f, 5.9f, 870.0f), new Vector3(887.4f, 5.8f, 860.0f), new Vector3(889.2f, 5.8f, 848.2f),
                    new Vector3(890.5f, 5.8f, 842.3f), new Vector3(892.2f, 5.8f, 836.5f), new Vector3(894.2f, 5.8f, 830.9f),
                    new Vector3(896.6f, 5.8f, 825.4f), new Vector3(899.2f, 5.8f, 820.0f), new Vector3(903.3f, 5.7f, 813.1f),
                    new Vector3(907.7f, 5.7f, 806.5f), new Vector3(916.3f, 5.7f, 795.4f), new Vector3(933.2f, 5.6f, 775.6f),
                    new Vector3(943.5f, 5.6f, 763.4f), new Vector3(949.6f, 5.6f, 755.4f), new Vector3(954.0f, 5.5f, 748.7f),
                    new Vector3(958.0f, 5.5f, 741.8f), new Vector3(962.5f, 5.5f, 732.9f), new Vector3(968.2f, 5.4f, 720.1f),
                    new Vector3(972.5f, 5.4f, 708.9f), new Vector3(974.9f, 5.4f, 701.3f), new Vector3(976.9f, 5.4f, 693.5f),
                    new Vector3(978.8f, 5.3f, 683.7f), new Vector3(981.3f, 5.3f, 665.9f), new Vector3(982.5f, 5.2f, 651.9f),
                    new Vector3(982.6f, 5.2f, 643.9f), new Vector3(982.3f, 5.2f, 635.9f), new Vector3(981.3f, 5.2f, 626.0f),
                    new Vector3(979.5f, 5.1f, 614.1f), new Vector3(976.5f, 5.1f, 600.4f), new Vector3(973.4f, 5.0f, 588.9f),
                    new Vector3(970.1f, 5.0f, 579.4f), new Vector3(967.1f, 5.0f, 572.0f), new Vector3(962.7f, 5.0f, 563.0f),
                    new Vector3(951.2f, 4.9f, 541.9f), new Vector3(943.8f, 4.8f, 530.0f), new Vector3(903.8f, 4.6f, 472.6f),
                    new Vector3(863.6f, 4.4f, 415.3f), new Vector3(852.8f, 4.4f, 400.8f), new Vector3(848.8f, 4.4f, 396.4f),
                    new Vector3(842.9f, 4.3f, 391.0f), new Vector3(836.6f, 4.3f, 386.0f), new Vector3(831.8f, 4.3f, 382.5f),
                    new Vector3(825.0f, 4.3f, 378.2f), new Vector3(818.0f, 4.3f, 374.4f), new Vector3(807.1f, 4.2f, 369.2f),
                    new Vector3(799.7f, 4.2f, 366.2f), new Vector3(794.0f, 4.2f, 364.4f), new Vector3(788.2f, 4.2f, 362.8f),
                    new Vector3(780.4f, 4.2f, 361.2f), new Vector3(764.6f, 4.2f, 358.8f), new Vector3(740.7f, 4.1f, 356.0f),
                    new Vector3(728.9f, 4.1f, 354.0f), new Vector3(723.0f, 4.1f, 352.7f), new Vector3(717.3f, 4.1f, 351.0f),
                    new Vector3(707.9f, 4.1f, 347.5f), new Vector3(700.5f, 4.1f, 344.4f), new Vector3(693.4f, 4.0f, 340.8f),
                    new Vector3(686.5f, 4.0f, 336.8f), new Vector3(628.3f, 4.0f, 297.8f), new Vector3(570.5f, 4.0f, 258.3f),
                    new Vector3(550.9f, 3.9f, 244.4f), new Vector3(540.1f, 3.9f, 235.5f), new Vector3(523.9f, 3.9f, 220.6f),
                    new Vector3(505.9f, 3.8f, 201.8f), new Vector3(490.1f, 3.7f, 183.8f), new Vector3(481.5f, 3.7f, 172.7f),
                    new Vector3(471.2f, 3.7f, 157.9f), new Vector3(457.7f, 3.6f, 135.7f), new Vector3(447.1f, 3.5f, 116.4f),
                    new Vector3(441.2f, 3.4f, 103.7f), new Vector3(436.0f, 3.4f, 90.7f), new Vector3(427.1f, 3.3f, 64.2f),
                    new Vector3(421.9f, 3.2f, 44.9f), new Vector3(413.5f, 3.0f, 5.8f), new Vector3(412.2f, 3.0f, -2.1f),
                    new Vector3(411.0f, 2.9f, -14.1f), new Vector3(410.3f, 2.8f, -30.1f), new Vector3(410.9f, 2.7f, -62.1f),
                    new Vector3(414.5f, 2.4f, -132.0f), new Vector3(417.9f, 2.1f, -179.9f), new Vector3(419.2f, 2.1f, -187.8f),
                    new Vector3(420.5f, 2.1f, -193.6f), new Vector3(423.9f, 2.0f, -205.1f), new Vector3(425.9f, 2.0f, -210.8f),
                    new Vector3(428.4f, 2.0f, -216.3f), new Vector3(433.0f, 1.9f, -225.2f), new Vector3(436.1f, 1.9f, -230.3f),
                    new Vector3(439.5f, 1.9f, -235.2f), new Vector3(443.3f, 1.8f, -239.9f), new Vector3(448.6f, 1.8f, -245.8f),
                    new Vector3(460.1f, 1.7f, -257.0f), new Vector3(487.6f, 1.6f, -283.3f), new Vector3(497.6f, 1.5f, -293.1f),
                    new Vector3(503.0f, 1.5f, -299.0f), new Vector3(506.7f, 1.5f, -303.7f), new Vector3(510.0f, 1.5f, -308.7f),
                    new Vector3(512.8f, 1.4f, -314.0f), new Vector3(515.2f, 1.4f, -319.5f), new Vector3(518.4f, 1.4f, -329.0f),
                    new Vector3(520.1f, 1.4f, -334.7f), new Vector3(521.4f, 1.3f, -340.6f), new Vector3(522.1f, 1.3f, -346.6f),
                    new Vector3(527.3f, 1.1f, -416.4f), new Vector3(532.8f, 1.0f, -486.2f), new Vector3(536.9f, 1.0f, -540.0f),
                    new Vector3(537.1f, 1.0f, -552.0f), new Vector3(536.7f, 1.0f, -560.0f), new Vector3(534.1f, 1.0f, -589.9f),
                    new Vector3(532.6f, 1.0f, -599.8f), new Vector3(530.1f, 1.0f, -611.6f), new Vector3(527.4f, 1.0f, -621.2f),
                    new Vector3(522.9f, 1.1f, -634.4f), new Vector3(514.6f, 1.1f, -654.8f), new Vector3(500.9f, 1.1f, -683.8f),
                    new Vector3(467.9f, 1.3f, -745.5f), new Vector3(433.9f, 1.5f, -806.7f), new Vector3(399.5f, 1.7f, -867.7f),
                    new Vector3(373.0f, 1.9f, -912.4f), new Vector3(358.4f, 2.0f, -934.0f), new Vector3(354.8f, 2.0f, -938.8f),
                    new Vector3(348.0f, 2.0f, -941.8f), new Vector3(342.3f, 2.0f, -939.7f), new Vector3(278.0f, 2.3f, -912.1f),
                    new Vector3(213.6f, 2.5f, -884.5f), new Vector3(149.4f, 2.7f, -856.6f), new Vector3(142.1f, 2.7f, -853.3f),
                    new Vector3(137.0f, 2.7f, -850.1f), new Vector3(132.3f, 2.7f, -846.4f), new Vector3(127.9f, 2.7f, -842.3f),
                    new Vector3(123.9f, 2.8f, -837.9f), new Vector3(120.3f, 2.8f, -833.1f), new Vector3(117.4f, 2.8f, -827.8f),
                    new Vector3(115.1f, 2.8f, -822.3f), new Vector3(113.3f, 2.8f, -816.6f), new Vector3(111.9f, 2.8f, -810.7f),
                    new Vector3(110.5f, 2.8f, -802.8f), new Vector3(109.8f, 2.8f, -796.9f), new Vector3(109.8f, 2.9f, -790.9f),
                    new Vector3(110.5f, 2.9f, -784.9f), new Vector3(111.7f, 2.9f, -779.1f), new Vector3(131.3f, 3.0f, -711.8f),
                    new Vector3(150.1f, 3.0f, -644.4f), new Vector3(161.8f, 3.0f, -595.8f), new Vector3(162.4f, 2.9f, -589.8f),
                    new Vector3(160.2f, 2.9f, -582.2f), new Vector3(154.7f, 2.9f, -576.5f), new Vector3(147.0f, 2.9f, -574.9f),
                    new Vector3(77.1f, 2.7f, -578.5f), new Vector3(53.1f, 2.6f, -579.7f), new Vector3(47.1f, 2.6f, -579.5f),
                    new Vector3(41.2f, 2.6f, -578.4f), new Vector3(35.5f, 2.5f, -576.6f), new Vector3(29.9f, 2.5f, -574.3f),
                    new Vector3(24.6f, 2.5f, -571.7f), new Vector3(19.4f, 2.5f, -568.6f), new Vector3(14.7f, 2.4f, -564.9f),
                    new Vector3(10.6f, 2.4f, -560.5f), new Vector3(7.1f, 2.4f, -555.7f), new Vector3(4.0f, 2.3f, -550.5f),
                    new Vector3(1.2f, 2.3f, -545.2f), new Vector3(-1.2f, 2.3f, -539.7f), new Vector3(-3.0f, 2.3f, -534.0f),
                    new Vector3(-4.2f, 2.2f, -528.1f), new Vector3(-4.9f, 2.2f, -522.1f), new Vector3(-4.2f, 1.8f, -452.1f),
                    new Vector3(-3.4f, 1.4f, -382.1f), new Vector3(-2.6f, 1.0f, -312.1f), new Vector3(-1.8f, 0.7f, -242.1f),
                    new Vector3(-1.1f, 0.4f, -172.0f), new Vector3(-0.5f, 0.1f, -102.0f), new Vector3(0.0f, 0.0f, -32.0f)
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, -3.5f, 70.0f), new Vector3(-0.1f, -10.1f, 140.1f),
                    new Vector3(-0.4f, -10.8f, 148.1f), new Vector3(-1.0f, -11.3f, 154.0f), new Vector3(-2.0f, -11.7f, 159.9f),
                    new Vector3(-3.5f, -12.2f, 165.7f), new Vector3(-6.6f, -12.8f, 175.3f), new Vector3(-9.4f, -13.2f, 182.8f),
                    new Vector3(-11.8f, -13.5f, 188.2f), new Vector3(-15.1f, -13.7f, 193.2f), new Vector3(-19.5f, -13.9f, 197.4f),
                    new Vector3(-24.4f, -14.0f, 200.8f), new Vector3(-29.9f, -14.0f, 203.1f), new Vector3(-35.8f, -14.0f, 204.4f),
                    new Vector3(-41.8f, -14.1f, 204.9f), new Vector3(-47.8f, -14.2f, 204.8f), new Vector3(-55.3f, -14.3f, 202.4f),
                    new Vector3(-66.0f, -14.7f, 196.9f), new Vector3(-79.8f, -15.3f, 188.9f), new Vector3(-95.0f, -16.3f, 179.2f),
                    new Vector3(-100.2f, -16.6f, 176.2f), new Vector3(-105.9f, -17.0f, 174.2f), new Vector3(-111.8f, -17.4f, 173.4f),
                    new Vector3(-117.8f, -17.8f, 173.6f), new Vector3(-123.6f, -18.2f, 175.0f), new Vector3(-129.0f, -18.7f, 177.6f),
                    new Vector3(-133.9f, -19.1f, 181.0f), new Vector3(-138.5f, -19.6f, 184.9f), new Vector3(-149.9f, -20.8f, 196.1f),
                    new Vector3(-160.0f, -21.9f, 205.8f), new Vector3(-167.7f, -22.7f, 212.3f), new Vector3(-175.7f, -23.5f, 218.2f),
                    new Vector3(-182.4f, -24.1f, 222.6f), new Vector3(-189.3f, -24.7f, 226.6f), new Vector3(-196.5f, -25.3f, 230.2f),
                    new Vector3(-203.9f, -25.9f, 233.3f), new Vector3(-215.2f, -26.7f, 237.3f), new Vector3(-221.0f, -27.1f, 238.9f),
                    new Vector3(-226.9f, -27.5f, 240.1f), new Vector3(-238.7f, -28.2f, 241.8f), new Vector3(-248.7f, -28.7f, 242.6f),
                    new Vector3(-254.7f, -28.9f, 242.8f), new Vector3(-262.7f, -29.2f, 242.6f), new Vector3(-272.7f, -29.6f, 241.7f),
                    new Vector3(-280.6f, -29.8f, 240.5f), new Vector3(-288.4f, -29.9f, 238.7f), new Vector3(-296.1f, -30.0f, 236.4f),
                    new Vector3(-305.5f, -30.0f, 233.0f), new Vector3(-314.6f, -30.0f, 229.1f), new Vector3(-321.8f, -30.1f, 225.5f),
                    new Vector3(-328.8f, -30.1f, 221.5f), new Vector3(-335.5f, -30.2f, 217.2f), new Vector3(-341.9f, -30.3f, 212.5f),
                    new Vector3(-348.1f, -30.4f, 207.4f), new Vector3(-355.5f, -30.5f, 200.6f), new Vector3(-362.5f, -30.6f, 193.5f),
                    new Vector3(-369.0f, -30.8f, 185.9f), new Vector3(-372.7f, -30.9f, 181.1f), new Vector3(-383.6f, -31.3f, 164.4f),
                    new Vector3(-420.1f, -33.1f, 104.6f), new Vector3(-454.5f, -35.0f, 43.6f), new Vector3(-488.3f, -36.7f, -17.7f),
                    new Vector3(-521.6f, -37.8f, -79.3f), new Vector3(-555.3f, -38.0f, -140.7f), new Vector3(-589.5f, -37.6f, -201.9f),
                    new Vector3(-623.7f, -37.0f, -263.0f), new Vector3(-657.9f, -36.2f, -324.1f), new Vector3(-683.1f, -35.6f, -369.6f),
                    new Vector3(-685.6f, -35.5f, -375.0f), new Vector3(-687.6f, -35.4f, -380.7f), new Vector3(-688.9f, -35.4f, -386.5f),
                    new Vector3(-689.3f, -35.3f, -392.5f), new Vector3(-688.8f, -35.3f, -398.5f), new Vector3(-687.3f, -35.2f, -404.3f),
                    new Vector3(-684.8f, -35.1f, -409.8f), new Vector3(-681.4f, -35.1f, -414.7f), new Vector3(-677.4f, -35.0f, -419.1f),
                    new Vector3(-673.0f, -34.9f, -423.2f), new Vector3(-668.2f, -34.9f, -426.8f), new Vector3(-663.1f, -34.8f, -430.0f),
                    new Vector3(-656.0f, -34.7f, -433.7f), new Vector3(-592.7f, -34.2f, -463.7f), new Vector3(-583.5f, -34.2f, -467.4f),
                    new Vector3(-575.9f, -34.1f, -470.0f), new Vector3(-570.1f, -34.1f, -471.5f), new Vector3(-562.2f, -34.1f, -472.9f),
                    new Vector3(-550.3f, -34.0f, -474.3f), new Vector3(-544.3f, -34.0f, -474.7f), new Vector3(-538.3f, -34.0f, -474.7f),
                    new Vector3(-532.3f, -34.0f, -474.4f), new Vector3(-526.3f, -34.0f, -473.5f), new Vector3(-520.5f, -34.0f, -472.1f),
                    new Vector3(-501.3f, -34.0f, -466.5f), new Vector3(-495.7f, -33.9f, -464.4f), new Vector3(-490.2f, -33.9f, -462.0f),
                    new Vector3(-484.8f, -33.9f, -459.3f), new Vector3(-477.9f, -33.9f, -455.3f), new Vector3(-467.9f, -33.8f, -448.6f),
                    new Vector3(-440.9f, -33.5f, -428.0f), new Vector3(-386.4f, -32.6f, -384.0f), new Vector3(-332.6f, -31.4f, -339.1f),
                    new Vector3(-279.0f, -30.2f, -294.1f), new Vector3(-224.5f, -29.1f, -250.1f), new Vector3(-207.2f, -28.8f, -236.4f),
                    new Vector3(-202.3f, -28.7f, -233.0f), new Vector3(-197.1f, -28.7f, -230.0f), new Vector3(-191.6f, -28.6f, -227.8f),
                    new Vector3(-185.7f, -28.5f, -226.3f), new Vector3(-179.8f, -28.5f, -225.4f), new Vector3(-173.8f, -28.4f, -225.1f),
                    new Vector3(-167.8f, -28.4f, -225.2f), new Vector3(-161.8f, -28.3f, -225.8f), new Vector3(-155.9f, -28.3f, -226.8f),
                    new Vector3(-146.2f, -28.2f, -229.2f), new Vector3(-134.7f, -28.1f, -232.7f), new Vector3(-127.2f, -28.1f, -235.5f),
                    new Vector3(-121.8f, -28.1f, -238.0f), new Vector3(-116.5f, -28.0f, -240.8f), new Vector3(-111.3f, -28.0f, -243.9f),
                    new Vector3(-106.4f, -28.0f, -247.3f), new Vector3(-101.7f, -28.0f, -251.0f), new Vector3(-89.8f, -28.0f, -261.7f),
                    new Vector3(-84.1f, -28.0f, -267.4f), new Vector3(-80.3f, -28.0f, -272.0f), new Vector3(-77.0f, -28.0f, -277.0f),
                    new Vector3(-74.2f, -27.9f, -282.3f), new Vector3(-72.1f, -27.9f, -287.9f), new Vector3(-70.5f, -27.9f, -293.7f),
                    new Vector3(-69.4f, -27.9f, -299.6f), new Vector3(-68.8f, -27.8f, -305.6f), new Vector3(-68.5f, -27.8f, -317.6f),
                    new Vector3(-69.5f, -27.5f, -357.6f), new Vector3(-73.0f, -27.2f, -397.5f), new Vector3(-74.8f, -27.0f, -411.3f),
                    new Vector3(-76.7f, -26.9f, -421.2f), new Vector3(-79.2f, -26.8f, -430.9f), new Vector3(-81.2f, -26.7f, -436.5f),
                    new Vector3(-83.7f, -26.7f, -441.9f), new Vector3(-87.1f, -26.6f, -446.9f), new Vector3(-93.1f, -26.5f, -452.1f),
                    new Vector3(-98.6f, -26.4f, -454.5f), new Vector3(-104.5f, -26.4f, -455.3f), new Vector3(-110.5f, -26.3f, -455.0f),
                    new Vector3(-116.3f, -26.2f, -453.4f), new Vector3(-121.5f, -26.2f, -450.5f), new Vector3(-126.4f, -26.1f, -447.0f),
                    new Vector3(-130.6f, -26.0f, -442.8f), new Vector3(-140.8f, -25.8f, -430.4f), new Vector3(-148.5f, -25.7f, -421.2f),
                    new Vector3(-152.7f, -25.6f, -416.9f), new Vector3(-157.2f, -25.6f, -412.9f), new Vector3(-162.2f, -25.5f, -409.6f),
                    new Vector3(-167.6f, -25.4f, -407.0f), new Vector3(-173.3f, -25.4f, -405.1f), new Vector3(-179.2f, -25.3f, -404.0f),
                    new Vector3(-185.2f, -25.2f, -404.0f), new Vector3(-191.1f, -25.2f, -404.8f), new Vector3(-196.8f, -25.1f, -406.6f),
                    new Vector3(-202.3f, -25.0f, -409.1f), new Vector3(-207.5f, -25.0f, -412.0f), new Vector3(-212.4f, -24.9f, -415.6f),
                    new Vector3(-216.6f, -24.9f, -419.8f), new Vector3(-220.3f, -24.8f, -424.6f), new Vector3(-223.3f, -24.7f, -429.7f),
                    new Vector3(-225.6f, -24.7f, -435.3f), new Vector3(-227.0f, -24.6f, -441.1f), new Vector3(-227.8f, -24.6f, -447.1f),
                    new Vector3(-227.8f, -24.5f, -453.1f), new Vector3(-226.6f, -24.5f, -458.9f), new Vector3(-224.9f, -24.4f, -464.7f),
                    new Vector3(-222.2f, -24.4f, -472.2f), new Vector3(-219.8f, -24.3f, -477.7f), new Vector3(-217.0f, -24.3f, -483.0f),
                    new Vector3(-190.5f, -24.1f, -527.8f), new Vector3(-184.1f, -24.0f, -540.3f), new Vector3(-180.0f, -24.0f, -549.4f),
                    new Vector3(-177.2f, -24.0f, -556.9f), new Vector3(-175.4f, -24.0f, -562.6f), new Vector3(-174.1f, -24.0f, -568.5f),
                    new Vector3(-173.2f, -24.0f, -574.4f), new Vector3(-172.4f, -24.1f, -582.4f), new Vector3(-172.0f, -24.1f, -594.4f),
                    new Vector3(-172.6f, -24.3f, -616.4f), new Vector3(-173.8f, -24.5f, -632.3f), new Vector3(-175.1f, -24.6f, -642.2f),
                    new Vector3(-176.3f, -24.7f, -648.1f), new Vector3(-177.9f, -24.8f, -653.9f), new Vector3(-180.2f, -24.8f, -659.4f),
                    new Vector3(-183.8f, -24.9f, -664.2f), new Vector3(-188.2f, -25.0f, -668.3f), new Vector3(-195.4f, -25.2f, -671.5f),
                    new Vector3(-201.4f, -25.3f, -671.7f), new Vector3(-209.0f, -25.4f, -669.5f), new Vector3(-214.0f, -25.5f, -666.2f),
                    new Vector3(-218.7f, -25.7f, -659.8f), new Vector3(-225.5f, -25.9f, -647.6f), new Vector3(-257.6f, -27.4f, -585.3f),
                    new Vector3(-267.2f, -27.8f, -567.7f), new Vector3(-270.6f, -27.9f, -562.8f), new Vector3(-277.1f, -28.1f, -555.2f),
                    new Vector3(-281.3f, -28.2f, -550.9f), new Vector3(-285.8f, -28.4f, -547.0f), new Vector3(-290.6f, -28.5f, -543.4f),
                    new Vector3(-295.7f, -28.6f, -540.2f), new Vector3(-301.0f, -28.7f, -537.4f), new Vector3(-310.2f, -28.9f, -533.4f),
                    new Vector3(-317.6f, -29.0f, -530.5f), new Vector3(-323.4f, -29.1f, -528.8f), new Vector3(-329.2f, -29.2f, -527.3f),
                    new Vector3(-337.1f, -29.3f, -526.0f), new Vector3(-345.1f, -29.4f, -525.2f), new Vector3(-351.1f, -29.5f, -524.8f),
                    new Vector3(-357.1f, -29.5f, -524.9f), new Vector3(-363.0f, -29.6f, -525.4f), new Vector3(-369.0f, -29.7f, -526.4f),
                    new Vector3(-374.8f, -29.7f, -527.8f), new Vector3(-380.5f, -29.8f, -529.5f), new Vector3(-388.0f, -29.9f, -532.3f),
                    new Vector3(-395.3f, -29.9f, -535.6f), new Vector3(-400.6f, -29.9f, -538.4f), new Vector3(-405.7f, -30.0f, -541.6f),
                    new Vector3(-413.6f, -30.0f, -547.7f), new Vector3(-427.3f, -30.0f, -559.4f), new Vector3(-478.1f, -30.9f, -607.6f),
                    new Vector3(-528.3f, -32.7f, -656.4f), new Vector3(-542.5f, -33.4f, -670.5f), new Vector3(-546.1f, -33.6f, -675.3f),
                    new Vector3(-548.3f, -33.8f, -680.9f), new Vector3(-549.0f, -34.0f, -686.8f), new Vector3(-548.3f, -34.2f, -692.8f),
                    new Vector3(-546.7f, -34.4f, -698.5f), new Vector3(-543.7f, -34.6f, -703.7f), new Vector3(-539.8f, -34.8f, -708.3f),
                    new Vector3(-535.5f, -35.0f, -712.4f), new Vector3(-529.4f, -35.3f, -717.6f), new Vector3(-519.8f, -35.7f, -724.8f),
                    new Vector3(-462.2f, -38.0f, -764.7f), new Vector3(-457.0f, -38.2f, -767.7f), new Vector3(-451.6f, -38.3f, -770.3f),
                    new Vector3(-446.0f, -38.5f, -772.4f), new Vector3(-440.2f, -38.6f, -773.8f), new Vector3(-430.3f, -38.9f, -775.5f),
                    new Vector3(-410.4f, -39.3f, -777.9f), new Vector3(-366.5f, -39.9f, -780.2f), new Vector3(-346.5f, -40.0f, -780.5f),
                    new Vector3(-340.5f, -40.0f, -780.3f), new Vector3(-320.6f, -39.9f, -777.9f), new Vector3(-287.0f, -39.2f, -772.8f),
                    new Vector3(-255.8f, -38.1f, -765.5f), new Vector3(-238.5f, -37.3f, -760.7f), new Vector3(-230.9f, -36.9f, -758.0f),
                    new Vector3(-223.5f, -36.5f, -754.9f), new Vector3(-205.8f, -35.5f, -745.8f), new Vector3(-197.0f, -34.9f, -740.9f),
                    new Vector3(-190.3f, -34.5f, -736.6f), new Vector3(-183.9f, -34.0f, -731.8f), new Vector3(-177.8f, -33.5f, -726.6f),
                    new Vector3(-172.0f, -33.0f, -721.1f), new Vector3(-166.6f, -32.5f, -715.1f), new Vector3(-160.3f, -31.9f, -707.4f),
                    new Vector3(-153.3f, -31.1f, -697.6f), new Vector3(-145.8f, -30.3f, -685.8f), new Vector3(-119.5f, -27.0f, -638.7f),
                    new Vector3(-98.9f, -24.6f, -597.5f), new Vector3(-83.7f, -23.2f, -564.8f), new Vector3(-62.0f, -22.1f, -524.3f),
                    new Vector3(-37.6f, -21.5f, -478.3f), new Vector3(-28.8f, -20.9f, -462.6f), new Vector3(-25.3f, -20.5f, -455.4f),
                    new Vector3(-22.2f, -20.1f, -448.0f), new Vector3(-18.4f, -19.4f, -436.7f), new Vector3(-13.6f, -18.2f, -419.3f),
                    new Vector3(-8.5f, -16.4f, -395.8f), new Vector3(-5.9f, -15.2f, -380.0f), new Vector3(-4.4f, -13.9f, -364.1f),
                    new Vector3(-3.3f, -11.7f, -336.1f), new Vector3(-2.5f, -7.2f, -266.1f), new Vector3(-1.2f, -5.9f, -196.1f),
                    new Vector3(-0.2f, -3.8f, -126.1f), new Vector3(0.0f, -1.0f, -56.0f)
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
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 70.0f), new Vector3(-0.8f, 0.0f, 132.0f),
                    new Vector3(-2.2f, 0.0f, 139.9f), new Vector3(-6.1f, 0.0f, 146.8f), new Vector3(-10.0f, 0.0f, 151.3f),
                    new Vector3(-14.6f, 0.0f, 155.2f), new Vector3(-19.6f, 0.0f, 158.5f), new Vector3(-25.0f, 0.0f, 161.1f),
                    new Vector3(-30.7f, 0.0f, 162.9f), new Vector3(-36.7f, 0.0f, 163.6f), new Vector3(-42.7f, 0.0f, 163.6f),
                    new Vector3(-112.4f, 0.0f, 156.9f), new Vector3(-182.1f, 0.0f, 150.1f), new Vector3(-223.8f, 0.0f, 145.4f),
                    new Vector3(-229.7f, 0.0f, 144.3f), new Vector3(-235.5f, 0.0f, 142.6f), new Vector3(-241.1f, 0.0f, 140.5f),
                    new Vector3(-246.4f, 0.0f, 137.8f), new Vector3(-251.5f, 0.0f, 134.5f), new Vector3(-256.2f, 0.0f, 130.8f),
                    new Vector3(-260.6f, 0.0f, 126.8f), new Vector3(-264.8f, 0.0f, 122.5f), new Vector3(-268.6f, 0.0f, 117.8f),
                    new Vector3(-271.7f, 0.0f, 112.7f), new Vector3(-284.2f, 0.0f, 87.7f), new Vector3(-303.6f, 0.0f, 48.2f),
                    new Vector3(-308.6f, 0.0f, 39.5f), new Vector3(-311.9f, 0.0f, 34.5f), new Vector3(-317.8f, 0.0f, 26.4f),
                    new Vector3(-324.2f, 0.0f, 18.7f), new Vector3(-329.7f, 0.0f, 12.9f), new Vector3(-335.6f, 0.0f, 7.5f),
                    new Vector3(-343.3f, 0.0f, 1.2f), new Vector3(-351.4f, 0.0f, -4.7f), new Vector3(-363.3f, 0.0f, -12.2f),
                    new Vector3(-370.2f, 0.0f, -16.2f), new Vector3(-375.7f, 0.0f, -18.6f), new Vector3(-381.4f, 0.0f, -20.5f),
                    new Vector3(-391.0f, 0.0f, -23.2f), new Vector3(-398.8f, 0.0f, -24.9f), new Vector3(-404.8f, 0.0f, -25.9f),
                    new Vector3(-410.7f, 0.0f, -26.5f), new Vector3(-416.7f, 0.0f, -26.6f), new Vector3(-422.7f, 0.0f, -26.5f),
                    new Vector3(-428.7f, 0.0f, -26.0f), new Vector3(-434.6f, 0.0f, -25.2f), new Vector3(-442.5f, 0.0f, -23.6f),
                    new Vector3(-450.2f, 0.0f, -21.5f), new Vector3(-459.7f, 0.0f, -18.3f), new Vector3(-470.9f, 0.0f, -13.9f),
                    new Vector3(-524.2f, 0.0f, 8.8f), new Vector3(-550.5f, 0.0f, 18.6f), new Vector3(-569.5f, 0.0f, 24.6f),
                    new Vector3(-594.8f, 0.0f, 31.0f), new Vector3(-612.4f, 0.0f, 34.4f), new Vector3(-628.3f, 0.0f, 36.5f),
                    new Vector3(-642.2f, 0.0f, 37.6f), new Vector3(-712.2f, 0.0f, 39.0f), new Vector3(-782.3f, 0.0f, 40.0f),
                    new Vector3(-852.3f, 0.0f, 40.7f), new Vector3(-914.3f, 0.0f, 40.8f), new Vector3(-922.3f, 0.0f, 40.2f),
                    new Vector3(-928.2f, 0.0f, 39.5f), new Vector3(-935.7f, 0.0f, 36.7f), new Vector3(-940.7f, 0.0f, 33.4f),
                    new Vector3(-945.4f, 0.0f, 29.7f), new Vector3(-949.8f, 0.0f, 25.6f), new Vector3(-953.7f, 0.0f, 21.1f),
                    new Vector3(-957.1f, 0.0f, 13.9f), new Vector3(-958.4f, 0.0f, 8.0f), new Vector3(-958.6f, 0.0f, 2.0f),
                    new Vector3(-957.8f, 0.0f, -3.9f), new Vector3(-955.0f, 0.0f, -11.4f), new Vector3(-951.4f, 0.0f, -16.2f),
                    new Vector3(-947.1f, 0.0f, -20.4f), new Vector3(-942.3f, 0.0f, -24.0f), new Vector3(-937.3f, 0.0f, -27.1f),
                    new Vector3(-916.0f, 0.0f, -38.2f), new Vector3(-894.1f, 0.0f, -48.2f), new Vector3(-860.7f, 0.0f, -61.6f),
                    new Vector3(-815.0f, 0.0f, -81.9f), new Vector3(-751.9f, 0.0f, -112.2f), new Vector3(-689.0f, 0.0f, -143.0f),
                    new Vector3(-626.3f, 0.0f, -174.1f), new Vector3(-563.6f, 0.0f, -205.2f), new Vector3(-500.8f, 0.0f, -236.3f),
                    new Vector3(-438.2f, 0.0f, -267.5f), new Vector3(-375.5f, 0.0f, -298.7f), new Vector3(-312.8f, 0.0f, -329.9f),
                    new Vector3(-249.8f, 0.0f, -360.5f), new Vector3(-185.8f, 0.0f, -388.9f), new Vector3(-122.1f, 0.0f, -417.9f),
                    new Vector3(-58.8f, 0.0f, -447.7f), new Vector3(4.3f, 0.0f, -478.1f), new Vector3(67.2f, 0.0f, -509.0f),
                    new Vector3(92.1f, 0.0f, -521.7f), new Vector3(97.6f, 0.0f, -524.0f), new Vector3(103.5f, 0.0f, -525.4f),
                    new Vector3(111.1f, 0.0f, -523.8f), new Vector3(115.4f, 0.0f, -517.3f), new Vector3(116.2f, 0.0f, -511.4f),
                    new Vector3(117.1f, 0.0f, -481.4f), new Vector3(117.5f, 0.0f, -471.4f), new Vector3(120.0f, 0.0f, -464.0f),
                    new Vector3(126.9f, 0.0f, -460.3f), new Vector3(132.8f, 0.0f, -459.0f), new Vector3(158.5f, 0.0f, -455.1f),
                    new Vector3(222.0f, 0.0f, -447.1f), new Vector3(235.7f, 0.0f, -444.2f), new Vector3(262.8f, 0.0f, -437.2f),
                    new Vector3(276.2f, 0.0f, -433.0f), new Vector3(285.5f, 0.0f, -429.4f), new Vector3(301.9f, 0.0f, -422.0f),
                    new Vector3(325.1f, 0.0f, -410.1f), new Vector3(345.8f, 0.0f, -398.0f), new Vector3(360.8f, 0.0f, -388.1f),
                    new Vector3(375.3f, 0.0f, -377.4f), new Vector3(389.1f, 0.0f, -365.8f), new Vector3(440.3f, 0.0f, -318.0f),
                    new Vector3(490.8f, 0.0f, -269.6f), new Vector3(541.3f, 0.0f, -221.1f), new Vector3(591.9f, 0.0f, -172.7f),
                    new Vector3(614.2f, 0.0f, -149.7f), new Vector3(648.2f, 0.0f, -110.4f), new Vector3(667.9f, 0.0f, -85.2f),
                    new Vector3(699.5f, 0.0f, -38.9f), new Vector3(737.1f, 0.0f, 20.1f), new Vector3(775.1f, 0.0f, 78.9f),
                    new Vector3(788.6f, 0.0f, 101.2f), new Vector3(792.3f, 0.0f, 108.3f), new Vector3(794.8f, 0.0f, 113.7f),
                    new Vector3(796.7f, 0.0f, 119.4f), new Vector3(797.9f, 0.0f, 125.3f), new Vector3(798.4f, 0.0f, 131.3f),
                    new Vector3(798.5f, 0.0f, 137.3f), new Vector3(798.3f, 0.0f, 143.2f), new Vector3(797.4f, 0.0f, 149.2f),
                    new Vector3(796.0f, 0.0f, 155.0f), new Vector3(794.0f, 0.0f, 160.7f), new Vector3(791.6f, 0.0f, 166.2f),
                    new Vector3(788.8f, 0.0f, 171.5f), new Vector3(785.4f, 0.0f, 176.4f), new Vector3(781.6f, 0.0f, 181.0f),
                    new Vector3(777.2f, 0.0f, 185.2f), new Vector3(772.5f, 0.0f, 188.8f), new Vector3(767.5f, 0.0f, 192.2f),
                    new Vector3(762.3f, 0.0f, 195.2f), new Vector3(756.9f, 0.0f, 197.7f), new Vector3(749.4f, 0.0f, 200.7f),
                    new Vector3(743.7f, 0.0f, 202.5f), new Vector3(737.8f, 0.0f, 203.6f), new Vector3(731.8f, 0.0f, 204.1f),
                    new Vector3(723.8f, 0.0f, 204.4f), new Vector3(717.9f, 0.0f, 204.2f), new Vector3(711.9f, 0.0f, 203.5f),
                    new Vector3(704.0f, 0.0f, 202.1f), new Vector3(698.2f, 0.0f, 200.6f), new Vector3(692.5f, 0.0f, 198.8f),
                    new Vector3(686.9f, 0.0f, 196.5f), new Vector3(681.6f, 0.0f, 193.7f), new Vector3(676.6f, 0.0f, 190.5f),
                    new Vector3(671.8f, 0.0f, 186.8f), new Vector3(667.3f, 0.0f, 182.8f), new Vector3(663.1f, 0.0f, 178.6f),
                    new Vector3(659.2f, 0.0f, 174.1f), new Vector3(655.5f, 0.0f, 169.3f), new Vector3(652.1f, 0.0f, 164.3f),
                    new Vector3(649.2f, 0.0f, 159.1f), new Vector3(646.8f, 0.0f, 153.6f), new Vector3(644.9f, 0.0f, 147.9f),
                    new Vector3(640.5f, 0.0f, 130.5f), new Vector3(625.1f, 0.0f, 62.1f), new Vector3(610.0f, 0.0f, -6.2f),
                    new Vector3(600.3f, 0.0f, -47.1f), new Vector3(597.4f, 0.0f, -56.6f), new Vector3(595.1f, 0.0f, -62.2f),
                    new Vector3(592.4f, 0.0f, -67.5f), new Vector3(588.9f, 0.0f, -72.4f), new Vector3(544.5f, 0.0f, -126.5f),
                    new Vector3(517.2f, 0.0f, -158.5f), new Vector3(512.8f, 0.0f, -162.5f), new Vector3(507.8f, 0.0f, -165.9f),
                    new Vector3(502.3f, 0.0f, -168.2f), new Vector3(496.5f, 0.0f, -169.9f), new Vector3(490.7f, 0.0f, -171.1f),
                    new Vector3(468.8f, 0.0f, -173.5f), new Vector3(399.0f, 0.0f, -178.6f), new Vector3(385.0f, 0.0f, -179.1f),
                    new Vector3(377.2f, 0.0f, -177.7f), new Vector3(371.9f, 0.0f, -172.0f), new Vector3(370.5f, 0.0f, -164.1f),
                    new Vector3(369.0f, 0.0f, -94.2f), new Vector3(368.5f, 0.0f, -86.2f), new Vector3(367.0f, 0.0f, -78.3f),
                    new Vector3(365.0f, 0.0f, -72.7f), new Vector3(360.4f, 0.0f, -66.1f), new Vector3(356.0f, 0.0f, -62.1f),
                    new Vector3(351.0f, 0.0f, -58.7f), new Vector3(345.6f, 0.0f, -56.2f), new Vector3(339.8f, 0.0f, -54.7f),
                    new Vector3(302.2f, 0.0f, -48.9f), new Vector3(276.4f, 0.0f, -45.8f), new Vector3(268.4f, 0.0f, -45.2f),
                    new Vector3(262.4f, 0.0f, -45.3f), new Vector3(254.8f, 0.0f, -47.5f), new Vector3(249.6f, 0.0f, -50.5f),
                    new Vector3(244.1f, 0.0f, -56.2f), new Vector3(241.1f, 0.0f, -61.5f), new Vector3(238.7f, 0.0f, -66.9f),
                    new Vector3(235.3f, 0.0f, -76.4f), new Vector3(233.1f, 0.0f, -84.0f), new Vector3(231.3f, 0.0f, -91.8f),
                    new Vector3(229.5f, 0.0f, -101.7f), new Vector3(228.6f, 0.0f, -109.6f), new Vector3(228.3f, 0.0f, -115.6f),
                    new Vector3(228.8f, 0.0f, -185.6f), new Vector3(229.8f, 0.0f, -255.6f), new Vector3(230.0f, 0.0f, -289.6f),
                    new Vector3(229.5f, 0.0f, -295.6f), new Vector3(228.1f, 0.0f, -305.5f), new Vector3(226.5f, 0.0f, -313.4f),
                    new Vector3(224.4f, 0.0f, -321.1f), new Vector3(221.9f, 0.0f, -328.7f), new Vector3(219.6f, 0.0f, -334.2f),
                    new Vector3(216.9f, 0.0f, -339.6f), new Vector3(211.8f, 0.0f, -345.7f), new Vector3(205.5f, 0.0f, -350.6f),
                    new Vector3(200.4f, 0.0f, -353.9f), new Vector3(193.2f, 0.0f, -357.2f), new Vector3(128.1f, 0.0f, -383.0f),
                    new Vector3(80.1f, 0.0f, -403.1f), new Vector3(70.8f, 0.0f, -406.8f), new Vector3(65.1f, 0.0f, -408.5f),
                    new Vector3(59.2f, 0.0f, -409.6f), new Vector3(53.2f, 0.0f, -410.2f), new Vector3(47.2f, 0.0f, -410.5f),
                    new Vector3(41.2f, 0.0f, -410.5f), new Vector3(31.2f, 0.0f, -409.7f), new Vector3(25.3f, 0.0f, -408.9f),
                    new Vector3(19.4f, 0.0f, -407.7f), new Vector3(13.8f, 0.0f, -405.6f), new Vector3(7.5f, 0.0f, -400.7f),
                    new Vector3(3.4f, 0.0f, -393.9f), new Vector3(2.0f, 0.0f, -386.1f), new Vector3(1.7f, 0.0f, -380.1f),
                    new Vector3(1.0f, 0.0f, -310.1f), new Vector3(0.6f, 0.0f, -240.1f), new Vector3(0.2f, 0.0f, -170.0f),
                    new Vector3(0.0f, 0.0f, -100.0f), new Vector3(0.0f, 0.0f, -30.0f)
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
                // Real activation-zone count: THREE.
                DrsZoneThreeNormalized = new Vector2(0.63f, 0.71f),
                TargetLengthMeters = 5412f,
                AnchorSubdivisions = 2,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 70.0f), new Vector3(0.1f, 0.0f, 140.0f),
                    new Vector3(0.4f, 0.0f, 210.0f), new Vector3(0.9f, 0.0f, 280.0f), new Vector3(1.9f, 0.0f, 350.0f),
                    new Vector3(3.4f, 0.0f, 398.0f), new Vector3(4.5f, 0.0f, 405.9f), new Vector3(10.1f, 0.0f, 411.1f),
                    new Vector3(18.1f, 0.0f, 411.3f), new Vector3(25.6f, 0.0f, 408.8f), new Vector3(32.0f, 0.0f, 404.1f),
                    new Vector3(64.2f, 0.0f, 374.1f), new Vector3(74.7f, 0.0f, 364.8f), new Vector3(81.1f, 0.0f, 360.0f),
                    new Vector3(88.4f, 0.0f, 356.9f), new Vector3(96.4f, 0.0f, 356.3f), new Vector3(104.2f, 0.0f, 357.8f),
                    new Vector3(125.2f, 0.0f, 364.5f), new Vector3(178.2f, 0.0f, 382.6f), new Vector3(187.8f, 0.0f, 385.4f),
                    new Vector3(195.6f, 0.0f, 386.8f), new Vector3(203.6f, 0.0f, 387.4f), new Vector3(211.6f, 0.0f, 387.3f),
                    new Vector3(280.8f, 0.0f, 376.8f), new Vector3(349.9f, 0.0f, 365.7f), new Vector3(419.0f, 0.0f, 354.5f),
                    new Vector3(488.1f, 0.0f, 343.2f), new Vector3(557.2f, 0.0f, 331.9f), new Vector3(626.3f, 0.0f, 320.6f),
                    new Vector3(695.3f, 0.0f, 309.1f), new Vector3(750.5f, 0.0f, 299.4f), new Vector3(758.1f, 0.0f, 297.0f),
                    new Vector3(764.8f, 0.0f, 292.7f), new Vector3(769.7f, 0.0f, 286.4f), new Vector3(772.0f, 0.0f, 278.8f),
                    new Vector3(772.4f, 0.0f, 270.8f), new Vector3(771.1f, 0.0f, 262.9f), new Vector3(768.5f, 0.0f, 255.3f),
                    new Vector3(764.9f, 0.0f, 248.2f), new Vector3(760.4f, 0.0f, 241.6f), new Vector3(755.2f, 0.0f, 235.5f),
                    new Vector3(749.6f, 0.0f, 229.8f), new Vector3(734.4f, 0.0f, 216.8f), new Vector3(679.8f, 0.0f, 173.0f),
                    new Vector3(660.5f, 0.0f, 155.5f), new Vector3(643.7f, 0.0f, 138.5f), new Vector3(607.1f, 0.0f, 98.7f),
                    new Vector3(602.2f, 0.0f, 92.4f), new Vector3(598.1f, 0.0f, 85.6f), new Vector3(593.7f, 0.0f, 76.6f),
                    new Vector3(576.6f, 0.0f, 36.0f), new Vector3(571.6f, 0.0f, 25.1f), new Vector3(567.6f, 0.0f, 18.2f),
                    new Vector3(562.2f, 0.0f, 12.4f), new Vector3(555.7f, 0.0f, 7.7f), new Vector3(549.0f, 0.0f, 3.4f),
                    new Vector3(541.7f, 0.0f, 0.0f), new Vector3(533.9f, 0.0f, -1.8f), new Vector3(526.0f, 0.0f, -2.4f),
                    new Vector3(518.0f, 0.0f, -2.1f), new Vector3(500.1f, 0.0f, -0.3f), new Vector3(468.3f, 0.0f, 3.5f),
                    new Vector3(460.3f, 0.0f, 3.9f), new Vector3(452.3f, 0.0f, 3.3f), new Vector3(444.4f, 0.0f, 2.0f),
                    new Vector3(436.7f, 0.0f, -0.1f), new Vector3(429.4f, 0.0f, -3.2f), new Vector3(422.6f, 0.0f, -7.4f),
                    new Vector3(416.5f, 0.0f, -12.6f), new Vector3(410.9f, 0.0f, -18.3f), new Vector3(391.1f, 0.0f, -43.4f),
                    new Vector3(349.1f, 0.0f, -99.4f), new Vector3(307.0f, 0.0f, -155.4f), new Vector3(292.2f, 0.0f, -174.3f),
                    new Vector3(286.5f, 0.0f, -179.8f), new Vector3(279.3f, 0.0f, -183.3f), new Vector3(271.4f, 0.0f, -184.5f),
                    new Vector3(263.8f, 0.0f, -182.4f), new Vector3(258.4f, 0.0f, -176.6f), new Vector3(255.6f, 0.0f, -169.1f),
                    new Vector3(254.3f, 0.0f, -161.2f), new Vector3(253.8f, 0.0f, -153.2f), new Vector3(254.1f, 0.0f, -145.2f),
                    new Vector3(258.8f, 0.0f, -103.5f), new Vector3(268.1f, 0.0f, -34.1f), new Vector3(277.4f, 0.0f, 35.2f),
                    new Vector3(286.2f, 0.0f, 104.7f), new Vector3(290.7f, 0.0f, 144.4f), new Vector3(290.8f, 0.0f, 152.4f),
                    new Vector3(290.0f, 0.0f, 160.4f), new Vector3(288.5f, 0.0f, 168.2f), new Vector3(286.5f, 0.0f, 176.0f),
                    new Vector3(283.9f, 0.0f, 183.6f), new Vector3(280.3f, 0.0f, 190.7f), new Vector3(275.6f, 0.0f, 197.1f),
                    new Vector3(269.9f, 0.0f, 202.8f), new Vector3(256.4f, 0.0f, 214.6f), new Vector3(228.3f, 0.0f, 237.1f),
                    new Vector3(221.5f, 0.0f, 241.3f), new Vector3(213.8f, 0.0f, 240.1f), new Vector3(209.8f, 0.0f, 233.5f),
                    new Vector3(208.2f, 0.0f, 223.6f), new Vector3(200.4f, 0.0f, 154.0f), new Vector3(195.0f, 0.0f, 84.3f),
                    new Vector3(192.8f, 0.0f, 14.3f), new Vector3(191.1f, 0.0f, -55.7f), new Vector3(189.6f, 0.0f, -125.7f),
                    new Vector3(188.7f, 0.0f, -195.7f), new Vector3(188.1f, 0.0f, -265.7f), new Vector3(187.4f, 0.0f, -335.7f),
                    new Vector3(186.8f, 0.0f, -405.7f), new Vector3(187.2f, 0.0f, -447.7f), new Vector3(187.9f, 0.0f, -455.6f),
                    new Vector3(189.5f, 0.0f, -463.5f), new Vector3(192.5f, 0.0f, -470.9f), new Vector3(197.2f, 0.0f, -477.3f),
                    new Vector3(203.4f, 0.0f, -482.4f), new Vector3(210.5f, 0.0f, -486.0f), new Vector3(218.1f, 0.0f, -488.4f),
                    new Vector3(226.0f, 0.0f, -490.0f), new Vector3(233.9f, 0.0f, -490.8f), new Vector3(241.9f, 0.0f, -490.8f),
                    new Vector3(249.9f, 0.0f, -490.0f), new Vector3(261.7f, 0.0f, -488.1f), new Vector3(271.5f, 0.0f, -485.9f),
                    new Vector3(279.2f, 0.0f, -483.7f), new Vector3(286.7f, 0.0f, -480.9f), new Vector3(293.9f, 0.0f, -477.6f),
                    new Vector3(302.8f, 0.0f, -472.9f), new Vector3(311.3f, 0.0f, -467.7f), new Vector3(319.5f, 0.0f, -462.0f),
                    new Vector3(325.9f, 0.0f, -457.1f), new Vector3(333.4f, 0.0f, -450.5f), new Vector3(340.5f, 0.0f, -443.5f),
                    new Vector3(347.2f, 0.0f, -436.0f), new Vector3(352.2f, 0.0f, -429.8f), new Vector3(356.8f, 0.0f, -423.3f),
                    new Vector3(361.0f, 0.0f, -416.4f), new Vector3(364.5f, 0.0f, -409.3f), new Vector3(371.4f, 0.0f, -392.6f),
                    new Vector3(390.0f, 0.0f, -342.0f), new Vector3(396.2f, 0.0f, -327.2f), new Vector3(400.6f, 0.0f, -318.2f),
                    new Vector3(404.5f, 0.0f, -311.2f), new Vector3(408.9f, 0.0f, -304.6f), new Vector3(413.9f, 0.0f, -298.3f),
                    new Vector3(419.4f, 0.0f, -292.5f), new Vector3(425.2f, 0.0f, -287.0f), new Vector3(432.9f, 0.0f, -280.6f),
                    new Vector3(441.0f, 0.0f, -274.7f), new Vector3(447.7f, 0.0f, -270.4f), new Vector3(454.8f, 0.0f, -266.7f),
                    new Vector3(462.2f, 0.0f, -263.6f), new Vector3(469.8f, 0.0f, -261.2f), new Vector3(477.6f, 0.0f, -259.3f),
                    new Vector3(485.4f, 0.0f, -257.8f), new Vector3(493.4f, 0.0f, -256.8f), new Vector3(503.3f, 0.0f, -256.2f),
                    new Vector3(511.3f, 0.0f, -256.1f), new Vector3(519.3f, 0.0f, -256.6f), new Vector3(527.2f, 0.0f, -257.6f),
                    new Vector3(535.0f, 0.0f, -259.4f), new Vector3(542.7f, 0.0f, -261.8f), new Vector3(552.0f, 0.0f, -265.4f),
                    new Vector3(615.7f, 0.0f, -294.3f), new Vector3(653.5f, 0.0f, -312.7f), new Vector3(660.5f, 0.0f, -316.6f),
                    new Vector3(667.1f, 0.0f, -321.1f), new Vector3(673.3f, 0.0f, -326.1f), new Vector3(679.2f, 0.0f, -331.5f),
                    new Vector3(684.7f, 0.0f, -337.4f), new Vector3(689.3f, 0.0f, -343.9f), new Vector3(697.6f, 0.0f, -357.6f),
                    new Vector3(701.1f, 0.0f, -364.7f), new Vector3(702.3f, 0.0f, -372.6f), new Vector3(700.9f, 0.0f, -380.4f),
                    new Vector3(697.5f, 0.0f, -387.7f), new Vector3(692.8f, 0.0f, -394.2f), new Vector3(687.4f, 0.0f, -400.1f),
                    new Vector3(680.1f, 0.0f, -406.9f), new Vector3(674.0f, 0.0f, -412.0f), new Vector3(667.4f, 0.0f, -416.5f),
                    new Vector3(607.8f, 0.0f, -453.3f), new Vector3(547.9f, 0.0f, -489.5f), new Vector3(487.9f, 0.0f, -525.6f),
                    new Vector3(427.9f, 0.0f, -561.6f), new Vector3(367.9f, 0.0f, -597.6f), new Vector3(307.8f, 0.0f, -633.6f),
                    new Vector3(247.8f, 0.0f, -669.6f), new Vector3(187.7f, 0.0f, -705.5f), new Vector3(127.6f, 0.0f, -741.5f),
                    new Vector3(67.6f, 0.0f, -777.4f), new Vector3(52.0f, 0.0f, -786.5f), new Vector3(44.4f, 0.0f, -788.8f),
                    new Vector3(36.4f, 0.0f, -789.5f), new Vector3(28.6f, 0.0f, -788.1f), new Vector3(23.0f, 0.0f, -782.6f),
                    new Vector3(18.9f, 0.0f, -775.8f), new Vector3(5.8f, 0.0f, -751.0f), new Vector3(-1.8f, 0.0f, -734.7f),
                    new Vector3(-5.4f, 0.0f, -725.4f), new Vector3(-7.7f, 0.0f, -717.7f), new Vector3(-9.0f, 0.0f, -709.8f),
                    new Vector3(-10.0f, 0.0f, -693.9f), new Vector3(-10.5f, 0.0f, -623.9f), new Vector3(-10.3f, 0.0f, -553.9f),
                    new Vector3(-9.8f, 0.0f, -483.9f), new Vector3(-8.6f, 0.0f, -413.9f), new Vector3(-6.7f, 0.0f, -343.9f),
                    new Vector3(-4.3f, 0.0f, -274.0f), new Vector3(-2.2f, 0.0f, -204.0f), new Vector3(-0.9f, 0.0f, -134.0f),
                    new Vector3(-0.2f, 0.0f, -64.0f)
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

            // Zone COUNT is per circuit, not a constant two. Monaco and Suzuka run a
            // single activation zone; Bahrain, Jeddah, Miami, Austria, Mexico and
            // Singapore run three. AddDrsZone skips a Vector2.zero entry, so the spec
            // expresses the real count directly.
            AddDrsZone(asset, spec.DrsZoneOneNormalized, length);
            AddDrsZone(asset, spec.DrsZoneTwoNormalized, length);
            AddDrsZone(asset, spec.DrsZoneThreeNormalized, length);

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
