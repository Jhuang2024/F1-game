using System;
using System.Collections;
using F1Game.UI.Theme;
using UnityEngine;

namespace F1Game.UI.Services
{
    /// <summary>
    /// Screen/panel transitions driven by theme motion tokens. All durations
    /// come from <see cref="UiTheme.Motion"/>; when reduced motion is requested
    /// (accessibility) every transition completes instantly.
    /// </summary>
    public sealed class TransitionService
    {
        readonly MonoBehaviour coroutineHost;
        // One in-flight fade per CanvasGroup: starting a new fade cancels the
        // previous one so overlapping requests never fight over alpha.
        readonly System.Collections.Generic.Dictionary<CanvasGroup, Coroutine> running =
            new System.Collections.Generic.Dictionary<CanvasGroup, Coroutine>();

        public bool ReducedMotion { get; set; }

        public TransitionService(MonoBehaviour coroutineHost)
        {
            this.coroutineHost = coroutineHost;
        }

        public void FadeIn(CanvasGroup group, Action onComplete = null)
        {
            Fade(group, 0f, 1f, UiTheme.Active.motion.screen, onComplete);
        }

        public void FadeOut(CanvasGroup group, Action onComplete = null)
        {
            Fade(group, group.alpha, 0f, UiTheme.Active.motion.screen, onComplete);
        }

        /// <summary>
        /// Stops any in-flight fade on the group WITHOUT touching its alpha.
        /// Wired to ScreenRouter.ExitTransition: a screen being hidden must not
        /// have a still-running enter fade drag its alpha back up afterwards
        /// (the boot-instantiation-pass bug that stacked every screen visible
        /// with the dead Results overlay on top).
        /// </summary>
        public void CancelFade(CanvasGroup group)
        {
            if (group == null)
            {
                return;
            }

            if (running.TryGetValue(group, out Coroutine active))
            {
                if (active != null && coroutineHost != null)
                {
                    coroutineHost.StopCoroutine(active);
                }

                running.Remove(group);
            }
        }

        public void Fade(CanvasGroup group, float from, float to, float duration, Action onComplete = null)
        {
            if (group == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (running.TryGetValue(group, out Coroutine active))
            {
                if (active != null && coroutineHost != null)
                {
                    coroutineHost.StopCoroutine(active);
                }

                running.Remove(group);
            }

            if (ReducedMotion || duration <= 0f || coroutineHost == null || !coroutineHost.isActiveAndEnabled)
            {
                group.alpha = to;
                onComplete?.Invoke();
                return;
            }

            running[group] = coroutineHost.StartCoroutine(FadeRoutine(group, from, to, duration, onComplete));
        }

        IEnumerator FadeRoutine(CanvasGroup group, float from, float to, float duration, Action onComplete)
        {
            AnimationCurve ease = UiTheme.Active.motion.ease;
            float elapsed = 0f;
            group.alpha = from;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.LerpUnclamped(from, to, ease.Evaluate(Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            group.alpha = to;
            running.Remove(group);
            onComplete?.Invoke();
        }
    }
}
