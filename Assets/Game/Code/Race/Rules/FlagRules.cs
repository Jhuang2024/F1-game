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
            return flag == RaceFlag.VirtualSafetyCar
                || flag == RaceFlag.SafetyCar
                || flag == RaceFlag.FullCourseYellow;
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
