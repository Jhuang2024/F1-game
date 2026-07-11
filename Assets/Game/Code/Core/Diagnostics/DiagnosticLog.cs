namespace F1Game.Core.Diagnostics
{
    /// <summary>Diagnostic log category - the subsystem a line came from.</summary>
    public enum LogCategory
    {
        General,
        Race,
        Ai,
        Rules,
        Pit,
        Save,
        Physics,
        Track,
        Ui,
        Audio,
        Network,
    }

    /// <summary>
    /// Structured error codes for the crash/diagnostics package. Stable numeric
    /// identity so a report references a code rather than a free-text string.
    /// </summary>
    public enum DiagnosticCode
    {
        None = 0,
        SaveLoadFailed = 1001,
        SaveWriteFailed = 1002,
        SaveCorrupt = 1003,
        TrackBuildFailed = 2001,
        TrackDefinitionUnusable = 2002,
        RaceStartFailed = 3001,
        HudBindFailed = 4001,
        AudioBankMissing = 5001,
    }

    /// <summary>
    /// Pure formatting + gating for structured diagnostics, engine-free so it is
    /// testable without Unity. The Assembly-CSharp <c>GameLog</c> delegates its
    /// category-aware overloads here; the actual Debug.Log call stays there.
    /// Category enabling: General always on (build/session summaries), the rest
    /// gated by <see cref="VerboseCategories"/> so release builds stay quiet
    /// while a diagnostics session can opt specific subsystems in.
    /// </summary>
    public static class DiagnosticLog
    {
        /// <summary>Bit mask of categories emitted even when global verbose is off.</summary>
        public static int VerboseCategories;

        public static void Enable(LogCategory category, bool on)
        {
            int bit = 1 << (int)category;
            if (on)
            {
                VerboseCategories |= bit;
            }
            else
            {
                VerboseCategories &= ~bit;
            }
        }

        public static bool IsEnabled(LogCategory category, bool globalVerbose)
        {
            if (category == LogCategory.General || globalVerbose)
            {
                return true;
            }

            return (VerboseCategories & (1 << (int)category)) != 0;
        }

        /// <summary>Uppercase tag for a category, e.g. "[AI]".</summary>
        public static string Tag(LogCategory category)
        {
            switch (category)
            {
                case LogCategory.Race: return "[RACE]";
                case LogCategory.Ai: return "[AI]";
                case LogCategory.Rules: return "[RULES]";
                case LogCategory.Pit: return "[PIT]";
                case LogCategory.Save: return "[SAVE]";
                case LogCategory.Physics: return "[PHYS]";
                case LogCategory.Track: return "[TRACK]";
                case LogCategory.Ui: return "[UI]";
                case LogCategory.Audio: return "[AUDIO]";
                case LogCategory.Network: return "[NET]";
                default: return "[GAME]";
            }
        }

        public static string Format(LogCategory category, string message)
        {
            return Tag(category) + " " + (message ?? "");
        }

        /// <summary>Error line with a stable code, always worth printing.</summary>
        public static string FormatError(DiagnosticCode code, string message)
        {
            return "[ERR:" + ((int)code).ToString() + "] " + Tag(LogCategoryFor(code)) + " " + (message ?? "");
        }

        static LogCategory LogCategoryFor(DiagnosticCode code)
        {
            switch (code)
            {
                case DiagnosticCode.SaveLoadFailed:
                case DiagnosticCode.SaveWriteFailed:
                case DiagnosticCode.SaveCorrupt:
                    return LogCategory.Save;
                case DiagnosticCode.TrackBuildFailed:
                case DiagnosticCode.TrackDefinitionUnusable:
                    return LogCategory.Track;
                case DiagnosticCode.RaceStartFailed:
                    return LogCategory.Race;
                case DiagnosticCode.HudBindFailed:
                    return LogCategory.Ui;
                case DiagnosticCode.AudioBankMissing:
                    return LogCategory.Audio;
                default:
                    return LogCategory.General;
            }
        }
    }
}
