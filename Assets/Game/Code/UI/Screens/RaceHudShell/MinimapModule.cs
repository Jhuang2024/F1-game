using System.Collections.Generic;
using F1Game.Core;
using F1Game.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace F1Game.UI.Screens.RaceHudShell
{
    /// <summary>
    /// Track minimap: the circuit outline (a pooled closed polyline of rotated
    /// segment images, rebuilt only when the track changes) plus the live car
    /// dots (pooled circular sprites, repositioned each frame) read from
    /// <see cref="HudTrackMap"/>. Normalized 0..1 map space is scaled into
    /// this module's own square rect, so it needs no world-scale knowledge.
    /// Pooled - never destroys/reallocates elements per frame.
    /// </summary>
    public sealed class MinimapModule : MonoBehaviour
    {
        const float OutlineThickness = 2.5f;

        [SerializeField] RectTransform container;
        readonly List<Image> outlineSegments = new List<Image>();
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
            // Closed loop: one segment per outline point, the last wrapping
            // back to the first, so the circuit reads as one continuous drawn
            // line instead of a scatter of dots.
            int segmentCount = count >= 2 ? count : 0;
            EnsurePool(outlineSegments, segmentCount, OutlineThickness, UiTheme.Active.palette.textMuted, null);
            for (int i = 0; i < outlineSegments.Count; i++)
            {
                bool active = i < segmentCount;
                if (outlineSegments[i].gameObject.activeSelf != active)
                {
                    outlineSegments[i].gameObject.SetActive(active);
                }

                if (active)
                {
                    Vector2 from = HudTrackMap.Outline[i] * mapSize;
                    Vector2 to = HudTrackMap.Outline[(i + 1) % count] * mapSize;
                    PlaceSegment(outlineSegments[i], from, to);
                }
            }
        }

        void RenderCarDots()
        {
            int count = HudTrackMap.DotCount;
            EnsurePool(carDots, count, 6f, UiTheme.Active.palette.textPrimary, UiSprites.Circle);
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

        void EnsurePool(List<Image> pool, int needed, float size, Color color, Sprite sprite)
        {
            while (pool.Count < needed)
            {
                var go = new GameObject(sprite == null ? "Segment" : "Dot", typeof(RectTransform));
                go.transform.SetParent(container, false);
                var img = go.AddComponent<Image>();
                img.color = color;
                img.sprite = sprite;
                img.raycastTarget = false;
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

        // Stretch a thin rect between two map points and rotate it to lie
        // along them. Slightly over-length (by its own thickness) so
        // consecutive segments overlap and corners never show gaps.
        void PlaceSegment(Image segment, Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            RectTransform rt = segment.rectTransform;
            rt.sizeDelta = new Vector2(delta.magnitude + OutlineThickness, OutlineThickness);
            rt.anchoredPosition = (from + to) * 0.5f;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
    }
}
