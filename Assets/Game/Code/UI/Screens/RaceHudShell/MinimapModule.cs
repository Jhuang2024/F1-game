using System.Collections.Generic;
using F1Game.Core;
using F1Game.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace F1Game.UI.Screens.RaceHudShell
{
    /// <summary>
    /// Track minimap: the circuit outline (pooled dots rebuilt only when the
    /// track changes) plus the live car dots (pooled, repositioned each frame)
    /// read from <see cref="HudTrackMap"/>. Normalized 0..1 map space is scaled
    /// into this module's own square rect, so it needs no world-scale knowledge.
    /// Pooled - never destroys/reallocates dots per frame.
    /// </summary>
    public sealed class MinimapModule : MonoBehaviour
    {
        [SerializeField] RectTransform container;
        readonly List<Image> outlineDots = new List<Image>();
        readonly List<Image> carDots = new List<Image>();
        int builtOutlineVersion = -1;
        float mapSize;

        public void Bind(RectTransform mapContainer)
        {
            container = mapContainer;
        }

        void Update()
        {
            if (container == null)
            {
                return;
            }

            Rect rect = container.rect;
            mapSize = Mathf.Min(rect.width, rect.height);
            if (mapSize <= 1f)
            {
                return;
            }

            RebuildOutlineIfNeeded();
            RenderCarDots();
        }

        void RebuildOutlineIfNeeded()
        {
            if (builtOutlineVersion == HudTrackMap.OutlineVersion)
            {
                return;
            }

            builtOutlineVersion = HudTrackMap.OutlineVersion;
            int count = HudTrackMap.OutlineCount;
            EnsurePool(outlineDots, count, 3f, UiTheme.Active.palette.textMuted);
            for (int i = 0; i < outlineDots.Count; i++)
            {
                bool active = i < count;
                if (outlineDots[i].gameObject.activeSelf != active)
                {
                    outlineDots[i].gameObject.SetActive(active);
                }

                if (active)
                {
                    Place(outlineDots[i], HudTrackMap.Outline[i].x, HudTrackMap.Outline[i].y);
                }
            }
        }

        void RenderCarDots()
        {
            int count = HudTrackMap.DotCount;
            EnsurePool(carDots, count, 6f, UiTheme.Active.palette.textPrimary);
            for (int i = 0; i < carDots.Count; i++)
            {
                bool active = i < count;
                if (carDots[i].gameObject.activeSelf != active)
                {
                    carDots[i].gameObject.SetActive(active);
                }

                if (!active)
                {
                    continue;
                }

                HudMapDot dot = HudTrackMap.Dots[i];
                Place(carDots[i], dot.X, dot.Y);
                Color c = dot.Retired ? UiTheme.Active.palette.textMuted
                    : (dot.IsPlayer ? UiTheme.Active.palette.accent
                    : (dot.InPit ? UiTheme.Active.palette.warning : UiTheme.Active.palette.textPrimary));
                carDots[i].color = c;
                // Player dot rides slightly larger and on top.
                carDots[i].rectTransform.SetAsLastSibling();
                float size = dot.IsPlayer ? 9f : 6f;
                carDots[i].rectTransform.sizeDelta = new Vector2(size, size);
            }
        }

        void EnsurePool(List<Image> pool, int needed, float size, Color color)
        {
            while (pool.Count < needed)
            {
                var go = new GameObject("Dot", typeof(RectTransform));
                go.transform.SetParent(container, false);
                var img = go.AddComponent<Image>();
                img.color = color;
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(size, size);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                pool.Add(img);
            }
        }

        void Place(Image dot, float nx, float ny)
        {
            dot.rectTransform.anchoredPosition = new Vector2(nx * mapSize, ny * mapSize);
        }
    }
}
