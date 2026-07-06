using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    [Serializable]
    public class TeamDatabase
    {
        public List<TeamData> teams = new List<TeamData>();
    }

    [Serializable]
    public class TeamData
    {
        public string id;
        public string name;
        public string shortName;
        public string primaryColor;
        public string secondaryColor;
        public string carPerformanceId;
        public int reliability;
        public int reputation;

        public Color PrimaryUnityColor
        {
            get
            {
                Color color;
                return ColorUtility.TryParseHtmlString(primaryColor, out color) ? color : Color.white;
            }
        }

        public Color SecondaryUnityColor
        {
            get
            {
                Color color;
                return ColorUtility.TryParseHtmlString(secondaryColor, out color) ? color : Color.black;
            }
        }
    }

    [Serializable]
    public class DriverDatabase
    {
        public List<DriverData> drivers = new List<DriverData>();
    }

    [Serializable]
    public class DriverData
    {
        public string id;
        public string displayName;
        public string abbreviation;
        public int number;
        public string teamId;
        public int pace;
        public int racecraft;
        public int qualifying;
        public int tyreManagement;
        public int wetSkill;
        public int consistency;
        public int aggression;
        public int defending;
        public int overtaking;
        public int awareness;
        public int experience;
        public int developmentPotential;

        public float AverageRating
        {
            get
            {
                return (qualifying + defending + overtaking + pace) / 4f;
            }
        }

        public int OverallRating
        {
            get { return Mathf.RoundToInt(AverageRating); }
        }
    }

    [Serializable]
    public class CalendarDatabase
    {
        public List<CalendarEventData> events = new List<CalendarEventData>();
    }

    [Serializable]
    public class CalendarEventData
    {
        public int round;
        public string trackId;
        public string displayName;
        public string country;
        public int laps3;
        public int laps5;
        public int laps25Percent;
        public string weatherProfile;
    }

    [Serializable]
    public class CarPerformanceDatabase
    {
        public List<CarPerformanceData> cars = new List<CarPerformanceData>();
    }

    [Serializable]
    public class CarPerformanceData
    {
        public string id;
        public int topSpeed;
        public int acceleration;
        public int cornering;
        public int braking;
        public int reliability;
        public int ersEfficiency;
        public int tyreManagement;
        public int aeroEfficiency;
        public int chassisBalance;
        public int enginePower;
    }

    [Serializable]
    public class UpgradeDatabase
    {
        public List<UpgradeData> upgrades = new List<UpgradeData>();
    }

    [Serializable]
    public class UpgradeData
    {
        public string id;
        public string category;
        public string displayName;
        public int cost;
        public int developmentDays;
        public float successChance;
        public string requiredUpgradeId;
        public int topSpeedDelta;
        public int accelerationDelta;
        public int corneringDelta;
        public int brakingDelta;
        public int reliabilityDelta;
        public int ersDelta;
        public int tyreDelta;
        public int aeroDelta;
        public int chassisDelta;
        public int engineDelta;
    }

    [Serializable]
    public class CareerSaveData
    {
        public int currentSeason = 1;
        public int currentRound = 1;
        public string playerDriverName = "Player Driver";
        public string playerTeamId = "williams";
        public bool useExistingDriver;
        public string selectedDriverId = "";
        public string rivalDriverId = "";
        public int contractTargetPosition = 8;
        public int reputation = 25;
        public int resourcePoints = 500;
        public int difficultyIndex = 1;
        public List<StandingEntry> driverStandings = new List<StandingEntry>();
        public List<StandingEntry> constructorStandings = new List<StandingEntry>();
        public List<RaceResultRecord> raceResults = new List<RaceResultRecord>();
        public List<QualifyingResultRecord> qualifyingResults = new List<QualifyingResultRecord>();
        public List<QualifyingResultEntry> lastQualifyingResults = new List<QualifyingResultEntry>();
        public List<string> completedUpgradeIds = new List<string>();
        public List<string> failedUpgradeIds = new List<string>();
    }

    [Serializable]
    public class StandingEntry
    {
        public string id;
        public string displayName;
        public string teamId;
        public int points;
        public int wins;
        public int podiums;
    }

    [Serializable]
    public class RaceResultRecord
    {
        public int season;
        public int round;
        public string eventName;
        public List<RaceResultEntry> results = new List<RaceResultEntry>();
    }

    [Serializable]
    public class RaceResultEntry
    {
        public string driverId;
        public string driverName;
        public string teamId;
        public int finishingPosition;
        public int gridPosition;
        public int completedLaps;
        public float totalTime;
        public float bestLapTime;
        public float penaltiesSeconds;
        public string penaltyReason;
        public int points;
        public bool isPlayer;
        public string tyreCompound;
    }

    [Serializable]
    public class QualifyingResultRecord
    {
        public int season;
        public int round;
        public string eventName;
        public List<QualifyingResultEntry> results = new List<QualifyingResultEntry>();
    }

    [Serializable]
    public class QualifyingResultEntry
    {
        public string driverId;
        public string driverName;
        public string teamId;
        public int position;
        public float bestLapTime;
        public bool isPlayer;
        public bool invalidated;
        public string session;
        public string eliminatedIn;
    }

    [Serializable]
    public class GameSettingsData
    {
        public int laps = 5;
        public int difficultyIndex = 1;
        public bool manualGears;
        public bool cameraShake = true;
        public bool audioEnabled = true;
        public string tyreCompound = "Medium";
        public int aiOpponentCount = 21;
        public bool autoBrakeAssist;
        public bool absAssist = true;
        public bool tractionControl = true;
        public bool racingLineAssist = true;
        public float steeringSensitivity = 1f;
        public float throttleSensitivity = 1f;
        public float brakeSensitivity = 1f;
        public float controllerDeadzone = 0.12f;
        public int ersMode;
        public int raceLengthPreset = 1;
    }

    public enum RaceDifficulty
    {
        Easy,
        Medium,
        Hard,
        Expert
    }

    public enum TyreCompound
    {
        Soft,
        Medium,
        Hard,
        Intermediate,
        Wet
    }

    public enum WeatherState
    {
        Clear,
        Cloudy,
        LightRain,
        HeavyRain
    }

    public enum RaceWeekendSession
    {
        QuickRace,
        Qualifying,
        Race
    }

    public enum ErsStrategyMode
    {
        Balanced,
        Attack,
        Harvest
    }
}
