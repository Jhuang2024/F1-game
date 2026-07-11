using System.Collections.Generic;
using F1Game.Track;
using NUnit.Framework;
using UnityEngine;

namespace F1Game.Tests
{
    /// <summary>
    /// Static validation of every authored circuit definition: the whole
    /// calendar loads through the catalog, so a malformed definition would
    /// break a playable circuit. These checks run without a scene.
    /// </summary>
    public class AuthoredCircuitCatalogTests
    {
        static readonly string[] CalendarTrackIds =
        {
            "melbourne_park", "china_suzuka_technical", "suzuka_figure_eight",
            "bahrain_desert", "jeddah_fast_street", "miami_park_street",
            "canada_stop_go", "monaco_tight_street", "barcelona_flowing",
            "austria_hillside", "silverstone_high_speed", "spa_flowing",
            "hungary_technical", "zandvoort_coastal", "monza_low_downforce",
            "madrid_hybrid_street", "baku_fast_street", "singapore_night",
            "austin_rollercoaster", "mexico_high_altitude",
            "interlagos_short_flowing", "las_vegas_street", "qatar_high_speed",
            "abu_dhabi_finale",
        };

        [Test]
        public void EveryCalendarCircuitIsInTheCatalog()
        {
            foreach (string trackId in CalendarTrackIds)
            {
                Assert.IsTrue(AuthoredCircuitCatalog.Contains(trackId), trackId + " missing from catalog");
            }

            Assert.IsTrue(AuthoredCircuitCatalog.Contains(ReferenceTrackGenerator.ReferenceTrackId));
            Assert.IsFalse(AuthoredCircuitCatalog.Contains("not_a_real_circuit"));
            Assert.IsNull(AuthoredCircuitCatalog.Generate("not_a_real_circuit"));
        }

        [Test]
        public void EveryDefinitionIsStructurallySound()
        {
            var allIds = new List<string>(CalendarTrackIds) { ReferenceTrackGenerator.ReferenceTrackId };
            foreach (string trackId in allIds)
            {
                TrackDefinitionAsset definition = AuthoredCircuitCatalog.Generate(trackId);
                Assert.IsNotNull(definition, trackId);
                try
                {
                    Assert.AreEqual(trackId, definition.trackId, trackId);
                    Assert.GreaterOrEqual(definition.spline.Count, 8, trackId + " spline too coarse");
                    Assert.IsTrue(definition.closedLoop, trackId);

                    float length = definition.ComputeLength();
                    Assert.Greater(length, 2000f, trackId + " implausibly short");
                    Assert.Less(length, 12000f, trackId + " implausibly long");

                    foreach (TrackDefinitionAsset.SplinePoint point in definition.spline)
                    {
                        Assert.Greater(point.width, 8f, trackId + " road too narrow");
                        Assert.Less(point.width, 60f, trackId + " road too wide");
                    }

                    Assert.AreEqual(2, definition.drsZones.Count, trackId + " DRS zone count");
                    foreach (TrackDefinitionAsset.DrsZone zone in definition.drsZones)
                    {
                        Assert.GreaterOrEqual(zone.activationDistance, 0f, trackId);
                        Assert.LessOrEqual(zone.activationDistance, length, trackId);
                        Assert.GreaterOrEqual(zone.endDistance, 0f, trackId);
                        Assert.LessOrEqual(zone.endDistance, length, trackId);
                    }

                    Assert.AreEqual(2, definition.sectorBoundaryDistances.Length, trackId);
                    Assert.Greater(definition.sectorBoundaryDistances[1], definition.sectorBoundaryDistances[0], trackId);

                    Assert.AreEqual(22, definition.gridSlots.Count, trackId + " grid slots");
                    Assert.AreEqual(22, definition.pitLane.stallCount, trackId + " pit stalls");
                    Assert.AreEqual(definition.spline.Count, definition.racingLineOffsets.Count, trackId + " racing-line offsets");
                }
                finally
                {
                    Object.DestroyImmediate(definition);
                }
            }
        }
    }
}
