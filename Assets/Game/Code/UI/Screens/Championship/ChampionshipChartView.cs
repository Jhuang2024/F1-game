using System;
using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1Game.UI.Screens.Championship
{
    /// <summary>
    /// Production championship-graph screen. Renders each series as a polyline of
    /// rotated segment images inside a fixed plot rect, mapping the engine-free
    /// normalized [0,1] chart coordinates to pixels; draws y-tick gridlines/labels,
    /// x round labels and a colour-coded legend; and toggles between the drivers' and
    /// constructors' championships. Display only - the bridge supplies the model built
    /// from the authoritative career progression; no standings are recomputed here.
    /// </summary>
    public class ChampionshipChartView : ScreenView
    {
        public const string Id = "championship-chart";

        const float PlotWidth = 900f;
        const float PlotHeight = 330f;
        const float LineThickness = 3f;
        const float GridThickness = 1f;

        [SerializeField] TMP_Text titleLabel;
        [SerializeField] ThemedButton driversButton;
        [SerializeField] ThemedButton constructorsButton;
        [SerializeField] RectTransform plotRect;
        [SerializeField] RectTransform yLabelContainer;
        [SerializeField] RectTransform xLabelContainer;
        [SerializeField] RectTransform legendContainer;
        [SerializeField] TMP_Text emptyLabel;
        [SerializeField] TMP_Text legendTemplate;
        [SerializeField] ThemedButton backButton;

        readonly List<Image> segmentPool = new List<Image>();
        readonly List<Image> gridPool = new List<Image>();
        readonly List<TMP_Text> yLabelPool = new List<TMP_Text>();
        readonly List<TMP_Text> xLabelPool = new List<TMP_Text>();
        readonly List<TMP_Text> legendPool = new List<TMP_Text>();

        public event Action<bool> ModeSelected; // true = constructors
        public event Action BackClicked;

        public void Bind(TMP_Text title, ThemedButton drivers, ThemedButton constructors, RectTransform plot,
            RectTransform yLabels, RectTransform xLabels, RectTransform legend, TMP_Text empty, TMP_Text legendRow,
            ThemedButton back)
        {
            titleLabel = title;
            driversButton = drivers;
            constructorsButton = constructors;
            plotRect = plot;
            yLabelContainer = yLabels;
            xLabelContainer = xLabels;
            legendContainer = legend;
            emptyLabel = empty;
            legendTemplate = legendRow;
            backButton = back;

            if (legendTemplate != null)
            {
                legendTemplate.gameObject.SetActive(false);
            }

            if (driversButton != null)
            {
                driversButton.Clicked += () => ModeSelected?.Invoke(false);
            }

            if (constructorsButton != null)
            {
                constructorsButton.Clicked += () => ModeSelected?.Invoke(true);
            }

            if (backButton != null)
            {
                backButton.Clicked += () => BackClicked?.Invoke();
            }

            SetScreenId(Id);
            SetDefaultSelection(back != null ? back.gameObject : null);
        }

        public void Render(ChampionshipChartModel model)
        {
            ChampionshipChartModel m = model ?? new ChampionshipChartModel();

            if (titleLabel != null)
            {
                titleLabel.text = m.title ?? "";
            }

            if (driversButton != null)
            {
                driversButton.SetSelectedInGroup(!m.constructorsMode);
            }

            if (constructorsButton != null)
            {
                constructorsButton.SetSelectedInGroup(m.constructorsMode);
            }

            bool hasData = m.series != null && m.series.Count > 0;
            if (emptyLabel != null)
            {
                emptyLabel.gameObject.SetActive(!hasData);
                if (!hasData)
                {
                    emptyLabel.text = string.IsNullOrEmpty(m.emptyMessage)
                        ? "No completed rounds this season yet."
                        : m.emptyMessage;
                }
            }

            RenderGrid(m.yTicks);
            RenderYLabels(m.yTicks);
            RenderXLabels(m.roundLabels);
            RenderSeries(m.series);
            RenderLegend(m.series);
        }

        void RenderGrid(List<int> yTicks)
        {
            int count = yTicks != null ? yTicks.Count : 0;
            EnsureImages(gridPool, plotRect, count);
            int maxTick = count > 0 ? yTicks[count - 1] : 1;
            for (int i = 0; i < count; i++)
            {
                float ny = maxTick > 0 ? yTicks[i] / (float)maxTick : 0f;
                Image line = gridPool[i];
                line.gameObject.SetActive(true);
                line.color = new Color(1f, 1f, 1f, i == 0 ? 0.35f : 0.12f);
                Configure(line.rectTransform, new Vector2(0f, ny * PlotHeight), new Vector2(PlotWidth, ny * PlotHeight), GridThickness);
            }

            Deactivate(gridPool, count);
        }

        void RenderYLabels(List<int> yTicks)
        {
            int count = yTicks != null ? yTicks.Count : 0;
            EnsureLabels(yLabelPool, yLabelContainer, count);
            int maxTick = count > 0 ? yTicks[count - 1] : 1;
            for (int i = 0; i < count; i++)
            {
                float ny = maxTick > 0 ? yTicks[i] / (float)maxTick : 0f;
                TMP_Text label = yLabelPool[i];
                label.gameObject.SetActive(true);
                label.text = yTicks[i].ToString();
                label.alignment = TextAlignmentOptions.MidlineRight;
                RectTransform rt = label.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(46f, 20f);
                rt.anchoredPosition = new Vector2(-6f, ny * PlotHeight);
            }

            Deactivate(yLabelPool, count);
        }

        void RenderXLabels(List<string> roundLabels)
        {
            int count = roundLabels != null ? roundLabels.Count : 0;
            EnsureLabels(xLabelPool, xLabelContainer, count);
            for (int i = 0; i < count; i++)
            {
                float nx = count <= 1 ? 0f : i / (float)(count - 1);
                TMP_Text label = xLabelPool[i];
                label.gameObject.SetActive(true);
                label.text = roundLabels[i] ?? "";
                label.alignment = TextAlignmentOptions.Top;
                RectTransform rt = label.rectTransform;
                // Same bottom-left origin as the plot; sit just below the baseline.
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(60f, 20f);
                rt.anchoredPosition = new Vector2(nx * PlotWidth, -4f);
            }

            Deactivate(xLabelPool, count);
        }

        void RenderSeries(List<ChampionshipSeriesModel> series)
        {
            // Count the segments needed across every series.
            int needed = 0;
            if (series != null)
            {
                for (int s = 0; s < series.Count; s++)
                {
                    int pc = series[s].points != null ? series[s].points.Count : 0;
                    needed += Mathf.Max(0, pc - 1);
                }
            }

            EnsureImages(segmentPool, plotRect, needed);

            int seg = 0;
            if (series != null)
            {
                for (int s = 0; s < series.Count; s++)
                {
                    ChampionshipSeriesModel sm = series[s];
                    List<Vector2> pts = sm.points;
                    if (pts == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        Image line = segmentPool[seg++];
                        line.gameObject.SetActive(true);
                        line.color = sm.color;
                        Configure(line.rectTransform,
                            new Vector2(pts[i].x * PlotWidth, pts[i].y * PlotHeight),
                            new Vector2(pts[i + 1].x * PlotWidth, pts[i + 1].y * PlotHeight),
                            sm.isPlayer ? LineThickness + 1.5f : LineThickness);
                    }
                }
            }

            Deactivate(segmentPool, seg);
        }

        void RenderLegend(List<ChampionshipSeriesModel> series)
        {
            int count = series != null ? series.Count : 0;
            EnsureLabels(legendPool, legendContainer, count, legendTemplate);
            for (int i = 0; i < count; i++)
            {
                ChampionshipSeriesModel sm = series[i];
                TMP_Text row = legendPool[i];
                row.gameObject.SetActive(true);
                string hex = ColorUtility.ToHtmlStringRGB(sm.color);
                string marker = sm.isPlayer ? " <b>(YOU)</b>" : "";
                string value = string.IsNullOrEmpty(sm.finalValue) ? "" : "  <color=#8aa>" + sm.finalValue + "</color>";
                row.text = "<color=#" + hex + ">■</color> " + sm.label + marker + value;
            }

            Deactivate(legendPool, count);
        }

        // ---- pooling + segment geometry helpers ----

        static void Configure(RectTransform rect, Vector2 a, Vector2 b, float thickness)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = a;
            float distance = Vector2.Distance(a, b);
            rect.sizeDelta = new Vector2(distance, thickness);
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        void EnsureImages(List<Image> pool, RectTransform parent, int count)
        {
            while (pool.Count < count)
            {
                var go = new GameObject("Segment", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                Image image = go.AddComponent<Image>();
                image.raycastTarget = false;
                pool.Add(image);
            }
        }

        void EnsureLabels(List<TMP_Text> pool, RectTransform parent, int count, TMP_Text template = null)
        {
            while (pool.Count < count)
            {
                TMP_Text label;
                if (template != null)
                {
                    label = Instantiate(template, parent);
                }
                else
                {
                    var go = new GameObject("Label", typeof(RectTransform));
                    go.transform.SetParent(parent, false);
                    label = go.AddComponent<TextMeshProUGUI>();
                    label.fontSize = 13f;
                    label.color = new Color(0.7f, 0.78f, 0.85f);
                }

                pool.Add(label);
            }
        }

        static void Deactivate(List<Image> pool, int from)
        {
            for (int i = from; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
            }
        }

        static void Deactivate(List<TMP_Text> pool, int from)
        {
            for (int i = from; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
            }
        }
    }
}
