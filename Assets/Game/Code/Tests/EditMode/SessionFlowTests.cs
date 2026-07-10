using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class SessionFlowTests
    {
        [Test]
        public void CareerWeekendRoutesThroughQualifyingFirst()
        {
            Assert.AreEqual(WeekendSession.Qualifying, SessionFlow.NextCareerStep(hasQualifyingForRound: false));
            Assert.AreEqual(WeekendSession.Race, SessionFlow.NextCareerStep(hasQualifyingForRound: true));
        }

        [Test]
        public void RacingSessionClassificationCoversRaceAndQuickRace()
        {
            Assert.IsTrue(SessionFlow.IsRacingSession(WeekendSession.Race));
            Assert.IsTrue(SessionFlow.IsRacingSession(WeekendSession.QuickRace));
            Assert.IsFalse(SessionFlow.IsRacingSession(WeekendSession.Qualifying));
            Assert.IsFalse(SessionFlow.IsRacingSession(WeekendSession.Practice));
        }
    }
}
