using F1Game.Input;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// The single authoritative owner of high-level UI ownership state. Before
    /// this, RuntimeUi, ProductionUiBridge, UiShell and RaceManager each decided
    /// independently which UI should be visible, which allowed the strategy
    /// screen to survive into a live race and duplicate/racey transitions.
    ///
    /// This coordinator owns: the flow state, the single-flight transition guard,
    /// and which input action map is active. It does not build UI itself — it
    /// gates the code that does.
    /// </summary>
    public static class UiSessionCoordinator
    {
        public enum UiFlowState
        {
            Boot,
            Frontend,
            StartingSession,
            LiveSession,
            Paused,
            Results,
            ReturningToFrontend,
        }

        public static UiFlowState State { get; private set; } = UiFlowState.Boot;

        static bool transitionInProgress;

        /// <summary>True while a session is being started or is live/paused.</summary>
        public static bool IsStartingOrLive =>
            State == UiFlowState.StartingSession ||
            State == UiFlowState.LiveSession ||
            State == UiFlowState.Paused;

        public static bool TransitionInProgress => transitionInProgress;

        public static void EnterFrontend()
        {
            State = UiFlowState.Frontend;
            transitionInProgress = false;
            SwitchToFrontendInput();
        }

        /// <summary>
        /// Single-flight guard for starting a session. Returns false if a start is
        /// already in progress or a session is already live, so a duplicate Start
        /// press / repeated submit / controller-East cannot re-enter the flow.
        /// </summary>
        public static bool TryBeginSessionStart()
        {
            if (transitionInProgress || IsStartingOrLive)
            {
                return false;
            }

            transitionInProgress = true;
            State = UiFlowState.StartingSession;
            return true;
        }

        public static void EnterLiveSession()
        {
            State = UiFlowState.LiveSession;
            transitionInProgress = false;
            SwitchToDrivingInput();
        }

        public static void EnterPaused() => State = UiFlowState.Paused;

        public static void ResumeLive() => State = UiFlowState.LiveSession;

        public static void EnterResults()
        {
            State = UiFlowState.Results;
            transitionInProgress = false;
            SwitchToFrontendInput();
        }

        /// <summary>Start failed: restore a single coherent frontend state.</summary>
        public static void FailToFrontend()
        {
            State = UiFlowState.Frontend;
            transitionInProgress = false;
            SwitchToFrontendInput();
        }

        static void SwitchToFrontendInput()
        {
            if (DriveInputConfig.UseInputSystem)
            {
                try { InputService.Instance.EnterFrontend(); } catch { /* input system optional */ }
            }
        }

        static void SwitchToDrivingInput()
        {
            if (DriveInputConfig.UseInputSystem)
            {
                try { InputService.Instance.EnterDriving(); } catch { /* input system optional */ }
            }
        }
    }
}
