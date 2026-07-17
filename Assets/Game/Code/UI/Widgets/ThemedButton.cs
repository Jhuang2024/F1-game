using F1Game.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace F1Game.UI.Widgets
{
    /// <summary>
    /// Production button with the full explicit state set the design system
    /// requires: normal, hover, pressed, focused, selected, disabled, loading
    /// and error. Focus is drawn as an outline (never a colour shift alone) so
    /// keyboard/controller focus is always visible. All colours/durations come
    /// from the active <see cref="UiTheme"/>.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class ThemedButton : Selectable, ISubmitHandler, IPointerClickHandler
    {
        public enum Variant { Primary, Secondary, Tertiary, Destructive }
        public enum VisualState { Normal, Hover, Pressed, Focused, Selected, Disabled, Loading, Error }

        [SerializeField] Variant variant = Variant.Secondary;
        [SerializeField] TMP_Text label;
        [SerializeField] Image background;
        [SerializeField] GameObject focusOutline;
        [SerializeField] GameObject loadingIndicator;

        bool isSelectedInGroup;
        bool isLoading;
        bool isError;
        bool isFocused;
        bool isHovered;
        bool isPressed;

        public event System.Action Clicked;

        public TMP_Text Label => label;
        public VisualState CurrentState { get; private set; } = VisualState.Normal;

        public void Bind(TMP_Text labelText, Image backgroundImage, GameObject outline, GameObject loading)
        {
            label = labelText;
            background = backgroundImage;
            focusOutline = outline;
            loadingIndicator = loading;
            Refresh();
        }

        public void SetVariant(Variant value)
        {
            variant = value;
            Refresh();
        }

        public void SetText(string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        /// <summary>Selected within a group (tabs, segmented options, grid choices).</summary>
        public void SetSelectedInGroup(bool selected)
        {
            isSelectedInGroup = selected;
            Refresh();
        }

        public void SetLoading(bool loading)
        {
            isLoading = loading;
            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(loading);
            }

            Refresh();
        }

        public void SetError(bool error)
        {
            isError = error;
            Refresh();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            TryClick();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                TryClick();
            }
        }

        void TryClick()
        {
            if (!IsInteractable() || isLoading)
            {
                return;
            }

            Clicked?.Invoke();
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            isHovered = true;
            Refresh();
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            base.OnPointerExit(eventData);
            isHovered = false;
            Refresh();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            isPressed = true;
            Refresh();
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            isPressed = false;
            Refresh();
        }

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            isFocused = true;
            Refresh();
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            base.OnDeselect(eventData);
            isFocused = false;
            Refresh();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Refresh();
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            // Selectable invokes this on EVERY state change, including
            // programmatic `interactable` toggles that never pass through the
            // pointer/focus overrides above. Without this hook a code-disabled
            // button (stepper at its bound, gated Start Race, ineligible
            // career action) kept its enabled colours while silently
            // no-opping.
            Refresh();
        }

        void Refresh()
        {
            CurrentState = ResolveState();
            Apply(CurrentState);
        }

        VisualState ResolveState()
        {
            if (!IsInteractable()) return VisualState.Disabled;
            if (isError) return VisualState.Error;
            if (isLoading) return VisualState.Loading;
            if (isPressed) return VisualState.Pressed;
            if (isSelectedInGroup) return VisualState.Selected;
            if (isFocused) return VisualState.Focused;
            if (isHovered) return VisualState.Hover;
            return VisualState.Normal;
        }

        void Apply(VisualState state)
        {
            UiTheme theme = UiTheme.Active;
            if (background == null)
            {
                return;
            }

            Color surface;
            Color text = theme.palette.textPrimary;

            switch (state)
            {
                case VisualState.Disabled:
                    surface = theme.states.disabledSurface;
                    text = theme.states.disabledText;
                    break;
                case VisualState.Error:
                    surface = theme.palette.danger;
                    break;
                case VisualState.Loading:
                    surface = theme.states.pressed;
                    text = theme.palette.textMuted;
                    break;
                case VisualState.Pressed:
                    surface = variant == Variant.Primary ? Darken(theme.palette.accent, 0.8f) : theme.states.pressed;
                    break;
                case VisualState.Selected:
                    surface = theme.states.selected;
                    break;
                case VisualState.Hover:
                    surface = variant == Variant.Primary ? Darken(theme.palette.accent, 1.1f) : theme.states.hover;
                    break;
                default:
                    surface = variant switch
                    {
                        Variant.Primary => theme.palette.accent,
                        Variant.Destructive => Darken(theme.palette.danger, 0.85f),
                        Variant.Tertiary => Color.clear,
                        _ => theme.states.normal,
                    };
                    break;
            }

            background.color = surface;
            if (label != null)
            {
                label.color = text;
            }

            if (focusOutline != null)
            {
                focusOutline.SetActive(state == VisualState.Focused || (isFocused && state == VisualState.Selected));
            }
        }

        static Color Darken(Color color, float factor)
        {
            return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
        }
    }
}
