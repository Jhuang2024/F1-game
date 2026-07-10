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

        public void Fade(CanvasGroup group, float from, float to, float duration, Action onComplete = null)
        {
            if (group == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (ReducedMotion || duration <= 0f || !coroutineHost.isActiveAndEnabled)
            {
                group.alpha = to;
                onComplete?.Invoke();
                return;
            }

            coroutineHost.StartCoroutine(FadeRoutine(group, from, to, duration, onComplete));
        }

        static IEnumerator FadeRoutine(CanvasGroup group, float from, float to, float duration, Action onComplete)
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
            onComplete?.Invoke();
        }
    }
}
