using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// The livery model must round-trip hex and storage exactly, reject malformed
    /// input by degrading to a fallback (never a blank car), tell distinct liveries
    /// apart, and expose retrievable placeholder presets. Pinned here.
    /// </summary>
    public class CarLiveryTests
    {
        [Test]
        public void HexParsesAndRoundTripsBothForms()
        {
            Assert.IsTrue(LiveryColor.TryParseHex("#E10600", out LiveryColor withHash));
            Assert.IsTrue(LiveryColor.TryParseHex("e10600", out LiveryColor noHash));
            Assert.AreEqual(0xE1, withHash.R);
            Assert.AreEqual(0x06, withHash.G);
            Assert.AreEqual(0x00, withHash.B);
            Assert.AreEqual(withHash.R, noHash.R);
            Assert.AreEqual("#E10600", withHash.ToHex());
        }

        [Test]
        public void MalformedHexIsRejected()
        {
            Assert.IsFalse(LiveryColor.TryParseHex(null, out _));
            Assert.IsFalse(LiveryColor.TryParseHex("", out _));
            Assert.IsFalse(LiveryColor.TryParseHex("#12345", out _));   // too short
            Assert.IsFalse(LiveryColor.TryParseHex("#GG0000", out _));  // non-hex
            Assert.IsFalse(LiveryColor.TryParseHex("1234567", out _));  // too long
        }

        [Test]
        public void StorageRoundTripsExactly()
        {
            var livery = new CarLivery(
                new LiveryColor(225, 6, 0),
                new LiveryColor(26, 26, 26),
                new LiveryColor(255, 255, 255));
            string stored = livery.ToStorage();
            Assert.AreEqual("#E10600,#1A1A1A,#FFFFFF", stored);

            CarLivery back = CarLivery.FromStorage(stored, LiveryPresets.Default);
            Assert.AreEqual(livery.Primary.R, back.Primary.R);
            Assert.AreEqual(livery.Secondary.G, back.Secondary.G);
            Assert.AreEqual(livery.Accent.B, back.Accent.B);
        }

        [Test]
        public void FromStorageFallsBackOnGarbage()
        {
            CarLivery fallback = LiveryPresets.Default;
            Assert.AreSame(fallback, CarLivery.FromStorage(null, fallback));
            Assert.AreSame(fallback, CarLivery.FromStorage("not,a,livery", fallback));
            Assert.AreSame(fallback, CarLivery.FromStorage("#E10600,#1A1A1A", fallback)); // only two
        }

        [Test]
        public void AreDistinctSeparatesSimilarFromDifferentPrimaries()
        {
            var red = new CarLivery(new LiveryColor(225, 6, 0), new LiveryColor(0, 0, 0), new LiveryColor(255, 255, 255));
            var nearRed = new CarLivery(new LiveryColor(230, 10, 5), new LiveryColor(0, 0, 0), new LiveryColor(255, 255, 255));
            var blue = new CarLivery(new LiveryColor(0, 114, 198), new LiveryColor(0, 0, 0), new LiveryColor(255, 255, 255));
            Assert.IsFalse(CarLivery.AreDistinct(red, nearRed));
            Assert.IsTrue(CarLivery.AreDistinct(red, blue));
        }

        [Test]
        public void PresetsAreRetrievableAndDefaultIsStable()
        {
            Assert.Greater(LiveryPresets.All.Count, 0);
            Assert.IsTrue(LiveryPresets.TryGet("azure", out CarLivery azure));
            Assert.AreEqual("#0072C6", azure.Primary.ToHex());
            // Unknown id degrades to the default rather than failing.
            Assert.IsFalse(LiveryPresets.TryGet("does-not-exist", out CarLivery fallback));
            Assert.AreEqual(LiveryPresets.Default.Primary.ToHex(), fallback.Primary.ToHex());
        }
    }
}
