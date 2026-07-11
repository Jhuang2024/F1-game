using System.Collections.Generic;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager running-order subsystem (partial). Ticks the classification and
    /// returns a snapshot of the current running order, and records a completed AI
    /// overtake for the post-race diagnostics. Split out of the RaceManager monolith
    /// verbatim - same class, same members, identical ordering; the public entry
    /// points stay public so the AI and consumers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        public List<RaceParticipant> GetRunningOrderSnapshot()
        {
            SortRunningOrder();
            return State != null ? new List<RaceParticipant>(State.SortedOrder) : new List<RaceParticipant>();
        }

        // Called by AiVehicleController on the AttackingInside/AttackingOutside/
        // SideBySide -> CompletingPass edge, once per completed overtake, for the
        // post-race diagnostics log.
        public void ReportAiOvertakeCompleted(RaceParticipant participant)
        {
            AiOvertakesCompletedCount++;
            if (participant != null)
            {
                participant.overtakesCompleted++;
            }
        }

        void SortRunningOrder()
        {
            if (State != null) State.Tick();
        }

    }
}
