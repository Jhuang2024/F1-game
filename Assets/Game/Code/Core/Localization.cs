using System.Collections.Generic;

namespace F1Game.Core
{
    /// <summary>
    /// Minimal localization seam. User-facing strings are looked up by a stable
    /// key with an English fallback baked in at the call site, so the game reads
    /// exactly as before until a translation table is loaded - the fallback IS
    /// the source-of-truth English text. A loaded table overrides any key it
    /// carries; unknown keys always fall through to the caller's fallback, so
    /// partial translations degrade gracefully rather than showing blank or key
    /// text. Engine-free and allocation-light on the hot path (a dictionary
    /// lookup). The active-language table is set once at startup / on a language
    /// change; callers never cache the result.
    /// </summary>
    public static class Localization
    {
        static Dictionary<string, string> table;

        /// <summary>Currently loaded language code (empty = source English fallbacks).</summary>
        public static string Language { get; private set; } = "";

        /// <summary>Loads a translation table for a language; null/empty clears it.</summary>
        public static void Load(string language, Dictionary<string, string> translations)
        {
            table = translations;
            Language = translations == null ? "" : (language ?? "");
        }

        public static void Clear()
        {
            table = null;
            Language = "";
        }

        /// <summary>
        /// The localized string for a key, or the English <paramref name="fallback"/>
        /// when no table is loaded or the key is missing/blank.
        /// </summary>
        public static string Get(string key, string fallback)
        {
            if (table != null && !string.IsNullOrEmpty(key) &&
                table.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            return fallback;
        }
    }
}
