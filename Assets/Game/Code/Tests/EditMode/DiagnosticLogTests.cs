using F1Game.Core.Diagnostics;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class DiagnosticLogTests
    {
        [SetUp]
        public void Reset()
        {
            DiagnosticLog.VerboseCategories = 0;
        }

        [Test]
        public void GeneralAlwaysEnabledOthersGated()
        {
            // General prints regardless of verbose or category mask.
            Assert.IsTrue(DiagnosticLog.IsEnabled(LogCategory.General, globalVerbose: false));

            // A non-general category is off until verbose or its bit is set.
            Assert.IsFalse(DiagnosticLog.IsEnabled(LogCategory.Ai, globalVerbose: false));
            Assert.IsTrue(DiagnosticLog.IsEnabled(LogCategory.Ai, globalVerbose: true));

            DiagnosticLog.Enable(LogCategory.Ai, true);
            Assert.IsTrue(DiagnosticLog.IsEnabled(LogCategory.Ai, globalVerbose: false));
            // Enabling one category does not enable another.
            Assert.IsFalse(DiagnosticLog.IsEnabled(LogCategory.Pit, globalVerbose: false));

            DiagnosticLog.Enable(LogCategory.Ai, false);
            Assert.IsFalse(DiagnosticLog.IsEnabled(LogCategory.Ai, globalVerbose: false));
        }

        [Test]
        public void FormatPrefixesTheCategoryTag()
        {
            Assert.AreEqual("[AI] car spun", DiagnosticLog.Format(LogCategory.Ai, "car spun"));
            Assert.AreEqual("[PIT] ", DiagnosticLog.Format(LogCategory.Pit, ""));
            Assert.AreEqual("[GAME] boot", DiagnosticLog.Format(LogCategory.General, "boot"));
        }

        [Test]
        public void ErrorFormatCarriesTheStableCodeAndCategory()
        {
            string line = DiagnosticLog.FormatError(DiagnosticCode.SaveCorrupt, "bad file");
            StringAssert.StartsWith("[ERR:1003]", line);
            StringAssert.Contains("[SAVE]", line);
            StringAssert.Contains("bad file", line);

            // Codes route to their owning category tag.
            StringAssert.Contains("[TRACK]", DiagnosticLog.FormatError(DiagnosticCode.TrackBuildFailed, "x"));
            StringAssert.Contains("[RACE]", DiagnosticLog.FormatError(DiagnosticCode.RaceStartFailed, "x"));
            StringAssert.Contains("[UI]", DiagnosticLog.FormatError(DiagnosticCode.HudBindFailed, "x"));
            StringAssert.Contains("[UI]", DiagnosticLog.FormatError(DiagnosticCode.UiScreenMissing, "x"));
            StringAssert.Contains("[AUDIO]", DiagnosticLog.FormatError(DiagnosticCode.AudioBankMissing, "x"));
            StringAssert.Contains("[INPUT]", DiagnosticLog.FormatError(DiagnosticCode.InputActionsMissing, "x"));
            StringAssert.StartsWith("[ERR:4002]", DiagnosticLog.FormatError(DiagnosticCode.UiScreenMissing, "x"));
            StringAssert.StartsWith("[ERR:6001]", DiagnosticLog.FormatError(DiagnosticCode.InputActionsMissing, "x"));
        }

        [Test]
        public void InputCategoryTagAndGating()
        {
            Assert.AreEqual("[INPUT] rebind saved", DiagnosticLog.Format(LogCategory.Input, "rebind saved"));
            Assert.IsFalse(DiagnosticLog.IsEnabled(LogCategory.Input, globalVerbose: false));
            Assert.IsTrue(DiagnosticLog.IsEnabled(LogCategory.Input, globalVerbose: true));
        }
    }
}
