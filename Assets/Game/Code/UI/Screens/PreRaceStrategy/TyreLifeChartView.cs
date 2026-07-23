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

            var stints = model.stints;
            int laps = model.raceLaps > 0 ? model.raceLaps : 1;
            if (stints == null || stints.Count == 0)
            {
                return;
            }

            // FILLED AREA. Sub-sample the whole race into thin adjacent columns
            // (many per lap, so a short race still reads smooth) and fill each from
            // the baseline up to the tyre's remaining life at that point. Life falls
            // linearly across a stint from 100% and resets to full at the next stint,
            // so the top edge of the fill IS the compound-coloured life curve - the
            // sawtooth the reference screen shows. A faint dotted line traces the
            // grip warming up within each stint.
            int cols = Mathf.Clamp(laps * 12, 96, 320);
            int dotEvery = Mathf.Max(1, cols / Mathf.Max(1, laps * 2));
            for (int c = 0; c < cols; c++)
            {
                float lapPos = (c + 0.5f) / cols * laps;

                // Which stint this column falls in, and how far into it.
                int si = stints.Count - 1;
                float stintStartLap = 0f;
                float acc = 0f;
                for (int s = 0; s < stints.Count; s++)
                {
                    float end = acc + stints[s].laps;
                    if (lapPos < end || s == stints.Count - 1)
                    {
                        si = s;
                        stintStartLap = acc;
                        break;
                    }

                    acc = end;
                }

                StintPlan st = stints[si];
                float life = st.expectedLife > 0.1f ? st.expectedLife : 1f;
                float within = lapPos - stintStartLap;
                float remaining = Mathf.Clamp01(1f - within / life);

                float x0 = (float)c / cols;
                float x1 = (float)(c + 1) / cols;
                Image bar = MakeImage(chartBand, "col" + c,
                    new Vector2(x0, 0f), new Vector2(x1, Mathf.Max(0.04f, remaining)),
                    CompoundColor(st.compoundIndex));
                spawned.Add(bar.gameObject);

                if (c % dotEvery == 0)
                {
                    float grip = 0.32f + 0.30f * Mathf.Clamp01(within / (0.55f * life));
                    float mid = (x0 + x1) * 0.5f;
                    Image dot = MakeImage(chartBand, "grip" + c,
                        new Vector2(mid, grip), new Vector2(mid, grip), new Color(1f, 1f, 1f, 0.8f));
                    dot.rectTransform.sizeDelta = new Vector2(3f, 3f);
                    spawned.Add(dot.gameObject);
                }
            }

            // Pit dividers, cumulative pit times, pit-lap numbers, stint labels.
            float cum = 0f;
            for (int s = 0; s < stints.Count; s++)
            {
                StintPlan st = stints[s];
                float startFrac = cum / laps;
                cum += st.laps;
                float endFrac = cum / laps;

                float centre = (startFrac + endFrac) * 0.5f;
                float halfW = Mathf.Max(0.1f, (endFrac - startFrac) * 0.5f);
                TMP_Text label = MakeLabel(axisRow, "Stint" + s,
                    new Vector2(Mathf.Clamp01(centre - halfW), 0f),
                    new Vector2(Mathf.Clamp01(centre + halfW), 1f),
                    UiScreenFactory.TextStyle.Caption, TextAlignmentOptions.Center, CompoundColor(st.compoundIndex));
                label.text = st.compoundShort + " · " + st.laps + "L";
                spawned.Add(label.gameObject);

                if (s < stints.Count - 1)
                {
                    Image div = MakeImage(chartBand, "Pit" + s,
                        new Vector2(endFrac, 0f), new Vector2(endFrac, 1f), new Color(1f, 1f, 1f, 0.55f));
                    div.rectTransform.sizeDelta = new Vector2(2f, 0f);
                    spawned.Add(div.gameObject);

                    if (!string.IsNullOrEmpty(st.pitTimeLabel))
                    {
                        TMP_Text tm = MakeLabel(chartBand, "PitTime" + s,
                            new Vector2(Mathf.Clamp01(endFrac - 0.17f), 0.8f), new Vector2(Mathf.Clamp01(endFrac + 0.17f), 1f),
                            UiScreenFactory.TextStyle.Caption, TextAlignmentOptions.Center, theme.palette.textPrimary);
                        tm.text = st.pitTimeLabel;
                        spawned.Add(tm.gameObject);
                    }

                    TMP_Text pitLap = MakeLabel(axisRow, "PitLap" + s,
                        new Vector2(Mathf.Clamp01(endFrac - 0.09f), 0f), new Vector2(Mathf.Clamp01(endFrac + 0.09f), 1f),
                        UiScreenFactory.TextStyle.Caption, TextAlignmentOptions.Center, theme.palette.textMuted);
                    pitLap.text = Mathf.RoundToInt(cum).ToString();
                    spawned.Add(pitLap.gameObject);
                }
            }
        }
    }
}
