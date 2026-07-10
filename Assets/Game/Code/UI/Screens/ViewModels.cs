using System;
using System.Collections.Generic;
using UnityEngine;

namespace F1Game.UI.Screens
{
    // Plain view-models: the bridge (Assembly-CSharp) maps monolith data types
    // into these, so no F1Game.UI code ever references legacy classes.

    [Serializable]
    public class MainMenuModel
    {
        public string playerName;
        public string careerSummary;   // e.g. "Season 2 · Round 5/22 · P3 in standings"
        public bool hasCareer;
        public string versionLabel;
    }

    [Serializable]
    public class TrackCardModel
    {
        public string eventId;
        public string trackName;
        public string location;
        public float lengthKm;
        public int laps;
        public string weatherHint;
    }

    [Serializable]
    public class StrategyModel
    {
        public string trackName;
        public int raceLaps;
        public string weatherForecast;
        public string[] compoundNames = { "Soft", "Medium", "Hard", "Intermediate", "Wet" };
        public int selectedCompoundIndex = 1;
        public int plannedStopCount = 1;
        public int plannedPitLapOne;
        public int plannedPitLapTwo;
        public int stopOneCompoundIndex = 2;
        public int stopTwoCompoundIndex = 2;
    }

    /// <summary>The player's confirmed pre-race choices, handed back to the bridge.</summary>
    public struct StrategyChoice
    {
        public int StartCompoundIndex;
        public int PlannedStopCount;
        public int PlannedPitLapOne;
        public int PlannedPitLapTwo;
        public int StopOneCompoundIndex;
        public int StopTwoCompoundIndex;
    }

    public static class CompoundPalette
    {
        /// <summary>Data-identity colours for tyre compounds (soft/medium/hard/inter/wet).</summary>
        public static Color For(int compoundIndex)
        {
            switch (compoundIndex)
            {
                case 0: return new Color(0.91f, 0.22f, 0.19f);
                case 1: return new Color(0.95f, 0.79f, 0.16f);
                case 2: return new Color(0.92f, 0.92f, 0.92f);
                case 3: return new Color(0.23f, 0.72f, 0.35f);
                default: return new Color(0.20f, 0.45f, 0.90f);
            }
        }
    }
}
