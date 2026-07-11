namespace F1Game.Core
{
    /// <summary>
    /// Per-frame player telemetry snapshot the production HUD modules read.
    /// Populated once per frame by the Assembly-CSharp race layer (RaceEventRelay)
    /// so HUD widgets never poll the giant race manager directly — they read this
    /// value type and subscribe to the typed event bus for discrete events.
    /// </summary>
    public struct HudTelemetrySnapshot
    {
        public bool Valid;

        public int Position;
        public int FieldSize;
        public int Lap;
        public int TotalLaps;
        public float SessionClockSeconds;

        public float SpeedKph;
        public int Gear;          // -1 reverse, 0 neutral, 1..8
        public float Rpm01;

        public float Ers01;       // battery charge
        public bool DrsActive;
        public bool DrsAvailable;

        public float Fuel01;
        public float FuelLapsRemaining;

        public int TyreCompound;  // 0 soft .. 4 wet
        public float TyreWear01;  // 0 fresh .. 1 worn
        public float BrakeTemp01;
        public string TyreTempStatus; // COLD / EDGE / OPT / HOT
        public float LockupSeverity;  // 0 none .. 1 heavy lockup

        // Player input telemetry (0..1) for the pedal bars.
        public float Throttle01;
        public float Brake01;

        public float DeltaSeconds; // to reference lap / ghost (+ slower)
        public bool HasDelta;

        // Slipstream tow (Tier 2 parity): strength drives the pill; the source
        // code names the car being followed.
        public float SlipstreamStrength;   // 0 = clear air
        public float SlipstreamBonusKph;
        public string SlipstreamSourceCode;

        // Relative gaps to the cars immediately around the player (seconds).
        public float GapAheadSeconds;
        public bool HasGapAhead;
        public float GapBehindSeconds;
        public bool HasGapBehind;

        // Race-control / flag context for the flag module.
        public Events.FlagState Flag;
        public bool BlueFlag;

        // Pit + penalty status chips.
        public bool PitLimiterActive;
        public bool IsPitting;
        public float PenaltySeconds;

        // Start-light sequence (0 lights when the sequence isn't showing).
        public int StartLightCount;
        public bool StartLightsVisible;

        // Lap timing (seconds; 0 / negative = not set yet).
        public float CurrentLapSeconds;
        public float LastLapSeconds;
        public bool LastLapInvalidated;
        public float BestLapSeconds;
        public float SessionBestSeconds;

        // Live weather for the conditions chip.
        public Events.WeatherKind Weather;

        // Session identity (Tier 4 parity): kind + event display name.
        public Events.SessionKind Session;
        public string EventName;

        // Session-specific (Tier 7): qualifying lap feedback text, time-trial
        // checkpoint progress this lap.
        public string QualifyingFeedback;
        public int CheckpointsPassed;

        // Rolling session message (race layer's one-line status: "Lights
        // out", pit calls, reaction time...). Empty when nothing current.
        public string SessionMessage;

        // Race-control surface (Tier 3 parity): pace enforcement and restart
        // context beyond the one-word flag.
        public bool PaceLimited;
        public float PaceCapKph;          // 9999 = uncapped
        public float RestartCountdownSeconds;
        public string RaceControlDetail;  // red-flag reason / SC queue line; empty otherwise

        // Damage + fuel emergency states and pit service progress.
        public float Damage01;            // 0 pristine .. 1 destroyed
        public bool FuelStarved;
        public float PitStopProgress01;   // 0 outside service, else 0..1

        // Pit strategy surface (Tier 2 parity).
        public bool CanCancelPit;         // a manual/SC pit request can still be cancelled
        public bool PitRequested;         // team has been asked to box
        public int NextPlannedPitLap;     // 0 = no remaining planned stop
        public int NextPlannedPitCompound; // 0 soft .. 4 wet
        public bool BoxThisLap;           // planned lap is the one being driven
        public bool ScPitWindowOpen;      // race control recommends boxing under SC/VSC

        // Last completed sector times this lap (0 = not set yet) and the
        // session-wide best per sector for purple/green comparison.
        public float Sector1Seconds;
        public float Sector2Seconds;
        public float Sector3Seconds;
        public float BestSector1Seconds;
        public float BestSector2Seconds;
        public float BestSector3Seconds;
    }

    /// <summary>Latest telemetry snapshot (single-player, main car).</summary>
    public static class HudTelemetry
    {
        public static HudTelemetrySnapshot Current;

        public static void Publish(in HudTelemetrySnapshot snapshot)
        {
            Current = snapshot;
        }

        public static void Clear()
        {
            Current = default;
        }
    }

    /// <summary>One row of the running order, as the timing tower renders it.</summary>
    public struct HudRaceOrderEntry
    {
        public int Position;
        public string Code;
        public float GapToLeaderSeconds;
        public float IntervalSeconds;   // gap to the car one place ahead
        public int Compound;            // 0 soft .. 4 wet
        public bool DrsActive;          // wing currently open
        public bool IsPlayer;
        public bool InPit;
        public bool Retired;
    }

    /// <summary>
    /// Low-cadence running-order snapshot for the timing tower. Published by
    /// the race layer a few times a second into a fixed buffer (no per-frame
    /// allocation on either side).
    /// </summary>
    public static class HudRaceOrder
    {
        public const int MaxEntries = 32;
        public static readonly HudRaceOrderEntry[] Entries = new HudRaceOrderEntry[MaxEntries];
        public static int Count;

        public static void Clear()
        {
            Count = 0;
        }
    }
}
