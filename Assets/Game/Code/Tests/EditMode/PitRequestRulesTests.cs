using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class PitRequestRulesTests
    {
        static PitRequestContext CancellableBaseline()
        {
            return new PitRequestContext
            {
                IsQualifying = false,
                IsTimeTrial = false,
                RaceFinished = false,
                ParticipantRetired = false,
                ParticipantFinished = false,
                ManualPitRequested = true,
                ManualPitCommitted = false,
                Origin = PitRequestOrigin.Manual,
                VehiclePitRequested = true,
                InPitSequence = false,
                IsPitting = false,
                PitEntryCommitted = false,
                CrossedPitEntryLimiterLine = false,
            };
        }

        [Test]
        public void BaselineManualRequestIsCancellable()
        {
            Assert.IsTrue(PitRequestRules.CanCancel(CancellableBaseline()));
        }

        [Test]
        public void SafetyCarPromptRequestIsCancellableLikeManual()
        {
            var context = CancellableBaseline();
            context.Origin = PitRequestOrigin.SafetyCarPrompt;
            Assert.IsTrue(PitRequestRules.CanCancel(context));
        }

        [Test]
        public void PreRacePlanRequestIsNeverCancellable()
        {
            var context = CancellableBaseline();
            context.Origin = PitRequestOrigin.PreRacePlan;
            Assert.IsFalse(PitRequestRules.CanCancel(context));
        }

        [Test]
        public void CommitmentLocksOutCancellation()
        {
            var committed = CancellableBaseline();
            committed.ManualPitCommitted = true;
            Assert.IsFalse(PitRequestRules.CanCancel(committed));

            var inSequence = CancellableBaseline();
            inSequence.InPitSequence = true;
            Assert.IsFalse(PitRequestRules.CanCancel(inSequence));

            var entryCommitted = CancellableBaseline();
            entryCommitted.PitEntryCommitted = true;
            Assert.IsFalse(PitRequestRules.CanCancel(entryCommitted));

            // The live limiter-line probe must lock out even when cached flags lag.
            var crossedLine = CancellableBaseline();
            crossedLine.CrossedPitEntryLimiterLine = true;
            Assert.IsFalse(PitRequestRules.CanCancel(crossedLine));
        }

        [Test]
        public void SessionAndParticipantStateGate()
        {
            var qualifying = CancellableBaseline();
            qualifying.IsQualifying = true;
            Assert.IsFalse(PitRequestRules.CanCancel(qualifying));

            var finishedRace = CancellableBaseline();
            finishedRace.RaceFinished = true;
            Assert.IsFalse(PitRequestRules.CanCancel(finishedRace));

            var retired = CancellableBaseline();
            retired.ParticipantRetired = true;
            Assert.IsFalse(PitRequestRules.CanCancel(retired));

            var noVehicleLatch = CancellableBaseline();
            noVehicleLatch.VehiclePitRequested = false;
            Assert.IsFalse(PitRequestRules.CanCancel(noVehicleLatch));
        }

        [Test]
        public void RaceControlOfferRequiresLiveOfferAndOpenPitEntry()
        {
            Assert.IsTrue(PitRequestRules.CanAcceptRaceControlOffer(true, false));
            Assert.IsFalse(PitRequestRules.CanAcceptRaceControlOffer(false, false));
            Assert.IsFalse(PitRequestRules.CanAcceptRaceControlOffer(true, true));
        }
    }
}
