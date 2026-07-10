namespace F1Game.Race.Rules
{
    /// <summary>
    /// Origin of an active pit request. Mirrors the monolith's
    /// <c>PitRequestSource</c> (RaceParticipant.cs) one-to-one so the boundary
    /// mapping is a straight cast-free switch.
    /// </summary>
    public enum PitRequestOrigin
    {
        None,
        PreRacePlan,
        Manual,
        SafetyCarPrompt,
    }

    /// <summary>
    /// Snapshot of everything the cancel/accept decisions read. Filled at the
    /// boundary from RaceParticipant/VehicleController/Track state.
    /// </summary>
    public struct PitRequestContext
    {
        public bool IsQualifying;
        public bool IsTimeTrial;
        public bool RaceFinished;
        public bool ParticipantRetired;
        public bool ParticipantFinished;

        public bool ManualPitRequested;
        public bool ManualPitCommitted;
        public PitRequestOrigin Origin;
        public bool VehiclePitRequested;

        /// <summary>True when pitPhase != None.</summary>
        public bool InPitSequence;
        public bool IsPitting;
        public bool PitEntryCommitted;

        /// <summary>Live check against the pit-entry limiter line (authoritative commitment boundary).</summary>
        public bool CrossedPitEntryLimiterLine;
    }

    /// <summary>
    /// Pure decision logic for player pit requests, extracted from
    /// <c>RaceManager.CanCancelManualPitRequest</c> / <c>AcceptRaceControlPitOffer</c>.
    /// </summary>
    public static class PitRequestRules
    {
        /// <summary>
        /// Mirrors CanCancelManualPitRequest: only an uncommitted Manual or
        /// SafetyCarPrompt request, before any pit-entry commitment (including the
        /// live limiter-line check), can be cancelled. A PreRacePlan request never can.
        /// </summary>
        public static bool CanCancel(in PitRequestContext context)
        {
            if (context.IsQualifying || context.IsTimeTrial || context.RaceFinished ||
                context.ParticipantRetired || context.ParticipantFinished)
            {
                return false;
            }

            if (!context.ManualPitRequested || context.ManualPitCommitted)
            {
                return false;
            }

            if (context.Origin != PitRequestOrigin.Manual && context.Origin != PitRequestOrigin.SafetyCarPrompt)
            {
                return false;
            }

            if (!context.VehiclePitRequested)
            {
                return false;
            }

            if (context.InPitSequence || context.IsPitting || context.PitEntryCommitted)
            {
                return false;
            }

            if (context.CrossedPitEntryLimiterLine)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Mirrors AcceptRaceControlPitOffer's gates: an offer must be live and the
        /// car must not have already missed this lap's pit entry.
        /// </summary>
        public static bool CanAcceptRaceControlOffer(bool offerActive, bool missedPitEntryThisLap)
        {
            return offerActive && !missedPitEntryThisLap;
        }
    }
}
