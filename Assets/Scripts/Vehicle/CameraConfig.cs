using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Feature flag for the camera backend. Cinemachine (RaceCameraDirector) is
    /// the default live path; the legacy hand-rolled CameraRig positioning stays
    /// behind this flag as a documented fallback until in-editor validation, per
    /// the migration rule that a legacy fallback is retained until the
    /// replacement is validated across the full path.
    /// </summary>
    public static class CameraConfig
    {
        const string ToggleKey = "f1game_cinemachine";

        /// <summary>1 = Cinemachine, 0 = legacy CameraRig, unset = Cinemachine (default).</summary>
        public static bool UseCinemachine
        {
            get
            {
                int toggle = PlayerPrefs.GetInt(ToggleKey, -1);
                if (toggle == 0) return false;
                return true;
            }
        }

        public static void SetUseCinemachine(bool value)
        {
            PlayerPrefs.SetInt(ToggleKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
