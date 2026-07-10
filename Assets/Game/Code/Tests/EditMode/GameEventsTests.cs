using System;
using F1Game.Core.Events;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class GameEventsTests
    {
        [SetUp]
        public void SetUp()
        {
            GameEvents.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            GameEvents.Reset();
        }

        [Test]
        public void SubscribersReceivePublishedEvents()
        {
            PenaltyIssuedEvent received = default;
            int calls = 0;
            GameEvents.Subscribe<PenaltyIssuedEvent>(e => { received = e; calls++; });

            GameEvents.Publish(new PenaltyIssuedEvent("ver", PenaltyKind.TimePenalty, 5f, "Track limits"));

            Assert.AreEqual(1, calls);
            Assert.AreEqual("ver", received.DriverId);
            Assert.AreEqual(5f, received.Seconds);
        }

        [Test]
        public void UnsubscribeStopsDelivery()
        {
            int calls = 0;
            Action<LapCompletedEvent> handler = _ => calls++;
            GameEvents.Subscribe(handler);
            GameEvents.Publish(new LapCompletedEvent("ham", 1, 92.4f, true, true));
            GameEvents.Unsubscribe(handler);
            GameEvents.Publish(new LapCompletedEvent("ham", 2, 91.9f, true, true));

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void ThrowingSubscriberDoesNotStarveLaterSubscribers()
        {
            int delivered = 0;
            Type errorType = null;
            GameEvents.HandlerError += (type, _) => errorType = type;
            GameEvents.Subscribe<FlagChangedEvent>(_ => throw new InvalidOperationException("boom"));
            GameEvents.Subscribe<FlagChangedEvent>(_ => delivered++);

            GameEvents.Publish(new FlagChangedEvent(FlagState.Green, FlagState.SafetyCar));

            Assert.AreEqual(1, delivered, "The subscriber after the throwing one must still run");
            Assert.AreEqual(typeof(FlagChangedEvent), errorType);
        }

        [Test]
        public void ResetClearsAllChannels()
        {
            int calls = 0;
            GameEvents.Subscribe<RetirementEvent>(_ => calls++);
            GameEvents.Reset();
            GameEvents.Publish(new RetirementEvent("sai", "Engine"));

            Assert.AreEqual(0, calls);
            Assert.AreEqual(0, GameEvents.SubscriberCount<RetirementEvent>());
        }

        [Test]
        public void EventsAreIsolatedPerType()
        {
            int penaltyCalls = 0;
            GameEvents.Subscribe<PenaltyIssuedEvent>(_ => penaltyCalls++);
            GameEvents.Publish(new RetirementEvent("nor", "Gearbox"));
            Assert.AreEqual(0, penaltyCalls);
        }
    }
}
