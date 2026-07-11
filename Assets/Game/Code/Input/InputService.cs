using F1Game.Core.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace F1Game.Input
{
    /// <summary>
    /// Owns the F1Controls action asset: loads it (with any saved binding
    /// overrides), exposes the action maps, and switches between them so only
    /// one context (Frontend / Driving / MFD / Replay / PhotoMode) is live at a
    /// time. Debug stays available in development builds only.
    /// </summary>
    public sealed class InputService
    {
        public const string AssetResourcePath = "Input/F1Controls";

        public InputActionAsset Asset { get; }
        public InputActionMap Frontend { get; }
        public InputActionMap Driving { get; }
        public InputActionMap Mfd { get; }
        public InputActionMap Replay { get; }
        public InputActionMap PhotoMode { get; }
        public InputActionMap Debug { get; }

        public RebindService Rebinding { get; }
        public DeviceWatcher Devices { get; }

        static InputService instance;

        public static InputService Instance => instance ??= new InputService();

        InputService()
        {
            Asset = Resources.Load<InputActionAsset>(AssetResourcePath);
            if (Asset == null)
            {
                UnityEngine.Debug.LogError(DiagnosticLog.FormatError(DiagnosticCode.InputActionsMissing, "F1Controls.inputactions missing from Resources/Input."));
                Asset = ScriptableObject.CreateInstance<InputActionAsset>();
            }

            Frontend = Asset.FindActionMap("Frontend");
            Driving = Asset.FindActionMap("Driving");
            Mfd = Asset.FindActionMap("MFD");
            Replay = Asset.FindActionMap("Replay");
            PhotoMode = Asset.FindActionMap("PhotoMode");
            Debug = Asset.FindActionMap("Debug");

            Rebinding = new RebindService(Asset);
            Rebinding.LoadOverrides();
            Devices = new DeviceWatcher();

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug?.Enable();
#endif
        }

        public void EnterFrontend() => Activate(Frontend);
        public void EnterDriving() => Activate(Driving);
        public void EnterMfd() => Activate(Mfd);
        public void EnterReplay() => Activate(Replay);
        public void EnterPhotoMode() => Activate(PhotoMode);

        void Activate(InputActionMap target)
        {
            foreach (InputActionMap map in new[] { Frontend, Driving, Mfd, Replay, PhotoMode })
            {
                if (map == null)
                {
                    continue;
                }

                if (map == target)
                {
                    map.Enable();
                }
                else
                {
                    map.Disable();
                }
            }
        }
    }
}
