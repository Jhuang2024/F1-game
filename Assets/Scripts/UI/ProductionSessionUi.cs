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

        /// <summary>Pause: reveal the legacy pause panel by hiding the production HUD overlay.</summary>
        public static void SetPaused(bool paused)
        {
            if (!ProductionUiReadiness.Enabled)
            {
                return;
            }

            UiShell shell = ProductionUiBridge.Shell;
            if (shell != null)
            {
                // Hide the HUD overlay while paused so the legacy pause menu (lower
                // canvas) is visible and interactable; restore it on resume.
                shell.SetShellVisible(!paused);
            }

            if (paused)
            {
                UiSessionCoordinator.EnterPaused();
            }
            else
            {
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
