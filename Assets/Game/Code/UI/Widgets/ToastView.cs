using System.Collections;
using F1Game.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1Game.UI.Widgets
{
    /// <summary>Pooled toast row managed by ToastService.</summary>
    public class ToastView : MonoBehaviour
    {
        public enum Kind { Info, Success, Warning, Error }

        [SerializeField] TMP_Text label;
        [SerializeField] Image accent;
        [SerializeField] CanvasGroup group;

        System.Action<ToastView> returnToPool;
        Coroutine lifetime;

        public void Bind(TMP_Text labelText, Image accentImage, CanvasGroup canvasGroup)
        {
            label = labelText;
            accent = accentImage;
            group = canvasGroup;
        }

        public void Present(string message, Kind kind, float seconds, System.Action<ToastView> onDone)
        {
            returnToPool = onDone;
            if (label != null)
            {
                label.text = message;
            }

            if (accent != null)
            {
                UiTheme theme = UiTheme.Active;
                accent.color = kind switch
                {
                    Kind.Success => theme.palette.positive,
                    Kind.Warning => theme.palette.warning,
                    Kind.Error => theme.palette.danger,
                    _ => theme.palette.accent,
                };
            }

            if (group != null)
            {
                group.alpha = 1f;
            }

            if (lifetime != null)
            {
                StopCoroutine(lifetime);
            }

            lifetime = StartCoroutine(Lifetime(seconds));
        }

        public void Dismiss()
        {
            if (lifetime != null)
            {
                StopCoroutine(lifetime);
                lifetime = null;
            }

            returnToPool?.Invoke(this);
        }

        IEnumerator Lifetime(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);

            float fade = UiTheme.Active.motion.micro;
            float elapsed = 0f;
            while (elapsed < fade && group != null)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(elapsed / fade);
                yield return null;
            }

            lifetime = null;
            returnToPool?.Invoke(this);
        }
    }
}
