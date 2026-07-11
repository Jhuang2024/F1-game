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

        /// <summary>
        /// Raised after the active language table changes (Load / LoadFromText /
        /// LoadPseudolocale / Clear). Runtime binders (e.g. a LocalizedText component)
        /// subscribe to re-fetch their strings so a language switch updates live UI
        /// without a scene reload. System.Action keeps this engine-free.
        /// </summary>
        public static event System.Action LanguageChanged;

        /// <summary>Loads a translation table for a language; null/empty clears it.</summary>
        public static void Load(string language, Dictionary<string, string> translations)
        {
            table = translations;
            Language = translations == null ? "" : (language ?? "");
            LanguageChanged?.Invoke();
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
            LanguageChanged?.Invoke();
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

        /// <summary>
        /// Like <see cref="Get"/> but the resolved template is a composite format
        /// string filled with <paramref name="args"/> under the invariant culture
        /// (e.g. "lap.counter" -&gt; "LAP {0}/{1}"). A malformed template (bad
        /// placeholder) degrades to the resolved template unformatted rather than
        /// throwing, so a bad translation can never crash the UI.
        /// </summary>
        public static string GetFormat(string key, string fallbackFormat, params object[] args)
        {
            string template = Get(key, fallbackFormat);
            if (args == null || args.Length == 0)
            {
                return template;
            }

            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, template ?? "", args);
            }
            catch (System.FormatException)
            {
                return template;
            }
        }

        /// <summary>
        /// Loads a synthetic pseudo-locale generated from a set of source
        /// English key=value entries (the harvested authoring template): every value
        /// is transformed by <see cref="Pseudolocalize"/> - accented, bracketed and
        /// padded - so the game can be run "translated" before real translations
        /// exist. This is the standard localization QA tool: it makes any
        /// un-externalized (hard-coded) string obvious because it stays plain, and it
        /// surfaces layouts that will truncate once strings get ~40% longer. Clearly
        /// a PLACEHOLDER, not a shipping translation.
        /// </summary>
        public static void LoadPseudolocale(Dictionary<string, string> englishSource)
        {
            var pseudo = new Dictionary<string, string>();
            if (englishSource != null)
            {
                foreach (KeyValuePair<string, string> entry in englishSource)
                {
                    if (!string.IsNullOrEmpty(entry.Key))
                    {
                        pseudo[entry.Key] = Pseudolocalize(entry.Value);
                    }
                }
            }

            Load("qps-ploc", pseudo);
        }

        /// <summary>
        /// Standard pseudo-localization of a source string: maps ASCII letters to
        /// accented look-alikes (still readable), wraps the result in brackets, and
        /// pads it ~40% longer with dots to expose truncation. Text inside
        /// <c>{...}</c> format placeholders is left untouched so
        /// <see cref="GetFormat"/> still works. Deterministic and engine-free.
        /// </summary>
        public static string Pseudolocalize(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            var sb = new System.Text.StringBuilder(source.Length + 8);
            sb.Append('⟦'); // heavy left bracket, visually distinct from any real text
            int letters = 0;
            bool inPlaceholder = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '{')
                {
                    inPlaceholder = true;
                }
                else if (c == '}')
                {
                    inPlaceholder = false;
                }

                if (inPlaceholder || c == '}')
                {
                    sb.Append(c);
                    continue;
                }

                sb.Append(Accent(c));
                if (char.IsLetter(c))
                {
                    letters++;
                }
            }

            // ~40% expansion (rounded up), the industry rule-of-thumb growth budget.
            int pad = (letters * 2 + 4) / 5;
            for (int i = 0; i < pad; i++)
            {
                sb.Append('·'); // middle dot
            }

            sb.Append('⟧'); // heavy right bracket
            return sb.ToString();
        }

        static char Accent(char c)
        {
            switch (c)
            {
                case 'a': return 'å';
                case 'e': return 'é';
                case 'i': return 'î';
                case 'o': return 'ø';
                case 'u': return 'ü';
                case 'A': return 'Å';
                case 'E': return 'É';
                case 'I': return 'Î';
                case 'O': return 'Ø';
                case 'U': return 'Ü';
                default: return c;
            }
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
