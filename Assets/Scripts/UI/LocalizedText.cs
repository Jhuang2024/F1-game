using F1Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Binds a <see cref="Text"/> to a localization key so it shows the localized
    /// string (or the English fallback) and re-fetches automatically when the active
    /// language changes (<see cref="Localization.LanguageChanged"/>). The fallback is
    /// captured from the Text's authored value the first time it binds, so existing
    /// UI reads exactly as before until a translation table is loaded - the component
    /// is transparent on the untranslated path.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [SerializeField] string key;
        [SerializeField] string fallback;
        [SerializeField] bool fallbackCaptured;

        Text target;

        /// <summary>Attach + bind a key in code (captures the label's current text as the fallback).</summary>
        public static LocalizedText Bind(Text text, string localizationKey)
        {
            if (text == null)
            {
                return null;
            }

            var lt = text.GetComponent<LocalizedText>();
            if (lt == null)
            {
                lt = text.gameObject.AddComponent<LocalizedText>();
            }

            lt.SetKey(localizationKey);
            return lt;
        }

        public void SetKey(string localizationKey)
        {
            EnsureTarget();
            key = localizationKey;
            if (!fallbackCaptured)
            {
                fallback = target != null ? target.text : "";
                fallbackCaptured = true;
            }

            Apply();
        }

        void EnsureTarget()
        {
            if (target == null)
            {
                target = GetComponent<Text>();
            }
        }

        void OnEnable()
        {
            EnsureTarget();
            if (!fallbackCaptured)
            {
                // Bound in the editor: the authored text is the English source of truth.
                fallback = target != null ? target.text : "";
                fallbackCaptured = true;
            }

            Localization.LanguageChanged += Apply;
            Apply();
        }

        void OnDisable()
        {
            Localization.LanguageChanged -= Apply;
        }

        void Apply()
        {
            EnsureTarget();
            if (target == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            target.text = Localization.Get(key, fallback);
        }
    }
}
