using System;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.RaceHudShell
{
    /// <summary>
    /// Production pause overlay: a dimming backdrop and a menu card with the
    /// session's pause actions. Lives on the shell's overlay layer above the
    /// HUD; the bridge configures the session line and wires the action hooks
    /// per pause (they depend on the live RaceManager/GameBootstrap). Pure
    /// presentation - no race-layer references.
    /// </summary>
    public sealed class PauseOverlay : MonoBehaviour
    {
        [SerializeField] TMP_Text sessionLine;
        [SerializeField] ThemedButton resumeButton;
        [SerializeField] ThemedButton endPracticeButton;
        [SerializeField] ThemedButton mainMenuButton;
        [SerializeField] ThemedButton restartButton;
        [SerializeField] ThemedButton quitButton;

        public Action OnResume;
        public Action OnEndPractice;
        public Action OnMainMenu;
        public Action OnRestart;
        public Action OnQuit;

        const string RestartLabel = "Restart Session";
        const string RestartConfirmLabel = "Confirm Restart?";
        bool restartArmed;

        public void Bind(TMP_Text session, ThemedButton resume, ThemedButton endPractice,
            ThemedButton mainMenu, ThemedButton restart, ThemedButton quit)
        {
            sessionLine = session;
            resumeButton = resume;
            endPracticeButton = endPractice;
            mainMenuButton = mainMenu;
            restartButton = restart;
            quitButton = quit;

            // Any non-restart action also disarms a pending restart confirmation.
            resumeButton.Clicked += () => { DisarmRestart(); OnResume?.Invoke(); };
            endPracticeButton.Clicked += () => { DisarmRestart(); OnEndPractice?.Invoke(); };
            mainMenuButton.Clicked += () => { DisarmRestart(); OnMainMenu?.Invoke(); };
            quitButton.Clicked += () => { DisarmRestart(); OnQuit?.Invoke(); };

            // Restart is destructive (throws away the session), so it confirms
            // in place: first click arms, second click restarts.
            restartButton.Clicked += OnRestartClicked;

            gameObject.SetActive(false);
        }

        void OnRestartClicked()
        {
            if (!restartArmed)
            {
                restartArmed = true;
                restartButton.SetText(RestartConfirmLabel);
                return;
            }

            DisarmRestart();
            OnRestart?.Invoke();
        }

        void DisarmRestart()
        {
            if (!restartArmed)
            {
                return;
            }

            restartArmed = false;
            if (restartButton != null)
            {
                restartButton.SetText(RestartLabel);
            }
        }

        /// <summary>Sets the session/event line and whether the End-Practice action applies.</summary>
        public void Configure(string sessionLabel, bool isPractice)
        {
            if (sessionLine != null)
            {
                sessionLine.text = sessionLabel ?? "";
            }

            if (endPracticeButton != null && endPracticeButton.gameObject.activeSelf != isPractice)
            {
                endPracticeButton.gameObject.SetActive(isPractice);
            }
        }

        public void SetVisible(bool visible)
        {
            // A fresh pause (or a hide) always starts from a disarmed restart.
            DisarmRestart();

            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }

            if (visible)
            {
                transform.SetAsLastSibling();
            }
        }
    }
}
