using F1Game.Core.Events;

namespace F1Game.Core
{
    /// <summary>
    /// Engine-free race-audio cue mapping: the pure decisions the RaceAudioDirector
    /// uses to turn a race event into an audio-bank key, plus the radio-interrupt
    /// arbitration. Extracted VERBATIM from the director so the flag/weather -> key
    /// mapping and the "does this radio message interrupt the current one" rule are
    /// unit-testable without an AudioSource or bank. The keys are identical to the
    /// previous inline strings (no behaviour change); the director owns the actual
    /// clip resolution and playback.
    /// </summary>
    public static class RaceAudioCues
    {
        public const string Penalty = "race_control_penalty";
        public const string PitCall = "pit_call";
        public const string RainAlert = "weather_rain_alert";
        public const string RadioBeep = "radio_beep";

        /// <summary>The bank key for a flag state, or null when the flag has no cue.</summary>
        public static string FlagCue(FlagState flag)
        {
            switch (flag)
            {
                case FlagState.SafetyCar: return "race_control_safety_car";
                case FlagState.VirtualSafetyCar: return "race_control_vsc";
                case FlagState.Yellow:
                case FlagState.DoubleYellow: return "race_control_yellow";
                case FlagState.Red: return "race_control_red";
                case FlagState.Green: return "race_control_green";
                default: return null;
            }
        }

        /// <summary>The bank key for a weather change, or null when it needs no alert.</summary>
        public static string WeatherCue(WeatherKind weather)
        {
            return weather == WeatherKind.LightRain || weather == WeatherKind.HeavyRain ? RainAlert : null;
        }

        /// <summary>
        /// Whether an incoming radio message should take the radio channel now: yes
        /// when nothing is pending, when it is more critical (a lower priority number)
        /// than the pending one, or when the channel is idle.
        /// </summary>
        public static bool ShouldInterruptRadio(bool hasPending, int pendingPriority, int newPriority, bool radioPlaying)
        {
            return !hasPending || newPriority < pendingPriority || !radioPlaying;
        }
    }
}
