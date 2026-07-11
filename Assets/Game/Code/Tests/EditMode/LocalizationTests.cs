using System.Collections.Generic;
using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// The localization seam must be invisible until a table is loaded (English
    /// fallbacks are the source of truth) and must degrade gracefully on partial
    /// tables. Pinned here.
    /// </summary>
    public class LocalizationTests
    {
        [TearDown]
        public void Reset()
        {
            Localization.Clear();
        }

        [Test]
        public void FallsBackToEnglishWhenNoTableLoaded()
        {
            Assert.AreEqual("GAMEPLAY", Localization.Get("settings.heading.gameplay", "GAMEPLAY"));
            Assert.AreEqual("", Localization.Language);
        }

        [Test]
        public void LoadedTableOverridesKnownKeys()
        {
            Localization.Load("fr", new Dictionary<string, string> { { "settings.heading.gameplay", "JEU" } });
            Assert.AreEqual("JEU", Localization.Get("settings.heading.gameplay", "GAMEPLAY"));
            Assert.AreEqual("fr", Localization.Language);
        }

        [Test]
        public void UnknownAndBlankKeysDegradeToFallback()
        {
            Localization.Load("fr", new Dictionary<string, string> { { "known", "connu" }, { "blank", "" } });
            Assert.AreEqual("fallback", Localization.Get("missing.key", "fallback"));
            Assert.AreEqual("fallback", Localization.Get("blank", "fallback"));
            Assert.AreEqual("connu", Localization.Get("known", "x"));
        }

        [Test]
        public void ClearRestoresEnglishFallbacks()
        {
            Localization.Load("fr", new Dictionary<string, string> { { "k", "v" } });
            Localization.Clear();
            Assert.AreEqual("english", Localization.Get("k", "english"));
            Assert.AreEqual("", Localization.Language);
        }
    }
}
