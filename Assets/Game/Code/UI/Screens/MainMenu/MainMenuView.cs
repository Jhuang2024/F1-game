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
        [SerializeField] ThemedButton settingsButton;

        public TMP_Text HeroTitle => heroTitle;
        public TMP_Text CareerContext => careerContext;
        public TMP_Text VersionLabel => versionLabel;
        public ThemedButton ContinueCareerButton => continueCareerButton;
        public ThemedButton QuickRaceButton => quickRaceButton;
        public ThemedButton TimeTrialButton => timeTrialButton;
        public ThemedButton SettingsButton => settingsButton;

        public void Bind(
            TMP_Text hero, TMP_Text context, TMP_Text version,
            ThemedButton continueCareer, ThemedButton quickRace, ThemedButton timeTrial, ThemedButton settings)
        {
            heroTitle = hero;
            careerContext = context;
            versionLabel = version;
            continueCareerButton = continueCareer;
            quickRaceButton = quickRace;
            timeTrialButton = timeTrial;
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
        }
    }
}
