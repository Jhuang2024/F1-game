using System.Collections.Generic;
using F1Game.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1Game.UI.Screens.PreRaceStrategy
{
    /// <summary>
    /// A procedurally-drawn tyre-life chart for one strategy: a race-length
    /// timeline of compound-coloured columns whose height is the tyre's remaining
    /// life at the start of each lap, so each stint reads as a life curve falling
    /// from full to the cliff and resetting to full at every pit stop. Under it,
    /// a per-stint axis (compound + stint length) and, in the header, the strategy
    /// title, its stop/compound summary and an estimated total race time. Built and
    /// re-rendered entirely in code (there is no chart prefab), driven by
    /// <see cref="StrategyChartModel"/>.
    /// </summary>
    public class TyreLifeChartView : MonoBehaviour
    {
        RectTransform chartBand;
        RectTransform axisRow;
        Image accentBar;
        TMP_Text titleText;
        TMP_Text subtitleText;
        TMP_Text totalTimeText;
        readonly List<GameObject> spawned = new List<GameObject>();

        static readonly Color CriticalLife = new Color(0.85f, 0.12f, 0.10f);

        static Color CompoundColor(int compoundIndex)
        {
            switch (compoundIndex)
            {
                case 0: return new Color(1f, 0.20f, 0.16f);   // Soft - red
                case 1: return new Color(1f, 0.86f, 0.20f);   // Medium - yellow
                case 2: return new Color(0.92f, 0.94f, 0.96f); // Hard - white
                case 3: return new Color(0.22f, 0.88f, 0.34f); // Inter - green
                default: return new Color(0.26f, 0.52f, 1f);   // Wet - blue
            }
        }

        static RectTransform MakeRect(Transform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = aMin;
            rect.anchorMax = aMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        static Image MakeImage(Transform parent, string name, Vector2 aMin, Vector2 aMax, Color color)
        {
            RectTransform rect = MakeRect(parent, name, aMin, aMax);
            var img = rect.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// Builds an empty chart card under <paramref name="parent"/> and returns
        /// the view. The card is a single layout child (fixed preferred height); its
        /// internals are anchored fractionally so it adapts to whatever width the
        /// surrounding layout gives it.
        /// </summary>
        public static TyreLifeChartView Build(Transform parent, string name, float cardHeight = 210f)
        {
            UiTheme theme = UiTheme.Active;

            var cardGo = new GameObject(name, typeof(RectTransform));
            cardGo.transform.SetParent(parent, false);
            var cardBg = cardGo.AddComponent<Image>();
            cardBg.color = theme.palette.surface;
            cardBg.raycastTarget = false;
            var layoutElement = cardGo.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = cardHeight;
            layoutElement.flexibleWidth = 1f;

            var view = cardGo.AddComponent<TyreLifeChartView>();

            // Left accent stripe marks the recommended strategy.
            view.accentBar = MakeImage(cardGo.transform, "Accent", new Vector2(0f, 0f), new Vector2(0.006f, 1f), theme.palette.accent);

            // Header band: title + summary on the left, total time on the right.
            view.titleText = MakeLabel(cardGo.transform, "Title", new Vector2(0.02f, 0.83f), new Vector2(0.62f, 0.99f),
                UiScreenFactory.TextStyle.Label, TextAlignmentOptions.BottomLeft, theme.palette.textPrimary);
            view.subtitleText = MakeLabel(cardGo.transform, "Subtitle", new Vector2(0.02f, 0.70f), new Vector2(0.62f, 0.83f),
                UiScreenFactory.TextStyle.Caption, TextAlignmentOptions.TopLeft, theme.palette.textMuted);
            view.totalTimeText = MakeLabel(cardGo.transform, "TotalTime", new Vector2(0.5f, 0.70f), new Vector2(0.98f, 0.99f),
                UiScreenFactory.TextStyle.H3, TextAlignmentOptions.Right, theme.palette.textPrimary);

            // Chart band: faint backdrop + a 50%/100% guide, columns drawn on top.
            view.chartBand = MakeRect(cardGo.transform, "ChartBand", new Vector2(0.02f, 0.20f), new Vector2(0.98f, 0.66f));
            MakeImage(view.chartBand, "Backdrop", Vector2.zero, Vector2.one, new Color(1f, 1f, 1f, 0.03f));
            MakeImage(view.chartBand, "GuideMid", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Color(1f, 1f, 1f, 0.06f))
                .rectTransform.sizeDelta = new Vector2(0f, 1f);

            view.axisRow = MakeRect(cardGo.transform, "AxisRow", new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.19f));

            return view;
        }

        static TMP_Text MakeLabel(Transform parent, string name, Vector2 aMin, Vector2 aMax,
            UiScreenFactory.TextStyle style, TextAlignmentOptions align, Color color)
        {
            TMP_Text t = UiScreenFactory.CreateText(parent, name, style, "");
            RectTransform rect = t.rectTransform;
            rect.anchorMin = aMin;
            rect.anchorMax = aMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public void Render(StrategyChartModel model)
        {
            UiTheme theme = UiTheme.Active;

            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null)
                {
                    Destroy(spawned[i]);
                }
            }

            spawned.Clear();

            if (titleText != null) titleText.text = model.title;
            if (subtitleText != null) subtitleText.text = model.subtitle;
            if (totalTimeText != null)
            {
                totalTimeText.text = model.totalTime;
                totalTimeText.color = model.isRecommended ? theme.palette.positive : theme.palette.textPrimary;
            }

            if (accentBar != null)
            {
                accentBar.color = model.isRecommended ? theme.palette.positive : theme.palette.outline;
            }

            int laps = model.raceLaps > 0 ? model.raceLaps : 1;
            float lapFrac = 1f / laps;

            int lapCursor = 0;
            for (int s = 0; s < model.stints.Count; s++)
            {
                StintPlan stint = model.stints[s];
                Color baseColor = CompoundColor(stint.compoundIndex);
                float life = stint.expectedLife > 0.1f ? stint.expectedLife : 1f;

                for (int j = 0; j < stint.laps; j++)
                {
                    int g = lapCursor + j;
                    if (g >= laps)
                    {
                        break;
                    }

                    // Remaining life at the START of this lap; a stint driven past
                    // its life drops toward the cliff (near-zero, deep red).
                    float remaining = Mathf.Clamp01(1f - j / life);
                    float height = Mathf.Lerp(0.06f, 1f, remaining);
                    Color col = Color.Lerp(baseColor, CriticalLife, (1f - remaining) * 0.65f);

                    float x0 = g * lapFrac;
                    float x1 = (g + 1) * lapFrac;
                    Image bar = MakeImage(chartBand, "Lap" + (g + 1),
                        new Vector2(x0 + lapFrac * 0.08f, 0f),
                        new Vector2(x1 - lapFrac * 0.08f, height), col);
                    spawned.Add(bar.gameObject);
                }

                lapCursor += stint.laps;

                // Pit divider at the end of each stint but the last.
                if (s < model.stints.Count - 1 && lapCursor < laps)
                {
                    float x = lapCursor * lapFrac;
                    Image div = MakeImage(chartBand, "Pit" + lapCursor,
                        new Vector2(x, 0f), new Vector2(x, 1f), theme.palette.accent);
                    div.rectTransform.sizeDelta = new Vector2(2f, 0f);
                    spawned.Add(div.gameObject);
                }

                // Axis label centred under the stint: compound + stint length.
                float centre = (lapCursor - stint.laps * 0.5f) * lapFrac;
                float halfW = Mathf.Max(0.12f, stint.laps * lapFrac * 0.5f);
                TMP_Text label = MakeLabel(axisRow, "Stint" + s,
                    new Vector2(Mathf.Clamp01(centre - halfW), 0f),
                    new Vector2(Mathf.Clamp01(centre + halfW), 1f),
                    UiScreenFactory.TextStyle.Caption, TextAlignmentOptions.Center, baseColor);
                label.text = stint.compoundShort + " · " + stint.laps + "L";
                spawned.Add(label.gameObject);
            }
        }
    }
}
