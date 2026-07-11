using System.Collections.Generic;
using F1Game.Race;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// BroadcastCameraSelector picks which fixed trackside camera covers the car.
    /// The distance band (min/max range), the ideal-distance preference, the
    /// hysteresis hold on the current camera, and the empty/out-of-range fallbacks
    /// are pinned here.
    /// </summary>
    public class BroadcastCameraSelectorTests
    {
        static List<BroadcastCameraSelector.CameraPoint> Cams(params (float x, float y, float z)[] pts)
        {
            var list = new List<BroadcastCameraSelector.CameraPoint>();
            foreach (var p in pts)
            {
                list.Add(new BroadcastCameraSelector.CameraPoint(p.x, p.y, p.z));
            }

            return list;
        }

        [Test]
        public void NoCamerasReturnsMinusOne()
        {
            Assert.AreEqual(-1, BroadcastCameraSelector.SelectCamera(null, 0f, 0f, 0f, currentIndex: -1));
            Assert.AreEqual(-1, BroadcastCameraSelector.SelectCamera(Cams(), 0f, 0f, 0f, currentIndex: -1));
        }

        [Test]
        public void PicksCameraNearestTheIdealFramingDistance()
        {
            // Cam 0 is 20m away (too tight), cam 1 is ~60m (ideal), cam 2 is 150m (wide).
            var cams = Cams((20f, 0f, 0f), (60f, 0f, 0f), (150f, 0f, 0f));
            Assert.AreEqual(1, BroadcastCameraSelector.SelectCamera(cams, 0f, 0f, 0f, currentIndex: -1));
        }

        [Test]
        public void CamerasOutsideTheRangeBandAreIgnored()
        {
            // Cam 0 is 4m (below min 8), cam 1 is 300m (beyond max 170); only cam 2 (70m) is usable.
            var cams = Cams((4f, 0f, 0f), (300f, 0f, 0f), (70f, 0f, 0f));
            Assert.AreEqual(2, BroadcastCameraSelector.SelectCamera(cams, 0f, 0f, 0f, currentIndex: -1));
        }

        [Test]
        public void HoldsTheCurrentCameraWithinTheSwitchMargin()
        {
            // Current cam 0 at 66m (cost 6 vs ideal 60); rival cam 1 at 60m (cost 0).
            // Difference 6 <= default margin 14, so the live cam is held (no flicker cut).
            var cams = Cams((66f, 0f, 0f), (60f, 0f, 0f));
            Assert.AreEqual(0, BroadcastCameraSelector.SelectCamera(cams, 0f, 0f, 0f, currentIndex: 0));
        }

        [Test]
        public void SwitchesWhenARivalIsClearlyBetter()
        {
            // Current cam 0 at 120m (cost 60); rival cam 1 at 60m (cost 0). 60 > margin 14 -> cut.
            var cams = Cams((120f, 0f, 0f), (60f, 0f, 0f));
            Assert.AreEqual(1, BroadcastCameraSelector.SelectCamera(cams, 0f, 0f, 0f, currentIndex: 0));
        }

        [Test]
        public void HoldsTheCurrentCameraWhenNothingIsInRange()
        {
            // Both cameras are beyond max range; the live cam 1 is held rather than cutting to black.
            var cams = Cams((400f, 0f, 0f), (500f, 0f, 0f));
            Assert.AreEqual(1, BroadcastCameraSelector.SelectCamera(cams, 0f, 0f, 0f, currentIndex: 1));
            // With no valid current camera either, report "no camera".
            Assert.AreEqual(-1, BroadcastCameraSelector.SelectCamera(cams, 0f, 0f, 0f, currentIndex: -1));
        }
    }
}
