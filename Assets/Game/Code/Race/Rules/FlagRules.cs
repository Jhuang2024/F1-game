namespace F1Game.Race.Rules
{
    /// <summary>
    /// The full flag set and the pure rules governing what each flag permits
    /// (overtaking, DRS, pit, pace). Extends the existing extracted rulebook so
    /// race-control flag policy lives in one testable place rather than scattered
    /// conditionals. Detection (who trips a flag) stays with the race layer; the
    /// consequences live here.
    /// </summary>
    public enum RaceFlag
    {
        Green,
        LocalYellow,
        DoubleYellow,
        FullCourseYellow,
        VirtualSafetyCar,
        SafetyCar,
        Red,
        Blue,
        Black,
        BlackWhiteWarning,
        MechanicalBlackOrange,
        White,       // slow vehicle on track
        Chequered,
    }

    public static class FlagRules
    {
        // Numeric authority for the fixed caution speed caps (kph). The race
        // layer owns the safety-car target speed (it varies with the queue);
        // these two are constants of the rulebook.
        public const float VirtualSafetyCarSpeedCapKph = 190f;
        public const float LocalYellowSpeedCapKph = 210f;
        // Red flag: the field must come to a controlled stop and form up for a
        // standing restart. Deliberately well below the VSC cap - this is a
        // "slow right down", not a delta pace.
        public const float RedFlagSpeedCapKph = 80f;

        // Blue-flag detection: shown to a car about to be lapped once the
        // lapping car closes within this time gap behind it. A car a full lap
        // (or more) of total progress ahead counts as lapping.
        public const float BlueFlagGapSeconds = 2f;
        public const float BlueFlagLapProgressFraction = 0.98f;

        /// <summary>Overtaking permitted under this flag (for cars not being lapped).</summary>
        public static bool OvertakingAllowed(RaceFlag flag)
        {
            switch (flag)
            {
                case RaceFlag.LocalYellow:
                case RaceFlag.DoubleYellow:
                case RaceFlag.FullCourseYellow:
                case RaceFlag.VirtualSafetyCar:
                case RaceFlag.SafetyCar:
                case RaceFlag.Red:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>DRS is disabled whenever any caution is shown.</summary>
        public static bool DrsAllowed(RaceFlag flag)
        {
            return flag == RaceFlag.Green || flag == RaceFlag.Blue || flag == RaceFlag.White;
        }

        /// <summary>A car must reduce to a delta pace under these flags.</summary>
        public static bool RequiresPaceControl(RaceFlag flag)
        {
            // Red belongs here: a red flag is the strongest pace restriction there
            // is, but it was omitted, so IsRaceControlPaceLimited was false during a
            // red and the player's soft limiter (and the overspeed penalty timer)
            // never engaged for it.
            return flag == RaceFlag.VirtualSafetyCar
                || flag == RaceFlag.SafetyCar
                || flag == RaceFlag.FullCourseYellow
                || flag == RaceFlag.Red;
        }

        /// <summary>Blue flag: the shown car must let faster (lapping) traffic by.</summary>
        public static bool MustYield(RaceFlag flag)
        {
            return flag == RaceFlag.Blue;
        }

        /// <summary>Black / black-orange end or restrict a car's participation.</summary>
        public static bool EndsParticipation(RaceFlag flag)
        {
            return flag == RaceFlag.Black;
        }

        public static bool RequiresPitForRepair(RaceFlag flag)
        {
            return flag == RaceFlag.MechanicalBlackOrange;
        }
    }
}
