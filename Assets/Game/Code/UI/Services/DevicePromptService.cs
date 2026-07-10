using F1Game.Core.Events;

namespace F1Game.UI.Services
{
    /// <summary>
    /// Tracks the most recently used input device (from
    /// <see cref="InputDeviceChangedEvent"/>, published by the input layer's
    /// DeviceWatcher) and resolves the right prompt glyph names for it, so
    /// button hints switch automatically between keyboard, Xbox-style and
    /// PlayStation-style prompts on hot-plug or first input.
    /// </summary>
    public sealed class DevicePromptService
    {
        public InputDeviceKind CurrentKind { get; private set; } = InputDeviceKind.KeyboardMouse;
        public string CurrentDeviceName { get; private set; } = "Keyboard & Mouse";

        public event System.Action<InputDeviceKind> DeviceKindChanged;

        public DevicePromptService()
        {
            GameEvents.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        }

        public void Dispose()
        {
            GameEvents.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        }

        void OnDeviceChanged(InputDeviceChangedEvent evt)
        {
            if (evt.Kind == CurrentKind)
            {
                return;
            }

            CurrentKind = evt.Kind;
            CurrentDeviceName = evt.DisplayName;
            DeviceKindChanged?.Invoke(evt.Kind);
        }

        /// <summary>
        /// Glyph identifier for a logical action button on the current device,
        /// e.g. "confirm" → "Enter" / "A" / "Cross". Icon art keyed by these ids
        /// lives in Assets/Game/Art/Icons (placeholder set until real art lands).
        /// </summary>
        public string GlyphId(string logicalButton)
        {
            switch (CurrentKind)
            {
                case InputDeviceKind.PlayStationStyleGamepad:
                    switch (logicalButton)
                    {
                        case "confirm": return "ps_cross";
                        case "back": return "ps_circle";
                        case "context": return "ps_square";
                        case "alt": return "ps_triangle";
                        default: return "ps_" + logicalButton;
                    }

                case InputDeviceKind.XboxStyleGamepad:
                case InputDeviceKind.GenericGamepad:
                    switch (logicalButton)
                    {
                        case "confirm": return "xb_a";
                        case "back": return "xb_b";
                        case "context": return "xb_x";
                        case "alt": return "xb_y";
                        default: return "xb_" + logicalButton;
                    }

                case InputDeviceKind.SteeringWheel:
                    return "wheel_" + logicalButton;

                default:
                    switch (logicalButton)
                    {
                        case "confirm": return "kb_enter";
                        case "back": return "kb_escape";
                        case "context": return "kb_e";
                        case "alt": return "kb_tab";
                        default: return "kb_" + logicalButton;
                    }
            }
        }
    }
}
