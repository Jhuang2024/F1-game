using UnityEngine;

namespace LocalFormulaRacing
{
    public class GameSettingsStore
    {
        const string SettingsFile = "formula_racing_settings.json";

        public GameSettingsData Current { get; private set; }

        public void Load()
        {
            Current = LocalJsonStore.Load(SettingsFile, new GameSettingsData());
            if (Current.laps < 3)
            {
                Current.laps = 5;
            }

            Current.aiOpponentCount = 21;

            Current.steeringSensitivity = ClampSetting(Current.steeringSensitivity, 1f, 0.45f, 1.65f);
            Current.throttleSensitivity = ClampSetting(Current.throttleSensitivity, 1f, 0.45f, 1.65f);
            Current.brakeSensitivity = ClampSetting(Current.brakeSensitivity, 1f, 0.45f, 1.65f);
            Current.controllerDeadzone = ClampSetting(Current.controllerDeadzone, 0.12f, 0.02f, 0.35f);
            if (Current.ersMode < 0 || Current.ersMode > 2)
            {
                Current.ersMode = 0;
            }

            Current.hudScale = ClampSetting(Current.hudScale, 1f, 0.75f, 1.3f);
            Current.cameraFov = ClampSetting(Current.cameraFov, 60f, 48f, 78f);
            Current.cameraShakeStrength = Mathf.Clamp(Current.cameraShakeStrength, 0f, 0.6f);
            Current.sceneryDensity = ClampSetting(Current.sceneryDensity, 1f, 0.25f, 2f);
            Current.graphicsQuality = Mathf.Clamp(Current.graphicsQuality, 0, 3);

            Current.setupFrontWing = ClampSetupStep(Current.setupFrontWing);
            Current.setupRearWing = ClampSetupStep(Current.setupRearWing);
            Current.setupBrakeBias = ClampSetupStep(Current.setupBrakeBias);
            Current.setupSuspension = ClampSetupStep(Current.setupSuspension);
            Current.setupRideHeight = ClampSetupStep(Current.setupRideHeight);
            Current.plannedPitLap = Mathf.Clamp(Current.plannedPitLap, 0, 99);
            if (string.IsNullOrEmpty(Current.plannedSecondCompound))
            {
                Current.plannedSecondCompound = "Medium";
            }
        }

        int ClampSetupStep(int value)
        {
            return value <= 0 ? 3 : Mathf.Clamp(value, 1, 5);
        }

        public void Save()
        {
            LocalJsonStore.Save(SettingsFile, Current);
        }

        public RaceDifficulty Difficulty
        {
            get
            {
                if (Current.difficultyIndex < 0)
                {
                    Current.difficultyIndex = 0;
                }

                if (Current.difficultyIndex > 3)
                {
                    Current.difficultyIndex = 3;
                }

                return (RaceDifficulty)Current.difficultyIndex;
            }
        }

        public TyreCompound SelectedTyreCompound
        {
            get
            {
                TyreCompound compound;
                if (System.Enum.TryParse(Current.tyreCompound, true, out compound))
                {
                    return compound;
                }

                return TyreCompound.Medium;
            }
        }

        public ErsStrategyMode ErsMode
        {
            get { return (ErsStrategyMode)Mathf.Clamp(Current.ersMode, 0, 2); }
        }

        float ClampSetting(float value, float fallback, float min, float max)
        {
            if (value <= 0f)
            {
                value = fallback;
            }

            return UnityEngine.Mathf.Clamp(value, min, max);
        }
    }
}
