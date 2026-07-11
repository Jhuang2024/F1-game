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

        public float DeltaSeconds; // to reference lap (+ slower)
        public bool HasDelta;

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
}
