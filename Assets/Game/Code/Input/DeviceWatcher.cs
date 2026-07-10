using F1Game.Core.Events;
using UnityEngine.InputSystem;

namespace F1Game.Input
{
    /// <summary>
    /// Watches device hot-plugging and last-used device, classifies it
    /// (keyboard / Xbox-style / PlayStation-style / generic pad / wheel) and
    /// publishes <see cref="InputDeviceChangedEvent"/> so UI prompts and
    /// calibration screens follow the active device automatically.
    /// </summary>
    public sealed class DeviceWatcher
    {
        InputDeviceKind lastPublished = InputDeviceKind.KeyboardMouse;
        bool anyPublished;

        public DeviceWatcher()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            InputSystem.onActionChange += OnActionChange;
        }

        public void Dispose()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onActionChange -= OnActionChange;
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
            {
                Publish(device);
            }
            else if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
            {
                // Fall back to keyboard prompts when the active pad goes away.
                if (Classify(device) == lastPublished)
                {
                    PublishKind(InputDeviceKind.KeyboardMouse, "Keyboard & Mouse");
                }
            }
        }

        void OnActionChange(object actionOrMap, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed || !(actionOrMap is InputAction action))
            {
                return;
            }

            InputDevice device = action.activeControl?.device;
            if (device != null)
            {
                Publish(device);
            }
        }

        void Publish(InputDevice device)
        {
            PublishKind(Classify(device), device.displayName);
        }

        void PublishKind(InputDeviceKind kind, string name)
        {
            if (anyPublished && kind == lastPublished)
            {
                return;
            }

            anyPublished = true;
            lastPublished = kind;
            GameEvents.Publish(new InputDeviceChangedEvent(kind, name));
        }

        public static InputDeviceKind Classify(InputDevice device)
        {
            switch (device)
            {
                case null:
                    return InputDeviceKind.KeyboardMouse;
                case Keyboard _:
                case Mouse _:
                    return InputDeviceKind.KeyboardMouse;
                case Gamepad pad:
                {
                    string name = (pad.description.manufacturer + " " + pad.description.product + " " + pad.name).ToLowerInvariant();
                    if (name.Contains("dualshock") || name.Contains("dualsense") || name.Contains("sony") || name.Contains("wireless controller"))
                    {
                        return InputDeviceKind.PlayStationStyleGamepad;
                    }

                    if (name.Contains("xbox") || name.Contains("xinput") || name.Contains("microsoft"))
                    {
                        return InputDeviceKind.XboxStyleGamepad;
                    }

                    return InputDeviceKind.GenericGamepad;
                }
                default:
                {
                    string layout = device.layout.ToLowerInvariant();
                    if (layout.Contains("wheel") || device.description.deviceClass == "Wheel")
                    {
                        return InputDeviceKind.SteeringWheel;
                    }

                    return InputDeviceKind.GenericGamepad;
                }
            }
        }
    }
}
