using F1Game.Core.Diagnostics;
using F1Game.UI;
using F1Game.UI.Screens.RaceHudShell;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Bridges the race layer to the production HUD so
    /// <c>RaceManager.StartSession</c> no longer unconditionally builds the legacy
    /// <c>RaceHud</c>. Exactly one HUD is shown: the production <c>HudRoot</c> when
    /// the production UI is active and ready, otherwise the legacy HUD. Never both.
    /// </summary>
    public static class ProductionSessionUi
    {
        /// <summary>
        /// Shows the production HUD if the production UI owns the frontend.
        /// Returns true when it did (caller must NOT build the legacy HUD).
        /// </summary>
        public static bool TryShowRaceHud()
        {
            if (!ProductionUiReadiness.Enabled)
            {
                return false;
            }

            UiShell shell = ProductionUiBridge.Shell;
            if (shell == null)
            {
                return false;
            }

            try
            {
                shell.SetShellVisible(true);
                shell.Modals.CloseAll();
                shell.Router.ResetStack();
                shell.Router.Show(HudRoot.Id);
                UiSessionCoordinator.EnterLiveSession();
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogError(DiagnosticLog.FormatError(DiagnosticCode.HudBindFailed, "Production HUD show failed; falling back to legacy HUD: " + exception));
                return false;
            }
        }

        /// <summary>Hides the production HUD/shell (results, pause-to-menu, cleanup).</summary>
        public static void HideHud()
        {
            UiShell shell = ProductionUiBridge.Shell;
            if (shell != null)
            {
                shell.SetShellVisible(false);
            }
        }

        /// <summary>
        /// Session ended → results. Hides the production HUD and unlocks frontend
        /// navigation so the (currently legacy) results screen shows cleanly, and
        /// the strategy screen can never be resurrected. No-op when production UI
        /// is not the active frontend.
        /// </summary>
        public static void BeginResults()
        {
            if (!ProductionUiReadiness.Enabled)
            {
                return;
            }

            HideHud();
            UiShell.NavigationLocked = false;
            UiSessionCoordinator.EnterResults();
        }

        /// <summary>
        /// Show the production post-race classification when the production UI is
        /// the active frontend. Returns true when it did (caller must NOT run the
        /// legacy results path); false leaves the legacy results screen as the
        /// compatibility fallback. Never throws into the race loop.
        /// </summary>
        public static bool TryShowResults(System.Collections.Generic.List<RaceResultEntry> results, bool careerRace, string debriefLine = null)
        {
            if (!ProductionUiReadiness.Enabled)
            {
                return false;
            }

            try
            {
                return ProductionUiBridge.TryShowResults(results, careerRace, debriefLine);
            }
            catch (System.Exception exception)
            {
                Debug.LogError(DiagnosticLog.FormatError(DiagnosticCode.HudBindFailed, "Production results show failed; legacy fallback: " + exception));
                return false;
            }
        }

        /// <summary>
        /// Show the production time-trial result (personal-best / track-record
        /// screen) when the production UI is the active frontend and the player set
        /// a clean lap this session. Returns true when it did (caller must NOT run
        /// the legacy straight-to-menu exit); false leaves the legacy path as the
        /// compatibility fallback. Never throws into the exit handler. The result
        /// screen's own buttons route back through GameBootstrap (which cleans up the
        /// race world), so the caller skips its own teardown only when this is true.
        /// </summary>
        public static bool TryShowTimeTrialResult(RaceManager race)
        {
            if (!ProductionUiReadiness.Enabled)
            {
                return false;
            }

            try
            {
                return ProductionUiBridge.TryShowTimeTrialResult(race);
            }
            catch (System.Exception exception)
            {
                Debug.LogError(DiagnosticLog.FormatError(DiagnosticCode.HudBindFailed, "Production time-trial result show failed; legacy fallback: " + exception));
                return false;
            }
        }

        /// <summary>Production qualifying results (career only), same fallback contract.</summary>
        public static bool TryShowQualifyingResults(System.Collections.Generic.List<QualifyingResultEntry> results, bool careerRace)
        {
            if (!ProductionUiReadiness.Enabled)
            {
                return false;
            }

            try
            {
                return ProductionUiBridge.TryShowQualifyingResults(results, careerRace);
            }
            catch (System.Exception exception)
            {
                Debug.LogError(DiagnosticLog.FormatError(DiagnosticCode.HudBindFailed, "Production qualifying results show failed; legacy fallback: " + exception));
                return false;
            }
        }

        /// <summary>Pause: reveal the legacy pause panel by hiding the production HUD overlay.</summary>
        public static void SetPaused(RaceManager race, bool paused)
        {
            if (!ProductionUiReadiness.Enabled)
            {
                return;
            }

            if (paused)
            {
                // The production HUD stays up; the pause overlay renders above it
                // (the legacy pause panel does not exist while the production HUD
                // is active). Shell stays visible.
                ProductionUiBridge.ShowPauseMenu(race);
                UiSessionCoordinator.EnterPaused();
            }
            else
            {
                ProductionUiBridge.HidePauseMenu();
                UiSessionCoordinator.ResumeLive();
            }
        }

        /// <summary>Full teardown (return to menu / cleanup): hide HUD, unlock nav, frontend state.</summary>
        public static void EndSession()
        {
            HideHud();
            UiShell.NavigationLocked = false;
            UiSessionCoordinator.EnterFrontend();
        }
    }
}
