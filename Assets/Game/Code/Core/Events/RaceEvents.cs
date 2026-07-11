namespace F1Game.Core.Events
{
    // Race-domain event contracts. These deliberately carry only primitives and
    // enums (no UnityEngine or monolith types) so any assembly — including the
    // pure-logic rules and test assemblies — can consume them.

    public enum SessionKind
    {
        Practice,
        Qualifying,
        Race,
        TimeTrial,
    }

    public enum SessionPhase
    {
        PreSession,
        Formation,
        StartLights,
        Green,
        SafetyCar,
        VirtualSafetyCar,
        RedFlag,
        Chequered,
        Finished,
    }

    public enum FlagState
    {
        Green,
        Yellow,
        DoubleYellow,
        VirtualSafetyCar,
        SafetyCar,
        Red,
        Blue,
        Chequered,
    }

    public enum PenaltyKind
    {
        Warning,
        TrackLimitsWarning,
        TimePenalty,
        DriveThrough,
        StopGo,
        GridDrop,
        Disqualification,
    }

    public enum PitRequestState
    {
        None,
        Requested,
        Confirmed,
        Cancelled,
        EnteredPitLane,
        ServiceStarted,
        ServiceComplete,
        ExitedPitLane,
    }

    public enum WeatherKind
    {
        Clear,
        Overcast,
        LightRain,
        HeavyRain,
        Drying,
        Night,
    }

    public readonly struct SessionStateChangedEvent
    {
        public readonly SessionKind Kind;
        public readonly SessionPhase Previous;
        public readonly SessionPhase Current;

        public SessionStateChangedEvent(SessionKind kind, SessionPhase previous, SessionPhase current)
        {
            Kind = kind;
            Previous = previous;
            Current = current;
        }
    }

    /// <summary>Weekend progression: one session ended and the next was selected.</summary>
    public readonly struct SessionAdvancedEvent
    {
        public readonly SessionKind Completed;
        public readonly SessionKind Next;
        public readonly bool WeekendComplete;

        public SessionAdvancedEvent(SessionKind completed, SessionKind next, bool weekendComplete)
        {
            Completed = completed;
            Next = next;
            WeekendComplete = weekendComplete;
        }
    }

    public readonly struct LapCompletedEvent
    {
        public readonly string DriverId;
        public readonly int LapNumber;
        public readonly float LapTimeSeconds;
        public readonly bool IsPersonalBest;
        public readonly bool IsSessionBest;

        public LapCompletedEvent(string driverId, int lapNumber, float lapTimeSeconds, bool isPersonalBest, bool isSessionBest)
        {
            DriverId = driverId;
            LapNumber = lapNumber;
            LapTimeSeconds = lapTimeSeconds;
            IsPersonalBest = isPersonalBest;
            IsSessionBest = isSessionBest;
        }
    }

    public readonly struct SectorCompletedEvent
    {
        public readonly string DriverId;
        public readonly int SectorIndex;
        public readonly float SectorTimeSeconds;

        public SectorCompletedEvent(string driverId, int sectorIndex, float sectorTimeSeconds)
        {
            DriverId = driverId;
            SectorIndex = sectorIndex;
            SectorTimeSeconds = sectorTimeSeconds;
        }
    }

    public readonly struct PositionChangedEvent
    {
        public readonly string DriverId;
        public readonly int PreviousPosition;
        public readonly int CurrentPosition;

        public PositionChangedEvent(string driverId, int previousPosition, int currentPosition)
        {
            DriverId = driverId;
            PreviousPosition = previousPosition;
            CurrentPosition = currentPosition;
        }
    }

    public readonly struct FlagChangedEvent
    {
        public readonly FlagState Previous;
        public readonly FlagState Current;

        public FlagChangedEvent(FlagState previous, FlagState current)
        {
            Previous = previous;
            Current = current;
        }
    }

    public readonly struct PenaltyIssuedEvent
    {
        public readonly string DriverId;
        public readonly PenaltyKind Kind;
        public readonly float Seconds;
        public readonly string Reason;

        public PenaltyIssuedEvent(string driverId, PenaltyKind kind, float seconds, string reason)
        {
            DriverId = driverId;
            Kind = kind;
            Seconds = seconds;
            Reason = reason;
        }
    }

    public readonly struct PitRequestChangedEvent
    {
        public readonly string DriverId;
        public readonly PitRequestState State;
        public readonly int PlannedLap;

        public PitRequestChangedEvent(string driverId, PitRequestState state, int plannedLap)
        {
            DriverId = driverId;
            State = state;
            PlannedLap = plannedLap;
        }
    }

    public readonly struct WeatherChangedEvent
    {
        public readonly WeatherKind Previous;
        public readonly WeatherKind Current;
        public readonly float Wetness01;

        public WeatherChangedEvent(WeatherKind previous, WeatherKind current, float wetness01)
        {
            Previous = previous;
            Current = current;
            Wetness01 = wetness01;
        }
    }

    public readonly struct DamageReportedEvent
    {
        public readonly string DriverId;
        public readonly string Component;
        public readonly float Severity01;

        public DamageReportedEvent(string driverId, string component, float severity01)
        {
            DriverId = driverId;
            Component = component;
            Severity01 = severity01;
        }
    }

    public readonly struct RetirementEvent
    {
        public readonly string DriverId;
        public readonly string Reason;

        public RetirementEvent(string driverId, string reason)
        {
            DriverId = driverId;
            Reason = reason;
        }
    }

    public readonly struct RadioMessageEvent
    {
        /// <summary>0 = critical (penalty/red flag), 1 = important, 2 = informational.</summary>
        public readonly int Priority;
        public readonly string Speaker;
        public readonly string Message;

        public RadioMessageEvent(int priority, string speaker, string message)
        {
            Priority = priority;
            Speaker = speaker;
            Message = message;
        }
    }

    /// <summary>
    /// A short HUD toast (overtake, position change, session-fastest, tyre/
    /// fuel/lockup/damage warnings, pit-window prompts). The race layer already
    /// queues these; the relay drains that queue and republishes each here so
    /// the production notification feed and legacy HUD share one source.
    /// </summary>
    public readonly struct HudToastEvent
    {
        /// <summary>0 = positive/green, 1 = caution/amber, 2 = neutral.</summary>
        public readonly int Tone;
        public readonly string Message;

        public HudToastEvent(int tone, string message)
        {
            Tone = tone;
            Message = message;
        }
    }
}
