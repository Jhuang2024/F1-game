using UnityEngine;
using UnityEngine.EventSystems;

namespace F1Game.UI.Navigation
{
    /// <summary>
    /// Base class for an authored screen prefab. A view owns only presentation:
    /// it is shown/hidden by the <see cref="ScreenRouter"/> via CanvasGroup
    /// (never destroyed per navigation) and declares its default selectable so
    /// controller/keyboard focus always lands somewhere visible.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class ScreenView : MonoBehaviour
    {
        [Tooltip("Unique router id, e.g. \"main-menu\".")]
        [SerializeField] string screenId;

        [Tooltip("First selected object when the screen gains focus (controller/keyboard navigation).")]
        [SerializeField] GameObject defaultSelection;

        CanvasGroup canvasGroup;

        public string ScreenId => screenId;
        public GameObject DefaultSelection => defaultSelection;

        public CanvasGroup CanvasGroup
        {
            get
            {
                if (canvasGroup == null)
                {
                    canvasGroup = GetComponent<CanvasGroup>();
                }

                return canvasGroup;
            }
        }

        protected void SetScreenId(string id) => screenId = id;
        protected void SetDefaultSelection(GameObject selection) => defaultSelection = selection;

        /// <summary>Called by the router after the view becomes visible.</summary>
        public virtual void OnEntered(object args) { }

        /// <summary>Called by the router before the view is hidden.</summary>
        public virtual void OnExited() { }

        public void SetVisible(bool visible)
        {
            CanvasGroup.alpha = visible ? 1f : 0f;
            CanvasGroup.interactable = visible;
            CanvasGroup.blocksRaycasts = visible;

            if (visible && defaultSelection != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(defaultSelection);
            }
        }
    }
}
