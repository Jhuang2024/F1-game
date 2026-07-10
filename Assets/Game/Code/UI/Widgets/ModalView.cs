using F1Game.UI.Services;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace F1Game.UI.Widgets
{
    /// <summary>
    /// Base modal prefab component. Instantiated by <see cref="ModalService"/>
    /// on the dedicated modal layer; captures focus while open and restores the
    /// previous selection when closed.
    /// </summary>
    public class ModalView : MonoBehaviour
    {
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text body;
        [SerializeField] ThemedButton confirmButton;
        [SerializeField] ThemedButton cancelButton;
        [SerializeField] GameObject initialFocus;

        ModalService service;
        System.Action onClosed;
        GameObject previousSelection;

        public TMP_Text Title => title;
        public TMP_Text Body => body;
        public ThemedButton ConfirmButton => confirmButton;
        public ThemedButton CancelButton => cancelButton;

        public void Bind(TMP_Text titleText, TMP_Text bodyText, ThemedButton confirm, ThemedButton cancel, GameObject focus)
        {
            title = titleText;
            body = bodyText;
            confirmButton = confirm;
            cancelButton = cancel;
            initialFocus = focus;
        }

        public void Opened(ModalService owner, object args, System.Action closedCallback)
        {
            service = owner;
            onClosed = closedCallback;
            previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

            if (initialFocus != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(initialFocus);
            }

            if (cancelButton != null)
            {
                cancelButton.Clicked += Close;
            }

            OnOpened(args);
        }

        public void Closed()
        {
            if (cancelButton != null)
            {
                cancelButton.Clicked -= Close;
            }

            if (previousSelection != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(previousSelection);
            }

            onClosed?.Invoke();
            OnClosed();
        }

        public void Close()
        {
            service?.CloseTop();
        }

        protected virtual void OnOpened(object args) { }
        protected virtual void OnClosed() { }
    }
}
