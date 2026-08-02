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
                    // Was a bare ">= 8" while TrackDefinitionAsset.Validate() demanded
                    // 16 - two different "valid" standards, with the weaker one being
                    // what the suite actually enforced and Validate() never called at
                    // all. Run the real validator instead.
                    Assert.GreaterOrEqual(definition.spline.Count, TrackDefinitionAsset.MinimumSplinePoints, trackId + " spline too coarse");
                    List<string> problems = definition.Validate();
                    Assert.IsEmpty(problems, trackId + " failed Validate(): " + string.Join("; ", problems));
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

                    // Zone one is authored as a WRAPPING window on every circuit
                    // (e.g. Monza 0.88 -> 0.08), so activation > end is expected and
                    // legal. What must hold is that the runtime treats it as wrapping
                    // rather than as an unsatisfiable range - the previous assertions
                    // only bounds-checked the two endpoints, which is why zone one
                    // silently never activated on any circuit. Probe the runtime.
                    var drs = new DrsRuntime(definition.drsZones);
                    foreach (TrackDefinitionAsset.DrsZone zone in definition.drsZones)
                    {
                        float insideStart = zone.activationDistance + 1f;
                        float insideEnd = Mathf.Max(0f, zone.endDistance - 1f);
                        Assert.GreaterOrEqual(drs.ZoneIndexAt(insideStart), 0,
                            trackId + " DRS zone not active just after its activation point");
                        Assert.GreaterOrEqual(drs.ZoneIndexAt(insideEnd), 0,
                            trackId + " DRS zone not active just before its end point");
                    }

                    Assert.AreEqual(2, definition.sectorBoundaryDistances.Length, trackId);
                    Assert.Greater(definition.sectorBoundaryDistances[1], definition.sectorBoundaryDistances[0], trackId);

                    Assert.AreEqual(22, definition.gridSlots.Count, trackId + " grid slots");
                    // Pole must be AHEAD of the back of the grid, and the whole grid
                    // must sit behind the start/finish line. The suite previously only
                    // counted the slots, which is why a generator that ran the grid
                    // forwards from the line - putting pole 168m behind P22 - passed.
                    var sampler = new TrackSplineSampler();
                    sampler.Build(definition.spline, true);
                    float poleDistance = sampler.NearestDistance(definition.gridSlots[0].position);
                    float backDistance = sampler.NearestDistance(definition.gridSlots[21].position);
                    Assert.Greater(poleDistance, backDistance, trackId + " pole must start ahead of the last slot");
                    Assert.Greater(poleDistance, sampler.Length * 0.5f, trackId + " grid must form up behind the line");

                    // The racing line must actually be a line, not the centreline.
                    bool anyOffset = false;
                    for (int i = 0; i < definition.racingLineOffsets.Count; i++)
                    {
                        if (Mathf.Abs(definition.racingLineOffsets[i]) > 0.25f)
                        {
                            anyOffset = true;
                            break;
                        }
                    }

                    Assert.IsTrue(anyOffset, trackId + " racing line is flat - AI would have no apex to aim at");
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
