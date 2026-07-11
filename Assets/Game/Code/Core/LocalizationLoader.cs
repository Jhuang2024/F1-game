using UnityEngine;

namespace F1Game.Core
{
    /// <summary>
    /// Runtime loader for translation tables, kept separate from the pure
    /// <see cref="Localization"/> lookup so that class stays engine-free and
    /// unit-testable. Loads <c>Resources/Localization/&lt;language&gt;.txt</c>
    /// (a key=value document) into the active table. Safe by construction: an
    /// empty/"en" language or a missing file clears the table, so the game falls
    /// back to its English source text - dropping in a translation file is the
    /// only step needed to enable a language, and nothing breaks without one.
    /// </summary>
    public static class LocalizationLoader
    {
        public const string ResourceFolder = "Localization";

        /// <summary>
        /// Loads the given language's table from Resources; returns true when a
        /// table was actually applied. English / missing files clear to fallbacks.
        /// </summary>
        public static bool LoadLanguage(string language)
        {
            if (string.IsNullOrEmpty(language) || language == "en")
            {
                Localization.Clear();
                return false;
            }

            TextAsset asset = Resources.Load<TextAsset>(ResourceFolder + "/" + language);
            if (asset == null)
            {
                Localization.Clear();
                return false;
            }

            Localization.LoadFromText(language, asset.text);
            return true;
        }
    }
}
