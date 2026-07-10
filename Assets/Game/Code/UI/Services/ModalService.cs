using System;
using System.Collections.Generic;
using F1Game.Core.Events;
using F1Game.UI.Widgets;
using UnityEngine;

namespace F1Game.UI.Services
{
    /// <summary>
    /// Central modal host. Modals render on a dedicated top-priority canvas
    /// layer (created by UiShell), so a modal can never appear behind another
    /// canvas — one of the explicit failure modes of the legacy UI. Supports a
    /// stack (modal over modal) with backdrop, focus capture and Escape/B close.
    /// </summary>
    public sealed class ModalService
    {
        readonly Transform modalLayer;
        readonly Stack<ModalView> open = new Stack<ModalView>();

        public bool AnyOpen => open.Count > 0;

        public ModalService(Transform modalLayer)
        {
            this.modalLayer = modalLayer;
        }

        public ModalView Show(ModalView prefab, object args = null, Action onClosed = null)
        {
            if (prefab == null)
            {
                return null;
            }

            ModalView modal = UnityEngine.Object.Instantiate(prefab, modalLayer);
            modal.Opened(this, args, onClosed);
            open.Push(modal);
            GameEvents.Publish(new UiNavigationEvent(null, "modal:" + prefab.name, true));
            return modal;
        }

        public void CloseTop()
        {
            if (open.Count == 0)
            {
                return;
            }

            ModalView modal = open.Pop();
            if (modal != null)
            {
                modal.Closed();
                UnityEngine.Object.Destroy(modal.gameObject);
            }
        }

        public void CloseAll()
        {
            while (open.Count > 0)
            {
                CloseTop();
            }
        }
    }
}
