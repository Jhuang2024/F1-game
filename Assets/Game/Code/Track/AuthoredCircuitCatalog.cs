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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 90.1f), new Vector3(0.0f, 0.0f, 180.2f),
                    new Vector3(0.0f, 0.0f, 270.3f), new Vector3(0.0f, 0.0f, 360.4f), new Vector3(0.1f, 0.0f, 450.5f),
                    new Vector3(0.8f, 0.0f, 540.6f), new Vector3(1.4f, 0.0f, 612.6f), new Vector3(11.8f, 0.0f, 624.9f),
                    new Vector3(29.8f, 0.0f, 625.2f), new Vector3(43.0f, 0.0f, 635.6f), new Vector3(40.6f, 0.0f, 653.1f),
                    new Vector3(10.4f, 0.0f, 738.0f), new Vector3(4.0f, 0.0f, 764.2f), new Vector3(-0.1f, 0.0f, 790.9f),
                    new Vector3(-1.2f, 0.0f, 881.0f), new Vector3(-1.8f, 0.0f, 944.1f), new Vector3(0.7f, 0.0f, 971.0f),
                    new Vector3(3.4f, 0.0f, 988.8f), new Vector3(15.5f, 0.0f, 1032.1f), new Vector3(25.4f, 0.0f, 1057.3f),
                    new Vector3(37.9f, 0.0f, 1081.2f), new Vector3(52.9f, 0.0f, 1103.7f), new Vector3(75.7f, 0.0f, 1131.6f),
                    new Vector3(94.6f, 0.0f, 1150.9f), new Vector3(122.3f, 0.0f, 1173.9f), new Vector3(175.3f, 0.0f, 1208.0f),
                    new Vector3(233.6f, 0.0f, 1232.0f), new Vector3(250.8f, 0.0f, 1237.3f), new Vector3(285.8f, 0.0f, 1245.9f),
                    new Vector3(374.5f, 0.0f, 1261.3f), new Vector3(463.3f, 0.0f, 1276.6f), new Vector3(552.4f, 0.0f, 1289.9f),
                    new Vector3(641.6f, 0.0f, 1302.9f), new Vector3(677.2f, 0.0f, 1308.3f), new Vector3(689.1f, 0.0f, 1320.7f),
                    new Vector3(692.5f, 0.0f, 1338.4f), new Vector3(701.6f, 0.0f, 1352.9f), new Vector3(718.8f, 0.0f, 1358.4f),
                    new Vector3(761.9f, 0.0f, 1371.3f), new Vector3(844.7f, 0.0f, 1406.7f), new Vector3(927.4f, 0.0f, 1442.6f),
                    new Vector3(977.0f, 0.0f, 1463.9f), new Vector3(994.3f, 0.0f, 1468.7f), new Vector3(1012.3f, 0.0f, 1468.9f),
                    new Vector3(1029.8f, 0.0f, 1465.1f), new Vector3(1045.8f, 0.0f, 1456.9f), new Vector3(1059.7f, 0.0f, 1445.6f),
                    new Vector3(1070.9f, 0.0f, 1431.6f), new Vector3(1077.4f, 0.0f, 1414.8f), new Vector3(1093.6f, 0.0f, 1326.2f),
                    new Vector3(1110.2f, 0.0f, 1237.7f), new Vector3(1121.4f, 0.0f, 1175.6f), new Vector3(1120.5f, 0.0f, 1157.8f),
                    new Vector3(1111.3f, 0.0f, 1142.8f), new Vector3(1096.9f, 0.0f, 1132.0f), new Vector3(1021.6f, 0.0f, 1082.6f),
                    new Vector3(946.7f, 0.0f, 1032.5f), new Vector3(872.0f, 0.0f, 982.1f), new Vector3(812.5f, 0.0f, 941.5f),
                    new Vector3(758.3f, 0.0f, 894.0f), new Vector3(695.3f, 0.0f, 829.6f), new Vector3(632.6f, 0.0f, 764.9f),
                    new Vector3(571.1f, 0.0f, 699.0f), new Vector3(508.7f, 0.0f, 634.1f), new Vector3(446.3f, 0.0f, 569.1f),
                    new Vector3(384.0f, 0.0f, 504.1f), new Vector3(340.5f, 0.0f, 458.3f), new Vector3(329.8f, 0.0f, 444.0f),
                    new Vector3(327.3f, 0.0f, 426.6f), new Vector3(330.5f, 0.0f, 408.9f), new Vector3(334.2f, 0.0f, 391.3f),
                    new Vector3(336.0f, 0.0f, 373.4f), new Vector3(334.8f, 0.0f, 355.5f), new Vector3(330.2f, 0.0f, 338.1f),
                    new Vector3(322.7f, 0.0f, 321.8f), new Vector3(312.8f, 0.0f, 306.8f), new Vector3(300.7f, 0.0f, 293.5f),
                    new Vector3(287.6f, 0.0f, 281.1f), new Vector3(278.0f, 0.0f, 266.1f), new Vector3(274.4f, 0.0f, 248.5f),
                    new Vector3(270.7f, 0.0f, 158.5f), new Vector3(267.8f, 0.0f, 68.5f), new Vector3(266.2f, 0.0f, -21.6f),
                    new Vector3(264.5f, 0.0f, -111.7f), new Vector3(262.9f, 0.0f, -201.8f), new Vector3(261.3f, 0.0f, -291.8f),
                    new Vector3(259.6f, 0.0f, -381.9f), new Vector3(258.0f, 0.0f, -472.0f), new Vector3(256.4f, 0.0f, -562.1f),
                    new Vector3(254.7f, 0.0f, -652.2f), new Vector3(253.3f, 0.0f, -706.2f), new Vector3(250.4f, 0.0f, -723.9f),
                    new Vector3(242.7f, 0.0f, -740.1f), new Vector3(231.5f, 0.0f, -754.1f), new Vector3(217.8f, 0.0f, -765.8f),
                    new Vector3(201.9f, 0.0f, -774.0f), new Vector3(184.6f, 0.0f, -779.0f), new Vector3(166.6f, 0.0f, -779.7f),
                    new Vector3(148.9f, 0.0f, -776.8f), new Vector3(123.2f, 0.0f, -768.5f), new Vector3(107.2f, 0.0f, -760.2f),
                    new Vector3(92.0f, 0.0f, -750.5f), new Vector3(72.2f, 0.0f, -732.3f), new Vector3(60.6f, 0.0f, -718.4f),
                    new Vector3(50.4f, 0.0f, -703.6f), new Vector3(30.7f, 0.0f, -663.1f), new Vector3(21.9f, 0.0f, -637.6f),
                    new Vector3(12.7f, 0.0f, -593.5f), new Vector3(3.3f, 0.0f, -503.9f), new Vector3(-5.5f, 0.0f, -414.2f),
                    new Vector3(-7.7f, 0.0f, -324.2f), new Vector3(-6.2f, 0.0f, -234.2f), new Vector3(-4.5f, 0.0f, -144.1f),
                    new Vector3(-2.1f, 0.0f, -54.0f)
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
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.1f, 90.1f), new Vector3(2.0f, 0.1f, 108.0f),
                    new Vector3(6.0f, 0.2f, 125.6f), new Vector3(13.8f, 0.2f, 141.6f), new Vector3(24.1f, 0.3f, 156.4f),
                    new Vector3(36.9f, 0.3f, 169.0f), new Vector3(51.5f, 0.4f, 179.6f), new Vector3(68.0f, 0.5f, 186.6f),
                    new Vector3(85.7f, 0.5f, 190.0f), new Vector3(103.5f, 0.6f, 190.1f), new Vector3(121.0f, 0.7f, 185.8f),
                    new Vector3(137.9f, 0.8f, 179.5f), new Vector3(153.2f, 0.9f, 170.2f), new Vector3(164.2f, 0.9f, 156.0f),
                    new Vector3(169.6f, 1.0f, 139.1f), new Vector3(171.0f, 1.1f, 121.4f), new Vector3(166.3f, 1.2f, 104.0f),
                    new Vector3(155.4f, 1.3f, 89.8f), new Vector3(138.8f, 1.4f, 83.5f), new Vector3(121.7f, 1.5f, 88.9f),
                    new Vector3(98.0f, 1.6f, 101.9f), new Vector3(81.7f, 1.7f, 108.8f), new Vector3(64.8f, 1.8f, 103.3f),
                    new Vector3(53.0f, 1.9f, 90.2f), new Vector3(49.8f, 2.0f, 73.0f), new Vector3(54.6f, 2.1f, 56.1f),
                    new Vector3(67.7f, 2.1f, 43.7f), new Vector3(82.0f, 2.2f, 32.8f), new Vector3(98.1f, 2.3f, 24.7f),
                    new Vector3(115.3f, 2.4f, 19.3f), new Vector3(133.2f, 2.5f, 19.2f), new Vector3(159.3f, 2.6f, 26.3f),
                    new Vector3(191.8f, 2.7f, 41.9f), new Vector3(255.1f, 2.9f, 76.4f), new Vector3(312.8f, 3.0f, 101.9f),
                    new Vector3(392.4f, 3.0f, 144.2f), new Vector3(408.9f, 3.0f, 151.0f), new Vector3(426.3f, 3.0f, 155.8f),
                    new Vector3(516.1f, 3.2f, 163.7f), new Vector3(605.9f, 3.4f, 171.8f), new Vector3(641.9f, 3.5f, 173.8f),
                    new Vector3(655.7f, 3.5f, 164.6f), new Vector3(653.8f, 3.6f, 147.4f), new Vector3(643.5f, 3.6f, 133.0f),
                    new Vector3(629.9f, 3.7f, 121.3f), new Vector3(615.1f, 3.7f, 111.0f), new Vector3(575.9f, 3.8f, 88.7f),
                    new Vector3(559.3f, 3.9f, 81.8f), new Vector3(533.8f, 4.0f, 73.0f), new Vector3(446.9f, 4.3f, 48.7f),
                    new Vector3(369.2f, 4.5f, 25.6f), new Vector3(345.0f, 4.6f, 13.6f), new Vector3(329.7f, 4.6f, 4.1f),
                    new Vector3(317.4f, 4.7f, -9.1f), new Vector3(306.8f, 4.7f, -23.7f), new Vector3(298.2f, 4.7f, -39.4f),
                    new Vector3(292.1f, 4.8f, -56.3f), new Vector3(288.3f, 4.8f, -73.9f), new Vector3(287.4f, 4.8f, -91.9f),
                    new Vector3(289.3f, 4.9f, -109.8f), new Vector3(294.1f, 4.9f, -127.1f), new Vector3(300.7f, 4.9f, -143.9f),
                    new Vector3(308.7f, 4.9f, -160.0f), new Vector3(319.2f, 5.0f, -174.7f), new Vector3(332.6f, 5.0f, -186.6f),
                    new Vector3(395.4f, 5.0f, -238.0f), new Vector3(407.8f, 5.0f, -251.1f), new Vector3(417.5f, 5.0f, -266.2f),
                    new Vector3(422.9f, 5.0f, -283.3f), new Vector3(425.8f, 5.0f, -301.1f), new Vector3(424.3f, 4.9f, -318.9f),
                    new Vector3(419.3f, 4.9f, -336.2f), new Vector3(410.2f, 4.9f, -351.6f), new Vector3(397.4f, 4.9f, -364.2f),
                    new Vector3(382.7f, 4.8f, -374.4f), new Vector3(365.8f, 4.8f, -380.6f), new Vector3(286.9f, 4.6f, -399.1f),
                    new Vector3(272.1f, 4.6f, -408.7f), new Vector3(266.4f, 4.5f, -425.3f), new Vector3(269.2f, 4.5f, -442.7f),
                    new Vector3(308.7f, 4.3f, -491.9f), new Vector3(322.4f, 4.2f, -503.2f), new Vector3(340.1f, 4.2f, -504.2f),
                    new Vector3(426.6f, 3.9f, -478.9f), new Vector3(513.1f, 3.6f, -453.4f), new Vector3(599.2f, 3.4f, -426.5f),
                    new Vector3(685.2f, 3.2f, -399.6f), new Vector3(771.3f, 3.1f, -373.1f), new Vector3(782.6f, 3.0f, -361.0f),
                    new Vector3(777.9f, 3.0f, -344.1f), new Vector3(768.0f, 3.0f, -329.0f), new Vector3(757.6f, 3.0f, -314.4f),
                    new Vector3(753.9f, 3.0f, -296.9f), new Vector3(759.9f, 3.0f, -280.6f), new Vector3(775.0f, 3.0f, -270.7f),
                    new Vector3(790.9f, 3.0f, -262.3f), new Vector3(808.3f, 3.0f, -257.7f), new Vector3(826.2f, 3.0f, -257.3f),
                    new Vector3(843.8f, 2.9f, -260.9f), new Vector3(860.6f, 2.9f, -267.4f), new Vector3(876.2f, 2.9f, -276.3f),
                    new Vector3(888.5f, 2.9f, -289.2f), new Vector3(897.7f, 2.8f, -304.6f), new Vector3(902.4f, 2.8f, -321.8f),
                    new Vector3(903.6f, 2.8f, -339.7f), new Vector3(902.1f, 2.7f, -357.7f), new Vector3(898.0f, 2.7f, -375.2f),
                    new Vector3(891.0f, 2.7f, -391.7f), new Vector3(882.2f, 2.6f, -407.4f), new Vector3(871.0f, 2.6f, -421.5f),
                    new Vector3(858.3f, 2.5f, -434.1f), new Vector3(835.5f, 2.4f, -448.8f), new Vector3(750.8f, 2.2f, -479.6f),
                    new Vector3(665.6f, 1.9f, -509.0f), new Vector3(580.4f, 1.6f, -538.5f), new Vector3(495.2f, 1.4f, -567.9f),
                    new Vector3(410.1f, 1.2f, -597.5f), new Vector3(325.1f, 1.1f, -627.5f), new Vector3(240.0f, 1.0f, -657.5f),
                    new Vector3(155.0f, 1.0f, -687.4f), new Vector3(69.8f, 1.1f, -717.0f), new Vector3(-15.6f, 1.2f, -745.6f),
                    new Vector3(-100.5f, 1.3f, -776.0f), new Vector3(-185.3f, 1.4f, -806.7f), new Vector3(-269.8f, 1.6f, -837.9f),
                    new Vector3(-295.2f, 1.6f, -847.3f), new Vector3(-312.7f, 1.6f, -850.3f), new Vector3(-325.8f, 1.7f, -839.7f),
                    new Vector3(-319.0f, 1.7f, -823.6f), new Vector3(-307.5f, 1.7f, -809.7f), new Vector3(-294.2f, 1.7f, -797.6f),
                    new Vector3(-280.4f, 1.8f, -786.0f), new Vector3(-264.3f, 1.8f, -778.2f), new Vector3(-180.8f, 1.9f, -744.2f),
                    new Vector3(-97.3f, 2.0f, -710.2f), new Vector3(-22.1f, 2.0f, -679.7f), new Vector3(-11.0f, 2.0f, -666.6f),
                    new Vector3(-8.3f, 2.0f, -649.0f), new Vector3(-7.1f, 1.9f, -558.9f), new Vector3(-5.9f, 1.6f, -468.7f),
                    new Vector3(-4.8f, 1.2f, -378.6f), new Vector3(-3.6f, 0.8f, -288.5f), new Vector3(-2.5f, 0.4f, -198.3f),
                    new Vector3(-1.4f, 0.1f, -108.2f), new Vector3(-0.2f, 0.0f, -18.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 90.1f), new Vector3(0.0f, 0.0f, 180.1f),
                    new Vector3(0.0f, 0.0f, 234.2f), new Vector3(6.1f, 0.0f, 250.1f), new Vector3(23.4f, 0.0f, 254.2f),
                    new Vector3(40.5f, 0.0f, 249.5f), new Vector3(74.0f, 0.0f, 236.3f), new Vector3(91.5f, 0.0f, 232.1f),
                    new Vector3(108.5f, 0.0f, 235.2f), new Vector3(124.7f, 0.0f, 242.7f), new Vector3(145.3f, 0.0f, 260.2f),
                    new Vector3(169.8f, 0.0f, 271.6f), new Vector3(187.1f, 0.0f, 275.7f), new Vector3(204.7f, 0.0f, 275.7f),
                    new Vector3(247.2f, 0.0f, 260.8f), new Vector3(282.4f, 0.0f, 232.7f), new Vector3(301.6f, 0.0f, 213.8f),
                    new Vector3(312.6f, 0.0f, 199.6f), new Vector3(327.4f, 0.0f, 177.0f), new Vector3(335.0f, 0.0f, 160.7f),
                    new Vector3(341.6f, 0.0f, 144.0f), new Vector3(344.9f, 0.0f, 126.4f), new Vector3(348.6f, 0.0f, 99.6f),
                    new Vector3(350.4f, 0.0f, 9.6f), new Vector3(352.0f, 0.0f, -80.4f), new Vector3(353.6f, 0.0f, -170.5f),
                    new Vector3(360.3f, 0.0f, -187.1f), new Vector3(369.2f, 0.0f, -202.7f), new Vector3(381.2f, 0.0f, -216.1f),
                    new Vector3(394.9f, 0.0f, -227.8f), new Vector3(410.6f, 0.0f, -236.6f), new Vector3(435.9f, 0.0f, -245.9f),
                    new Vector3(452.3f, 0.0f, -253.3f), new Vector3(465.6f, 0.0f, -265.0f), new Vector3(476.6f, 0.0f, -279.1f),
                    new Vector3(484.6f, 0.0f, -295.2f), new Vector3(487.7f, 0.0f, -322.1f), new Vector3(481.5f, 0.0f, -357.5f),
                    new Vector3(476.1f, 0.0f, -383.9f), new Vector3(479.3f, 0.0f, -410.6f), new Vector3(485.7f, 0.0f, -427.5f),
                    new Vector3(496.3f, 0.0f, -441.8f), new Vector3(508.5f, 0.0f, -455.0f), new Vector3(529.1f, 0.0f, -472.5f),
                    new Vector3(544.2f, 0.0f, -482.2f), new Vector3(560.8f, 0.0f, -488.7f), new Vector3(578.5f, 0.0f, -491.7f),
                    new Vector3(596.5f, 0.0f, -491.9f), new Vector3(613.8f, 0.0f, -487.6f), new Vector3(630.3f, 0.0f, -480.9f),
                    new Vector3(645.3f, 0.0f, -470.9f), new Vector3(656.1f, 0.0f, -456.6f), new Vector3(659.3f, 0.0f, -439.3f),
                    new Vector3(655.3f, 0.0f, -422.2f), new Vector3(644.9f, 0.0f, -407.6f), new Vector3(632.3f, 0.0f, -394.7f),
                    new Vector3(617.6f, 0.0f, -372.1f), new Vector3(610.9f, 0.0f, -355.5f), new Vector3(579.7f, 0.0f, -271.0f),
                    new Vector3(560.7f, 0.0f, -220.4f), new Vector3(551.8f, 0.0f, -204.9f), new Vector3(501.9f, 0.0f, -129.9f),
                    new Vector3(451.1f, 0.0f, -55.5f), new Vector3(410.4f, 0.0f, 4.0f), new Vector3(403.9f, 0.0f, 20.5f),
                    new Vector3(383.5f, 0.0f, 108.2f), new Vector3(362.8f, 0.0f, 195.8f), new Vector3(356.1f, 0.0f, 222.0f),
                    new Vector3(332.0f, 0.0f, 280.2f), new Vector3(299.3f, 0.0f, 334.2f), new Vector3(257.9f, 0.0f, 381.7f),
                    new Vector3(244.5f, 0.0f, 393.6f), new Vector3(173.9f, 0.0f, 449.5f), new Vector3(102.9f, 0.0f, 504.9f),
                    new Vector3(31.3f, 0.0f, 559.5f), new Vector3(-13.0f, 0.0f, 590.5f), new Vector3(-90.8f, 0.0f, 635.8f),
                    new Vector3(-114.2f, 0.0f, 649.3f), new Vector3(-171.1f, 0.0f, 676.3f), new Vector3(-189.1f, 0.0f, 677.7f),
                    new Vector3(-198.5f, 0.0f, 663.0f), new Vector3(-200.0f, 0.0f, 645.1f), new Vector3(-199.8f, 0.0f, 627.1f),
                    new Vector3(-201.2f, 0.0f, 609.2f), new Vector3(-208.9f, 0.0f, 593.3f), new Vector3(-221.3f, 0.0f, 580.6f),
                    new Vector3(-237.1f, 0.0f, 572.3f), new Vector3(-254.2f, 0.0f, 568.3f), new Vector3(-271.3f, 0.0f, 573.3f),
                    new Vector3(-284.6f, 0.0f, 585.0f), new Vector3(-306.5f, 0.0f, 624.3f), new Vector3(-318.0f, 0.0f, 638.1f),
                    new Vector3(-334.0f, 0.0f, 645.8f), new Vector3(-351.5f, 0.0f, 650.0f), new Vector3(-396.5f, 0.0f, 647.9f),
                    new Vector3(-406.5f, 0.0f, 636.7f), new Vector3(-405.9f, 0.0f, 618.8f), new Vector3(-418.9f, 0.0f, 607.7f),
                    new Vector3(-479.7f, 0.0f, 591.4f), new Vector3(-490.3f, 0.0f, 578.6f), new Vector3(-485.4f, 0.0f, 561.9f),
                    new Vector3(-442.2f, 0.0f, 483.0f), new Vector3(-397.2f, 0.0f, 405.0f), new Vector3(-352.3f, 0.0f, 326.9f),
                    new Vector3(-307.3f, 0.0f, 248.9f), new Vector3(-262.3f, 0.0f, 170.9f), new Vector3(-217.4f, 0.0f, 92.8f),
                    new Vector3(-172.4f, 0.0f, 14.8f), new Vector3(-127.4f, 0.0f, -63.2f), new Vector3(-83.2f, 0.0f, -141.7f),
                    new Vector3(-39.0f, 0.0f, -220.1f), new Vector3(5.2f, 0.0f, -298.6f), new Vector3(49.4f, 0.0f, -377.1f),
                    new Vector3(93.6f, 0.0f, -455.5f), new Vector3(137.8f, 0.0f, -534.0f), new Vector3(146.7f, 0.0f, -549.6f),
                    new Vector3(160.2f, 0.0f, -560.2f), new Vector3(175.3f, 0.0f, -551.7f), new Vector3(182.9f, 0.0f, -536.0f),
                    new Vector3(185.8f, 0.0f, -518.3f), new Vector3(189.1f, 0.0f, -482.4f), new Vector3(186.9f, 0.0f, -446.5f),
                    new Vector3(178.9f, 0.0f, -420.7f), new Vector3(168.4f, 0.0f, -406.1f), new Vector3(155.4f, 0.0f, -393.9f),
                    new Vector3(141.2f, 0.0f, -382.8f), new Vector3(80.7f, 0.0f, -343.7f), new Vector3(54.4f, 0.0f, -319.1f),
                    new Vector3(27.1f, 0.0f, -283.3f), new Vector3(8.4f, 0.0f, -242.4f), new Vector3(0.6f, 0.0f, -198.1f),
                    new Vector3(0.2f, 0.0f, -108.1f), new Vector3(0.0f, 0.0f, -18.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.1f, 90.1f), new Vector3(-0.2f, 0.3f, 180.2f),
                    new Vector3(2.7f, 0.5f, 252.2f), new Vector3(10.5f, 0.6f, 296.5f), new Vector3(15.3f, 0.7f, 313.9f),
                    new Vector3(44.2f, 1.1f, 399.2f), new Vector3(52.8f, 1.2f, 424.8f), new Vector3(55.0f, 1.2f, 442.6f),
                    new Vector3(46.8f, 1.3f, 457.9f), new Vector3(10.7f, 1.4f, 484.8f), new Vector3(4.6f, 1.5f, 500.9f),
                    new Vector3(11.2f, 1.6f, 517.3f), new Vector3(25.0f, 1.6f, 528.3f), new Vector3(42.3f, 1.7f, 532.3f),
                    new Vector3(60.0f, 1.7f, 528.6f), new Vector3(116.2f, 1.9f, 500.1f), new Vector3(147.3f, 1.9f, 481.9f),
                    new Vector3(174.8f, 2.0f, 458.7f), new Vector3(199.8f, 2.0f, 432.8f), new Vector3(254.2f, 2.1f, 360.9f),
                    new Vector3(279.9f, 2.1f, 323.9f), new Vector3(286.8f, 2.2f, 307.4f), new Vector3(284.2f, 2.2f, 289.7f),
                    new Vector3(275.2f, 2.3f, 274.1f), new Vector3(267.9f, 2.3f, 257.7f), new Vector3(269.1f, 2.4f, 240.0f),
                    new Vector3(309.9f, 2.7f, 159.7f), new Vector3(322.3f, 2.8f, 135.6f), new Vector3(332.6f, 2.8f, 120.9f),
                    new Vector3(356.7f, 3.0f, 94.1f), new Vector3(367.8f, 3.0f, 80.0f), new Vector3(379.3f, 3.1f, 55.5f),
                    new Vector3(383.8f, 3.2f, 28.9f), new Vector3(384.7f, 3.3f, 1.9f), new Vector3(382.9f, 3.4f, -16.0f),
                    new Vector3(363.6f, 3.7f, -104.0f), new Vector3(350.2f, 3.9f, -165.7f), new Vector3(357.2f, 3.9f, -181.8f),
                    new Vector3(372.1f, 3.9f, -191.2f), new Vector3(389.7f, 3.9f, -192.5f), new Vector3(407.7f, 4.0f, -193.2f),
                    new Vector3(424.0f, 4.0f, -200.5f), new Vector3(436.5f, 4.0f, -213.4f), new Vector3(445.6f, 4.0f, -228.9f),
                    new Vector3(451.2f, 4.0f, -245.9f), new Vector3(453.9f, 4.0f, -272.8f), new Vector3(454.7f, 3.9f, -317.9f),
                    new Vector3(452.5f, 3.9f, -335.7f), new Vector3(440.2f, 3.7f, -424.9f), new Vector3(435.2f, 3.6f, -460.6f),
                    new Vector3(414.4f, 3.3f, -548.3f), new Vector3(397.0f, 3.0f, -608.9f), new Vector3(371.9f, 2.8f, -676.4f),
                    new Vector3(332.8f, 2.5f, -757.6f), new Vector3(301.1f, 2.2f, -822.3f), new Vector3(291.2f, 2.2f, -837.3f),
                    new Vector3(274.8f, 2.2f, -843.0f), new Vector3(256.9f, 2.1f, -841.4f), new Vector3(239.2f, 2.1f, -843.9f),
                    new Vector3(223.4f, 2.1f, -852.0f), new Vector3(210.0f, 2.0f, -864.0f), new Vector3(199.2f, 2.0f, -878.4f),
                    new Vector3(190.3f, 2.0f, -894.0f), new Vector3(150.9f, 2.0f, -975.0f), new Vector3(113.2f, 1.9f, -1056.8f),
                    new Vector3(92.7f, 1.8f, -1106.8f), new Vector3(67.4f, 1.7f, -1193.3f), new Vector3(57.0f, 1.6f, -1237.1f),
                    new Vector3(54.6f, 1.6f, -1254.9f), new Vector3(53.5f, 1.5f, -1272.9f), new Vector3(52.5f, 1.4f, -1363.0f),
                    new Vector3(51.8f, 1.3f, -1408.0f), new Vector3(45.8f, 1.2f, -1424.1f), new Vector3(29.0f, 1.2f, -1427.3f),
                    new Vector3(16.8f, 1.2f, -1415.2f), new Vector3(17.6f, 1.2f, -1397.3f), new Vector3(26.4f, 1.1f, -1316.7f),
                    new Vector3(24.3f, 1.0f, -1298.8f), new Vector3(2.8f, 1.0f, -1211.3f), new Vector3(-11.3f, 1.0f, -1122.3f),
                    new Vector3(-25.5f, 1.1f, -1033.3f), new Vector3(-38.9f, 1.3f, -944.2f), new Vector3(-40.3f, 1.3f, -926.3f),
                    new Vector3(-39.8f, 1.5f, -836.2f), new Vector3(-39.3f, 1.6f, -746.1f), new Vector3(-38.8f, 1.8f, -656.0f),
                    new Vector3(-38.2f, 1.9f, -565.9f), new Vector3(-37.7f, 2.0f, -475.8f), new Vector3(-37.2f, 2.0f, -385.7f),
                    new Vector3(-36.7f, 1.6f, -295.6f), new Vector3(-36.4f, 1.3f, -250.6f), new Vector3(-29.6f, 1.2f, -234.9f),
                    new Vector3(-12.9f, 1.1f, -229.3f), new Vector3(-1.7f, 1.0f, -216.1f), new Vector3(-0.4f, 0.9f, -198.2f),
                    new Vector3(-0.8f, 0.3f, -108.1f), new Vector3(-0.2f, 0.0f, -18.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.8f, 90.1f), new Vector3(0.0f, 2.7f, 180.3f),
                    new Vector3(-0.2f, 5.0f, 270.4f), new Vector3(-0.8f, 7.0f, 360.6f), new Vector3(-0.6f, 8.0f, 450.7f),
                    new Vector3(-0.7f, 8.3f, 540.9f), new Vector3(-1.2f, 9.3f, 631.0f), new Vector3(-1.3f, 10.0f, 685.1f),
                    new Vector3(1.8f, 10.3f, 702.8f), new Vector3(11.1f, 10.6f, 717.7f), new Vector3(26.3f, 10.9f, 727.1f),
                    new Vector3(44.0f, 11.2f, 729.7f), new Vector3(71.0f, 11.6f, 729.6f), new Vector3(88.5f, 11.9f, 733.6f),
                    new Vector3(104.4f, 12.2f, 741.9f), new Vector3(117.3f, 12.4f, 754.5f), new Vector3(124.6f, 12.7f, 771.0f),
                    new Vector3(153.7f, 13.6f, 846.7f), new Vector3(163.3f, 13.7f, 861.9f), new Vector3(174.6f, 13.8f, 876.0f),
                    new Vector3(188.2f, 13.9f, 887.7f), new Vector3(203.7f, 14.0f, 896.9f), new Vector3(220.0f, 14.0f, 904.5f),
                    new Vector3(237.6f, 14.0f, 908.2f), new Vector3(255.6f, 14.0f, 909.2f), new Vector3(273.4f, 14.0f, 906.8f),
                    new Vector3(290.6f, 13.9f, 901.7f), new Vector3(307.5f, 13.9f, 895.4f), new Vector3(322.9f, 13.9f, 886.0f),
                    new Vector3(337.0f, 13.8f, 874.8f), new Vector3(349.2f, 13.7f, 861.7f), new Vector3(360.3f, 13.7f, 847.5f),
                    new Vector3(369.9f, 13.6f, 832.2f), new Vector3(377.7f, 13.6f, 816.0f), new Vector3(389.4f, 13.4f, 781.9f),
                    new Vector3(395.8f, 13.3f, 755.6f), new Vector3(398.1f, 13.2f, 737.8f), new Vector3(398.6f, 12.8f, 647.7f),
                    new Vector3(398.6f, 12.4f, 557.5f), new Vector3(398.7f, 12.2f, 485.4f), new Vector3(394.3f, 12.1f, 468.1f),
                    new Vector3(383.7f, 12.1f, 453.8f), new Vector3(369.5f, 12.1f, 443.1f), new Vector3(352.3f, 12.0f, 438.2f),
                    new Vector3(334.4f, 12.0f, 438.6f), new Vector3(316.8f, 12.0f, 442.3f), new Vector3(301.0f, 12.0f, 450.9f),
                    new Vector3(287.1f, 12.0f, 462.1f), new Vector3(276.1f, 11.9f, 476.3f), new Vector3(266.5f, 11.8f, 491.5f),
                    new Vector3(259.4f, 11.7f, 508.1f), new Vector3(255.8f, 11.5f, 525.7f), new Vector3(253.3f, 11.3f, 552.6f),
                    new Vector3(253.7f, 10.0f, 642.7f), new Vector3(254.2f, 8.6f, 723.8f), new Vector3(247.1f, 8.3f, 740.3f),
                    new Vector3(233.0f, 8.0f, 751.1f), new Vector3(215.5f, 7.6f, 754.0f), new Vector3(199.0f, 7.3f, 747.6f),
                    new Vector3(187.3f, 7.0f, 734.0f), new Vector3(137.6f, 5.5f, 658.8f), new Vector3(107.8f, 4.8f, 613.7f),
                    new Vector3(96.1f, 4.5f, 589.3f), new Vector3(89.9f, 4.3f, 572.4f), new Vector3(82.9f, 4.2f, 546.3f),
                    new Vector3(80.5f, 4.1f, 528.4f), new Vector3(79.6f, 3.9f, 438.3f), new Vector3(80.0f, 3.8f, 420.3f),
                    new Vector3(83.5f, 3.7f, 402.7f), new Vector3(95.6f, 3.6f, 389.6f), new Vector3(112.0f, 3.4f, 382.8f),
                    new Vector3(129.9f, 3.3f, 381.6f), new Vector3(147.9f, 3.1f, 382.1f), new Vector3(165.8f, 2.9f, 380.0f),
                    new Vector3(182.9f, 2.7f, 374.2f), new Vector3(198.7f, 2.5f, 365.7f), new Vector3(272.5f, 1.3f, 313.9f),
                    new Vector3(346.5f, 0.1f, 262.4f), new Vector3(359.2f, -0.2f, 249.7f), new Vector3(368.9f, -0.4f, 234.5f),
                    new Vector3(376.0f, -0.6f, 218.0f), new Vector3(378.7f, -0.8f, 200.3f), new Vector3(378.3f, -1.0f, 182.3f),
                    new Vector3(374.6f, -1.2f, 164.7f), new Vector3(367.5f, -1.4f, 148.2f), new Vector3(321.6f, -1.9f, 70.7f),
                    new Vector3(275.6f, -1.9f, -6.9f), new Vector3(229.7f, -1.3f, -84.5f), new Vector3(183.7f, -0.3f, -162.0f),
                    new Vector3(137.8f, 0.9f, -239.6f), new Vector3(115.4f, 1.5f, -278.7f), new Vector3(112.8f, 1.8f, -296.2f),
                    new Vector3(120.5f, 2.0f, -312.0f), new Vector3(135.4f, 2.2f, -321.8f), new Vector3(153.2f, 2.5f, -324.6f),
                    new Vector3(171.1f, 2.7f, -323.1f), new Vector3(188.4f, 2.9f, -318.3f), new Vector3(204.6f, 3.1f, -310.5f),
                    new Vector3(219.7f, 3.2f, -300.7f), new Vector3(232.4f, 3.4f, -288.1f), new Vector3(246.4f, 3.6f, -264.9f),
                    new Vector3(253.2f, 3.7f, -248.3f), new Vector3(258.5f, 3.8f, -231.1f), new Vector3(265.3f, 3.9f, -214.4f),
                    new Vector3(275.3f, 4.0f, -199.6f), new Vector3(289.8f, 4.0f, -188.9f), new Vector3(307.1f, 4.0f, -184.4f),
                    new Vector3(325.1f, 4.0f, -184.7f), new Vector3(341.9f, 4.0f, -190.7f), new Vector3(356.2f, 4.1f, -201.4f),
                    new Vector3(366.9f, 4.1f, -215.8f), new Vector3(372.6f, 4.2f, -232.8f), new Vector3(372.2f, 4.2f, -250.7f),
                    new Vector3(367.3f, 4.3f, -268.0f), new Vector3(339.0f, 4.7f, -353.6f), new Vector3(324.9f, 4.9f, -396.4f),
                    new Vector3(317.1f, 5.0f, -412.6f), new Vector3(305.1f, 5.1f, -426.0f), new Vector3(291.0f, 5.2f, -437.1f),
                    new Vector3(275.0f, 5.3f, -445.2f), new Vector3(257.5f, 5.4f, -449.2f), new Vector3(167.4f, 5.8f, -447.6f),
                    new Vector3(86.3f, 6.0f, -447.9f), new Vector3(68.5f, 6.0f, -445.8f), new Vector3(51.5f, 6.0f, -439.8f),
                    new Vector3(36.5f, 6.0f, -429.9f), new Vector3(23.4f, 5.9f, -417.6f), new Vector3(12.9f, 5.7f, -403.0f),
                    new Vector3(4.8f, 5.5f, -386.9f), new Vector3(1.1f, 5.3f, -369.5f), new Vector3(-0.6f, 5.1f, -351.6f),
                    new Vector3(-0.6f, 3.5f, -261.4f), new Vector3(-0.4f, 1.8f, -171.3f), new Vector3(-0.2f, 0.5f, -81.1f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 6.2f, 90.1f), new Vector3(-0.6f, 17.2f, 180.2f),
                    new Vector3(-1.1f, 22.1f, 270.3f), new Vector3(-1.3f, 23.9f, 306.4f), new Vector3(1.1f, 25.5f, 324.1f),
                    new Vector3(15.8f, 27.4f, 333.5f), new Vector3(50.8f, 32.0f, 341.7f), new Vector3(134.6f, 45.8f, 374.8f),
                    new Vector3(219.2f, 56.4f, 406.0f), new Vector3(305.2f, 58.3f, 432.7f), new Vector3(383.5f, 59.9f, 453.7f),
                    new Vector3(472.3f, 62.2f, 469.0f), new Vector3(561.1f, 63.8f, 484.6f), new Vector3(578.8f, 63.9f, 487.9f),
                    new Vector3(630.6f, 63.6f, 503.3f), new Vector3(696.3f, 61.2f, 532.9f), new Vector3(774.3f, 56.7f, 578.0f),
                    new Vector3(852.2f, 52.9f, 623.2f), new Vector3(868.3f, 52.4f, 631.1f), new Vector3(884.1f, 52.1f, 627.0f),
                    new Vector3(885.1f, 52.0f, 609.3f), new Vector3(876.3f, 50.9f, 546.9f), new Vector3(854.8f, 45.9f, 459.4f),
                    new Vector3(850.0f, 44.6f, 442.0f), new Vector3(827.6f, 39.9f, 383.1f), new Vector3(789.9f, 33.6f, 301.2f),
                    new Vector3(750.7f, 30.1f, 220.1f), new Vector3(712.6f, 29.0f, 138.5f), new Vector3(685.7f, 26.4f, 71.6f),
                    new Vector3(658.9f, 22.1f, -14.4f), new Vector3(632.1f, 18.2f, -100.4f), new Vector3(622.2f, 17.6f, -115.3f),
                    new Vector3(605.7f, 17.0f, -121.6f), new Vector3(588.2f, 16.6f, -118.5f), new Vector3(574.3f, 16.3f, -107.3f),
                    new Vector3(551.5f, 16.0f, -79.4f), new Vector3(541.4f, 16.0f, -64.5f), new Vector3(525.6f, 16.4f, -32.2f),
                    new Vector3(519.2f, 16.9f, -5.9f), new Vector3(517.2f, 17.3f, 12.0f), new Vector3(515.8f, 18.1f, 38.9f),
                    new Vector3(519.4f, 19.2f, 74.8f), new Vector3(530.3f, 20.4f, 109.1f), new Vector3(537.0f, 21.0f, 125.8f),
                    new Vector3(573.7f, 24.0f, 208.1f), new Vector3(598.8f, 25.5f, 265.8f), new Vector3(601.3f, 25.7f, 283.7f),
                    new Vector3(600.2f, 25.9f, 301.7f), new Vector3(594.7f, 26.0f, 318.7f), new Vector3(585.7f, 26.0f, 334.3f),
                    new Vector3(573.2f, 26.2f, 347.1f), new Vector3(558.1f, 26.6f, 356.7f), new Vector3(541.4f, 27.1f, 363.3f),
                    new Vector3(523.7f, 27.7f, 366.3f), new Vector3(505.8f, 28.5f, 365.8f), new Vector3(418.6f, 33.5f, 343.0f),
                    new Vector3(357.7f, 37.4f, 326.8f), new Vector3(341.3f, 38.5f, 319.4f), new Vector3(327.2f, 39.5f, 308.4f),
                    new Vector3(315.6f, 40.4f, 295.0f), new Vector3(307.2f, 41.3f, 279.0f), new Vector3(304.1f, 42.1f, 261.3f),
                    new Vector3(304.9f, 42.8f, 243.4f), new Vector3(308.6f, 43.3f, 225.9f), new Vector3(316.6f, 43.7f, 209.9f),
                    new Vector3(343.5f, 44.0f, 173.7f), new Vector3(352.8f, 43.9f, 158.3f), new Vector3(359.3f, 43.9f, 141.5f),
                    new Vector3(367.1f, 43.7f, 115.7f), new Vector3(369.6f, 43.5f, 97.9f), new Vector3(371.3f, 43.3f, 79.9f),
                    new Vector3(370.2f, 43.0f, 52.9f), new Vector3(367.0f, 42.8f, 35.3f), new Vector3(345.7f, 41.6f, -52.3f),
                    new Vector3(324.5f, 40.5f, -139.8f), new Vector3(303.3f, 40.0f, -227.4f), new Vector3(282.3f, 37.7f, -315.0f),
                    new Vector3(262.7f, 32.6f, -393.7f), new Vector3(254.0f, 31.2f, -409.3f), new Vector3(241.7f, 29.9f, -422.2f),
                    new Vector3(226.5f, 28.5f, -431.6f), new Vector3(209.6f, 27.2f, -437.6f), new Vector3(191.9f, 25.8f, -440.7f),
                    new Vector3(101.8f, 20.1f, -443.3f), new Vector3(56.8f, 18.5f, -444.6f), new Vector3(39.3f, 18.1f, -442.0f),
                    new Vector3(27.2f, 18.0f, -429.3f), new Vector3(20.4f, 17.9f, -412.7f), new Vector3(13.4f, 17.0f, -386.6f),
                    new Vector3(4.2f, 13.6f, -333.3f), new Vector3(1.6f, 6.6f, -243.3f), new Vector3(1.0f, 3.9f, -153.2f),
                    new Vector3(0.4f, 1.2f, -63.1f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.1f, 90.1f), new Vector3(0.0f, 3.7f, 180.3f),
                    new Vector3(-0.2f, 6.7f, 270.4f), new Vector3(-0.8f, 8.4f, 324.5f), new Vector3(3.4f, 8.8f, 341.7f),
                    new Vector3(15.9f, 9.2f, 354.1f), new Vector3(33.5f, 9.5f, 354.2f), new Vector3(48.0f, 9.7f, 343.8f),
                    new Vector3(86.5f, 10.0f, 293.8f), new Vector3(99.6f, 10.1f, 270.2f), new Vector3(110.1f, 10.2f, 245.3f),
                    new Vector3(121.2f, 10.5f, 211.0f), new Vector3(125.7f, 10.7f, 184.3f), new Vector3(127.5f, 11.6f, 94.2f),
                    new Vector3(127.9f, 12.6f, 4.1f), new Vector3(128.2f, 13.1f, -41.0f), new Vector3(131.5f, 13.3f, -58.6f),
                    new Vector3(140.8f, 13.4f, -73.9f), new Vector3(155.2f, 13.6f, -84.4f), new Vector3(172.0f, 13.7f, -90.3f),
                    new Vector3(189.9f, 13.8f, -89.6f), new Vector3(206.2f, 13.9f, -82.3f), new Vector3(218.5f, 13.9f, -69.5f),
                    new Vector3(227.8f, 14.0f, -54.1f), new Vector3(260.2f, 13.7f, 29.9f), new Vector3(270.8f, 13.5f, 44.4f),
                    new Vector3(286.0f, 13.3f, 53.6f), new Vector3(302.7f, 13.1f, 60.2f), new Vector3(320.2f, 12.9f, 64.6f),
                    new Vector3(408.4f, 11.5f, 83.4f), new Vector3(496.4f, 10.0f, 102.6f), new Vector3(584.5f, 8.7f, 121.9f),
                    new Vector3(655.2f, 8.1f, 135.7f), new Vector3(691.2f, 8.0f, 137.0f), new Vector3(727.3f, 7.9f, 136.0f),
                    new Vector3(744.7f, 7.8f, 139.9f), new Vector3(757.2f, 7.6f, 152.6f), new Vector3(810.8f, 6.0f, 225.0f),
                    new Vector3(853.7f, 4.1f, 283.0f), new Vector3(867.5f, 3.6f, 294.4f), new Vector3(883.7f, 3.1f, 302.4f),
                    new Vector3(901.3f, 2.6f, 304.7f), new Vector3(918.9f, 2.1f, 301.1f), new Vector3(934.8f, 1.5f, 292.9f),
                    new Vector3(946.8f, 1.0f, 279.6f), new Vector3(954.8f, 0.5f, 263.5f), new Vector3(959.4f, 0.0f, 246.1f),
                    new Vector3(963.4f, -1.4f, 192.2f), new Vector3(963.6f, -1.9f, 174.2f), new Vector3(950.9f, -3.5f, 85.0f),
                    new Vector3(941.5f, -4.0f, 22.6f), new Vector3(933.6f, -4.0f, 6.7f), new Vector3(917.8f, -4.0f, 0.1f),
                    new Vector3(899.9f, -4.1f, 2.2f), new Vector3(885.7f, -4.1f, -8.1f), new Vector3(831.8f, -4.6f, -80.3f),
                    new Vector3(810.3f, -4.9f, -109.3f), new Vector3(801.5f, -5.0f, -125.0f), new Vector3(797.5f, -5.2f, -142.5f),
                    new Vector3(801.5f, -5.4f, -159.7f), new Vector3(837.2f, -6.1f, -222.4f), new Vector3(843.9f, -6.2f, -239.0f),
                    new Vector3(845.3f, -6.4f, -256.9f), new Vector3(840.0f, -6.6f, -273.8f), new Vector3(829.0f, -6.7f, -288.1f),
                    new Vector3(814.3f, -6.9f, -298.3f), new Vector3(736.6f, -7.6f, -344.0f), new Vector3(713.3f, -7.7f, -357.8f),
                    new Vector3(698.8f, -7.8f, -368.4f), new Vector3(685.7f, -7.9f, -380.7f), new Vector3(675.5f, -8.0f, -395.6f),
                    new Vector3(668.1f, -8.0f, -412.0f), new Vector3(638.3f, -7.7f, -497.0f), new Vector3(632.4f, -7.6f, -514.0f),
                    new Vector3(623.7f, -7.4f, -529.8f), new Vector3(610.7f, -7.2f, -542.3f), new Vector3(595.3f, -6.9f, -551.5f),
                    new Vector3(578.1f, -6.7f, -556.4f), new Vector3(560.1f, -6.4f, -556.2f), new Vector3(470.1f, -4.8f, -551.4f),
                    new Vector3(380.0f, -3.0f, -547.1f), new Vector3(289.9f, -1.4f, -543.7f), new Vector3(208.9f, -0.4f, -539.7f),
                    new Vector3(194.2f, -0.3f, -530.2f), new Vector3(190.5f, -0.1f, -513.0f), new Vector3(205.7f, 0.1f, -424.2f),
                    new Vector3(208.4f, 0.2f, -406.4f), new Vector3(209.5f, 0.3f, -388.4f), new Vector3(210.0f, 0.8f, -334.3f),
                    new Vector3(206.6f, 1.1f, -316.7f), new Vector3(197.8f, 1.3f, -301.2f), new Vector3(182.9f, 1.5f, -291.5f),
                    new Vector3(165.1f, 1.8f, -290.6f), new Vector3(148.2f, 2.1f, -296.1f), new Vector3(134.9f, 2.4f, -308.1f),
                    new Vector3(128.2f, 2.7f, -324.7f), new Vector3(127.5f, 4.2f, -414.8f), new Vector3(125.7f, 5.2f, -477.8f),
                    new Vector3(118.1f, 5.5f, -494.0f), new Vector3(106.4f, 5.7f, -507.6f), new Vector3(91.5f, 6.0f, -517.6f),
                    new Vector3(74.2f, 6.2f, -522.5f), new Vector3(56.3f, 6.4f, -522.9f), new Vector3(39.3f, 6.6f, -516.9f),
                    new Vector3(24.2f, 6.7f, -507.2f), new Vector3(12.6f, 6.8f, -493.6f), new Vector3(5.1f, 6.9f, -477.4f),
                    new Vector3(2.1f, 7.0f, -459.7f), new Vector3(1.4f, 6.6f, -369.6f), new Vector3(1.0f, 4.9f, -279.4f),
                    new Vector3(0.7f, 2.8f, -189.3f), new Vector3(0.4f, 0.9f, -99.2f), new Vector3(0.0f, 0.0f, -9.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.0f, 90.1f), new Vector3(0.0f, 3.3f, 180.1f),
                    new Vector3(0.0f, 5.3f, 270.2f), new Vector3(0.9f, 6.1f, 360.3f), new Vector3(1.3f, 6.3f, 387.3f),
                    new Vector3(6.3f, 6.5f, 404.4f), new Vector3(19.3f, 6.8f, 416.6f), new Vector3(35.9f, 7.1f, 422.9f),
                    new Vector3(53.5f, 7.5f, 421.0f), new Vector3(68.8f, 7.9f, 411.8f), new Vector3(78.7f, 8.4f, 397.0f),
                    new Vector3(81.1f, 8.9f, 379.6f), new Vector3(81.0f, 11.4f, 289.5f), new Vector3(81.1f, 11.8f, 271.5f),
                    new Vector3(86.5f, 12.9f, 226.8f), new Vector3(96.5f, 13.5f, 192.2f), new Vector3(111.9f, 13.9f, 159.7f),
                    new Vector3(116.2f, 14.0f, 142.3f), new Vector3(112.1f, 14.0f, 125.1f), new Vector3(103.5f, 14.0f, 109.3f),
                    new Vector3(91.1f, 13.9f, 96.3f), new Vector3(52.1f, 13.6f, 58.9f), new Vector3(42.1f, 13.5f, 44.1f),
                    new Vector3(43.5f, 13.3f, 26.6f), new Vector3(51.8f, 13.2f, 11.0f), new Vector3(66.6f, 13.0f, 1.4f),
                    new Vector3(84.2f, 12.8f, 0.3f), new Vector3(100.7f, 12.6f, 7.6f), new Vector3(169.5f, 11.5f, 65.7f),
                    new Vector3(184.1f, 11.3f, 76.2f), new Vector3(214.6f, 10.9f, 95.4f), new Vector3(247.5f, 10.6f, 110.0f),
                    new Vector3(281.8f, 10.3f, 120.8f), new Vector3(308.2f, 10.2f, 126.6f), new Vector3(335.0f, 10.1f, 129.6f),
                    new Vector3(370.3f, 10.0f, 136.7f), new Vector3(396.1f, 9.9f, 144.8f), new Vector3(411.3f, 9.8f, 154.3f),
                    new Vector3(440.3f, 9.4f, 175.7f), new Vector3(464.1f, 8.9f, 202.7f), new Vector3(489.9f, 8.1f, 239.6f),
                    new Vector3(501.2f, 7.7f, 253.6f), new Vector3(513.3f, 7.3f, 266.9f), new Vector3(534.3f, 6.7f, 283.8f),
                    new Vector3(566.1f, 5.8f, 300.7f), new Vector3(651.0f, 3.8f, 330.7f), new Vector3(685.1f, 3.2f, 342.4f),
                    new Vector3(711.8f, 2.7f, 346.4f), new Vector3(729.7f, 2.5f, 345.2f), new Vector3(747.1f, 2.3f, 340.8f),
                    new Vector3(763.1f, 2.1f, 332.9f), new Vector3(785.2f, 2.0f, 317.3f), new Vector3(796.3f, 2.0f, 303.1f),
                    new Vector3(804.9f, 2.0f, 287.4f), new Vector3(810.9f, 1.9f, 270.5f), new Vector3(814.2f, 1.8f, 252.8f),
                    new Vector3(814.2f, 1.6f, 234.9f), new Vector3(811.8f, 1.5f, 217.1f), new Vector3(792.6f, 0.4f, 138.3f),
                    new Vector3(787.2f, -0.2f, 102.7f), new Vector3(783.5f, -1.8f, 12.7f), new Vector3(773.0f, -2.1f, -0.6f),
                    new Vector3(758.1f, -2.4f, -10.6f), new Vector3(742.0f, -2.7f, -18.5f), new Vector3(725.0f, -2.9f, -24.6f),
                    new Vector3(698.6f, -3.3f, -30.6f), new Vector3(671.7f, -3.6f, -32.3f), new Vector3(635.7f, -3.9f, -30.5f),
                    new Vector3(600.6f, -4.0f, -22.4f), new Vector3(576.2f, -4.0f, -10.8f), new Vector3(567.2f, -3.9f, 4.3f),
                    new Vector3(567.6f, -3.8f, 21.9f), new Vector3(574.0f, -3.7f, 38.4f), new Vector3(585.2f, -3.5f, 52.4f),
                    new Vector3(598.5f, -3.3f, 64.5f), new Vector3(636.6f, -2.8f, 88.4f), new Vector3(665.6f, -2.2f, 109.9f),
                    new Vector3(691.9f, -1.6f, 134.4f), new Vector3(702.6f, -1.3f, 148.8f), new Vector3(709.0f, -1.0f, 165.0f),
                    new Vector3(707.5f, -0.7f, 182.7f), new Vector3(699.6f, -0.3f, 198.6f), new Vector3(686.5f, 0.0f, 210.4f),
                    new Vector3(669.9f, 0.3f, 217.3f), new Vector3(652.0f, 0.5f, 216.7f), new Vector3(574.1f, 1.6f, 194.6f),
                    new Vector3(507.6f, 2.0f, 166.8f), new Vector3(436.8f, 2.4f, 127.4f), new Vector3(422.0f, 2.6f, 117.2f),
                    new Vector3(350.8f, 4.3f, 62.0f), new Vector3(304.0f, 5.7f, 19.7f), new Vector3(266.2f, 6.9f, -18.8f),
                    new Vector3(238.9f, 7.9f, -54.6f), new Vector3(224.7f, 8.3f, -64.5f), new Vector3(208.8f, 8.6f, -58.1f),
                    new Vector3(194.8f, 8.9f, -46.9f), new Vector3(178.1f, 9.2f, -41.7f), new Vector3(161.0f, 9.5f, -46.6f),
                    new Vector3(148.4f, 9.7f, -59.2f), new Vector3(143.1f, 9.8f, -76.1f), new Vector3(146.8f, 9.9f, -93.4f),
                    new Vector3(194.5f, 9.8f, -169.7f), new Vector3(242.5f, 8.8f, -246.0f), new Vector3(276.1f, 8.0f, -299.3f),
                    new Vector3(283.5f, 7.7f, -315.6f), new Vector3(284.9f, 7.5f, -333.5f), new Vector3(281.9f, 7.2f, -351.2f),
                    new Vector3(272.7f, 7.0f, -366.5f), new Vector3(259.0f, 6.8f, -378.0f), new Vector3(242.9f, 6.6f, -385.8f),
                    new Vector3(176.4f, 6.1f, -413.5f), new Vector3(158.9f, 6.0f, -417.8f), new Vector3(141.0f, 6.0f, -419.8f),
                    new Vector3(123.1f, 5.9f, -420.3f), new Vector3(105.3f, 5.8f, -418.1f), new Vector3(88.0f, 5.6f, -413.2f),
                    new Vector3(71.8f, 5.3f, -405.6f), new Vector3(56.5f, 5.0f, -396.1f), new Vector3(42.1f, 4.7f, -385.3f),
                    new Vector3(24.8f, 4.2f, -364.6f), new Vector3(16.0f, 3.8f, -348.9f), new Vector3(4.0f, 3.1f, -314.9f),
                    new Vector3(0.2f, 2.6f, -288.2f), new Vector3(0.0f, 2.0f, -198.1f), new Vector3(-0.1f, 1.0f, -108.1f),
                    new Vector3(-0.2f, 0.0f, -18.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.5f, 90.0f), new Vector3(0.0f, 1.7f, 180.0f),
                    new Vector3(-2.9f, 2.0f, 197.5f), new Vector3(-17.4f, 2.3f, 205.0f), new Vector3(-43.7f, 2.8f, 210.8f),
                    new Vector3(-52.2f, 3.1f, 224.5f), new Vector3(-51.8f, 4.2f, 278.5f), new Vector3(-48.9f, 4.6f, 296.2f),
                    new Vector3(-44.7f, 5.0f, 313.7f), new Vector3(-25.5f, 5.7f, 344.1f), new Vector3(-12.7f, 6.1f, 356.3f),
                    new Vector3(9.2f, 6.6f, 372.0f), new Vector3(92.7f, 8.0f, 405.7f), new Vector3(176.2f, 8.9f, 439.4f),
                    new Vector3(259.6f, 9.1f, 473.0f), new Vector3(342.6f, 9.6f, 507.8f), new Vector3(392.4f, 10.1f, 528.7f),
                    new Vector3(479.4f, 11.2f, 552.0f), new Vector3(506.1f, 11.5f, 555.6f), new Vector3(559.9f, 12.3f, 560.2f),
                    new Vector3(622.8f, 13.2f, 556.5f), new Vector3(640.5f, 13.4f, 553.3f), new Vector3(675.6f, 13.9f, 545.3f),
                    new Vector3(727.1f, 14.5f, 529.2f), new Vector3(773.6f, 15.1f, 501.8f), new Vector3(800.8f, 15.4f, 478.2f),
                    new Vector3(815.2f, 15.6f, 468.7f), new Vector3(828.2f, 15.7f, 479.5f), new Vector3(844.2f, 15.8f, 481.9f),
                    new Vector3(857.9f, 15.9f, 470.5f), new Vector3(874.7f, 15.9f, 465.4f), new Vector3(892.7f, 16.0f, 464.4f),
                    new Vector3(964.0f, 16.0f, 473.7f), new Vector3(981.0f, 15.9f, 479.4f), new Vector3(1013.8f, 15.9f, 494.3f),
                    new Vector3(1031.1f, 15.8f, 498.4f), new Vector3(1049.0f, 15.7f, 500.4f), new Vector3(1120.9f, 15.4f, 505.2f),
                    new Vector3(1147.4f, 15.3f, 509.9f), new Vector3(1163.1f, 15.2f, 518.4f), new Vector3(1178.4f, 15.1f, 516.5f),
                    new Vector3(1195.3f, 14.9f, 484.7f), new Vector3(1208.8f, 14.8f, 472.9f), new Vector3(1226.2f, 14.7f, 469.0f),
                    new Vector3(1244.0f, 14.5f, 471.2f), new Vector3(1261.3f, 14.4f, 476.1f), new Vector3(1303.9f, 14.1f, 490.4f),
                    new Vector3(1321.6f, 14.0f, 492.3f), new Vector3(1410.5f, 13.4f, 478.3f), new Vector3(1437.0f, 13.2f, 473.4f),
                    new Vector3(1471.9f, 13.0f, 464.6f), new Vector3(1484.8f, 12.9f, 452.2f), new Vector3(1494.2f, 12.8f, 437.0f),
                    new Vector3(1520.1f, 12.5f, 389.7f), new Vector3(1532.8f, 12.4f, 377.4f), new Vector3(1548.1f, 12.3f, 368.6f),
                    new Vector3(1574.8f, 12.2f, 365.0f), new Vector3(1592.7f, 12.2f, 366.3f), new Vector3(1680.6f, 12.0f, 385.8f),
                    new Vector3(1698.5f, 12.0f, 385.8f), new Vector3(1716.4f, 12.0f, 384.3f), new Vector3(1742.5f, 11.9f, 377.5f),
                    new Vector3(1764.6f, 11.8f, 362.0f), new Vector3(1776.7f, 11.8f, 348.8f), new Vector3(1787.6f, 11.7f, 334.6f),
                    new Vector3(1795.3f, 11.5f, 308.8f), new Vector3(1796.2f, 11.3f, 290.9f), new Vector3(1795.8f, 11.2f, 272.9f),
                    new Vector3(1787.6f, 10.9f, 238.0f), new Vector3(1765.9f, 10.5f, 209.2f), new Vector3(1730.9f, 9.9f, 181.0f),
                    new Vector3(1697.2f, 9.5f, 168.3f), new Vector3(1679.4f, 9.3f, 167.8f), new Vector3(1661.4f, 9.0f, 168.4f),
                    new Vector3(1643.8f, 8.8f, 171.5f), new Vector3(1626.3f, 8.5f, 175.8f), new Vector3(1610.2f, 8.3f, 183.5f),
                    new Vector3(1586.8f, 7.9f, 197.1f), new Vector3(1539.1f, 7.0f, 238.1f), new Vector3(1479.7f, 5.9f, 305.7f),
                    new Vector3(1432.0f, 5.1f, 359.7f), new Vector3(1366.2f, 4.4f, 421.1f), new Vector3(1350.2f, 4.3f, 428.5f),
                    new Vector3(1332.4f, 4.2f, 427.6f), new Vector3(1310.7f, 4.1f, 411.5f), new Vector3(1247.0f, 4.0f, 347.9f),
                    new Vector3(1219.0f, 4.1f, 325.3f), new Vector3(1202.7f, 4.1f, 317.9f), new Vector3(1185.5f, 4.2f, 313.3f),
                    new Vector3(1167.6f, 4.2f, 312.1f), new Vector3(1079.0f, 4.6f, 327.7f), new Vector3(1061.2f, 4.7f, 330.8f),
                    new Vector3(1043.2f, 4.8f, 330.9f), new Vector3(1025.7f, 5.0f, 327.4f), new Vector3(1008.5f, 5.1f, 322.1f),
                    new Vector3(983.7f, 5.2f, 311.4f), new Vector3(960.9f, 5.4f, 297.0f), new Vector3(942.9f, 5.6f, 277.0f),
                    new Vector3(933.7f, 5.7f, 261.6f), new Vector3(925.6f, 5.8f, 245.6f), new Vector3(910.0f, 6.5f, 156.9f),
                    new Vector3(902.2f, 6.8f, 112.6f), new Vector3(908.9f, 6.9f, 86.5f), new Vector3(917.7f, 7.1f, 61.0f),
                    new Vector3(913.5f, 7.2f, 45.5f), new Vector3(898.0f, 7.3f, 36.6f), new Vector3(840.1f, 7.6f, 11.7f),
                    new Vector3(755.3f, 7.9f, -18.3f), new Vector3(737.8f, 7.9f, -22.8f), new Vector3(721.4f, 8.0f, -30.0f),
                    new Vector3(708.4f, 8.0f, -42.1f), new Vector3(698.5f, 8.0f, -56.8f), new Vector3(693.4f, 8.0f, -73.9f),
                    new Vector3(689.3f, 7.9f, -154.8f), new Vector3(682.0f, 7.9f, -190.0f), new Vector3(664.5f, 7.8f, -221.5f),
                    new Vector3(651.1f, 7.7f, -233.0f), new Vector3(636.4f, 7.7f, -243.4f), new Vector3(619.8f, 7.6f, -250.1f),
                    new Vector3(602.4f, 7.5f, -254.0f), new Vector3(512.4f, 7.2f, -254.2f), new Vector3(422.4f, 6.9f, -254.4f),
                    new Vector3(332.4f, 6.5f, -253.8f), new Vector3(314.6f, 6.5f, -255.4f), new Vector3(305.5f, 6.4f, -268.3f),
                    new Vector3(310.8f, 6.4f, -284.7f), new Vector3(317.7f, 6.3f, -301.1f), new Vector3(318.3f, 6.2f, -319.0f),
                    new Vector3(313.1f, 6.1f, -408.6f), new Vector3(297.0f, 6.0f, -415.8f), new Vector3(207.5f, 6.0f, -424.5f),
                    new Vector3(117.6f, 5.5f, -427.6f), new Vector3(45.6f, 4.7f, -427.2f), new Vector3(18.7f, 4.4f, -424.4f),
                    new Vector3(6.4f, 4.2f, -412.1f), new Vector3(0.7f, 4.0f, -396.0f), new Vector3(-0.1f, 2.7f, -306.0f),
                    new Vector3(0.0f, 1.6f, -216.0f), new Vector3(0.0f, 0.6f, -126.0f), new Vector3(0.0f, 0.1f, -36.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 90.1f), new Vector3(0.0f, 0.0f, 162.2f),
                    new Vector3(-6.5f, 0.0f, 178.6f), new Vector3(-21.6f, 0.0f, 187.5f), new Vector3(-39.4f, 0.0f, 189.7f),
                    new Vector3(-120.4f, 0.0f, 192.1f), new Vector3(-210.3f, 0.0f, 185.6f), new Vector3(-300.4f, 0.0f, 183.7f),
                    new Vector3(-327.5f, 0.0f, 183.5f), new Vector3(-342.6f, 0.0f, 176.0f), new Vector3(-346.0f, 0.0f, 158.9f),
                    new Vector3(-348.2f, 0.0f, 68.8f), new Vector3(-350.8f, 0.0f, -21.3f), new Vector3(-354.0f, 0.0f, -111.4f),
                    new Vector3(-355.7f, 0.0f, -201.5f), new Vector3(-354.1f, 0.0f, -291.6f), new Vector3(-352.5f, 0.0f, -381.7f),
                    new Vector3(-350.7f, 0.0f, -471.8f), new Vector3(-348.5f, 0.0f, -561.9f), new Vector3(-346.3f, 0.0f, -652.0f),
                    new Vector3(-343.7f, 0.0f, -669.6f), new Vector3(-334.5f, 0.0f, -684.1f), new Vector3(-317.7f, 0.0f, -690.2f),
                    new Vector3(-227.6f, 0.0f, -693.5f), new Vector3(-137.6f, 0.0f, -696.9f), new Vector3(-120.4f, 0.0f, -701.6f),
                    new Vector3(-112.4f, 0.0f, -717.3f), new Vector3(-109.2f, 0.0f, -807.4f), new Vector3(-102.8f, 0.0f, -897.3f),
                    new Vector3(-89.7f, 0.0f, -986.4f), new Vector3(-83.7f, 0.0f, -1022.0f), new Vector3(-75.4f, 0.0f, -1036.5f),
                    new Vector3(-57.6f, 0.0f, -1038.0f), new Vector3(-30.6f, 0.0f, -1037.8f), new Vector3(-19.5f, 0.0f, -1050.4f),
                    new Vector3(5.9f, 0.0f, -1136.8f), new Vector3(28.5f, 0.0f, -1224.0f), new Vector3(51.1f, 0.0f, -1311.3f),
                    new Vector3(60.3f, 0.0f, -1346.2f), new Vector3(74.9f, 0.0f, -1388.8f), new Vector3(78.6f, 0.0f, -1406.4f),
                    new Vector3(69.5f, 0.0f, -1421.0f), new Vector3(52.3f, 0.0f, -1425.6f), new Vector3(-37.6f, 0.0f, -1418.6f),
                    new Vector3(-109.4f, 0.0f, -1413.1f), new Vector3(-124.5f, 0.0f, -1420.8f), new Vector3(-132.6f, 0.0f, -1436.7f),
                    new Vector3(-147.3f, 0.0f, -1447.1f), new Vector3(-159.0f, 0.0f, -1460.6f), new Vector3(-165.2f, 0.0f, -1477.5f),
                    new Vector3(-175.7f, 0.0f, -1491.6f), new Vector3(-193.0f, 0.0f, -1494.4f), new Vector3(-228.8f, 0.0f, -1490.8f),
                    new Vector3(-244.6f, 0.0f, -1498.8f), new Vector3(-250.1f, 0.0f, -1515.6f), new Vector3(-255.8f, 0.0f, -1596.6f),
                    new Vector3(-251.8f, 0.0f, -1686.6f), new Vector3(-247.8f, 0.0f, -1758.5f), new Vector3(-235.6f, 0.0f, -1847.9f),
                    new Vector3(-232.1f, 0.0f, -1865.5f), new Vector3(-226.6f, 0.0f, -1882.6f), new Vector3(-217.6f, 0.0f, -1898.2f),
                    new Vector3(-200.4f, 0.0f, -1919.0f), new Vector3(-187.2f, 0.0f, -1931.2f), new Vector3(-117.8f, 0.0f, -1988.7f),
                    new Vector3(-76.1f, 0.0f, -2023.1f), new Vector3(-60.9f, 0.0f, -2032.8f), new Vector3(-44.3f, 0.0f, -2039.6f),
                    new Vector3(40.8f, 0.0f, -2069.3f), new Vector3(125.9f, 0.0f, -2098.9f), new Vector3(143.1f, 0.0f, -2104.4f),
                    new Vector3(160.8f, 0.0f, -2103.5f), new Vector3(176.7f, 0.0f, -2095.8f), new Vector3(246.7f, 0.0f, -2038.9f),
                    new Vector3(316.6f, 0.0f, -1982.0f), new Vector3(370.4f, 0.0f, -1934.1f), new Vector3(395.5f, 0.0f, -1908.2f),
                    new Vector3(405.5f, 0.0f, -1893.2f), new Vector3(409.9f, 0.0f, -1875.8f), new Vector3(406.8f, 0.0f, -1858.2f),
                    new Vector3(398.1f, 0.0f, -1842.6f), new Vector3(342.1f, 0.0f, -1771.9f), new Vector3(314.1f, 0.0f, -1736.6f),
                    new Vector3(304.8f, 0.0f, -1721.2f), new Vector3(297.2f, 0.0f, -1704.9f), new Vector3(267.7f, 0.0f, -1619.8f),
                    new Vector3(238.1f, 0.0f, -1534.6f), new Vector3(229.2f, 0.0f, -1509.1f), new Vector3(220.3f, 0.0f, -1493.5f),
                    new Vector3(208.5f, 0.0f, -1480.0f), new Vector3(194.1f, 0.0f, -1469.4f), new Vector3(117.6f, 0.0f, -1421.8f),
                    new Vector3(104.4f, 0.0f, -1409.6f), new Vector3(94.6f, 0.0f, -1394.5f), new Vector3(88.4f, 0.0f, -1377.6f),
                    new Vector3(65.5f, 0.0f, -1290.4f), new Vector3(42.8f, 0.0f, -1203.2f), new Vector3(20.0f, 0.0f, -1116.0f),
                    new Vector3(13.4f, 0.0f, -1089.8f), new Vector3(7.2f, 0.0f, -1054.3f), new Vector3(2.4f, 0.0f, -982.3f),
                    new Vector3(3.0f, 0.0f, -892.2f), new Vector3(1.2f, 0.0f, -802.1f), new Vector3(0.0f, 0.0f, -712.0f),
                    new Vector3(0.0f, 0.0f, -621.9f), new Vector3(0.1f, 0.0f, -531.7f), new Vector3(-1.6f, 0.0f, -441.6f),
                    new Vector3(-1.9f, 0.0f, -351.5f), new Vector3(-1.7f, 0.0f, -261.4f), new Vector3(-1.5f, 0.0f, -171.2f),
                    new Vector3(-0.7f, 0.0f, -81.1f)
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
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 7.5f, 90.1f), new Vector3(0.0f, 21.7f, 180.3f),
                    new Vector3(-3.1f, 29.8f, 261.3f), new Vector3(-8.8f, 29.8f, 306.0f), new Vector3(-13.0f, 29.6f, 323.5f),
                    new Vector3(-27.1f, 29.2f, 332.3f), new Vector3(-41.4f, 28.8f, 323.2f), new Vector3(-89.6f, 25.8f, 247.0f),
                    new Vector3(-123.7f, 23.3f, 193.9f), new Vector3(-135.2f, 22.7f, 180.0f), new Vector3(-149.1f, 22.1f, 168.6f),
                    new Vector3(-164.4f, 21.5f, 159.1f), new Vector3(-180.9f, 21.1f, 152.0f), new Vector3(-198.4f, 20.6f, 147.8f),
                    new Vector3(-216.3f, 20.3f, 145.8f), new Vector3(-234.3f, 20.1f, 146.5f), new Vector3(-251.7f, 20.0f, 151.0f),
                    new Vector3(-336.6f, 19.0f, 181.3f), new Vector3(-421.6f, 16.5f, 211.3f), new Vector3(-439.6f, 15.9f, 212.5f),
                    new Vector3(-457.6f, 15.3f, 211.6f), new Vector3(-483.5f, 14.4f, 204.0f), new Vector3(-509.2f, 13.5f, 195.7f),
                    new Vector3(-527.2f, 12.9f, 195.1f), new Vector3(-544.6f, 12.4f, 198.8f), new Vector3(-560.8f, 11.9f, 206.6f),
                    new Vector3(-576.5f, 11.4f, 215.6f), new Vector3(-592.8f, 11.0f, 223.2f), new Vector3(-610.3f, 10.6f, 227.1f),
                    new Vector3(-628.1f, 10.4f, 225.2f), new Vector3(-659.7f, 10.0f, 208.0f), new Vector3(-675.4f, 10.0f, 199.0f),
                    new Vector3(-692.0f, 10.0f, 192.3f), new Vector3(-709.7f, 9.9f, 188.8f), new Vector3(-727.7f, 9.8f, 188.3f),
                    new Vector3(-745.5f, 9.7f, 190.7f), new Vector3(-763.0f, 9.5f, 195.1f), new Vector3(-779.8f, 9.4f, 201.5f),
                    new Vector3(-795.7f, 9.2f, 209.9f), new Vector3(-809.6f, 8.9f, 221.4f), new Vector3(-820.6f, 8.7f, 235.6f),
                    new Vector3(-827.0f, 8.4f, 252.4f), new Vector3(-848.6f, 7.0f, 340.0f), new Vector3(-853.3f, 6.7f, 357.4f),
                    new Vector3(-865.1f, 6.4f, 370.2f), new Vector3(-881.9f, 6.2f, 376.6f), new Vector3(-899.3f, 5.9f, 381.3f),
                    new Vector3(-916.9f, 5.6f, 385.1f), new Vector3(-934.8f, 5.3f, 387.0f), new Vector3(-961.9f, 5.0f, 386.6f),
                    new Vector3(-979.9f, 4.8f, 386.9f), new Vector3(-997.5f, 4.6f, 390.5f), new Vector3(-1012.8f, 4.4f, 399.7f),
                    new Vector3(-1024.4f, 4.3f, 413.4f), new Vector3(-1030.6f, 4.2f, 430.3f), new Vector3(-1031.2f, 4.1f, 448.1f),
                    new Vector3(-1027.2f, 4.0f, 465.6f), new Vector3(-1021.3f, 4.0f, 482.6f), new Vector3(-1025.5f, 4.0f, 499.3f),
                    new Vector3(-1038.9f, 4.1f, 511.2f), new Vector3(-1109.0f, 5.1f, 567.9f), new Vector3(-1165.2f, 6.4f, 613.1f),
                    new Vector3(-1181.1f, 6.7f, 621.5f), new Vector3(-1199.0f, 7.1f, 622.6f), new Vector3(-1289.2f, 9.1f, 624.0f),
                    new Vector3(-1379.3f, 10.8f, 625.5f), new Vector3(-1469.4f, 11.8f, 627.1f), new Vector3(-1532.5f, 12.0f, 626.9f),
                    new Vector3(-1550.3f, 12.1f, 624.4f), new Vector3(-1567.7f, 12.2f, 620.2f), new Vector3(-1573.6f, 12.4f, 604.9f),
                    new Vector3(-1564.1f, 12.5f, 590.3f), new Vector3(-1489.3f, 14.0f, 540.0f), new Vector3(-1414.3f, 15.8f, 490.0f),
                    new Vector3(-1340.9f, 17.7f, 437.8f), new Vector3(-1268.4f, 19.3f, 384.2f), new Vector3(-1197.1f, 20.0f, 329.0f),
                    new Vector3(-1127.6f, 19.7f, 271.6f), new Vector3(-1058.7f, 18.7f, 213.6f), new Vector3(-991.4f, 17.3f, 153.5f),
                    new Vector3(-925.3f, 15.8f, 92.3f), new Vector3(-860.8f, 14.6f, 29.3f), new Vector3(-797.2f, 14.0f, -34.6f),
                    new Vector3(-733.6f, 13.5f, -98.5f), new Vector3(-675.1f, 11.8f, -154.6f), new Vector3(-659.8f, 11.4f, -155.3f),
                    new Vector3(-655.2f, 10.9f, -138.1f), new Vector3(-652.2f, 10.4f, -120.3f), new Vector3(-628.4f, 7.5f, -33.4f),
                    new Vector3(-620.7f, 6.6f, -7.5f), new Vector3(-605.5f, 5.3f, 35.0f), new Vector3(-593.1f, 4.8f, 47.2f),
                    new Vector3(-575.8f, 4.3f, 47.0f), new Vector3(-562.1f, 3.8f, 35.5f), new Vector3(-531.6f, 2.7f, -9.2f),
                    new Vector3(-531.6f, 2.4f, -26.9f), new Vector3(-539.4f, 2.2f, -43.0f), new Vector3(-557.6f, 2.0f, -74.1f),
                    new Vector3(-561.8f, 2.0f, -91.6f), new Vector3(-567.3f, 2.2f, -136.3f), new Vector3(-562.8f, 2.3f, -153.4f),
                    new Vector3(-542.9f, 2.6f, -183.4f), new Vector3(-527.1f, 2.8f, -188.9f), new Vector3(-516.1f, 3.0f, -175.7f),
                    new Vector3(-483.3f, 4.3f, -91.7f), new Vector3(-448.7f, 5.7f, -8.5f), new Vector3(-437.4f, 6.0f, 5.4f),
                    new Vector3(-422.4f, 6.3f, 15.3f), new Vector3(-406.1f, 6.6f, 22.8f), new Vector3(-380.3f, 6.9f, 31.0f),
                    new Vector3(-362.8f, 7.1f, 35.4f), new Vector3(-345.0f, 7.3f, 34.3f), new Vector3(-328.3f, 7.5f, 27.9f),
                    new Vector3(-272.0f, 7.9f, -0.6f), new Vector3(-258.3f, 8.0f, -12.3f), new Vector3(-247.7f, 8.0f, -26.8f),
                    new Vector3(-233.8f, 8.0f, -49.9f), new Vector3(-222.1f, 8.1f, -84.0f), new Vector3(-219.1f, 8.1f, -101.8f),
                    new Vector3(-217.8f, 8.2f, -119.7f), new Vector3(-223.2f, 8.2f, -136.7f), new Vector3(-248.0f, 8.7f, -223.4f),
                    new Vector3(-269.1f, 9.1f, -311.0f), new Vector3(-270.2f, 9.2f, -328.7f), new Vector3(-261.9f, 9.3f, -344.5f),
                    new Vector3(-248.0f, 9.4f, -355.8f), new Vector3(-225.0f, 9.6f, -370.0f), new Vector3(-145.3f, 9.9f, -412.0f),
                    new Vector3(-65.5f, 10.0f, -454.0f), new Vector3(-33.6f, 9.6f, -470.8f), new Vector3(-16.4f, 9.3f, -475.3f),
                    new Vector3(-0.9f, 8.9f, -468.3f), new Vector3(2.2f, 8.5f, -450.7f), new Vector3(2.7f, 5.7f, -360.6f),
                    new Vector3(2.5f, 3.4f, -270.4f), new Vector3(1.7f, 2.7f, -180.3f), new Vector3(0.8f, 1.1f, -90.1f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 90.1f), new Vector3(-0.7f, 0.0f, 144.2f),
                    new Vector3(-3.8f, 0.0f, 161.9f), new Vector3(-10.3f, 0.0f, 178.7f), new Vector3(-19.5f, 0.0f, 194.1f),
                    new Vector3(-30.4f, 0.0f, 208.3f), new Vector3(-43.7f, 0.0f, 220.5f), new Vector3(-67.4f, 0.0f, 233.3f),
                    new Vector3(-84.1f, 0.0f, 240.2f), new Vector3(-101.5f, 0.0f, 244.7f), new Vector3(-119.3f, 0.0f, 247.3f),
                    new Vector3(-136.4f, 0.0f, 244.1f), new Vector3(-144.2f, 0.0f, 228.7f), new Vector3(-142.5f, 0.0f, 174.7f),
                    new Vector3(-144.6f, 0.0f, 156.9f), new Vector3(-149.6f, 0.0f, 139.6f), new Vector3(-149.2f, 0.0f, 122.2f),
                    new Vector3(-137.0f, 0.0f, 109.5f), new Vector3(-120.7f, 0.0f, 101.8f), new Vector3(-109.5f, 0.0f, 88.6f),
                    new Vector3(-114.9f, 0.0f, 72.4f), new Vector3(-131.7f, 0.0f, 66.1f), new Vector3(-219.2f, 0.0f, 44.8f),
                    new Vector3(-271.9f, 0.0f, 33.1f), new Vector3(-282.3f, 0.0f, 19.6f), new Vector3(-282.4f, 0.0f, -70.5f),
                    new Vector3(-282.1f, 0.0f, -160.6f), new Vector3(-281.8f, 0.0f, -250.7f), new Vector3(-282.0f, 0.0f, -322.8f),
                    new Vector3(-285.2f, 0.0f, -358.7f), new Vector3(-291.5f, 0.0f, -384.9f), new Vector3(-297.0f, 0.0f, -402.1f),
                    new Vector3(-327.6f, 0.0f, -486.8f), new Vector3(-341.0f, 0.0f, -520.3f), new Vector3(-353.2f, 0.0f, -533.4f),
                    new Vector3(-368.6f, 0.0f, -542.6f), new Vector3(-409.0f, 0.0f, -562.6f), new Vector3(-422.4f, 0.0f, -574.3f),
                    new Vector3(-429.2f, 0.0f, -590.6f), new Vector3(-430.5f, 0.0f, -608.5f), new Vector3(-430.9f, 0.0f, -698.6f),
                    new Vector3(-431.1f, 0.0f, -734.7f), new Vector3(-435.8f, 0.0f, -752.1f), new Vector3(-443.7f, 0.0f, -768.2f),
                    new Vector3(-455.2f, 0.0f, -781.9f), new Vector3(-469.4f, 0.0f, -792.9f), new Vector3(-492.8f, 0.0f, -806.5f),
                    new Vector3(-507.0f, 0.0f, -817.4f), new Vector3(-522.5f, 0.0f, -839.5f), new Vector3(-530.6f, 0.0f, -855.6f),
                    new Vector3(-548.7f, 0.0f, -896.8f), new Vector3(-560.8f, 0.0f, -909.8f), new Vector3(-577.9f, 0.0f, -914.0f),
                    new Vector3(-668.0f, 0.0f, -915.8f), new Vector3(-758.1f, 0.0f, -917.6f), new Vector3(-848.2f, 0.0f, -919.4f),
                    new Vector3(-866.2f, 0.0f, -919.7f), new Vector3(-883.4f, 0.0f, -924.3f), new Vector3(-891.3f, 0.0f, -940.2f),
                    new Vector3(-888.9f, 0.0f, -957.2f), new Vector3(-876.9f, 0.0f, -970.6f), new Vector3(-812.7f, 0.0f, -1033.7f),
                    new Vector3(-797.1f, 0.0f, -1041.2f), new Vector3(-786.2f, 0.0f, -1028.4f), new Vector3(-768.1f, 0.0f, -987.3f),
                    new Vector3(-752.4f, 0.0f, -980.0f), new Vector3(-735.3f, 0.0f, -984.7f), new Vector3(-653.0f, 0.0f, -1021.3f),
                    new Vector3(-570.7f, 0.0f, -1058.0f), new Vector3(-488.4f, 0.0f, -1094.8f), new Vector3(-405.0f, 0.0f, -1128.9f),
                    new Vector3(-321.5f, 0.0f, -1162.7f), new Vector3(-254.6f, 0.0f, -1189.4f), new Vector3(-220.1f, 0.0f, -1199.8f),
                    new Vector3(-184.8f, 0.0f, -1207.1f), new Vector3(-148.9f, 0.0f, -1210.1f), new Vector3(-112.8f, 0.0f, -1209.6f),
                    new Vector3(-96.9f, 0.0f, -1203.4f), new Vector3(-84.6f, 0.0f, -1169.5f), new Vector3(-71.6f, 0.0f, -1159.0f),
                    new Vector3(-8.5f, 0.0f, -1159.2f), new Vector3(7.4f, 0.0f, -1152.2f), new Vector3(12.0f, 0.0f, -1135.1f),
                    new Vector3(14.7f, 0.0f, -1045.0f), new Vector3(14.7f, 0.0f, -954.9f), new Vector3(14.8f, 0.0f, -864.8f),
                    new Vector3(14.5f, 0.0f, -774.7f), new Vector3(13.9f, 0.0f, -684.6f), new Vector3(11.9f, 0.0f, -594.5f),
                    new Vector3(6.7f, 0.0f, -504.5f), new Vector3(4.2f, 0.0f, -414.5f), new Vector3(3.0f, 0.0f, -324.4f),
                    new Vector3(2.2f, 0.0f, -234.3f), new Vector3(1.3f, 0.0f, -144.2f), new Vector3(0.5f, 0.0f, -54.1f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 90.1f), new Vector3(-1.4f, 0.0f, 108.0f),
                    new Vector3(-8.1f, 0.0f, 124.4f), new Vector3(-21.6f, 0.0f, 135.0f), new Vector3(-39.1f, 0.0f, 137.2f),
                    new Vector3(-56.2f, 0.0f, 131.9f), new Vector3(-72.1f, 0.0f, 123.7f), new Vector3(-86.2f, 0.0f, 112.6f),
                    new Vector3(-97.7f, 0.0f, 98.9f), new Vector3(-105.8f, 0.0f, 82.9f), new Vector3(-116.5f, 0.0f, 48.5f),
                    new Vector3(-118.4f, 0.0f, 30.7f), new Vector3(-120.6f, 0.0f, -5.3f), new Vector3(-125.5f, 0.0f, -22.6f),
                    new Vector3(-141.3f, 0.0f, -44.6f), new Vector3(-154.4f, 0.0f, -56.8f), new Vector3(-179.0f, 0.0f, -67.8f),
                    new Vector3(-196.2f, 0.0f, -72.8f), new Vector3(-214.1f, 0.0f, -74.6f), new Vector3(-231.9f, 0.0f, -72.9f),
                    new Vector3(-258.4f, 0.0f, -67.4f), new Vector3(-274.8f, 0.0f, -60.5f), new Vector3(-290.6f, 0.0f, -51.8f),
                    new Vector3(-351.3f, 0.0f, 14.7f), new Vector3(-411.8f, 0.0f, 81.6f), new Vector3(-472.3f, 0.0f, 148.4f),
                    new Vector3(-532.7f, 0.0f, 215.3f), new Vector3(-593.2f, 0.0f, 282.1f), new Vector3(-653.6f, 0.0f, 348.9f),
                    new Vector3(-714.1f, 0.0f, 415.8f), new Vector3(-774.5f, 0.0f, 482.6f), new Vector3(-835.0f, 0.0f, 549.5f),
                    new Vector3(-865.2f, 0.0f, 582.9f), new Vector3(-873.2f, 0.0f, 598.7f), new Vector3(-867.5f, 0.0f, 614.8f),
                    new Vector3(-799.1f, 0.0f, 673.4f), new Vector3(-730.8f, 0.0f, 732.2f), new Vector3(-697.3f, 0.0f, 762.3f),
                    new Vector3(-681.4f, 0.0f, 784.2f), new Vector3(-677.6f, 0.0f, 801.5f), new Vector3(-674.7f, 0.0f, 846.4f),
                    new Vector3(-678.3f, 0.0f, 873.2f), new Vector3(-685.7f, 0.0f, 899.2f), new Vector3(-692.5f, 0.0f, 915.8f),
                    new Vector3(-706.8f, 0.0f, 938.8f), new Vector3(-717.2f, 0.0f, 953.5f), new Vector3(-729.3f, 0.0f, 966.8f),
                    new Vector3(-745.5f, 0.0f, 966.0f), new Vector3(-760.2f, 0.0f, 955.6f), new Vector3(-777.1f, 0.0f, 951.5f),
                    new Vector3(-793.3f, 0.0f, 958.5f), new Vector3(-806.3f, 0.0f, 970.8f), new Vector3(-813.1f, 0.0f, 987.1f),
                    new Vector3(-820.4f, 0.0f, 1022.4f), new Vector3(-826.0f, 0.0f, 1039.5f), new Vector3(-830.4f, 0.0f, 1057.0f),
                    new Vector3(-843.1f, 0.0f, 1069.0f), new Vector3(-859.8f, 0.0f, 1070.8f), new Vector3(-876.6f, 0.0f, 1065.6f),
                    new Vector3(-905.8f, 0.0f, 1044.6f), new Vector3(-974.6f, 0.0f, 986.4f), new Vector3(-1043.4f, 0.0f, 928.2f),
                    new Vector3(-1112.3f, 0.0f, 870.0f), new Vector3(-1126.8f, 0.0f, 859.4f), new Vector3(-1202.1f, 0.0f, 809.8f),
                    new Vector3(-1218.9f, 0.0f, 803.9f), new Vector3(-1236.3f, 0.0f, 798.9f), new Vector3(-1254.0f, 0.0f, 798.2f),
                    new Vector3(-1290.0f, 0.0f, 800.8f), new Vector3(-1307.2f, 0.0f, 805.6f), new Vector3(-1390.1f, 0.0f, 840.7f),
                    new Vector3(-1471.6f, 0.0f, 879.2f), new Vector3(-1488.8f, 0.0f, 883.5f), new Vector3(-1524.8f, 0.0f, 886.5f),
                    new Vector3(-1560.5f, 0.0f, 882.2f), new Vector3(-1577.6f, 0.0f, 876.7f), new Vector3(-1644.0f, 0.0f, 848.5f),
                    new Vector3(-1659.8f, 0.0f, 840.1f), new Vector3(-1698.7f, 0.0f, 817.3f), new Vector3(-1708.1f, 0.0f, 802.9f),
                    new Vector3(-1703.1f, 0.0f, 786.2f), new Vector3(-1696.7f, 0.0f, 769.4f), new Vector3(-1673.7f, 0.0f, 682.3f),
                    new Vector3(-1651.8f, 0.0f, 594.9f), new Vector3(-1631.4f, 0.0f, 507.1f), new Vector3(-1613.4f, 0.0f, 418.8f),
                    new Vector3(-1609.6f, 0.0f, 401.2f), new Vector3(-1581.9f, 0.0f, 315.4f), new Vector3(-1544.2f, 0.0f, 233.6f),
                    new Vector3(-1505.1f, 0.0f, 152.3f), new Vector3(-1474.5f, 0.0f, 97.2f), new Vector3(-1425.2f, 0.0f, 21.8f),
                    new Vector3(-1371.2f, 0.0f, -50.3f), new Vector3(-1312.8f, 0.0f, -118.9f), new Vector3(-1252.5f, 0.0f, -185.9f),
                    new Vector3(-1192.1f, 0.0f, -252.8f), new Vector3(-1132.5f, 0.0f, -320.3f), new Vector3(-1075.1f, 0.0f, -389.8f),
                    new Vector3(-1017.7f, 0.0f, -459.3f), new Vector3(-960.3f, 0.0f, -528.8f), new Vector3(-902.3f, 0.0f, -597.7f),
                    new Vector3(-843.8f, 0.0f, -666.3f), new Vector3(-784.4f, 0.0f, -734.1f), new Vector3(-726.2f, 0.0f, -802.9f),
                    new Vector3(-708.9f, 0.0f, -823.7f), new Vector3(-692.8f, 0.0f, -827.0f), new Vector3(-678.8f, 0.0f, -815.8f),
                    new Vector3(-662.8f, 0.0f, -808.1f), new Vector3(-617.9f, 0.0f, -810.3f), new Vector3(-591.4f, 0.0f, -804.7f),
                    new Vector3(-575.5f, 0.0f, -796.5f), new Vector3(-560.0f, 0.0f, -787.3f), new Vector3(-491.1f, 0.0f, -729.3f),
                    new Vector3(-422.9f, 0.0f, -670.4f), new Vector3(-356.3f, 0.0f, -609.7f), new Vector3(-289.7f, 0.0f, -549.0f),
                    new Vector3(-222.1f, 0.0f, -489.4f), new Vector3(-154.5f, 0.0f, -429.8f), new Vector3(-101.3f, 0.0f, -381.1f),
                    new Vector3(-43.7f, 0.0f, -311.8f), new Vector3(-16.2f, 0.0f, -276.1f), new Vector3(-8.1f, 0.0f, -260.1f),
                    new Vector3(-2.3f, 0.0f, -243.1f), new Vector3(0.3f, 0.0f, -225.3f), new Vector3(0.4f, 0.0f, -135.2f),
                    new Vector3(0.1f, 0.0f, -45.1f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 90.1f), new Vector3(0.0f, 0.0f, 180.3f),
                    new Vector3(0.0f, 0.0f, 270.4f), new Vector3(-0.2f, 0.0f, 360.6f), new Vector3(0.0f, 0.0f, 450.6f),
                    new Vector3(5.4f, 0.0f, 467.8f), new Vector3(15.6f, 0.0f, 482.7f), new Vector3(29.1f, 0.0f, 494.4f),
                    new Vector3(45.6f, 0.0f, 501.0f), new Vector3(63.4f, 0.0f, 502.6f), new Vector3(80.9f, 0.0f, 499.4f),
                    new Vector3(96.8f, 0.0f, 491.4f), new Vector3(109.6f, 0.0f, 479.1f), new Vector3(154.9f, 0.0f, 401.1f),
                    new Vector3(195.5f, 0.0f, 330.9f), new Vector3(207.3f, 0.0f, 317.4f), new Vector3(222.4f, 0.0f, 307.7f),
                    new Vector3(239.5f, 0.0f, 302.6f), new Vector3(257.1f, 0.0f, 302.6f), new Vector3(274.3f, 0.0f, 307.6f),
                    new Vector3(289.6f, 0.0f, 317.2f), new Vector3(300.6f, 0.0f, 331.3f), new Vector3(342.1f, 0.0f, 411.3f),
                    new Vector3(362.6f, 0.0f, 451.4f), new Vector3(372.9f, 0.0f, 466.3f), new Vector3(385.9f, 0.0f, 478.7f),
                    new Vector3(400.5f, 0.0f, 489.2f), new Vector3(485.4f, 0.0f, 519.4f), new Vector3(570.5f, 0.0f, 549.2f),
                    new Vector3(655.6f, 0.0f, 579.0f), new Vector3(723.7f, 0.0f, 602.8f), new Vector3(741.3f, 0.0f, 606.5f),
                    new Vector3(758.9f, 0.0f, 602.7f), new Vector3(775.4f, 0.0f, 595.8f), new Vector3(789.0f, 0.0f, 584.1f),
                    new Vector3(799.0f, 0.0f, 569.2f), new Vector3(826.7f, 0.0f, 483.4f), new Vector3(827.4f, 0.0f, 465.6f),
                    new Vector3(821.7f, 0.0f, 448.4f), new Vector3(811.9f, 0.0f, 433.4f), new Vector3(798.5f, 0.0f, 421.6f),
                    new Vector3(782.1f, 0.0f, 414.1f), new Vector3(696.9f, 0.0f, 384.7f), new Vector3(611.7f, 0.0f, 355.3f),
                    new Vector3(586.1f, 0.0f, 346.5f), new Vector3(572.6f, 0.0f, 335.2f), new Vector3(566.6f, 0.0f, 318.4f),
                    new Vector3(571.6f, 0.0f, 301.5f), new Vector3(584.2f, 0.0f, 289.1f), new Vector3(601.5f, 0.0f, 285.0f),
                    new Vector3(691.0f, 0.0f, 274.7f), new Vector3(780.6f, 0.0f, 264.0f), new Vector3(870.1f, 0.0f, 253.3f),
                    new Vector3(887.3f, 0.0f, 248.6f), new Vector3(902.5f, 0.0f, 239.2f), new Vector3(915.2f, 0.0f, 226.6f),
                    new Vector3(923.3f, 0.0f, 210.8f), new Vector3(925.8f, 0.0f, 193.2f), new Vector3(923.1f, 0.0f, 175.5f),
                    new Vector3(916.1f, 0.0f, 159.0f), new Vector3(904.4f, 0.0f, 145.6f), new Vector3(889.2f, 0.0f, 136.1f),
                    new Vector3(872.1f, 0.0f, 130.6f), new Vector3(845.7f, 0.0f, 125.0f), new Vector3(812.1f, 0.0f, 112.0f),
                    new Vector3(787.8f, 0.0f, 100.1f), new Vector3(764.9f, 0.0f, 85.8f), new Vector3(737.3f, 0.0f, 62.6f),
                    new Vector3(724.2f, 0.0f, 50.2f), new Vector3(700.8f, 0.0f, 22.8f), new Vector3(687.7f, 0.0f, 10.5f),
                    new Vector3(671.8f, 0.0f, 2.3f), new Vector3(654.2f, 0.0f, -1.1f), new Vector3(636.2f, 0.0f, 0.5f),
                    new Vector3(549.2f, 0.0f, 23.8f), new Vector3(496.9f, 0.0f, 37.8f), new Vector3(479.0f, 0.0f, 37.0f),
                    new Vector3(463.1f, 0.0f, 29.6f), new Vector3(450.9f, 0.0f, 16.4f), new Vector3(443.5f, 0.0f, 0.1f),
                    new Vector3(443.4f, 0.0f, -17.4f), new Vector3(446.8f, 0.0f, -35.1f), new Vector3(457.2f, 0.0f, -69.6f),
                    new Vector3(475.6f, 0.0f, -110.8f), new Vector3(498.9f, 0.0f, -149.3f), new Vector3(510.0f, 0.0f, -163.4f),
                    new Vector3(534.1f, 0.0f, -190.3f), new Vector3(568.4f, 0.0f, -219.5f), new Vector3(598.4f, 0.0f, -239.4f),
                    new Vector3(614.6f, 0.0f, -247.3f), new Vector3(697.8f, 0.0f, -281.9f), new Vector3(781.2f, 0.0f, -316.2f),
                    new Vector3(839.6f, 0.0f, -340.1f), new Vector3(853.9f, 0.0f, -350.8f), new Vector3(865.5f, 0.0f, -364.5f),
                    new Vector3(875.0f, 0.0f, -379.7f), new Vector3(881.9f, 0.0f, -396.3f), new Vector3(885.3f, 0.0f, -423.1f),
                    new Vector3(884.7f, 0.0f, -513.2f), new Vector3(883.3f, 0.0f, -531.1f), new Vector3(877.2f, 0.0f, -547.9f),
                    new Vector3(867.2f, 0.0f, -562.9f), new Vector3(853.1f, 0.0f, -573.9f), new Vector3(775.0f, 0.0f, -618.8f),
                    new Vector3(720.1f, 0.0f, -649.9f), new Vector3(702.6f, 0.0f, -653.2f), new Vector3(684.7f, 0.0f, -651.0f),
                    new Vector3(668.2f, 0.0f, -644.4f), new Vector3(653.4f, 0.0f, -634.2f), new Vector3(584.4f, 0.0f, -576.2f),
                    new Vector3(515.4f, 0.0f, -518.2f), new Vector3(467.0f, 0.0f, -477.8f), new Vector3(450.9f, 0.0f, -469.8f),
                    new Vector3(433.2f, 0.0f, -466.6f), new Vector3(415.5f, 0.0f, -469.1f), new Vector3(399.2f, 0.0f, -476.7f),
                    new Vector3(324.6f, 0.0f, -527.3f), new Vector3(250.0f, 0.0f, -577.9f), new Vector3(175.4f, 0.0f, -628.5f),
                    new Vector3(100.8f, 0.0f, -679.1f), new Vector3(84.7f, 0.0f, -687.0f), new Vector3(67.4f, 0.0f, -691.1f),
                    new Vector3(49.8f, 0.0f, -689.3f), new Vector3(33.1f, 0.0f, -682.7f), new Vector3(19.0f, 0.0f, -671.6f),
                    new Vector3(8.8f, 0.0f, -656.9f), new Vector3(2.5f, 0.0f, -640.0f), new Vector3(2.0f, 0.0f, -549.9f),
                    new Vector3(1.6f, 0.0f, -459.7f), new Vector3(1.3f, 0.0f, -369.6f), new Vector3(1.0f, 0.0f, -279.5f),
                    new Vector3(0.7f, 0.0f, -189.3f), new Vector3(0.4f, 0.0f, -99.2f), new Vector3(0.0f, 0.0f, -9.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 90.0f), new Vector3(0.0f, 0.0f, 180.1f),
                    new Vector3(0.0f, 0.0f, 270.1f), new Vector3(0.0f, 0.0f, 360.1f), new Vector3(0.0f, 0.0f, 432.2f),
                    new Vector3(-9.3f, 0.0f, 446.3f), new Vector3(-27.0f, 0.0f, 447.7f), new Vector3(-45.0f, 0.0f, 447.9f),
                    new Vector3(-59.1f, 0.0f, 458.9f), new Vector3(-61.8f, 0.0f, 476.4f), new Vector3(-54.8f, 0.0f, 492.7f),
                    new Vector3(-46.1f, 0.0f, 508.4f), new Vector3(2.1f, 0.0f, 573.6f), new Vector3(9.8f, 0.0f, 589.8f),
                    new Vector3(14.8f, 0.0f, 607.0f), new Vector3(29.8f, 0.0f, 695.7f), new Vector3(44.2f, 0.0f, 784.6f),
                    new Vector3(54.6f, 0.0f, 846.8f), new Vector3(63.2f, 0.0f, 881.6f), new Vector3(57.1f, 0.0f, 897.6f),
                    new Vector3(36.6f, 0.0f, 915.1f), new Vector3(24.9f, 0.0f, 928.8f), new Vector3(15.4f, 0.0f, 944.0f),
                    new Vector3(8.9f, 0.0f, 970.2f), new Vector3(7.8f, 0.0f, 988.1f), new Vector3(8.9f, 0.0f, 1006.0f),
                    new Vector3(16.5f, 0.0f, 1031.9f), new Vector3(29.4f, 0.0f, 1055.5f), new Vector3(42.3f, 0.0f, 1068.1f),
                    new Vector3(70.8f, 0.0f, 1090.1f), new Vector3(83.2f, 0.0f, 1103.1f), new Vector3(93.1f, 0.0f, 1118.1f),
                    new Vector3(99.2f, 0.0f, 1134.8f), new Vector3(103.7f, 0.0f, 1152.3f), new Vector3(108.3f, 0.0f, 1197.0f),
                    new Vector3(106.4f, 0.0f, 1214.7f), new Vector3(101.5f, 0.0f, 1231.9f), new Vector3(91.5f, 0.0f, 1246.9f),
                    new Vector3(75.8f, 0.0f, 1268.9f), new Vector3(68.9f, 0.0f, 1285.2f), new Vector3(70.2f, 0.0f, 1303.1f),
                    new Vector3(75.3f, 0.0f, 1320.3f), new Vector3(102.9f, 0.0f, 1396.5f), new Vector3(111.2f, 0.0f, 1412.5f),
                    new Vector3(124.5f, 0.0f, 1423.9f), new Vector3(139.6f, 0.0f, 1433.7f), new Vector3(156.6f, 0.0f, 1439.3f),
                    new Vector3(174.5f, 0.0f, 1440.7f), new Vector3(201.5f, 0.0f, 1441.1f), new Vector3(218.2f, 0.0f, 1446.1f),
                    new Vector3(232.2f, 0.0f, 1457.5f), new Vector3(241.6f, 0.0f, 1472.6f), new Vector3(272.5f, 0.0f, 1527.6f),
                    new Vector3(321.6f, 0.0f, 1592.0f), new Vector3(347.1f, 0.0f, 1639.6f), new Vector3(360.9f, 0.0f, 1672.8f),
                    new Vector3(387.0f, 0.0f, 1759.0f), new Vector3(413.3f, 0.0f, 1845.1f), new Vector3(437.4f, 0.0f, 1931.8f),
                    new Vector3(461.5f, 0.0f, 2018.6f), new Vector3(466.1f, 0.0f, 2035.9f), new Vector3(464.4f, 0.0f, 2053.7f),
                    new Vector3(457.9f, 0.0f, 2070.3f), new Vector3(446.2f, 0.0f, 2083.8f), new Vector3(432.0f, 0.0f, 2095.0f),
                    new Vector3(414.3f, 0.0f, 2098.2f), new Vector3(396.5f, 0.0f, 2097.0f), new Vector3(379.1f, 0.0f, 2092.5f),
                    new Vector3(365.3f, 0.0f, 2081.5f), new Vector3(353.7f, 0.0f, 2067.9f), new Vector3(340.6f, 0.0f, 2044.3f),
                    new Vector3(333.2f, 0.0f, 2018.4f), new Vector3(331.5f, 0.0f, 2000.4f), new Vector3(333.1f, 0.0f, 1982.7f),
                    new Vector3(349.8f, 0.0f, 1903.4f), new Vector3(351.6f, 0.0f, 1885.6f), new Vector3(352.3f, 0.0f, 1858.6f),
                    new Vector3(349.9f, 0.0f, 1831.7f), new Vector3(342.6f, 0.0f, 1796.5f), new Vector3(333.1f, 0.0f, 1771.2f),
                    new Vector3(317.6f, 0.0f, 1738.8f), new Vector3(295.6f, 0.0f, 1710.3f), new Vector3(251.1f, 0.0f, 1665.6f),
                    new Vector3(226.7f, 0.0f, 1639.2f), new Vector3(209.0f, 0.0f, 1607.9f), new Vector3(194.4f, 0.0f, 1565.4f),
                    new Vector3(181.4f, 0.0f, 1553.0f), new Vector3(165.4f, 0.0f, 1545.2f), new Vector3(147.6f, 0.0f, 1542.9f),
                    new Vector3(120.7f, 0.0f, 1544.1f), new Vector3(103.6f, 0.0f, 1539.3f), new Vector3(86.8f, 0.0f, 1532.8f),
                    new Vector3(73.0f, 0.0f, 1521.3f), new Vector3(62.2f, 0.0f, 1507.0f), new Vector3(44.9f, 0.0f, 1475.4f),
                    new Vector3(26.1f, 0.0f, 1424.8f), new Vector3(19.3f, 0.0f, 1398.7f), new Vector3(14.2f, 0.0f, 1372.2f),
                    new Vector3(12.9f, 0.0f, 1354.3f), new Vector3(12.8f, 0.0f, 1327.3f), new Vector3(21.5f, 0.0f, 1237.7f),
                    new Vector3(23.7f, 0.0f, 1201.7f), new Vector3(20.3f, 0.0f, 1156.8f), new Vector3(13.1f, 0.0f, 1121.6f),
                    new Vector3(7.3f, 0.0f, 1104.6f), new Vector3(-10.0f, 0.0f, 1063.0f), new Vector3(-24.4f, 0.0f, 1020.4f),
                    new Vector3(-27.9f, 0.0f, 1002.8f), new Vector3(-32.5f, 0.0f, 976.2f), new Vector3(-32.5f, 0.0f, 931.2f),
                    new Vector3(-29.2f, 0.0f, 904.4f), new Vector3(-11.6f, 0.0f, 825.4f), new Vector3(-5.6f, 0.0f, 789.9f),
                    new Vector3(-4.2f, 0.0f, 753.9f), new Vector3(-8.1f, 0.0f, 700.1f), new Vector3(-25.7f, 0.0f, 630.3f),
                    new Vector3(-41.4f, 0.0f, 588.1f), new Vector3(-79.1f, 0.0f, 516.4f), new Vector3(-95.0f, 0.0f, 494.6f),
                    new Vector3(-101.5f, 0.0f, 478.1f), new Vector3(-102.1f, 0.0f, 460.2f), new Vector3(-96.8f, 0.0f, 443.1f),
                    new Vector3(-73.5f, 0.0f, 404.6f), new Vector3(-67.0f, 0.0f, 387.8f), new Vector3(-63.3f, 0.0f, 370.2f),
                    new Vector3(-62.2f, 0.0f, 352.4f), new Vector3(-70.6f, 0.0f, 280.9f), new Vector3(-75.6f, 0.0f, 263.7f),
                    new Vector3(-84.6f, 0.0f, 248.3f), new Vector3(-96.0f, 0.0f, 234.5f), new Vector3(-161.1f, 0.0f, 172.3f),
                    new Vector3(-206.5f, 0.0f, 128.5f), new Vector3(-230.1f, 0.0f, 101.3f), new Vector3(-255.4f, 0.0f, 64.1f),
                    new Vector3(-267.6f, 0.0f, 40.1f), new Vector3(-277.5f, 0.0f, 15.0f), new Vector3(-290.4f, 0.0f, -37.5f),
                    new Vector3(-296.9f, 0.0f, -127.3f), new Vector3(-300.3f, 0.0f, -208.2f), new Vector3(-295.4f, 0.0f, -262.0f),
                    new Vector3(-286.3f, 0.0f, -315.2f), new Vector3(-264.7f, 0.0f, -383.9f), new Vector3(-244.6f, 0.0f, -434.1f),
                    new Vector3(-214.0f, 0.0f, -489.1f), new Vector3(-160.2f, 0.0f, -561.3f), new Vector3(-117.5f, 0.0f, -619.3f),
                    new Vector3(-104.0f, 0.0f, -631.2f), new Vector3(-88.0f, 0.0f, -636.9f), new Vector3(-70.3f, 0.0f, -634.6f),
                    new Vector3(-55.5f, 0.0f, -625.1f), new Vector3(-47.7f, 0.0f, -609.5f), new Vector3(-45.0f, 0.0f, -591.8f),
                    new Vector3(-35.2f, 0.0f, -502.3f), new Vector3(-24.1f, 0.0f, -413.0f), new Vector3(-12.9f, 0.0f, -323.6f),
                    new Vector3(-4.9f, 0.0f, -234.0f), new Vector3(-1.6f, 0.0f, -144.0f), new Vector3(-0.5f, 0.0f, -54.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.2f, 17.7f), new Vector3(7.4f, 0.7f, 34.1f),
                    new Vector3(17.7f, 1.4f, 48.8f), new Vector3(70.4f, 6.5f, 122.0f), new Vector3(117.3f, 8.9f, 188.3f),
                    new Vector3(133.9f, 10.3f, 190.8f), new Vector3(147.7f, 12.2f, 180.3f), new Vector3(152.7f, 14.5f, 163.3f),
                    new Vector3(154.6f, 17.1f, 145.4f), new Vector3(164.4f, 19.9f, 130.4f), new Vector3(175.0f, 22.7f, 115.8f),
                    new Vector3(183.0f, 25.3f, 99.7f), new Vector3(197.3f, 27.4f, 93.8f), new Vector3(202.2f, 29.2f, 108.8f),
                    new Vector3(189.4f, 30.8f, 121.2f), new Vector3(172.3f, 32.0f, 142.2f), new Vector3(166.6f, 32.1f, 158.6f),
                    new Vector3(177.1f, 32.6f, 172.0f), new Vector3(194.1f, 33.5f, 177.7f), new Vector3(219.3f, 35.2f, 187.4f),
                    new Vector3(236.7f, 36.6f, 191.4f), new Vector3(250.4f, 37.8f, 183.7f), new Vector3(251.9f, 39.1f, 165.9f),
                    new Vector3(250.7f, 41.8f, 111.8f), new Vector3(241.2f, 40.5f, 40.3f), new Vector3(231.0f, 37.9f, -3.6f),
                    new Vector3(221.7f, 36.3f, -29.0f), new Vector3(204.9f, 34.6f, -60.9f), new Vector3(195.0f, 34.2f, -75.9f),
                    new Vector3(172.9f, 33.5f, -104.4f), new Vector3(149.0f, 30.5f, -131.4f), new Vector3(134.9f, 28.4f, -142.4f),
                    new Vector3(96.8f, 22.4f, -166.6f), new Vector3(16.0f, 15.9f, -206.7f), new Vector3(-26.3f, 12.8f, -222.2f),
                    new Vector3(-78.7f, 7.1f, -235.5f), new Vector3(-131.9f, 4.0f, -245.4f), new Vector3(-137.1f, 3.9f, -261.9f),
                    new Vector3(-151.3f, 3.7f, -270.6f), new Vector3(-167.8f, 3.4f, -266.7f), new Vector3(-185.3f, 3.0f, -265.8f),
                    new Vector3(-274.6f, 0.1f, -279.1f), new Vector3(-363.5f, -1.9f, -293.9f), new Vector3(-390.2f, -2.0f, -298.6f),
                    new Vector3(-401.2f, -2.0f, -312.0f), new Vector3(-412.4f, -2.1f, -336.6f), new Vector3(-418.6f, -2.2f, -353.5f),
                    new Vector3(-423.1f, -2.3f, -371.0f), new Vector3(-426.2f, -2.5f, -397.8f), new Vector3(-425.3f, -2.9f, -442.9f),
                    new Vector3(-415.7f, -3.0f, -457.7f), new Vector3(-401.2f, -3.1f, -468.4f), new Vector3(-391.5f, -3.3f, -483.0f),
                    new Vector3(-375.2f, -3.9f, -571.6f), new Vector3(-370.7f, -3.9f, -589.1f), new Vector3(-373.7f, -4.0f, -605.6f),
                    new Vector3(-389.0f, -4.0f, -615.0f), new Vector3(-388.8f, -4.0f, -632.7f), new Vector3(-372.4f, -3.9f, -674.7f),
                    new Vector3(-364.0f, -3.9f, -690.6f), new Vector3(-348.6f, -3.9f, -712.8f), new Vector3(-329.5f, -3.8f, -731.9f),
                    new Vector3(-313.8f, -3.7f, -740.8f), new Vector3(-297.3f, -3.7f, -748.1f), new Vector3(-286.4f, -3.6f, -761.1f),
                    new Vector3(-289.9f, -3.5f, -776.4f), new Vector3(-306.9f, -3.5f, -782.2f), new Vector3(-324.3f, -3.4f, -786.9f),
                    new Vector3(-342.1f, -3.4f, -789.7f), new Vector3(-360.0f, -3.3f, -788.8f), new Vector3(-370.6f, -3.2f, -775.8f),
                    new Vector3(-374.2f, -3.2f, -758.2f), new Vector3(-384.7f, -3.1f, -743.8f), new Vector3(-395.7f, -3.1f, -729.5f),
                    new Vector3(-415.4f, -3.0f, -699.3f), new Vector3(-443.7f, -3.0f, -613.7f), new Vector3(-455.9f, -2.9f, -570.3f),
                    new Vector3(-467.9f, -2.7f, -499.2f), new Vector3(-473.0f, -2.4f, -445.4f), new Vector3(-472.5f, -2.3f, -391.3f),
                    new Vector3(-470.1f, -2.2f, -364.3f), new Vector3(-460.4f, -2.1f, -329.6f), new Vector3(-456.7f, -2.0f, -312.0f),
                    new Vector3(-457.3f, -2.0f, -294.0f), new Vector3(-450.0f, -2.0f, -279.7f), new Vector3(-432.7f, -2.0f, -274.7f),
                    new Vector3(-414.8f, -2.0f, -273.2f), new Vector3(-343.5f, -1.9f, -261.9f), new Vector3(-257.3f, -1.6f, -235.6f),
                    new Vector3(-240.0f, -1.5f, -230.5f), new Vector3(-204.5f, -1.4f, -224.1f), new Vector3(-186.5f, -1.3f, -222.3f),
                    new Vector3(-169.4f, -1.2f, -216.9f), new Vector3(-112.6f, -1.1f, -189.4f), new Vector3(-87.6f, -1.0f, -179.2f),
                    new Vector3(-52.5f, -1.0f, -171.1f), new Vector3(-8.0f, -0.9f, -163.5f), new Vector3(9.1f, -0.8f, -157.9f),
                    new Vector3(24.4f, -0.8f, -148.6f), new Vector3(37.6f, -0.7f, -136.4f), new Vector3(48.7f, -0.6f, -122.3f),
                    new Vector3(55.5f, -0.5f, -105.8f), new Vector3(56.2f, -0.4f, -88.0f), new Vector3(52.9f, -0.3f, -70.3f),
                    new Vector3(46.3f, -0.2f, -53.5f), new Vector3(36.7f, -0.1f, -38.5f), new Vector3(22.7f, -0.1f, -27.2f),
                    new Vector3(9.2f, 0.0f, -15.2f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.7f, 90.1f), new Vector3(0.0f, 5.3f, 180.1f),
                    new Vector3(0.7f, 8.7f, 270.2f), new Vector3(1.5f, 10.0f, 360.2f), new Vector3(3.4f, 11.4f, 432.2f),
                    new Vector3(8.3f, 12.0f, 449.5f), new Vector3(15.8f, 12.6f, 465.8f), new Vector3(26.4f, 13.4f, 480.2f),
                    new Vector3(39.5f, 14.2f, 492.6f), new Vector3(85.1f, 16.8f, 521.5f), new Vector3(102.2f, 17.8f, 526.7f),
                    new Vector3(120.1f, 18.7f, 526.2f), new Vector3(136.8f, 19.6f, 519.7f), new Vector3(151.1f, 20.5f, 509.0f),
                    new Vector3(161.2f, 21.4f, 494.2f), new Vector3(166.0f, 22.2f, 477.0f), new Vector3(165.8f, 23.0f, 459.2f),
                    new Vector3(155.5f, 25.7f, 369.7f), new Vector3(146.3f, 26.1f, 289.2f), new Vector3(149.4f, 26.3f, 271.4f),
                    new Vector3(158.5f, 26.4f, 256.1f), new Vector3(170.2f, 26.7f, 242.4f), new Vector3(204.4f, 27.3f, 213.1f),
                    new Vector3(214.5f, 27.6f, 198.2f), new Vector3(220.4f, 27.9f, 181.4f), new Vector3(221.6f, 28.3f, 163.5f),
                    new Vector3(217.6f, 28.6f, 146.2f), new Vector3(187.4f, 30.0f, 80.8f), new Vector3(181.4f, 30.3f, 63.8f),
                    new Vector3(178.7f, 30.6f, 46.2f), new Vector3(180.0f, 30.9f, 28.4f), new Vector3(185.4f, 31.2f, 11.3f),
                    new Vector3(193.7f, 31.4f, -4.7f), new Vector3(205.3f, 31.6f, -18.4f), new Vector3(239.5f, 31.9f, -47.8f),
                    new Vector3(251.4f, 32.0f, -61.1f), new Vector3(261.1f, 32.0f, -76.2f), new Vector3(267.0f, 31.9f, -93.1f),
                    new Vector3(269.1f, 31.6f, -110.8f), new Vector3(266.9f, 31.3f, -128.6f), new Vector3(261.4f, 30.9f, -145.7f),
                    new Vector3(252.5f, 30.4f, -161.4f), new Vector3(240.4f, 29.8f, -174.6f), new Vector3(225.6f, 29.2f, -184.8f),
                    new Vector3(163.7f, 26.2f, -221.6f), new Vector3(150.6f, 25.4f, -233.9f), new Vector3(140.9f, 24.6f, -248.9f),
                    new Vector3(134.9f, 23.8f, -265.9f), new Vector3(132.6f, 23.0f, -283.7f), new Vector3(136.9f, 22.2f, -301.1f),
                    new Vector3(142.9f, 21.5f, -318.1f), new Vector3(160.6f, 19.9f, -359.4f), new Vector3(173.8f, 19.1f, -383.0f),
                    new Vector3(184.3f, 18.7f, -397.6f), new Vector3(196.1f, 18.3f, -411.3f), new Vector3(216.2f, 18.0f, -429.3f),
                    new Vector3(237.5f, 18.0f, -445.9f), new Vector3(253.8f, 17.8f, -453.4f), new Vector3(297.0f, 17.0f, -466.1f),
                    new Vector3(323.6f, 16.3f, -470.7f), new Vector3(359.6f, 15.1f, -470.8f), new Vector3(448.4f, 11.2f, -456.0f),
                    new Vector3(536.8f, 7.3f, -438.9f), new Vector3(553.2f, 6.7f, -444.7f), new Vector3(627.4f, 4.3f, -495.8f),
                    new Vector3(664.4f, 4.0f, -521.3f), new Vector3(673.2f, 3.9f, -536.5f), new Vector3(667.8f, 3.7f, -553.1f),
                    new Vector3(628.0f, 1.7f, -633.9f), new Vector3(583.1f, -1.5f, -712.0f), new Vector3(538.3f, -4.9f, -790.1f),
                    new Vector3(528.9f, -5.5f, -805.5f), new Vector3(517.2f, -6.1f, -819.1f), new Vector3(503.6f, -6.6f, -830.9f),
                    new Vector3(488.2f, -7.0f, -840.1f), new Vector3(423.9f, -8.0f, -872.6f), new Vector3(412.0f, -8.0f, -885.1f),
                    new Vector3(414.9f, -8.0f, -902.4f), new Vector3(428.6f, -8.1f, -912.4f), new Vector3(446.2f, -8.3f, -910.0f),
                    new Vector3(531.0f, -9.5f, -879.8f), new Vector3(582.7f, -10.4f, -864.2f), new Vector3(600.5f, -10.8f, -861.2f),
                    new Vector3(618.5f, -11.1f, -860.6f), new Vector3(645.4f, -11.7f, -861.3f), new Vector3(671.7f, -12.2f, -867.3f),
                    new Vector3(688.3f, -12.5f, -874.4f), new Vector3(719.2f, -13.1f, -892.8f), new Vector3(733.9f, -13.3f, -903.2f),
                    new Vector3(755.3f, -13.6f, -919.6f), new Vector3(774.8f, -13.8f, -938.4f), new Vector3(817.3f, -13.9f, -996.6f),
                    new Vector3(839.6f, -13.5f, -1035.6f), new Vector3(851.2f, -13.1f, -1060.1f), new Vector3(860.2f, -12.5f, -1085.5f),
                    new Vector3(866.5f, -12.0f, -1111.8f), new Vector3(870.5f, -11.3f, -1138.5f), new Vector3(872.2f, -10.4f, -1174.5f),
                    new Vector3(869.8f, -9.7f, -1201.4f), new Vector3(865.0f, -9.0f, -1228.0f), new Vector3(835.6f, -7.0f, -1313.1f),
                    new Vector3(823.5f, -6.4f, -1356.5f), new Vector3(820.0f, -6.2f, -1374.1f), new Vector3(820.8f, -6.1f, -1392.0f),
                    new Vector3(827.5f, -6.0f, -1408.5f), new Vector3(838.2f, -6.0f, -1422.9f), new Vector3(851.5f, -5.9f, -1434.9f),
                    new Vector3(866.1f, -5.7f, -1445.4f), new Vector3(881.6f, -5.4f, -1454.6f), new Vector3(897.6f, -5.1f, -1462.8f),
                    new Vector3(914.8f, -4.7f, -1468.3f), new Vector3(932.6f, -4.2f, -1470.2f), new Vector3(950.1f, -3.7f, -1467.2f),
                    new Vector3(965.8f, -3.2f, -1459.0f), new Vector3(978.0f, -2.6f, -1446.0f), new Vector3(986.9f, -1.9f, -1430.5f),
                    new Vector3(992.5f, -1.2f, -1413.4f), new Vector3(993.4f, -0.6f, -1395.6f), new Vector3(989.2f, 0.9f, -1359.8f),
                    new Vector3(976.9f, 3.9f, -1288.8f), new Vector3(954.3f, 7.2f, -1201.7f), new Vector3(937.1f, 8.7f, -1150.5f),
                    new Vector3(900.4f, 10.0f, -1068.2f), new Vector3(874.1f, 10.2f, -1021.1f), new Vector3(828.2f, 11.6f, -943.6f),
                    new Vector3(783.6f, 14.0f, -865.4f), new Vector3(736.5f, 16.7f, -788.6f), new Vector3(688.3f, 19.3f, -712.5f),
                    new Vector3(643.0f, 21.3f, -634.7f), new Vector3(620.4f, 21.8f, -595.8f), new Vector3(609.2f, 21.9f, -581.8f),
                    new Vector3(595.3f, 22.0f, -570.4f), new Vector3(579.5f, 22.0f, -561.8f), new Vector3(563.1f, 21.9f, -554.4f),
                    new Vector3(528.9f, 21.6f, -542.9f), new Vector3(493.9f, 21.2f, -534.7f), new Vector3(458.1f, 20.6f, -530.3f),
                    new Vector3(431.1f, 20.0f, -529.4f), new Vector3(341.2f, 17.9f, -534.5f), new Vector3(251.3f, 15.6f, -538.9f),
                    new Vector3(233.3f, 15.2f, -538.4f), new Vector3(206.4f, 14.5f, -535.7f), new Vector3(191.4f, 14.1f, -528.2f),
                    new Vector3(187.7f, 13.8f, -510.7f), new Vector3(184.7f, 13.4f, -492.9f), new Vector3(174.0f, 13.1f, -479.4f),
                    new Vector3(156.4f, 12.8f, -476.6f), new Vector3(138.5f, 12.6f, -477.4f), new Vector3(111.6f, 12.3f, -475.5f),
                    new Vector3(94.3f, 12.1f, -470.2f), new Vector3(79.0f, 12.0f, -460.9f), new Vector3(64.8f, 12.0f, -449.9f),
                    new Vector3(51.7f, 12.0f, -437.6f), new Vector3(35.1f, 11.7f, -416.3f), new Vector3(22.1f, 11.3f, -392.6f),
                    new Vector3(14.6f, 10.9f, -376.3f), new Vector3(9.0f, 10.5f, -359.2f), new Vector3(4.0f, 9.9f, -341.9f),
                    new Vector3(-0.6f, 8.8f, -306.2f), new Vector3(-1.6f, 5.4f, -216.1f), new Vector3(-0.9f, 2.2f, -126.1f),
                    new Vector3(-0.3f, 0.2f, -36.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.2f, 90.1f), new Vector3(0.0f, 0.8f, 180.2f),
                    new Vector3(1.6f, 1.0f, 198.1f), new Vector3(4.2f, 1.1f, 216.0f), new Vector3(10.3f, 1.3f, 232.8f),
                    new Vector3(18.5f, 1.5f, 248.7f), new Vector3(30.2f, 1.6f, 262.3f), new Vector3(52.2f, 1.9f, 278.1f),
                    new Vector3(77.4f, 2.2f, 287.7f), new Vector3(112.6f, 2.6f, 295.6f), new Vector3(201.4f, 3.5f, 310.4f),
                    new Vector3(228.1f, 3.8f, 314.9f), new Vector3(318.1f, 4.5f, 318.3f), new Vector3(408.2f, 4.9f, 315.5f),
                    new Vector3(498.1f, 5.0f, 310.3f), new Vector3(525.2f, 5.1f, 310.7f), new Vector3(542.6f, 5.1f, 314.7f),
                    new Vector3(559.8f, 5.2f, 320.0f), new Vector3(601.0f, 5.4f, 338.2f), new Vector3(626.9f, 5.5f, 345.9f),
                    new Vector3(644.7f, 5.6f, 343.6f), new Vector3(662.0f, 5.7f, 338.5f), new Vector3(743.3f, 6.2f, 299.7f),
                    new Vector3(778.9f, 6.5f, 294.9f), new Vector3(796.8f, 6.6f, 296.9f), new Vector3(813.6f, 6.7f, 303.0f),
                    new Vector3(829.7f, 6.9f, 311.2f), new Vector3(868.4f, 7.2f, 334.1f), new Vector3(885.8f, 7.3f, 338.8f),
                    new Vector3(903.7f, 7.5f, 339.0f), new Vector3(921.2f, 7.6f, 335.6f), new Vector3(937.2f, 7.7f, 328.0f),
                    new Vector3(956.4f, 7.9f, 309.0f), new Vector3(970.1f, 8.1f, 285.8f), new Vector3(997.3f, 8.4f, 228.8f),
                    new Vector3(1007.3f, 8.5f, 214.0f), new Vector3(1019.4f, 8.6f, 200.8f), new Vector3(1093.9f, 8.9f, 150.2f),
                    new Vector3(1169.3f, 9.0f, 100.7f), new Vector3(1244.4f, 8.9f, 51.0f), new Vector3(1319.6f, 8.5f, 1.3f),
                    new Vector3(1394.7f, 8.1f, -48.5f), new Vector3(1469.9f, 7.7f, -98.2f), new Vector3(1522.4f, 7.3f, -133.2f),
                    new Vector3(1593.7f, 6.8f, -188.3f), new Vector3(1649.5f, 6.5f, -233.8f), new Vector3(1661.6f, 6.4f, -247.1f),
                    new Vector3(1671.2f, 6.4f, -262.1f), new Vector3(1677.7f, 6.3f, -278.9f), new Vector3(1680.6f, 6.2f, -296.7f),
                    new Vector3(1678.4f, 6.2f, -314.5f), new Vector3(1673.4f, 6.1f, -331.8f), new Vector3(1666.0f, 6.1f, -348.1f),
                    new Vector3(1655.4f, 6.1f, -362.5f), new Vector3(1641.6f, 6.0f, -373.8f), new Vector3(1625.7f, 6.0f, -382.3f),
                    new Vector3(1575.0f, 6.0f, -400.9f), new Vector3(1534.0f, 5.9f, -419.7f), new Vector3(1495.7f, 5.8f, -443.3f),
                    new Vector3(1452.1f, 5.6f, -475.3f), new Vector3(1417.3f, 5.3f, -503.8f), new Vector3(1343.4f, 4.7f, -555.4f),
                    new Vector3(1328.6f, 4.5f, -565.8f), new Vector3(1318.1f, 4.4f, -579.6f), new Vector3(1321.1f, 4.3f, -596.7f),
                    new Vector3(1342.8f, 4.0f, -625.4f), new Vector3(1348.2f, 3.8f, -642.0f), new Vector3(1342.2f, 3.7f, -658.9f),
                    new Vector3(1314.8f, 3.4f, -682.2f), new Vector3(1291.9f, 3.1f, -696.6f), new Vector3(1267.0f, 2.9f, -707.0f),
                    new Vector3(1240.8f, 2.7f, -713.9f), new Vector3(1214.2f, 2.5f, -718.5f), new Vector3(1196.4f, 2.3f, -716.8f),
                    new Vector3(1181.0f, 2.2f, -707.9f), new Vector3(1159.5f, 2.0f, -691.5f), new Vector3(1093.0f, 1.4f, -630.7f),
                    new Vector3(1026.5f, 1.1f, -569.9f), new Vector3(960.0f, 1.0f, -509.0f), new Vector3(893.5f, 1.1f, -448.2f),
                    new Vector3(827.5f, 1.4f, -386.9f), new Vector3(815.8f, 1.5f, -373.3f), new Vector3(807.3f, 1.5f, -357.4f),
                    new Vector3(804.7f, 1.6f, -339.6f), new Vector3(804.8f, 1.7f, -321.7f), new Vector3(809.0f, 1.8f, -295.0f),
                    new Vector3(829.5f, 2.2f, -207.2f), new Vector3(833.3f, 2.4f, -180.5f), new Vector3(832.0f, 2.5f, -153.5f),
                    new Vector3(827.4f, 2.6f, -126.9f), new Vector3(815.8f, 2.8f, -102.5f), new Vector3(766.1f, 3.2f, -27.3f),
                    new Vector3(716.5f, 3.6f, 47.9f), new Vector3(708.7f, 3.6f, 64.1f), new Vector3(707.7f, 3.7f, 81.6f),
                    new Vector3(718.5f, 3.8f, 95.6f), new Vector3(735.3f, 3.8f, 101.6f), new Vector3(814.9f, 4.0f, 117.1f),
                    new Vector3(830.0f, 4.0f, 126.5f), new Vector3(836.6f, 4.0f, 142.4f), new Vector3(830.8f, 4.0f, 159.0f),
                    new Vector3(816.7f, 4.0f, 170.1f), new Vector3(784.2f, 4.0f, 185.8f), new Vector3(759.4f, 4.1f, 196.5f),
                    new Vector3(742.5f, 4.1f, 202.6f), new Vector3(716.2f, 4.2f, 209.0f), new Vector3(698.5f, 4.2f, 212.1f),
                    new Vector3(680.5f, 4.3f, 213.1f), new Vector3(662.9f, 4.3f, 211.0f), new Vector3(647.7f, 4.4f, 201.6f),
                    new Vector3(581.0f, 4.7f, 141.1f), new Vector3(514.7f, 5.2f, 80.1f), new Vector3(448.4f, 5.6f, 19.0f),
                    new Vector3(382.1f, 6.1f, -42.0f), new Vector3(315.8f, 6.5f, -103.0f), new Vector3(249.5f, 6.8f, -164.1f),
                    new Vector3(183.2f, 7.0f, -225.1f), new Vector3(172.4f, 7.0f, -239.5f), new Vector3(163.1f, 7.0f, -254.9f),
                    new Vector3(158.9f, 7.0f, -272.4f), new Vector3(158.7f, 7.0f, -290.4f), new Vector3(162.8f, 6.9f, -307.7f),
                    new Vector3(170.6f, 6.9f, -323.6f), new Vector3(185.1f, 6.8f, -333.9f), new Vector3(202.2f, 6.6f, -339.3f),
                    new Vector3(263.9f, 6.0f, -352.3f), new Vector3(280.6f, 5.8f, -358.9f), new Vector3(293.3f, 5.5f, -371.5f),
                    new Vector3(300.0f, 5.3f, -388.2f), new Vector3(301.5f, 5.1f, -406.1f), new Vector3(296.3f, 4.8f, -423.2f),
                    new Vector3(286.6f, 4.6f, -438.1f), new Vector3(272.5f, 4.3f, -449.0f), new Vector3(255.9f, 4.1f, -455.7f),
                    new Vector3(238.0f, 3.8f, -454.5f), new Vector3(221.2f, 3.6f, -448.4f), new Vector3(143.9f, 2.5f, -402.0f),
                    new Vector3(90.3f, 2.1f, -368.8f), new Vector3(65.1f, 2.0f, -343.1f), new Vector3(47.9f, 2.0f, -322.2f),
                    new Vector3(32.1f, 1.9f, -300.3f), new Vector3(18.4f, 1.8f, -277.0f), new Vector3(9.8f, 1.6f, -251.4f),
                    new Vector3(3.4f, 1.4f, -225.2f), new Vector3(0.8f, 1.0f, -171.2f), new Vector3(0.4f, 0.3f, -81.1f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 2.4f, 90.0f), new Vector3(0.0f, 5.7f, 180.1f),
                    new Vector3(0.0f, 5.4f, 225.1f), new Vector3(8.3f, 3.7f, 239.5f), new Vector3(24.8f, 1.0f, 237.1f),
                    new Vector3(36.1f, -2.7f, 223.2f), new Vector3(86.7f, -24.4f, 148.7f), new Vector3(112.1f, -29.9f, 111.6f),
                    new Vector3(137.6f, -27.9f, 64.0f), new Vector3(171.7f, -18.4f, -19.3f), new Vector3(177.6f, -16.7f, -36.3f),
                    new Vector3(195.5f, -11.5f, -124.5f), new Vector3(211.4f, 6.8f, -213.1f), new Vector3(227.4f, 18.0f, -301.7f),
                    new Vector3(235.4f, 19.3f, -346.0f), new Vector3(240.9f, 20.3f, -363.1f), new Vector3(270.2f, 25.1f, -418.9f),
                    new Vector3(275.8f, 26.8f, -435.9f), new Vector3(278.7f, 28.4f, -453.6f), new Vector3(279.4f, 30.1f, -471.6f),
                    new Vector3(277.5f, 31.8f, -489.5f), new Vector3(271.0f, 34.2f, -515.7f), new Vector3(253.8f, 37.6f, -557.2f),
                    new Vector3(249.0f, 38.6f, -574.5f), new Vector3(247.5f, 39.3f, -592.4f), new Vector3(250.3f, 39.9f, -682.4f),
                    new Vector3(253.8f, 39.2f, -772.4f), new Vector3(257.4f, 38.1f, -862.3f), new Vector3(254.9f, 37.7f, -898.2f),
                    new Vector3(251.4f, 37.4f, -925.0f), new Vector3(230.5f, 36.4f, -1012.5f), new Vector3(209.0f, 36.0f, -1100.0f),
                    new Vector3(187.5f, 35.6f, -1187.4f), new Vector3(166.3f, 34.2f, -1274.9f), new Vector3(145.0f, 32.4f, -1362.4f),
                    new Vector3(123.8f, 30.5f, -1449.9f), new Vector3(102.6f, 28.9f, -1537.3f), new Vector3(80.9f, 28.1f, -1624.7f),
                    new Vector3(72.0f, 28.0f, -1659.6f), new Vector3(65.2f, 27.9f, -1676.2f), new Vector3(53.4f, 27.8f, -1689.6f),
                    new Vector3(36.8f, 27.7f, -1696.4f), new Vector3(19.0f, 27.4f, -1698.4f), new Vector3(1.3f, 27.2f, -1699.8f),
                    new Vector3(-15.0f, 26.9f, -1707.3f), new Vector3(-27.8f, 26.5f, -1719.8f), new Vector3(-35.3f, 26.1f, -1736.1f),
                    new Vector3(-61.9f, 23.6f, -1822.1f), new Vector3(-70.7f, 23.0f, -1837.7f), new Vector3(-83.3f, 22.5f, -1850.4f),
                    new Vector3(-99.6f, 21.9f, -1858.0f), new Vector3(-117.4f, 21.3f, -1860.8f), new Vector3(-207.1f, 18.2f, -1868.5f),
                    new Vector3(-296.8f, 15.3f, -1876.1f), new Vector3(-386.5f, 13.2f, -1883.8f), new Vector3(-404.5f, 12.8f, -1885.3f),
                    new Vector3(-422.3f, 12.6f, -1883.4f), new Vector3(-437.7f, 12.3f, -1874.2f), new Vector3(-448.9f, 12.2f, -1860.4f),
                    new Vector3(-454.3f, 12.1f, -1843.5f), new Vector3(-453.1f, 12.0f, -1825.7f), new Vector3(-446.3f, 12.0f, -1809.2f),
                    new Vector3(-433.8f, 11.9f, -1796.6f), new Vector3(-417.9f, 11.7f, -1788.3f), new Vector3(-400.0f, 11.5f, -1786.0f),
                    new Vector3(-310.1f, 9.4f, -1782.9f), new Vector3(-283.1f, 8.5f, -1782.0f), new Vector3(-265.7f, 7.9f, -1777.4f),
                    new Vector3(-251.5f, 7.3f, -1766.7f), new Vector3(-241.8f, 6.7f, -1751.7f), new Vector3(-236.6f, 6.0f, -1734.6f),
                    new Vector3(-219.5f, 2.7f, -1646.2f), new Vector3(-212.4f, 1.4f, -1610.9f), new Vector3(-186.3f, -1.0f, -1524.8f),
                    new Vector3(-157.1f, -2.0f, -1439.6f), new Vector3(-126.5f, -2.7f, -1355.0f), new Vector3(-108.5f, -3.7f, -1304.1f),
                    new Vector3(-106.9f, -4.0f, -1286.2f), new Vector3(-109.1f, -4.5f, -1268.3f), new Vector3(-114.1f, -4.9f, -1251.0f),
                    new Vector3(-122.0f, -5.3f, -1234.8f), new Vector3(-132.5f, -5.8f, -1220.3f), new Vector3(-145.6f, -6.3f, -1208.1f),
                    new Vector3(-160.2f, -6.8f, -1197.6f), new Vector3(-205.4f, -8.2f, -1168.0f), new Vector3(-221.9f, -8.7f, -1160.9f),
                    new Vector3(-239.2f, -9.1f, -1155.9f), new Vector3(-256.9f, -9.6f, -1152.8f), new Vector3(-274.8f, -10.0f, -1151.3f),
                    new Vector3(-292.7f, -10.3f, -1152.6f), new Vector3(-319.1f, -10.9f, -1158.5f), new Vector3(-343.5f, -11.3f, -1169.9f),
                    new Vector3(-373.5f, -11.7f, -1189.7f), new Vector3(-444.1f, -12.0f, -1245.6f), new Vector3(-514.6f, -12.5f, -1301.5f),
                    new Vector3(-585.2f, -13.3f, -1357.4f), new Vector3(-606.5f, -13.6f, -1374.0f), new Vector3(-621.9f, -13.8f, -1383.3f),
                    new Vector3(-638.8f, -14.0f, -1389.4f), new Vector3(-656.6f, -14.2f, -1390.8f), new Vector3(-674.3f, -14.4f, -1388.5f),
                    new Vector3(-691.0f, -14.6f, -1382.3f), new Vector3(-705.7f, -14.8f, -1372.0f), new Vector3(-717.5f, -14.9f, -1358.6f),
                    new Vector3(-726.6f, -15.1f, -1343.1f), new Vector3(-734.9f, -15.3f, -1327.1f), new Vector3(-745.9f, -15.4f, -1312.9f),
                    new Vector3(-759.9f, -15.5f, -1301.6f), new Vector3(-776.4f, -15.7f, -1294.3f), new Vector3(-793.9f, -15.8f, -1290.2f),
                    new Vector3(-811.7f, -15.9f, -1291.1f), new Vector3(-828.7f, -15.9f, -1296.9f), new Vector3(-908.9f, -15.8f, -1337.6f),
                    new Vector3(-988.6f, -14.5f, -1379.5f), new Vector3(-1004.5f, -14.2f, -1387.9f), new Vector3(-1021.9f, -13.8f, -1392.1f),
                    new Vector3(-1039.6f, -13.4f, -1389.8f), new Vector3(-1055.6f, -12.9f, -1381.8f), new Vector3(-1067.5f, -12.5f, -1368.7f),
                    new Vector3(-1104.8f, -10.1f, -1286.8f), new Vector3(-1119.4f, -9.1f, -1253.9f), new Vector3(-1123.5f, -8.7f, -1236.5f),
                    new Vector3(-1124.9f, -8.3f, -1218.6f), new Vector3(-1123.2f, -7.9f, -1200.7f), new Vector3(-1118.4f, -7.5f, -1183.4f),
                    new Vector3(-1110.5f, -7.2f, -1167.2f), new Vector3(-1099.8f, -6.9f, -1152.8f), new Vector3(-1086.8f, -6.6f, -1140.4f),
                    new Vector3(-1031.4f, -6.0f, -1094.4f), new Vector3(-992.8f, -5.9f, -1071.3f), new Vector3(-951.9f, -5.5f, -1052.5f),
                    new Vector3(-900.7f, -4.7f, -1035.3f), new Vector3(-813.5f, -2.6f, -1013.0f), new Vector3(-769.0f, -1.4f, -1006.6f),
                    new Vector3(-679.0f, 1.3f, -1008.4f), new Vector3(-589.0f, 3.9f, -1011.9f), new Vector3(-544.0f, 5.1f, -1013.2f),
                    new Vector3(-517.1f, 5.8f, -1011.6f), new Vector3(-490.4f, 6.3f, -1007.3f), new Vector3(-464.3f, 6.9f, -1000.7f),
                    new Vector3(-422.5f, 7.5f, -984.0f), new Vector3(-406.7f, 7.7f, -975.4f), new Vector3(-376.2f, 8.0f, -956.3f),
                    new Vector3(-303.3f, 7.9f, -903.5f), new Vector3(-259.8f, 7.8f, -871.5f), new Vector3(-246.2f, 7.7f, -859.7f),
                    new Vector3(-234.3f, 7.6f, -846.2f), new Vector3(-220.0f, 7.5f, -823.3f), new Vector3(-211.0f, 7.4f, -797.9f),
                    new Vector3(-197.6f, 6.9f, -708.9f), new Vector3(-183.4f, 6.4f, -620.0f), new Vector3(-176.1f, 6.2f, -575.5f),
                    new Vector3(-155.2f, 6.0f, -506.6f), new Vector3(-118.8f, 5.8f, -424.3f), new Vector3(-79.5f, 4.4f, -343.3f),
                    new Vector3(-71.7f, 4.1f, -327.1f), new Vector3(-58.7f, 3.7f, -315.8f), new Vector3(-42.7f, 3.3f, -322.0f),
                    new Vector3(-27.6f, 3.0f, -331.8f), new Vector3(-10.8f, 2.6f, -335.6f), new Vector3(1.1f, 2.3f, -324.0f),
                    new Vector3(2.1f, 1.9f, -306.1f), new Vector3(-0.4f, 1.0f, -216.1f), new Vector3(-0.3f, 0.7f, -126.0f),
                    new Vector3(-0.1f, 0.1f, -36.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 90.1f), new Vector3(1.5f, 0.0f, 180.2f),
                    new Vector3(6.5f, 0.0f, 270.2f), new Vector3(5.0f, 0.0f, 288.1f), new Vector3(-6.2f, 0.0f, 301.3f),
                    new Vector3(-23.4f, 0.0f, 305.0f), new Vector3(-41.0f, 0.0f, 307.6f), new Vector3(-57.2f, 0.0f, 315.3f),
                    new Vector3(-72.1f, 0.0f, 325.4f), new Vector3(-84.8f, 0.0f, 338.1f), new Vector3(-93.5f, 0.0f, 353.9f),
                    new Vector3(-103.7f, 0.0f, 368.4f), new Vector3(-121.1f, 0.0f, 372.2f), new Vector3(-135.7f, 0.0f, 362.2f),
                    new Vector3(-145.5f, 0.0f, 347.2f), new Vector3(-151.6f, 0.0f, 330.5f), new Vector3(-161.9f, 0.0f, 286.6f),
                    new Vector3(-164.4f, 0.0f, 268.8f), new Vector3(-164.6f, 0.0f, 250.8f), new Vector3(-161.6f, 0.0f, 233.1f),
                    new Vector3(-154.3f, 0.0f, 207.0f), new Vector3(-135.7f, 0.0f, 118.9f), new Vector3(-132.8f, 0.0f, 64.9f),
                    new Vector3(-135.0f, 0.0f, 28.9f), new Vector3(-138.9f, 0.0f, 11.4f), new Vector3(-148.2f, 0.0f, -3.8f),
                    new Vector3(-162.2f, 0.0f, -14.4f), new Vector3(-179.0f, 0.0f, -20.9f), new Vector3(-196.6f, 0.0f, -24.2f),
                    new Vector3(-214.4f, 0.0f, -22.1f), new Vector3(-302.8f, 0.0f, -4.5f), new Vector3(-391.1f, 0.0f, 13.1f),
                    new Vector3(-479.5f, 0.0f, 30.7f), new Vector3(-559.1f, 0.0f, 46.5f), new Vector3(-584.8f, 0.0f, 54.9f),
                    new Vector3(-600.9f, 0.0f, 63.0f), new Vector3(-624.5f, 0.0f, 76.0f), new Vector3(-696.0f, 0.0f, 130.9f),
                    new Vector3(-767.1f, 0.0f, 186.3f), new Vector3(-838.2f, 0.0f, 241.7f), new Vector3(-873.8f, 0.0f, 269.2f),
                    new Vector3(-889.7f, 0.0f, 275.7f), new Vector3(-903.3f, 0.0f, 265.0f), new Vector3(-959.4f, 0.0f, 194.5f),
                    new Vector3(-1011.5f, 0.0f, 121.0f), new Vector3(-1024.7f, 0.0f, 110.9f), new Vector3(-1037.6f, 0.0f, 122.9f),
                    new Vector3(-1090.2f, 0.0f, 196.1f), new Vector3(-1111.5f, 0.0f, 225.2f), new Vector3(-1145.3f, 0.0f, 255.0f),
                    new Vector3(-1162.8f, 0.0f, 255.2f), new Vector3(-1178.7f, 0.0f, 247.5f), new Vector3(-1192.5f, 0.0f, 236.2f),
                    new Vector3(-1246.6f, 0.0f, 164.1f), new Vector3(-1300.8f, 0.0f, 92.1f), new Vector3(-1355.1f, 0.0f, 20.1f),
                    new Vector3(-1409.6f, 0.0f, -51.6f), new Vector3(-1420.7f, 0.0f, -65.8f), new Vector3(-1424.3f, 0.0f, -83.0f),
                    new Vector3(-1424.0f, 0.0f, -101.0f), new Vector3(-1417.2f, 0.0f, -117.4f), new Vector3(-1405.5f, 0.0f, -131.0f),
                    new Vector3(-1372.0f, 0.0f, -161.2f), new Vector3(-1348.1f, 0.0f, -173.8f), new Vector3(-1338.7f, 0.0f, -186.6f),
                    new Vector3(-1347.8f, 0.0f, -202.0f), new Vector3(-1352.5f, 0.0f, -219.4f), new Vector3(-1348.8f, 0.0f, -246.1f),
                    new Vector3(-1341.7f, 0.0f, -262.5f), new Vector3(-1313.5f, 0.0f, -297.7f), new Vector3(-1270.6f, 0.0f, -343.8f),
                    new Vector3(-1261.9f, 0.0f, -359.3f), new Vector3(-1255.9f, 0.0f, -376.2f), new Vector3(-1242.9f, 0.0f, -400.0f),
                    new Vector3(-1229.4f, 0.0f, -409.9f), new Vector3(-1212.1f, 0.0f, -407.7f), new Vector3(-1200.2f, 0.0f, -394.7f),
                    new Vector3(-1170.9f, 0.0f, -309.5f), new Vector3(-1141.7f, 0.0f, -224.2f), new Vector3(-1112.4f, 0.0f, -139.0f),
                    new Vector3(-1083.5f, 0.0f, -53.6f), new Vector3(-1061.9f, 0.0f, 5.7f), new Vector3(-1034.7f, 0.0f, 62.5f),
                    new Vector3(-1020.0f, 0.0f, 72.5f), new Vector3(-1002.4f, 0.0f, 74.6f), new Vector3(-987.5f, 0.0f, 65.7f),
                    new Vector3(-927.9f, 0.0f, -2.0f), new Vector3(-869.0f, 0.0f, -70.2f), new Vector3(-856.3f, 0.0f, -82.8f),
                    new Vector3(-841.4f, 0.0f, -93.0f), new Vector3(-825.2f, 0.0f, -100.8f), new Vector3(-808.3f, 0.0f, -107.0f),
                    new Vector3(-720.3f, 0.0f, -125.9f), new Vector3(-631.8f, 0.0f, -142.9f), new Vector3(-543.3f, 0.0f, -160.0f),
                    new Vector3(-454.8f, 0.0f, -177.3f), new Vector3(-375.1f, 0.0f, -192.3f), new Vector3(-359.6f, 0.0f, -198.6f),
                    new Vector3(-360.7f, 0.0f, -216.2f), new Vector3(-361.3f, 0.0f, -234.1f), new Vector3(-356.9f, 0.0f, -251.5f),
                    new Vector3(-345.4f, 0.0f, -265.2f), new Vector3(-329.8f, 0.0f, -273.8f), new Vector3(-312.6f, 0.0f, -279.1f),
                    new Vector3(-225.2f, 0.0f, -300.9f), new Vector3(-137.1f, 0.0f, -320.0f), new Vector3(-84.0f, 0.0f, -330.2f),
                    new Vector3(-66.4f, 0.0f, -328.5f), new Vector3(-12.8f, 0.0f, -267.6f), new Vector3(-4.2f, 0.0f, -252.1f),
                    new Vector3(-2.2f, 0.0f, -234.3f), new Vector3(-1.0f, 0.0f, -144.2f), new Vector3(-0.4f, 0.0f, -54.1f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.1f, 90.1f), new Vector3(0.9f, 0.4f, 180.2f),
                    new Vector3(2.3f, 0.8f, 270.2f), new Vector3(3.2f, 1.1f, 324.3f), new Vector3(13.0f, 1.2f, 338.6f),
                    new Vector3(29.9f, 1.3f, 344.9f), new Vector3(46.8f, 1.4f, 350.7f), new Vector3(62.1f, 1.5f, 360.2f),
                    new Vector3(76.3f, 1.6f, 371.3f), new Vector3(88.8f, 1.7f, 384.2f), new Vector3(104.5f, 1.9f, 406.2f),
                    new Vector3(111.3f, 2.0f, 422.8f), new Vector3(115.5f, 2.1f, 440.3f), new Vector3(116.7f, 2.1f, 458.2f),
                    new Vector3(115.2f, 2.6f, 548.3f), new Vector3(117.1f, 2.9f, 638.3f), new Vector3(119.4f, 3.0f, 683.3f),
                    new Vector3(130.5f, 3.0f, 772.7f), new Vector3(136.5f, 3.1f, 808.2f), new Vector3(158.8f, 3.3f, 895.5f),
                    new Vector3(182.1f, 3.5f, 973.1f), new Vector3(189.2f, 3.6f, 989.6f), new Vector3(203.6f, 3.7f, 999.3f),
                    new Vector3(220.3f, 3.8f, 994.2f), new Vector3(294.6f, 4.2f, 943.3f), new Vector3(310.1f, 4.3f, 934.3f),
                    new Vector3(327.3f, 4.4f, 935.0f), new Vector3(342.5f, 4.5f, 944.4f), new Vector3(355.5f, 4.6f, 956.9f),
                    new Vector3(417.3f, 5.0f, 1022.4f), new Vector3(473.2f, 5.4f, 1081.2f), new Vector3(490.7f, 5.4f, 1084.0f),
                    new Vector3(508.6f, 5.5f, 1083.0f), new Vector3(597.5f, 5.8f, 1068.2f), new Vector3(641.8f, 5.9f, 1060.2f),
                    new Vector3(659.1f, 5.9f, 1055.5f), new Vector3(742.1f, 6.0f, 1020.3f), new Vector3(799.4f, 6.0f, 994.1f),
                    new Vector3(869.1f, 5.9f, 975.9f), new Vector3(884.1f, 5.8f, 966.4f), new Vector3(891.9f, 5.8f, 950.9f),
                    new Vector3(890.3f, 5.8f, 933.0f), new Vector3(891.3f, 5.7f, 915.0f), new Vector3(894.2f, 5.7f, 897.2f),
                    new Vector3(900.0f, 5.6f, 880.3f), new Vector3(908.0f, 5.6f, 864.2f), new Vector3(930.5f, 5.5f, 836.1f),
                    new Vector3(948.1f, 5.4f, 815.6f), new Vector3(958.7f, 5.4f, 801.1f), new Vector3(970.6f, 5.3f, 776.8f),
                    new Vector3(979.7f, 5.2f, 751.4f), new Vector3(985.3f, 5.1f, 715.8f), new Vector3(986.0f, 5.0f, 688.9f),
                    new Vector3(982.2f, 4.9f, 662.1f), new Vector3(975.0f, 4.8f, 636.1f), new Vector3(967.7f, 4.8f, 619.7f),
                    new Vector3(950.1f, 4.7f, 588.2f), new Vector3(934.5f, 4.6f, 566.2f), new Vector3(882.9f, 4.3f, 492.4f),
                    new Vector3(856.8f, 4.2f, 455.7f), new Vector3(843.7f, 4.2f, 443.3f), new Vector3(829.2f, 4.2f, 432.6f),
                    new Vector3(813.2f, 4.1f, 424.5f), new Vector3(796.4f, 4.1f, 417.9f), new Vector3(769.9f, 4.1f, 412.9f),
                    new Vector3(734.1f, 4.0f, 408.7f), new Vector3(716.7f, 4.0f, 404.3f), new Vector3(700.0f, 4.0f, 397.6f),
                    new Vector3(684.3f, 4.0f, 388.9f), new Vector3(609.5f, 4.0f, 338.7f), new Vector3(550.1f, 3.8f, 297.8f),
                    new Vector3(529.6f, 3.7f, 280.2f), new Vector3(492.3f, 3.6f, 241.1f), new Vector3(476.0f, 3.5f, 219.6f),
                    new Vector3(456.9f, 3.3f, 189.0f), new Vector3(444.0f, 3.2f, 165.3f), new Vector3(431.2f, 3.1f, 131.6f),
                    new Vector3(423.2f, 2.9f, 105.8f), new Vector3(411.8f, 2.7f, 53.0f), new Vector3(410.4f, 2.6f, 35.1f),
                    new Vector3(409.4f, 2.5f, 8.1f), new Vector3(414.1f, 2.1f, -81.9f), new Vector3(417.2f, 1.9f, -126.8f),
                    new Vector3(421.2f, 1.8f, -144.3f), new Vector3(427.3f, 1.7f, -161.2f), new Vector3(435.9f, 1.6f, -177.1f),
                    new Vector3(447.4f, 1.6f, -190.9f), new Vector3(493.0f, 1.3f, -234.5f), new Vector3(505.0f, 1.3f, -247.9f),
                    new Vector3(513.4f, 1.2f, -263.8f), new Vector3(518.8f, 1.2f, -281.0f), new Vector3(520.7f, 1.2f, -298.8f),
                    new Vector3(527.1f, 1.0f, -388.7f), new Vector3(534.0f, 1.0f, -478.5f), new Vector3(534.6f, 1.0f, -496.4f),
                    new Vector3(530.5f, 1.1f, -541.3f), new Vector3(524.4f, 1.1f, -567.6f), new Vector3(507.1f, 1.2f, -609.2f),
                    new Vector3(464.8f, 1.5f, -688.7f), new Vector3(420.8f, 1.7f, -767.3f), new Vector3(375.6f, 2.0f, -845.3f),
                    new Vector3(355.5f, 2.1f, -875.2f), new Vector3(343.3f, 2.2f, -885.9f), new Vector3(318.5f, 2.3f, -875.2f),
                    new Vector3(235.8f, 2.6f, -839.5f), new Vector3(153.3f, 2.8f, -803.4f), new Vector3(136.9f, 2.8f, -795.9f),
                    new Vector3(122.9f, 2.9f, -784.7f), new Vector3(112.8f, 2.9f, -770.0f), new Vector3(107.5f, 2.9f, -752.9f),
                    new Vector3(105.5f, 2.9f, -735.1f), new Vector3(108.6f, 3.0f, -717.4f), new Vector3(134.7f, 3.0f, -631.2f),
                    new Vector3(157.5f, 2.8f, -544.0f), new Vector3(157.9f, 2.8f, -526.5f), new Vector3(143.9f, 2.7f, -517.3f),
                    new Vector3(53.9f, 2.4f, -522.2f), new Vector3(36.2f, 2.3f, -520.2f), new Vector3(19.7f, 2.2f, -513.0f),
                    new Vector3(6.3f, 2.1f, -501.2f), new Vector3(-2.5f, 2.0f, -485.6f), new Vector3(-7.2f, 1.9f, -468.4f),
                    new Vector3(-6.3f, 1.4f, -378.3f), new Vector3(-4.8f, 0.9f, -288.2f), new Vector3(-3.3f, 0.5f, -198.2f),
                    new Vector3(-1.8f, 0.2f, -108.1f), new Vector3(-0.3f, 0.0f, -18.0f)
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
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, -5.3f, 90.0f), new Vector3(0.0f, -13.0f, 180.0f),
                    new Vector3(-0.3f, -15.0f, 261.0f), new Vector3(-4.1f, -15.9f, 278.6f), new Vector3(-10.1f, -16.9f, 295.5f),
                    new Vector3(-21.3f, -18.1f, 309.2f), new Vector3(-37.9f, -19.4f, 315.1f), new Vector3(-55.4f, -20.8f, 313.0f),
                    new Vector3(-79.0f, -23.0f, 299.9f), new Vector3(-94.0f, -24.4f, 290.0f), new Vector3(-110.6f, -25.7f, 283.8f),
                    new Vector3(-127.9f, -26.9f, 287.2f), new Vector3(-141.9f, -28.0f, 298.2f), new Vector3(-160.8f, -29.2f, 317.5f),
                    new Vector3(-182.7f, -29.9f, 333.3f), new Vector3(-198.7f, -30.0f, 341.5f), new Vector3(-215.5f, -30.1f, 347.8f),
                    new Vector3(-233.1f, -30.2f, 351.4f), new Vector3(-251.0f, -30.4f, 353.1f), new Vector3(-269.0f, -30.7f, 352.5f),
                    new Vector3(-286.7f, -31.0f, 349.6f), new Vector3(-303.8f, -31.4f, 344.2f), new Vector3(-320.3f, -31.8f, 337.0f),
                    new Vector3(-335.6f, -32.2f, 327.6f), new Vector3(-349.7f, -32.7f, 316.4f), new Vector3(-362.6f, -33.2f, 303.9f),
                    new Vector3(-373.9f, -33.7f, 289.9f), new Vector3(-421.2f, -36.1f, 213.4f), new Vector3(-465.1f, -37.7f, 134.8f),
                    new Vector3(-508.3f, -37.9f, 55.8f), new Vector3(-551.3f, -37.3f, -23.3f), new Vector3(-595.2f, -36.4f, -101.8f),
                    new Vector3(-639.2f, -35.4f, -180.4f), new Vector3(-683.2f, -34.5f, -258.9f), new Vector3(-688.8f, -34.3f, -275.8f),
                    new Vector3(-687.4f, -34.2f, -293.5f), new Vector3(-677.9f, -34.1f, -308.4f), new Vector3(-663.9f, -34.1f, -319.5f),
                    new Vector3(-590.7f, -33.9f, -354.3f), new Vector3(-573.8f, -33.8f, -360.5f), new Vector3(-547.1f, -33.6f, -364.4f),
                    new Vector3(-529.1f, -33.4f, -364.1f), new Vector3(-503.2f, -33.0f, -356.8f), new Vector3(-486.6f, -32.8f, -350.0f),
                    new Vector3(-471.2f, -32.5f, -340.6f), new Vector3(-442.4f, -31.9f, -319.0f), new Vector3(-372.5f, -30.4f, -262.4f),
                    new Vector3(-303.8f, -29.0f, -204.2f), new Vector3(-233.9f, -28.1f, -147.5f), new Vector3(-205.8f, -28.0f, -124.9f),
                    new Vector3(-189.9f, -28.0f, -116.9f), new Vector3(-172.2f, -28.0f, -114.4f), new Vector3(-154.4f, -27.9f, -116.5f),
                    new Vector3(-137.0f, -27.8f, -121.4f), new Vector3(-120.4f, -27.7f, -128.2f), new Vector3(-105.2f, -27.6f, -137.8f),
                    new Vector3(-85.1f, -27.4f, -155.7f), new Vector3(-74.8f, -27.2f, -170.4f), new Vector3(-69.6f, -27.0f, -187.6f),
                    new Vector3(-68.5f, -26.9f, -205.5f), new Vector3(-70.3f, -26.3f, -259.4f), new Vector3(-73.7f, -25.8f, -295.3f),
                    new Vector3(-77.1f, -25.6f, -312.9f), new Vector3(-82.7f, -25.4f, -330.0f), new Vector3(-94.6f, -25.2f, -342.7f),
                    new Vector3(-112.2f, -25.0f, -344.4f), new Vector3(-127.8f, -24.9f, -335.8f), new Vector3(-144.8f, -24.6f, -314.9f),
                    new Vector3(-157.6f, -24.5f, -302.3f), new Vector3(-173.7f, -24.3f, -294.5f), new Vector3(-191.5f, -24.2f, -294.2f),
                    new Vector3(-208.0f, -24.1f, -301.3f), new Vector3(-220.3f, -24.1f, -314.2f), new Vector3(-227.2f, -24.0f, -330.7f),
                    new Vector3(-226.8f, -24.0f, -348.4f), new Vector3(-220.9f, -24.0f, -365.4f), new Vector3(-193.4f, -24.4f, -411.9f),
                    new Vector3(-181.0f, -24.8f, -435.9f), new Vector3(-174.9f, -25.1f, -452.8f), new Vector3(-172.5f, -25.4f, -470.5f),
                    new Vector3(-171.9f, -25.7f, -488.5f), new Vector3(-174.0f, -26.4f, -524.4f), new Vector3(-177.3f, -26.8f, -542.1f),
                    new Vector3(-186.8f, -27.2f, -556.9f), new Vector3(-203.4f, -27.5f, -561.3f), new Vector3(-218.2f, -27.9f, -551.9f),
                    new Vector3(-259.2f, -29.4f, -471.8f), new Vector3(-267.9f, -29.6f, -456.0f), new Vector3(-279.3f, -29.8f, -442.2f),
                    new Vector3(-293.5f, -29.9f, -431.1f), new Vector3(-309.6f, -30.0f, -423.2f), new Vector3(-326.7f, -30.0f, -417.5f),
                    new Vector3(-344.4f, -30.1f, -414.8f), new Vector3(-362.4f, -30.3f, -414.8f), new Vector3(-379.8f, -30.5f, -418.8f),
                    new Vector3(-396.4f, -30.9f, -425.7f), new Vector3(-411.4f, -31.3f, -435.5f), new Vector3(-458.2f, -33.1f, -477.7f),
                    new Vector3(-522.6f, -36.2f, -540.5f), new Vector3(-542.1f, -37.1f, -559.2f), new Vector3(-549.0f, -37.7f, -575.3f),
                    new Vector3(-544.5f, -38.2f, -592.3f), new Vector3(-532.1f, -38.6f, -605.2f), new Vector3(-517.7f, -39.1f, -615.9f),
                    new Vector3(-466.0f, -39.9f, -652.1f), new Vector3(-450.2f, -40.0f, -660.5f), new Vector3(-432.9f, -39.9f, -664.8f),
                    new Vector3(-397.1f, -39.2f, -668.6f), new Vector3(-352.1f, -37.6f, -670.5f), new Vector3(-334.2f, -36.7f, -669.5f),
                    new Vector3(-280.8f, -33.7f, -661.3f), new Vector3(-237.3f, -30.9f, -650.0f), new Vector3(-220.6f, -29.8f, -643.2f),
                    new Vector3(-196.8f, -28.2f, -630.6f), new Vector3(-182.1f, -27.1f, -620.2f), new Vector3(-169.0f, -26.1f, -607.8f),
                    new Vector3(-157.7f, -25.2f, -593.9f), new Vector3(-143.1f, -24.0f, -571.1f), new Vector3(-100.7f, -22.0f, -491.8f),
                    new Vector3(-82.4f, -20.9f, -450.7f), new Vector3(-39.7f, -15.0f, -371.5f), new Vector3(-26.3f, -12.9f, -348.0f),
                    new Vector3(-19.7f, -11.5f, -331.3f), new Vector3(-7.0f, -7.9f, -278.8f), new Vector3(-4.2f, -6.7f, -252.0f),
                    new Vector3(-2.6f, -5.1f, -162.0f), new Vector3(-0.9f, -1.6f, -72.0f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 90.1f), new Vector3(0.0f, 0.0f, 180.2f),
                    new Vector3(-0.2f, 0.0f, 243.3f), new Vector3(-6.2f, 0.0f, 259.8f), new Vector3(-19.6f, 0.0f, 271.5f),
                    new Vector3(-36.5f, 0.0f, 277.2f), new Vector3(-126.1f, 0.0f, 268.5f), new Vector3(-215.8f, 0.0f, 259.8f),
                    new Vector3(-233.5f, 0.0f, 256.2f), new Vector3(-249.9f, 0.0f, 249.0f), new Vector3(-263.4f, 0.0f, 237.2f),
                    new Vector3(-274.0f, 0.0f, 222.7f), new Vector3(-304.7f, 0.0f, 157.5f), new Vector3(-320.4f, 0.0f, 135.5f),
                    new Vector3(-332.6f, 0.0f, 122.3f), new Vector3(-346.6f, 0.0f, 110.9f), new Vector3(-369.5f, 0.0f, 96.6f),
                    new Vector3(-386.3f, 0.0f, 90.4f), new Vector3(-403.8f, 0.0f, 86.3f), new Vector3(-421.8f, 0.0f, 85.5f),
                    new Vector3(-439.6f, 0.0f, 87.6f), new Vector3(-465.4f, 0.0f, 95.7f), new Vector3(-548.4f, 0.0f, 130.9f),
                    new Vector3(-591.9f, 0.0f, 142.6f), new Vector3(-627.4f, 0.0f, 148.7f), new Vector3(-717.4f, 0.0f, 150.9f),
                    new Vector3(-807.5f, 0.0f, 152.0f), new Vector3(-897.6f, 0.0f, 152.5f), new Vector3(-915.6f, 0.0f, 152.2f),
                    new Vector3(-933.3f, 0.0f, 149.5f), new Vector3(-947.8f, 0.0f, 138.9f), new Vector3(-957.4f, 0.0f, 124.2f),
                    new Vector3(-957.6f, 0.0f, 106.5f), new Vector3(-948.1f, 0.0f, 91.8f), new Vector3(-933.2f, 0.0f, 81.7f),
                    new Vector3(-892.7f, 0.0f, 61.9f), new Vector3(-833.5f, 0.0f, 40.1f), new Vector3(-752.6f, 0.0f, 0.4f),
                    new Vector3(-671.8f, 0.0f, -39.5f), new Vector3(-591.0f, 0.0f, -79.4f), new Vector3(-510.2f, 0.0f, -119.3f),
                    new Vector3(-429.5f, 0.0f, -159.3f), new Vector3(-348.7f, 0.0f, -199.3f), new Vector3(-267.8f, 0.0f, -239.0f),
                    new Vector3(-185.6f, 0.0f, -275.8f), new Vector3(-103.6f, 0.0f, -313.2f), new Vector3(-22.1f, 0.0f, -351.6f),
                    new Vector3(58.9f, 0.0f, -391.1f), new Vector3(91.1f, 0.0f, -407.3f), new Vector3(108.0f, 0.0f, -412.1f),
                    new Vector3(117.3f, 0.0f, -399.1f), new Vector3(118.0f, 0.0f, -363.1f), new Vector3(124.3f, 0.0f, -347.9f),
                    new Vector3(141.5f, 0.0f, -343.8f), new Vector3(230.9f, 0.0f, -332.2f), new Vector3(265.8f, 0.0f, -323.3f),
                    new Vector3(291.2f, 0.0f, -314.2f), new Vector3(331.5f, 0.0f, -293.9f), new Vector3(369.4f, 0.0f, -269.6f),
                    new Vector3(410.1f, 0.0f, -234.0f), new Vector3(474.9f, 0.0f, -171.4f), new Vector3(539.7f, 0.0f, -108.8f),
                    new Vector3(598.3f, 0.0f, -52.7f), new Vector3(646.2f, 0.0f, 1.1f), new Vector3(673.9f, 0.0f, 36.6f),
                    new Vector3(722.9f, 0.0f, 112.2f), new Vector3(771.6f, 0.0f, 188.0f), new Vector3(794.7f, 0.0f, 226.7f),
                    new Vector3(798.3f, 0.0f, 244.2f), new Vector3(797.8f, 0.0f, 262.1f), new Vector3(792.2f, 0.0f, 279.1f),
                    new Vector3(782.7f, 0.0f, 294.3f), new Vector3(768.8f, 0.0f, 305.7f), new Vector3(752.8f, 0.0f, 313.9f),
                    new Vector3(735.5f, 0.0f, 318.4f), new Vector3(717.6f, 0.0f, 318.8f), new Vector3(699.8f, 0.0f, 315.6f),
                    new Vector3(683.1f, 0.0f, 309.2f), new Vector3(668.5f, 0.0f, 298.8f), new Vector3(656.2f, 0.0f, 285.6f),
                    new Vector3(646.9f, 0.0f, 270.3f), new Vector3(626.4f, 0.0f, 182.6f), new Vector3(607.1f, 0.0f, 94.5f),
                    new Vector3(598.2f, 0.0f, 59.6f), new Vector3(590.9f, 0.0f, 43.2f), new Vector3(533.3f, 0.0f, -26.1f),
                    new Vector3(521.8f, 0.0f, -40.0f), new Vector3(508.3f, 0.0f, -51.7f), new Vector3(491.3f, 0.0f, -57.3f),
                    new Vector3(473.5f, 0.0f, -59.4f), new Vector3(383.6f, 0.0f, -66.2f), new Vector3(371.2f, 0.0f, -55.2f),
                    new Vector3(369.4f, 0.0f, 25.8f), new Vector3(364.4f, 0.0f, 43.0f), new Vector3(351.3f, 0.0f, 55.2f),
                    new Vector3(334.3f, 0.0f, 60.4f), new Vector3(271.8f, 0.0f, 69.1f), new Vector3(254.4f, 0.0f, 66.2f),
                    new Vector3(242.1f, 0.0f, 53.8f), new Vector3(235.3f, 0.0f, 37.1f), new Vector3(231.0f, 0.0f, 19.6f),
                    new Vector3(228.6f, 0.0f, 1.8f), new Vector3(228.2f, 0.0f, -16.2f), new Vector3(229.8f, 0.0f, -106.3f),
                    new Vector3(230.9f, 0.0f, -169.4f), new Vector3(229.5f, 0.0f, -187.3f), new Vector3(225.9f, 0.0f, -205.0f),
                    new Vector3(219.8f, 0.0f, -221.9f), new Vector3(208.5f, 0.0f, -235.4f), new Vector3(193.2f, 0.0f, -244.6f),
                    new Vector3(109.2f, 0.0f, -277.3f), new Vector3(76.3f, 0.0f, -292.0f), new Vector3(58.9f, 0.0f, -296.5f),
                    new Vector3(40.9f, 0.0f, -297.2f), new Vector3(23.0f, 0.0f, -295.4f), new Vector3(7.7f, 0.0f, -287.0f),
                    new Vector3(2.5f, 0.0f, -270.3f), new Vector3(1.4f, 0.0f, -180.2f), new Vector3(0.7f, 0.0f, -90.1f)
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
                AnchorSubdivisions = 4,
                SketchAnchors = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 90.1f), new Vector3(0.0f, 0.0f, 180.2f),
                    new Vector3(0.0f, 0.0f, 270.3f), new Vector3(0.3f, 0.0f, 360.4f), new Vector3(1.4f, 0.0f, 450.5f),
                    new Vector3(2.5f, 0.0f, 540.6f), new Vector3(2.7f, 0.0f, 558.7f), new Vector3(13.8f, 0.0f, 570.2f),
                    new Vector3(30.3f, 0.0f, 564.5f), new Vector3(76.1f, 0.0f, 521.2f), new Vector3(92.2f, 0.0f, 514.0f),
                    new Vector3(109.9f, 0.0f, 517.0f), new Vector3(186.4f, 0.0f, 543.6f), new Vector3(204.3f, 0.0f, 545.8f),
                    new Vector3(222.3f, 0.0f, 544.9f), new Vector3(311.2f, 0.0f, 530.2f), new Vector3(400.1f, 0.0f, 515.5f),
                    new Vector3(489.0f, 0.0f, 500.9f), new Vector3(577.9f, 0.0f, 486.2f), new Vector3(666.8f, 0.0f, 471.6f),
                    new Vector3(746.8f, 0.0f, 458.0f), new Vector3(763.3f, 0.0f, 451.9f), new Vector3(772.2f, 0.0f, 437.1f),
                    new Vector3(771.5f, 0.0f, 419.3f), new Vector3(763.9f, 0.0f, 403.1f), new Vector3(752.3f, 0.0f, 389.4f),
                    new Vector3(682.3f, 0.0f, 332.7f), new Vector3(661.4f, 0.0f, 315.5f), new Vector3(606.6f, 0.0f, 255.8f),
                    new Vector3(596.9f, 0.0f, 240.8f), new Vector3(589.4f, 0.0f, 224.4f), new Vector3(572.3f, 0.0f, 182.7f),
                    new Vector3(561.1f, 0.0f, 168.9f), new Vector3(545.9f, 0.0f, 159.3f), new Vector3(528.6f, 0.0f, 155.0f),
                    new Vector3(510.8f, 0.0f, 155.9f), new Vector3(466.2f, 0.0f, 162.0f), new Vector3(448.2f, 0.0f, 160.6f),
                    new Vector3(431.0f, 0.0f, 155.6f), new Vector3(416.4f, 0.0f, 145.4f), new Vector3(404.0f, 0.0f, 132.4f),
                    new Vector3(350.1f, 0.0f, 60.1f), new Vector3(296.3f, 0.0f, -12.1f), new Vector3(283.5f, 0.0f, -24.7f),
                    new Vector3(266.1f, 0.0f, -26.4f), new Vector3(255.8f, 0.0f, -12.9f), new Vector3(253.6f, 0.0f, 4.9f),
                    new Vector3(254.0f, 0.0f, 22.9f), new Vector3(266.4f, 0.0f, 112.1f), new Vector3(278.6f, 0.0f, 201.4f),
                    new Vector3(289.8f, 0.0f, 290.8f), new Vector3(291.4f, 0.0f, 308.7f), new Vector3(288.7f, 0.0f, 326.5f),
                    new Vector3(283.7f, 0.0f, 343.7f), new Vector3(273.8f, 0.0f, 358.7f), new Vector3(224.6f, 0.0f, 398.2f),
                    new Vector3(210.3f, 0.0f, 393.4f), new Vector3(207.3f, 0.0f, 375.8f), new Vector3(197.8f, 0.0f, 286.2f),
                    new Vector3(195.2f, 0.0f, 259.3f), new Vector3(192.7f, 0.0f, 169.3f), new Vector3(190.4f, 0.0f, 79.2f),
                    new Vector3(188.6f, 0.0f, -10.9f), new Vector3(187.8f, 0.0f, -101.0f), new Vector3(186.8f, 0.0f, -191.1f),
                    new Vector3(185.6f, 0.0f, -281.2f), new Vector3(188.5f, 0.0f, -308.1f), new Vector3(198.9f, 0.0f, -322.7f),
                    new Vector3(215.2f, 0.0f, -330.2f), new Vector3(232.9f, 0.0f, -333.3f), new Vector3(250.8f, 0.0f, -331.9f),
                    new Vector3(277.3f, 0.0f, -326.5f), new Vector3(293.7f, 0.0f, -319.4f), new Vector3(309.7f, 0.0f, -311.0f),
                    new Vector3(331.3f, 0.0f, -294.8f), new Vector3(349.7f, 0.0f, -275.0f), new Vector3(359.6f, 0.0f, -260.1f),
                    new Vector3(367.8f, 0.0f, -244.1f), new Vector3(392.3f, 0.0f, -176.3f), new Vector3(404.2f, 0.0f, -152.1f),
                    new Vector3(415.4f, 0.0f, -137.9f), new Vector3(428.7f, 0.0f, -125.8f), new Vector3(443.2f, 0.0f, -115.2f),
                    new Vector3(458.7f, 0.0f, -106.4f), new Vector3(484.8f, 0.0f, -99.3f), new Vector3(511.8f, 0.0f, -98.1f),
                    new Vector3(529.6f, 0.0f, -100.4f), new Vector3(546.7f, 0.0f, -105.5f), new Vector3(628.7f, 0.0f, -143.0f),
                    new Vector3(653.2f, 0.0f, -154.3f), new Vector3(668.1f, 0.0f, -164.5f), new Vector3(681.5f, 0.0f, -176.5f),
                    new Vector3(691.6f, 0.0f, -191.3f), new Vector3(701.2f, 0.0f, -206.5f), new Vector3(700.2f, 0.0f, -224.0f),
                    new Vector3(690.5f, 0.0f, -238.9f), new Vector3(677.5f, 0.0f, -251.3f), new Vector3(663.4f, 0.0f, -262.6f),
                    new Vector3(586.1f, 0.0f, -308.9f), new Vector3(508.8f, 0.0f, -355.1f), new Vector3(431.4f, 0.0f, -401.3f),
                    new Vector3(354.0f, 0.0f, -447.5f), new Vector3(276.7f, 0.0f, -493.8f), new Vector3(199.3f, 0.0f, -540.0f),
                    new Vector3(122.0f, 0.0f, -586.2f), new Vector3(52.2f, 0.0f, -627.6f), new Vector3(35.0f, 0.0f, -631.4f),
                    new Vector3(20.4f, 0.0f, -623.6f), new Vector3(-7.9f, 0.0f, -567.3f), new Vector3(-11.0f, 0.0f, -540.4f),
                    new Vector3(-11.3f, 0.0f, -450.3f), new Vector3(-10.9f, 0.0f, -360.2f), new Vector3(-10.5f, 0.0f, -270.1f),
                    new Vector3(-7.2f, 0.0f, -180.1f), new Vector3(-3.6f, 0.0f, -90.0f)
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
