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
        static Dictionary<string, string> recorded;

        /// <summary>Currently loaded language code (empty = source English fallbacks).</summary>
        public static string Language { get; private set; } = "";

        /// <summary>True while key harvesting is active (see StartRecording).</summary>
        public static bool IsRecording => recorded != null;

        /// <summary>Loads a translation table for a language; null/empty clears it.</summary>
        public static void Load(string language, Dictionary<string, string> translations)
        {
            table = translations;
            Language = translations == null ? "" : (language ?? "");
        }

        /// <summary>
        /// Parses and loads a translation table from a simple <c>key=value</c> text
        /// document (one entry per line; blank lines and <c>#</c> comments ignored).
        /// A loader (e.g. a Resources TextAsset reader) hands the raw text here.
        /// </summary>
        public static void LoadFromText(string language, string content)
        {
            Load(language, Parse(content));
        }

        public static void Clear()
        {
            table = null;
            Language = "";
        }

        /// <summary>
        /// Parses a <c>key=value</c> document into a table. Lines are trimmed;
        /// blank lines and lines beginning with <c>#</c> are skipped; the key is
        /// everything before the first <c>=</c> (trimmed), the value everything
        /// after (leading/trailing space trimmed, inner spaces kept); malformed
        /// lines (no <c>=</c> or empty key) are ignored. Later duplicate keys win.
        /// </summary>
        public static Dictionary<string, string> Parse(string content)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(content))
            {
                return result;
            }

            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                result[key] = line.Substring(eq + 1).Trim();
            }

            return result;
        }

        /// <summary>
        /// Validation helper for tooling: returns the required keys that the
        /// currently loaded table is missing or maps to a blank value. An empty
        /// result means full coverage of the required set. With no table loaded,
        /// every required key is reported missing.
        /// </summary>
        public static List<string> MissingKeys(IEnumerable<string> requiredKeys)
        {
            var missing = new List<string>();
            if (requiredKeys == null)
            {
                return missing;
            }

            foreach (string key in requiredKeys)
            {
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (table == null || !table.TryGetValue(key, out string value) || string.IsNullOrEmpty(value))
                {
                    missing.Add(key);
                }
            }

            return missing;
        }

        /// <summary>
        /// The localized string for a key, or the English <paramref name="fallback"/>
        /// when no table is loaded or the key is missing/blank.
        /// </summary>
        public static string Get(string key, string fallback)
        {
            // Key harvesting (authoring): record each key with its English source
            // the first time it's requested, so a play-through can export a
            // complete translation template - including runtime-derived keys
            // (button.<slug>, settings.row.<slug>) that no source scan would find.
            if (recorded != null && !string.IsNullOrEmpty(key) && !recorded.ContainsKey(key))
            {
                recorded[key] = fallback ?? "";
            }

            if (table != null && !string.IsNullOrEmpty(key) &&
                table.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            return fallback;
        }

        /// <summary>Begins harvesting requested keys + their English source for a template.</summary>
        public static void StartRecording()
        {
            recorded = new Dictionary<string, string>();
        }

        /// <summary>Stops harvesting; the collected keys remain available via ExportRecordedTemplate.</summary>
        public static void StopRecording()
        {
            recorded = null;
        }

        /// <summary>
        /// A key=value template document of every key seen since StartRecording,
        /// sorted by key with the English source as the value - the starting point
        /// for a translation file. Empty when nothing was recorded.
        /// </summary>
        public static string ExportRecordedTemplate()
        {
            if (recorded == null || recorded.Count == 0)
            {
                return "";
            }

            var keys = new List<string>(recorded.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(keys[i]).Append('=').Append(recorded[keys[i]]).Append('\n');
            }

            return sb.ToString();
        }
    }
}
