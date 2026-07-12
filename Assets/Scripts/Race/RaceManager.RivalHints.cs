using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager rival/teammate hint subsystem (partial). A short characterising
    /// hint about a rival's driving traits (for radio/engineer colour) and the
    /// teammate lookup. Split out of the RaceManager monolith verbatim - same
    /// class, same members, identical selection; the public FindTeammate stays
    /// public so external callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        // Part 8: a short trait-flavored aside for the rival radio callout - only
        // fires for the traits that actually change how to race them.
        string RivalTraitHint(RaceParticipant rival)
        {
            if (rival == null || rival.driverData == null)
            {
                return "";
            }

            List<string> traits = DriverTraits.Compute(rival.driverData);
            if (traits.Contains("Aggressive Overtaker"))
            {
                return " He attacks early, don't leave a gap.";
            }

            if (traits.Contains("Defensive Wall"))
            {
                return " He defends hard, get a clean run before you commit.";
            }

            if (traits.Contains("Error-Prone"))
            {
                return " He's error-prone under pressure, stay close.";
            }

            return "";
        }

        public RaceParticipant FindTeammate(RaceParticipant participant)
        {
            if (participant == null || State == null)
            {
                return null;
            }

            for (int i = 0; i < State.Participants.Count; i++)
            {
                RaceParticipant candidate = State.Participants[i];
                if (candidate != null && candidate != participant && candidate.teamId == participant.teamId)
                {
                    return candidate;
                }
            }

            return null;
        }

    }
}
