using UnityEngine;

namespace LocalFormulaRacing
{
    public class GameSettingsStore
    {
        const string SettingsFile = "formula_racing_settings.json";

        // UI Scale: a global text/UI size multiplier for every menu and HUD
        // panel, distinct from the existing hudScale (which only resizes
        // in-race HUD cards around their own screen edge). GameSettingsData
        // itself lives in Assets/Scripts/Data/DataModels.cs, which is out of
        // scope for this pass, so this setting is stored and persisted here
        // via PlayerPrefs instead of the JSON settings blob - it never needs
        // to travel with a save file, just the local machine's display
        // preference, so PlayerPrefs is a perfectly natural home for it.
        const string UiScaleKey = "formula_racing_ui_scale";
        public const float UiScaleDefault = 1f;
        public const float UiScaleMin = 0.85f;
        public const float UiScaleMax = 1.15f;

        public float UiScale { get; private set; } = UiScaleDefault;

        public GameSettingsData Current { get; private set; }

        public void Load()
        {
            Current = LocalJsonStore.Load(SettingsFile, new GameSettingsData());
            UiScale = ClampSetting(PlayerPrefs.GetFloat(UiScaleKey, UiScaleDefault), UiScaleDefault, UiScaleMin, UiScaleMax);
            if (Current.laps < 3)
            {
                Current.laps = 5;
            }

            Current.aiOpponentCount = 21;

            Current.masterVolume = Mathf.Clamp01(Current.masterVolume);
            Current.engineVolume = Mathf.Clamp01(Current.engineVolume);
            Current.uiVolume = Mathf.Clamp01(Current.uiVolume);
            Current.radioVolume = Mathf.Clamp01(Current.radioVolume);
            Current.ambienceVolume = Mathf.Clamp01(Current.ambienceVolume);

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
            Current.controllerVibration = Mathf.Clamp01(Current.controllerVibration);
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

            MigrateLegacyStrategyFields();
            Current.plannedStopCount = Mathf.Clamp(Current.plannedStopCount <= 0 ? 1 : Current.plannedStopCount, 1, 2);
            Current.plannedPitLapOne = Mathf.Clamp(Current.plannedPitLapOne, 0, 99);
            Current.plannedPitLapTwo = Mathf.Clamp(Current.plannedPitLapTwo, 0, 99);
            if (string.IsNullOrEmpty(Current.plannedStopOneCompound))
            {
                Current.plannedStopOneCompound = "Hard";
            }

            if (string.IsNullOrEmpty(Current.plannedStopTwoCompound))
            {
                Current.plannedStopTwoCompound = "Medium";
            }

            Current.safetyCarFrequency = Mathf.Clamp(Current.safetyCarFrequency, 0, 3);
            Current.mechanicalFailureMode = Mathf.Clamp(Current.mechanicalFailureMode, 0, 2);

            Current.engineerMessageVerbosity = Mathf.Clamp(Current.engineerMessageVerbosity, 0, 3);
            Current.racePresentation = Mathf.Clamp(Current.racePresentation, 0, 2);
            Current.weatherVariability = Mathf.Clamp(Current.weatherVariability, 0, 3);
            Current.cameraShakeLevel = Mathf.Clamp(Current.cameraShakeLevel, 0, 3);
        }

        // Older saves only know plannedPitLap/plannedSecondCompound (single mandatory
        // stop). The first time a save with those set is loaded under the new
        // stop-indexed fields, carry the old choice forward as stop 1 so existing
        // players don't lose their plan.
        void MigrateLegacyStrategyFields()
        {
            // Run ONCE per settings file. This had no completion flag, so it
            // executed on every single Load() and both branches stayed
            // re-satisfiable forever - there was no way to tell "this is the legacy
            // default" from "the player deliberately chose this". The compound
            // branch was the visible one: plannedStopOneCompound defaults to "Hard"
            // and plannedSecondCompound defaults to "Medium" and is written by no UI
            // anywhere, so a player who set their first stop to Hard found it
            // silently back on Medium at the next launch, every time. The lap branch
            // had the same shape: with a legacy plannedPitLap set, stop 1 could
            // never be returned to lap 0 ("engineer's choice").
            if (Current.legacyStrategyFieldsMigrated)
            {
                return;
            }

            Current.legacyStrategyFieldsMigrated = true;

            if (Current.plannedPitLapOne <= 0 && Current.plannedPitLap > 0)
            {
                Current.plannedPitLapOne = Current.plannedPitLap;
            }

            if (Current.plannedStopOneCompound == "Hard" && !string.IsNullOrEmpty(Current.plannedSecondCompound))
            {
                Current.plannedStopOneCompound = Current.plannedSecondCompound;
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

        // Sets and immediately persists the UI scale so it survives a restart
        // even though it lives outside the main JSON settings blob. Callers
        // (the Display Settings screen) should also push the new value into
        // UiFactory.GlobalUiScale / RuntimeUi's canvas scaler right away so the
        // change is visible without leaving the screen.
        public void SetUiScale(float value)
        {
            UiScale = Mathf.Clamp(value, UiScaleMin, UiScaleMax);
            PlayerPrefs.SetFloat(UiScaleKey, UiScale);
            PlayerPrefs.Save();
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
