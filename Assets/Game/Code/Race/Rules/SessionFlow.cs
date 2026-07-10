namespace F1Game.Race.Rules
{
    /// <summary>
    /// Weekend session kinds. Mirrors the monolith's <c>RaceWeekendSession</c>
    /// (DataModels.cs) one-to-one; mapped at the boundary.
    /// </summary>
    public enum WeekendSession
    {
        QuickRace,
        Qualifying,
        Race,
        Practice,
    }

    /// <summary>
    /// Weekend/session progression decisions, extracted from the transitions
    /// scattered across GameBootstrap.StartCareerRace, RaceManager.StartSession
    /// and the Q-phase advance in RaceManager.Update.
    /// </summary>
    public static class SessionFlow
    {
        /// <summary>
        /// Career "go racing" decision (GameBootstrap.StartCareerRace): without a
        /// stored qualifying result for the round, the weekend routes to
        /// qualifying first; otherwise straight to the race.
        /// </summary>
        public static WeekendSession NextCareerStep(bool hasQualifyingForRound)
        {
            return hasQualifyingForRound ? WeekendSession.Race : WeekendSession.Qualifying;
        }

        /// <summary>
        /// End-of-phase decision inside qualifying: advance Q1→Q2→Q3, then finish.
        /// Mirrors the qualifyingTransitionTimer branch in RaceManager.Update.
        /// </summary>
        public static bool TryAdvanceQualifyingPhase(int currentPhase, out int nextPhase)
        {
            if (QualifyingProgression.IsFinalPhase(currentPhase))
            {
                nextPhase = currentPhase;
                return false;
            }

            nextPhase = currentPhase + 1;
            return true;
        }

        /// <summary>Sessions where a mandatory pit stop / race classification apply.</summary>
        public static bool IsRacingSession(WeekendSession session)
        {
            return session == WeekendSession.Race || session == WeekendSession.QuickRace;
        }
    }
}
