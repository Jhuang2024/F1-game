using System;

namespace F1Game.Core
{
    /// <summary>
    /// UI → race command channel, the mirror image of <see cref="HudTelemetry"/>
    /// (which flows race → UI). The production HUD lives in the F1Game.UI
    /// assembly and cannot reference the race layer, so an interactive HUD
    /// control invokes a command here and the race layer (RaceEventRelay)
    /// registers the handler. Handlers are cleared on session teardown so a
    /// destroyed race can never be driven by a stale click.
    /// </summary>
    public static class HudCommands
    {
        /// <summary>Registered by the race layer to <c>RaceManager.CancelManualPitRequest</c>.</summary>
        public static Action CancelPitRequest;

        /// <summary>Invoked by the cancel-pit HUD button; a no-op when nothing is registered.</summary>
        public static void RequestCancelPit()
        {
            Action handler = CancelPitRequest;
            handler?.Invoke();
        }

        /// <summary>Drop all handlers (session teardown).</summary>
        public static void Clear()
        {
            CancelPitRequest = null;
        }
    }
}
