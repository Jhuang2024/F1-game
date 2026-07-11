using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// Pins the layered engine-audio mix curve (off-throttle attenuation, RPM
    /// crossfade weight, layer volume, pitch) exactly as EngineAudioLayers.Tick drove
    /// it, so a future refactor can't silently change the engine's feel.
    /// </summary>
    public class EngineAudioMixTests
    {
        [Test]
        public void OffThrottleInvertsClampedThrottle()
        {
            Assert.AreEqual(0f, EngineAudioMix.OffThrottle(1f), 1e-6f);
            Assert.AreEqual(1f, EngineAudioMix.OffThrottle(0f), 1e-6f);
            Assert.AreEqual(0.25f, EngineAudioMix.OffThrottle(0.75f), 1e-6f);
            // Out-of-range throttle is clamped first.
            Assert.AreEqual(0f, EngineAudioMix.OffThrottle(2f), 1e-6f);
            Assert.AreEqual(1f, EngineAudioMix.OffThrottle(-1f), 1e-6f);
        }

        [Test]
        public void LayerWeightPeaksAtTheBandCentreAndFadesWithLayerCount()
        {
            // At the centre, full weight regardless of layer count.
            Assert.AreEqual(1f, EngineAudioMix.LayerWeight(0.5f, 0.5f, 4), 1e-6f);
            // 4 layers -> a 0.25 distance reaches zero weight (1 - 0.25*4).
            Assert.AreEqual(0f, EngineAudioMix.LayerWeight(0.75f, 0.5f, 4), 1e-6f);
            // Halfway there is half weight.
            Assert.AreEqual(0.5f, EngineAudioMix.LayerWeight(0.625f, 0.5f, 4), 1e-6f);
            // Beyond the band it clamps at zero (never negative).
            Assert.AreEqual(0f, EngineAudioMix.LayerWeight(1f, 0.5f, 4), 1e-6f);
        }

        [Test]
        public void LayerVolumeCombinesWeightAttenuationAndMaster()
        {
            // Full throttle (offThrottle 0): attenuation is 1, so volume = weight*master.
            Assert.AreEqual(0.4f, EngineAudioMix.LayerVolume(0.5f, 0f, 0.8f, 0.8f), 1e-6f);
            // Fully lifted (offThrottle 1) with 0.5 attenuation coefficient halves it.
            Assert.AreEqual(0.5f * (1f - 0.5f) * 1f, EngineAudioMix.LayerVolume(0.5f, 1f, 0.5f, 1f), 1e-6f);
        }

        [Test]
        public void LayerPitchRunsFromLowToHighAcrossTheRpmRange()
        {
            Assert.AreEqual(0.85f, EngineAudioMix.LayerPitch(0f, 1f), 1e-6f);
            Assert.AreEqual(1.25f, EngineAudioMix.LayerPitch(1f, 1f), 1e-6f);
            Assert.AreEqual(1.05f, EngineAudioMix.LayerPitch(0.5f, 1f), 1e-6f);
            // Scaled by the external factor.
            Assert.AreEqual(1.05f * 1.1f, EngineAudioMix.LayerPitch(0.5f, 1.1f), 1e-5f);
        }
    }
}
