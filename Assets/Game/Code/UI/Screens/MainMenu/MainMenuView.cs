using F1Game.UI.Navigation;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.MainMenu
{
    /// <summary>
    /// Main menu view: hero title, career context line, and the mode actions.
    /// Pure presentation — all behavior comes from MainMenuPresenter.
    /// </summary>
    public class MainMenuView : ScreenView
    {
        public const string Id = "main-menu";

        [SerializeField] TMP_Text heroTitle;
        [SerializeField] TMP_Text careerContext;
        [SerializeField] TMP_Text versionLabel;
        [SerializeField] ThemedButton continueCareerButton;
        [SerializeField] ThemedButton quickRaceButton;
        [SerializeField] ThemedButton timeTrialButton;
        [SerializeField] ThemedButton standingsButton;
        [SerializeField] ThemedButton settingsButton;

        public TMP_Text HeroTitle => heroTitle;
        public TMP_Text CareerContext => careerContext;
        public TMP_Text VersionLabel => versionLabel;
        public ThemedButton ContinueCareerButton => continueCareerButton;
        public ThemedButton QuickRaceButton => quickRaceButton;
        public ThemedButton TimeTrialButton => timeTrialButton;
        public ThemedButton StandingsButton => standingsButton;
        public ThemedButton SettingsButton => settingsButton;

        public void Bind(
            TMP_Text hero, TMP_Text context, TMP_Text version,
            ThemedButton continueCareer, ThemedButton quickRace, ThemedButton timeTrial, ThemedButton standings, ThemedButton settings)
        {
            heroTitle = hero;
            careerContext = context;
            versionLabel = version;
            continueCareerButton = continueCareer;
            quickRaceButton = quickRace;
            timeTrialButton = timeTrial;
            standingsButton = standings;
            settingsButton = settings;
            SetScreenId(Id);
            SetDefaultSelection(continueCareer != null ? continueCareer.gameObject : quickRace.gameObject);
        }

        public void Render(MainMenuModel model)
        {
            if (careerContext != null)
            {
                careerContext.text = model.hasCareer ? model.careerSummary : "Start your career";
            }

            if (versionLabel != null)
            {
                versionLabel.text = model.versionLabel;
            }

            if (continueCareerButton != null)
            {
                continueCareerButton.SetText(model.hasCareer ? "Continue Career" : "Start Career");
            }

            // Standings only mean something once a career exists.
            if (standingsButton != null && standingsButton.gameObject.activeSelf != model.hasCareer)
            {
                standingsButton.gameObject.SetActive(model.hasCareer);
            }

            // Explicit navigation targets are NOT auto-skipped when inactive:
            // with the Standings button hidden, Down from Time Trial used to
            // land on the disabled object and controller focus stalled.
            // Re-point the neighbours around the gap whenever it toggles.
            if (standingsButton != null && timeTrialButton != null && settingsButton != null)
            {
                UnityEngine.UI.Navigation timeTrialNav = timeTrialButton.navigation;
                timeTrialNav.selectOnDown = model.hasCareer ? standingsButton : (UnityEngine.UI.Selectable)settingsButton;
                timeTrialButton.navigation = timeTrialNav;

                UnityEngine.UI.Navigation settingsNav = settingsButton.navigation;
                settingsNav.selectOnUp = model.hasCareer ? standingsButton : (UnityEngine.UI.Selectable)timeTrialButton;
                settingsButton.navigation = settingsNav;
            }
        }
    }
}
