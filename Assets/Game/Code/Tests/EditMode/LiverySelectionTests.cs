using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// LiverySelection must round-trip each selection form (preset id, custom storage,
    /// generated marker) and fall back to a distinct generated livery for empty or
    /// unrecognized selections, so a stored choice always resolves to a paintable
    /// livery. Pinned here.
    /// </summary>
    public class LiverySelectionTests
    {
        [Test]
        public void ResolvesANamedPreset()
        {
            CarLivery livery = LiverySelection.Resolve(LiverySelection.ForPreset("azure"), 0, 20);
            Assert.AreEqual("#0072C6", livery.Primary.ToHex());
        }

        [Test]
        public void RoundTripsACustomLivery()
        {
            var custom = new CarLivery(
                new LiveryColor(10, 20, 30),
                new LiveryColor(40, 50, 60),
                new LiveryColor(70, 80, 90));
            CarLivery back = LiverySelection.Resolve(LiverySelection.ForCustom(custom), 0, 20);
            Assert.AreEqual(custom.Primary.ToHex(), back.Primary.ToHex());
            Assert.AreEqual(custom.Secondary.ToHex(), back.Secondary.ToHex());
            Assert.AreEqual(custom.Accent.ToHex(), back.Accent.ToHex());
        }

        [Test]
        public void ResolvesAGeneratedMarkerToTheSameLivery()
        {
            CarLivery direct = LiveryGenerator.Generate(3, 20);
            CarLivery resolved = LiverySelection.Resolve(LiverySelection.ForGenerated(3, 20), 0, 20);
            Assert.AreEqual(direct.Primary.ToHex(), resolved.Primary.ToHex());
        }

        [Test]
        public void EmptySelectionFallsBackToTheGeneratedSlot()
        {
            CarLivery fallback = LiveryGenerator.Generate(5, 20);
            CarLivery resolved = LiverySelection.Resolve("", 5, 20);
            Assert.AreEqual(fallback.Primary.ToHex(), resolved.Primary.ToHex());
        }

        [Test]
        public void UnknownPresetAndMalformedMarkerFallBack()
        {
            CarLivery fallback = LiveryGenerator.Generate(2, 20);
            Assert.AreEqual(fallback.Primary.ToHex(),
                LiverySelection.Resolve("no-such-preset", 2, 20).Primary.ToHex());
            Assert.AreEqual(fallback.Primary.ToHex(),
                LiverySelection.Resolve("generated:oops", 2, 20).Primary.ToHex());
        }
    }
}
