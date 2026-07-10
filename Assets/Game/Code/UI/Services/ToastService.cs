using System.Collections.Generic;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using UnityEngine;

namespace F1Game.UI.Services
{
    /// <summary>
    /// Pooled toast notifications (top of the toast layer, newest first).
    /// Instances are recycled, not destroyed — notifications are one of the
    /// frequent-spawn objects the performance pass requires pooling for.
    /// </summary>
    public sealed class ToastService
    {
        readonly Transform toastLayer;
        readonly ToastView prefab;
        readonly Queue<ToastView> pool = new Queue<ToastView>();
        readonly List<ToastView> live = new List<ToastView>();

        public int MaxVisible { get; set; } = 4;

        public ToastService(Transform toastLayer, ToastView prefab)
        {
            this.toastLayer = toastLayer;
            this.prefab = prefab;
        }

        public void Show(string message, ToastView.Kind kind = ToastView.Kind.Info)
        {
            if (prefab == null)
            {
                Debug.Log("[Toast] " + message);
                return;
            }

            ToastView toast = pool.Count > 0 ? pool.Dequeue() : Object.Instantiate(prefab, toastLayer);
            toast.transform.SetParent(toastLayer, false);
            toast.transform.SetAsFirstSibling();
            toast.gameObject.SetActive(true);
            toast.Present(message, kind, UiTheme.Active.components.toastSeconds, ReturnToPool);
            live.Add(toast);

            while (live.Count > MaxVisible)
            {
                ToastView oldest = live[0];
                live.RemoveAt(0);
                oldest.Dismiss();
            }
        }

        void ReturnToPool(ToastView toast)
        {
            live.Remove(toast);
            toast.gameObject.SetActive(false);
            pool.Enqueue(toast);
        }
    }
}
