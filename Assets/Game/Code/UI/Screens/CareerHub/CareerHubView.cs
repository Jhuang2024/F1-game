using F1Game.UI.Navigation;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.CareerHub
{
    /// <summary>
    /// Career hub: season context, the next event card, and the career
    /// actions. Systems not yet migrated stay reachable through the full
    /// legacy career menu action. Pure presentation.
    /// </summary>
    public class CareerHubView : ScreenView
    {
        public const string Id = "career-hub";

        [SerializeField] TMP_Text seasonLabel;
        [SerializeField] TMP_Text standingLine;
        [SerializeField] TMP_Text nextEventTitle;
        [SerializeField] TMP_Text nextEventDetail;
        [SerializeField] ThemedButton continueButton;
        [SerializeField] ThemedButton standingsButton;
        [SerializeField] ThemedButton profileButton;
        [SerializeField] ThemedButton legacyMenuButton;
        [SerializeField] ThemedButton backButton;

        public ThemedButton ContinueButton => continueButton;
        public ThemedButton StandingsButton => standingsButton;
        public ThemedButton ProfileButton => profileButton;
        public ThemedButton LegacyMenuButton => legacyMenuButton;
        public ThemedButton BackButton => backButton;

        public void Bind(TMP_Text season, TMP_Text standing, TMP_Text eventTitle, TMP_Text eventDetail,
            ThemedButton continueBtn, ThemedButton standings, ThemedButton profile, ThemedButton legacyMenu, ThemedButton back)
        {
            seasonLabel = season;
            standingLine = standing;
            nextEventTitle = eventTitle;
            nextEventDetail = eventDetail;
            continueButton = continueBtn;
            standingsButton = standings;
            profileButton = profile;
            legacyMenuButton = legacyMenu;
            backButton = back;
            SetScreenId(Id);
            SetDefaultSelection(continueBtn != null ? continueBtn.gameObject : back.gameObject);
        }

        public void Render(CareerHubModel model)
        {
            if (seasonLabel != null)
            {
                seasonLabel.text = model.seasonLabel ?? "";
            }

            if (standingLine != null)
            {
                standingLine.text = model.standingLine ?? "";
            }

            if (nextEventTitle != null)
            {
                nextEventTitle.text = model.nextEventName ?? "";
            }

            if (nextEventDetail != null)
            {
                nextEventDetail.text = model.nextEventDetail ?? "";
            }

            if (continueButton != null)
            {
                continueButton.SetText(model.continueLabel ?? "Continue");
            }
        }
    }
}
