using UnityEngine;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Runtime control surface for the <see cref="CinematicDirector"/>: a compact
    /// button bar (Live / Replay / Spectator / Broadcast / Photo) plus a current-mode
    /// label. Pressing a mode calls into the director, which owns the actual camera
    /// lifecycle and guarantees a single active mode; this HUD only sends intents and
    /// reflects the resulting mode. Built with the existing UiFactory idiom.
    ///
    /// Default-off: it shares the director's opt-in switch
    /// (<see cref="CinematicDirector.EnabledKey"/>) and is only created alongside a
    /// director, so it never touches the live race HUD.
    /// VISUAL VALIDATION PENDING (in-editor run).
    /// </summary>
    public sealed class CinematicHud : MonoBehaviour
    {
        static readonly CinematicDirector.Mode[] Modes =
        {
            CinematicDirector.Mode.Live,
            CinematicDirector.Mode.Replay,
            CinematicDirector.Mode.Spectator,
            CinematicDirector.Mode.Broadcast,
            CinematicDirector.Mode.Photo,
        };

        CinematicDirector director;
        Text modeLabel;

        /// <summary>
        /// Build the control bar under <paramref name="parent"/> (a HUD canvas) bound
        /// to <paramref name="director"/>. Returns null when the cinematic feature is
        /// off or inputs are missing, so callers stay on the live HUD unchanged.
        /// </summary>
        public static CinematicHud Create(Transform parent, CinematicDirector director)
        {
            if (parent == null || director == null || !CinematicDirector.FeatureEnabled)
            {
                return null;
            }

            var go = new GameObject("Cinematic HUD");
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<CinematicHud>();
            hud.director = director;
            hud.Build(go.transform);
            return hud;
        }

        void Build(Transform root)
        {
            // Top-centre bar spanning the mode buttons plus the label.
            RectTransform bar = UiFactory.CreateGlassPanel(
                root, "Cinematic Bar",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-430f, -74f), new Vector2(430f, -30f),
                UiFactory.PanelDarker);
            bar.pivot = new Vector2(0.5f, 1f);

            float x = 14f;
            for (int i = 0; i < Modes.Length; i++)
            {
                CinematicDirector.Mode mode = Modes[i];
                Button button = UiFactory.CreateSecondaryButton(bar, mode.ToString(), () => Select(mode));
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(120f, 36f);
                rect.anchoredPosition = new Vector2(x, 0f);
                x += 128f;
            }

            modeLabel = UiFactory.CreateText(bar, "Mode", "LIVE", 16, UiFactory.AccentCyan, TextAnchor.MiddleRight);
            RectTransform labelRect = modeLabel.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(1f, 0.5f);
            labelRect.anchorMax = new Vector2(1f, 0.5f);
            labelRect.pivot = new Vector2(1f, 0.5f);
            labelRect.sizeDelta = new Vector2(150f, 30f);
            labelRect.anchoredPosition = new Vector2(-16f, 0f);

            RefreshLabel();
        }

        void Select(CinematicDirector.Mode mode)
        {
            if (director == null)
            {
                return;
            }

            director.SetMode(mode);
            RefreshLabel();
        }

        void Update()
        {
            // The director can change mode on its own (e.g. photo exit), so keep the
            // label in sync each frame - cheap, and the bar is only up in cinematic use.
            RefreshLabel();
        }

        void RefreshLabel()
        {
            if (modeLabel != null && director != null)
            {
                modeLabel.text = director.CurrentMode.ToString().ToUpperInvariant();
            }
        }
    }
}
