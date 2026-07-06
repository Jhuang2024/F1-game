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
