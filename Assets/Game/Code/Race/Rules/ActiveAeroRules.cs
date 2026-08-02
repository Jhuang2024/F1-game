namespace F1Game.Race.Rules
{
    /// <summary>
    /// Aerodynamic and overtaking-aid rules for the 2026 Formula 1 regulations.
    ///
    /// This replaces DrsRules, which modelled the 2011-2025 DRS: a rear flap that
    /// only opened for a car within one second of the car ahead, inside a designated
    /// zone. That is not how the current cars work, and the game's own data has been
    /// on the 2026 season all along (see carPerformance.json - every chassis id is
    /// _2026), so the ruleset and the field it was being applied to disagreed.
    ///
    /// Two separate things replace DRS, and the whole point is that they are
    /// separate:
    ///
    /// 1. ACTIVE AERO. Every car has movable front AND rear wings with a low-drag
    ///    "X-mode" for straights and a high-downforce "Z-mode" for corners. It is
    ///    available to EVERY car in the designated activation zones - it is not an
    ///    overtaking aid and carries no gap requirement. Modelling it with the old
    ///    one-second gate meant the leader in clear air ran the entire race dragging
    ///    a wing the real car would have folded flat down every straight.
    ///
    /// 2. OVERRIDE MODE. This is the overtaking aid. A car within one second of the
    ///    car ahead gets extra MGU-K deployment - the standard deployment tapers off
    ///    from around 290 km/h, Override holds full power much closer to top speed -
    ///    from a limited extra energy budget. It is NOT tied to the activation zones;
    ///    a driver can use it wherever they are close enough.
    ///
    /// Engine-free (F1Game.Race has no UnityEngine reference).
    /// </summary>
    public static class ActiveAeroRules
    {
        /// <summary>
        /// Gap to the car ahead, in seconds, within which Override Mode is armed.
        /// </summary>
        public const float OverrideGapSeconds = 1f;

        /// <summary>
        /// Override is unavailable until a car has completed this many laps. The real
        /// regulation holds the overtaking aid for the opening laps of a race; with
        /// this game's shorter distances one lap is the equivalent gate.
        /// </summary>
        public const int MinCompletedLapsForOverride = 1;

        /// <summary>
        /// Extra deployment energy Override may spend per lap, as a fraction of the
        /// battery. Spending it is what stops Override being a permanent free boost
        /// for anyone sitting in a train - a driver who holds it down all lap has
        /// nothing left for the move that matters.
        /// </summary>
        public const float OverrideEnergyPerLap01 = 0.35f;

        /// <summary>
        /// Rate the Override budget drains while deployed, as a fraction per second.
        /// </summary>
        public const float OverrideDrainPerSecond01 = 0.14f;

        /// <summary>
        /// Whether the movable wings may go to low-drag X-mode right now.
        ///
        /// No gap requirement and no eligibility to earn - in 2026 this is ordinary
        /// aerodynamics available to the whole field. Wet running, a flag that
        /// forbids it and the post-restart cooldown all still lock the wings shut,
        /// exactly as race control does in reality, and it only applies inside a
        /// designated activation zone.
        /// </summary>
        public static bool WingModeAvailable(
            bool isWet,
            bool restartCooldownActive,
            bool flagAllowsMovableAero,
            int zoneIndex)
        {
            if (isWet || restartCooldownActive || !flagAllowsMovableAero)
            {
                return false;
            }

            return zoneIndex != 0;
        }

        /// <summary>
        /// Whether Override Mode is armed: within a second of the car ahead, past the
        /// opening-lap gate, with budget left, and not under a flag that forbids it.
        /// Deliberately NOT zone-gated - that is the substantive difference from DRS.
        /// Qualifying and time trial have no car ahead to chase, so Override does not
        /// apply there at all; the wings above still do.
        /// </summary>
        public static bool OverrideAvailable(
            bool isWet,
            bool restartCooldownActive,
            bool flagAllowsMovableAero,
            bool isQualifyingOrTimeTrial,
            int completedLaps,
            float intervalToAheadSeconds,
            float overrideEnergyRemaining01)
        {
            if (isQualifyingOrTimeTrial || restartCooldownActive || !flagAllowsMovableAero)
            {
                return false;
            }

            // Unlike the wings, Override is pure electrical deployment, so rain does
            // not disarm it - it just makes it harder to use.
            if (isWet && intervalToAheadSeconds > OverrideGapSeconds)
            {
                return false;
            }

            if (completedLaps < MinCompletedLapsForOverride)
            {
                return false;
            }

            if (overrideEnergyRemaining01 <= 0f)
            {
                return false;
            }

            return intervalToAheadSeconds <= OverrideGapSeconds;
        }
    }
}
