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

        public void Bind(TMP_Text session, ThemedButton resume, ThemedButton endPractice,
            ThemedButton mainMenu, ThemedButton restart, ThemedButton quit)
        {
            sessionLine = session;
            resumeButton = resume;
            endPracticeButton = endPractice;
            mainMenuButton = mainMenu;
            restartButton = restart;
            quitButton = quit;

            resumeButton.Clicked += () => OnResume?.Invoke();
            endPracticeButton.Clicked += () => OnEndPractice?.Invoke();
            mainMenuButton.Clicked += () => OnMainMenu?.Invoke();
            restartButton.Clicked += () => OnRestart?.Invoke();
            quitButton.Clicked += () => OnQuit?.Invoke();

            gameObject.SetActive(false);
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
