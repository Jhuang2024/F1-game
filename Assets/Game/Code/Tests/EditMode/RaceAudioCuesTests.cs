using F1Game.Core;
using F1Game.Core.Events;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// Pins the race-audio cue mapping (flag/weather -> bank key) and the radio
    /// interrupt arbitration exactly as RaceAudioDirector drove them, so a refactor
    /// can't silently change which cue fires or which radio message wins the channel.
    /// </summary>
    public class RaceAudioCuesTests
    {
        [Test]
        public void FlagCueMapsEachFlaggedState()
        {
            Assert.AreEqual("race_control_green", RaceAudioCues.FlagCue(FlagState.Green));
            Assert.AreEqual("race_control_yellow", RaceAudioCues.FlagCue(FlagState.Yellow));
            Assert.AreEqual("race_control_yellow", RaceAudioCues.FlagCue(FlagState.DoubleYellow));
            Assert.AreEqual("race_control_vsc", RaceAudioCues.FlagCue(FlagState.VirtualSafetyCar));
            Assert.AreEqual("race_control_safety_car", RaceAudioCues.FlagCue(FlagState.SafetyCar));
            Assert.AreEqual("race_control_red", RaceAudioCues.FlagCue(FlagState.Red));
        }

        [Test]
        public void FlagCueIsNullForStatesWithoutACue()
        {
            Assert.IsNull(RaceAudioCues.FlagCue(FlagState.Blue));
            Assert.IsNull(RaceAudioCues.FlagCue(FlagState.Chequered));
        }

        [Test]
        public void WeatherCueOnlyAlertsForRain()
        {
            Assert.AreEqual(RaceAudioCues.RainAlert, RaceAudioCues.WeatherCue(WeatherKind.LightRain));
            Assert.AreEqual(RaceAudioCues.RainAlert, RaceAudioCues.WeatherCue(WeatherKind.HeavyRain));
            Assert.IsNull(RaceAudioCues.WeatherCue(WeatherKind.Clear));
            Assert.IsNull(RaceAudioCues.WeatherCue(WeatherKind.Overcast));
            Assert.IsNull(RaceAudioCues.WeatherCue(WeatherKind.Drying));
        }

        [Test]
        public void RadioInterruptsWhenNothingPendingOrChannelIdle()
        {
            // Nothing pending -> always plays.
            Assert.IsTrue(RaceAudioCues.ShouldInterruptRadio(false, 0, 5, true));
            // Pending but the channel is idle -> plays.
            Assert.IsTrue(RaceAudioCues.ShouldInterruptRadio(true, 2, 5, false));
        }

        [Test]
        public void RadioInterruptsOnlyForHigherCriticalityWhilePlaying()
        {
            // More critical (lower number) than the pending one, still playing -> interrupts.
            Assert.IsTrue(RaceAudioCues.ShouldInterruptRadio(true, 5, 2, true));
            // Less critical (higher number) while playing -> held.
            Assert.IsFalse(RaceAudioCues.ShouldInterruptRadio(true, 2, 5, true));
            // Equal priority while playing -> held (does not interrupt itself).
            Assert.IsFalse(RaceAudioCues.ShouldInterruptRadio(true, 3, 3, true));
        }
    }
}
