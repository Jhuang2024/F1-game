using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Unity-facing replay transport bar: a scrub track with a draggable playhead,
    /// play/pause and speed-cycle controls, and a time readout. It reads and drives
    /// the engine-free <see cref="F1Game.Race.ReplayPlaybackController"/> transport
    /// exposed by a <see cref="ReplayCameraController"/> - all of which is already
    /// unit-tested; this component is the thin UI binding.
    ///
    /// Default-off and inert: it shares the replay camera's opt-in switch
    /// (<see cref="ReplayCameraController.EnabledKey"/>) and is only built/attached
    /// when a replay is being watched, so it never touches the live race HUD.
    /// VISUAL VALIDATION PENDING (needs an in-editor replay run).
    /// </summary>
    public sealed class ReplayScrubberUi : MonoBehaviour
    {
        static readonly float[] SpeedSteps = { 0.25f, 0.5f, 1f, 2f, 4f, 8f };

        ReplayCameraController controller;
        ScrubTrack track;
        Image trackFill;
        RectTransform handle;
        Text playPauseLabel;
        Text speedLabel;
        Text timeLabel;

        bool scrubbing;
        // While the pointer is dragging, playback is paused so the view follows the
        // scrub; the prior play state is restored on release (mirrors every video
        // scrubber). Null means "not mid-scrub".
        bool? resumeAfterScrub;

        /// <summary>
        /// Build the transport bar under <paramref name="parent"/> (usually the HUD
        /// canvas) bound to <paramref name="cam"/>. Returns null when the replay
        /// camera feature is off or inputs are missing, so callers stay on the live
        /// HUD unchanged.
        /// </summary>
        public static ReplayScrubberUi Create(Transform parent, ReplayCameraController cam)
        {
            if (parent == null || cam == null || !ReplayCameraController.FeatureEnabled)
            {
                return null;
            }

            var go = new GameObject("Replay Scrubber");
            go.transform.SetParent(parent, false);
            var ui = go.AddComponent<ReplayScrubberUi>();
            ui.controller = cam;
            ui.Build(go.transform);
            return ui;
        }

        void Build(Transform root)
        {
            // Bottom-anchored bar spanning most of the width.
            RectTransform bar = UiFactory.CreateGlassPanel(
                root, "Transport Bar",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-560f, 28f), new Vector2(560f, 92f),
                UiFactory.PanelDarker);
            bar.pivot = new Vector2(0.5f, 0f);

            // Play/pause on the left.
            Button play = UiFactory.CreateSecondaryButton(bar, "Pause", TogglePlay);
            RectTransform playRect = play.GetComponent<RectTransform>();
            playRect.anchorMin = new Vector2(0f, 0.5f);
            playRect.anchorMax = new Vector2(0f, 0.5f);
            playRect.pivot = new Vector2(0f, 0.5f);
            playRect.sizeDelta = new Vector2(120f, 44f);
            playRect.anchoredPosition = new Vector2(16f, 0f);
            playPauseLabel = play.GetComponentInChildren<Text>();

            // Speed cycle next to it.
            Button speed = UiFactory.CreateSecondaryButton(bar, "1x", CycleSpeed);
            RectTransform speedRect = speed.GetComponent<RectTransform>();
            speedRect.anchorMin = new Vector2(0f, 0.5f);
            speedRect.anchorMax = new Vector2(0f, 0.5f);
            speedRect.pivot = new Vector2(0f, 0.5f);
            speedRect.sizeDelta = new Vector2(78f, 44f);
            speedRect.anchoredPosition = new Vector2(146f, 0f);
            speedLabel = speed.GetComponentInChildren<Text>();

            // Time readout on the right.
            timeLabel = UiFactory.CreateText(bar, "Time", "0:00 / 0:00", 18, UiFactory.TextMuted, TextAnchor.MiddleRight);
            RectTransform timeRect = timeLabel.GetComponent<RectTransform>();
            timeRect.anchorMin = new Vector2(1f, 0.5f);
            timeRect.anchorMax = new Vector2(1f, 0.5f);
            timeRect.pivot = new Vector2(1f, 0.5f);
            timeRect.sizeDelta = new Vector2(150f, 32f);
            timeRect.anchoredPosition = new Vector2(-18f, 0f);

            // Scrub track between the controls and the readout.
            RectTransform trackRect = UiFactory.CreateRect(
                bar, "Scrub Track",
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(236f, -6f), new Vector2(-176f, 6f));
            Image trackImage = trackRect.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(trackImage, new Color(0.1f, 0.13f, 0.17f, 0.95f));

            RectTransform fillRect = UiFactory.CreateRect(
                trackRect, "Scrub Fill",
                Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            trackFill = fillRect.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(trackFill, UiFactory.AccentCyan);
            trackFill.raycastTarget = false;

            handle = UiFactory.CreateRect(
                trackRect, "Scrub Handle",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            handle.sizeDelta = new Vector2(10f, 26f);
            Image handleImage = handle.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(handleImage, UiFactory.TextPrimary);
            handleImage.raycastTarget = false;

            // The track owns the pointer interaction and forwards a 0-1 value.
            track = trackRect.gameObject.AddComponent<ScrubTrack>();
            track.Bind(trackRect, OnScrubBegin, OnScrubMove, OnScrubEnd);

            RefreshControls();
        }

        void TogglePlay()
        {
            if (controller == null)
            {
                return;
            }

            controller.TogglePlay();
            RefreshControls();
        }

        void CycleSpeed()
        {
            if (controller == null)
            {
                return;
            }

            float current = controller.Transport.SpeedMultiplier;
            int next = 0;
            for (int i = 0; i < SpeedSteps.Length; i++)
            {
                if (SpeedSteps[i] > current + 0.001f)
                {
                    next = i;
                    break;
                }
            }

            controller.SetSpeed(SpeedSteps[next]);
            RefreshControls();
        }

        void OnScrubBegin(float normalized)
        {
            scrubbing = true;
            resumeAfterScrub = controller != null && controller.IsPlaying;
            if (controller != null)
            {
                controller.Pause();
            }

            OnScrubMove(normalized);
        }

        void OnScrubMove(float normalized)
        {
            if (controller == null)
            {
                return;
            }

            controller.SeekNormalized(normalized);
            RefreshPlayhead();
        }

        void OnScrubEnd(float normalized)
        {
            OnScrubMove(normalized);
            scrubbing = false;
            if (controller != null && resumeAfterScrub == true)
            {
                controller.Play();
            }

            resumeAfterScrub = null;
            RefreshControls();
        }

        void Update()
        {
            // Playback advances the transport in the camera's LateUpdate; reflect the
            // moving playhead here unless the user is actively scrubbing.
            if (!scrubbing)
            {
                RefreshPlayhead();
            }
        }

        void RefreshControls()
        {
            if (controller == null)
            {
                return;
            }

            if (playPauseLabel != null)
            {
                playPauseLabel.text = controller.IsPlaying ? "Pause" : "Play";
            }

            if (speedLabel != null)
            {
                float s = controller.Transport.SpeedMultiplier;
                speedLabel.text = (Math.Abs(s - Mathf.Round(s)) < 0.001f)
                    ? $"{Mathf.RoundToInt(s)}x"
                    : $"{s:0.##}x";
            }

            RefreshPlayhead();
        }

        void RefreshPlayhead()
        {
            if (controller == null)
            {
                return;
            }

            float n = Mathf.Clamp01(controller.NormalizedPlayhead);
            if (trackFill != null)
            {
                trackFill.rectTransform.anchorMax = new Vector2(n, 1f);
            }

            if (handle != null)
            {
                handle.anchorMin = new Vector2(n, 0.5f);
                handle.anchorMax = new Vector2(n, 0.5f);
                handle.anchoredPosition = Vector2.zero;
            }

            if (timeLabel != null)
            {
                F1Game.Race.ReplayPlaybackController t = controller.Transport;
                float elapsed = t.PlayheadSeconds - t.StartSeconds;
                timeLabel.text = $"{FormatTime(elapsed)} / {FormatTime(t.DurationSeconds)}";
            }
        }

        static string FormatTime(float seconds)
        {
            if (seconds < 0f || float.IsNaN(seconds))
            {
                seconds = 0f;
            }

            int total = Mathf.FloorToInt(seconds);
            int mins = total / 60;
            int secs = total % 60;
            return $"{mins}:{secs:00}";
        }

        /// <summary>
        /// Pointer handler on the scrub track: converts a click/drag x-position into a
        /// 0-1 value and forwards begin/move/end so the parent can seek. Kept tiny and
        /// self-contained so the track rect is the only raycast target.
        /// </summary>
        sealed class ScrubTrack : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            RectTransform rect;
            Action<float> onBegin;
            Action<float> onMove;
            Action<float> onEnd;

            public void Bind(RectTransform trackRect, Action<float> begin, Action<float> move, Action<float> end)
            {
                rect = trackRect;
                onBegin = begin;
                onMove = move;
                onEnd = end;
            }

            public void OnPointerDown(PointerEventData eventData) => onBegin?.Invoke(ValueAt(eventData));
            public void OnDrag(PointerEventData eventData) => onMove?.Invoke(ValueAt(eventData));
            public void OnPointerUp(PointerEventData eventData) => onEnd?.Invoke(ValueAt(eventData));

            float ValueAt(PointerEventData eventData)
            {
                if (rect == null)
                {
                    return 0f;
                }

                Camera cam = eventData.pressEventCamera;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, cam, out Vector2 local))
                {
                    float width = rect.rect.width;
                    if (width > 0.0001f)
                    {
                        // Local x is measured from the rect's pivot; normalize across
                        // the full width to a 0-1 track position.
                        float fromLeft = local.x - rect.rect.xMin;
                        return Mathf.Clamp01(fromLeft / width);
                    }
                }

                return 0f;
            }
        }
    }
}
