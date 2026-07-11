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

    [Serializable]
    public class StandingsRowModel
    {
        public int position;
        public string name;
        public string detail;    // team for a driver row; empty for a team row
        public int points;
        public int wins;
        public bool isPlayer;
    }

    [Serializable]
    public class CalendarRowModel
    {
        public int round;
        public string trackName;
        public string country;
        public int laps;
        public bool isDone;
        public bool isNext;
    }

    [Serializable]
    public class CareerStandingsModel
    {
        public string seasonLabel;  // e.g. "Season 2 · Round 5/22"
        public List<StandingsRowModel> drivers = new List<StandingsRowModel>();
        public List<StandingsRowModel> teams = new List<StandingsRowModel>();
        public List<CalendarRowModel> calendar = new List<CalendarRowModel>();
    }

    [Serializable]
    public class SettingsRowModel
    {
        public string label;    // "Race Laps"
        public string value;    // "5" / "Expert" / "On"
        public bool isHeading;  // true = category divider row, value ignored
    }

    [Serializable]
    public class SettingsModel
    {
        public List<SettingsRowModel> rows = new List<SettingsRowModel>();
        // Inline accessibility quick-toggle button labels (e.g. "Units: MPH").
        public string unitsToggleLabel = "";
        public string cameraShakeToggleLabel = "";
        public string compactHudToggleLabel = "";
        public string uiAnimationsToggleLabel = "";
    }

    [Serializable]
    public class CareerHubModel
    {
        public string seasonLabel;      // "Season 2 · Round 5/22"
        public string nextEventName;    // "Italian Grand Prix"
        public string nextEventDetail;  // "Italy · 24 laps · Variable"
        public string continueLabel;    // "Continue: Qualifying" / "Continue: Race"
        public string standingLine;     // "P3 · 86 pts · 2 wins" (empty pre-season)
    }

    [Serializable]
    public class ProfileStatModel
    {
        public string label;
        public string value;
    }

    [Serializable]
    public class DriverProfileModel
    {
        public string driverName;
        public string teamLine;     // "Aurora Racing · Season 3"
        public List<ProfileStatModel> stats = new List<ProfileStatModel>();
    }

    [Serializable]
    public class ResultRowModel
    {
        public int position;
        public string code;         // driver name / abbreviation
        public string team;
        public string gapText;      // winner's total time, "+N.Ns", or "DNF"
        public string bestLapText;
        public int pitStops;
        public int points;
        public string penaltyText;  // "+Ns", a DNF reason, or "--"
        public bool isPlayer;
        public bool dnf;
    }

    [Serializable]
    public class ResultsModel
    {
        public string title;        // "RACE RESULT"
        public string subtitle;     // event / lap context
        public bool isCareer;
        public string primaryActionLabel;  // "Continue Career" / "Race Again"
        // Race classification shows pit/points columns; the qualifying variant
        // drops them (gapText carries the best lap, penaltyText the elim tag).
        public bool showRaceColumns = true;
        public List<ResultRowModel> rows = new List<ResultRowModel>();
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
