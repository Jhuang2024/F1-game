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
            Localization.StopRecording();
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

        [Test]
        public void ParseReadsKeyValueLinesAndSkipsCommentsAndBlanks()
        {
            string doc = "# a comment\n\nsettings.title = Réglages \n button.back=Retour\nmalformed line\n=novalue\n";
            Dictionary<string, string> table = Localization.Parse(doc);
            Assert.AreEqual(2, table.Count);
            Assert.AreEqual("Réglages", table["settings.title"]);   // trimmed
            Assert.AreEqual("Retour", table["button.back"]);
            Assert.IsFalse(table.ContainsKey("malformed line"));
            Assert.IsFalse(table.ContainsKey(""));
        }

        [Test]
        public void ParseKeepsInnerSpacesAndLaterDuplicatesWin()
        {
            Dictionary<string, string> table = Localization.Parse("k=first value\nk=second value");
            Assert.AreEqual("second value", table["k"]);
        }

        [Test]
        public void LoadFromTextMakesEntriesLive()
        {
            Localization.LoadFromText("de", "hud.lap=RUNDE");
            Assert.AreEqual("RUNDE", Localization.Get("hud.lap", "LAP"));
            Assert.AreEqual("de", Localization.Language);
        }

        [Test]
        public void MissingKeysReportsUncoveredRequiredKeys()
        {
            Localization.Load("fr", new Dictionary<string, string> { { "a", "A" }, { "b", "" } });
            var missing = Localization.MissingKeys(new[] { "a", "b", "c" });
            // "a" is covered; "b" is blank; "c" is absent.
            CollectionAssert.AreEquivalent(new[] { "b", "c" }, missing);
        }

        [Test]
        public void MissingKeysReportsAllWhenNoTableLoaded()
        {
            var missing = Localization.MissingKeys(new[] { "a", "b" });
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, missing);
        }

        [Test]
        public void RecordingHarvestsRequestedKeysWithEnglishSource()
        {
            Localization.StartRecording();
            Assert.IsTrue(Localization.IsRecording);
            Localization.Get("b.key", "Beta");
            Localization.Get("a.key", "Alpha");
            Localization.Get("b.key", "IGNORED SECOND FALLBACK"); // first-seen wins

            // Exported template is sorted by key with the English source as value.
            Assert.AreEqual("a.key=Alpha\nb.key=Beta\n", Localization.ExportRecordedTemplate());
        }

        [Test]
        public void StopRecordingEndsHarvestAndClears()
        {
            Localization.StartRecording();
            Localization.Get("k", "V");
            Localization.StopRecording();
            Assert.IsFalse(Localization.IsRecording);
            Assert.AreEqual("", Localization.ExportRecordedTemplate());
        }

        [Test]
        public void RecordingRoundTripsThroughParse()
        {
            Localization.StartRecording();
            Localization.Get("screen.title", "SETTINGS");
            string template = Localization.ExportRecordedTemplate();
            Localization.StopRecording();

            Dictionary<string, string> parsed = Localization.Parse(template);
            Assert.AreEqual("SETTINGS", parsed["screen.title"]);
        }

        [Test]
        public void GetFormatFillsPlaceholdersFromTheResolvedTemplate()
        {
            Localization.Load("de", new Dictionary<string, string> { { "lap.counter", "RUNDE {0}/{1}" } });
            Assert.AreEqual("RUNDE 3/58", Localization.GetFormat("lap.counter", "LAP {0}/{1}", 3, 58));
            // Missing key -> the English fallback format is used.
            Assert.AreEqual("LAP 3/58", Localization.GetFormat("lap.missing", "LAP {0}/{1}", 3, 58));
        }

        [Test]
        public void GetFormatDegradesToTheTemplateOnAMalformedFormat()
        {
            // A bad translation ("{0" - unclosed placeholder) must not throw.
            Localization.Load("xx", new Dictionary<string, string> { { "bad.key", "VALUE {0" } });
            Assert.AreEqual("VALUE {0", Localization.GetFormat("bad.key", "VALUE {0}", 5));
        }

        [Test]
        public void LanguageChangedFiresOnLoadAndClear()
        {
            int count = 0;
            System.Action handler = () => count++;
            Localization.LanguageChanged += handler;
            try
            {
                Localization.Load("fr", new Dictionary<string, string> { { "k", "v" } });
                Localization.Clear();
                Assert.AreEqual(2, count);
            }
            finally
            {
                Localization.LanguageChanged -= handler;
            }
        }

        [Test]
        public void PseudolocalizeExpandsAndPreservesFormatPlaceholders()
        {
            string pseudo = Localization.Pseudolocalize("LAP {0}/{1}");
            // Brackets wrap it, and the {0}/{1} placeholders survive verbatim for GetFormat.
            StringAssert.StartsWith("⟦", pseudo);
            StringAssert.EndsWith("⟧", pseudo);
            StringAssert.Contains("{0}", pseudo);
            StringAssert.Contains("{1}", pseudo);
            // It is meaningfully longer than the source (truncation-catching pad).
            Assert.Greater(pseudo.Length, "LAP {0}/{1}".Length);
        }

        [Test]
        public void LoadPseudolocaleTranslatesEveryValueAndStaysFormattable()
        {
            var english = new Dictionary<string, string>
            {
                { "lap.counter", "LAP {0}/{1}" },
                { "screen.title", "SETTINGS" },
            };
            Localization.LoadPseudolocale(english);
            Assert.AreEqual("qps-ploc", Localization.Language);
            // Placeholders still fill through the pseudo template.
            StringAssert.Contains("3/58", Localization.GetFormat("lap.counter", "LAP {0}/{1}", 3, 58));
            // A plain value is pseudo-translated (no longer the raw English).
            Assert.AreNotEqual("SETTINGS", Localization.Get("screen.title", "SETTINGS"));
        }
    }
}
