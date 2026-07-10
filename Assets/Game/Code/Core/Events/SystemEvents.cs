namespace F1Game.Core.Events
{
    // Application-level event contracts: UI navigation, persistence, input devices.

    public enum InputDeviceKind
    {
        KeyboardMouse,
        XboxStyleGamepad,
        PlayStationStyleGamepad,
        GenericGamepad,
        SteeringWheel,
    }

    public readonly struct UiNavigationEvent
    {
        public readonly string FromScreen;
        public readonly string ToScreen;
        public readonly bool IsModal;

        public UiNavigationEvent(string fromScreen, string toScreen, bool isModal)
        {
            FromScreen = fromScreen;
            ToScreen = toScreen;
            IsModal = isModal;
        }
    }

    public readonly struct SaveCompletedEvent
    {
        public readonly string FileName;
        public readonly bool Success;
        public readonly string Error;

        public SaveCompletedEvent(string fileName, bool success, string error)
        {
            FileName = fileName;
            Success = success;
            Error = error;
        }
    }

    public readonly struct InputDeviceChangedEvent
    {
        public readonly InputDeviceKind Kind;
        public readonly string DisplayName;

        public InputDeviceChangedEvent(InputDeviceKind kind, string displayName)
        {
            Kind = kind;
            DisplayName = displayName;
        }
    }

    public readonly struct GraphicsPresetChangedEvent
    {
        /// <summary>0 = Low, 1 = Medium, 2 = High (matches settings graphicsQuality tiers).</summary>
        public readonly int PresetIndex;

        public GraphicsPresetChangedEvent(int presetIndex)
        {
            PresetIndex = presetIndex;
        }
    }
}
