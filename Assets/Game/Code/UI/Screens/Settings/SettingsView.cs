using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.Settings
{
    /// <summary>
    /// Production settings screen. Presents the live game settings as a read-only
    /// summary (pooled TMP rows, category headings + label/value lines), with a
    /// "Classic Settings" button that hands editing back to the legacy screen -
    /// production-first display, legacy fallback for editing until the inline
    /// controls are built and validated. Pure presentation; the presenter owns
    /// the button actions.
    /// </summary>
    public class SettingsView : ScreenView
    {
        public const string Id = "settings";

        [SerializeField] TMP_Text header;
        [SerializeField] RectTransform rowContainer;
        [SerializeField] TMP_Text rowTemplate;
        [SerializeField] ThemedButton unitsButton;
        [SerializeField] ThemedButton cameraShakeButton;
        [SerializeField] ThemedButton compactHudButton;
        [SerializeField] ThemedButton uiAnimationsButton;
        [SerializeField] ThemedButton classicButton;
        [SerializeField] ThemedButton backButton;

        readonly List<TMP_Text> rows = new List<TMP_Text>();

        public ThemedButton UnitsButton => unitsButton;
        public ThemedButton CameraShakeButton => cameraShakeButton;
        public ThemedButton CompactHudButton => compactHudButton;
        public ThemedButton UiAnimationsButton => uiAnimationsButton;
        public ThemedButton ClassicButton => classicButton;
        public ThemedButton BackButton => backButton;

        public void Bind(TMP_Text headerText, RectTransform container, TMP_Text template,
            ThemedButton units, ThemedButton cameraShake, ThemedButton compactHud, ThemedButton uiAnimations,
            ThemedButton classic, ThemedButton back)
        {
            header = headerText;
            rowContainer = container;
            rowTemplate = template;
            unitsButton = units;
            cameraShakeButton = cameraShake;
            compactHudButton = compactHud;
            uiAnimationsButton = uiAnimations;
            classicButton = classic;
            backButton = back;
            rowTemplate.gameObject.SetActive(false);
            SetScreenId(Id);
            SetDefaultSelection(back != null ? back.gameObject : null);
        }

        /// <summary>Labels for the inline accessibility quick-toggle buttons.</summary>
        public void RenderToggles(string units, string cameraShake, string compactHud, string uiAnimations)
        {
            if (unitsButton != null) unitsButton.SetText(units);
            if (cameraShakeButton != null) cameraShakeButton.SetText(cameraShake);
            if (compactHudButton != null) compactHudButton.SetText(compactHud);
            if (uiAnimationsButton != null) uiAnimationsButton.SetText(uiAnimations);
        }

        public void RenderRows(IReadOnlyList<SettingsRowModel> models)
        {
            EnsureRowCount(models.Count);
            string accent = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.accent);
            string muted = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.textMuted);
            for (int i = 0; i < models.Count; i++)
            {
                SettingsRowModel model = models[i];
                TMP_Text row = rows[i];
                row.gameObject.SetActive(true);
                if (model.isHeading)
                {
                    row.text = "<color=#" + accent + "><b>" + (model.label ?? "") + "</b></color>";
                }
                else
                {
                    row.text = string.Format(
                        "{0}   <align=right><color=#{1}>{2}</color></align>",
                        model.label,
                        muted,
                        string.IsNullOrEmpty(model.value) ? "-" : model.value);
                }
            }

            for (int i = models.Count; i < rows.Count; i++)
            {
                rows[i].gameObject.SetActive(false);
            }
        }

        void EnsureRowCount(int count)
        {
            while (rows.Count < count)
            {
                TMP_Text row = Instantiate(rowTemplate, rowContainer);
                rows.Add(row);
            }
        }
    }
}
