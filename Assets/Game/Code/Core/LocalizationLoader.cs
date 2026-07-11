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

        /// <summary>The English source-of-truth template (also the pseudo-locale source).</summary>
        public const string SourceLanguage = "en";

        /// <summary>Synthetic pseudo-locale code (generated from the English template for QA).</summary>
        public const string PseudoLanguage = "qps-ploc";

        /// <summary>
        /// Loads the given language's table from Resources; returns true when a
        /// table was actually applied. English / missing files clear to fallbacks;
        /// the pseudo-locale is generated on the fly from the English template.
        /// </summary>
        public static bool LoadLanguage(string language)
        {
            if (string.IsNullOrEmpty(language) || language == SourceLanguage)
            {
                Localization.Clear();
                return false;
            }

            if (language == PseudoLanguage)
            {
                return LoadPseudolocale();
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

        /// <summary>
        /// Builds and activates the pseudo-locale from the English template
        /// (<c>Resources/Localization/en.txt</c>). Clears to fallbacks when the
        /// template is missing. Standard localization QA aid; clearly a placeholder.
        /// </summary>
        public static bool LoadPseudolocale()
        {
            TextAsset source = Resources.Load<TextAsset>(ResourceFolder + "/" + SourceLanguage);
            if (source == null)
            {
                Localization.Clear();
                return false;
            }

            Localization.LoadPseudolocale(Localization.Parse(source.text));
            return true;
        }
    }
}
