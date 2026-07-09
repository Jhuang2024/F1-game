using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    public class RuntimeUi : MonoBehaviour
    {
        GameBootstrap bootstrap;
        Canvas canvas;
        RaceHud hud;
        GameObject pausePanel;
        string selectedTeamId = "williams";
        string selectedDriverId = "";
        bool useExistingDriver;

        // ShowCareerSetup used to update team selection without a screen
        // rebuild (so the typed driver name in the InputField wasn't lost),
        // which meant clicking a team gave no visible "this one is now active"
        // feedback beyond a text label. Adding SetButtonSelected highlighting
        // requires a rebuild to redraw every button's state, so the typed name
        // is now shadowed here across rebuilds instead of being lost.
        string careerSetupDraftName;
        bool careerSetupDraftNameSet;

        // Quick Race track picked on ShowQuickRaceTrackSelect, carried through to
        // GameBootstrap.BeginQuickRace instead of it silently defaulting to
        // data.Calendar.events[0]. Career mode never reads or writes this.
        CalendarEventData quickRaceSelectedEvent;
        public CalendarEventData QuickRaceSelectedEvent { get { return quickRaceSelectedEvent; } }

        // Championship graph screen (ShowChampionshipGraphs) state - which tab
        // (driver/constructor/combined) and whether the full field is expanded,
        // plus the currently tapped point/legend entry - persisted across this
        // screen's own rebuilds (every tap/tab click re-invokes
        // ShowChampionshipGraphs) so the screen doesn't snap back to defaults
        // on every interaction.
        int championshipGraphTab; // 0 = WDC, 1 = WCC, 2 = Combined overview
        bool championshipGraphShowFullField;
        string championshipGraphSelectedSeriesId = "";
        int championshipGraphSelectedRound = -1;

        public void Initialize(GameBootstrap owner)
        {
            bootstrap = owner;
            canvas = UiFactory.CreateCanvas("Runtime UI");
            canvas.transform.SetParent(transform, false);
        }

        public void Clear()
        {
            if (hud != null)
            {
                Destroy(hud.gameObject);
                hud = null;
            }

            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(canvas.transform.GetChild(i).gameObject);
            }
        }

        public void ShowMainMenu(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            // Reapply the player's saved UI Scale here (main menu is always
            // the first screen shown, and the return point from everywhere
            // else) so it takes effect as soon as settings are loaded, not
            // only after the player revisits Display Settings.
            UiFactory.ApplyUiScale(canvas, settings.UiScale);
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Main background", new Color(0.004f, 0.007f, 0.011f, 1f));
            UiFactory.CreateBand(background, "Top accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -10f), Vector2.zero, new Color(0.95f, 0.03f, 0.025f, 1f));
            UiFactory.CreateBand(background, "Header wash", new Vector2(0f, 0.74f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.72f));
            UiFactory.CreateBand(background, "Garage floor", new Vector2(0f, 0f), new Vector2(1f, 0.24f), Vector2.zero, Vector2.zero, new Color(0.024f, 0.028f, 0.032f, 1f));
            UiFactory.CreateBand(background, "Track line left", new Vector2(0f, 0.275f), new Vector2(1f, 0.286f), new Vector2(0f, 0f), Vector2.zero, new Color(0.92f, 0.94f, 0.9f, 0.9f));
            RectTransform redLine = UiFactory.CreateBand(background, "Track line red", new Vector2(0f, 0.235f), new Vector2(1f, 0.245f), new Vector2(0f, 0f), Vector2.zero, new Color(0.85f, 0.04f, 0.035f, 0.82f));
            if (UiFactory.AnimationsEnabled)
            {
                UiPulse pulse = redLine.gameObject.AddComponent<UiPulse>();
                pulse.speed = 0.7f;
                pulse.minAlpha = 0.45f;
                pulse.maxAlpha = 0.85f;
            }
            UiFactory.CreateBand(background, "Right paddock shadow", new Vector2(0.52f, 0.22f), new Vector2(1f, 0.98f), Vector2.zero, Vector2.zero, new Color(0.014f, 0.019f, 0.025f, 0.82f));
            UiFactory.CreateBand(background, "Bottom vignette", new Vector2(0f, 0f), new Vector2(1f, 0.08f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.58f));
            BuildMenuCarSilhouette(background);

            RectTransform titleArea = UiFactory.CreateRect(background, "Title area", new Vector2(0.055f, 0.62f), new Vector2(0.64f, 0.92f), Vector2.zero, Vector2.zero);
            Text title = UiFactory.CreateText(titleArea, "Title", "LOCAL FORMULA", 74, Color.white, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().sizeDelta = new Vector2(920f, 94f);
            RectTransform titleUnderline = UiFactory.CreateRect(titleArea, "Title underline", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            titleUnderline.sizeDelta = new Vector2(238f, 5f);
            titleUnderline.pivot = new Vector2(0f, 1f);
            titleUnderline.anchoredPosition = new Vector2(4f, -84f);
            Image underlineImage = titleUnderline.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(underlineImage, UiFactory.Accent);
            Text subtitle = UiFactory.CreateText(titleArea, "Subtitle", "CAREER RACING", 30, new Color(0.95f, 0.05f, 0.04f), TextAnchor.UpperLeft);
            subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(2f, -100f);
            Text seasonTag = UiFactory.CreateText(titleArea, "Season tag", data.Calendar.events.Count + " ROUND WORLD SEASON", 20, new Color(0.74f, 0.84f, 0.88f), TextAnchor.UpperLeft);
            seasonTag.GetComponent<RectTransform>().anchoredPosition = new Vector2(4f, -142f);

            // Trimmed to the five things a player actually starts from here.
            // Track Info, Driver Ratings, Career Stats and Race Weekend all live
            // inside the Career hub now instead of cluttering the front door.
            // Each entry is a small hover-reactive card (title + one-line
            // description) rather than a plain stacked button, so the front door
            // reads as a set of designed choices.
            RectTransform menu = UiFactory.CreateRect(background, "Menu", new Vector2(0.06f, 0.16f), new Vector2(0.32f, 0.6f), Vector2.zero, Vector2.zero);
            UiFactory.AddVerticalLayout(menu, 10, new RectOffset(0, 0, 0, 0));
            string careerCardInfo = "Season " + career.Save.currentSeason + "  ·  Round " + career.Save.currentRound;
            // Part 21: routes through GameBootstrap.ShowCareer() rather than
            // straight to ShowCareerHub, same as the post-race report's
            // "Continue Career" button - that's the one choke point that
            // knows to redirect into the season-review flow instead when a
            // season transition is still pending (e.g. the player quit right
            // after Abu Dhabi and is only now reopening the app).
            UiFactory.CreateModeCard(menu, "Career", careerCardInfo, UiFactory.Accent, true, bootstrap.ShowCareer);
            UiFactory.CreateModeCard(menu, "Quick Race", "Jump straight into a race weekend", UiFactory.AccentCyan, false, bootstrap.StartQuickRace);
            UiFactory.CreateModeCard(menu, "Time Trial", "Chase your best lap, no rivals", UiFactory.AccentPurple, false, bootstrap.ShowTimeTrialSetup);
            UiFactory.CreateModeCard(menu, "Settings", "Difficulty, assists, display, controls", UiFactory.AccentAmber, false, () => ShowSettings(data, career, settings));
            UiFactory.CreateModeCard(menu, "Quit", "Exit to desktop", UiFactory.TextMuted, false, Application.Quit);

            // Bottom status strip: live save dot, career round, difficulty, build label.
            RectTransform statusStrip = UiFactory.CreateBand(background, "Status strip", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 34f), new Color(0.004f, 0.006f, 0.009f, 0.92f));
            Image saveDot = UiFactory.CreatePulsingDot(statusStrip, "Save state", 12f, UiFactory.AccentGreen);
            RectTransform saveDotRect = saveDot.rectTransform;
            saveDotRect.anchorMin = new Vector2(0f, 0.5f);
            saveDotRect.anchorMax = new Vector2(0f, 0.5f);
            saveDotRect.anchoredPosition = new Vector2(24f, 0f);
            Text status = UiFactory.CreateText(statusStrip, "Status text",
                "CAREER SAVE LOADED  ·  SEASON " + career.Save.currentSeason + " ROUND " + career.Save.currentRound +
                "  ·  DIFFICULTY " + settings.Difficulty.ToString().ToUpperInvariant() +
                "  ·  LOCAL FORMULA", 14, UiFactory.TextMuted, TextAnchor.MiddleCenter);
            RectTransform statusRect = status.GetComponent<RectTransform>();
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;

            // Career summary panel: designed card with header row, key stats as
            // proper stat tiles, and the next event called out.
            RectTransform summary = UiFactory.CreateGlassPanel(background, "Career summary", new Vector2(0.58f, 0.14f), new Vector2(0.92f, 0.7f), Vector2.zero, Vector2.zero, new Color(0.02f, 0.03f, 0.042f, 0.92f));
            RectTransform summaryAccent = UiFactory.CreateRect(summary, "Summary accent", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            summaryAccent.sizeDelta = new Vector2(84f, 4f);
            summaryAccent.pivot = new Vector2(0f, 1f);
            summaryAccent.anchoredPosition = new Vector2(28f, -16f);
            Image summaryAccentImage = summaryAccent.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(summaryAccentImage, UiFactory.Accent);

            Text heading = UiFactory.CreateText(summary, "Summary title", "NEXT WEEKEND", 17, UiFactory.TextMuted, TextAnchor.UpperLeft);
            heading.GetComponent<RectTransform>().anchoredPosition = new Vector2(28f, -30f);
            TeamData team = data.FindTeam(career.Save.playerTeamId);
            CalendarEventData current = career.CurrentEvent();
            PlayerRecordsData records = PlayerRecordsStore.Data;
            Text eventTitle = UiFactory.CreateText(summary, "Summary event", (current == null ? "Prototype GP" : current.displayName).ToUpperInvariant(), 30, Color.white, TextAnchor.UpperLeft);
            RectTransform eventTitleRect = eventTitle.GetComponent<RectTransform>();
            eventTitleRect.sizeDelta = new Vector2(560f, 44f);
            eventTitleRect.anchoredPosition = new Vector2(28f, -58f);

            Text driverLine = UiFactory.CreateText(summary, "Summary driver",
                career.Save.playerDriverName + "   ·   " + (team == null ? career.Save.playerTeamId : team.name) +
                "\nSeason " + career.Save.currentSeason + "  ·  Round " + career.Save.currentRound,
                19, new Color(0.82f, 0.9f, 0.94f), TextAnchor.UpperLeft);
            RectTransform driverLineRect = driverLine.GetComponent<RectTransform>();
            driverLineRect.sizeDelta = new Vector2(560f, 60f);
            driverLineRect.anchoredPosition = new Vector2(28f, -112f);
            driverLine.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform summaryStats = UiFactory.CreateRect(summary, "Summary stats", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 22f), new Vector2(-24f, 104f));
            UiFactory.AddHorizontalLayout(summaryStats, 10, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(summaryStats, "Wins", records.raceWins.ToString(), 118f);
            UiFactory.CreateStatCard(summaryStats, "Podiums", records.podiums.ToString(), 118f);
            UiFactory.CreateStatCard(summaryStats, "Poles", records.polePositions.ToString(), 118f);
            UiFactory.CreateStatCard(summaryStats, "Resources", career.Save.resourcePoints + " RP", 148f);
        }

        void BuildMenuCarSilhouette(RectTransform background)
        {
            RectTransform car = UiFactory.CreateRect(background, "Menu car silhouette", new Vector2(0.46f, 0.31f), new Vector2(0.97f, 0.58f), Vector2.zero, Vector2.zero);
            UiFactory.CreateBand(car, "Rear shadow", new Vector2(0.02f, 0.13f), new Vector2(0.98f, 0.29f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.78f));
            UiFactory.CreateBand(car, "Floor", new Vector2(0.08f, 0.38f), new Vector2(0.88f, 0.49f), Vector2.zero, Vector2.zero, new Color(0.22f, 0.24f, 0.25f, 0.96f));
            UiFactory.CreateBand(car, "Sidepod left", new Vector2(0.3f, 0.46f), new Vector2(0.58f, 0.6f), Vector2.zero, Vector2.zero, new Color(0.9f, 0.045f, 0.035f, 0.98f));
            UiFactory.CreateBand(car, "Survival cell", new Vector2(0.42f, 0.56f), new Vector2(0.68f, 0.72f), Vector2.zero, Vector2.zero, new Color(0.96f, 0.065f, 0.045f, 0.98f));
            UiFactory.CreateBand(car, "Nose", new Vector2(0.64f, 0.52f), new Vector2(0.96f, 0.59f), Vector2.zero, Vector2.zero, new Color(0.96f, 0.065f, 0.045f, 0.98f));
            UiFactory.CreateBand(car, "Rear wing", new Vector2(0.04f, 0.56f), new Vector2(0.2f, 0.74f), Vector2.zero, Vector2.zero, new Color(0.88f, 0.92f, 0.9f, 0.94f));
            UiFactory.CreateBand(car, "Beam wing", new Vector2(0.08f, 0.47f), new Vector2(0.31f, 0.54f), Vector2.zero, Vector2.zero, new Color(0.78f, 0.82f, 0.84f, 0.86f));
            UiFactory.CreateBand(car, "Front wing", new Vector2(0.87f, 0.42f), new Vector2(1f, 0.68f), Vector2.zero, Vector2.zero, new Color(0.88f, 0.92f, 0.9f, 0.94f));
            UiFactory.CreateBand(car, "Wheel rear", new Vector2(0.15f, 0.08f), new Vector2(0.3f, 0.38f), Vector2.zero, Vector2.zero, new Color(0.002f, 0.003f, 0.005f, 1f));
            UiFactory.CreateBand(car, "Wheel front", new Vector2(0.72f, 0.08f), new Vector2(0.87f, 0.38f), Vector2.zero, Vector2.zero, new Color(0.002f, 0.003f, 0.005f, 1f));
            UiFactory.CreateBand(car, "Tyre shine rear", new Vector2(0.18f, 0.29f), new Vector2(0.27f, 0.34f), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.1f));
            UiFactory.CreateBand(car, "Tyre shine front", new Vector2(0.75f, 0.29f), new Vector2(0.84f, 0.34f), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.1f));
            UiFactory.CreateBand(car, "Cockpit", new Vector2(0.49f, 0.65f), new Vector2(0.58f, 0.8f), Vector2.zero, Vector2.zero, new Color(0.01f, 0.04f, 0.055f, 0.98f));
        }

        public void ShowCareerHub(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            // See ShowMainMenu - reapplied here too since a career can resume
            // straight into this hub without passing through the main menu.
            UiFactory.ApplyUiScale(canvas, settings.UiScale);
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Career background", new Color(0.012f, 0.016f, 0.021f, 1f));
            TeamData headerTeam = data.FindTeam(career.Save.playerTeamId);
            UiFactory.CreateScreenHeader(background, "Career",
                career.Save.playerDriverName + "  ·  " + (headerTeam == null ? career.Save.playerTeamId : headerTeam.name) +
                "  ·  Season " + career.Save.currentSeason + ", Round " + career.Save.currentRound);

            // Stat row: season, reputation, resources, contract target.
            RectTransform profile = UiFactory.CreateRect(background, "Career profile strip", new Vector2(0.05f, 0.79f), new Vector2(0.95f, 0.85f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(profile, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(profile, "Reputation", career.Save.reputation.ToString(), 190f);
            UiFactory.CreateStatCard(profile, "Resources", career.Save.resourcePoints + " RP", 190f);
            UiFactory.CreateStatCard(profile, "Contract Target", "P" + career.Save.contractTargetPosition, 190f);
            PlayerRecordsData headerRecords = PlayerRecordsStore.Data;
            UiFactory.CreateStatCard(profile, "Wins / Podiums", headerRecords.raceWins + " / " + headerRecords.podiums, 210f);

            // Part 18: a single latest-headline teaser right under the profile
            // strip - the full feed lives on the Rivalry screen so the hub
            // doesn't get crowded.
            if (settings.Current.careerNewsFeedEnabled && career.Save.newsFeed != null && career.Save.newsFeed.Count > 0)
            {
                Text newsTeaser = UiFactory.CreateText(background, "News teaser",
                    "NEWS: " + career.Save.newsFeed[career.Save.newsFeed.Count - 1], 14, UiFactory.AccentCyan, TextAnchor.MiddleLeft);
                RectTransform newsRect = newsTeaser.GetComponent<RectTransform>();
                newsRect.anchorMin = new Vector2(0.05f, 0.77f);
                newsRect.anchorMax = new Vector2(0.95f, 0.79f);
                newsRect.offsetMin = Vector2.zero;
                newsRect.offsetMax = Vector2.zero;
            }

            // Left column: the next event is the one primary action on this
            // screen (also in the footer would be a duplicate path - it lives
            // here only). A real vertical layout stacks label/name/condition so
            // a long Grand Prix name that wraps to two lines can never collide
            // with the button below it, which the old fixed-Y-offset version could.
            RectTransform left = UiFactory.CreateGlassPanel(background, "Next event card", new Vector2(0.05f, 0.36f), new Vector2(0.36f, 0.77f), Vector2.zero, Vector2.zero, UiFactory.PanelDark);
            RectTransform leftAccent = UiFactory.CreateRect(left, "Next event accent", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            leftAccent.sizeDelta = new Vector2(64f, 4f);
            leftAccent.pivot = new Vector2(0f, 1f);
            leftAccent.anchoredPosition = new Vector2(24f, -18f);
            Image leftAccentImage = leftAccent.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(leftAccentImage, UiFactory.Accent);

            CalendarEventData current = career.CurrentEvent();
            bool hasQualifying = career.HasQualifyingForCurrentRound();
            string profile2 = current == null ? "" : current.weatherProfile.ToLower();

            RectTransform infoStack = UiFactory.CreateRect(left, "Next event info stack", Vector2.zero, Vector2.one, new Vector2(24f, 84f), new Vector2(-24f, -32f));
            VerticalLayoutGroup infoLayout = infoStack.gameObject.AddComponent<VerticalLayoutGroup>();
            infoLayout.spacing = 6f;
            infoLayout.childAlignment = TextAnchor.UpperLeft;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;

            UiFactory.CreateText(infoStack, "Next event label", "NEXT EVENT", 14, UiFactory.TextMuted, TextAnchor.UpperLeft);
            Text eventName = UiFactory.CreateText(infoStack, "Next event name", (current == null ? "Prototype GP" : current.displayName).ToUpperInvariant(), 25, Color.white, TextAnchor.UpperLeft);
            eventName.verticalOverflow = VerticalWrapMode.Overflow;
            Text conditionText = UiFactory.CreateText(infoStack, "Next event condition",
                WeatherProfileText(profile2) + "  ·  " + (hasQualifying ? "Grid set" : "Qualifying required"),
                15, hasQualifying ? new Color(0.55f, 1f, 0.65f) : new Color(1f, 0.85f, 0.4f), TextAnchor.UpperLeft);
            conditionText.verticalOverflow = VerticalWrapMode.Overflow;

            // Fixed-height action band pinned to the card's bottom edge - always
            // below the info stack no matter how many lines it wrapped to.
            RectTransform primaryActionSlot = UiFactory.CreateRect(left, "Next event primary action", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 18f), new Vector2(-24f, 72f));
            Button primaryAction = UiFactory.CreatePrimaryButton(primaryActionSlot, "Race Weekend", bootstrap.ShowRaceWeekend);
            RectTransform primaryRect = primaryAction.GetComponent<RectTransform>();
            primaryRect.anchorMin = Vector2.zero;
            primaryRect.anchorMax = Vector2.one;
            primaryRect.offsetMin = Vector2.zero;
            primaryRect.offsetMax = Vector2.zero;

            // Compact 2x2 grid of secondary screens below the event card, instead
            // of four full-width buttons stacked inside it.
            // Taller than before (was 0.14-0.33) to fit a fifth entry (Team
            // Ratings) alongside Driver Ratings without shrinking the buttons -
            // still clears the footer bar (0.072 of a 1080-reference canvas).
            RectTransform secondaryCard = UiFactory.CreateGlassPanel(background, "Secondary actions card", new Vector2(0.05f, 0.09f), new Vector2(0.36f, 0.33f), Vector2.zero, Vector2.zero, UiFactory.PanelDarker);
            RectTransform secondaryGrid = UiFactory.CreateRect(secondaryCard, "Secondary actions grid", Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -16f));
            GridLayoutGroup secondaryLayout = secondaryGrid.gameObject.AddComponent<GridLayoutGroup>();
            secondaryLayout.spacing = new Vector2(10f, 10f);
            secondaryLayout.cellSize = new Vector2(255f, 46f);
            secondaryLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            secondaryLayout.constraintCount = 2;
            UiFactory.CreateSecondaryButton(secondaryGrid, "Track Info", bootstrap.ShowTrackInfo);
            UiFactory.CreateSecondaryButton(secondaryGrid, "Driver Ratings", () => ShowDriverRatings(data, career, settings));
            UiFactory.CreateSecondaryButton(secondaryGrid, "Team Ratings", () => ShowTeamRatings(data, career, settings));
            UiFactory.CreateSecondaryButton(secondaryGrid, "Career Stats", () => ShowCareerStats(data, career, settings));
            UiFactory.CreateSecondaryButton(secondaryGrid, "Driver & Team", () => ShowCareerSetup(data, career, settings));
            UiFactory.CreateSecondaryButton(secondaryGrid, "Rivalry", () => ShowRivalryHub(data, career, settings));
            UiFactory.CreateSecondaryButton(secondaryGrid, "Championship Graphs", () => ShowChampionshipGraphs(data, career, settings));
            UiFactory.CreateSecondaryButton(secondaryGrid, "Season Archive", () => ShowSeasonArchive(data, career, settings));

            // Standings as real rows (position badge, team accent dot, name,
            // points) instead of a single concatenated Text block.
            RectTransform standingsPanel = UiFactory.CreateScrollPanel(background, "Standings panel", new Vector2(0.39f, 0.14f), new Vector2(0.66f, 0.77f), 4, new RectOffset(20, 20, 18, 18));
            UiFactory.CreateSubHeader(standingsPanel, "Driver Standings");
            BuildStandingsRows(data, standingsPanel, career.Save.driverStandings, 380f);
            UiFactory.CreateDivider(standingsPanel);
            UiFactory.CreateSubHeader(standingsPanel, "Constructors");
            BuildStandingsRows(data, standingsPanel, career.Save.constructorStandings, 380f);
            // Direct entry point into the full progression-over-time view - the
            // panel above only ever shows the current snapshot, not the round-by-
            // round shape of the season.
            Button openChampionshipGraphs = UiFactory.CreateSecondaryButton(standingsPanel, "Full Championship Graphs ->", () => ShowChampionshipGraphs(data, career, settings));
            UiFactory.SetSize(openChampionshipGraphs, 380f, 44f);

            // R&D: overview card with pending report messages; the full command
            // center lives on its own screen behind the button below.
            RectTransform right = UiFactory.CreateScrollPanel(background, "Rnd overview panel", new Vector2(0.69f, 0.14f), new Vector2(0.95f, 0.77f), 10, new RectOffset(20, 20, 18, 18));
            UiFactory.CreateSubHeader(right, "R&D Division");
            TeamData careerTeam = data.FindTeam(career.Save.playerTeamId);
            CarPerformanceData baseCar = careerTeam == null ? null : data.FindCar(careerTeam.carPerformanceId);
            CarPerformanceData tunedCar = baseCar == null ? null : career.ApplyCareerUpgrades(baseCar);
            if (baseCar != null && tunedCar != null)
            {
                Text carStats = UiFactory.CreateText(right, "Car performance", BuildCarPerformanceText(baseCar, tunedCar), 15, new Color(0.72f, 0.84f, 0.9f), TextAnchor.UpperLeft);
                carStats.verticalOverflow = VerticalWrapMode.Overflow;
                UiFactory.SetSize(carStats, 390f, 104f);
            }

            BuildRndReportCard(right, career);

            string regulationRisk = career.Save.regulationAffectedCategories == null || career.Save.regulationAffectedCategories.Count == 0
                ? "None announced"
                : string.Join(", ", career.Save.regulationAffectedCategories.ToArray());
            Text rndSummary = UiFactory.CreateText(right, "Rnd summary",
                "Active projects: " + career.ActiveProjectCount() + " / " + career.MaxActiveProjects() +
                "\nCompleted upgrades: " + career.Save.completedUpgradeIds.Count +
                "\nRegulation risk: " + regulationRisk,
                15, UiFactory.TextMuted, TextAnchor.UpperLeft);
            rndSummary.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(rndSummary, 384f, 68f);

            Button openRnd = UiFactory.CreatePrimaryButton(right, "R&D Command Center", () => ShowRndCenter(data, career, settings));
            UiFactory.SetSize(openRnd, 384f, 52f);

            // Race Weekend already lives on the event card above - the footer is
            // for navigation, not a second path to the same primary action.
            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
            UiFactory.CreateSecondaryButton(footerLeft, "Settings", () => ShowSettings(data, career, settings));
        }

        // ---------- championship progression graphs (WDC / WCC) ----------
        // New screen built on top of CareerManager.GetDriverChampionshipProgression /
        // GetConstructorChampionshipProgression - both derive from the existing
        // race-results history, so this screen always reflects the season fresh
        // as of whenever it's opened (both progressions are re-fetched at the top
        // of every build, never cached across screens).

        // CareerManager.FindStandingPosition is private, so this mirrors its
        // simple linear scan rather than requiring a CareerManager signature
        // change just for this screen.
        static int FindStandingsRank(List<StandingEntry> standings, string id)
        {
            if (standings == null || string.IsNullOrEmpty(id))
            {
                return -1;
            }

            for (int i = 0; i < standings.Count; i++)
            {
                if (standings[i].id == id)
                {
                    return i + 1;
                }
            }

            return -1;
        }

        static int ChampionshipLastPoints(CareerManager.ChampionshipSeries series)
        {
            if (series == null || series.cumulativePoints == null || series.cumulativePoints.Count == 0)
            {
                return 0;
            }

            return series.cumulativePoints[series.cumulativePoints.Count - 1];
        }

        static CareerManager.ChampionshipSeries FindChampionshipPlayerSeries(CareerManager.ChampionshipProgression progression, bool team)
        {
            if (progression == null || progression.series == null)
            {
                return null;
            }

            for (int i = 0; i < progression.series.Count; i++)
            {
                CareerManager.ChampionshipSeries series = progression.series[i];
                if (team ? series.isPlayerTeam : series.isPlayer)
                {
                    return series;
                }
            }

            return null;
        }

        static CareerManager.ChampionshipSeries FindChampionshipLeaderSeries(CareerManager.ChampionshipProgression progression)
        {
            if (progression == null || progression.series == null || progression.series.Count == 0)
            {
                return null;
            }

            CareerManager.ChampionshipSeries leader = progression.series[0];
            int leaderPoints = ChampionshipLastPoints(leader);
            for (int i = 1; i < progression.series.Count; i++)
            {
                int points = ChampionshipLastPoints(progression.series[i]);
                if (points > leaderPoints)
                {
                    leaderPoints = points;
                    leader = progression.series[i];
                }
            }

            return leader;
        }

        // Biggest gainer/loser: compares each series' points over its last up to
        // 3 rounds (or first-to-last if the season has fewer than 3 rounds so
        // far) - simple momentum signal rather than a full form model.
        static CareerManager.ChampionshipSeries FindChampionshipBiggestMover(CareerManager.ChampionshipProgression progression, bool gainer, out int delta)
        {
            delta = 0;
            CareerManager.ChampionshipSeries best = null;
            bool first = true;
            if (progression != null && progression.series != null)
            {
                for (int i = 0; i < progression.series.Count; i++)
                {
                    CareerManager.ChampionshipSeries series = progression.series[i];
                    List<int> points = series.cumulativePoints;
                    if (points == null || points.Count == 0)
                    {
                        continue;
                    }

                    int windowStart = Mathf.Max(0, points.Count - 3);
                    int seriesDelta = points[points.Count - 1] - points[windowStart];
                    if (first || (gainer ? seriesDelta > delta : seriesDelta < delta))
                    {
                        delta = seriesDelta;
                        best = series;
                        first = false;
                    }
                }
            }

            return best;
        }

        static CareerManager.ChampionshipSeries FindChampionshipSeriesById(CareerManager.ChampionshipProgression progression, string id)
        {
            if (progression == null || progression.series == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < progression.series.Count; i++)
            {
                if (progression.series[i].id == id)
                {
                    return progression.series[i];
                }
            }

            return null;
        }

        string BuildChampionshipDetailText(CareerManager.ChampionshipProgression a, CareerManager.ChampionshipProgression b, string seriesId, int roundIndex)
        {
            if (string.IsNullOrEmpty(seriesId) || roundIndex < 0)
            {
                return "Tap a point on a line, or a legend entry, to see round-by-round detail here.";
            }

            CareerManager.ChampionshipSeries series = FindChampionshipSeriesById(a, seriesId);
            CareerManager.ChampionshipProgression owner = a;
            if (series == null)
            {
                series = FindChampionshipSeriesById(b, seriesId);
                owner = b;
            }

            if (series == null || owner == null || owner.roundLabels == null || roundIndex >= series.cumulativePoints.Count || roundIndex >= owner.roundLabels.Count)
            {
                return "Tap a point on a line, or a legend entry, to see round-by-round detail here.";
            }

            int points = series.cumulativePoints[roundIndex];
            int previousPoints = roundIndex > 0 ? series.cumulativePoints[roundIndex - 1] : 0;
            int gained = points - previousPoints;
            return series.label + "  ·  " + owner.roundLabels[roundIndex] + "  ·  " + points + " pts total (+" + gained + " that round)";
        }

        void OnChampionshipPointTapped(GameDataRepository data, CareerManager career, GameSettingsStore settings, string seriesId, int roundIndex)
        {
            championshipGraphSelectedSeriesId = seriesId;
            championshipGraphSelectedRound = roundIndex;
            ShowChampionshipGraphs(data, career, settings);
        }

        // x-position of a round index and y-position of a points value within a
        // plot area's local space (origin = plot area's own bottom-left corner,
        // inset by `origin` to leave room for axis labels).
        static float ChampionshipRoundX(int roundIndex, int roundCount, float plotWidth, float originX)
        {
            return roundCount <= 1 ? originX : originX + (roundIndex / (float)(roundCount - 1)) * plotWidth;
        }

        static float ChampionshipPointsY(int points, int maxPoints, float plotHeight, float originY)
        {
            return originY + Mathf.Clamp01(points / (float)Mathf.Max(1, maxPoints)) * plotHeight;
        }

        // Draws one progression (WDC or WCC) into plotArea, and its legend into
        // legendArea (expected to be a CreateScrollPanel content rect, which
        // already carries its own VerticalLayoutGroup - rows are just appended).
        // Both rects are cleared of any previous children first so repeated
        // taps/tab switches never accumulate stale geometry.
        void BuildChampionshipChart(RectTransform plotArea, RectTransform legendArea, CareerManager.ChampionshipProgression progression, string teammateSeriesId, bool showFullField, string selectedSeriesId, int selectedRoundIndex, System.Action<string, int> onPointTapped)
        {
            for (int i = plotArea.childCount - 1; i >= 0; i--)
            {
                Destroy(plotArea.GetChild(i).gameObject);
            }

            for (int i = legendArea.childCount - 1; i >= 0; i--)
            {
                Destroy(legendArea.GetChild(i).gameObject);
            }

            if (progression == null || progression.series == null || progression.series.Count == 0 || progression.rounds == null || progression.rounds.Count == 0)
            {
                BuildEmptyState(plotArea, "Not enough championship history yet - complete at least one round to see a progression line.", Mathf.Max(200f, plotArea.rect.width - 40f));
                return;
            }

            List<CareerManager.ChampionshipSeries> allSeries = progression.series;
            List<CareerManager.ChampionshipSeries> shown = new List<CareerManager.ChampionshipSeries>();
            if (showFullField)
            {
                shown.AddRange(allSeries);
            }
            else
            {
                List<CareerManager.ChampionshipSeries> sorted = new List<CareerManager.ChampionshipSeries>(allSeries);
                sorted.Sort((a, b) => ChampionshipLastPoints(b).CompareTo(ChampionshipLastPoints(a)));
                int topCount = Mathf.Min(8, sorted.Count);
                for (int i = 0; i < topCount; i++)
                {
                    shown.Add(sorted[i]);
                }

                // Always keep the player, player's team and (for the driver graph)
                // the player's teammate visible even if they fell outside the
                // top-8 by points.
                for (int i = 0; i < allSeries.Count; i++)
                {
                    CareerManager.ChampionshipSeries series = allSeries[i];
                    bool mustInclude = series.isPlayer || series.isPlayerTeam || (!string.IsNullOrEmpty(teammateSeriesId) && series.id == teammateSeriesId);
                    if (mustInclude && !shown.Contains(series))
                    {
                        shown.Add(series);
                    }
                }
            }

            int maxPoints = 10;
            for (int i = 0; i < shown.Count; i++)
            {
                maxPoints = Mathf.Max(maxPoints, ChampionshipLastPoints(shown[i]));
            }

            int niceMax = Mathf.Max(10, Mathf.CeilToInt(maxPoints / 10f) * 10);

            UiFactory.DrawChartGridlines(plotArea, 4, fraction => Mathf.RoundToInt(fraction * niceMax).ToString());

            const float originX = 56f;
            const float originY = 32f;
            float plotWidth = Mathf.Max(1f, plotArea.rect.width - originX - 16f);
            float plotHeight = Mathf.Max(1f, plotArea.rect.height - originY - 20f);

            int roundCount = progression.rounds.Count;
            int labelStride = Mathf.Max(1, Mathf.CeilToInt(roundCount / 9f));
            for (int r = 0; r < roundCount; r += labelStride)
            {
                string roundLabel = progression.roundLabels != null && r < progression.roundLabels.Count ? progression.roundLabels[r] : ("R" + progression.rounds[r]);
                Text label = UiFactory.CreateText(plotArea, "Round label " + r, roundLabel, 12, UiFactory.TextMuted, TextAnchor.UpperCenter);
                RectTransform labelRect = label.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(0f, 0f);
                labelRect.pivot = new Vector2(0.5f, 1f);
                labelRect.anchoredPosition = new Vector2(ChampionshipRoundX(r, roundCount, plotWidth, originX), originY - 6f);
                labelRect.sizeDelta = new Vector2(70f, 16f);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            Color[] palette = UiFactory.ChartPalette;
            int colorCursor = 0;
            for (int s = 0; s < shown.Count; s++)
            {
                CareerManager.ChampionshipSeries series = shown[s];
                bool highlight = series.isPlayer || series.isPlayerTeam;
                bool isTeammate = !highlight && !string.IsNullOrEmpty(teammateSeriesId) && series.id == teammateSeriesId;
                Color color = highlight ? UiFactory.Accent : (isTeammate ? UiFactory.AccentCyan : palette[colorCursor % palette.Length]);
                if (!highlight && !isTeammate)
                {
                    colorCursor++;
                }

                bool selected = !string.IsNullOrEmpty(selectedSeriesId) && series.id == selectedSeriesId;
                float thickness = (highlight ? 4f : (showFullField ? 1.6f : 2.6f)) + (selected ? 1.5f : 0f);
                float lineAlpha = (!showFullField || highlight || isTeammate || selected) ? 1f : 0.5f;
                Color lineColor = new Color(color.r, color.g, color.b, lineAlpha);

                List<int> points = series.cumulativePoints;
                int count = Mathf.Min(roundCount, points != null ? points.Count : 0);
                List<Vector2> localPoints = new List<Vector2>();
                for (int r = 0; r < count; r++)
                {
                    localPoints.Add(new Vector2(ChampionshipRoundX(r, roundCount, plotWidth, originX), ChampionshipPointsY(points[r], niceMax, plotHeight, originY)));
                }

                UiFactory.DrawChartPolyline(plotArea, "Series " + series.id, localPoints, thickness, lineColor);

                if (count > 0)
                {
                    string seriesId = series.id;
                    int lastRoundIndex = count - 1;
                    UiFactory.CreateChartPoint(plotArea, "Series " + series.id + " point", localPoints[count - 1], highlight ? 13f : 9f, color, () =>
                    {
                        if (onPointTapped != null)
                        {
                            onPointTapped(seriesId, lastRoundIndex);
                        }
                    });
                }

                BuildChampionshipLegendEntry(legendArea, series.label, color, ChampionshipLastPoints(series), selected, () =>
                {
                    if (onPointTapped != null)
                    {
                        onPointTapped(series.id, count - 1);
                    }
                });
            }
        }

        // One legend row: color swatch, series label, final points total - tappable
        // to select the same series the corresponding line's end point selects.
        void BuildChampionshipLegendEntry(RectTransform legendArea, string label, Color color, int points, bool selected, System.Action onTap)
        {
            RectTransform row = UiFactory.CreateRect(legendArea, "Legend " + label, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.SetSize(row, 232f, 28f);
            Image background = row.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(background, selected ? new Color(color.r, color.g, color.b, 0.28f) : new Color(1f, 1f, 1f, 0.02f));
            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() =>
            {
                SimpleAudioManager.PlayClick();
                if (onTap != null)
                {
                    onTap();
                }
            });

            RectTransform swatch = UiFactory.CreateRect(row, "Legend swatch", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            swatch.sizeDelta = new Vector2(10f, 10f);
            swatch.pivot = new Vector2(0f, 0.5f);
            swatch.anchoredPosition = new Vector2(8f, 0f);
            Image swatchImage = swatch.gameObject.AddComponent<Image>();
            swatchImage.sprite = UiFactory.CircleSprite;
            swatchImage.color = color;
            swatchImage.raycastTarget = false;

            Text labelText = UiFactory.CreateText(row, "Legend label", label, 13, selected ? Color.white : UiFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(24f, 0f);
            labelRect.offsetMax = new Vector2(-46f, 0f);
            labelText.resizeTextForBestFit = true;
            labelText.resizeTextMinSize = 9;
            labelText.resizeTextMaxSize = 13;
            labelText.raycastTarget = false;

            Text pointsText = UiFactory.CreateText(row, "Legend points", points.ToString(), 13, UiFactory.TextMuted, TextAnchor.MiddleRight);
            RectTransform pointsRect = pointsText.GetComponent<RectTransform>();
            pointsRect.anchorMin = new Vector2(1f, 0f);
            pointsRect.anchorMax = new Vector2(1f, 1f);
            pointsRect.pivot = new Vector2(1f, 0.5f);
            pointsRect.offsetMin = new Vector2(-42f, 0f);
            pointsRect.offsetMax = new Vector2(-6f, 0f);
            pointsText.raycastTarget = false;
        }

        // Small label used to caption each mini-chart in the Combined overview -
        // added after BuildChampionshipChart runs (which clears plotArea's
        // children first) so it always survives the rebuild.
        void AddChampionshipMiniChartCaption(RectTransform plotArea, string caption)
        {
            Text label = UiFactory.CreateText(plotArea, "Mini chart caption", caption, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            label.fontStyle = FontStyle.Bold;
            RectTransform rect = label.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(10f, -20f);
            rect.offsetMax = new Vector2(-10f, -4f);
            label.raycastTarget = false;
        }

        // WDC/WCC championship progression graphs - cumulative points per round,
        // one line per driver/constructor. Both progressions are fetched fresh
        // from CareerManager every time this screen is built, so it always
        // reflects the latest completed race the moment it's opened.
        public void ShowChampionshipGraphs(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Championship graphs background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "Championship Progression", "Cumulative points by round - drivers' and constructors' championships.");

            CareerManager.ChampionshipProgression driverProgression = career.GetDriverChampionshipProgression();
            CareerManager.ChampionshipProgression constructorProgression = career.GetConstructorChampionshipProgression();

            string playerDriverId;
            string teammateDriverId;
            ResolvePlayerDriverIds(data, career, out playerDriverId, out teammateDriverId);

            // Tabs (left) + full-field expand toggle (right).
            RectTransform tabsRow = UiFactory.CreateRect(background, "Championship tabs row", new Vector2(0.05f, 0.855f), new Vector2(0.95f, 0.905f), Vector2.zero, Vector2.zero);
            RectTransform tabsHolder = UiFactory.CreateRect(tabsRow, "Championship tabs holder", new Vector2(0f, 0f), new Vector2(0.55f, 1f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(tabsHolder, 10, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateFilterTab(tabsHolder, "WDC", championshipGraphTab == 0, () => { championshipGraphTab = 0; ShowChampionshipGraphs(data, career, settings); });
            UiFactory.CreateFilterTab(tabsHolder, "WCC", championshipGraphTab == 1, () => { championshipGraphTab = 1; ShowChampionshipGraphs(data, career, settings); });
            UiFactory.CreateFilterTab(tabsHolder, "Combined", championshipGraphTab == 2, () => { championshipGraphTab = 2; ShowChampionshipGraphs(data, career, settings); });

            if (championshipGraphTab != 2)
            {
                RectTransform toggleHolder = UiFactory.CreateRect(tabsRow, "Championship field toggle holder", new Vector2(0.55f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
                Button fieldToggle = UiFactory.CreateSecondaryButton(toggleHolder, championshipGraphShowFullField ? "Showing Full Field" : "Showing Top Drivers", () => { championshipGraphShowFullField = !championshipGraphShowFullField; ShowChampionshipGraphs(data, career, settings); });
                RectTransform fieldToggleRect = fieldToggle.GetComponent<RectTransform>();
                fieldToggleRect.anchorMin = new Vector2(1f, 0.5f);
                fieldToggleRect.anchorMax = new Vector2(1f, 0.5f);
                fieldToggleRect.pivot = new Vector2(1f, 0.5f);
                fieldToggleRect.anchoredPosition = Vector2.zero;
                UiFactory.SetSize(fieldToggle, 280f, 44f);
            }

            // Summary cards: leaders/gaps/momentum/position, always recomputed
            // fresh from the two progressions above.
            RectTransform summaryRow = UiFactory.CreateRect(background, "Championship summary row", new Vector2(0.05f, 0.745f), new Vector2(0.95f, 0.85f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(summaryRow, 10, new RectOffset(0, 0, 0, 0));

            CareerManager.ChampionshipSeries driverPlayerSeries = FindChampionshipPlayerSeries(driverProgression, false);
            CareerManager.ChampionshipSeries driverLeaderSeries = FindChampionshipLeaderSeries(driverProgression);
            CareerManager.ChampionshipSeries teamPlayerSeries = FindChampionshipPlayerSeries(constructorProgression, true);
            CareerManager.ChampionshipSeries teamLeaderSeries = FindChampionshipLeaderSeries(constructorProgression);

            int driverGap = driverPlayerSeries != null && driverLeaderSeries != null ? ChampionshipLastPoints(driverLeaderSeries) - ChampionshipLastPoints(driverPlayerSeries) : 0;
            int teamGap = teamPlayerSeries != null && teamLeaderSeries != null ? ChampionshipLastPoints(teamLeaderSeries) - ChampionshipLastPoints(teamPlayerSeries) : 0;
            int driverPosition = driverPlayerSeries != null ? FindStandingsRank(career.Save.driverStandings, driverPlayerSeries.id) : -1;
            int teamPosition = teamPlayerSeries != null ? FindStandingsRank(career.Save.constructorStandings, teamPlayerSeries.id) : -1;

            CareerManager.ChampionshipProgression moverSource = championshipGraphTab == 1 ? constructorProgression : driverProgression;
            int gainDelta;
            CareerManager.ChampionshipSeries gainerSeries = FindChampionshipBiggestMover(moverSource, true, out gainDelta);
            int lossDelta;
            CareerManager.ChampionshipSeries loserSeries = FindChampionshipBiggestMover(moverSource, false, out lossDelta);

            UiFactory.CreateStatCard(summaryRow, "WDC Leader", driverLeaderSeries != null ? driverLeaderSeries.label + "  (" + ChampionshipLastPoints(driverLeaderSeries) + ")" : "--", 240f);
            UiFactory.CreateStatCard(summaryRow, "Gap To You", driverPlayerSeries != null ? (driverGap <= 0 ? "Leading" : "-" + driverGap + " pts") : "--", 170f);
            UiFactory.CreateStatCard(summaryRow, "WCC Leader", teamLeaderSeries != null ? teamLeaderSeries.label + "  (" + ChampionshipLastPoints(teamLeaderSeries) + ")" : "--", 240f);
            UiFactory.CreateStatCard(summaryRow, "Gap To Your Team", teamPlayerSeries != null ? (teamGap <= 0 ? "Leading" : "-" + teamGap + " pts") : "--", 190f);
            UiFactory.CreateStatCard(summaryRow, "Biggest Gainer", gainerSeries != null ? gainerSeries.label + "  +" + gainDelta : "--", 220f);
            UiFactory.CreateStatCard(summaryRow, "Biggest Loser", loserSeries != null ? loserSeries.label + "  " + lossDelta : "--", 220f);
            UiFactory.CreateStatCard(summaryRow, "Your Position", (driverPosition > 0 ? "P" + driverPosition : "--") + " drv / " + (teamPosition > 0 ? "P" + teamPosition : "--") + " ctor", 220f);

            // Chart(s) + legend(s).
            if (championshipGraphTab == 2)
            {
                const float top = 0.735f;
                const float bottom = 0.17f;
                const float gap = 0.015f;
                float mid = (top + bottom) * 0.5f;

                RectTransform plotTop = UiFactory.CreateChartPlotArea(background, "WDC mini plot", new Vector2(0.05f, mid + gap), new Vector2(0.72f, top), Vector2.zero, Vector2.zero);
                RectTransform legendTop = UiFactory.CreateScrollPanel(background, "WDC mini legend", new Vector2(0.73f, mid + gap), new Vector2(0.95f, top), 6, new RectOffset(8, 8, 8, 8));
                BuildChampionshipChart(plotTop, legendTop, driverProgression, teammateDriverId, false, championshipGraphSelectedSeriesId, championshipGraphSelectedRound,
                    (seriesId, roundIndex) => OnChampionshipPointTapped(data, career, settings, seriesId, roundIndex));
                AddChampionshipMiniChartCaption(plotTop, "WDC - DRIVERS");

                RectTransform plotBottom = UiFactory.CreateChartPlotArea(background, "WCC mini plot", new Vector2(0.05f, bottom), new Vector2(0.72f, mid - gap), Vector2.zero, Vector2.zero);
                RectTransform legendBottom = UiFactory.CreateScrollPanel(background, "WCC mini legend", new Vector2(0.73f, bottom), new Vector2(0.95f, mid - gap), 6, new RectOffset(8, 8, 8, 8));
                BuildChampionshipChart(plotBottom, legendBottom, constructorProgression, "", false, championshipGraphSelectedSeriesId, championshipGraphSelectedRound,
                    (seriesId, roundIndex) => OnChampionshipPointTapped(data, career, settings, seriesId, roundIndex));
                AddChampionshipMiniChartCaption(plotBottom, "WCC - CONSTRUCTORS");
            }
            else
            {
                CareerManager.ChampionshipProgression activeProgression = championshipGraphTab == 0 ? driverProgression : constructorProgression;
                string activeTeammateId = championshipGraphTab == 0 ? teammateDriverId : "";
                RectTransform plotArea = UiFactory.CreateChartPlotArea(background, "Championship plot area", new Vector2(0.05f, 0.17f), new Vector2(0.72f, 0.735f), Vector2.zero, Vector2.zero);
                RectTransform legendArea = UiFactory.CreateScrollPanel(background, "Championship legend", new Vector2(0.73f, 0.17f), new Vector2(0.95f, 0.735f), 8, new RectOffset(10, 10, 10, 10));
                BuildChampionshipChart(plotArea, legendArea, activeProgression, activeTeammateId, championshipGraphShowFullField, championshipGraphSelectedSeriesId, championshipGraphSelectedRound,
                    (seriesId, roundIndex) => OnChampionshipPointTapped(data, career, settings, seriesId, roundIndex));
            }

            // Detail card for the tapped point/legend entry.
            RectTransform detailBar = UiFactory.CreateGlassPanel(background, "Championship detail bar", new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.16f), Vector2.zero, Vector2.zero, UiFactory.PanelDarker);
            string detailText = BuildChampionshipDetailText(driverProgression, constructorProgression, championshipGraphSelectedSeriesId, championshipGraphSelectedRound);
            Text detailLabel = UiFactory.CreateText(detailBar, "Championship detail text", detailText, 15, UiFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform detailLabelRect = detailLabel.GetComponent<RectTransform>();
            detailLabelRect.anchorMin = Vector2.zero;
            detailLabelRect.anchorMax = Vector2.one;
            detailLabelRect.offsetMin = new Vector2(20f, 6f);
            detailLabelRect.offsetMax = new Vector2(-20f, -6f);
            detailLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
        }

        // Nominal content width used inside the two Rivalry/Form glass cards -
        // real rendered width is auto-stretched by StretchListChildrenWidth, this
        // is only used to size the absolutely-positioned chip/avatar rows and to
        // pick a safe (conservative, never-overflow) chip-wrap ceiling.
        const float RivalryPanelContentWidth = 420f;

        // Part 3 (redesigned): rivalry & form dashboard. Two premium glass cards
        // (championship rival head-to-head, recent form) built with the same
        // avatar/chip/stat-bar language as BuildDriverCard/BuildTeamCard, plus a
        // real Team News feed sourced from CareerManager.GetNewsArticles below -
        // replaces the old flat BuildReportCard row of plain text blocks, which
        // read as a leftover from an earlier pass next to the rating-card screens.
        public void ShowRivalryHub(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Rivalry background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "Rivalry & Form", "Teammate battle, championship rival, recent form and the latest team news.");

            // Screen-body/vertical-layout pattern (see ShowRaceTyreSelect's bodyMargin)
            // instead of hand-picked fractional anchors for every panel: each section
            // gets guaranteed, non-overlapping space regardless of how tall its content
            // ends up being.
            RectTransform body = UiFactory.CreateScreenBody(background, 108f, 78f);
            RectTransform bodyMargin = UiFactory.CreateRect(body, "Rivalry body margin", Vector2.zero, Vector2.one, new Vector2(48f, 24f), new Vector2(-48f, -24f));
            VerticalLayoutGroup bodyLayout = UiFactory.AddVerticalLayout(bodyMargin, 20, new RectOffset(0, 0, 0, 0));
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = false;

            RectTransform topRow = UiFactory.CreateRect(bodyMargin, "Rivalry top row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.SetFlexibleRowHeight(topRow, 430f);
            UiFactory.AddResponsiveHorizontalLayout(topRow, 20, new RectOffset(0, 0, 0, 0));

            RectTransform rivalryCard = UiFactory.CreateGlassPanel(topRow, "Rivalry card", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiFactory.PanelDark);
            UiFactory.SetFlexibleColumnWidth(rivalryCard, 480f);
            BuildRivalrySection(rivalryCard, data, career);

            RectTransform formCard = UiFactory.CreateGlassPanel(topRow, "Form card", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiFactory.PanelDark);
            UiFactory.SetFlexibleColumnWidth(formCard, 480f);
            BuildFormSection(formCard, career);

            RectTransform newsContainer = UiFactory.CreateGlassPanel(bodyMargin, "Team news container", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiFactory.PanelDarker);
            UiFactory.SetFlexibleRowHeight(newsContainer, 220f);
            BuildTeamNewsSection(newsContainer, career);

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
        }

        // Rivalry card: identity, trait chips, an intensity indicator derived from
        // how close/frequent the head-to-head is, race/qualifying comparison bars,
        // a momentum line and the latest real rivalry headline - or a polished
        // empty state before a rival has been identified (very early career).
        void BuildRivalrySection(RectTransform card, GameDataRepository data, CareerManager career)
        {
            RectTransform list = UiFactory.CreateRect(card, "Rivalry list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(list, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.StretchListChildrenWidth(list);

            UiFactory.CreateSubHeader(list, "Championship Rival");

            DriverData rivalDriver = string.IsNullOrEmpty(career.Save.rivalDriverId) ? null : data.FindDriver(career.Save.rivalDriverId);
            if (rivalDriver == null)
            {
                BuildEmptyState(list, "No rival identified yet - keep racing. One is picked automatically once your results are close enough to compare against another driver.", RivalryPanelContentWidth);
                return;
            }

            // Part 1: resolve through GetEffectiveDriver so a mid-career
            // transfer is reflected here too, instead of the rival's stale
            // drivers.json teamId.
            rivalDriver = career.GetEffectiveDriver(rivalDriver);
            TeamData rivalTeam = data.FindTeam(rivalDriver.teamId);
            Color rivalColor = rivalTeam != null ? rivalTeam.PrimaryUnityColor : UiFactory.Accent;

            RectTransform identity = UiFactory.CreateRect(list, "Rival identity row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.SetSize(identity, RivalryPanelContentWidth, 56f);
            RectTransform avatar = UiFactory.CreateProceduralHelmetIcon(identity, rivalDriver.abbreviation.ToUpperInvariant(), rivalColor, 48f);
            SetTopLeft(avatar, 0f, 0f);
            Text rivalName = UiFactory.CreateText(identity, "Rival name", rivalDriver.displayName, 18, Color.white, TextAnchor.UpperLeft);
            rivalName.fontStyle = FontStyle.Bold;
            SetTopLeft(rivalName.rectTransform, 60f, 2f);
            UiFactory.SetSize(rivalName, RivalryPanelContentWidth - 60f, 24f);
            Text rivalMeta = UiFactory.CreateText(identity, "Rival meta", rivalTeam != null ? rivalTeam.name : rivalDriver.teamId, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(rivalMeta.rectTransform, 60f, 28f);
            UiFactory.SetSize(rivalMeta, RivalryPanelContentWidth - 60f, 20f);

            List<string> traits = DriverTraits.Compute(rivalDriver);
            if (traits.Count > 0)
            {
                RectTransform traitRow = UiFactory.CreateRect(list, "Rival traits row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                float traitHeight = UiFactory.CreateWrappingChipRow(traitRow, "Rival traits", traits, UiFactory.AccentAmber, 0f, 0f, RivalryPanelContentWidth);
                UiFactory.SetSize(traitRow, RivalryPanelContentWidth, traitHeight);
            }

            // Wrapped in its own plain (non-layout) row rather than added to `list`
            // directly: `list` has StretchListChildrenWidth applied for the
            // comparison bars below, which would otherwise force this pill to
            // stretch into a full-width bar instead of a compact badge.
            int totalEvents = career.Save.rivalRaceWins + career.Save.rivalRaceLosses + career.Save.rivalQualifyingWins + career.Save.rivalQualifyingLosses;
            RectTransform intensityRow = UiFactory.CreateRect(list, "Rival intensity row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.SetSize(intensityRow, RivalryPanelContentWidth, 30f);
            Text intensityPill = UiFactory.CreatePillLabel(intensityRow, "Rivalry Intensity: " + RivalryIntensityLabel(totalEvents, career.Save.rivalRaceWins, career.Save.rivalRaceLosses),
                RivalryIntensityColor(totalEvents, career.Save.rivalRaceWins, career.Save.rivalRaceLosses));
            SetTopLeft(intensityPill.rectTransform, 0f, 2f);

            UiFactory.CreateComparisonBar(list, "Race Wins - You / Rival", career.Save.rivalRaceWins, UiFactory.AccentGreen, career.Save.rivalRaceLosses, rivalColor,
                Mathf.Max(1, career.Save.rivalRaceWins + career.Save.rivalRaceLosses), RivalryPanelContentWidth);
            UiFactory.CreateComparisonBar(list, "Qualifying Wins - You / Rival", career.Save.rivalQualifyingWins, UiFactory.AccentGreen, career.Save.rivalQualifyingLosses, rivalColor,
                Mathf.Max(1, career.Save.rivalQualifyingWins + career.Save.rivalQualifyingLosses), RivalryPanelContentWidth);

            string momentum = career.Save.rivalRaceWins == career.Save.rivalRaceLosses
                ? "Momentum: even at the moment."
                : career.Save.rivalRaceWins > career.Save.rivalRaceLosses
                    ? "<color=#6CFF8D>Momentum: you're ahead in the head-to-head.</color>"
                    : "<color=#FF6C6C>Momentum: the rival has the edge right now.</color>";
            Text momentumText = UiFactory.CreateText(list, "Rival momentum", momentum, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            momentumText.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(momentumText, RivalryPanelContentWidth, 22f);

            Text incidentsNote = UiFactory.CreateText(list, "Rival incidents note",
                "Incidents between the two of you aren't tracked individually yet - see the post-race report for full incident detail.",
                13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            incidentsNote.fontStyle = FontStyle.Italic;
            incidentsNote.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(incidentsNote, RivalryPanelContentWidth, 34f);

            string narrative = FindLatestNewsHeadline(career, CareerManager.NewsCategoryRivalry);
            Text narrativeText = UiFactory.CreateText(list, "Rival narrative", string.IsNullOrEmpty(narrative)
                ? "Next rival review in " + Mathf.Max(0, 4 - career.Save.roundsSinceRivalPicked) + " round(s)."
                : narrative, 13, UiFactory.AccentCyan, TextAnchor.UpperLeft);
            narrativeText.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(narrativeText, RivalryPanelContentWidth, 40f);
        }

        // Form card: recent finishing positions as tier-colored chips, qualifying
        // trend and consistency pulled from the real (uncapped) round-by-round
        // history rather than just the 3-race rolling window, DNF/incident count,
        // and a teammate comparison - or a polished empty state pre-round-1.
        void BuildFormSection(RectTransform card, CareerManager career)
        {
            RectTransform list = UiFactory.CreateRect(card, "Form list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(list, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.StretchListChildrenWidth(list);

            UiFactory.CreateSubHeader(list, "Recent Form");

            List<RaceResultRecord> raceHistory = career.Save.raceResults;
            if (raceHistory == null || raceHistory.Count == 0)
            {
                BuildEmptyState(list, "No races completed yet - your form guide fills in after your first race weekend.", RivalryPanelContentWidth);
                return;
            }

            // Last up to 5 races' player entries, oldest first - a genuinely real
            // window rather than just the capped 3-race recentFormPositions list.
            List<RaceResultEntry> recentPlayerResults = new List<RaceResultEntry>();
            for (int i = Mathf.Max(0, raceHistory.Count - 5); i < raceHistory.Count; i++)
            {
                RaceResultEntry entry = raceHistory[i].results != null ? raceHistory[i].results.Find(e => e.isPlayer) : null;
                if (entry != null)
                {
                    recentPlayerResults.Add(entry);
                }
            }

            RectTransform positionsRow = UiFactory.CreateRect(list, "Form positions row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.SetSize(positionsRow, RivalryPanelContentWidth, 30f);
            UiFactory.AddHorizontalLayout(positionsRow, 6, new RectOffset(0, 0, 0, 0));
            for (int i = 0; i < recentPlayerResults.Count; i++)
            {
                int pos = recentPlayerResults[i].finishingPosition;
                Color chipColor = pos <= 3 ? UiFactory.AccentGreen : (pos <= 10 ? UiFactory.AccentAmber : UiFactory.TextMuted);
                UiFactory.CreatePillLabel(positionsRow, "P" + pos, chipColor);
            }

            Text formTrend = UiFactory.CreateText(list, "Form trend", FormTrendLabel(career.Save.recentFormPositions), 13, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            UiFactory.SetSize(formTrend, RivalryPanelContentWidth, 20f);

            List<int> qualiPositions = new List<int>();
            List<QualifyingResultRecord> qualiHistory = career.Save.qualifyingResults;
            if (qualiHistory != null)
            {
                for (int i = Mathf.Max(0, qualiHistory.Count - 5); i < qualiHistory.Count; i++)
                {
                    QualifyingResultEntry entry = qualiHistory[i].results != null ? qualiHistory[i].results.Find(e => e.isPlayer) : null;
                    if (entry != null)
                    {
                        qualiPositions.Add(entry.position);
                    }
                }
            }

            string qualiLine = qualiPositions.Count == 0
                ? "Qualifying pace: not enough data yet."
                : "Qualifying pace: " + string.Join(" -> ", qualiPositions.ConvertAll(p => "P" + p).ToArray()) + "  (" + TrendWord(qualiPositions) + ")";
            Text qualiText = UiFactory.CreateText(list, "Qualifying trend", qualiLine, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            qualiText.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(qualiText, RivalryPanelContentWidth, 20f);

            string consistencyLine;
            if (recentPlayerResults.Count < 2)
            {
                consistencyLine = "Consistency: not enough data yet.";
            }
            else
            {
                int best = int.MaxValue;
                int worst = 0;
                for (int i = 0; i < recentPlayerResults.Count; i++)
                {
                    best = Mathf.Min(best, recentPlayerResults[i].finishingPosition);
                    worst = Mathf.Max(worst, recentPlayerResults[i].finishingPosition);
                }

                int spread = worst - best;
                consistencyLine = "Consistency: " + (spread <= 3 ? "very consistent" : (spread <= 7 ? "somewhat variable" : "erratic")) + " (P" + best + " to P" + worst + ")";
            }

            Text consistencyText = UiFactory.CreateText(list, "Form consistency", consistencyLine, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            UiFactory.SetSize(consistencyText, RivalryPanelContentWidth, 20f);

            int dnfCount = 0;
            int incidentTotal = 0;
            for (int i = 0; i < recentPlayerResults.Count; i++)
            {
                RaceResultEntry entry = recentPlayerResults[i];
                if (!string.IsNullOrEmpty(entry.penaltyReason) && entry.penaltyReason.Contains("DNF"))
                {
                    dnfCount++;
                }

                incidentTotal += entry.trackLimitWarnings + entry.lockups;
            }

            Text incidentsText = UiFactory.CreateText(list, "Form incidents",
                "DNFs: " + dnfCount + " / " + recentPlayerResults.Count + " recent races  ·  Incidents flagged: " + incidentTotal,
                13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            UiFactory.SetSize(incidentsText, RivalryPanelContentWidth, 20f);

            UiFactory.CreateDivider(list);
            UiFactory.CreateSubHeader(list, "Teammate Comparison");
            UiFactory.CreateComparisonBar(list, "Race Wins - You / Teammate", career.Save.teammateRaceWins, UiFactory.Accent, career.Save.teammateRaceLosses, UiFactory.AccentCyan,
                Mathf.Max(1, career.Save.teammateRaceWins + career.Save.teammateRaceLosses), RivalryPanelContentWidth);
            UiFactory.CreateComparisonBar(list, "Qualifying Wins - You / Teammate", career.Save.teammateQualifyingWins, UiFactory.Accent, career.Save.teammateQualifyingLosses, UiFactory.AccentCyan,
                Mathf.Max(1, career.Save.teammateQualifyingWins + career.Save.teammateQualifyingLosses), RivalryPanelContentWidth);
            Text teammateVerdict = UiFactory.CreateText(list, "Teammate verdict", TeammateBattleVerdict(career.Save.teammateRaceWins, career.Save.teammateRaceLosses), 13, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            UiFactory.SetSize(teammateVerdict, RivalryPanelContentWidth, 20f);
        }

        // Team News: real articles (headline + body + category) from
        // CareerManager.GetNewsArticles, each row sized from an estimate of how
        // many lines its body will actually wrap to - the old version reused the
        // CreateRect(...zero...)+SetFixedRowHeight pattern that never gave the row
        // a real size, so every article rendered at zero height and the panel
        // looked empty even when news existed.
        void BuildTeamNewsSection(RectTransform container, CareerManager career)
        {
            RectTransform newsPanel = UiFactory.CreateScrollPanel(container, "News feed panel", new Vector2(0.015f, 0.05f), new Vector2(0.985f, 0.95f), 10, new RectOffset(20, 20, 16, 16));
            UiFactory.StretchListChildrenWidth(newsPanel);
            UiFactory.CreateSubHeader(newsPanel, "Team News");

            List<NewsArticle> articles = career.GetNewsArticles();
            if (articles == null || articles.Count == 0)
            {
                bool tooEarly = career.Save.currentSeason <= 1 && career.Save.currentRound <= 1;
                BuildEmptyState(newsPanel, tooEarly
                    ? "No news yet - team news (results, rivalries, R&D, race control drama) appears here after your first race weekend."
                    : "Nothing to report right now - check back after your next session.", 1400f);
                return;
            }

            const float newsWidth = 1600f;
            const float bodyStartX = 210f;
            const float bodyWidth = newsWidth - bodyStartX - 40f;
            for (int i = articles.Count - 1; i >= 0; i--)
            {
                NewsArticle article = articles[i];
                float bodyHeight = EstimateWrappedTextHeight(article.body, bodyWidth, 7f, 18f);
                float rowHeight = Mathf.Max(64f, 40f + bodyHeight + 14f);

                RectTransform row = UiFactory.CreateRect(newsPanel, "News row " + i, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                UiFactory.SetSize(row, newsWidth, rowHeight);
                Image rowBackground = row.gameObject.AddComponent<Image>();
                UiFactory.StyleRoundedSmall(rowBackground, i % 2 == 0 ? UiFactory.RowEven : UiFactory.RowOdd);

                Text categoryPill = UiFactory.CreatePillLabel(row, article.category, NewsCategoryColor(article.category));
                SetTopLeft(categoryPill.rectTransform, 14f, 10f);

                Text headline = UiFactory.CreateText(row, "News headline", article.headline, 15, Color.white, TextAnchor.UpperLeft);
                headline.fontStyle = FontStyle.Bold;
                SetTopLeft(headline.rectTransform, bodyStartX, 8f);
                UiFactory.SetSize(headline, newsWidth - bodyStartX - 190f, 22f);

                Text roundLabel = UiFactory.CreateText(row, "News round", article.raceWeekLabel, 13, UiFactory.TextMuted, TextAnchor.UpperRight);
                SetTopRight(roundLabel.rectTransform, 14f, 10f);
                UiFactory.SetSize(roundLabel, 170f, 18f);

                Text body = UiFactory.CreateText(row, "News body", article.body, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
                body.verticalOverflow = VerticalWrapMode.Overflow;
                SetTopLeft(body.rectTransform, bodyStartX, 40f);
                UiFactory.SetSize(body, bodyWidth, bodyHeight + 4f);

                if (UiFactory.AnimationsEnabled)
                {
                    UiFadeIn reveal = row.gameObject.AddComponent<UiFadeIn>();
                    reveal.startDelay = Mathf.Min(articles.Count - 1 - i, 10) * 0.03f;
                }
            }
        }

        // Small "not enough data" placeholder shared by the rivalry/form/news
        // sections - a muted dot plus an explanatory line instead of a screen
        // that just looks broken/blank before there's real history to show.
        void BuildEmptyState(RectTransform list, string message, float width)
        {
            RectTransform row = UiFactory.CreateRect(list, "Empty state", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.SetSize(row, width, 90f);
            Image dot = UiFactory.CreatePulsingDot(row, "Empty state dot", 10f, UiFactory.TextMuted);
            SetTopLeft(dot.rectTransform, 2f, 6f);
            Text text = UiFactory.CreateText(row, "Empty state text", message, 14, UiFactory.TextMuted, TextAnchor.UpperLeft);
            text.verticalOverflow = VerticalWrapMode.Overflow;
            SetTopLeft(text.rectTransform, 22f, 0f);
            UiFactory.SetSize(text, width - 22f, 86f);
        }

        // Rough text-wrap height estimate (character-count heuristic, same spirit
        // as UiFactory.CreateWrappingChipRow's charWidth) so a news row's real
        // RectTransform height actually matches how many lines its body wraps to,
        // instead of a fixed guess that clips long articles or leaves a gap under
        // short ones.
        float EstimateWrappedTextHeight(string text, float width, float charWidth, float lineHeight)
        {
            if (string.IsNullOrEmpty(text) || width <= 0f)
            {
                return lineHeight;
            }

            int charsPerLine = Mathf.Max(10, Mathf.FloorToInt(width / charWidth));
            int lines = Mathf.Max(1, Mathf.CeilToInt(text.Length / (float)charsPerLine));
            return lines * lineHeight;
        }

        Color NewsCategoryColor(string category)
        {
            if (category == CareerManager.NewsCategoryRace) return UiFactory.Accent;
            if (category == CareerManager.NewsCategoryRnd) return UiFactory.AccentCyan;
            if (category == CareerManager.NewsCategoryRivalry) return UiFactory.AccentPurple;
            if (category == CareerManager.NewsCategoryRaceControl) return UiFactory.AccentAmber;
            if (category == CareerManager.NewsCategoryRegulations) return UiFactory.AccentAmber;
            if (category == CareerManager.NewsCategoryRumour) return UiFactory.AccentGreen;
            return UiFactory.TextMuted;
        }

        string FindLatestNewsHeadline(CareerManager career, string category)
        {
            List<NewsArticle> articles = career.GetNewsArticles();
            if (articles == null)
            {
                return "";
            }

            for (int i = articles.Count - 1; i >= 0; i--)
            {
                if (articles[i].category == category)
                {
                    return articles[i].headline;
                }
            }

            return "";
        }

        string TrendWord(List<int> positions)
        {
            if (positions.Count < 2)
            {
                return "steady";
            }

            int first = positions[0];
            int last = positions[positions.Count - 1];
            if (last < first) return "improving";
            if (last > first) return "sliding";
            return "steady";
        }

        string RivalryIntensityLabel(int totalEvents, int wins, int losses)
        {
            if (totalEvents == 0)
            {
                return "BUILDING";
            }

            float closeness = 1f - Mathf.Abs(wins - losses) / (float)Mathf.Max(1, totalEvents);
            if (closeness >= 0.6f) return "FIERCE";
            if (closeness >= 0.3f) return "WARM";
            return "ONE-SIDED";
        }

        Color RivalryIntensityColor(int totalEvents, int wins, int losses)
        {
            if (totalEvents == 0)
            {
                return UiFactory.TextMuted;
            }

            float closeness = 1f - Mathf.Abs(wins - losses) / (float)Mathf.Max(1, totalEvents);
            if (closeness >= 0.6f) return UiFactory.Accent;
            if (closeness >= 0.3f) return UiFactory.AccentAmber;
            return UiFactory.TextMuted;
        }

        string TraitLine(DriverData driver)
        {
            List<string> traits = DriverTraits.Compute(driver);
            return traits.Count == 0 ? "" : string.Join(" · ", traits.ToArray());
        }

        string TeammateBattleVerdict(int wins, int losses)
        {
            if (wins + losses == 0) return "No head-to-head data yet.";
            if (wins > losses) return "You're winning the teammate battle.";
            if (losses > wins) return "Teammate is ahead in the battle.";
            return "Dead even with your teammate.";
        }

        string FormTrendLabel(List<int> recentFormPositions)
        {
            if (recentFormPositions == null || recentFormPositions.Count < 2)
            {
                return "Trend: not enough data yet.";
            }

            int first = recentFormPositions[0];
            int last = recentFormPositions[recentFormPositions.Count - 1];
            if (last < first) return "Trend: improving.";
            if (last > first) return "Trend: sliding back.";
            return "Trend: holding steady.";
        }

        // Separate onboarding/setup flow for choosing a driver name, team, or an
        // existing driver to play as - kept out of the day-to-day career dashboard.
        public void ShowCareerSetup(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            if (!careerSetupDraftNameSet)
            {
                careerSetupDraftName = career.Save.playerDriverName;
                careerSetupDraftNameSet = true;
            }

            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Career setup background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "Driver & Team", "Choose who you race as, then start a new career with that setup.");

            RectTransform left = UiFactory.CreateCard(background, "Setup identity card", new Vector2(0.05f, 0.14f), new Vector2(0.5f, 0.82f));
            RectTransform identityList = UiFactory.CreateRect(left, "Identity list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(identityList, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(identityList, "Driver Name");
            InputField nameInput = UiFactory.CreateInputField(identityList, careerSetupDraftName);
            // Team selection below now rebuilds the whole screen so its button
            // highlighting can update - without shadowing the typed name here
            // and feeding it back in as the InputField's default text above,
            // that rebuild would silently discard whatever the player had typed.
            nameInput.onValueChanged.AddListener(value => careerSetupDraftName = value);

            UiFactory.CreateSubHeader(identityList, "Starting Team");
            Text selectedTeam = UiFactory.CreateText(identityList, "Selected team", data.FindTeam(selectedTeamId).name, 20, Color.white, TextAnchor.MiddleLeft);
            RectTransform teamGrid = UiFactory.CreateRect(identityList, "Team buttons", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            teamGrid.sizeDelta = new Vector2(560f, 240f);
            GridLayoutGroup grid = teamGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(134f, 36f);
            grid.spacing = new Vector2(8f, 8f);
            // Selection state was previously invisible - the only feedback a
            // pick had happened was the "Selected team" text line changing, so
            // the grid of 10+ near-identical buttons gave no at-a-glance answer
            // to "which one is active". SetButtonSelected (already used for tab
            // bars elsewhere) now highlights the current pick directly on the grid.
            for (int i = 0; i < data.Teams.teams.Count; i++)
            {
                TeamData team = data.Teams.teams[i];
                Button teamButton = UiFactory.CreateButton(teamGrid, team.shortName, () =>
                {
                    selectedTeamId = team.id;
                    ShowCareerSetup(data, career, settings);
                });
                UiFactory.SetButtonSelected(teamButton, team.id == selectedTeamId);
            }

            RectTransform right = UiFactory.CreateCard(background, "Setup driver card", new Vector2(0.52f, 0.14f), new Vector2(0.95f, 0.82f));
            RectTransform driverList = UiFactory.CreateRect(right, "Driver list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(driverList, 10, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(driverList, "Or Play As An Existing Driver");
            RectTransform modeRow = UiFactory.CreateRect(driverList, "Mode row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            modeRow.sizeDelta = new Vector2(560f, 40f);
            Button modeButton = UiFactory.CreateSecondaryButton(modeRow, useExistingDriver ? "Using existing driver" : "Using custom driver", () =>
            {
                useExistingDriver = !useExistingDriver;
                // Bug fix: switching back to "custom driver" used to leave the
                // previously-picked real driver's id sitting in selectedDriverId,
                // which could get saved as the player's identity and leave that
                // same real driver unfixed in the AI roster - two cars with the
                // same name/team. Clearing it here means the toggle actually
                // means what its label says.
                if (!useExistingDriver)
                {
                    selectedDriverId = "";
                }

                ShowCareerSetup(data, career, settings);
            });
            UiFactory.SetSize(modeButton, 300f, 40f);

            // Redundancy/streamlining audit: this grid used to hard-cap at the
            // first 15 of the 22 drivers in the roster (Mathf.Min(15, ...)),
            // silently making roughly a third of the grid - more than three
            // full teams - impossible to pick as an existing driver. Wrapped in
            // a real scroll-grid (CreateScrollGridPanel, the same pattern
            // BuildDriverCard's grid uses) instead of a fixed-size
            // GridLayoutGroup so every driver in the roster is reachable.
            RectTransform driverGrid = UiFactory.CreateScrollGridPanel(driverList, "Driver buttons", Vector2.zero, Vector2.zero, new Vector2(176f, 34f), new Vector2(8f, 8f), new RectOffset(4, 4, 4, 4));
            RectTransform driverGridViewport = driverGrid.parent as RectTransform;
            if (driverGridViewport != null)
            {
                driverGridViewport.sizeDelta = new Vector2(560f, 250f);
            }
            for (int i = 0; i < data.Drivers.drivers.Count; i++)
            {
                DriverData driver = data.Drivers.drivers[i];
                Button driverButton = UiFactory.CreateButton(driverGrid, driver.abbreviation + " " + driver.displayName, () =>
                {
                    selectedDriverId = driver.id;
                    useExistingDriver = true;
                    selectedTeamId = driver.teamId;
                    careerSetupDraftName = driver.displayName;
                    ShowCareerSetup(data, career, settings);
                });
                UiFactory.SetButtonSelected(driverButton, useExistingDriver && driver.id == selectedDriverId);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Cancel", () =>
            {
                careerSetupDraftNameSet = false;
                ShowCareerHub(data, career, settings);
            });
            UiFactory.CreatePrimaryButton(footerRight, "Start New Career", () =>
            {
                careerSetupDraftNameSet = false;
                career.StartNewCareer(nameInput.text, selectedTeamId, useExistingDriver, selectedDriverId);
                ShowCareerHub(data, career, settings);
            });
        }

        // Pending R&D results surface once on the career hub, then the queue is
        // cleared - rendering the card consumes the report.
        void BuildRndReportCard(RectTransform parent, CareerManager career)
        {
            if (career.Save.pendingRndMessages == null || career.Save.pendingRndMessages.Count == 0)
            {
                return;
            }

            Text header = UiFactory.CreateText(parent, "Rnd report header", "R&D REPORT", 14, UiFactory.AccentAmber, TextAnchor.MiddleLeft);
            UiFactory.SetSize(header, 384f, 22f);
            for (int i = 0; i < career.Save.pendingRndMessages.Count; i++)
            {
                RectTransform card = UiFactory.CreateRect(parent, "Rnd report " + i, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                card.sizeDelta = new Vector2(384f, 56f);
                Image cardBackground = card.gameObject.AddComponent<Image>();
                cardBackground.color = UiFactory.PanelDark;
                UiFactory.CreateBand(card, "Report rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.AccentAmber);
                Text message = UiFactory.CreateText(card, "Report message", career.Save.pendingRndMessages[i], 14, UiFactory.TextPrimary, TextAnchor.MiddleLeft);
                RectTransform messageRect = message.GetComponent<RectTransform>();
                messageRect.anchorMin = Vector2.zero;
                messageRect.anchorMax = Vector2.one;
                messageRect.offsetMin = new Vector2(14f, 4f);
                messageRect.offsetMax = new Vector2(-10f, -4f);
            }

            career.Save.pendingRndMessages.Clear();
            career.Write();
        }

        // Full R&D management screen: department facilities, live projects with
        // progress, the rework bench and the risk-mode upgrade tree.
        public void ShowRndCenter(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Rnd background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "R&D Command Center",
                "Season " + career.Save.currentSeason + ", Round " + career.Save.currentRound + "  ·  Facilities, live projects and the upgrade tree.");

            string regulationRisk = career.Save.regulationAffectedCategories == null || career.Save.regulationAffectedCategories.Count == 0
                ? "None announced"
                : string.Join(", ", career.Save.regulationAffectedCategories.ToArray());

            RectTransform stats = UiFactory.CreateRect(background, "Rnd stat strip", new Vector2(0.05f, 0.79f), new Vector2(0.95f, 0.85f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(stats, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(stats, "Resources", career.Save.resourcePoints + " RP", 200f);
            UiFactory.CreateStatCard(stats, "Project Slots", career.ActiveProjectCount() + " / " + career.MaxActiveProjects(), 200f);
            UiFactory.CreateStatCard(stats, "Season", "S" + career.Save.currentSeason + " · R" + career.Save.currentRound, 180f);
            Text riskValue = UiFactory.CreateStatCard(stats, "Regulation Risk At Season End", regulationRisk, 560f);
            riskValue.fontSize = 18;
            riskValue.color = UiFactory.AccentAmber;

            RectTransform departments = UiFactory.CreateScrollPanel(background, "Departments panel", new Vector2(0.05f, 0.14f), new Vector2(0.30f, 0.77f), 8, new RectOffset(16, 16, 14, 14));
            UiFactory.CreateSubHeader(departments, "Departments");
            Text departmentHint = UiFactory.CreateText(departments, "Department hint", "Each level: -10% dev time, +4% success, higher tiers. Every 2 levels add a project slot.", 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            departmentHint.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(departmentHint, 430f, 36f);
            for (int i = 0; i < CareerManager.DepartmentNames.Length; i++)
            {
                CreateDepartmentCard(departments, data, career, settings, i);
            }

            RectTransform projects = UiFactory.CreateScrollPanel(background, "Projects panel", new Vector2(0.32f, 0.14f), new Vector2(0.57f, 0.77f), 8, new RectOffset(16, 16, 14, 14));
            UiFactory.CreateSubHeader(projects, "Active Projects");
            int activeShown = 0;
            for (int i = 0; i < career.Save.activeUpgradeProjects.Count; i++)
            {
                ActiveUpgradeProject project = career.Save.activeUpgradeProjects[i];
                if (project.status == CareerManager.ProjectInDevelopment)
                {
                    CreateActiveProjectCard(projects, data, project);
                    activeShown++;
                }
            }

            if (activeShown == 0)
            {
                Text noProjects = UiFactory.CreateText(projects, "No projects", "No projects in development.\nStart one from the upgrade tree.", 15, UiFactory.TextMuted, TextAnchor.UpperLeft);
                noProjects.verticalOverflow = VerticalWrapMode.Overflow;
                UiFactory.SetSize(noProjects, 430f, 48f);
            }

            bool anyRework = false;
            for (int i = 0; i < career.Save.activeUpgradeProjects.Count; i++)
            {
                if (career.Save.activeUpgradeProjects[i].status == CareerManager.ProjectReworkAvailable)
                {
                    anyRework = true;
                }
            }

            if (anyRework)
            {
                UiFactory.CreateDivider(projects);
                UiFactory.CreateSubHeader(projects, "Rework Bench");
                for (int i = 0; i < career.Save.activeUpgradeProjects.Count; i++)
                {
                    ActiveUpgradeProject project = career.Save.activeUpgradeProjects[i];
                    if (project.status == CareerManager.ProjectReworkAvailable)
                    {
                        CreateReworkCard(projects, data, career, settings, project);
                    }
                }
            }

            RectTransform tree = UiFactory.CreateScrollPanel(background, "Upgrade tree panel", new Vector2(0.59f, 0.14f), new Vector2(0.95f, 0.77f), 8, new RectOffset(16, 16, 14, 14));
            UiFactory.CreateSubHeader(tree, "Upgrade Tree");
            string lastCategory = null;
            for (int i = 0; i < data.Upgrades.upgrades.Count; i++)
            {
                UpgradeData upgrade = data.Upgrades.upgrades[i];
                if (!string.IsNullOrEmpty(upgrade.category) && upgrade.category != lastCategory)
                {
                    lastCategory = upgrade.category;
                    bool affected = career.Save.regulationAffectedCategories != null && career.Save.regulationAffectedCategories.Contains(lastCategory);
                    int level = career.GetDepartmentLevel(career.GetDepartmentIndex(lastCategory));
                    string headerLine = lastCategory.ToUpperInvariant() + "  ·  LV " + level + (affected ? "  ·  REGULATION RISK" : "");
                    Text categoryText = UiFactory.CreateText(tree, "Upgrade category " + i, headerLine, 14, affected ? UiFactory.AccentAmber : UiFactory.Accent, TextAnchor.MiddleLeft);
                    UiFactory.SetSize(categoryText, 600f, 22f);
                }

                CreateRndUpgradeCard(tree, data, career, settings, upgrade);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        // Department facility card: level pips, per-level effect, upgrade button.
        void CreateDepartmentCard(RectTransform parent, GameDataRepository data, CareerManager career, GameSettingsStore settings, int departmentIndex)
        {
            string departmentName = CareerManager.DepartmentNames[departmentIndex];
            int level = career.GetDepartmentLevel(departmentIndex);
            int cost = career.GetDepartmentUpgradeCost(departmentIndex);
            bool maxed = level >= CareerManager.MaxDepartmentLevel;
            bool affordable = career.Save.resourcePoints >= cost;
            bool affected = career.Save.regulationAffectedCategories != null && career.Save.regulationAffectedCategories.Contains(departmentName);

            RectTransform card = UiFactory.CreateRect(parent, departmentName + " department card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(430f, 96f);
            Image cardBackground = card.gameObject.AddComponent<Image>();
            cardBackground.color = UiFactory.PanelDark;
            UiFactory.CreateBand(card, "Department rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), affected ? UiFactory.AccentAmber : UiFactory.AccentCyan);

            Text nameText = UiFactory.CreateText(card, "Department name", departmentName + (affected ? "  ·  REG RISK" : ""), 16, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            RectTransform nameRect = nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0.72f, 1f);
            nameRect.offsetMin = new Vector2(14f, -28f);
            nameRect.offsetMax = new Vector2(0f, -6f);

            for (int i = 0; i < CareerManager.MaxDepartmentLevel; i++)
            {
                RectTransform pip = UiFactory.CreateRect(card, "Level pip " + i, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
                pip.sizeDelta = new Vector2(18f, 8f);
                pip.pivot = new Vector2(0f, 1f);
                pip.anchoredPosition = new Vector2(14f + i * 24f, -34f);
                Image pipImage = pip.gameObject.AddComponent<Image>();
                UiFactory.StyleRoundedSmall(pipImage, i < level ? UiFactory.AccentCyan : UiFactory.MeterTrack);
            }

            Text effectText = UiFactory.CreateText(card, "Department effect",
                level > 1
                    ? "-" + (level - 1) * 10 + "% dev time  ·  +" + (level - 1) * 4 + "% success  ·  tier " + level + " unlocked"
                    : "Level 1  ·  tier 1 projects only",
                13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform effectRect = effectText.GetComponent<RectTransform>();
            effectRect.anchorMin = new Vector2(0f, 0f);
            effectRect.anchorMax = new Vector2(0.62f, 0f);
            effectRect.offsetMin = new Vector2(14f, 8f);
            effectRect.offsetMax = new Vector2(0f, 34f);

            if (maxed)
            {
                Text maxText = UiFactory.CreateText(card, "Department max", "MAX LEVEL", 14, UiFactory.AccentGreen, TextAnchor.MiddleRight);
                RectTransform maxRect = maxText.GetComponent<RectTransform>();
                maxRect.anchorMin = new Vector2(0.62f, 0f);
                maxRect.anchorMax = new Vector2(1f, 0.55f);
                maxRect.offsetMin = Vector2.zero;
                maxRect.offsetMax = new Vector2(-12f, 0f);
            }
            else if (!affordable)
            {
                Text needText = UiFactory.CreateText(card, "Department need rp", "NEED " + cost + " RP", 14, UiFactory.TextMuted, TextAnchor.MiddleRight);
                RectTransform needRect = needText.GetComponent<RectTransform>();
                needRect.anchorMin = new Vector2(0.55f, 0f);
                needRect.anchorMax = new Vector2(1f, 0.55f);
                needRect.offsetMin = Vector2.zero;
                needRect.offsetMax = new Vector2(-12f, 0f);
            }
            else
            {
                Button upgradeButton = UiFactory.CreateButton(card, "Upgrade  " + cost + " RP", () =>
                {
                    career.TryUpgradeDepartment(departmentIndex);
                    ShowRndCenter(data, career, settings);
                });
                RectTransform buttonRect = upgradeButton.GetComponent<RectTransform>();
                buttonRect.anchorMin = new Vector2(1f, 0f);
                buttonRect.anchorMax = new Vector2(1f, 0f);
                buttonRect.pivot = new Vector2(1f, 0f);
                buttonRect.sizeDelta = new Vector2(160f, 34f);
                buttonRect.anchoredPosition = new Vector2(-12f, 8f);
            }
        }

        // Live project card: name, risk mode, week progress bar, success odds.
        void CreateActiveProjectCard(RectTransform parent, GameDataRepository data, ActiveUpgradeProject project)
        {
            UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == project.upgradeId);
            string projectName = upgrade != null ? upgrade.displayName : project.upgradeId;

            RectTransform card = UiFactory.CreateRect(parent, project.upgradeId + " project card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(430f, 92f);
            Image cardBackground = card.gameObject.AddComponent<Image>();
            cardBackground.color = UiFactory.PanelDark;
            UiFactory.CreateBand(card, "Project rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.AccentCyan);

            Text nameText = UiFactory.CreateText(card, "Project name", projectName, 16, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            RectTransform nameRect = nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0.68f, 1f);
            nameRect.offsetMin = new Vector2(14f, -28f);
            nameRect.offsetMax = new Vector2(0f, -6f);

            Text modeText = UiFactory.CreateText(card, "Project mode", RiskModeName(project.riskMode), 13, RiskModeColor(project.riskMode), TextAnchor.UpperRight);
            RectTransform modeRect = modeText.GetComponent<RectTransform>();
            modeRect.anchorMin = new Vector2(0.68f, 1f);
            modeRect.anchorMax = new Vector2(1f, 1f);
            modeRect.offsetMin = new Vector2(0f, -28f);
            modeRect.offsetMax = new Vector2(-12f, -6f);

            int doneWeeks = project.totalRaceWeeks - project.remainingRaceWeeks;
            Text progressText = UiFactory.CreateText(card, "Project progress",
                project.category + "  ·  " + doneWeeks + " / " + project.totalRaceWeeks + " weeks  ·  " + Mathf.RoundToInt(project.successChance * 100f) + "% success",
                13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform progressRect = progressText.GetComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0f, 1f);
            progressRect.anchorMax = new Vector2(1f, 1f);
            progressRect.offsetMin = new Vector2(14f, -52f);
            progressRect.offsetMax = new Vector2(-12f, -32f);

            Image fill = UiFactory.CreateProgressBar(card, "Project bar", 400f, 10f, UiFactory.AccentCyan, project.totalRaceWeeks <= 0 ? 0f : (float)doneWeeks / project.totalRaceWeeks);
            RectTransform barRect = fill.transform.parent.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 0f);
            barRect.pivot = new Vector2(0f, 0f);
            barRect.anchoredPosition = new Vector2(14f, 12f);
        }

        // Failed project on the rework bench: retry at 40% cost or abandon it.
        void CreateReworkCard(RectTransform parent, GameDataRepository data, CareerManager career, GameSettingsStore settings, ActiveUpgradeProject project)
        {
            UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == project.upgradeId);
            string projectName = upgrade != null ? upgrade.displayName : project.upgradeId;
            int reworkCost = career.GetReworkCost(project);
            bool affordable = career.Save.resourcePoints >= reworkCost;

            RectTransform card = UiFactory.CreateRect(parent, project.upgradeId + " rework card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(430f, 92f);
            Image cardBackground = card.gameObject.AddComponent<Image>();
            cardBackground.color = UiFactory.PanelDark;
            UiFactory.CreateBand(card, "Rework rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.Accent);

            Text nameText = UiFactory.CreateText(card, "Rework name", projectName, 16, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            RectTransform nameRect = nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0.68f, 1f);
            nameRect.offsetMin = new Vector2(14f, -28f);
            nameRect.offsetMax = new Vector2(0f, -6f);

            Text infoText = UiFactory.CreateText(card, "Rework info",
                "Failed  ·  retry " + Mathf.Max(1, project.totalRaceWeeks / 2) + " wk  ·  " + Mathf.RoundToInt(Mathf.Min(0.95f, project.successChance + 0.12f) * 100f) + "% success",
                13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform infoRect = infoText.GetComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0f, 1f);
            infoRect.anchorMax = new Vector2(1f, 1f);
            infoRect.offsetMin = new Vector2(14f, -52f);
            infoRect.offsetMax = new Vector2(-12f, -32f);

            if (affordable)
            {
                Button reworkButton = UiFactory.CreateButton(card, "Rework  " + reworkCost + " RP", () =>
                {
                    career.TryReworkProject(project.upgradeId);
                    ShowRndCenter(data, career, settings);
                });
                RectTransform reworkRect = reworkButton.GetComponent<RectTransform>();
                reworkRect.anchorMin = new Vector2(0f, 0f);
                reworkRect.anchorMax = new Vector2(0f, 0f);
                reworkRect.pivot = new Vector2(0f, 0f);
                reworkRect.sizeDelta = new Vector2(170f, 32f);
                reworkRect.anchoredPosition = new Vector2(14f, 8f);
            }
            else
            {
                Text needText = UiFactory.CreateText(card, "Rework need rp", "NEED " + reworkCost + " RP", 13, UiFactory.TextMuted, TextAnchor.LowerLeft);
                RectTransform needRect = needText.GetComponent<RectTransform>();
                needRect.anchorMin = new Vector2(0f, 0f);
                needRect.anchorMax = new Vector2(0.5f, 0f);
                needRect.offsetMin = new Vector2(14f, 8f);
                needRect.offsetMax = new Vector2(0f, 40f);
            }

            Button abandonButton = UiFactory.CreateDangerButton(card, "Abandon", () =>
            {
                career.AbandonProject(project.upgradeId);
                ShowRndCenter(data, career, settings);
            });
            RectTransform abandonRect = abandonButton.GetComponent<RectTransform>();
            abandonRect.anchorMin = new Vector2(1f, 0f);
            abandonRect.anchorMax = new Vector2(1f, 0f);
            abandonRect.pivot = new Vector2(1f, 0f);
            abandonRect.sizeDelta = new Vector2(130f, 32f);
            abandonRect.anchoredPosition = new Vector2(-12f, 8f);
        }

        // Upgrade tree node: tier/cost/time/odds plus a risk-mode launcher row
        // when the project can actually be started.
        void CreateRndUpgradeCard(RectTransform parent, GameDataRepository data, CareerManager career, GameSettingsStore settings, UpgradeData upgrade)
        {
            bool done = career.Save.completedUpgradeIds.Contains(upgrade.id);
            bool abandoned = career.Save.failedUpgradeIds.Contains(upgrade.id);
            ActiveUpgradeProject project = career.FindProject(upgrade.id);
            bool inDevelopment = project != null && project.status == CareerManager.ProjectInDevelopment;
            bool reworkable = project != null && project.status == CareerManager.ProjectReworkAvailable;
            bool prereqMissing = !string.IsNullOrEmpty(upgrade.requiredUpgradeId) && !career.Save.completedUpgradeIds.Contains(upgrade.requiredUpgradeId);
            bool tierLocked = career.GetDepartmentLevel(career.GetDepartmentIndex(upgrade.category)) < Mathf.Max(1, upgrade.tier);
            bool locked = prereqMissing || tierLocked;
            bool available = !done && !abandoned && !inDevelopment && !reworkable && !locked;
            bool slotsFull = career.ActiveProjectCount() >= career.MaxActiveProjects();

            RectTransform card = UiFactory.CreateRect(parent, upgrade.id + " card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(640f, available ? 116f : 82f);
            Image cardBackground = card.gameObject.AddComponent<Image>();
            cardBackground.color = available ? UiFactory.PanelDark : new Color(0.014f, 0.02f, 0.026f, 0.7f);

            Color stateColor = done ? UiFactory.AccentGreen
                : (inDevelopment ? UiFactory.AccentCyan
                : (reworkable ? UiFactory.Accent
                : (abandoned ? UiFactory.Accent
                : (locked ? UiFactory.TextMuted : UiFactory.AccentPurple))));
            UiFactory.CreateBand(card, "Upgrade state rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), stateColor);

            Text nameText = UiFactory.CreateText(card, "Upgrade name", upgrade.displayName, 16, available ? UiFactory.TextPrimary : UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform nameRect = nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0.72f, 1f);
            nameRect.offsetMin = new Vector2(14f, -28f);
            nameRect.offsetMax = new Vector2(0f, -6f);

            string state;
            if (done)
            {
                state = project != null && project.bonusApplied ? "COMPLETED +30%" : "COMPLETED";
            }
            else if (inDevelopment)
            {
                state = project.remainingRaceWeeks + "/" + project.totalRaceWeeks + " WK LEFT";
            }
            else if (reworkable)
            {
                state = "REWORK";
            }
            else if (abandoned)
            {
                state = "ABANDONED";
            }
            else if (locked)
            {
                state = "LOCKED";
            }
            else
            {
                state = slotsFull ? "SLOTS FULL" : "AVAILABLE";
            }

            Text stateText = UiFactory.CreateText(card, "Upgrade state", state, 13, stateColor, TextAnchor.UpperRight);
            RectTransform stateRect = stateText.GetComponent<RectTransform>();
            stateRect.anchorMin = new Vector2(0.6f, 1f);
            stateRect.anchorMax = new Vector2(1f, 1f);
            stateRect.offsetMin = new Vector2(0f, -28f);
            stateRect.offsetMax = new Vector2(-12f, -6f);

            string infoLine;
            if (prereqMissing)
            {
                UpgradeData required = data.Upgrades.upgrades.Find(item => item.id == upgrade.requiredUpgradeId);
                infoLine = "Requires " + (required != null ? required.displayName : upgrade.requiredUpgradeId);
            }
            else if (tierLocked)
            {
                infoLine = "Requires " + upgrade.category + " department level " + upgrade.tier;
            }
            else
            {
                infoLine = "Tier " + upgrade.tier + "  ·  " + upgrade.cost + " RP  ·  " + career.ComputeProjectWeeks(upgrade, CareerManager.RiskStandard) + " wk  ·  " +
                    Mathf.RoundToInt(career.ComputeProjectSuccessChance(upgrade, CareerManager.RiskStandard) * 100f) + "% std success";
            }

            Text infoText = UiFactory.CreateText(card, "Upgrade info", infoLine, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform infoRect = infoText.GetComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0f, 1f);
            infoRect.anchorMax = new Vector2(1f, 1f);
            infoRect.offsetMin = new Vector2(14f, -50f);
            infoRect.offsetMax = new Vector2(-12f, -30f);

            Text deltaText = UiFactory.CreateText(card, "Upgrade deltas", BuildUpgradeDeltaText(upgrade), 13, new Color(0.55f, 0.95f, 0.65f), TextAnchor.UpperLeft);
            RectTransform deltaRect = deltaText.GetComponent<RectTransform>();
            deltaRect.anchorMin = new Vector2(0f, 1f);
            deltaRect.anchorMax = new Vector2(1f, 1f);
            deltaRect.offsetMin = new Vector2(14f, -72f);
            deltaRect.offsetMax = new Vector2(-12f, -52f);

            if (available)
            {
                CreateRiskModeButton(card, data, career, settings, upgrade, CareerManager.RiskConservative, 0);
                CreateRiskModeButton(card, data, career, settings, upgrade, CareerManager.RiskStandard, 1);
                CreateRiskModeButton(card, data, career, settings, upgrade, CareerManager.RiskRush, 2);
                CreateRiskModeButton(card, data, career, settings, upgrade, CareerManager.RiskExperimental, 3);
            }
        }

        void CreateRiskModeButton(RectTransform card, GameDataRepository data, CareerManager career, GameSettingsStore settings, UpgradeData upgrade, int riskMode, int slot)
        {
            int cost = career.ComputeProjectCost(upgrade, riskMode);
            bool affordable = career.Save.resourcePoints >= cost;
            bool slotsFull = career.ActiveProjectCount() >= career.MaxActiveProjects();
            string label = RiskModeShortName(riskMode) + " " + career.ComputeProjectWeeks(upgrade, riskMode) + "wk " +
                Mathf.RoundToInt(career.ComputeProjectSuccessChance(upgrade, riskMode) * 100f) + "%";

            if (!affordable || slotsFull)
            {
                Text lockedText = UiFactory.CreateText(card, upgrade.id + " risk " + slot, label, 13, UiFactory.TextMuted, TextAnchor.MiddleCenter);
                // Best-fit rather than a flat size: "CONSERVATIVE 5wk 45%" and
                // similar longer combinations don't reliably fit this narrow
                // 148px control at a fixed 13pt without wrapping and clipping
                // against the fixed 30px height.
                lockedText.resizeTextForBestFit = true;
                lockedText.resizeTextMinSize = 10;
                lockedText.resizeTextMaxSize = 13;
                RectTransform lockedRect = lockedText.GetComponent<RectTransform>();
                PositionRiskModeControl(lockedRect, slot);
                return;
            }

            Button riskButton = UiFactory.CreateButton(card, label, () =>
            {
                career.TryStartUpgradeProject(upgrade.id, riskMode);
                ShowRndCenter(data, career, settings);
            });
            PositionRiskModeControl(riskButton.GetComponent<RectTransform>(), slot);
            // Clipping fix: CreateButton's label defaults to a flat 19pt, which
            // does not reliably fit longer combinations like "CONSERVATIVE
            // 5wk 45%" inside this control's narrow 148px width once resized
            // down to slot size - best-fit keeps every risk label fully
            // visible instead of wrapping and truncating against the 30px
            // control height.
            Text riskButtonLabel = riskButton.GetComponentInChildren<Text>();
            if (riskButtonLabel != null)
            {
                riskButtonLabel.resizeTextForBestFit = true;
                riskButtonLabel.resizeTextMinSize = 10;
                riskButtonLabel.resizeTextMaxSize = 15;
            }
        }

        void PositionRiskModeControl(RectTransform rect, int slot)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(148f, 30f);
            rect.anchoredPosition = new Vector2(14f + slot * 154f, 8f);
        }

        string RiskModeName(int riskMode)
        {
            if (riskMode == CareerManager.RiskConservative)
            {
                return "CONSERVATIVE";
            }

            if (riskMode == CareerManager.RiskRush)
            {
                return "RUSH";
            }

            if (riskMode == CareerManager.RiskExperimental)
            {
                return "EXPERIMENTAL";
            }

            return "STANDARD";
        }

        string RiskModeShortName(int riskMode)
        {
            if (riskMode == CareerManager.RiskConservative)
            {
                return "CONS";
            }

            if (riskMode == CareerManager.RiskRush)
            {
                return "RUSH";
            }

            if (riskMode == CareerManager.RiskExperimental)
            {
                return "EXP";
            }

            return "STD";
        }

        Color RiskModeColor(int riskMode)
        {
            if (riskMode == CareerManager.RiskConservative)
            {
                return UiFactory.AccentGreen;
            }

            if (riskMode == CareerManager.RiskRush)
            {
                return UiFactory.AccentAmber;
            }

            if (riskMode == CareerManager.RiskExperimental)
            {
                return UiFactory.AccentPurple;
            }

            return UiFactory.AccentCyan;
        }

        string BuildUpgradeDeltaText(UpgradeData upgrade)
        {
            string text = "";
            text += DeltaPart("TOP", upgrade.topSpeedDelta);
            text += DeltaPart("ACC", upgrade.accelerationDelta);
            text += DeltaPart("COR", upgrade.corneringDelta);
            text += DeltaPart("BRK", upgrade.brakingDelta);
            text += DeltaPart("REL", upgrade.reliabilityDelta);
            text += DeltaPart("ERS", upgrade.ersDelta);
            text += DeltaPart("TYR", upgrade.tyreDelta);
            text += DeltaPart("AER", upgrade.aeroDelta);
            text += DeltaPart("CHA", upgrade.chassisDelta);
            text += DeltaPart("PWR", upgrade.engineDelta);
            return string.IsNullOrEmpty(text) ? "No direct stat change" : text.TrimEnd();
        }

        string DeltaPart(string label, int delta)
        {
            if (delta == 0)
            {
                return "";
            }

            return label + (delta > 0 ? " +" : " ") + delta + "  ";
        }

        // Shared tab bar for the settings category screens.
        void BuildSettingsTabBar(RectTransform background, GameDataRepository data, CareerManager career, GameSettingsStore settings, int active)
        {
            UiFactory.CreateTopNav(background, "Settings");
            RectTransform tabs = UiFactory.CreateRect(background, "Settings tabs", new Vector2(0.06f, 0.8f), new Vector2(0.94f, 0.87f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(tabs, 10, new RectOffset(0, 0, 0, 0));
            CreateSettingsTab(tabs, "Gameplay", active == 0, () => ShowSettings(data, career, settings));
            CreateSettingsTab(tabs, "Assists", active == 1, () => ShowAssists(data, career, settings));
            CreateSettingsTab(tabs, "Display & HUD", active == 2, () => ShowDisplaySettings(data, career, settings));
            CreateSettingsTab(tabs, "Controls", active == 3, () => ShowControls(data, career, settings));

            // Navigation lives in the footer on every settings screen, not as a
            // tab masquerading as a settings category.
            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        void CreateSettingsTab(RectTransform parent, string label, bool active, UnityEngine.Events.UnityAction action)
        {
            UnityEngine.UI.Button tab = active
                ? UiFactory.CreatePrimaryButton(parent, label, action)
                : UiFactory.CreateSecondaryButton(parent, label, action);
            UiFactory.SetSize(tab, 200f, 44f);
        }

        public void ShowSettings(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Settings background", new Color(0.012f, 0.016f, 0.021f, 1f));
            BuildSettingsTabBar(background, data, career, settings, 0);

            RectTransform panel = UiFactory.CreateCard(background, "Gameplay card", new Vector2(0.06f, 0.12f), new Vector2(0.6f, 0.76f));
            // Scroll-wrapped instead of a plain stretched rect: this card has
            // accumulated enough rows across passes that it can exceed the card's
            // fixed height, and CreateSettingRow rows no longer carry their own
            // hardcoded width, so StretchListChildrenWidth makes the list fill
            // whatever width the card actually has instead of collapsing to zero.
            RectTransform list = UiFactory.CreateScrollPanel(panel, "Gameplay list", new Vector2(0.035f, 0.03f), new Vector2(0.965f, 0.97f), 8, new RectOffset(20, 20, 16, 16));
            UiFactory.StretchListChildrenWidth(list);
            UiFactory.CreateSubHeader(list, "Session");

            RectTransform lapsControl;
            UiFactory.CreateSettingRow(list, "Race Laps", "Shorter for quick sessions, longer for a full-length race.", out lapsControl);
            UiFactory.CreateCycleControl(lapsControl, settings.Current.laps.ToString(), () =>
            {
                settings.Current.laps = settings.Current.laps == 3 ? 5 : (settings.Current.laps == 5 ? 14 : 3);
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform ghostControl;
            UiFactory.CreateSettingRow(list, "Time Trial Ghost", "A translucent car tracing a recorded lap alongside you.", out ghostControl);
            UiFactory.CreateCycleControl(ghostControl, GhostModeLabel(settings.Current.ghostMode), () =>
            {
                settings.Current.ghostMode = (settings.Current.ghostMode + 1) % 3;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform gridControl;
            UiFactory.CreateSettingRow(list, "Grid Size", "Fixed at a full field for now.", out gridControl);
            UiFactory.CreateText(gridControl, "Grid size value", "22 drivers", 16, UiFactory.TextMuted, TextAnchor.MiddleRight).GetComponent<RectTransform>().anchorMin = new Vector2(0f, 0f);

            RectTransform difficultyControl;
            UiFactory.CreateSettingRow(list, "Difficulty", "Affects AI pace, braking margins, and mistake frequency.", out difficultyControl);
            UiFactory.CreateSegmentedControl(difficultyControl, new[] { "Easy", "Medium", "Hard", "Expert" }, settings.Current.difficultyIndex, index =>
            {
                settings.Current.difficultyIndex = index;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform tyreControl;
            UiFactory.CreateSettingRow(list, "Default Tyre", "Used for quick race and time trial starts.", out tyreControl);
            UiFactory.CreateCycleControl(tyreControl, settings.Current.tyreCompound, () =>
            {
                settings.Current.tyreCompound = NextTyreName(settings.Current.tyreCompound);
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform ersControl;
            UiFactory.CreateSettingRow(list, "ERS Strategy", "Default deployment behavior; overridden any time by holding Shift.", out ersControl);
            UiFactory.CreateSegmentedControl(ersControl, new[] { "Balanced", "Attack", "Harvest" }, settings.Current.ersMode, index =>
            {
                settings.Current.ersMode = index;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform gearsControl;
            UiFactory.CreateSettingRow(list, "Manual Gears", "Shift with Q/E instead of automatic.", out gearsControl);
            UiFactory.CreateToggleControl(gearsControl, settings.Current.manualGears, () =>
            {
                settings.Current.manualGears = !settings.Current.manualGears;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform audioControl;
            UiFactory.CreateSettingRow(list, "Audio", "Engine, collision, and UI sound effects.", out audioControl);
            UiFactory.CreateToggleControl(audioControl, settings.Current.audioEnabled, () =>
            {
                settings.Current.audioEnabled = !settings.Current.audioEnabled;
                settings.Save();
                SimpleAudioManager.ApplySettings(settings.Current);
                ShowSettings(data, career, settings);
            });

            AddVolumeRow(list, "Master Volume", settings, () => settings.Current.masterVolume, v => settings.Current.masterVolume = v, data, career);
            AddVolumeRow(list, "Engine Volume", settings, () => settings.Current.engineVolume, v => settings.Current.engineVolume = v, data, career);
            AddVolumeRow(list, "UI Volume", settings, () => settings.Current.uiVolume, v => settings.Current.uiVolume = v, data, career);
            AddVolumeRow(list, "Radio Volume", settings, () => settings.Current.radioVolume, v => settings.Current.radioVolume = v, data, career);
            AddVolumeRow(list, "Ambience Volume", settings, () => settings.Current.ambienceVolume, v => settings.Current.ambienceVolume = v, data, career);

            // Race control / safety car pass: its own card in the unused right
            // portion of the Gameplay tab, mirroring the left/right two-card split
            // already used on the Display & HUD tab rather than opening a new tab.
            RectTransform raceControlPanel = UiFactory.CreateCard(background, "Race Control card", new Vector2(0.64f, 0.12f), new Vector2(0.94f, 0.76f));
            // This card is only ~30% of the canvas width (~576px at the 1920
            // reference) - the exact case that used to overflow when every
            // CreateSettingRow was hardcoded to 760px regardless of its container.
            // Scroll-wrapped for the same reason as the Gameplay list above.
            RectTransform raceControlList = UiFactory.CreateScrollPanel(raceControlPanel, "Race Control list", new Vector2(0.035f, 0.03f), new Vector2(0.965f, 0.97f), 8, new RectOffset(20, 20, 16, 16));
            UiFactory.StretchListChildrenWidth(raceControlList);
            UiFactory.CreateSubHeader(raceControlList, "Race Control");

            RectTransform safetyCarControl;
            UiFactory.CreateSettingRow(raceControlList, "Safety Car", "Off: local yellows only. Reduced: rare VSC/SC. Standard: occasional. High: more frequent, still not constant.", out safetyCarControl);
            UiFactory.CreateCycleControl(safetyCarControl, SafetyCarFrequencyLabel(settings.Current.safetyCarFrequency), () =>
            {
                settings.Current.safetyCarFrequency = (settings.Current.safetyCarFrequency + 1) % 4;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform mechanicalControl;
            UiFactory.CreateSettingRow(raceControlList, "Mechanical Failures", "Whether cars can suffer race-ending mechanical failures.", out mechanicalControl);
            UiFactory.CreateCycleControl(mechanicalControl, MechanicalFailureModeLabel(settings.Current.mechanicalFailureMode), () =>
            {
                settings.Current.mechanicalFailureMode = (settings.Current.mechanicalFailureMode + 1) % 3;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform lockupsControl;
            UiFactory.CreateSettingRow(raceControlList, "Lockups", "Locked front wheels under heavy braking cause flat spots and vibration.", out lockupsControl);
            UiFactory.CreateToggleControl(lockupsControl, settings.Current.lockupsEnabled, () =>
            {
                settings.Current.lockupsEnabled = !settings.Current.lockupsEnabled;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform raceControlMessagesControl;
            UiFactory.CreateSettingRow(raceControlList, "Race Control Messages", "Yellow flag, safety car, and incident radio calls from the pit wall.", out raceControlMessagesControl);
            UiFactory.CreateToggleControl(raceControlMessagesControl, settings.Current.raceControlMessages, () =>
            {
                settings.Current.raceControlMessages = !settings.Current.raceControlMessages;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform engineerMessagesControl;
            UiFactory.CreateSettingRow(raceControlList, "Engineer Messages", "Off: silent. Minimal: urgent calls only. Standard: full radio chatter. Frequent: shorter cooldown between lines.", out engineerMessagesControl);
            UiFactory.CreateCycleControl(engineerMessagesControl, EngineerVerbosityLabel(settings.Current.engineerMessageVerbosity), () =>
            {
                settings.Current.engineerMessageVerbosity = (settings.Current.engineerMessageVerbosity + 1) % 4;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform racePresentationControl;
            UiFactory.CreateSettingRow(raceControlList, "Race Presentation", "Minimal: essentials only. Standard: banners and HUD flourishes. Cinematic: adds a finish-line camera flourish.", out racePresentationControl);
            UiFactory.CreateCycleControl(racePresentationControl, RacePresentationLabel(settings.Current.racePresentation), () =>
            {
                settings.Current.racePresentation = (settings.Current.racePresentation + 1) % 3;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform weatherVariabilityControl;
            UiFactory.CreateSettingRow(raceControlList, "Weather Variability", "How often and how far the forecast drifts from its base state during a session.", out weatherVariabilityControl);
            UiFactory.CreateCycleControl(weatherVariabilityControl, WeatherVariabilityLabel(settings.Current.weatherVariability), () =>
            {
                settings.Current.weatherVariability = (settings.Current.weatherVariability + 1) % 4;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform trackEvolutionControl;
            UiFactory.CreateSettingRow(raceControlList, "Track Evolution", "Grip rises slightly as the session goes on and rubber goes down, washed away again by heavy rain.", out trackEvolutionControl);
            UiFactory.CreateToggleControl(trackEvolutionControl, settings.Current.trackEvolutionEnabled, () =>
            {
                settings.Current.trackEvolutionEnabled = !settings.Current.trackEvolutionEnabled;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform practiceProgramsControl;
            UiFactory.CreateSettingRow(raceControlList, "Practice Programs", "Optional pre-qualifying programs for R&D points and setup confidence.", out practiceProgramsControl);
            UiFactory.CreateToggleControl(practiceProgramsControl, settings.Current.practiceProgramsEnabled, () =>
            {
                settings.Current.practiceProgramsEnabled = !settings.Current.practiceProgramsEnabled;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            RectTransform newsFeedControl;
            UiFactory.CreateSettingRow(raceControlList, "Career News Feed", "Headlines about your results, rivalries and R&D on the career hub.", out newsFeedControl);
            UiFactory.CreateToggleControl(newsFeedControl, settings.Current.careerNewsFeedEnabled, () =>
            {
                settings.Current.careerNewsFeedEnabled = !settings.Current.careerNewsFeedEnabled;
                settings.Save();
                ShowSettings(data, career, settings);
            });

            UiFactory.CreateDivider(raceControlList);
            RectTransform trackRecordsControl;
            UiFactory.CreateSettingRow(raceControlList, "Track Records", "Clears every locally stored best-lap time so records start fresh.", out trackRecordsControl);
            UiFactory.CreateCycleControl(trackRecordsControl, "Reset Records", () =>
            {
                PlayerRecordsStore.ResetTrackRecords();
                ShowSettings(data, career, settings);
            });
        }

        string EngineerVerbosityLabel(int value)
        {
            switch (value)
            {
                case 0: return "Off";
                case 1: return "Minimal";
                case 3: return "Frequent";
                default: return "Standard";
            }
        }

        string RacePresentationLabel(int value)
        {
            switch (value)
            {
                case 0: return "Minimal";
                case 2: return "Cinematic";
                default: return "Standard";
            }
        }

        string WeatherVariabilityLabel(int value)
        {
            switch (value)
            {
                case 0: return "Off";
                case 1: return "Low";
                case 3: return "High";
                default: return "Standard";
            }
        }

        string GhostModeLabel(int value)
        {
            switch (value)
            {
                case 1: return "Session Best";
                case 2: return "All-Time Best";
                default: return "Off";
            }
        }

        string CameraShakeLevelLabel(int value)
        {
            switch (value)
            {
                case 0: return "Off";
                case 1: return "Low";
                case 3: return "High";
                default: return "Standard";
            }
        }

        string SafetyCarFrequencyLabel(int value)
        {
            switch (value)
            {
                case 0: return "Off";
                case 1: return "Reduced";
                case 3: return "High";
                default: return "Standard";
            }
        }

        string MechanicalFailureModeLabel(int value)
        {
            switch (value)
            {
                case 0: return "Off";
                case 1: return "AI Only";
                default: return "Standard";
            }
        }

        public void ShowDisplaySettings(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Display settings background", new Color(0.012f, 0.016f, 0.021f, 1f));
            BuildSettingsTabBar(background, data, career, settings, 2);

            RectTransform left = UiFactory.CreateCard(background, "HUD card", new Vector2(0.06f, 0.12f), new Vector2(0.5f, 0.76f));
            RectTransform leftList = UiFactory.CreateScrollPanel(left, "HUD list", new Vector2(0.035f, 0.03f), new Vector2(0.965f, 0.97f), 8, new RectOffset(20, 20, 16, 16));
            UiFactory.StretchListChildrenWidth(leftList);
            UiFactory.CreateSubHeader(leftList, "HUD & Camera");

            RectTransform hudScaleControl;
            UiFactory.CreateSettingRow(leftList, "HUD Scale", "Scales every in-race panel around its own screen edge, so nothing clips off.", out hudScaleControl);
            UiFactory.CreateCycleControl(hudScaleControl, settings.Current.hudScale.ToString("0.00"), () =>
            {
                settings.Current.hudScale = CycleFloat(settings.Current.hudScale, 0.8f, 1.25f, 0.15f);
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            // UI Scale: unlike HUD Scale above (which only resizes in-race
            // panels), this is a single global multiplier applied to the
            // whole runtime canvas (menus AND the HUD) via
            // UiFactory.ApplyUiScale - see GameSettingsStore.UiScale for why
            // it's stored outside the main settings blob. Applied immediately
            // on change (not just on next screen load) so the cycle control's
            // own label and every row on this screen visibly resize right
            // away, giving instant feedback on what the player just picked.
            RectTransform uiScaleControl;
            UiFactory.CreateSettingRow(leftList, "UI Scale", "Resizes every menu and HUD panel - use this if text feels too small or too big for your display.", out uiScaleControl);
            UiFactory.CreateCycleControl(uiScaleControl, settings.UiScale.ToString("0.00"), () =>
            {
                settings.SetUiScale(CycleFloat(settings.UiScale, GameSettingsStore.UiScaleMin, GameSettingsStore.UiScaleMax, 0.05f));
                UiFactory.ApplyUiScale(canvas, settings.UiScale);
                ShowDisplaySettings(data, career, settings);
            });

            RectTransform compactControl;
            UiFactory.CreateSettingRow(leftList, "Compact HUD", "Hides secondary cards and rows for a cleaner race view.", out compactControl);
            UiFactory.CreateToggleControl(compactControl, settings.Current.compactHud, () =>
            {
                settings.Current.compactHud = !settings.Current.compactHud;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            RectTransform unitsControl;
            UiFactory.CreateSettingRow(leftList, "Speed Units", "", out unitsControl);
            UiFactory.CreateSegmentedControl(unitsControl, new[] { "KM/H", "MPH" }, settings.Current.useMphUnits ? 1 : 0, index =>
            {
                settings.Current.useMphUnits = index == 1;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            RectTransform fovControl;
            UiFactory.CreateSettingRow(leftList, "Camera FOV", "Base field of view before speed-based widening.", out fovControl);
            UiFactory.CreateCycleControl(fovControl, settings.Current.cameraFov.ToString("0") + "°", () =>
            {
                settings.Current.cameraFov = CycleFloat(settings.Current.cameraFov, 52f, 76f, 4f);
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            RectTransform shakeToggleControl;
            UiFactory.CreateSettingRow(leftList, "Camera Shake", "Master switch for all camera movement effects.", out shakeToggleControl);
            UiFactory.CreateToggleControl(shakeToggleControl, settings.Current.cameraShake, () =>
            {
                settings.Current.cameraShake = !settings.Current.cameraShake;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            // A 6-segment row doesn't fit next to a label in a card this size once
            // the row genuinely respects the card's width instead of floating at a
            // fixed 760px - switched to the same single-button cycle control used by
            // every other numeric setting on this screen (HUD Scale, Camera FOV,
            // Scenery Density).
            RectTransform shakeAmountControl;
            UiFactory.CreateSettingRow(leftList, "Camera Movement", "How strongly speed, kerbs, braking, and impacts move the camera.", out shakeAmountControl);
            UiFactory.CreateCycleControl(shakeAmountControl, settings.Current.cameraShakeStrength.ToString("0.0"), () =>
            {
                settings.Current.cameraShakeStrength = CycleFloat(settings.Current.cameraShakeStrength, 0f, 0.5f, 0.1f);
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            RectTransform shakeLevelControl;
            UiFactory.CreateSettingRow(leftList, "Camera Shake Level", "Off/Low/Standard/High multiplier on top of Camera Movement above.", out shakeLevelControl);
            UiFactory.CreateCycleControl(shakeLevelControl, CameraShakeLevelLabel(settings.Current.cameraShakeLevel), () =>
            {
                settings.Current.cameraShakeLevel = (settings.Current.cameraShakeLevel + 1) % 4;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            RectTransform animControl;
            UiFactory.CreateSettingRow(leftList, "UI Animations", "Screen transitions and menu microinteractions.", out animControl);
            UiFactory.CreateToggleControl(animControl, settings.Current.uiAnimations, () =>
            {
                settings.Current.uiAnimations = !settings.Current.uiAnimations;
                UiFactory.AnimationsEnabled = settings.Current.uiAnimations;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            // HUD module toggles: independent of Compact HUD above (which is a
            // single all-or-nothing preset) - these let a player keep the full HUD
            // but drop just the one or two modules they personally don't want.
            UiFactory.CreateSubHeader(leftList, "HUD Modules");
            AddHudModuleToggle(leftList, "Timing Tower", "The full running order down the left side.", settings, data, career,
                s => s.hudShowTimingTower, (s, v) => s.hudShowTimingTower = v);
            AddHudModuleToggle(leftList, "Track Map", "The mini circuit map with car positions.", settings, data, career,
                s => s.hudShowTrackMap, (s, v) => s.hudShowTrackMap = v);
            AddHudModuleToggle(leftList, "Input Telemetry", "Throttle/brake/ERS input bars.", settings, data, career,
                s => s.hudShowInputTelemetry, (s, v) => s.hudShowInputTelemetry = v);
            AddHudModuleToggle(leftList, "Car Status Card", "Tyres, fuel, ERS, and damage readout.", settings, data, career,
                s => s.hudShowCarStatus, (s, v) => s.hudShowCarStatus = v);
            AddHudModuleToggle(leftList, "Radio Messages", "The engineer message stack.", settings, data, career,
                s => s.hudShowRadio, (s, v) => s.hudShowRadio = v);
            AddHudModuleToggle(leftList, "Race Control Banner", "Yellow/VSC/safety car/red flag status banner.", settings, data, career,
                s => s.hudShowRaceControlBanner, (s, v) => s.hudShowRaceControlBanner = v);
            AddHudModuleToggle(leftList, "Progress Strip", "The thin field-position strip along the top.", settings, data, career,
                s => s.hudShowProgressStrip, (s, v) => s.hudShowProgressStrip = v);

            RectTransform right = UiFactory.CreateCard(background, "Graphics card", new Vector2(0.54f, 0.12f), new Vector2(0.94f, 0.76f));
            RectTransform rightList = UiFactory.CreateScrollPanel(right, "Graphics list", new Vector2(0.035f, 0.03f), new Vector2(0.965f, 0.97f), 8, new RectOffset(20, 20, 16, 16));
            UiFactory.StretchListChildrenWidth(rightList);
            UiFactory.CreateSubHeader(rightList, "Graphics");

            RectTransform qualityControl;
            UiFactory.CreateSettingRow(rightList, "Quality", "Shadows and anti-aliasing; applies on the next track build.", out qualityControl);
            UiFactory.CreateSegmentedControl(qualityControl, new[] { "Low", "Medium", "High", "Ultra" }, Mathf.Clamp(settings.Current.graphicsQuality, 0, 3), index =>
            {
                settings.Current.graphicsQuality = index;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            RectTransform particlesControl;
            UiFactory.CreateSettingRow(rightList, "Particles", "Dust, spray, lockup smoke, and collision sparks.", out particlesControl);
            UiFactory.CreateToggleControl(particlesControl, settings.Current.particlesEnabled, () =>
            {
                settings.Current.particlesEnabled = !settings.Current.particlesEnabled;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            RectTransform sceneryControl;
            UiFactory.CreateSettingRow(rightList, "Scenery Density", "Trackside detail; applies on the next track build.", out sceneryControl);
            UiFactory.CreateCycleControl(sceneryControl, settings.Current.sceneryDensity.ToString("0.00"), () =>
            {
                settings.Current.sceneryDensity = CycleFloat(settings.Current.sceneryDensity, 0.5f, 2f, 0.5f);
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
        }

        // One row per HUD module toggle - a getter/setter pair keeps this to a
        // single-line call site per module instead of seven near-identical
        // CreateSettingRow/CreateToggleControl blocks in ShowDisplaySettings.
        void AddHudModuleToggle(RectTransform parent, string label, string description, GameSettingsStore settings, GameDataRepository data, CareerManager career,
            Func<GameSettingsData, bool> getter, Action<GameSettingsData, bool> setter)
        {
            RectTransform control;
            UiFactory.CreateSettingRow(parent, label, description, out control);
            UiFactory.CreateToggleControl(control, getter(settings.Current), () =>
            {
                setter(settings.Current, !getter(settings.Current));
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
        }

        // Rebuilt onto the same card + label/value row language as every other
        // settings tab (Gameplay, Assists, Display & HUD), replacing a single
        // hand-formatted paragraph of raw text - this was the one screen still
        // reading as an unstyled reference sheet next to the rest of the app.
        public void ShowControls(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Controls background", new Color(0.012f, 0.016f, 0.021f, 1f));
            BuildSettingsTabBar(background, data, career, settings, 3);

            RectTransform left = UiFactory.CreateCard(background, "Driving controls card", new Vector2(0.06f, 0.12f), new Vector2(0.5f, 0.76f));
            RectTransform leftList = UiFactory.CreateScrollPanel(left, "Driving controls list", new Vector2(0.035f, 0.03f), new Vector2(0.965f, 0.97f), 6, new RectOffset(20, 20, 16, 16));
            UiFactory.CreateSubHeader(leftList, "Driving");
            const float rowWidth = 560f;
            AddControlRow(leftList, "Throttle", "W  /  Up Arrow", rowWidth);
            AddControlRow(leftList, "Brake / Reverse", "S  /  Down Arrow", rowWidth);
            AddControlRow(leftList, "Steer Left / Right", "A/D  /  Left/Right Arrow", rowWidth);
            AddControlRow(leftList, "Gamepad", "Vertical / Horizontal axes", rowWidth);
            AddControlRow(leftList, "DRS Toggle", "Space (when race rules allow it)", rowWidth);
            AddControlRow(leftList, "ERS Mode Cycle", "R", rowWidth);
            AddControlRow(leftList, "ERS Manual Override", "Left Shift  /  Right Shift", rowWidth);
            AddControlRow(leftList, "Manual Shift Down / Up", "Q  /  E  (Manual Gears only)", rowWidth);
            AddControlRow(leftList, "Pit Request", "P", rowWidth);
            AddControlRow(leftList, "Camera Toggle", "C", rowWidth);
            AddControlRow(leftList, "Reset Car", "Hold R", rowWidth);
            AddControlRow(leftList, "Debug Overlay", "F1", rowWidth);

            RectTransform right = UiFactory.CreateCard(background, "Session controls card", new Vector2(0.54f, 0.12f), new Vector2(0.94f, 0.76f));
            RectTransform rightList = UiFactory.CreateScrollPanel(right, "Session controls list", new Vector2(0.035f, 0.03f), new Vector2(0.965f, 0.97f), 6, new RectOffset(20, 20, 16, 16));
            UiFactory.CreateSubHeader(rightList, "Session");
            AddControlRow(rightList, "Pause / Resume", "Esc", rowWidth);
            AddControlRow(rightList, "Restart Session", "Pause menu", rowWidth);
            AddControlRow(rightList, "Return to Menu", "Pause menu", rowWidth);
            UiFactory.CreateDivider(rightList);
            UiFactory.CreateSubHeader(rightList, "Assists");
            Text assistsNote = UiFactory.CreateText(rightList, "Assists note",
                "Auto-brake, ABS, traction control, racing line, and input sensitivity are changed on the Assists tab. ERS mode can also be cycled in-race with R.",
                14, new Color(0.78f, 0.86f, 0.9f), TextAnchor.UpperLeft);
            assistsNote.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(assistsNote, rowWidth, 78f);
        }

        // Read-only key/binding row for the Controls screen - the same shape as
        // every other label+value line in the app (CreateBreakdownRow), just
        // pointed at a fixed input instead of a togglable setting.
        void AddControlRow(Transform parent, string action, string binding, float width)
        {
            UiFactory.CreateBreakdownRow(parent, action, binding, UiFactory.TextPrimary, width);
        }

        public void ShowDriverRatings(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            ShowDriverRatings(data, career, settings, "overall");
        }

        // sortKey re-sorts the same underlying DriverData list pulled from
        // GameDataRepository - the filter tabs never recompute driver stats,
        // they just change the comparator before the (cheap) card rebuild.
        void ShowDriverRatings(GameDataRepository data, CareerManager career, GameSettingsStore settings, string sortKey)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Driver ratings background", new Color(0.006f, 0.009f, 0.014f, 1f));
            UiFactory.CreateScreenHeader(background, "Driver Ratings", "Overall is a weighted blend led by pace, racecraft and qualifying, updated each season from actual performance.");

            RectTransform modeTabs = UiFactory.CreateRect(background, "Ratings mode tabs", new Vector2(0.06f, 0.885f), new Vector2(0.34f, 0.935f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(modeTabs, 10, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateFilterTab(modeTabs, "Drivers", true, () => { });
            UiFactory.CreateFilterTab(modeTabs, "Teams", false, () => ShowTeamRatings(data, career, settings));

            RectTransform filterRow = UiFactory.CreateRect(background, "Ratings filters", new Vector2(0.06f, 0.815f), new Vector2(0.94f, 0.87f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(filterRow, 10, new RectOffset(0, 0, 0, 0));
            AddDriverFilterTab(filterRow, "Overall", "overall", sortKey, data, career, settings);
            AddDriverFilterTab(filterRow, "Pace", "pace", sortKey, data, career, settings);
            AddDriverFilterTab(filterRow, "Qualifying", "qualifying", sortKey, data, career, settings);
            AddDriverFilterTab(filterRow, "Racecraft", "racecraft", sortKey, data, career, settings);
            AddDriverFilterTab(filterRow, "Potential", "potential", sortKey, data, career, settings);

            List<DriverData> drivers = new List<DriverData>(data.Drivers.drivers);
            // Driver market + progression: show this season's effective team
            // and rating swing (see CareerManager.GetEffectiveDriver) rather
            // than the static drivers.json baseline, so a transfer or a rising/
            // declining driver is actually visible on the one screen built for
            // browsing driver stats and teams.
            if (career != null && career.Save != null)
            {
                for (int i = 0; i < drivers.Count; i++)
                {
                    drivers[i] = career.GetEffectiveDriver(drivers[i]);
                }
            }

            SortDriversByKey(drivers, sortKey);

            string playerDriverId;
            string teammateDriverId;
            ResolvePlayerDriverIds(data, career, out playerDriverId, out teammateDriverId);

            RectTransform grid = UiFactory.CreateScrollGridPanel(background, "Driver card grid", new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.79f), new Vector2(DriverCardWidth, DriverCardHeight), new Vector2(16f, 16f), new RectOffset(10, 10, 10, 10));
            for (int i = 0; i < drivers.Count; i++)
            {
                DriverData driver = drivers[i];
                TeamData team = data.FindTeam(driver.teamId);
                bool isPlayer = !string.IsNullOrEmpty(playerDriverId) && driver.id == playerDriverId;
                bool isTeammate = !isPlayer && !string.IsNullOrEmpty(teammateDriverId) && driver.id == teammateDriverId;
                BuildDriverCard(grid, driver, team, isPlayer, isTeammate, career);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        void AddDriverFilterTab(RectTransform parent, string label, string key, string activeKey, GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            UiFactory.CreateFilterTab(parent, label, key == activeKey, () => ShowDriverRatings(data, career, settings, key));
        }

        void SortDriversByKey(List<DriverData> drivers, string sortKey)
        {
            drivers.Sort((a, b) =>
            {
                int primary;
                switch (sortKey)
                {
                    case "pace":
                        primary = b.pace.CompareTo(a.pace);
                        break;
                    case "qualifying":
                        primary = b.qualifying.CompareTo(a.qualifying);
                        break;
                    case "racecraft":
                        primary = b.racecraft.CompareTo(a.racecraft);
                        break;
                    case "potential":
                        primary = b.developmentPotential.CompareTo(a.developmentPotential);
                        break;
                    default:
                        primary = b.OverallRating.CompareTo(a.OverallRating);
                        break;
                }

                return primary != 0 ? primary : b.pace.CompareTo(a.pace);
            });
        }

        // Heuristic only: drivers.json has no explicit "is the player" flag.
        // When the player picked an existing real driver, that id is the
        // player's card; otherwise CareerManager treats the team's first
        // roster entry as the vacated seat the player fills, so the other
        // entry on that team is the real teammate either way.
        void ResolvePlayerDriverIds(GameDataRepository data, CareerManager career, out string playerId, out string teammateId)
        {
            playerId = "";
            teammateId = "";
            if (career == null || career.Save == null || data == null)
            {
                return;
            }

            if (career.Save.useExistingDriver && !string.IsNullOrEmpty(career.Save.selectedDriverId))
            {
                playerId = career.Save.selectedDriverId;
            }

            List<DriverData> teamDrivers = data.GetDriversForTeam(career.Save.playerTeamId, career.Save.driverTransferRecords);
            string excludedId = string.IsNullOrEmpty(playerId) && teamDrivers.Count > 0 ? teamDrivers[0].id : playerId;
            for (int i = 0; i < teamDrivers.Count; i++)
            {
                if (teamDrivers[i].id != excludedId)
                {
                    teammateId = teamDrivers[i].id;
                    break;
                }
            }
        }

        // 1-3 threshold-based tags per driver - simple by design, not a scouting
        // algorithm. Order doubles as priority when a driver clears more than
        // three thresholds.
        // Part 8: driver rating cards read off the same DriverTraits.Compute(...)
        // list that AI tuning, radio messages and rivalry cards use, so a
        // "Wet Specialist" chip actually means the AI leans on it in the rain
        // rather than being a cosmetic label with no effect anywhere else.
        List<string> ComputeDriverTags(DriverData driver)
        {
            return DriverTraits.Compute(driver);
        }

        // Shared color ramp so a "90" reads the same purple whether it's on a
        // driver's Qualifying bar or a team's Cornering bar.
        static Color StatTierColor(float value)
        {
            if (value >= 90f)
            {
                return UiFactory.AccentPurple;
            }

            if (value >= 82f)
            {
                return UiFactory.AccentGreen;
            }

            if (value >= 72f)
            {
                return UiFactory.AccentAmber;
            }

            return UiFactory.TextMuted;
        }

        static void SetTopLeft(RectTransform rect, float x, float yFromTop)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -yFromTop);
        }

        static void SetTopRight(RectTransform rect, float xFromRight, float yFromTop)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-xFromRight, -yFromTop);
        }

        static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Premium driver card: procedural helmet avatar, name/team/number badge,
        // overall rating chip, role tags, and an 8-stat bar block. Built entirely
        // from UiFactory primitives - no external art.
        // Card height budgets for the worst realistic case (three long tags, all
        // forced onto their own row) so every card in the uniform ratings grid
        // shares one fixed size regardless of how many rows any single card's
        // chips actually need - see BuildDriverCard/BuildTeamCard.
        const float DriverCardWidth = 330f;
        // +12 vs the old 372: CreateStatBar's rows grew (bigger label/value
        // font) so the 4-row stat block below needs a little more room.
        const float DriverCardHeight = 384f;
        const float TeamCardWidth = 350f;
        // +19 vs the old 486: CreateStatBar's row growth across 5 rows, plus
        // the taller STRENGTHS/WEAKNESSES section labels above it.
        const float TeamCardHeight = 505f;

        void BuildDriverCard(RectTransform parent, DriverData driver, TeamData team, bool isPlayer, bool isTeammate, CareerManager career = null)
        {
            Color teamColor = team != null ? team.PrimaryUnityColor : new Color(0.5f, 0.55f, 0.6f);
            Color borderColor = isPlayer ? UiFactory.AccentGreen : (isTeammate ? UiFactory.AccentCyan : teamColor);
            RectTransform card = UiFactory.CreateBorderedCard(parent, "Driver card " + driver.id, DriverCardWidth, DriverCardHeight, borderColor, isPlayer || isTeammate);

            RectTransform avatar = UiFactory.CreateProceduralHelmetIcon(card, driver.abbreviation.ToUpperInvariant(), teamColor, 60f);
            SetTopLeft(avatar, 16f, 30f);

            if (isPlayer || isTeammate)
            {
                Text roleBadge = UiFactory.CreateText(card, "Driver card role badge", isPlayer ? "YOU" : "TEAMMATE", 13, isPlayer ? UiFactory.AccentGreen : UiFactory.AccentCyan, TextAnchor.UpperLeft);
                roleBadge.fontStyle = FontStyle.Bold;
                SetTopLeft(roleBadge.rectTransform, 88f, 8f);
                UiFactory.SetSize(roleBadge, 150f, 18f);
            }

            Text numberText = UiFactory.CreateText(card, "Driver card number", "#" + driver.number + "  " + driver.abbreviation.ToUpperInvariant(), 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(numberText.rectTransform, 88f, 30f);
            UiFactory.SetSize(numberText, 160f, 18f);

            // Long-name overflow fix: a handful of drivers (e.g. "Gabriel
            // Bortoleto", "Carlos Sainz Jr.") don't fit this 160px column at a
            // flat size 17 - the old fixed size wrapped to a second line that
            // the fixed 24px-tall box then silently truncated, dropping the
            // surname entirely and inviting overlap with the team line right
            // below. Best-fit shrinks the font instead of wrapping, and the
            // overflow safety net (matching the pattern already used for the
            // chip labels/radio cards) means a hypothetical name that still
            // doesn't fit at the floor size bleeds slightly rather than
            // vanishing.
            Text nameText = UiFactory.CreateText(card, "Driver card name", driver.displayName, 17, Color.white, TextAnchor.UpperLeft);
            nameText.fontStyle = FontStyle.Bold;
            nameText.resizeTextForBestFit = true;
            nameText.resizeTextMinSize = 12;
            nameText.resizeTextMaxSize = 17;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.verticalOverflow = VerticalWrapMode.Overflow;
            SetTopLeft(nameText.rectTransform, 88f, 48f);
            UiFactory.SetSize(nameText, 160f, 24f);

            string teamName = team == null ? driver.teamId : team.name;
            Text teamText = UiFactory.CreateText(card, "Driver card team", teamName, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            teamText.resizeTextForBestFit = true;
            teamText.resizeTextMinSize = 10;
            teamText.resizeTextMaxSize = 13;
            teamText.horizontalOverflow = HorizontalWrapMode.Overflow;
            teamText.verticalOverflow = VerticalWrapMode.Overflow;
            SetTopLeft(teamText.rectTransform, 88f, 72f);
            UiFactory.SetSize(teamText, 160f, 18f);

            int overall = driver.OverallRating;
            Color overallColor = StatTierColor(overall);
            RectTransform overallBadge = UiFactory.CreateRect(card, "Driver card overall badge", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            overallBadge.sizeDelta = new Vector2(54f, 54f);
            Image overallImage = overallBadge.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(overallImage, new Color(overallColor.r, overallColor.g, overallColor.b, 0.2f));
            SetTopRight(overallBadge, 16f, 22f);
            Text overallText = UiFactory.CreateText(overallBadge, "Driver card overall value", overall.ToString(), 22, overallColor, TextAnchor.MiddleCenter);
            overallText.fontStyle = FontStyle.Bold;
            StretchFull(overallText.GetComponent<RectTransform>());

            // Part 8: last season's overall rating swing (from this driver's
            // latest DriverRatingModifier), so a rising/declining driver is
            // visible at a glance without opening the season review.
            if (career != null && career.Save != null && career.Save.driverRatingModifiers != null)
            {
                DriverRatingModifier latest = career.Save.driverRatingModifiers.Find(m => m.driverId == driver.id && m.season == career.Save.currentSeason);
                if (latest != null && latest.lastOverallChange != 0)
                {
                    Color changeColor = latest.lastOverallChange > 0 ? UiFactory.AccentGreen : UiFactory.Accent;
                    string changeLabel = (latest.lastOverallChange > 0 ? "+" : "") + latest.lastOverallChange;
                    Text changeText = UiFactory.CreateText(card, "Driver card overall change", changeLabel, 12, changeColor, TextAnchor.MiddleCenter);
                    changeText.fontStyle = FontStyle.Bold;
                    UiFactory.SetSize(changeText, 54f, 16f);
                    SetTopRight(changeText.rectTransform, 16f, 78f);
                }
            }

            // Wrapping chip row (card overflow fix): tags now wrap onto extra
            // rows inside the card's own width instead of the old fixed-width
            // HorizontalLayoutGroup, which let long tags like "Qualifying
            // Monster" stretch straight past the card edge and clip into the
            // next card in the grid. The stat bar block below is positioned
            // dynamically off the actual height the tags used, so a
            // single-tag driver isn't left with a dead gap.
            List<string> tags = ComputeDriverTags(driver);
            const float tagRowY = 96f;
            const float tagRowWidth = DriverCardWidth - 32f;
            float tagRowHeight = UiFactory.CreateWrappingChipRow(card, "Driver card tags", tags, UiFactory.AccentAmber, 16f, tagRowY, tagRowWidth);

            string[] labels = { "Pace", "Qualifying", "Racecraft", "Overtaking", "Defending", "Consistency", "Tyre Mgmt", "Wet Skill" };
            int[] values = { driver.pace, driver.qualifying, driver.racecraft, driver.overtaking, driver.defending, driver.consistency, driver.tyreManagement, driver.wetSkill };
            const float columnWidth = 128f;
            float startY = tagRowY + tagRowHeight + 12f;
            // +3 vs the old 38: CreateStatBar's own row grew a few px to fit
            // its larger label/value font without clipping.
            const float rowHeight = 41f;
            for (int i = 0; i < labels.Length; i++)
            {
                int column = i / 4;
                int row = i % 4;
                RectTransform bar = UiFactory.CreateStatBar(card, labels[i], values[i], 100f, StatTierColor(values[i]), columnWidth);
                SetTopLeft(bar, 16f + column * (columnWidth + 20f), startY + row * rowHeight);
            }
        }

        // Car overall: 320-355 km/h real-world top speed range would swamp
        // every other 0-100 stat if used raw, so it's rescaled onto the same
        // 0-100 footing before blending - otherwise every car's top speed would
        // sit near the ceiling and the archetype/overall math would barely be
        // able to tell cars apart on anything else.
        static float TopSpeedScore(int topSpeedKmh)
        {
            return Mathf.Clamp((topSpeedKmh - 320f) / 35f * 100f, 0f, 100f);
        }

        // Part 6: delegates to the single canonical car-rating formula
        // (RatingCalculator.GetCarOverall) shared with GameDataRepository's
        // team-profile summary and every career R&D/AI-balancing call site, so
        // this screen can never disagree with any other about the same car.
        float ComputeCarOverall(CarPerformanceData car)
        {
            return RatingCalculator.GetCarOverall(car);
        }

        // The 10 car stats as (label, 0-100 score) pairs - shared by the stat
        // bar block and the strengths/weaknesses ranking so both read off the
        // same numbers.
        List<KeyValuePair<string, float>> BuildCarStatList(CarPerformanceData car)
        {
            return new List<KeyValuePair<string, float>>
            {
                new KeyValuePair<string, float>("Top Speed", TopSpeedScore(car.topSpeed)),
                new KeyValuePair<string, float>("Acceleration", car.acceleration),
                new KeyValuePair<string, float>("Cornering", car.cornering),
                new KeyValuePair<string, float>("Braking", car.braking),
                new KeyValuePair<string, float>("Reliability", car.reliability),
                new KeyValuePair<string, float>("ERS Efficiency", car.ersEfficiency),
                new KeyValuePair<string, float>("Tyre Mgmt", car.tyreManagement),
                new KeyValuePair<string, float>("Aero Efficiency", car.aeroEfficiency),
                new KeyValuePair<string, float>("Chassis Balance", car.chassisBalance),
                new KeyValuePair<string, float>("Engine Power", car.enginePower)
            };
        }

        // Compact stat names for the team card's strengths/weaknesses chips
        // (card overflow fix) - "Aero Efficiency" and "Chassis Balance" side by
        // side used to be long enough on their own to stretch a chip past the
        // card edge; the short forms below comfortably fit two to a row.
        static string ShortStatLabel(string statLabel)
        {
            switch (statLabel)
            {
                case "Acceleration": return "Accel";
                case "ERS Efficiency": return "ERS";
                case "Aero Efficiency": return "Aero";
                case "Chassis Balance": return "Chassis";
                case "Engine Power": return "Engine";
                default: return statLabel;
            }
        }

        // Simple threshold logic, not perfection: a stat only counts as the
        // car's defining trait if it clearly separates from the other two
        // "signature" stats (top speed / cornering / tyre management), and only
        // once it's already elite (>=90). Otherwise fall through to the
        // overall-driven archetypes.
        string ComputeCarArchetype(CarPerformanceData car, float overall)
        {
            float topSpeedScore = TopSpeedScore(car.topSpeed);
            float corneringScore = car.cornering;
            float tyreScore = car.tyreManagement;
            float topOfThree = Mathf.Max(topSpeedScore, Mathf.Max(corneringScore, tyreScore));

            if (topOfThree >= 90f)
            {
                if (Mathf.Approximately(topSpeedScore, topOfThree) && topSpeedScore - corneringScore >= 6f && topSpeedScore - tyreScore >= 6f)
                {
                    return "High-Speed Monster";
                }

                if (Mathf.Approximately(corneringScore, topOfThree) && corneringScore - topSpeedScore >= 6f && corneringScore - tyreScore >= 6f)
                {
                    return "Cornering Beast";
                }

                if (Mathf.Approximately(tyreScore, topOfThree) && tyreScore - topSpeedScore >= 6f && tyreScore - corneringScore >= 6f)
                {
                    return "Tyre-Friendly";
                }
            }

            if (overall >= 88f && car.reliability < 80)
            {
                return "Fragile Rocket";
            }

            float[] allScores = { topSpeedScore, car.acceleration, corneringScore, car.braking, car.reliability, car.ersEfficiency, tyreScore, car.aeroEfficiency, car.chassisBalance, car.enginePower };
            float max = allScores[0];
            float min = allScores[0];
            for (int i = 1; i < allScores.Length; i++)
            {
                max = Mathf.Max(max, allScores[i]);
                min = Mathf.Min(min, allScores[i]);
            }

            if (overall >= 88f && (max - min) <= 13f)
            {
                return "Balanced Front-Runner";
            }

            if (overall < 80f)
            {
                return "Backmarker";
            }

            return "Midfield Fighter";
        }

        // Team Ratings: a car-focused sibling to Driver Ratings, sharing the
        // same card-grid/stat-bar/chip visual language. Reachable from the
        // career hub's secondary grid and via the Drivers/Teams toggle on
        // either screen.
        public void ShowTeamRatings(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Team ratings background", new Color(0.006f, 0.009f, 0.014f, 1f));
            UiFactory.CreateScreenHeader(background, "Team Ratings", "Car overall is a weighted blend of speed, handling, and reliability stats.");

            RectTransform modeTabs = UiFactory.CreateRect(background, "Team ratings mode tabs", new Vector2(0.06f, 0.885f), new Vector2(0.34f, 0.935f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(modeTabs, 10, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateFilterTab(modeTabs, "Drivers", false, () => ShowDriverRatings(data, career, settings));
            UiFactory.CreateFilterTab(modeTabs, "Teams", true, () => { });

            // Part 6/7: always show this team's EFFECTIVE car (base + this
            // season's R&D, both player and AI) rather than the raw
            // drivers.json/carPerformance.json baseline - otherwise completed
            // AI development would never visibly change this screen.
            System.Func<TeamData, CarPerformanceData> effectiveCarFor = team =>
            {
                CarPerformanceData baseCar = data.FindCar(team.carPerformanceId);
                return career != null && career.Save != null ? career.GetEffectiveTeamCar(team, baseCar) : baseCar;
            };

            List<TeamData> teams = new List<TeamData>(data.Teams.teams);
            teams.Sort((a, b) =>
            {
                CarPerformanceData carA = effectiveCarFor(a);
                CarPerformanceData carB = effectiveCarFor(b);
                float overallA = carA == null ? 0f : ComputeCarOverall(carA);
                float overallB = carB == null ? 0f : ComputeCarOverall(carB);
                return overallB.CompareTo(overallA);
            });

            RectTransform grid = UiFactory.CreateScrollGridPanel(background, "Team card grid", new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.79f), new Vector2(TeamCardWidth, TeamCardHeight), new Vector2(16f, 16f), new RectOffset(10, 10, 10, 10));
            for (int i = 0; i < teams.Count; i++)
            {
                TeamData team = teams[i];
                CarPerformanceData car = effectiveCarFor(team);
                if (car == null)
                {
                    continue;
                }

                bool isPlayerTeam = career != null && career.Save != null && team.id == career.Save.playerTeamId;
                BuildTeamCard(grid, team, car, i + 1, isPlayerTeam, career);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        // Premium team/car card: team-colored header, reputation/reliability,
        // an overall badge, rank, archetype + strengths/weaknesses chips, and a
        // 10-stat bar block (same CreateStatBar/StatTierColor as driver cards).
        void BuildTeamCard(RectTransform parent, TeamData team, CarPerformanceData car, int rank, bool isPlayerTeam, CareerManager career = null)
        {
            Color primary = team.PrimaryUnityColor;
            Color borderColor = isPlayerTeam ? UiFactory.AccentGreen : primary;
            RectTransform card = UiFactory.CreateBorderedCard(parent, "Team card " + team.id, TeamCardWidth, TeamCardHeight, borderColor, isPlayerTeam);

            RectTransform stripe = UiFactory.CreateBand(card, "Team card stripe", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -6f), Vector2.zero, primary);
            stripe.GetComponent<Image>().raycastTarget = false;

            RectTransform rankBadge = UiFactory.CreateRect(card, "Team card rank", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rankBadge.sizeDelta = new Vector2(40f, 40f);
            Image rankImage = rankBadge.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(rankImage, rank == 1 ? new Color(1f, 0.8f, 0.2f, 0.9f) : new Color(0.1f, 0.13f, 0.17f, 0.9f));
            SetTopLeft(rankBadge, 16f, 22f);
            Text rankText = UiFactory.CreateText(rankBadge, "Rank value", "P" + rank, 16, rank == 1 ? new Color(0.06f, 0.06f, 0.07f) : Color.white, TextAnchor.MiddleCenter);
            rankText.fontStyle = FontStyle.Bold;
            StretchFull(rankText.GetComponent<RectTransform>());

            if (isPlayerTeam)
            {
                Text yourTeamBadge = UiFactory.CreateText(card, "Team card your team badge", "YOUR TEAM", 13, UiFactory.AccentGreen, TextAnchor.UpperLeft);
                yourTeamBadge.fontStyle = FontStyle.Bold;
                SetTopLeft(yourTeamBadge.rectTransform, 66f, 8f);
                UiFactory.SetSize(yourTeamBadge, 180f, 18f);
            }

            Text nameText = UiFactory.CreateText(card, "Team card name", team.name, 18, Color.white, TextAnchor.UpperLeft);
            nameText.fontStyle = FontStyle.Bold;
            SetTopLeft(nameText.rectTransform, 66f, 26f);
            UiFactory.SetSize(nameText, 190f, 24f);

            Text shortText = UiFactory.CreateText(card, "Team card short", team.shortName.ToUpperInvariant(), 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(shortText.rectTransform, 66f, 50f);
            UiFactory.SetSize(shortText, 190f, 18f);

            float overall = ComputeCarOverall(car);
            Color overallColor = StatTierColor(overall);
            RectTransform overallBadge = UiFactory.CreateRect(card, "Team card overall", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            overallBadge.sizeDelta = new Vector2(56f, 56f);
            Image overallImage = overallBadge.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(overallImage, new Color(overallColor.r, overallColor.g, overallColor.b, 0.2f));
            SetTopRight(overallBadge, 16f, 20f);
            Text overallText = UiFactory.CreateText(overallBadge, "Team card overall value", Mathf.RoundToInt(overall).ToString(), 22, overallColor, TextAnchor.MiddleCenter);
            overallText.fontStyle = FontStyle.Bold;
            StretchFull(overallText.GetComponent<RectTransform>());

            Text repText = UiFactory.CreateText(card, "Team card reputation", "REP " + team.reputation + "   ·   RELIABILITY " + team.reliability, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(repText.rectTransform, 16f, 78f);
            UiFactory.SetSize(repText, 310f, 18f);

            // Part 5/8: in-season R&D summary - season-start rating, the
            // in-season swing from completed upgrades, and how many have
            // completed so far, using the same TeamDevelopmentState every AI
            // team's development runs through.
            if (career != null && career.Save != null)
            {
                TeamDevelopmentState devState = career.GetOrCreateTeamDevelopmentState(team.id);
                int ratingChange = devState.currentRating - devState.ratingAtSeasonStart;
                string changeLabel = ratingChange > 0 ? "+" + ratingChange : ratingChange.ToString();
                Color devColor = ratingChange > 0 ? UiFactory.AccentGreen : (ratingChange < 0 ? UiFactory.Accent : UiFactory.TextMuted);
                Text devText = UiFactory.CreateText(card, "Team card development", "SEASON START " + devState.ratingAtSeasonStart + "   ·   " + changeLabel +
                    "   ·   " + devState.completedUpgradeIds.Count + " UPGRADE" + (devState.completedUpgradeIds.Count == 1 ? "" : "S"), 12, devColor, TextAnchor.UpperLeft);
                SetTopLeft(devText.rectTransform, 16f, 96f);
                UiFactory.SetSize(devText, 320f, 16f);
            }

            // Wrapping chip rows (card overflow fix): the old strengths/
            // weaknesses pills concatenated two full stat names into one
            // string ("+ Aero Efficiency / Chassis Balance") with no width
            // ceiling, which regularly stretched past the card edge and
            // clipped into the neighboring card in the grid. Short per-stat
            // labels plus the wrapping chip row keep every chip inside the
            // card no matter how the two longest/shortest stats land, and the
            // stat bar block below is positioned dynamically off however much
            // vertical space the archetype/strengths/weaknesses rows actually
            // used.
            const float sectionWidth = TeamCardWidth - 32f;
            float cursorY = career != null && career.Save != null ? 118f : 100f;

            string archetype = ComputeCarArchetype(car, overall);
            cursorY += UiFactory.CreateWrappingChipRow(card, "Team card archetype", new List<string> { archetype }, UiFactory.AccentPurple, 16f, cursorY, sectionWidth) + 14f;

            List<KeyValuePair<string, float>> stats = BuildCarStatList(car);
            List<KeyValuePair<string, float>> ranked = new List<KeyValuePair<string, float>>(stats);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

            Text strengthsLabel = UiFactory.CreateText(card, "Team card strengths label", "STRENGTHS", 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(strengthsLabel.rectTransform, 16f, cursorY);
            UiFactory.SetSize(strengthsLabel, sectionWidth, 16f);
            cursorY += 18f;
            List<string> strengthChips = new List<string> { ShortStatLabel(ranked[0].Key), ShortStatLabel(ranked[1].Key) };
            cursorY += UiFactory.CreateWrappingChipRow(card, "Team card strengths", strengthChips, UiFactory.AccentGreen, 16f, cursorY, sectionWidth) + 10f;

            Text weaknessesLabel = UiFactory.CreateText(card, "Team card weaknesses label", "WEAKNESSES", 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(weaknessesLabel.rectTransform, 16f, cursorY);
            UiFactory.SetSize(weaknessesLabel, sectionWidth, 16f);
            cursorY += 18f;
            List<string> weaknessChips = new List<string> { ShortStatLabel(ranked[ranked.Count - 1].Key), ShortStatLabel(ranked[ranked.Count - 2].Key) };
            cursorY += UiFactory.CreateWrappingChipRow(card, "Team card weaknesses", weaknessChips, UiFactory.Accent, 16f, cursorY, sectionWidth) + 14f;

            const float columnWidth = 145f;
            float startY = cursorY;
            // +3 vs the old 40: CreateStatBar's own row grew a few px to fit
            // its larger label/value font without clipping.
            const float rowHeight = 43f;
            for (int i = 0; i < stats.Count; i++)
            {
                int column = i / 5;
                int row = i % 5;
                RectTransform bar = UiFactory.CreateStatBar(card, stats[i].Key, stats[i].Value, 100f, StatTierColor(stats[i].Value), columnWidth);
                SetTopLeft(bar, 16f + column * (columnWidth + 18f), startY + row * rowHeight);
                if (stats[i].Key == "Top Speed")
                {
                    Transform valueTransform = bar.Find("Stat bar value");
                    Text valueText = valueTransform == null ? null : valueTransform.GetComponent<Text>();
                    if (valueText != null)
                    {
                        valueText.text = car.topSpeed + " KM/H";
                    }
                }
            }
        }

        public void ShowAssists(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Assists background", new Color(0.012f, 0.016f, 0.021f, 1f));
            BuildSettingsTabBar(background, data, career, settings, 1);
            RectTransform card = UiFactory.CreateCard(background, "Assists card", new Vector2(0.06f, 0.12f), new Vector2(0.6f, 0.76f));
            RectTransform panel = UiFactory.CreateScrollPanel(card, "Assists panel", new Vector2(0.035f, 0.03f), new Vector2(0.965f, 0.97f), 8, new RectOffset(20, 20, 16, 16));
            UiFactory.StretchListChildrenWidth(panel);
            UiFactory.CreateSubHeader(panel, "Driving Assists");

            RectTransform autoBrakeControl;
            UiFactory.CreateSettingRow(panel, "Auto Brake", "Brakes automatically for upcoming corners.", out autoBrakeControl);
            UiFactory.CreateToggleControl(autoBrakeControl, settings.Current.autoBrakeAssist, () =>
            {
                settings.Current.autoBrakeAssist = !settings.Current.autoBrakeAssist;
                settings.Save();
                ShowAssists(data, career, settings);
            });

            RectTransform absControl;
            UiFactory.CreateSettingRow(panel, "ABS", "Prevents wheel lockup under heavy braking.", out absControl);
            UiFactory.CreateToggleControl(absControl, settings.Current.absAssist, () =>
            {
                settings.Current.absAssist = !settings.Current.absAssist;
                settings.Save();
                ShowAssists(data, career, settings);
            });

            RectTransform tractionControl;
            UiFactory.CreateSettingRow(panel, "Traction Control", "Limits wheelspin under hard acceleration.", out tractionControl);
            UiFactory.CreateToggleControl(tractionControl, settings.Current.tractionControl, () =>
            {
                settings.Current.tractionControl = !settings.Current.tractionControl;
                settings.Save();
                ShowAssists(data, career, settings);
            });

            RectTransform racingLineControl;
            UiFactory.CreateSettingRow(panel, "Racing Line", "Shows the suggested line and braking references.", out racingLineControl);
            UiFactory.CreateToggleControl(racingLineControl, settings.Current.racingLineAssist, () =>
            {
                settings.Current.racingLineAssist = !settings.Current.racingLineAssist;
                settings.Save();
                ShowAssists(data, career, settings);
            });

            RectTransform steeringControl;
            UiFactory.CreateSettingRow(panel, "Steering Sensitivity", "", out steeringControl);
            UiFactory.CreateCycleControl(steeringControl, settings.Current.steeringSensitivity.ToString("0.00"), () =>
            {
                settings.Current.steeringSensitivity = CycleFloat(settings.Current.steeringSensitivity, 0.7f, 1.45f, 0.15f);
                settings.Save();
                ShowAssists(data, career, settings);
            });

            RectTransform throttleControl;
            UiFactory.CreateSettingRow(panel, "Throttle Sensitivity", "", out throttleControl);
            UiFactory.CreateCycleControl(throttleControl, settings.Current.throttleSensitivity.ToString("0.00"), () =>
            {
                settings.Current.throttleSensitivity = CycleFloat(settings.Current.throttleSensitivity, 0.7f, 1.45f, 0.15f);
                settings.Save();
                ShowAssists(data, career, settings);
            });

            RectTransform brakeControl;
            UiFactory.CreateSettingRow(panel, "Brake Sensitivity", "", out brakeControl);
            UiFactory.CreateCycleControl(brakeControl, settings.Current.brakeSensitivity.ToString("0.00"), () =>
            {
                settings.Current.brakeSensitivity = CycleFloat(settings.Current.brakeSensitivity, 0.7f, 1.45f, 0.15f);
                settings.Save();
                ShowAssists(data, career, settings);
            });
        }

        public void ShowRaceWeekend(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Weekend background", new Color(0.012f, 0.015f, 0.018f, 1f));
            CalendarEventData current = career.CurrentEvent();
            if (current == null)
            {
                current = data.Calendar.events.Count > 0 ? data.Calendar.events[0] : new CalendarEventData { displayName = "Prototype GP", round = 1, weatherProfile = "clear" };
            }
            TeamData team = data.FindTeam(career.Save.playerTeamId);
            bool hasQualifying = career.HasQualifyingForCurrentRound();
            string profile = current.weatherProfile.ToLower();
            UiFactory.CreateScreenHeader(background, current.displayName.ToUpper(),
                "Round " + career.Save.currentRound + "  ·  " + WeatherProfileText(profile) +
                "  ·  " + (team == null ? "Independent" : team.name));

            // Left: conditions briefing.
            float trackTemp = profile.Contains("hot") ? 44f : (profile.Contains("wet") ? 19f : (profile.Contains("cloud") ? 25f : 31f));
            string conditionsBody =
                "Track Temp   " + trackTemp.ToString("0") + "°C\n" +
                "Air Temp     " + (trackTemp - 7f).ToString("0") + "°C\n" +
                "Recommended  " + RecommendedTyreText(profile) + "\n\n" +
                (hasQualifying ? "Qualifying complete. Grid is set." : "Qualifying required before the race.");
            UiFactory.CreateInfoCard(background, "Weekend conditions", new Vector2(0.05f, 0.14f), new Vector2(0.34f, 0.76f), "Track Conditions", conditionsBody,
                hasQualifying ? UiFactory.AccentGreen : UiFactory.AccentAmber);

            // Middle: the current grid, or a locked state if qualifying hasn't
            // happened yet. Grid information must never be visible before
            // qualifying is actually driven or simulated - there is no
            // "provisional"/default grid fallback here, by design.
            RectTransform gridContentArea;
            UiFactory.CreateModernCard(background, hasQualifying ? "Starting Grid" : "Qualifying Not Run", hasQualifying ? UiFactory.AccentCyan : UiFactory.TextMuted, new Vector2(0.36f, 0.14f), new Vector2(0.64f, 0.76f), out gridContentArea);
            if (hasQualifying)
            {
                RectTransform gridRows = UiFactory.CreateScrollPanel(gridContentArea, "Grid rows", Vector2.zero, Vector2.one, 4, new RectOffset(6, 6, 6, 10));
                BuildQualifyingGridRows(data, gridRows, career.Save.lastQualifyingResults, 500f);
            }
            else
            {
                BuildLockedQualifyingState(gridContentArea);
            }

            // Right: session actions grouped by what they actually are, instead of
            // one long vertical stack of eight identical-looking buttons.
            RectTransform actions = UiFactory.CreateRect(background, "Weekend actions", new Vector2(0.66f, 0.14f), new Vector2(0.95f, 0.76f), Vector2.zero, Vector2.zero);
            VerticalLayoutGroup actionsLayout = UiFactory.AddVerticalLayout(actions, 20, new RectOffset(0, 0, 0, 0));
            // Centered instead of top-packed: this column always holds exactly
            // three groups now (Race/Qualifying merge into one below), so the
            // leftover vertical space is split evenly above and below rather
            // than collecting as one dead gap under the last card.
            actionsLayout.childAlignment = TextAnchor.MiddleLeft;

            // The Qualifying group is ALWAYS present - driving qualifying yourself
            // is a core session, so it must stay reachable even once a grid
            // already exists (re-running it overwrites the provisional result).
            // There is no separate "Race" action group here anymore: once a grid
            // is set, the actual forward action to start the race lives on the
            // Qualifying Results screen's own "Continue to Race" button
            // (ShowQualifyingResults), which is the natural point in the session
            // flow to move on. Keeping a second "Continue to Race" button here
            // was a redundant duplicate of that control. Removing the group
            // leaves three groups instead of four; the actions column's
            // VerticalLayoutGroup is centered (childAlignment MiddleLeft), so the
            // remaining groups simply re-center instead of leaving a gap.
            CreateWeekendActionGroup(actions, "Qualifying",
                hasQualifying ? "Grid is set. Re-run to replace the result." : "Drive the session yourself, or simulate it.",
                "Go to Qualifying", bootstrap.StartCareerQualifying, "Sim Qualifying", bootstrap.StartCareerSimQualifying);

            if (settings.Current.practiceProgramsEnabled)
            {
                CreateWeekendActionGroup(actions, "Practice", "Optional programs for resource points.",
                    "Practice Programs", () => ShowPracticePrograms(data, career, settings), null, null);
            }
            CreateWeekendActionGroup(actions, "Preparation", "Car setup and circuit notes.",
                "Car Setup", () => ShowCarSetup(data, career, settings, () => ShowRaceWeekend(data, career, settings)), "Track Info", bootstrap.ShowTrackInfo);

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
        }

        // One labeled cluster of related actions on the Race Weekend screen -
        // groups Practice / Qualifying / Race / Setup instead of one flat list.
        void CreateWeekendActionGroup(RectTransform parent, string title, string description, string primaryLabel, UnityEngine.Events.UnityAction primaryAction, string secondaryLabel, UnityEngine.Events.UnityAction secondaryAction)
        {
            RectTransform group = UiFactory.CreateRect(parent, title + " action group", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            group.sizeDelta = new Vector2(420f, 106f);
            Image background = group.gameObject.AddComponent<Image>();
            UiFactory.StyleRounded(background, UiFactory.PanelDark);
            RectTransform accent = UiFactory.CreateRect(group, title + " accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f));
            Image accentImage = accent.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(accentImage, UiFactory.AccentCyan);

            Text titleText = UiFactory.CreateText(group, title + " group title", title.ToUpperInvariant(), 15, UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(18f, -30f);
            titleRect.offsetMax = new Vector2(-14f, -10f);

            Text descriptionText = UiFactory.CreateText(group, title + " group description", description, 13, new Color(0.62f, 0.7f, 0.76f), TextAnchor.UpperLeft);
            RectTransform descriptionRect = descriptionText.GetComponent<RectTransform>();
            descriptionRect.anchorMin = new Vector2(0f, 1f);
            descriptionRect.anchorMax = new Vector2(1f, 1f);
            descriptionRect.offsetMin = new Vector2(18f, -52f);
            descriptionRect.offsetMax = new Vector2(-14f, -32f);

            RectTransform buttonRow = UiFactory.CreateRect(group, title + " buttons", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(18f, 12f), new Vector2(-14f, 46f));
            UiFactory.AddHorizontalLayout(buttonRow, 8, new RectOffset(0, 0, 0, 0));
            Button primaryButton = UiFactory.CreatePrimaryButton(buttonRow, primaryLabel, primaryAction);
            UiFactory.SetSize(primaryButton, string.IsNullOrEmpty(secondaryLabel) ? 384f : 186f, 44f);
            if (!string.IsNullOrEmpty(secondaryLabel))
            {
                Button secondaryButton = UiFactory.CreateSecondaryButton(buttonRow, secondaryLabel, secondaryAction);
                UiFactory.SetSize(secondaryButton, 186f, 44f);
            }
        }

        public void ShowQualifyingTyreSelect(GameDataRepository data, CareerManager career, GameSettingsStore settings, int phase)
        {
            ShowQualifyingTyreSelect(data, career, settings, phase, false);
        }

        public void ShowQualifyingTyreSelect(GameDataRepository data, CareerManager career, GameSettingsStore settings, int phase, bool simulate)
        {
            Clear();
            CalendarEventData current = career.CurrentEvent();
            if (current == null)
            {
                current = data.Calendar.events.Count > 0 ? data.Calendar.events[0] : new CalendarEventData { displayName = "Prototype GP", weatherProfile = "clear_hot" };
            }

            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Qualifying tyre background", new Color(0.012f, 0.015f, 0.018f, 1f));
            UiFactory.CreateScreenHeader(background, (simulate ? "Sim Qualifying" : "Q" + Mathf.Clamp(phase, 1, 3)) + " Pre-Session Briefing", current.displayName);

            // A real responsive two-column layout: the row uses
            // childControlWidth so it actually owns column sizing, and each
            // column carries a LayoutElement so neither one can collapse to
            // near-zero width (the old fixed-fraction anchors were silently
            // discarded by the HorizontalLayoutGroup, which is what made the
            // tyre guidance text wrap to a single character per line).
            RectTransform main = UiFactory.CreateRect(background, "Qualifying main layout", new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.86f), Vector2.zero, Vector2.zero);
            UiFactory.AddResponsiveHorizontalLayout(main, 24, new RectOffset(0, 0, 0, 0));

            RectTransform left = UiFactory.CreateGlassPanel(main, "Weather forecast", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiFactory.PanelDark);
            UiFactory.SetFixedColumnWidth(left, 440f);
            RectTransform leftList = UiFactory.CreateRect(left, "Weather list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(leftList, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(leftList, "Track Conditions");

            string profile = current.weatherProfile.ToLower();
            string condition = WeatherProfileText(profile);
            float trackTemp = profile.Contains("hot") ? 42f : (profile.Contains("wet") ? 18f : (profile.Contains("cloud") ? 26f : 32f));
            float airTemp = trackTemp - 8f;

            Text weatherText = UiFactory.CreateText(leftList, "Current weather", "Condition   " + condition + "\nTrack Temp  " + trackTemp.ToString("0") + "°C\nAir Temp    " + airTemp.ToString("0") + "°C\nHumidity    " + (profile.Contains("wet") ? "88%" : "42%") + "\nDRS Zones   2", 18, Color.white, TextAnchor.UpperLeft);
            UiFactory.SetSize(weatherText, 380f, 120f);
            UiFactory.CreateDivider(leftList);
            UiFactory.CreateSubHeader(leftList, "Session Forecast");
            string forecast = profile.Contains("mixed") ? "Expect variable rain intensity throughout the session." : (profile.Contains("wet") ? "Steady rain expected to continue." : "Dry track expected for the duration.");
            Text forecastText = UiFactory.CreateText(leftList, "Forecast text", forecast, 16, new Color(0.82f, 0.9f, 0.94f), TextAnchor.UpperLeft);
            forecastText.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(forecastText, 380f, 60f);
            UiFactory.CreateDivider(leftList);
            UiFactory.CreateSubHeader(leftList, "Session Objective");
            Text objectiveText = UiFactory.CreateText(leftList, "Qualifying objective", simulate ? "Bank a competitive grid slot for the race." : "Set the fastest clean lap you can - track position starts here.", 15, new Color(0.82f, 0.9f, 0.94f), TextAnchor.UpperLeft);
            objectiveText.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(objectiveText, 380f, 44f);

            RectTransform right = UiFactory.CreateGlassPanel(main, "Tyre Selection", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiFactory.PanelDark);
            UiFactory.SetFlexibleColumnWidth(right, 560f);
            RectTransform rightList = UiFactory.CreateRect(right, "Tyre list", Vector2.zero, Vector2.one, new Vector2(28f, 20f), new Vector2(-28f, -20f));
            UiFactory.AddVerticalLayout(rightList, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(rightList, "Tyre Selection");

            string currentCompound = settings.Current.tyreCompound;
            bool currentMismatch = IsTyreMismatch(currentCompound, profile);
            if (currentMismatch)
            {
                Text warning = UiFactory.CreateText(rightList, "Tyre mismatch warning", "Warning: " + currentCompound + " is a poor choice for " + condition.ToLowerInvariant() + " conditions.", 15, UiFactory.AccentAmber, TextAnchor.UpperLeft);
                UiFactory.SetSize(warning, 520f, 24f);
            }

            CreateTyreCompoundGrid(rightList, currentCompound, profile, true, tyreName =>
            {
                settings.Current.tyreCompound = tyreName;
                settings.Save();
                ShowQualifyingTyreSelect(data, career, settings, phase, simulate);
            });

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Back to Weekend", bootstrap.ShowRaceWeekend);
            UiFactory.CreatePrimaryButton(footerRight, simulate ? "Execute Simulation" : "Start Session", () =>
            {
                if (simulate)
                {
                    bootstrap.SimulateCareerQualifying();
                }
                else
                {
                    bootstrap.BeginCareerQualifying();
                }
            });
        }

        public void ShowRaceTyreSelect(GameDataRepository data, CareerManager career, GameSettingsStore settings, bool careerRace)
        {
            Clear();
            // Quick Race now picks its track on ShowQuickRaceTrackSelect first;
            // quickRaceSelectedEvent carries that choice here instead of this
            // always defaulting to the calendar's first event. Falls back to the
            // old default if this screen is somehow reached without going through
            // track select (e.g. a saved shortcut), so it never crashes.
            CalendarEventData current = careerRace
                ? career.CurrentEvent()
                : (quickRaceSelectedEvent != null ? quickRaceSelectedEvent : (data.Calendar.events.Count > 0 ? data.Calendar.events[0] : career.CurrentEvent()));
            if (current == null)
            {
                current = new CalendarEventData { displayName = "Prototype GP", weatherProfile = "clear" };
            }

            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Race tyre background", new Color(0.012f, 0.015f, 0.018f, 1f));
            UiFactory.CreateScreenHeader(background, "Race Tyre Selection", current.displayName);

            // Body sections are stacked with a real VerticalLayoutGroup instead of
            // fixed-size panels crammed together - the briefing card, tyre grid and
            // pit strategy row each get guaranteed space instead of overlapping
            // once any one of them needed more room than a hand-picked pixel size.
            RectTransform body = UiFactory.CreateScreenBody(background, 108f, 78f);
            RectTransform bodyMargin = UiFactory.CreateRect(body, "Race tyre body margin", Vector2.zero, Vector2.one, new Vector2(48f, 24f), new Vector2(-48f, -24f));
            VerticalLayoutGroup bodyLayout = UiFactory.AddVerticalLayout(bodyMargin, 20, new RectOffset(0, 0, 0, 0));
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = false;

            string profile = current.weatherProfile == null ? "" : current.weatherProfile.ToLowerInvariant();
            string currentCompound = settings.Current.tyreCompound;

            // Top row: briefing (left) + tyre selection (right).
            RectTransform topRow = UiFactory.CreateRect(bodyMargin, "Race tyre top row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.SetFlexibleRowHeight(topRow, 320f);
            UiFactory.AddResponsiveHorizontalLayout(topRow, 20, new RectOffset(0, 0, 0, 0));

            RectTransform left = UiFactory.CreateGlassPanel(topRow, "Race briefing card", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiFactory.PanelDark);
            UiFactory.SetFixedColumnWidth(left, 420f);
            RectTransform leftList = UiFactory.CreateRect(left, "Race briefing list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(leftList, 10, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(leftList, "Race Briefing");
            string raceObjectiveText = !careerRace
                ? "Post a strong, clean result."
                : (career.Save != null && !string.IsNullOrEmpty(career.Save.rivalDriverId) ? "Beat your rival and score points." : "Score points and keep it clean.");
            Text briefing = UiFactory.CreateText(leftList, "Race weather briefing", BuildWeatherBriefing(current, "Race", currentCompound, raceObjectiveText), 16, new Color(0.84f, 0.91f, 0.95f), TextAnchor.UpperLeft);
            briefing.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(briefing, 372f, 280f);

            RectTransform right = UiFactory.CreateGlassPanel(topRow, "Race tyre selection card", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiFactory.PanelDark);
            UiFactory.SetFlexibleColumnWidth(right, 560f);
            RectTransform rightList = UiFactory.CreateRect(right, "Race tyre list", Vector2.zero, Vector2.one, new Vector2(28f, 20f), new Vector2(-28f, -20f));
            UiFactory.AddVerticalLayout(rightList, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(rightList, "Tyre Selection");

            bool currentMismatch = IsTyreMismatch(currentCompound, profile);
            if (currentMismatch)
            {
                Text warning = UiFactory.CreateText(rightList, "Race tyre mismatch warning", "Warning: " + currentCompound + " is a poor choice for " + WeatherProfileText(profile).ToLowerInvariant() + " conditions.", 15, UiFactory.AccentAmber, TextAnchor.UpperLeft);
                UiFactory.SetSize(warning, 500f, 24f);
            }

            CreateTyreCompoundGrid(rightList, currentCompound, profile, false, tyreName =>
            {
                settings.Current.tyreCompound = tyreName;
                settings.Save();
                ShowRaceTyreSelect(data, career, settings, careerRace);
            });

            // Bottom row: pit strategy plan, shown on the HUD pit card and used
            // for the engineer's box calls during the race. Supports both a
            // 1-stop and a 2-stop plan; the "Strategy" row toggles whether the
            // stop-2 rows exist at all (they're omitted, not just disabled, to
            // keep a 1-stop plan compact). The card height below is a reasonable
            // fixed default rather than a worst-case-safe one - the list is
            // scroll-wrapped so the 2-stop case (5 description rows at their
            // current height) simply scrolls instead of clipping.
            RectTransform pitCard = UiFactory.CreateGlassPanel(bodyMargin, "Pit strategy card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, UiFactory.PanelDarker);
            UiFactory.SetFixedRowHeight(pitCard, 452f);
            RectTransform pitList = UiFactory.CreateScrollPanel(pitCard, "Pit strategy list", new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.96f), 6, new RectOffset(20, 20, 12, 12));
            UiFactory.StretchListChildrenWidth(pitList);
            UiFactory.CreateSubHeader(pitList, "Pit Strategy");

            int raceLaps = Mathf.Max(3, settings.Current.laps);
            int stopCount = Mathf.Clamp(settings.Current.plannedStopCount, 1, 2);

            RectTransform strategyControl;
            UiFactory.CreateSettingRow(pitList, "Strategy", "One mandatory stop, or two for extra tyre flexibility.", out strategyControl);
            UiFactory.CreateCycleControl(strategyControl, stopCount == 1 ? "1-Stop" : "2-Stop", () =>
            {
                settings.Current.plannedStopCount = stopCount == 1 ? 2 : 1;
                settings.Save();
                ShowRaceTyreSelect(data, career, settings, careerRace);
            });

            int stop1Lap = settings.Current.plannedPitLapOne;
            string stop1LapLabel = stop1Lap <= 0 ? "Auto" : "Lap " + stop1Lap;
            RectTransform stop1LapControl;
            UiFactory.CreateSettingRow(pitList, "Stop 1 Lap", "Leave on Auto to let the engineer call the window.", out stop1LapControl);
            UiFactory.CreateCycleControl(stop1LapControl, stop1LapLabel, () =>
            {
                settings.Current.plannedPitLapOne = settings.Current.plannedPitLapOne >= raceLaps - 1 ? 0 : settings.Current.plannedPitLapOne + 1;
                settings.Save();
                ShowRaceTyreSelect(data, career, settings, careerRace);
            });

            // Fuel system pass: fuel-load choice lives right alongside pit/tyre
            // strategy, same CreateSettingRow/CreateCycleControl pattern the other
            // rows in this card already use, and stores straight into
            // settings.Current.fuelLoadChoice (GameSettingsData, default Target).
            FuelLoadChoice fuelChoice = (FuelLoadChoice)settings.Current.fuelLoadChoice;
            RectTransform fuelControl;
            UiFactory.CreateSettingRow(pitList, "Fuel Load", FuelLoadChoiceDescription(fuelChoice), out fuelControl);
            UiFactory.CreateCycleControl(fuelControl, FuelLoadChoiceLabel(fuelChoice), () =>
            {
                settings.Current.fuelLoadChoice = (int)NextFuelLoadChoice(fuelChoice);
                settings.Save();
                ShowRaceTyreSelect(data, career, settings, careerRace);
            });

            RectTransform stop1TyreControl;
            UiFactory.CreateSettingRow(pitList, "Stop 1 Tyre", "Tyre fitted at the first stop.", out stop1TyreControl);
            UiFactory.CreateCycleControl(stop1TyreControl, settings.Current.plannedStopOneCompound, () =>
            {
                settings.Current.plannedStopOneCompound = NextTyreName(settings.Current.plannedStopOneCompound);
                settings.Save();
                ShowRaceTyreSelect(data, career, settings, careerRace);
            });

            // A 2-stop's second lap can never land at or before the first stop.
            // If the player tightened stop 1 after already picking a stop-2 lap,
            // silently drop the now-invalid stop-2 lap back to Auto rather than
            // letting the plan show a nonsensical order.
            int stop1LapLowerBound = stop1Lap > 0 ? stop1Lap : 0;
            if (stopCount == 2 && settings.Current.plannedPitLapTwo > 0 && stop1LapLowerBound > 0 &&
                settings.Current.plannedPitLapTwo <= stop1LapLowerBound)
            {
                settings.Current.plannedPitLapTwo = 0;
                settings.Save();
            }

            if (stopCount == 2)
            {
                int stop2Lap = settings.Current.plannedPitLapTwo;
                string stop2LapLabel = stop2Lap <= 0 ? "Auto" : "Lap " + stop2Lap;
                RectTransform stop2LapControl;
                UiFactory.CreateSettingRow(pitList, "Stop 2 Lap", "Leave on Auto to let the engineer call the window.", out stop2LapControl);
                UiFactory.CreateCycleControl(stop2LapControl, stop2LapLabel, () =>
                {
                    int next = settings.Current.plannedPitLapTwo;
                    do
                    {
                        next = next >= raceLaps - 1 ? 0 : next + 1;
                    }
                    while (next != 0 && stop1LapLowerBound > 0 && next <= stop1LapLowerBound);
                    settings.Current.plannedPitLapTwo = next;
                    settings.Save();
                    ShowRaceTyreSelect(data, career, settings, careerRace);
                });

                RectTransform stop2TyreControl;
                UiFactory.CreateSettingRow(pitList, "Stop 2 Tyre", "Tyre fitted at the second stop.", out stop2TyreControl);
                UiFactory.CreateCycleControl(stop2TyreControl, settings.Current.plannedStopTwoCompound, () =>
                {
                    settings.Current.plannedStopTwoCompound = NextTyreName(settings.Current.plannedStopTwoCompound);
                    settings.Save();
                    ShowRaceTyreSelect(data, career, settings, careerRace);
                });
            }

            // Resolved-plan preview, e.g. "Start Medium -> Lap 5 Hard" or, for a
            // 2-stop, "Start Soft -> Lap 4 Medium -> Lap 8 Hard". This screen has
            // no RaceManager instance yet (the race hasn't started), so an Auto
            // stop cannot be resolved through the same RecommendedPitLap/
            // GetPlannedPitLapForStop path the race itself uses - that resolver
            // reads live race-only state (weather, the assigned driver's tyre
            // management stat, per-driver jitter). A rough local re-estimate
            // here previously could show a different lap than the one the
            // engineer actually calls during the race. Rather than risk that
            // disagreement, an Auto stop is simply labelled "Auto" - the exact
            // lap is decided, once, by the real resolver when the race starts.
            string stop1LapText = stop1Lap > 0 ? ("Lap " + stop1Lap) : "Auto";
            string summaryLine = "Start " + settings.Current.tyreCompound + " → " + stop1LapText + " " + settings.Current.plannedStopOneCompound;
            if (stopCount == 2)
            {
                int stop2LapValue = settings.Current.plannedPitLapTwo;
                string stop2LapText = stop2LapValue > 0 ? ("Lap " + stop2LapValue) : "Auto";
                summaryLine += " → " + stop2LapText + " " + settings.Current.plannedStopTwoCompound;
            }

            Text summary = UiFactory.CreateText(pitList, "Strategy summary", summaryLine, 15, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            summary.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(summary, 900f, 26f);

            string recommendation;
            if (profileIsWet(current))
            {
                recommendation = "Wet weather: Intermediates or Wets are the safe call.";
            }
            else if (raceLaps <= 8)
            {
                recommendation = "Short race: a one-stop is usually enough.";
            }
            else
            {
                recommendation = "Longer race: a two-stop can undercut rivals on fresher tyres.";
            }

            Text recommendationText = UiFactory.CreateText(pitList, "Strategy recommendation", recommendation, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            recommendationText.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(recommendationText, 900f, 24f);

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, careerRace ? "Back to Weekend" : "Back to Track Select", () =>
            {
                if (careerRace)
                {
                    bootstrap.ShowRaceWeekend();
                }
                else
                {
                    ShowQuickRaceTrackSelect(data, career, settings);
                }
            });
            UiFactory.CreateSecondaryButton(footerLeft, "Car Setup", () => ShowCarSetup(data, career, settings, () => ShowRaceTyreSelect(data, career, settings, careerRace)));
            UiFactory.CreatePrimaryButton(footerRight, "Start Race", () =>
            {
                if (careerRace)
                {
                    bootstrap.BeginCareerRace();
                }
                else
                {
                    bootstrap.BeginQuickRace();
                }
            });
        }

        static readonly string[] TyreCompoundOrder = { "Soft", "Medium", "Hard", "Intermediate", "Wet" };

        string TyreShortDescriptor(string tyreName)
        {
            if (tyreName == "Soft") return "Peak grip, fastest wear";
            if (tyreName == "Medium") return "Balanced grip and life";
            if (tyreName == "Hard") return "Durable, lower grip";
            if (tyreName == "Intermediate") return "Damp track, light rain";
            if (tyreName == "Wet") return "Heavy rain, max clearance";
            return "";
        }

        bool IsTyreRecommended(string tyreName, string profile, bool qualifying)
        {
            string normalized = string.IsNullOrEmpty(profile) ? "" : profile.ToLowerInvariant();
            if (normalized.Contains("wet"))
            {
                return tyreName == "Wet";
            }

            if (normalized.Contains("mixed"))
            {
                return tyreName == "Intermediate";
            }

            if (normalized.Contains("hot"))
            {
                return qualifying ? tyreName == "Soft" : tyreName == "Hard";
            }

            return qualifying ? tyreName == "Soft" : tyreName == "Medium";
        }

        // A clear mismatch: slicks on a wet/mixed session, or wet-weather tyres
        // on a session with no rain in the forecast at all.
        bool IsTyreMismatch(string tyreName, string profile)
        {
            string normalized = string.IsNullOrEmpty(profile) ? "" : profile.ToLowerInvariant();
            bool rainy = normalized.Contains("wet") || normalized.Contains("mixed");
            bool slick = tyreName == "Soft" || tyreName == "Medium" || tyreName == "Hard";
            bool wetWeatherTyre = tyreName == "Intermediate" || tyreName == "Wet";
            return rainy ? slick : (wetWeatherTyre && !rainy);
        }

        // Compact compound card: color dot, name, one-line descriptor, a
        // recommended tag for the current conditions, and a clear selected
        // highlight. Replaces plain "Soft"/"Medium" buttons everywhere tyres
        // are chosen. Laid out in a GridLayoutGroup by the caller so five cards
        // never depend on fragile manual width math.
        void CreateTyreCompoundCard(RectTransform parent, string tyreName, string selectedCompound, string weatherProfile, bool qualifying, UnityEngine.Events.UnityAction onSelect)
        {
            bool selected = selectedCompound == tyreName;
            bool recommended = IsTyreRecommended(tyreName, weatherProfile, qualifying);
            bool mismatch = selected && IsTyreMismatch(tyreName, weatherProfile);

            RectTransform card = UiFactory.CreateRect(parent, tyreName + " compound card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            Image cardImage = card.gameObject.AddComponent<Image>();
            UiFactory.StyleRounded(cardImage, selected ? new Color(0.6f, 0.06f, 0.05f, 0.98f) : UiFactory.PanelDark);

            RectTransform accent = UiFactory.CreateRect(card, tyreName + " card accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            accent.sizeDelta = new Vector2(3f, 0f);
            Image accentImage = accent.gameObject.AddComponent<Image>();
            accentImage.color = TyreDotColor(tyreName);

            Image dot = UiFactory.CreateIconDot(card, tyreName + " dot", 16f, TyreDotColor(tyreName));
            RectTransform dotRect = dot.rectTransform;
            dotRect.anchorMin = new Vector2(0f, 1f);
            dotRect.anchorMax = new Vector2(0f, 1f);
            dotRect.pivot = new Vector2(0f, 1f);
            dotRect.anchoredPosition = new Vector2(14f, -14f);

            Text nameText = UiFactory.CreateText(card, tyreName + " name", tyreName.ToUpperInvariant(), 16, selected ? Color.white : UiFactory.TextPrimary, TextAnchor.UpperLeft);
            RectTransform nameRect = nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(36f, -30f);
            nameRect.offsetMax = new Vector2(-8f, -10f);

            Text descriptorText = UiFactory.CreateText(card, tyreName + " descriptor", TyreShortDescriptor(tyreName), 13, selected ? new Color(1f, 0.88f, 0.86f) : UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform descriptorRect = descriptorText.GetComponent<RectTransform>();
            descriptorRect.anchorMin = new Vector2(0f, 1f);
            descriptorRect.anchorMax = new Vector2(1f, 1f);
            descriptorRect.offsetMin = new Vector2(14f, -68f);
            descriptorRect.offsetMax = new Vector2(-10f, -34f);
            descriptorText.verticalOverflow = VerticalWrapMode.Overflow;

            if (recommended)
            {
                PositionTyreCardTag(UiFactory.CreatePillLabel(card, "Best", UiFactory.AccentGreen));
            }
            else if (mismatch)
            {
                PositionTyreCardTag(UiFactory.CreatePillLabel(card, "Risky", UiFactory.Accent));
            }

            Button button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = cardImage;
            ColorBlock colors = button.colors;
            colors.normalColor = cardImage.color;
            colors.highlightedColor = selected ? new Color(0.85f, 0.1f, 0.08f, 1f) : new Color(0.1f, 0.16f, 0.22f, 1f);
            colors.pressedColor = new Color(colors.normalColor.r * 0.6f, colors.normalColor.g * 0.6f, colors.normalColor.b * 0.6f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                SimpleAudioManager.PlayClick();
                onSelect();
            });
        }

        void PositionTyreCardTag(Text tag)
        {
            RectTransform tagRect = (RectTransform)tag.transform.parent;
            tagRect.anchorMin = new Vector2(0f, 0f);
            tagRect.anchorMax = new Vector2(0f, 0f);
            tagRect.pivot = new Vector2(0f, 0f);
            tagRect.anchoredPosition = new Vector2(12f, 10f);
        }

        // 3+2 grid of tyre compound cards - always sized by a GridLayoutGroup
        // (which fully owns child size/position) rather than a horizontal stack
        // of fixed-width buttons that can overflow its container.
        void CreateTyreCompoundGrid(RectTransform parent, string selectedCompound, string weatherProfile, bool qualifying, System.Action<string> onSelect)
        {
            RectTransform grid = UiFactory.CreateRect(parent, "Tyre compound grid", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            grid.sizeDelta = new Vector2(3f * 168f + 2f * 10f, 2f * 96f + 10f);
            GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(168f, 96f);
            layout.spacing = new Vector2(10f, 10f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            for (int i = 0; i < TyreCompoundOrder.Length; i++)
            {
                string tyreName = TyreCompoundOrder[i];
                CreateTyreCompoundCard(grid, tyreName, selectedCompound, weatherProfile, qualifying, () => onSelect(tyreName));
            }
        }

        // Quick Race track picker: the flow used to jump straight into tyre
        // select using a hardcoded data.Calendar.events[0], so it always raced
        // the same track no matter what the player wanted. This is now the
        // actual first step (Track -> Preview -> Race Setup -> Start Race);
        // picking a row stores the event on quickRaceSelectedEvent and
        // continues into the existing tyre-select/setup screen, which reads
        // that field instead of defaulting. Each row now also shows a track
        // preview widget (see BuildTrackPreview) - real per-track geometry
        // isn't available here since TrackManager only builds it inside an
        // actual loaded race scene, so it's a seeded procedural approximation
        // rather than a literal trace of the circuit; the traits line (reused
        // from ShowTrackInfo) still carries the "what kind of circuit is
        // this" read as clean stat text alongside it.
        public void ShowQuickRaceTrackSelect(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Quick race track background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "Quick Race - Select Track", "Pick a circuit to race. Conditions, layout notes and your best lap are shown per track.");

            RectTransform content = UiFactory.CreateScrollPanel(background, "Quick race track list", new Vector2(0.05f, 0.14f), new Vector2(0.68f, 0.86f), 10, new RectOffset(18, 18, 14, 14));
            for (int i = 0; i < data.Calendar.events.Count; i++)
            {
                BuildQuickRaceTrackRow(content, data.Calendar.events[i], data, career, settings);
            }

            // Right rail: the one setting that isn't per-track (race length is a
            // global session setting - see ShowSettings' own "Race Laps" row) plus
            // a reminder that weather/conditions come from the track itself.
            RectTransform rail = UiFactory.CreateGlassPanel(background, "Quick race setup rail", new Vector2(0.70f, 0.14f), new Vector2(0.95f, 0.86f), Vector2.zero, Vector2.zero, UiFactory.PanelDark);
            RectTransform railList = UiFactory.CreateRect(rail, "Quick race setup rail list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(railList, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.StretchListChildrenWidth(railList);

            UiFactory.CreateSubHeader(railList, "Race Setup");
            RectTransform lapsControl;
            // Configurable race length: cycles a PRESET (settings.raceLengthPreset,
            // an existing but previously-unused field), not a raw lap count -
            // no specific track is chosen yet on this screen, so 25%/50%/Full only
            // resolve to an actual lap count once a track is picked (see
            // SelectQuickRaceTrack, which reads this same preset against that
            // track's own laps3/laps5/laps25Percent).
            UiFactory.CreateSettingRow(railList, "Race Length", "Preset applies once you pick a track below - percent presets scale to that circuit's own distance.", out lapsControl);
            UiFactory.CreateCycleControl(lapsControl, RaceLengthPresetLabel(settings.Current.raceLengthPreset), () =>
            {
                settings.Current.raceLengthPreset = (settings.Current.raceLengthPreset + 1) % RaceLengthPresetCount;
                settings.Save();
                ShowQuickRaceTrackSelect(data, career, settings);
            });

            Text difficultyLine = UiFactory.CreateText(railList, "Quick race difficulty", "Difficulty: " + settings.Difficulty + "   ·   " + settings.Current.aiOpponentCount + " AI opponents", 14, UiFactory.TextMuted, TextAnchor.UpperLeft);
            difficultyLine.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(difficultyLine, 380f, 40f);

            UiFactory.CreateDivider(railList);
            Text hint = UiFactory.CreateText(railList, "Quick race hint",
                "Weather and track conditions are set per circuit - pick a track on the left to see its forecast and layout notes, then move into car setup and tyre choice.",
                13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(hint, 380f, 100f);

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => bootstrap.ShowMainMenu());
        }

        // One selectable track row: name/round, country/weather/typical lap
        // count, the same heuristic layout-character traits ShowTrackInfo shows,
        // and the player's best recorded lap there if any.
        void BuildQuickRaceTrackRow(RectTransform parent, CalendarEventData raceEvent, GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            // Row grew (was 92 tall) to fit the new track preview widget - see
            // BuildTrackPreview. This is the very first screen of Quick Race,
            // so it's the natural place for the "clean track preview" the
            // selection flow was missing before picking a track and moving on
            // into tyre/strategy setup.
            RectTransform card = UiFactory.CreateRect(parent, raceEvent.trackId + " quick race card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(1180f, 132f);
            Image background = card.gameObject.AddComponent<Image>();
            background.color = UiFactory.PanelDark;
            UiFactory.CreateBand(card, "Quick race card rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.Accent);

            Text titleText = UiFactory.CreateText(card, "Quick race card title", "R" + raceEvent.round.ToString("00") + "  " + raceEvent.displayName, 19, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            SetTopLeft(titleText.rectTransform, 16f, 14f);
            UiFactory.SetSize(titleText, 380f, 26f);

            Text metaText = UiFactory.CreateText(card, "Quick race card meta",
                raceEvent.country + "   ·   " + WeatherProfileText(raceEvent.weatherProfile).ToUpperInvariant() + "   ·   " + raceEvent.laps25Percent + " LAPS TYPICAL",
                14, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(metaText.rectTransform, 16f, 46f);
            UiFactory.SetSize(metaText, 380f, 20f);

            float best = PlayerRecordsStore.GetBestLap(raceEvent.trackId);
            Text bestText = UiFactory.CreateText(card, "Quick race card best", best > 0f ? "BEST " + UiFactory.FormatTime(best) : "NO RECORD", 13, best > 0f ? UiFactory.AccentPurple : UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(bestText.rectTransform, 16f, 74f);
            UiFactory.SetSize(bestText, 300f, 18f);

            // Track preview: see BuildTrackPreview for what it draws and why.
            RectTransform preview = BuildTrackPreview(card, raceEvent, 100f);
            preview.anchorMin = new Vector2(0f, 0.5f);
            preview.anchorMax = new Vector2(0f, 0.5f);
            preview.pivot = new Vector2(0f, 0.5f);
            preview.anchoredPosition = new Vector2(420f, 0f);

            Text traitsText = UiFactory.CreateText(card, "Quick race card traits", TrackTraits(raceEvent), 13, UiFactory.AccentCyan, TextAnchor.MiddleLeft);
            traitsText.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform traitsRect = traitsText.GetComponent<RectTransform>();
            traitsRect.anchorMin = new Vector2(0f, 0.5f);
            traitsRect.anchorMax = new Vector2(0f, 0.5f);
            traitsRect.pivot = new Vector2(0f, 0.5f);
            traitsRect.anchoredPosition = new Vector2(680f, 0f);
            UiFactory.SetSize(traitsText, 226f, 100f);

            Button select = UiFactory.CreatePrimaryButton(card, "Select", () => SelectQuickRaceTrack(raceEvent, data, career, settings));
            UiFactory.SetSize(select, 130f, 44f);
            RectTransform selectRect = select.GetComponent<RectTransform>();
            selectRect.anchorMin = new Vector2(1f, 0.5f);
            selectRect.anchorMax = new Vector2(1f, 0.5f);
            selectRect.pivot = new Vector2(1f, 0.5f);
            selectRect.anchoredPosition = new Vector2(-16f, 0f);
        }

        void SelectQuickRaceTrack(CalendarEventData raceEvent, GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            quickRaceSelectedEvent = raceEvent;
            // Configurable race length: resolve the preset picked in the setup
            // rail against THIS track's own lap data now that it's known, and
            // write the result into the plain settings.laps field RaceManager
            // already reads (RaceManager.RaceLaps) - keeps every downstream
            // consumer (mandatory pit rule, strategy planner, HUD lap counter)
            // unchanged, since they only ever see a normal lap count.
            settings.Current.laps = ResolveRaceLengthLaps(raceEvent, settings.Current.raceLengthPreset);
            settings.Save();
            ShowRaceTyreSelect(data, career, settings, false);
        }

        // Configurable race length presets (#99): 3/5 lap presets prefer the
        // track's own tuned laps3/laps5 values over a flat literal, since those
        // already account for that circuit's own lap length; 10 laps is a flat
        // literal per the requested preset list; 25/50/Full derive from the
        // track's existing laps25Percent (50% = double, Full = quadruple - the
        // only distances derivable from a single quarter-distance data point).
        const int RaceLengthPresetCount = 6;

        string RaceLengthPresetLabel(int preset)
        {
            switch (((preset % RaceLengthPresetCount) + RaceLengthPresetCount) % RaceLengthPresetCount)
            {
                case 0: return "3 Laps";
                case 1: return "5 Laps";
                case 2: return "10 Laps";
                case 3: return "25% Race";
                case 4: return "50% Race";
                default: return "Full Race";
            }
        }

        int ResolveRaceLengthLaps(CalendarEventData raceEvent, int preset)
        {
            int quarter = raceEvent != null && raceEvent.laps25Percent > 0 ? raceEvent.laps25Percent : 12;
            switch (((preset % RaceLengthPresetCount) + RaceLengthPresetCount) % RaceLengthPresetCount)
            {
                case 0: return Mathf.Max(3, raceEvent != null && raceEvent.laps3 > 0 ? raceEvent.laps3 : 3);
                case 1: return Mathf.Max(3, raceEvent != null && raceEvent.laps5 > 0 ? raceEvent.laps5 : 5);
                case 2: return Mathf.Max(3, 10);
                case 3: return Mathf.Max(3, quarter);
                case 4: return Mathf.Max(3, quarter * 2);
                default: return Mathf.Max(3, quarter * 4);
            }
        }

        // Redundancy audit: this used to be two near-identical screens
        // (ShowTimeTrialSetup and ShowTrackInfo) that both listed every
        // circuit, both showed the player's best lap, and both ultimately did
        // the same thing - start a Time Trial. ShowTrackInfo's whole row was
        // also a giant click target that silently launched a session, which is
        // actively dangerous now that this screen is reachable mid-career (from
        // the Race Weekend and Career Hub screens) - a player just checking
        // conditions before qualifying could accidentally abandon their
        // session. Merged into one screen: browse every track's info, then an
        // explicit "Time Trial" button per row (matching the explicit-Select
        // convention already used on ShowQuickRaceTrackSelect) to launch one.
        // GameBootstrap.ShowTimeTrialSetup() now forwards here too, so both
        // entry points land on the same, safer screen.
        public void ShowTrackInfo(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Track info background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "Track Info",
                "Every circuit on the calendar, with layout traits and your best lap. Time Trial tyre: " + settings.Current.tyreCompound + " (change in Settings).");

            RectTransform content = UiFactory.CreateScrollPanel(background, "Track info list", new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.86f), 6, new RectOffset(18, 18, 14, 14));
            for (int i = 0; i < data.Calendar.events.Count; i++)
            {
                CalendarEventData raceEvent = data.Calendar.events[i];
                CreateTrackInfoRow(content, raceEvent);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Car Setup", () => ShowCarSetup(data, career, settings, () => ShowTrackInfo(data, career, settings)));
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        // Garage setup screen: five simple 1..5 controls with a live trade-off
        // summary. Values persist in settings and apply to the player car only.
        public void ShowCarSetup(GameDataRepository data, CareerManager career, GameSettingsStore settings, UnityEngine.Events.UnityAction backAction)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Car setup background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "Car Setup", "Applies to your car in every session. AI cars run neutral setups.");

            UnityEngine.Events.UnityAction refresh = () => ShowCarSetup(data, career, settings, backAction);

            RectTransform left = UiFactory.CreateCard(background, "Setup card", new Vector2(0.06f, 0.14f), new Vector2(0.52f, 0.82f));
            // Scroll-wrapped: six description rows at their current (taller, wrap
            // safe) height plus the preset row and header can exceed this card's
            // fixed height, so the list scrolls instead of clipping the bottom rows.
            RectTransform list = UiFactory.CreateScrollPanel(left, "Setup list", new Vector2(0.035f, 0.03f), new Vector2(0.965f, 0.97f), 8, new RectOffset(20, 20, 16, 16));
            UiFactory.StretchListChildrenWidth(list);
            UiFactory.CreateSubHeader(list, "Quick Presets");
            RectTransform presetRow = UiFactory.CreateRect(list, "Setup preset row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            presetRow.sizeDelta = new Vector2(560f, 40f);
            UiFactory.AddHorizontalLayout(presetRow, 6, new RectOffset(0, 0, 0, 0));
            CreateSetupPresetButton(presetRow, "Balanced", settings, refresh, 3, 3, 3, 3, 3);
            CreateSetupPresetButton(presetRow, "High Downforce", settings, refresh, 5, 5, 3, 4, 4);
            CreateSetupPresetButton(presetRow, "Low Drag", settings, refresh, 1, 1, 3, 2, 2);
            CreateSetupPresetButton(presetRow, "Wet", settings, refresh, 4, 4, 3, 2, 4);
            CreateSetupPresetButton(presetRow, "Kerb Friendly", settings, refresh, 3, 3, 3, 1, 4);

            UiFactory.CreateDivider(list);
            UiFactory.CreateSubHeader(list, "Garage Setup");
            CreateSetupCycleButton(list, "Front Wing", "More wing: cornering grip up, top speed down.", settings.Current.setupFrontWing, value => settings.Current.setupFrontWing = value, settings, refresh);
            CreateSetupCycleButton(list, "Rear Wing", "More wing: cornering grip up, top speed down.", settings.Current.setupRearWing, value => settings.Current.setupRearWing = value, settings, refresh);
            CreateSetupCycleButton(list, "Brake Bias", "Off-center stops harder but unsettles the car.", settings.Current.setupBrakeBias, value => settings.Current.setupBrakeBias = value, settings, refresh);
            CreateSetupCycleButton(list, "Suspension", "Stiffer grips smooth tarmac but hates kerbs.", settings.Current.setupSuspension, value => settings.Current.setupSuspension = value, settings, refresh);
            CreateSetupCycleButton(list, "Ride Height", "Lower cuts drag but is harsher on kerbs.", settings.Current.setupRideHeight, value => settings.Current.setupRideHeight = value, settings, refresh);

            RectTransform resetSlot;
            UiFactory.CreateSettingRow(list, "Reset", "Return every setup value to neutral.", out resetSlot);
            UiFactory.CreateCycleControl(resetSlot, "Reset to Neutral", () =>
            {
                settings.Current.setupFrontWing = 3;
                settings.Current.setupRearWing = 3;
                settings.Current.setupBrakeBias = 3;
                settings.Current.setupSuspension = 3;
                settings.Current.setupRideHeight = 3;
                settings.Save();
                refresh();
            });

            RectTransform right = UiFactory.CreateCard(background, "Setup summary card", new Vector2(0.54f, 0.3f), new Vector2(0.94f, 0.82f));
            RectTransform summaryList = UiFactory.CreateRect(right, "Setup summary list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(summaryList, 10, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(summaryList, "Predicted Effect");
            Text summary = UiFactory.CreateText(summaryList, "Setup summary", BuildSetupSummary(settings.Current), 19, new Color(0.84f, 0.91f, 0.95f), TextAnchor.UpperLeft);
            summary.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(summary, 560f, 220f);

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Back", backAction);
        }

        // One-tap presets covering the common trade-offs so a player who doesn't
        // want to reason about five separate sliders still gets a sensible setup
        // for the conditions ahead, without adding a whole new screen for it.
        void CreateSetupPresetButton(RectTransform parent, string label, GameSettingsStore settings, UnityEngine.Events.UnityAction refresh, int frontWing, int rearWing, int brakeBias, int suspension, int rideHeight)
        {
            Button button = UiFactory.CreateSecondaryButton(parent, label, () =>
            {
                settings.Current.setupFrontWing = frontWing;
                settings.Current.setupRearWing = rearWing;
                settings.Current.setupBrakeBias = brakeBias;
                settings.Current.setupSuspension = suspension;
                settings.Current.setupRideHeight = rideHeight;
                settings.Save();
                refresh();
            });
            UiFactory.SetSize(button, 104f, 38f);
        }

        void CreateSetupCycleButton(RectTransform parent, string label, string description, int value, System.Action<int> assign, GameSettingsStore settings, UnityEngine.Events.UnityAction refresh)
        {
            RectTransform control;
            UiFactory.CreateSettingRow(parent, label, description, out control);
            UiFactory.CreateCycleControl(control, SetupStepLabel(value), () =>
            {
                assign(value % 5 + 1);
                settings.Save();
                refresh();
            });
        }

        string SetupStepLabel(int value)
        {
            string[] names = { "", "Very Low", "Low", "Neutral", "High", "Very High" };
            int clamped = Mathf.Clamp(value, 1, 5);
            return clamped + "/5  " + names[clamped];
        }

        // Mirrors the multipliers in VehicleController.ApplyCarSetup so the screen
        // tells the truth about what the sliders do.
        string BuildSetupSummary(GameSettingsData current)
        {
            float wing = (current.setupFrontWing + current.setupRearWing) * 0.5f - 3f;
            float bias = current.setupBrakeBias - 3f;
            float stiffness = current.setupSuspension - 3f;
            float ride = current.setupRideHeight - 3f;
            float gripPercent = (wing * 0.016f + stiffness * 0.004f - Mathf.Abs(bias) * 0.004f) * 100f;
            float topSpeedPercent = (-wing * 0.011f - ride * 0.0045f) * 100f;
            float brakePercent = bias * 1.4f;
            string kerbs = stiffness > 0 || ride < 0 ? "Reduced" : (stiffness < 0 ? "Improved" : "Neutral");
            string wear = stiffness > 0 ? "Higher" : (stiffness < 0 ? "Lower" : "Neutral");
            return "Cornering grip   " + SignedPercent(gripPercent) + "\n" +
                   "Top speed        " + SignedPercent(topSpeedPercent) + "\n" +
                   "Braking power    " + SignedPercent(brakePercent) + "\n" +
                   "Kerb tolerance   " + kerbs + "\n" +
                   "Tyre wear        " + wear;
        }

        string SignedPercent(float value)
        {
            return (value >= 0f ? "+" : "") + value.ToString("0.0") + "%";
        }

        public void ShowCareerStats(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Career stats background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateTopNav(background, "Career Stats");
            PlayerRecordsData records = PlayerRecordsStore.Data;

            RectTransform rowOne = UiFactory.CreateRect(background, "Stats row one", new Vector2(0.06f, 0.76f), new Vector2(0.94f, 0.84f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(rowOne, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(rowOne, "Races", records.racesFinished.ToString(), 190f);
            UiFactory.CreateStatCard(rowOne, "Wins", records.raceWins.ToString(), 190f);
            UiFactory.CreateStatCard(rowOne, "Podiums", records.podiums.ToString(), 190f);
            UiFactory.CreateStatCard(rowOne, "Poles", records.polePositions.ToString(), 190f);
            UiFactory.CreateStatCard(rowOne, "Fastest Laps", records.fastestLaps.ToString(), 210f);

            RectTransform rowTwo = UiFactory.CreateRect(background, "Stats row two", new Vector2(0.06f, 0.66f), new Vector2(0.94f, 0.74f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(rowTwo, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(rowTwo, "Points Scored", records.totalPoints.ToString(), 190f);
            UiFactory.CreateStatCard(rowTwo, "Clean Races", records.cleanRaces.ToString(), 190f);
            UiFactory.CreateStatCard(rowTwo, "Best Qualifying", records.bestQualifyingPosition > 0 ? "P" + records.bestQualifyingPosition : "--", 210f);
            UiFactory.CreateStatCard(rowTwo, "Track Limit Warnings", records.trackLimitWarningsTotal.ToString(), 250f);

            RectTransform content = UiFactory.CreateScrollPanel(background, "Track record list", new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.64f), 4, new RectOffset(18, 18, 12, 12));
            UiFactory.CreateSubHeader(content, "Local Track Records");
            if (records.trackRecords.Count == 0)
            {
                Text none = UiFactory.CreateText(content, "No records", "No lap records yet. Run a time trial to set benchmarks.", 17, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.SetSize(none, 900f, 28f);
            }

            const float recordRowWidth = 1100f;
            for (int i = 0; i < records.trackRecords.Count; i++)
            {
                TrackRecordEntry entry = records.trackRecords[i];
                string trackName = entry.trackId;
                for (int e = 0; e < data.Calendar.events.Count; e++)
                {
                    if (data.Calendar.events[e].trackId == entry.trackId)
                    {
                        trackName = data.Calendar.events[e].displayName;
                        break;
                    }
                }

                RectTransform row = UiFactory.CreateTableRow(content, "Record row " + i, recordRowWidth, 30f, false, i);
                UiFactory.AddRowCell(row, "Track", trackName, 0.02f, 0.6f, 15, new Color(0.9f, 0.95f, 0.98f), TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Time", UiFactory.FormatTime(entry.bestLapTime), 0.6f, 0.78f, 14, UiFactory.AccentCyan, TextAnchor.MiddleLeft);
                if (!string.IsNullOrEmpty(entry.context))
                {
                    UiFactory.AddRowCell(row, "Context", entry.context.ToUpperInvariant(), 0.78f, 1f, 13, UiFactory.TextMuted, TextAnchor.MiddleRight);
                }
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
            UiFactory.CreatePrimaryButton(footerRight, "Trophy Cabinet", () => ShowTrophyCabinet(data, career, settings));
        }

        // Trophy Cabinet: the deeper, more ceremonial counterpart to Career
        // Stats above - season/championship-level trophies and per-track
        // achievements (wins/podiums/poles) that a single flat stat row can't
        // hold, all sourced from the same PlayerRecordsData this session
        // already extended RecordRaceFinish/RecordSeasonCompleted to fill in.
        public void ShowTrophyCabinet(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Trophy cabinet background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateTopNav(background, "Trophy Cabinet");
            PlayerRecordsData records = PlayerRecordsStore.Data;

            RectTransform rowOne = UiFactory.CreateRect(background, "Trophy row one", new Vector2(0.06f, 0.76f), new Vector2(0.94f, 0.84f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(rowOne, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(rowOne, "Championships", records.championshipsWon.ToString(), 210f);
            UiFactory.CreateStatCard(rowOne, "Constructors' Titles", records.constructorsChampionshipsWon.ToString(), 240f);
            UiFactory.CreateStatCard(rowOne, "Seasons Completed", records.completedSeasons.ToString(), 240f);
            UiFactory.CreateStatCard(rowOne, "Clean Race Streak", records.bestCleanRaceStreak.ToString(), 220f);

            RectTransform rowTwo = UiFactory.CreateRect(background, "Trophy row two", new Vector2(0.06f, 0.66f), new Vector2(0.94f, 0.74f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(rowTwo, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(rowTwo, "Biggest Comeback", records.biggestComebackPositions > 0 ? "+" + records.biggestComebackPositions + " places" : "--", 240f);
            UiFactory.CreateStatCard(rowTwo, "Most Overtakes (1 race)", records.mostOvertakesInRace.ToString(), 260f);
            UiFactory.CreateStatCard(rowTwo, "Best Wet Result", records.bestWetFinishPosition > 0 ? "P" + records.bestWetFinishPosition : "--", 220f);

            RectTransform content = UiFactory.CreateScrollPanel(background, "Track achievement list", new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.64f), 4, new RectOffset(18, 18, 12, 12));
            UiFactory.CreateSubHeader(content, "Track Achievements");
            if (records.trackAchievements == null || records.trackAchievements.Count == 0)
            {
                Text none = UiFactory.CreateText(content, "No achievements", "No wins, podiums, or poles recorded yet - they'll show up here after your first race weekend.", 17, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.SetSize(none, 900f, 28f);
            }

            const float achievementRowWidth = 1100f;
            for (int i = 0; i < (records.trackAchievements != null ? records.trackAchievements.Count : 0); i++)
            {
                TrackAchievementEntry entry = records.trackAchievements[i];
                if (entry.wins == 0 && entry.podiums == 0 && entry.poles == 0)
                {
                    continue;
                }

                string trackName = entry.trackId;
                for (int e = 0; e < data.Calendar.events.Count; e++)
                {
                    if (data.Calendar.events[e].trackId == entry.trackId)
                    {
                        trackName = data.Calendar.events[e].displayName;
                        break;
                    }
                }

                RectTransform row = UiFactory.CreateTableRow(content, "Achievement row " + i, achievementRowWidth, 30f, false, i);
                UiFactory.AddRowCell(row, "Track", trackName, 0.02f, 0.5f, 15, new Color(0.9f, 0.95f, 0.98f), TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Wins", entry.wins + " wins", 0.5f, 0.68f, 13, UiFactory.AccentGreen, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Podiums", entry.podiums + " podiums", 0.68f, 0.86f, 13, UiFactory.AccentCyan, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Poles", entry.poles + " poles", 0.86f, 1f, 13, UiFactory.Accent, TextAnchor.MiddleRight);
            }

            RectTransform footerLeft2;
            RectTransform footerRight2;
            UiFactory.CreateFooterBar(background, out footerLeft2, out footerRight2);
            UiFactory.CreateSecondaryButton(footerLeft2, "Career Stats", () => ShowCareerStats(data, career, settings));
            UiFactory.CreateSecondaryButton(footerLeft2, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        // Practice programs: once-per-round simulated running that pays out resource
        // points and a little reputation, so weekends have more to do than qualify.
        public void ShowPracticePrograms(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Practice background", new Color(0.012f, 0.016f, 0.021f, 1f));
            CalendarEventData current = career.CurrentEvent();
            string eventName = current == null ? "Prototype GP" : current.displayName;
            UiFactory.CreateScreenHeader(background, "Practice Programs", "Round " + career.Save.currentRound + "  ·  " + eventName + "  ·  Each program can run once per round.");

            RectTransform content = UiFactory.CreateScrollPanel(background, "Practice list", new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.85f), 10, new RectOffset(18, 18, 14, 14));
            CreatePracticeProgramRow(content, data, career, settings, "acclimatisation", "Track Acclimatisation", "Learn the braking points and kerbs. Steady laps, no risks.", 22, 1);
            CreatePracticeProgramRow(content, data, career, settings, "tyreManagement", "Tyre Management", "Long-run stint watching temperatures and wear windows.", 20, 0);
            CreatePracticeProgramRow(content, data, career, settings, "ersManagement", "ERS Management", "Deployment mapping over a full lap for better battery use.", 18, 0);
            CreatePracticeProgramRow(content, data, career, settings, "qualifyingPace", "Qualifying Pace", "Low fuel, maximum attack simulation runs.", 24, 1);
            CreatePracticeProgramRow(content, data, career, settings, "racePace", "Race Pace", "Heavy fuel race simulation with pit stop rehearsal.", 26, 1);

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Race Weekend", bootstrap.ShowRaceWeekend);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
        }

        void CreatePracticeProgramRow(RectTransform parent, GameDataRepository data, CareerManager career, GameSettingsStore settings, string programId, string title, string description, int resourceReward, int reputationReward)
        {
            string key = "s" + career.Save.currentSeason + "_r" + career.Save.currentRound + "_" + programId;
            bool done = career.Save.completedPracticePrograms.Contains(key);

            RectTransform card = UiFactory.CreateRect(parent, programId + " program card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(1180f, 84f);
            Image background = card.gameObject.AddComponent<Image>();
            background.color = done ? new Color(0.014f, 0.02f, 0.026f, 0.7f) : UiFactory.PanelDark;
            UiFactory.CreateBand(card, "Program rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), done ? UiFactory.AccentGreen : UiFactory.AccentCyan);

            Text titleText = UiFactory.CreateText(card, "Program title", title, 19, done ? UiFactory.TextMuted : UiFactory.TextPrimary, TextAnchor.UpperLeft);
            RectTransform titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(0.6f, 1f);
            titleRect.offsetMin = new Vector2(16f, -32f);
            titleRect.offsetMax = new Vector2(0f, -6f);

            Text descriptionText = UiFactory.CreateText(card, "Program description", description, 15, UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform descriptionRect = descriptionText.GetComponent<RectTransform>();
            descriptionRect.anchorMin = new Vector2(0f, 0f);
            descriptionRect.anchorMax = new Vector2(0.6f, 1f);
            descriptionRect.offsetMin = new Vector2(16f, 8f);
            descriptionRect.offsetMax = new Vector2(0f, -36f);

            Text rewardText = UiFactory.CreateText(card, "Program reward", done ? "COMPLETE" : "+" + resourceReward + " RP" + (reputationReward > 0 ? "  +" + reputationReward + " REP" : ""), 16, done ? UiFactory.AccentGreen : UiFactory.AccentAmber, TextAnchor.MiddleRight);
            RectTransform rewardRect = rewardText.GetComponent<RectTransform>();
            rewardRect.anchorMin = new Vector2(0.6f, 0f);
            rewardRect.anchorMax = new Vector2(0.86f, 1f);
            rewardRect.offsetMin = Vector2.zero;
            rewardRect.offsetMax = Vector2.zero;

            if (!done)
            {
                // Playable practice programs: drives an actual session instead of
                // an instant reward - see GameBootstrap.StartCareerPractice and
                // RaceManager.EvaluatePracticeSession, which scores real telemetry
                // once the player ends the session from the pause menu.
                UnityEngine.UI.Button run = UiFactory.CreateButton(card, "Drive", () =>
                {
                    bootstrap.StartCareerPractice(programId);
                });
                RectTransform runRect = run.GetComponent<RectTransform>();
                runRect.anchorMin = new Vector2(0.88f, 0.5f);
                runRect.anchorMax = new Vector2(0.88f, 0.5f);
                runRect.sizeDelta = new Vector2(110f, 44f);
                runRect.anchoredPosition = new Vector2(55f, 0f);
            }
        }

        // Single source of truth for each practice program's reward, kept in sync
        // with the resourceReward/reputationReward literals passed into
        // CreatePracticeProgramRow above - used again by ShowPracticeSessionResult
        // once the player actually finishes driving the program.
        void PracticeProgramReward(string programId, out int resourceReward, out int reputationReward)
        {
            switch (programId)
            {
                case "acclimatisation": resourceReward = 22; reputationReward = 1; break;
                case "tyreManagement": resourceReward = 20; reputationReward = 0; break;
                case "ersManagement": resourceReward = 18; reputationReward = 0; break;
                case "qualifyingPace": resourceReward = 24; reputationReward = 1; break;
                case "racePace": resourceReward = 26; reputationReward = 1; break;
                default: resourceReward = 0; reputationReward = 0; break;
            }
        }

        // Playable practice programs: shown after the player ends a Practice
        // session from the pause menu (GameBootstrap.EndPracticeSession). Grants
        // the program's reward only if RaceManager.EvaluatePracticeSession judged
        // the session a pass, and only once per round per program.
        public void ShowPracticeSessionResult(GameDataRepository data, CareerManager career, GameSettingsStore settings, RaceManager.PracticeSessionResult result)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Practice result background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, result.title, result.passed ? "Program complete" : "Program not yet complete");

            RectTransform card = UiFactory.CreateCard(background, "Practice result card", new Vector2(0.28f, 0.28f), new Vector2(0.72f, 0.72f));
            RectTransform content = UiFactory.CreateRect(card, "Practice result content", Vector2.zero, Vector2.one, new Vector2(28f, 22f), new Vector2(-28f, -22f));
            UiFactory.AddVerticalLayout(content, 14, new RectOffset(0, 0, 0, 0));

            UiFactory.CreateText(content, "Practice result verdict", result.passed ? "PASSED" : "NOT ACHIEVED", 30, result.passed ? UiFactory.AccentGreen : UiFactory.AccentAmber, TextAnchor.MiddleLeft);
            Text metric = UiFactory.CreateText(content, "Practice result metric", result.metricSummary, 17, UiFactory.TextMuted, TextAnchor.UpperLeft);
            metric.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(metric, 480f, 60f);

            int resourceReward;
            int reputationReward;
            PracticeProgramReward(result.programId, out resourceReward, out reputationReward);

            string key = result.programId == null || career.Save == null ? null : ("s" + career.Save.currentSeason + "_r" + career.Save.currentRound + "_" + result.programId);
            bool alreadyRecorded = key != null && career.Save.completedPracticePrograms.Contains(key);
            if (result.passed && key != null && !alreadyRecorded)
            {
                career.Save.completedPracticePrograms.Add(key);
                career.Save.resourcePoints += resourceReward;
                career.Save.reputation += reputationReward;
                // Practice running feeds correlation data to the factory:
                // projects finishing this round get a small success bonus.
                career.Save.practiceQualityThisRound++;
                career.Write();
                UiFactory.CreateText(content, "Practice result reward", "+" + resourceReward + " RP" + (reputationReward > 0 ? "  +" + reputationReward + " REP" : ""), 18, UiFactory.AccentGreen, TextAnchor.MiddleLeft);
            }
            else if (!result.passed)
            {
                UiFactory.CreateText(content, "Practice result reward", "No reward yet - drive the session again to hit the target.", 15, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            }
            else
            {
                UiFactory.CreateText(content, "Practice result reward", "Already completed this round.", 15, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Practice Programs", () => ShowPracticePrograms(data, career, settings));
            UiFactory.CreateSecondaryButton(footerLeft, "Race Weekend", bootstrap.ShowRaceWeekend);
        }

        bool profileIsWet(CalendarEventData raceEvent)
        {
            if (raceEvent == null || string.IsNullOrEmpty(raceEvent.weatherProfile))
            {
                return false;
            }

            string profile = raceEvent.weatherProfile.ToLowerInvariant();
            return profile.Contains("wet") || profile.Contains("mixed");
        }

        // One circuit card in the track info list: name, weather, laps, local best
        // and layout traits; clicking it launches a time trial on that circuit.
        void CreateTrackInfoRow(RectTransform parent, CalendarEventData raceEvent)
        {
            // Row grew (was 72 tall, fractional-anchor columns) to fit the new
            // track preview widget - switched to the same fixed-pixel
            // SetTopLeft/anchored-pivot layout BuildQuickRaceTrackRow already
            // uses, since that reads more predictably once a widget with its
            // own internal layout (map + legend) needs a real, guaranteed slot
            // rather than a fractional share of the row.
            RectTransform card = UiFactory.CreateRect(parent, raceEvent.trackId + " info card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(1180f, 132f);
            Image background = card.gameObject.AddComponent<Image>();
            background.color = UiFactory.PanelDark;
            UiFactory.CreateBand(card, "Track info rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.Accent);

            Text titleText = UiFactory.CreateText(card, "Track title", "R" + raceEvent.round.ToString("00") + "  " + raceEvent.displayName, 18, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            SetTopLeft(titleText.rectTransform, 16f, 14f);
            UiFactory.SetSize(titleText, 380f, 26f);

            Text metaText = UiFactory.CreateText(card, "Track meta", raceEvent.country + "   ·   " + WeatherProfileText(raceEvent.weatherProfile).ToUpperInvariant() + "   ·   " + raceEvent.laps25Percent + " LAPS", 14, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(metaText.rectTransform, 16f, 46f);
            UiFactory.SetSize(metaText, 380f, 20f);

            float best = PlayerRecordsStore.GetBestLap(raceEvent.trackId);
            Text bestText = UiFactory.CreateText(card, "Track best", best > 0f ? "BEST " + UiFactory.FormatTime(best) : "NO RECORD", 14, best > 0f ? UiFactory.AccentPurple : UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(bestText.rectTransform, 16f, 74f);
            UiFactory.SetSize(bestText, 300f, 20f);

            // Track preview: see BuildTrackPreview for what it draws and why.
            RectTransform preview = BuildTrackPreview(card, raceEvent, 100f);
            preview.anchorMin = new Vector2(0f, 0.5f);
            preview.anchorMax = new Vector2(0f, 0.5f);
            preview.pivot = new Vector2(0f, 0.5f);
            preview.anchoredPosition = new Vector2(420f, 0f);

            Text traitsText = UiFactory.CreateText(card, "Track traits", TrackTraits(raceEvent), 14, UiFactory.AccentCyan, TextAnchor.MiddleLeft);
            traitsText.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform traitsRect = traitsText.GetComponent<RectTransform>();
            traitsRect.anchorMin = new Vector2(0f, 0.5f);
            traitsRect.anchorMax = new Vector2(0f, 0.5f);
            traitsRect.pivot = new Vector2(0f, 0.5f);
            traitsRect.anchoredPosition = new Vector2(680f, 0f);
            UiFactory.SetSize(traitsText, 320f, 110f);

            // Explicit action instead of the whole row being clickable - a
            // screen whose job is "show me info about this track" should never
            // silently launch a session on a stray click, especially now that
            // this screen is reachable mid-career-weekend. Matches the
            // explicit-button convention already used by
            // BuildQuickRaceTrackRow's "Select" button.
            Button launch = UiFactory.CreateSecondaryButton(card, "Time Trial", () => bootstrap.BeginTimeTrial(raceEvent));
            RectTransform launchRect = launch.GetComponent<RectTransform>();
            launchRect.anchorMin = new Vector2(1f, 0.5f);
            launchRect.anchorMax = new Vector2(1f, 0.5f);
            launchRect.pivot = new Vector2(1f, 0.5f);
            launchRect.sizeDelta = new Vector2(148f, 42f);
            launchRect.anchoredPosition = new Vector2(-16f, 0f);
        }

        // ---------- track preview ----------
        // New feature: a small visual "shape of the lap" widget for the track
        // selection and track info screens, with pit entry/exit and
        // tightest/highest-speed corner call-outs. No live per-track geometry
        // is available here - TrackManager only builds the real circuit mesh
        // inside an actual loaded race scene (see the comment that used to be
        // on ShowQuickRaceTrackSelect explaining why this never existed
        // before) - so this draws a deterministic procedural approximation of
        // a lap instead: seeded from the track's own id, so the same track
        // always renders the same shape, but it is not a literal trace of the
        // real circuit. The pit entry marker uses the actual shared
        // TrackRuntime.PitCorridorStartNormalized constant (a fraction of the
        // lap shared by every real circuit, so that part is accurate); pit
        // exit has no equivalent public constant to read, so it mirrors the
        // 0.992 literal TrackRuntime.GetPitReleasePose uses internally.
        // TODO(barrier-fix): if TrackManager/RaceManager ever expose a
        // scene-independent per-track corner list (distance + turn angle) and
        // a named pit-exit-normalized constant, swap this generator for that
        // real data so the preview traces the actual circuit instead of an
        // approximation. Tightest-corner/highest-speed markers below are
        // computed directly from this generated loop's own curvature - real
        // geometry analysis, just of an approximated lap rather than the true
        // one.
        const int TrackPreviewPointCount = 48;
        const float TrackPreviewPitEntryNormalized = 0.885f; // TrackRuntime.PitCorridorStartNormalized
        const float TrackPreviewPitExitNormalized = 0.992f; // matches TrackRuntime.GetPitReleasePose's internal constant

        // Builds the widget and returns its root RectTransform (caller
        // positions/parents it like any other element). mapSize is the
        // square map's edge length; a short color-keyed legend is placed to
        // its right, so the whole widget's footprint is
        // (mapSize + legend column) wide by mapSize tall.
        RectTransform BuildTrackPreview(Transform parent, CalendarEventData raceEvent, float mapSize)
        {
            const float legendColumnWidth = 128f;
            string name = (raceEvent != null && !string.IsNullOrEmpty(raceEvent.trackId) ? raceEvent.trackId : "track") + " preview";
            RectTransform root = UiFactory.CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            root.sizeDelta = new Vector2(mapSize + 8f + legendColumnWidth, mapSize);

            RectTransform mapArea = UiFactory.CreateRect(root, name + " map", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            mapArea.pivot = new Vector2(0f, 1f);
            mapArea.sizeDelta = new Vector2(mapSize, mapSize);
            Image mapBackground = mapArea.gameObject.AddComponent<Image>();
            UiFactory.StyleRoundedSmall(mapBackground, new Color(0.02f, 0.03f, 0.045f, 0.92f));

            string seedKey = raceEvent != null && !string.IsNullOrEmpty(raceEvent.trackId) ? raceEvent.trackId : "default_track";
            System.Random rng = new System.Random(seedKey.GetHashCode());

            float centerX = mapSize * 0.5f;
            float centerY = mapSize * 0.5f;
            float baseRx = mapSize * 0.36f;
            float baseRy = mapSize * 0.3f;
            float amp1 = 0.10f + (float)rng.NextDouble() * 0.10f;
            float amp2 = 0.06f + (float)rng.NextDouble() * 0.08f;
            int harmonicOne = 2 + rng.Next(0, 2);
            int harmonicTwo = 3 + rng.Next(0, 3);
            float phase1 = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float phase2 = (float)(rng.NextDouble() * Mathf.PI * 2.0);

            Vector2[] points = new Vector2[TrackPreviewPointCount];
            for (int i = 0; i < TrackPreviewPointCount; i++)
            {
                float t = i / (float)TrackPreviewPointCount;
                float angle = t * Mathf.PI * 2f;
                float radiusScale = 1f + amp1 * Mathf.Sin(harmonicOne * angle + phase1) + amp2 * Mathf.Sin(harmonicTwo * angle + phase2);
                points[i] = new Vector2(centerX + Mathf.Cos(angle) * baseRx * radiusScale, centerY + Mathf.Sin(angle) * baseRy * radiusScale);
            }

            float[] cumulative = new float[TrackPreviewPointCount + 1];
            for (int i = 0; i < TrackPreviewPointCount; i++)
            {
                Vector2 next = points[(i + 1) % TrackPreviewPointCount];
                cumulative[i + 1] = cumulative[i] + Vector2.Distance(points[i], next);
            }

            float totalLength = cumulative[TrackPreviewPointCount];

            // Ribbon.
            for (int i = 0; i < TrackPreviewPointCount; i++)
            {
                CreatePreviewDot(mapArea, points[i], 4f, new Color(0.2f, 0.72f, 1f, 0.55f));
            }

            // Start/finish.
            CreatePreviewDot(mapArea, points[0], 8f, Color.white);

            // Pit entry/exit - see the class comment above for accuracy notes.
            Vector2 pitEntryPoint = SamplePreviewLoop(points, cumulative, totalLength, TrackPreviewPitEntryNormalized);
            Vector2 pitExitPoint = SamplePreviewLoop(points, cumulative, totalLength, TrackPreviewPitExitNormalized);
            CreatePreviewDot(mapArea, pitEntryPoint, 7f, UiFactory.AccentAmber);
            CreatePreviewDot(mapArea, pitExitPoint, 7f, UiFactory.AccentAmber);

            // Tightest / highest-speed corner, derived from this generated
            // loop's own curvature rather than guessed from the track id.
            int tightestIndex = 0;
            float tightestAngle = -1f;
            int fastestIndex = 0;
            float fastestAngle = float.MaxValue;
            for (int i = 0; i < TrackPreviewPointCount; i++)
            {
                Vector2 previous = points[(i - 1 + TrackPreviewPointCount) % TrackPreviewPointCount];
                Vector2 current = points[i];
                Vector2 next = points[(i + 1) % TrackPreviewPointCount];
                Vector2 entryDirection = (current - previous).normalized;
                Vector2 exitDirection = (next - current).normalized;
                float turnAngle = Vector2.Angle(entryDirection, exitDirection);
                if (turnAngle > tightestAngle)
                {
                    tightestAngle = turnAngle;
                    tightestIndex = i;
                }

                if (turnAngle < fastestAngle)
                {
                    fastestAngle = turnAngle;
                    fastestIndex = i;
                }
            }

            CreatePreviewDot(mapArea, points[tightestIndex], 7f, UiFactory.AccentPurple);
            CreatePreviewDot(mapArea, points[fastestIndex], 7f, UiFactory.AccentGreen);

            string legendText =
                ColorTag(Color.white, "●") + " Start / Finish\n" +
                ColorTag(UiFactory.AccentAmber, "●") + " Pit In\n" +
                ColorTag(UiFactory.AccentAmber, "●") + " Pit Out\n" +
                ColorTag(UiFactory.AccentPurple, "●") + " Tightest Corner\n" +
                ColorTag(UiFactory.AccentGreen, "●") + " Highest Speed";
            Text legend = UiFactory.CreateText(root, name + " legend", legendText, 13, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            legend.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform legendRect = legend.GetComponent<RectTransform>();
            legendRect.anchorMin = new Vector2(0f, 0f);
            legendRect.anchorMax = new Vector2(0f, 1f);
            legendRect.pivot = new Vector2(0f, 0.5f);
            legendRect.offsetMin = new Vector2(mapSize + 8f, 0f);
            legendRect.offsetMax = new Vector2(mapSize + 8f + legendColumnWidth, 0f);

            return root;
        }

        void CreatePreviewDot(Transform parent, Vector2 localPoint, float size, Color color)
        {
            RectTransform dot = UiFactory.CreateRect(parent, "Preview dot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            dot.pivot = new Vector2(0.5f, 0.5f);
            dot.sizeDelta = new Vector2(size, size);
            dot.anchorMin = new Vector2(0f, 1f);
            dot.anchorMax = new Vector2(0f, 1f);
            dot.anchoredPosition = new Vector2(localPoint.x, -localPoint.y);
            Image image = dot.gameObject.AddComponent<Image>();
            image.sprite = UiFactory.GlowSprite;
            image.color = color;
            image.raycastTarget = false;
        }

        // Linearly interpolates a point on the generated loop at the given
        // normalized progress (0..1), matching how TrackRuntime.SampleAtDistance
        // maps a normalized fraction of the real lap to a world point.
        Vector2 SamplePreviewLoop(Vector2[] points, float[] cumulative, float totalLength, float normalized)
        {
            if (totalLength <= 0f || points.Length == 0)
            {
                return Vector2.zero;
            }

            float targetDistance = Mathf.Repeat(normalized, 1f) * totalLength;
            for (int i = 0; i < points.Length; i++)
            {
                if (cumulative[i + 1] >= targetDistance)
                {
                    float segmentLength = cumulative[i + 1] - cumulative[i];
                    float segmentT = segmentLength > 0.0001f ? (targetDistance - cumulative[i]) / segmentLength : 0f;
                    Vector2 next = points[(i + 1) % points.Length];
                    return Vector2.Lerp(points[i], next, segmentT);
                }
            }

            return points[points.Length - 1];
        }

        static string ColorTag(Color color, string text)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";
        }

        // Heuristic layout traits by circuit family: enough for strategy flavor
        // without pretending to be full telemetry.
        string TrackTraits(CalendarEventData raceEvent)
        {
            string id = raceEvent == null || string.IsNullOrEmpty(raceEvent.trackId) ? "" : raceEvent.trackId;
            string speed = "Balanced speed";
            if (id.Contains("monza") || id.Contains("las_vegas") || id.Contains("jeddah") || id.Contains("baku") || id.Contains("spa") || id.Contains("silverstone"))
            {
                speed = "High top speed";
            }
            else if (id.Contains("monaco") || id.Contains("singapore") || id.Contains("hungary") || id.Contains("madrid"))
            {
                speed = "Low-speed technical";
            }

            string wear = "Wear medium";
            if (id.Contains("bahrain") || id.Contains("suzuka") || id.Contains("barcelona") || id.Contains("silverstone") || id.Contains("qatar") || id.Contains("austin"))
            {
                wear = "Wear high";
            }
            else if (id.Contains("monza") || id.Contains("baku") || id.Contains("las_vegas") || id.Contains("canada"))
            {
                wear = "Wear low";
            }

            string overtaking = "Overtaking fair";
            if (id.Contains("monaco") || id.Contains("singapore") || id.Contains("hungary") || id.Contains("zandvoort"))
            {
                overtaking = "Overtaking hard";
            }
            else if (id.Contains("monza") || id.Contains("baku") || id.Contains("jeddah") || id.Contains("las_vegas") || id.Contains("spa") || id.Contains("china"))
            {
                overtaking = "Overtaking easy";
            }

            string rain = "";
            if (raceEvent != null && !string.IsNullOrEmpty(raceEvent.weatherProfile))
            {
                string profile = raceEvent.weatherProfile.ToLowerInvariant();
                if (profile.Contains("wet") || profile.Contains("mixed"))
                {
                    rain = "\nRain risk high";
                }
            }

            return speed + " · " + wear + "\n" + overtaking + rain;
        }

        public void ShowRaceHud(RaceManager race, RaceParticipant player)
        {
            Clear();
            GameObject hudObject = new GameObject("Race HUD");
            hud = hudObject.AddComponent<RaceHud>();
            hud.Build(canvas.transform, race, player);
            BuildPausePanel(race);
        }

        public void SetPauseVisible(bool visible)
        {
            if (pausePanel != null)
            {
                pausePanel.SetActive(visible);
            }
        }

        // Shared nominal content width for every hand-sized row in the results
        // report (podium/highlights/badges/cards), matching the classification
        // table below them so the whole report reads as one aligned column.
        const float ReportContentWidth = 1240f;

        // Full redesign (not a patch): the report used to build every section
        // straight onto CreateScrollPanel's content rect using hand-guessed
        // sizeDelta heights (see UiFactory's "auto-sizing report primitives"
        // comment for the full root-cause writeup). Any guess that undershot the
        // real rendered height - most catastrophically the race-control timeline's
        // `count * 24f + 12f`, but really any wrapped text anywhere above the
        // classification table - meant the outer ScrollRect's content height
        // came out too short, so long sections could visually bleed into / cover
        // whatever followed and the table itself could end up beyond the
        // reachable scroll range. Every section below now either has a fixed,
        // provably-sufficient height, or uses UiFactory's CreateAutoCard /
        // CreateAutoHeightList / CreateAutoHeightRow / CreateAutoText helpers so
        // its sizeDelta always reflects Unity's own computed preferred size. The
        // single LayoutRebuilder.ForceRebuildLayoutImmediate call at the end
        // guarantees all of that has actually resolved before the report is
        // shown, exactly like RaceHud's radio stack already does for its own
        // dynamically-built content.
        public void ShowResults(RaceManager race, List<RaceResultEntry> results, bool careerRace)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Results background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateTopNav(background, "Race Report");
            // Finish-line motif directly under the header, tying the results
            // screen back to the in-race chequered finish flourish.
            UiFactory.CreateCheckeredBand(background, "Results chequered accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -96f), new Vector2(0f, -92f));

            RectTransform content = UiFactory.CreateScrollPanel(background, "Results report", new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f), 20, new RectOffset(6, 6, 6, 16));

            RaceResultEntry player = results != null ? results.Find(entry => entry.isPlayer) : null;

            // Required top-to-bottom order: podium, player summary, teammate,
            // strategy, incidents/race-control, achievements, race-control
            // timeline, full classification, championship impact. Cornering
            // telemetry isn't one of the required 9 - it's extra player
            // performance content folded in right after the player summary.
            BuildPodiumSection(content, race, results);
            BuildPlayerSummarySection(content, race, results, player);
            BuildCorneringTelemetryCard(content, race);
            BuildDrivingTelemetryCard(content, race);
            BuildTeammateSection(content, results, player);
            BuildStrategySection(content, race, player);
            BuildIncidentSection(content, race, results, player);
            BuildAchievementsSection(content, race, results, player);
            BuildRaceControlTimeline(content, race);

            UiFactory.CreateDivider(content);
            UiFactory.CreateSubHeader(content, "Full Classification");
            BuildFullClassificationTable(content, race, results);

            BuildChampionshipSection(content, race, player);

            // See the class-level comment above this method: forces every
            // ContentSizeFitter/LayoutGroup added above to resolve its final
            // size before the ScrollRect's content height (and therefore the
            // reachable scroll range) is used for anything.
            // Layout-audit fix: one call isn't enough. Every VerticalLayoutGroup
            // here uses childControlHeight=false (see UiFactory.AddVerticalLayout),
            // so a group computes its own preferred height by reading each
            // direct child's raw sizeDelta.y, NOT the child's resolved
            // preferredHeight - and sizeDelta.y is only actually written to its
            // final wrapped-content value during a child's own layout pass. For
            // a nested chain like content -> card -> list -> text (three or four
            // VerticalLayoutGroup levels deep throughout this screen), a single
            // top-down rebuild sees every ancestor still holding its child's
            // placeholder size, so cards under-report their height, later
            // sections overlap them, and content's total height (the scrollable
            // range) comes out short. Repeating the rebuild lets each pass
            // consume the previous pass's now-correct child sizes until it
            // converges from the leaves up to content.
            for (int layoutPass = 0; layoutPass < 4; layoutPass++)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => bootstrap.ShowMainMenu());
            if (careerRace)
            {
                UiFactory.CreatePrimaryButton(footerRight, "Continue Career", () => bootstrap.ShowCareer());
            }
            else
            {
                UiFactory.CreatePrimaryButton(footerRight, "Race Again", () => bootstrap.StartQuickRace());
            }
        }

        // Part 21: the very first thing a player sees after the final calendar
        // race's report is dismissed (see GameBootstrap.ShowCareer, which
        // routes here instead of straight to ShowCareerHub whenever
        // career.Save.seasonTransitionPending is set). Deliberately a short,
        // punchy transition, not the full breakdown - that's ShowSeasonReview,
        // one tap away - so the "the season just ended" moment reads as an
        // event in its own right instead of being buried at the top of a long
        // scroll of stats.
        public void ShowSeasonComplete(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            UiFactory.ApplyUiScale(canvas, settings.UiScale);
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Season complete background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateCheckeredBand(background, "Season complete chequered accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -96f), new Vector2(0f, -92f));

            SeasonArchive archive = career.GetLatestSeasonArchive();
            int completedSeason = archive != null ? archive.season : Mathf.Max(1, career.Save.currentSeason - 1);
            UiFactory.CreateScreenHeader(background, "Season " + completedSeason + " Complete",
                "The chequered flag has fallen on the season - here's how it finished.");

            RectTransform hero = UiFactory.CreateGlassPanel(background, "Season complete hero", new Vector2(0.18f, 0.5f), new Vector2(0.82f, 0.78f), Vector2.zero, Vector2.zero, UiFactory.PanelDark);
            if (archive == null)
            {
                Text noArchiveText = UiFactory.CreateText(hero, "No archive text", "Season data unavailable.", 18, UiFactory.TextMuted, TextAnchor.MiddleCenter);
                RectTransform noArchiveRect = noArchiveText.GetComponent<RectTransform>();
                noArchiveRect.anchorMin = Vector2.zero;
                noArchiveRect.anchorMax = Vector2.one;
                noArchiveRect.offsetMin = Vector2.zero;
                noArchiveRect.offsetMax = Vector2.zero;
            }
            else
            {
                RectTransform heroStack = UiFactory.CreateRect(hero, "Season complete hero stack", Vector2.zero, Vector2.one, new Vector2(28f, 20f), new Vector2(-28f, -20f));
                VerticalLayoutGroup heroLayout = heroStack.gameObject.AddComponent<VerticalLayoutGroup>();
                heroLayout.spacing = 10f;
                heroLayout.childAlignment = TextAnchor.UpperCenter;
                heroLayout.childControlWidth = true;
                heroLayout.childControlHeight = true;
                heroLayout.childForceExpandWidth = true;
                heroLayout.childForceExpandHeight = false;

                UiFactory.CreateText(heroStack, "Champion line", "Drivers' Champion: " + archive.driverChampionName, 24, UiFactory.TextPrimary, TextAnchor.MiddleCenter);
                UiFactory.CreateText(heroStack, "Constructor line", "Constructors' Champion: " + archive.constructorChampionName, 20, UiFactory.AccentCyan, TextAnchor.MiddleCenter);

                string playerLine = archive.playerFinalPosition > 0
                    ? archive.playerDriverName + " finished P" + archive.playerFinalPosition + " with " + archive.playerPoints + " point" + (archive.playerPoints == 1 ? "" : "s")
                    : archive.playerDriverName + "'s season is in the books";
                UiFactory.CreateText(heroStack, "Player line", playerLine, 18, UiFactory.TextMuted, TextAnchor.MiddleCenter);

                RectTransform statRow = UiFactory.CreateRect(heroStack, "Season complete stat row", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                UiFactory.SetFixedRowHeight(statRow.gameObject.AddComponent<LayoutElement>(), 64f);
                UiFactory.AddHorizontalLayout(statRow, 12, new RectOffset(0, 0, 4, 0));
                UiFactory.CreateStatCard(statRow, "Wins", archive.playerWins.ToString(), 150f);
                UiFactory.CreateStatCard(statRow, "Podiums", archive.playerPodiums.ToString(), 150f);
                UiFactory.CreateStatCard(statRow, "Poles", archive.playerPoles.ToString(), 150f);
                UiFactory.CreateStatCard(statRow, "Team", archive.playerTeamName + " P" + archive.playerTeamFinalPosition, 210f);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => bootstrap.ShowMainMenu());
            UiFactory.CreatePrimaryButton(footerRight, "View Full Season Review", () =>
            {
                career.AdvanceToSeasonReview();
                ShowSeasonReview(data, career, settings, archive, true);
            });
        }

        // Part 21: the full season-finale breakdown - reachable from
        // ShowSeasonComplete's "View Full Season Review" (isFreshCompletion,
        // continues into the offseason flow) and from ShowSeasonArchive
        // (browsing an old season, so it goes back there instead). Every
        // field read here comes from the frozen SeasonArchive snapshot, never
        // the live Save, so a season viewed from the archive months later
        // reads exactly as it did the day it closed.
        public void ShowSeasonReview(GameDataRepository data, CareerManager career, GameSettingsStore settings, SeasonArchive archive, bool isFreshCompletion)
        {
            Clear();
            UiFactory.ApplyUiScale(canvas, settings.UiScale);
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Season review background", new Color(0.012f, 0.016f, 0.021f, 1f));

            if (archive == null)
            {
                UiFactory.CreateScreenHeader(background, "Season Review", "No season data available.");
                RectTransform emptyFooterLeft;
                RectTransform emptyFooterRight;
                UiFactory.CreateFooterBar(background, out emptyFooterLeft, out emptyFooterRight);
                UiFactory.CreateSecondaryButton(emptyFooterLeft, "Back", () => ShowCareerHub(data, career, settings));
                return;
            }

            UiFactory.CreateCheckeredBand(background, "Season review chequered accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -96f), new Vector2(0f, -92f));
            UiFactory.CreateScreenHeader(background, "Season " + archive.season + " Review",
                archive.driverChampionName + " (Drivers')  ·  " + archive.constructorChampionName + " (Constructors')");

            RectTransform content = UiFactory.CreateScrollPanel(background, "Season review report", new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f), 20, new RectOffset(6, 6, 16, 16));

            UiFactory.CreateSubHeader(content, "Final Drivers' Championship");
            BuildStandingsRows(data, content, archive.finalDriverStandings, ReportContentWidth);
            UiFactory.CreateDivider(content);

            UiFactory.CreateSubHeader(content, "Final Constructors' Championship");
            BuildStandingsRows(data, content, archive.finalConstructorStandings, ReportContentWidth);

            BuildSeasonReviewGraphSection(content, career, archive);

            UiFactory.CreateSubHeader(content, "Player Season Summary");
            RectTransform playerStatsRow1 = UiFactory.CreateAutoHeightRow(content, "Season player stats row 1", ReportContentWidth, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(playerStatsRow1, "Final Position", "P" + archive.playerFinalPosition, 220f);
            UiFactory.CreateStatCard(playerStatsRow1, "Points", archive.playerPoints.ToString(), 220f);
            UiFactory.CreateStatCard(playerStatsRow1, "Wins", archive.playerWins.ToString(), 220f);
            UiFactory.CreateStatCard(playerStatsRow1, "Podiums", archive.playerPodiums.ToString(), 220f);
            RectTransform playerStatsRow2 = UiFactory.CreateAutoHeightRow(content, "Season player stats row 2", ReportContentWidth, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(playerStatsRow2, "Poles", archive.playerPoles.ToString(), 220f);
            UiFactory.CreateStatCard(playerStatsRow2, "Fastest Laps", archive.playerFastestLaps.ToString(), 220f);
            UiFactory.CreateStatCard(playerStatsRow2, "DNFs", archive.playerDnfs.ToString(), 220f);
            UiFactory.CreateStatCard(playerStatsRow2, "Penalized Races", archive.playerPenalizedRaces.ToString(), 220f);

            UiFactory.CreateSubHeader(content, "Teammate Comparison");
            if (!string.IsNullOrEmpty(archive.teammateName))
            {
                RectTransform teammateArea;
                UiFactory.CreateAutoCard(content, "Season teammate comparison", "", UiFactory.AccentCyan, ReportContentWidth, out teammateArea);
                float teammateInnerWidth = ReportContentWidth - 34f;
                UiFactory.CreateAutoText(teammateArea, "Season teammate headline",
                    archive.playerDriverName + " P" + archive.playerFinalPosition + " (" + archive.playerPoints + " pts)   vs   " +
                    archive.teammateName + " P" + archive.teammateFinalPosition + " (" + archive.teammatePoints + " pts)",
                    15, UiFactory.TextPrimary, TextAnchor.UpperLeft, teammateInnerWidth);
                UiFactory.CreateAutoText(teammateArea, "Season teammate head to head",
                    "Head-to-head race finishes: " + archive.teammateHeadToHeadWins + "-" + archive.teammateHeadToHeadLosses,
                    14, UiFactory.TextMuted, TextAnchor.UpperLeft, teammateInnerWidth);
                int pointsCeiling = Mathf.Max(1, Mathf.Max(archive.playerPoints, archive.teammatePoints));
                UiFactory.CreateComparisonBar(teammateArea, "Points", archive.playerPoints, UiFactory.Accent, archive.teammatePoints, UiFactory.AccentCyan, pointsCeiling, teammateInnerWidth);
                int h2hCeiling = Mathf.Max(1, archive.teammateHeadToHeadWins + archive.teammateHeadToHeadLosses);
                UiFactory.CreateComparisonBar(teammateArea, "Head-to-Head Wins", archive.teammateHeadToHeadWins, UiFactory.Accent, archive.teammateHeadToHeadLosses, UiFactory.AccentCyan, h2hCeiling, teammateInnerWidth);
            }
            else
            {
                BuildEmptyState(content, "No teammate recorded this season.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Team Season Summary");
            RectTransform teamRow = UiFactory.CreateAutoHeightRow(content, "Season team summary row", ReportContentWidth, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(teamRow, "Team", archive.playerTeamName, 260f);
            UiFactory.CreateStatCard(teamRow, "Final Position", "P" + archive.playerTeamFinalPosition, 220f);
            UiFactory.CreateStatCard(teamRow, "Points", archive.playerTeamPoints.ToString(), 220f);

            UiFactory.CreateSubHeader(content, "Race Control Summary");
            RectTransform raceControlRow = UiFactory.CreateAutoHeightRow(content, "Season race control row", ReportContentWidth, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(raceControlRow, "Red Flags", archive.redFlagCount.ToString(), 220f);
            UiFactory.CreateStatCard(raceControlRow, "Safety Cars", archive.safetyCarCount.ToString(), 220f);
            UiFactory.CreateStatCard(raceControlRow, "Major Incidents", archive.majorIncidentCount.ToString(), 220f);

            UiFactory.CreateSubHeader(content, "Race Highlights");
            RectTransform highlightsRow = UiFactory.CreateAutoHeightRow(content, "Season race highlights row", ReportContentWidth, 14, new RectOffset(0, 0, 0, 0));
            bool anyHighlight = false;
            float highlightCardWidth = 290f;
            float highlightInnerWidth = highlightCardWidth - 34f;
            if (!string.IsNullOrEmpty(archive.bestRaceEventName) && archive.bestRaceFinish > 0)
            {
                RectTransform bestArea;
                UiFactory.CreateAutoCard(highlightsRow, "Season best race", "Best Race", UiFactory.AccentGreen, highlightCardWidth, out bestArea);
                UiFactory.CreateAutoText(bestArea, "Season best race text",
                    "P" + archive.bestRaceFinish + " at " + archive.bestRaceEventName + (archive.bestRaceGridPosition > 0 ? " (started P" + archive.bestRaceGridPosition + ")" : ""),
                    14, UiFactory.TextPrimary, TextAnchor.UpperLeft, highlightInnerWidth);
                anyHighlight = true;
            }

            if (!string.IsNullOrEmpty(archive.worstRaceEventName) && archive.worstRaceFinish > 0)
            {
                RectTransform worstArea;
                UiFactory.CreateAutoCard(highlightsRow, "Season worst race", "Worst Race", UiFactory.Accent, highlightCardWidth, out worstArea);
                UiFactory.CreateAutoText(worstArea, "Season worst race text", "P" + archive.worstRaceFinish + " at " + archive.worstRaceEventName,
                    14, UiFactory.TextPrimary, TextAnchor.UpperLeft, highlightInnerWidth);
                anyHighlight = true;
            }

            if (!string.IsNullOrEmpty(archive.biggestComebackEventName) && archive.biggestComebackPositionsGained > 0)
            {
                RectTransform comebackArea;
                UiFactory.CreateAutoCard(highlightsRow, "Season comeback", "Biggest Comeback", UiFactory.AccentCyan, highlightCardWidth, out comebackArea);
                UiFactory.CreateAutoText(comebackArea, "Season comeback text",
                    "+" + archive.biggestComebackPositionsGained + " places at " + archive.biggestComebackEventName,
                    14, UiFactory.TextPrimary, TextAnchor.UpperLeft, highlightInnerWidth);
                anyHighlight = true;
            }

            if (!string.IsNullOrEmpty(archive.biggestCollapseEventName) && archive.biggestCollapsePositionsLost > 0)
            {
                RectTransform collapseArea;
                UiFactory.CreateAutoCard(highlightsRow, "Season collapse", "Biggest Collapse", UiFactory.AccentAmber, highlightCardWidth, out collapseArea);
                UiFactory.CreateAutoText(collapseArea, "Season collapse text",
                    "-" + archive.biggestCollapsePositionsLost + " places at " + archive.biggestCollapseEventName,
                    14, UiFactory.TextPrimary, TextAnchor.UpperLeft, highlightInnerWidth);
                anyHighlight = true;
            }

            if (!anyHighlight)
            {
                BuildEmptyState(content, "No standout races recorded this season.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Rivalry");
            if (!string.IsNullOrEmpty(archive.rivalName))
            {
                RectTransform rivalryArea;
                UiFactory.CreateAutoCard(content, "Season rivalry", "", UiFactory.AccentPurple, ReportContentWidth, out rivalryArea);
                UiFactory.CreateAutoText(rivalryArea, "Season rivalry text",
                    "Rivalry with " + archive.rivalName + ": " + archive.rivalRaceWins + " wins - " + archive.rivalRaceLosses + " losses in head-to-head races.",
                    15, UiFactory.TextPrimary, TextAnchor.UpperLeft, ReportContentWidth - 34f);
            }
            else
            {
                BuildEmptyState(content, "No rivalry recorded this season.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Season Form");
            if (archive.formSummary != null && archive.formSummary.Count > 0)
            {
                RectTransform formRow = UiFactory.CreateRect(content, "Season form row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                float formHeight = UiFactory.CreateWrappingChipRow(formRow, "Season form chips", archive.formSummary, UiFactory.AccentAmber, 0f, 0f, ReportContentWidth);
                UiFactory.SetSize(formRow, ReportContentWidth, formHeight);
            }
            else
            {
                BuildEmptyState(content, "No form data recorded this season.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Upgrade Effectiveness");
            if (archive.upgradesCompleted != null && archive.upgradesCompleted.Count > 0)
            {
                RectTransform upgradeArea;
                UiFactory.CreateAutoCard(content, "Season upgrades", "", UiFactory.AccentCyan, ReportContentWidth, out upgradeArea);
                float upgradeInnerWidth = ReportContentWidth - 34f;
                for (int i = 0; i < archive.upgradesCompleted.Count; i++)
                {
                    UpgradeData upgrade = data.Upgrades.upgrades.Find(u => u.id == archive.upgradesCompleted[i]);
                    string line = upgrade != null ? upgrade.displayName + "  " + BuildUpgradeDeltaSummary(upgrade) : archive.upgradesCompleted[i];
                    UiFactory.CreateAutoText(upgradeArea, "Season upgrade " + i, line, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, upgradeInnerWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "No upgrades completed this season.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Reputation");
            List<SeasonArchive> allArchives = career.GetSeasonArchives();
            SeasonArchive previousArchive = allArchives != null ? allArchives.Find(a => a.season == archive.season - 1) : null;
            RectTransform reputationRow = UiFactory.CreateAutoHeightRow(content, "Season reputation row", ReportContentWidth, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateStatCard(reputationRow, "Reputation", archive.reputationAtSeasonEnd.ToString(), 220f);
            if (previousArchive != null)
            {
                int reputationDelta = archive.reputationAtSeasonEnd - previousArchive.reputationAtSeasonEnd;
                string reputationDeltaText = reputationDelta == 0 ? "No change" : (reputationDelta > 0 ? "+" + reputationDelta : reputationDelta.ToString());
                UiFactory.CreateStatCard(reputationRow, "Change vs Last Season", reputationDeltaText, 260f);
            }

            UiFactory.CreateSubHeader(content, "Regulation Changes In Effect");
            if (archive.regulations != null && archive.regulations.Count > 0)
            {
                for (int i = 0; i < archive.regulations.Count; i++)
                {
                    RegulationChange change = archive.regulations[i];
                    RectTransform regulationArea;
                    UiFactory.CreateAutoCard(content, "Season regulation " + i, change.title, change.magnitude >= 0f ? UiFactory.AccentGreen : UiFactory.AccentAmber, ReportContentWidth, out regulationArea);
                    float regulationInnerWidth = ReportContentWidth - 34f;
                    UiFactory.CreateAutoText(regulationArea, "Season regulation description " + i, change.description, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, regulationInnerWidth);
                    UiFactory.CreateAutoText(regulationArea, "Season regulation impact " + i, "Helps: " + change.beneficiaryHint + "   ·   Hurts: " + change.loserHint, 13, UiFactory.TextMuted, TextAnchor.UpperLeft, regulationInnerWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "No regulation changes recorded for this season.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Team Performance Changes");
            if (archive.teamPerformanceChanges != null && archive.teamPerformanceChanges.Count > 0)
            {
                RectTransform teamChangeArea;
                UiFactory.CreateAutoCard(content, "Season team performance", "", UiFactory.AccentCyan, ReportContentWidth, out teamChangeArea);
                float teamChangeInnerWidth = ReportContentWidth - 34f;
                for (int i = 0; i < archive.teamPerformanceChanges.Count; i++)
                {
                    TeamPerformanceModifier modifier = archive.teamPerformanceChanges[i];
                    TeamData modifierTeam = data.FindTeam(modifier.teamId);
                    string modifierName = modifierTeam != null ? modifierTeam.name : modifier.teamId;
                    string modifierSign = modifier.performanceDelta >= 0f ? "+" : "";
                    UiFactory.CreateAutoText(teamChangeArea, "Season team change " + i,
                        modifierName + "  " + modifierSign + (modifier.performanceDelta * 100f).ToString("0.0") + "%  -  " + modifier.trendLabel,
                        14, modifier.performanceDelta >= 0f ? UiFactory.AccentGreen : UiFactory.Accent, TextAnchor.UpperLeft, teamChangeInnerWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "No team performance changes recorded.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Season Awards");
            if (archive.awards != null && archive.awards.Count > 0)
            {
                for (int i = 0; i < archive.awards.Count; i++)
                {
                    SeasonAward award = archive.awards[i];
                    RectTransform awardArea;
                    UiFactory.CreateAutoCard(content, "Season award " + i, award.title, UiFactory.AccentAmber, ReportContentWidth, out awardArea);
                    float awardInnerWidth = ReportContentWidth - 34f;
                    UiFactory.CreateAutoText(awardArea, "Season award winner " + i, award.winnerName, 18, UiFactory.TextPrimary, TextAnchor.UpperLeft, awardInnerWidth);
                    UiFactory.CreateAutoText(awardArea, "Season award detail " + i, award.detail, 14, UiFactory.TextMuted, TextAnchor.UpperLeft, awardInnerWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "No season awards recorded.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Key Headlines");
            if (archive.keyHeadlines != null && archive.keyHeadlines.Count > 0)
            {
                for (int i = 0; i < archive.keyHeadlines.Count; i++)
                {
                    UiFactory.CreateAutoText(content, "Season headline " + i, "• " + archive.keyHeadlines[i], 14, UiFactory.TextMuted, TextAnchor.UpperLeft, ReportContentWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "No headlines recorded for this season.", ReportContentWidth);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => bootstrap.ShowMainMenu());
            if (isFreshCompletion)
            {
                UiFactory.CreatePrimaryButton(footerRight, "Continue to Offseason", () =>
                {
                    career.AdvanceToOffseason();
                    ShowOffseasonHub(data, career, settings);
                });
            }
            else
            {
                UiFactory.CreatePrimaryButton(footerRight, "Back to Season Archive", () => ShowSeasonArchive(data, career, settings));
            }
        }

        // "Shape of the season" mini WDC/WCC progression charts, reusing
        // BuildChampionshipChart/AddChampionshipMiniChartCaption exactly as
        // ShowChampionshipGraphs' Combined tab does, just fixed-height rows
        // instead of fractional full-screen anchors so they sit naturally
        // inside this screen's scrolling content list. Non-interactive (no
        // tap-for-detail state to persist here) - passes a null callback,
        // which BuildChampionshipChart already treats as "no-op on tap".
        void BuildSeasonReviewGraphSection(RectTransform content, CareerManager career, SeasonArchive archive)
        {
            UiFactory.CreateSubHeader(content, "Season Shape");
            CareerManager.ChampionshipProgression driverProgression = career.GetDriverChampionshipProgression(archive.season);
            CareerManager.ChampionshipProgression constructorProgression = career.GetConstructorChampionshipProgression(archive.season);

            RectTransform wdcChartRow = UiFactory.CreateRect(content, "Season review WDC chart row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.SetSize(wdcChartRow, ReportContentWidth, 280f);
            RectTransform wdcPlot = UiFactory.CreateChartPlotArea(wdcChartRow, "Season review WDC plot", new Vector2(0f, 0f), new Vector2(0.7f, 1f), Vector2.zero, Vector2.zero);
            RectTransform wdcLegend = UiFactory.CreateScrollPanel(wdcChartRow, "Season review WDC legend", new Vector2(0.72f, 0f), new Vector2(1f, 1f), 6, new RectOffset(8, 8, 8, 8));
            BuildChampionshipChart(wdcPlot, wdcLegend, driverProgression, archive.teammateId, false, "", -1, null);
            AddChampionshipMiniChartCaption(wdcPlot, "WDC - DRIVERS");

            RectTransform wccChartRow = UiFactory.CreateRect(content, "Season review WCC chart row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.SetSize(wccChartRow, ReportContentWidth, 280f);
            RectTransform wccPlot = UiFactory.CreateChartPlotArea(wccChartRow, "Season review WCC plot", new Vector2(0f, 0f), new Vector2(0.7f, 1f), Vector2.zero, Vector2.zero);
            RectTransform wccLegend = UiFactory.CreateScrollPanel(wccChartRow, "Season review WCC legend", new Vector2(0.72f, 0f), new Vector2(1f, 1f), 6, new RectOffset(8, 8, 8, 8));
            BuildChampionshipChart(wccPlot, wccLegend, constructorProgression, "", false, "", -1, null);
            AddChampionshipMiniChartCaption(wccPlot, "WCC - CONSTRUCTORS");
        }

        // Sums an UpgradeData's nonzero stat deltas into a short "(+3 Top
        // Speed, +1 Reliability)" clause for the Season Review's upgrade
        // effectiveness list - skips every stat the upgrade didn't touch.
        string BuildUpgradeDeltaSummary(UpgradeData upgrade)
        {
            List<string> parts = new List<string>();
            if (upgrade.topSpeedDelta != 0) parts.Add((upgrade.topSpeedDelta > 0 ? "+" : "") + upgrade.topSpeedDelta + " Top Speed");
            if (upgrade.accelerationDelta != 0) parts.Add((upgrade.accelerationDelta > 0 ? "+" : "") + upgrade.accelerationDelta + " Acceleration");
            if (upgrade.corneringDelta != 0) parts.Add((upgrade.corneringDelta > 0 ? "+" : "") + upgrade.corneringDelta + " Cornering");
            if (upgrade.brakingDelta != 0) parts.Add((upgrade.brakingDelta > 0 ? "+" : "") + upgrade.brakingDelta + " Braking");
            if (upgrade.reliabilityDelta != 0) parts.Add((upgrade.reliabilityDelta > 0 ? "+" : "") + upgrade.reliabilityDelta + " Reliability");
            if (upgrade.ersDelta != 0) parts.Add((upgrade.ersDelta > 0 ? "+" : "") + upgrade.ersDelta + " ERS");
            if (upgrade.tyreDelta != 0) parts.Add((upgrade.tyreDelta > 0 ? "+" : "") + upgrade.tyreDelta + " Tyre Management");
            if (upgrade.aeroDelta != 0) parts.Add((upgrade.aeroDelta > 0 ? "+" : "") + upgrade.aeroDelta + " Aero");
            if (upgrade.chassisDelta != 0) parts.Add((upgrade.chassisDelta > 0 ? "+" : "") + upgrade.chassisDelta + " Chassis");
            if (upgrade.engineDelta != 0) parts.Add((upgrade.engineDelta > 0 ? "+" : "") + upgrade.engineDelta + " Engine");
            return parts.Count > 0 ? "(" + string.Join(", ", parts.ToArray()) + ")" : "";
        }

        // Part 21: every completed season as a browsable card, most recent
        // first - the only place a player can look back at an old season's
        // ShowSeasonReview once they've moved past it.
        public void ShowSeasonArchive(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Season archive background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "Season Archive", "Every completed season of your career, most recent first.");

            RectTransform content = UiFactory.CreateScrollPanel(background, "Season archive list", new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f), 16, new RectOffset(10, 10, 16, 16));

            List<SeasonArchive> archives = career.GetSeasonArchives();
            if (archives == null || archives.Count == 0)
            {
                BuildEmptyState(content, "No completed seasons yet - finish your first full season to see it archived here.", ReportContentWidth);
            }
            else
            {
                for (int i = archives.Count - 1; i >= 0; i--)
                {
                    BuildSeasonArchiveCard(content, data, career, settings, archives[i]);
                }
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
        }

        // One archived-season card: headline facts plus an explicit "View
        // Season Review" button (same "card + explicit action button" shape
        // as BuildQuickRaceTrackRow, rather than making the whole card itself
        // a giant click target that could be triggered by an accidental tap
        // while scrolling this list).
        void BuildSeasonArchiveCard(RectTransform content, GameDataRepository data, CareerManager career, GameSettingsStore settings, SeasonArchive archive)
        {
            RectTransform card = UiFactory.CreateRect(content, "Season archive card " + archive.season, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.SetSize(card, ReportContentWidth, 132f);
            Image cardBackground = card.gameObject.AddComponent<Image>();
            cardBackground.color = UiFactory.PanelDark;
            UiFactory.CreateBand(card, "Season archive card rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.Accent);

            Text titleText = UiFactory.CreateText(card, "Season archive card title", "SEASON " + archive.season, 20, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            titleText.fontStyle = FontStyle.Bold;
            SetTopLeft(titleText.rectTransform, 20f, 14f);
            UiFactory.SetSize(titleText, 300f, 26f);

            Text championsText = UiFactory.CreateText(card, "Season archive card champions",
                "WDC: " + archive.driverChampionName + "   ·   WCC: " + archive.constructorChampionName,
                14, UiFactory.AccentCyan, TextAnchor.UpperLeft);
            SetTopLeft(championsText.rectTransform, 20f, 46f);
            UiFactory.SetSize(championsText, 760f, 20f);

            Text playerText = UiFactory.CreateText(card, "Season archive card player",
                archive.playerDriverName + " finished P" + archive.playerFinalPosition + " (" + archive.playerPoints + " pts, " + archive.playerWins + " wins, " + archive.playerPodiums + " podiums)   ·   " +
                archive.playerTeamName + " P" + archive.playerTeamFinalPosition,
                14, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(playerText.rectTransform, 20f, 70f);
            UiFactory.SetSize(playerText, 900f, 20f);

            string bestRaceLine = !string.IsNullOrEmpty(archive.bestRaceEventName) ? "Best race: P" + archive.bestRaceFinish + " at " + archive.bestRaceEventName : "Best race: --";
            Text bestText = UiFactory.CreateText(card, "Season archive card best", bestRaceLine, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetTopLeft(bestText.rectTransform, 20f, 94f);
            UiFactory.SetSize(bestText, 760f, 20f);

            Button openButton = UiFactory.CreatePrimaryButton(card, "View Season Review", () => ShowSeasonReview(data, career, settings, archive, false));
            UiFactory.SetSize(openButton, 220f, 44f);
            RectTransform openRect = openButton.GetComponent<RectTransform>();
            openRect.anchorMin = new Vector2(1f, 0.5f);
            openRect.anchorMax = new Vector2(1f, 0.5f);
            openRect.pivot = new Vector2(1f, 0.5f);
            openRect.anchoredPosition = new Vector2(-20f, 0f);
        }

        // Part 21: the offseason narrative screen - reachable only right after
        // a fresh season completion (see ShowSeasonReview's "Continue to
        // Offseason" button), explaining what changed for the new season
        // before pre-season testing. Every list here reads Save fields
        // already filtered/tagged to career.Save.currentSeason, which
        // BeginNewSeason has already advanced to by the time this screen can
        // be reached.
        public void ShowOffseasonHub(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            UiFactory.ApplyUiScale(canvas, settings.UiScale);
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Offseason background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "New Season Begins",
                "Season " + career.Save.currentSeason + " - regulations, team evolution and the contract picture for the year ahead.");

            RectTransform content = UiFactory.CreateScrollPanel(background, "Offseason report", new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f), 20, new RectOffset(6, 6, 16, 16));
            float innerWidth = ReportContentWidth - 34f;

            UiFactory.CreateSubHeader(content, "Season " + career.Save.currentSeason + " Calendar");
            if (data.Calendar != null && data.Calendar.events != null && data.Calendar.events.Count > 0)
            {
                RectTransform calendarArea;
                UiFactory.CreateAutoCard(content, "Offseason calendar", "", UiFactory.AccentCyan, ReportContentWidth, out calendarArea);
                for (int i = 0; i < data.Calendar.events.Count; i++)
                {
                    CalendarEventData raceEvent = data.Calendar.events[i];
                    UiFactory.CreateAutoText(calendarArea, "Offseason calendar row " + i,
                        "R" + raceEvent.round.ToString("00") + "  " + raceEvent.displayName + "   ·   " + raceEvent.country,
                        14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "Calendar unavailable.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Regulation Changes For Season " + career.Save.currentSeason);
            List<RegulationChange> newRegulations = career.Save.regulationChanges.FindAll(r => r.season == career.Save.currentSeason);
            if (newRegulations.Count > 0)
            {
                for (int i = 0; i < newRegulations.Count; i++)
                {
                    RegulationChange change = newRegulations[i];
                    RectTransform regulationArea;
                    UiFactory.CreateAutoCard(content, "Offseason regulation " + i, change.title, change.magnitude >= 0f ? UiFactory.AccentGreen : UiFactory.AccentAmber, ReportContentWidth, out regulationArea);
                    UiFactory.CreateAutoText(regulationArea, "Offseason regulation description " + i, change.description, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
                    UiFactory.CreateAutoText(regulationArea, "Offseason regulation impact " + i, "Helps: " + change.beneficiaryHint + "   ·   Hurts: " + change.loserHint, 13, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "No regulation changes for this season.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Team Performance Evolution");
            List<TeamPerformanceModifier> seasonModifiers = career.Save.teamPerformanceModifiers.FindAll(m => m.season == career.Save.currentSeason);
            seasonModifiers.Sort((a, b) => b.performanceDelta.CompareTo(a.performanceDelta));
            if (seasonModifiers.Count > 0)
            {
                float halfWidth = (ReportContentWidth - 16f) / 2f;
                float halfInnerWidth = halfWidth - 34f;
                RectTransform evolutionRow = UiFactory.CreateAutoHeightRow(content, "Offseason evolution row", ReportContentWidth, 16, new RectOffset(0, 0, 0, 0));

                RectTransform gainersArea;
                UiFactory.CreateAutoCard(evolutionRow, "Offseason gainers", "Biggest Gainers", UiFactory.AccentGreen, halfWidth, out gainersArea);
                int gainerCount = Mathf.Min(3, seasonModifiers.Count);
                for (int i = 0; i < gainerCount; i++)
                {
                    TeamPerformanceModifier modifier = seasonModifiers[i];
                    TeamData modifierTeam = data.FindTeam(modifier.teamId);
                    UiFactory.CreateAutoText(gainersArea, "Offseason gainer " + i,
                        (modifierTeam != null ? modifierTeam.name : modifier.teamId) + "  +" + (modifier.performanceDelta * 100f).ToString("0.0") + "%",
                        14, UiFactory.TextPrimary, TextAnchor.UpperLeft, halfInnerWidth);
                }

                RectTransform losersArea;
                UiFactory.CreateAutoCard(evolutionRow, "Offseason losers", "Biggest Losers", UiFactory.Accent, halfWidth, out losersArea);
                int loserCount = Mathf.Min(3, seasonModifiers.Count);
                for (int i = 0; i < loserCount; i++)
                {
                    TeamPerformanceModifier modifier = seasonModifiers[seasonModifiers.Count - 1 - i];
                    TeamData modifierTeam = data.FindTeam(modifier.teamId);
                    UiFactory.CreateAutoText(losersArea, "Offseason loser " + i,
                        (modifierTeam != null ? modifierTeam.name : modifier.teamId) + "  " + (modifier.performanceDelta * 100f).ToString("0.0") + "%",
                        14, UiFactory.TextPrimary, TextAnchor.UpperLeft, halfInnerWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "No team performance changes recorded.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Contract & Seat");
            TeamData playerTeam = data.FindTeam(career.Save.playerTeamId);
            List<NewsArticle> seasonNews = career.Save.newsArticles.FindAll(a => a.season == career.Save.currentSeason);
            NewsArticle rumourArticle = seasonNews.Find(a => a.category == CareerManager.NewsCategoryRumour);
            string reactionLine = rumourArticle != null ? rumourArticle.body
                : career.Save.playerDriverName + " stays with " + (playerTeam != null ? playerTeam.name : career.Save.playerTeamId) + " for Season " + career.Save.currentSeason + ".";
            RectTransform contractArea;
            UiFactory.CreateAutoCard(content, "Offseason contract", "", UiFactory.AccentPurple, ReportContentWidth, out contractArea);
            UiFactory.CreateAutoText(contractArea, "Offseason contract team",
                career.Save.playerDriverName + " remains with " + (playerTeam != null ? playerTeam.name : career.Save.playerTeamId),
                16, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(contractArea, "Offseason contract target",
                "New target: P" + career.Save.contractTargetPosition + " or better in Season " + career.Save.currentSeason,
                14, UiFactory.AccentGreen, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(contractArea, "Offseason contract reaction", reactionLine, 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);

            UiFactory.CreateSubHeader(content, "Season " + career.Save.currentSeason + " Objectives");
            if (career.Save.seasonObjectives != null && career.Save.seasonObjectives.Count > 0)
            {
                RectTransform objectivesArea;
                UiFactory.CreateAutoCard(content, "Offseason objectives", "", UiFactory.AccentAmber, ReportContentWidth, out objectivesArea);
                for (int i = 0; i < career.Save.seasonObjectives.Count; i++)
                {
                    SeasonObjective objective = career.Save.seasonObjectives[i];
                    UiFactory.CreateAutoText(objectivesArea, "Offseason objective " + i,
                        (objective.teamObjective ? "[Team] " : "[Driver] ") + objective.description,
                        14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "No objectives set yet.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Off-Season News");
            if (seasonNews.Count > 0)
            {
                int newsCount = Mathf.Min(5, seasonNews.Count);
                for (int i = seasonNews.Count - 1; i >= seasonNews.Count - newsCount; i--)
                {
                    NewsArticle article = seasonNews[i];
                    RectTransform newsArea;
                    UiFactory.CreateAutoCard(content, "Offseason news " + i, article.headline, NewsCategoryColor(article.category), ReportContentWidth, out newsArea);
                    UiFactory.CreateAutoText(newsArea, "Offseason news body " + i, article.body, 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "No news yet this season.", ReportContentWidth);
            }

            UiFactory.CreateSubHeader(content, "Rivalry");
            DriverData rival = string.IsNullOrEmpty(career.Save.rivalDriverId) ? null : data.FindDriver(career.Save.rivalDriverId);
            if (rival != null)
            {
                SeasonArchive lastArchive = career.GetLatestSeasonArchive();
                bool sameRival = lastArchive != null && lastArchive.rivalId == career.Save.rivalDriverId;
                string rivalryLine = sameRival
                    ? rival.displayName + " remains your closest rival heading into Season " + career.Save.currentSeason + "."
                    : "A new rival emerges for Season " + career.Save.currentSeason + ": " + rival.displayName + ".";
                UiFactory.CreateAutoText(content, "Offseason rivalry text", rivalryLine, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, ReportContentWidth);
            }
            else
            {
                BuildEmptyState(content, "No rival identified yet.", ReportContentWidth);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => bootstrap.ShowMainMenu());
            UiFactory.CreatePrimaryButton(footerRight, "Begin Pre-Season Testing", () =>
            {
                career.AdvanceToPreseason();
                ShowPreSeasonTesting(data, career, settings);
            });
        }

        // Part 21: on-demand pre-season testing report - GeneratePreSeasonTestingReport
        // is called fresh here (not persisted, same convention as
        // GenerateRaceWeekendBriefing), so it always reflects whatever this
        // season's team-performance modifiers/regulations just generated.
        public void ShowPreSeasonTesting(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            UiFactory.ApplyUiScale(canvas, settings.UiScale);
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Pre-season testing background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "Pre-Season Testing",
                "Season " + career.Save.currentSeason + " testing form before the lights go out on Round 1.");

            PreSeasonTestingReport report = career.GeneratePreSeasonTestingReport();
            RectTransform content = UiFactory.CreateScrollPanel(background, "Pre-season testing report", new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f), 20, new RectOffset(6, 6, 16, 16));
            float innerWidth = ReportContentWidth - 34f;

            UiFactory.CreateSubHeader(content, "Testing Pace Rankings");
            RectTransform headerRow = UiFactory.CreateTableRow(content, "Testing header row", ReportContentWidth, 28f, false, 1);
            headerRow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            UiFactory.AddRowCell(headerRow, "H pos", "POS", 0.0f, 0.06f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H team", "TEAM", 0.11f, 0.55f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H pace", "PACE RATING", 0.55f, 0.75f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H reliability", "RELIABILITY", 0.75f, 1f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);

            for (int i = 0; i < report.paceRanking.Count; i++)
            {
                PreSeasonTestingEntry entry = report.paceRanking[i];
                bool isPlayerTeam = entry.teamId == career.Save.playerTeamId;
                RectTransform row = UiFactory.CreateTableRow(content, "Testing row " + i, ReportContentWidth, 34f, isPlayerTeam, i);
                UiFactory.AddPositionBadge(row, i + 1, isPlayerTeam);
                Color textColor = isPlayerTeam ? Color.white : new Color(0.9f, 0.95f, 0.98f);
                UiFactory.AddRowCell(row, "Team", entry.teamName, 0.11f, 0.55f, 15, textColor, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Pace", entry.paceRating.ToString("0.0"), 0.55f, 0.75f, 14, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Reliability", entry.reliabilityNote, 0.75f, 1f, 13, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            }

            UiFactory.CreateSubHeader(content, "Expectations");
            RectTransform expectationsArea;
            UiFactory.CreateAutoCard(content, "Testing expectations", "", UiFactory.AccentCyan, ReportContentWidth, out expectationsArea);
            UiFactory.CreateAutoText(expectationsArea, "Testing expectation text", report.playerExpectationText, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(expectationsArea, "Testing teammate text", report.teammateBenchmarkText, 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);

            UiFactory.CreateSubHeader(content, "Tyre Degradation Preview");
            RectTransform tyreArea;
            UiFactory.CreateAutoCard(content, "Testing tyre", "", UiFactory.AccentAmber, ReportContentWidth, out tyreArea);
            UiFactory.CreateAutoText(tyreArea, "Testing tyre text", report.tyreDegradationText, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);

            UiFactory.CreateSubHeader(content, "Upgrade Recommendations");
            if (report.upgradeRecommendations != null && report.upgradeRecommendations.Count > 0)
            {
                RectTransform recommendationsArea;
                UiFactory.CreateAutoCard(content, "Testing recommendations", "", UiFactory.AccentGreen, ReportContentWidth, out recommendationsArea);
                for (int i = 0; i < report.upgradeRecommendations.Count; i++)
                {
                    UiFactory.CreateAutoText(recommendationsArea, "Testing recommendation " + i, "• " + report.upgradeRecommendations[i], 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
                }
            }
            else
            {
                BuildEmptyState(content, "No upgrade recommendations available.", ReportContentWidth);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => bootstrap.ShowMainMenu());
            UiFactory.CreatePrimaryButton(footerRight, "Continue to Round 1", () =>
            {
                career.ConfirmNewSeasonSetup();
                bootstrap.ShowCareer();
            });
        }

        // Section 1: podium / top-3. Centered, P1 clearly emphasized (bigger
        // avatar/medal/name via UiFactory.CreatePodiumCard), P2/P3 aligned to a
        // common baseline via the row's LowerCenter alignment.
        void BuildPodiumSection(RectTransform content, RaceManager race, List<RaceResultEntry> results)
        {
            if (results == null || results.Count == 0)
            {
                return;
            }

            UiFactory.CreateSubHeader(content, "Race Result");
            RectTransform podiumRow = UiFactory.CreateAutoHeightRow(content, "Podium row", ReportContentWidth, 24, new RectOffset(0, 0, 0, 0));
            podiumRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerCenter;
            if (results.Count > 1)
            {
                BuildPodiumSlot(podiumRow, race, results, 1);
            }

            BuildPodiumSlot(podiumRow, race, results, 0);
            if (results.Count > 2)
            {
                BuildPodiumSlot(podiumRow, race, results, 2);
            }
        }

        // Section 2: player result summary - race-wide highlight cards (fastest
        // lap / biggest mover / player finish / flags-and-safety-cars), a "Your
        // Race" card, and a short generated Race Story paragraph tying the
        // result, teammate, race-control context, strategy and any penalty/DNF
        // together in plain language.
        void BuildPlayerSummarySection(RectTransform content, RaceManager race, List<RaceResultEntry> results, RaceResultEntry player)
        {
            if (results == null || results.Count == 0)
            {
                return;
            }

            UiFactory.CreateSubHeader(content, "Player Result Summary");

            RectTransform highlights = UiFactory.CreateAutoHeightRow(content, "Result highlights", ReportContentWidth, 14, new RectOffset(0, 0, 0, 0));
            RaceResultEntry fastest = FindFastestLap(results);
            if (fastest != null)
            {
                UiFactory.CreateStatCard(highlights, "Fastest Lap", fastest.driverName + "  " + UiFactory.FormatTime(fastest.bestLapTime), 300f);
            }

            RaceResultEntry mover = FindBiggestMover(results);
            if (mover != null)
            {
                UiFactory.CreateStatCard(highlights, "Biggest Mover", mover.driverName + "  +" + (mover.gridPosition - mover.finishingPosition), 280f);
            }

            if (player != null)
            {
                UiFactory.CreateStatCard(highlights, "You Finished", "P" + player.finishingPosition + "  (" + player.points + " pts)", 260f);
            }

            if (race != null)
            {
                // Reworded from the old "N SC/VSC - M incidents" line, which
                // conflated a genuine race-control count with race.IncidentCount
                // (a raw, uncapped internal detector tick that could read in the
                // hundreds and meant nothing to a player). Every number here now
                // comes from the RaceControlHistory-backed counters, so it always
                // agrees with the Race Control Timeline and Incidents section
                // below.
                string flagsSummary = race.YellowFlagEventCount + " yellow · " + race.VirtualSafetyCarEventCount + " VSC · " + race.SafetyCarDeploymentCount + " SC";
                UiFactory.CreateStatCard(highlights, "Flags & Safety Cars", flagsSummary, 300f);
            }

            if (player == null)
            {
                return;
            }

            RaceResultEntry winner = results[0];
            float winnerTime = winner.totalTime + winner.penaltiesSeconds;
            float playerTime = player.totalTime + player.penaltiesSeconds;
            int positionsChanged = player.gridPosition > 0 ? player.gridPosition - player.finishingPosition : 0;
            Color positionsColor = positionsChanged > 0 ? UiFactory.AccentGreen : (positionsChanged < 0 ? UiFactory.Accent : UiFactory.TextMuted);
            string positionsLine = positionsChanged == 0 ? "No positions changed"
                : (positionsChanged > 0 ? "+" + positionsChanged + " positions gained" : positionsChanged + " positions lost");

            RectTransform yourRaceContentArea;
            UiFactory.CreateAutoCard(content, "Your race", "Your Race", UiFactory.Accent, ReportContentWidth, out yourRaceContentArea);
            float innerWidth = ReportContentWidth - 34f;
            UiFactory.CreateAutoText(yourRaceContentArea, "Your race grid", "Grid P" + (player.gridPosition > 0 ? player.gridPosition.ToString() : "-") + " -> Finish P" + player.finishingPosition, 15, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(yourRaceContentArea, "Your race positions", positionsLine, 14, positionsColor, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(yourRaceContentArea, "Your race gap", "Gap to winner: " + (player.finishingPosition == 1 ? "--" : "+" + Mathf.Max(0f, playerTime - winnerTime).ToString("0.0") + "s"), 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(yourRaceContentArea, "Your race overtakes", "Overtakes made: " + player.overtakesMade, 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(yourRaceContentArea, "Your race best lap", "Best lap: " + UiFactory.FormatTime(player.bestLapTime), 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);

            RaceResultEntry teammate = results.Find(entry => entry.teamId == player.teamId && entry.driverId != player.driverId);
            string story = BuildRaceStoryText(race, player, teammate);
            if (!string.IsNullOrEmpty(story))
            {
                RectTransform storyContentArea;
                UiFactory.CreateAutoCard(content, "Race story", "Race Story", UiFactory.AccentCyan, ReportContentWidth, out storyContentArea);
                UiFactory.CreateAutoText(storyContentArea, "Race story text", story, 14, new Color(0.88f, 0.93f, 0.97f), TextAnchor.UpperLeft, innerWidth);
            }
        }

        // Generates the 2-4 sentence Race Story paragraph from real fields only -
        // no placeholder text for anything unavailable, that clause is simply
        // omitted. Mentions start->finish, the teammate's finish, safety-car/red-
        // flag context (from the RaceControlHistory-backed counters, never the
        // raw incident tick), strategy, and any penalty/DNF affecting the player.
        string BuildRaceStoryText(RaceManager race, RaceResultEntry player, RaceResultEntry teammate)
        {
            if (player == null)
            {
                return "";
            }

            bool dnf = !string.IsNullOrEmpty(player.penaltyReason) && player.penaltyReason.Contains("DNF");
            string gridText = player.gridPosition > 0 ? "P" + player.gridPosition : "the back of the field";
            List<string> sentences = new List<string>();

            if (dnf)
            {
                sentences.Add("Starting from " + gridText + ", " + player.driverName + " didn't see the chequered flag (" + DnfReasonLabel(player.penaltyReason).ToLowerInvariant() + ").");
            }
            else
            {
                int gained = player.gridPosition > 0 ? player.gridPosition - player.finishingPosition : 0;
                string moveText = gained > 0 ? ", gaining " + gained + " place" + (gained == 1 ? "" : "s") + " along the way"
                    : gained < 0 ? ", losing " + (-gained) + " place" + (gained == -1 ? "" : "s") + " along the way"
                    : "";
                sentences.Add("Started " + gridText + " and brought it home P" + player.finishingPosition + moveText + ".");
            }

            if (teammate != null)
            {
                bool teammateAhead = teammate.finishingPosition < player.finishingPosition;
                sentences.Add(teammate.driverName + " finished P" + teammate.finishingPosition + (teammateAhead ? ", ahead of their teammate." : "."));
            }

            if (race != null)
            {
                if (race.RedFlagCount > 0)
                {
                    sentences.Add("The race was stopped with a red flag" + (string.IsNullOrEmpty(race.RedFlagReason) ? "." : " for " + race.RedFlagReason.ToLowerInvariant() + "."));
                }
                else if (race.SafetyCarDeploymentCount > 0)
                {
                    string vscClause = race.VirtualSafetyCarEventCount > 0 ? " and " + race.VirtualSafetyCarEventCount + " virtual safety car" + (race.VirtualSafetyCarEventCount == 1 ? "" : "s") : "";
                    sentences.Add("The race ran under " + race.SafetyCarDeploymentCount + " safety car period" + (race.SafetyCarDeploymentCount == 1 ? "" : "s") + vscClause + ".");
                }
                else if (race.YellowFlagEventCount > 0)
                {
                    sentences.Add("A largely clean race, punctuated by " + race.YellowFlagEventCount + " yellow flag" + (race.YellowFlagEventCount == 1 ? "" : "s") + ".");
                }
            }

            if (!dnf)
            {
                string strategyClause = !string.IsNullOrEmpty(player.strategySummary)
                    ? "Ran a " + player.strategySummary + " strategy across " + player.pitStops + " stop" + (player.pitStops == 1 ? "" : "s")
                    : (player.pitStops > 0 ? "Made " + player.pitStops + " pit stop" + (player.pitStops == 1 ? "" : "s") : "");
                if (player.penaltiesSeconds > 0f)
                {
                    string penaltyClause = "a " + player.penaltiesSeconds.ToString("0") + "s penalty" + (string.IsNullOrEmpty(player.penaltyReason) ? "" : " (" + player.penaltyReason + ")") + " affected the final result";
                    sentences.Add(string.IsNullOrEmpty(strategyClause) ? "However, " + penaltyClause + "." : strategyClause + "; " + penaltyClause + ".");
                }
                else if (!string.IsNullOrEmpty(strategyClause))
                {
                    sentences.Add(strategyClause + ".");
                }
            }

            // Fuel system pass: narrative summary of how the chosen fuel-load plan
            // played out - deliberately approximate (no per-lap fuel history is
            // tracked, just start/finish kg and the lift-and-coast total) rather
            // than claiming precision this data doesn't actually have.
            if (player.fuelStarvedRetirement)
            {
                sentences.Add("The car retired with fuel starvation after an aggressive fuel strategy.");
            }
            else if (!dnf)
            {
                FuelLoadChoice fuelChoice = (FuelLoadChoice)player.fuelLoadChoice;
                bool chosenLight = fuelChoice == FuelLoadChoice.AggressiveUnderfuel || fuelChoice == FuelLoadChoice.LightUnderfuel;
                bool chosenHeavy = fuelChoice == FuelLoadChoice.Heavy || fuelChoice == FuelLoadChoice.Safe;
                if (player.finishFuelKg < 0.3f && player.liftAndCoastSavedKg > 0.3f)
                {
                    sentences.Add("Fuel ran tight late in the race, but lift-and-coast saving through the braking zones recovered enough margin to reach the finish.");
                }
                else if (chosenLight && player.finishFuelKg >= 0.3f && player.finishFuelKg <= 2.5f)
                {
                    sentences.Add("Started light on fuel and managed the margin well, saving enough to reach the finish. Strong fuel management.");
                }
                else if (chosenHeavy && player.finishFuelKg > 3f)
                {
                    sentences.Add("Carried about " + player.finishFuelKg.ToString("0.0") + "kg of unused fuel - a touch too conservative, costing acceleration early in the race.");
                }
            }

            return string.Join(" ", sentences);
        }

        // Section 3: teammate comparison - finishing position, best lap and pit
        // stop head-to-head bars (UiFactory.CreateComparisonBar), and points
        // gained. Absorbs the old "Teammate Battle" card so there's exactly one
        // teammate-focused card, not two overlapping ones.
        void BuildTeammateSection(RectTransform content, List<RaceResultEntry> results, RaceResultEntry player)
        {
            if (player == null || results == null)
            {
                return;
            }

            RaceResultEntry teammate = results.Find(entry => entry.teamId == player.teamId && entry.driverId != player.driverId);
            if (teammate == null)
            {
                return;
            }

            UiFactory.CreateSubHeader(content, "Teammate Comparison");
            float teammateTime = teammate.totalTime + teammate.penaltiesSeconds;
            float playerTime = player.totalTime + player.penaltiesSeconds;
            bool playerAhead = player.finishingPosition < teammate.finishingPosition;
            int pointsGap = player.points - teammate.points;

            RectTransform contentArea;
            UiFactory.CreateAutoCard(content, "Teammate comparison", "", UiFactory.AccentCyan, ReportContentWidth, out contentArea);
            float innerWidth = ReportContentWidth - 34f;
            UiFactory.CreateAutoText(contentArea, "Teammate comparison headline", player.driverName + " P" + player.finishingPosition + "   vs   " + teammate.driverName + " P" + teammate.finishingPosition, 15, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(contentArea, "Teammate comparison verdict", playerAhead ? "You finished ahead of your teammate" : "Your teammate finished ahead of you", 14, playerAhead ? UiFactory.AccentGreen : UiFactory.Accent, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(contentArea, "Teammate comparison gap", "Gap: " + Mathf.Abs(teammateTime - playerTime).ToString("0.0") + "s   ·   Points: " + player.points + " vs " + teammate.points + (pointsGap != 0 ? "  (" + (pointsGap > 0 ? "+" : "") + pointsGap + ")" : ""), 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);

            float lapCeiling = Mathf.Max(player.bestLapTime, teammate.bestLapTime) * 1.02f;
            int stopCeiling = Mathf.Max(1, Mathf.Max(player.pitStops, teammate.pitStops));
            UiFactory.CreateComparisonBar(contentArea, "Best Lap (s)", player.bestLapTime, UiFactory.Accent, teammate.bestLapTime, UiFactory.AccentCyan, lapCeiling, innerWidth);
            UiFactory.CreateComparisonBar(contentArea, "Pit Stops", player.pitStops, UiFactory.Accent, teammate.pitStops, UiFactory.AccentCyan, stopCeiling, innerWidth);
        }

        // Section 4: strategy - actual strategy summary/pit stops/track limits/
        // penalties alongside a new Strategy Review card comparing the planned
        // stop count/window/compound(s) (RaceManager.GetPlannedStopCount /
        // GetPlannedPitLapForStop / GetPlannedCompoundForStop - all always
        // available, career or Quick Race, since they read GameSettingsStore with
        // safe defaults) against what actually happened. There is no per-stop
        // actual lap tracked anywhere in the data model, so only stop count and
        // compound sequence are compared - no fabricated lap numbers.
        void BuildStrategySection(RectTransform content, RaceManager race, RaceResultEntry player)
        {
            if (player == null)
            {
                return;
            }

            UiFactory.CreateSubHeader(content, "Strategy");
            RectTransform row = UiFactory.CreateAutoHeightRow(content, "Strategy row", ReportContentWidth, 14, new RectOffset(0, 0, 0, 0));
            float halfWidth = (ReportContentWidth - 14f) / 2f;
            float innerWidth = halfWidth - 34f;

            RectTransform actualContentArea;
            UiFactory.CreateAutoCard(row, "Your strategy", "Your Strategy", UiFactory.AccentAmber, halfWidth, out actualContentArea);
            UiFactory.CreateAutoText(actualContentArea, "Strategy pit stops", "Pit stops: " + player.pitStops, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(actualContentArea, "Strategy summary", string.IsNullOrEmpty(player.strategySummary) ? "No stops made" : player.strategySummary, 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(actualContentArea, "Strategy track limits", "Track limit warnings: " + player.trackLimitWarnings, 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(actualContentArea, "Strategy penalties", player.penaltiesSeconds > 0f ? "Penalties: +" + player.penaltiesSeconds.ToString("0") + "s (" + player.penaltyReason + ")" : "No penalties", 14, player.penaltiesSeconds > 0f ? UiFactory.AccentAmber : UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);

            if (race != null)
            {
                BuildStrategyReviewCard(row, race, player, halfWidth);
            }
        }

        void BuildStrategyReviewCard(RectTransform row, RaceManager race, RaceResultEntry player, float width)
        {
            int plannedStops = race.GetPlannedStopCount();
            List<string> plannedParts = new List<string>();
            for (int stop = 1; stop <= plannedStops; stop++)
            {
                plannedParts.Add(race.GetPlannedCompoundForStop(stop) + " (~lap " + race.GetPlannedPitLapForStop(stop) + ")");
            }

            bool dnf = !string.IsNullOrEmpty(player.penaltyReason) && player.penaltyReason.Contains("DNF");
            string verdict;
            Color verdictColor;
            if (dnf)
            {
                verdict = "Race ended before the plan could play out";
                verdictColor = UiFactory.TextMuted;
            }
            else if (player.pitStops == plannedStops)
            {
                verdict = "Matched the plan";
                verdictColor = UiFactory.AccentGreen;
            }
            else if (player.pitStops > plannedStops)
            {
                verdict = "Pitted more than planned (+" + (player.pitStops - plannedStops) + ")";
                verdictColor = UiFactory.AccentAmber;
            }
            else
            {
                verdict = "Pitted fewer times than planned (-" + (plannedStops - player.pitStops) + ")";
                verdictColor = UiFactory.AccentCyan;
            }

            RectTransform contentArea;
            UiFactory.CreateAutoCard(row, "Strategy review", "Strategy Review", UiFactory.AccentPurple, width, out contentArea);
            float innerWidth = width - 34f;
            UiFactory.CreateAutoText(contentArea, "Strategy review planned", "Planned: " + plannedStops + "-stop - " + string.Join(" -> ", plannedParts), 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(contentArea, "Strategy review actual", "Actual: " + (string.IsNullOrEmpty(player.strategySummary) ? player.pitStops + " stop(s)" : player.strategySummary), 14, new Color(0.88f, 0.93f, 0.97f), TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(contentArea, "Strategy review verdict", verdict, 14, verdictColor, TextAnchor.UpperLeft, innerWidth);
        }

        // Section 5: incidents/race-control summary - genuine race-control totals
        // (yellow/VSC/safety car/red flag/penalty counts, all derived from
        // RaceManager.RaceControlHistory so they always agree with the timeline
        // below) alongside a clearly-separate informational card for diagnostic
        // collision counters, DNFs, and the player's own lockup/flat-spot/track-
        // limit telemetry - never mixed into the same numbers as race control.
        void BuildIncidentSection(RectTransform content, RaceManager race, List<RaceResultEntry> results, RaceResultEntry player)
        {
            if (race == null)
            {
                return;
            }

            UiFactory.CreateSubHeader(content, "Incidents & Race Control");
            RectTransform row = UiFactory.CreateAutoHeightRow(content, "Incident row", ReportContentWidth, 14, new RectOffset(0, 0, 0, 0));
            float halfWidth = (ReportContentWidth - 14f) / 2f;
            float innerWidth = halfWidth - 34f;

            RectTransform rcContentArea;
            UiFactory.CreateAutoCard(row, "Race control totals", "Race Control", UiFactory.AccentGreen, halfWidth, out rcContentArea);
            UiFactory.CreateAutoText(rcContentArea, "RC yellow", "Yellow flags: " + race.YellowFlagEventCount, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(rcContentArea, "RC vsc", "Virtual safety cars: " + race.VirtualSafetyCarEventCount, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(rcContentArea, "RC sc", "Safety cars: " + race.SafetyCarDeploymentCount, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(rcContentArea, "RC penalties", "Penalties issued: " + race.PenaltyEventCount, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            if (race.RedFlagCount > 0)
            {
                UiFactory.CreateAutoText(rcContentArea, "RC red flag", "Red flags: " + race.RedFlagCount + " (" + race.RedFlagReason + ")", 14, UiFactory.Accent, TextAnchor.UpperLeft, innerWidth);
            }

            int dnfCount = 0;
            if (results != null)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    if (!string.IsNullOrEmpty(results[i].penaltyReason) && results[i].penaltyReason.Contains("DNF"))
                    {
                        dnfCount++;
                    }
                }
            }

            RectTransform infoContentArea;
            UiFactory.CreateAutoCard(row, "Incident cleanup", "Incident Cleanup", UiFactory.AccentCyan, halfWidth, out infoContentArea);
            UiFactory.CreateAutoText(infoContentArea, "Info header", "INFORMATIONAL - NOT RACE CONTROL", 12, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(infoContentArea, "Info car contact", "Car-contact incidents: " + race.CarContactIncidentCount, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(infoContentArea, "Info solo", "Solo incidents (spins/walls): " + race.SoloContactIncidentCount, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(infoContentArea, "Info dnf", "Retirements (DNF): " + dnfCount, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            if (player != null)
            {
                UiFactory.CreateAutoText(infoContentArea, "Info player telemetry header", "YOUR CAR", 12, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
                UiFactory.CreateStatBar(infoContentArea, "Lockups", player.lockups, 8f, UiFactory.AccentAmber, innerWidth);
                UiFactory.CreateStatBar(infoContentArea, "Flat Spot", player.flatSpotPercent, 100f, UiFactory.AccentAmber, innerWidth);

                // Track-limit stewarding depth: the per-event log (lap/sector),
                // not just a bare warning count - shows exactly where each one
                // happened instead of leaving the player to guess.
                if (player.trackLimitEvents != null && player.trackLimitEvents.Count > 0)
                {
                    UiFactory.CreateAutoText(infoContentArea, "Info track limits header", "TRACK LIMIT EVENTS", 12, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
                    for (int i = 0; i < player.trackLimitEvents.Count; i++)
                    {
                        UiFactory.CreateAutoText(infoContentArea, "Info track limit event " + i, player.trackLimitEvents[i], 13, UiFactory.AccentAmber, TextAnchor.UpperLeft, innerWidth);
                    }
                }
            }
        }

        // Section 6: achievements - a row of wrapping badge chips (Part 17),
        // now inside a proper rounded card so it reads as one designed section
        // instead of chips floating loose on the background.
        void BuildAchievementsSection(RectTransform content, RaceManager race, List<RaceResultEntry> results, RaceResultEntry player)
        {
            if (results == null || results.Count == 0)
            {
                return;
            }

            List<string> badges = new List<string>();
            RaceResultEntry fastest = FindFastestLap(results);
            RaceResultEntry mover = FindBiggestMover(results);
            RaceResultEntry loser = FindBiggestLoser(results);
            RaceResultEntry dotd = ComputeDriverOfTheDay(results);

            if (dotd != null)
            {
                badges.Add("DRIVER OF THE DAY: " + dotd.driverName.ToUpperInvariant());
            }

            if (player != null)
            {
                bool dnf = !string.IsNullOrEmpty(player.penaltyReason) && player.penaltyReason.Contains("DNF");
                if (player.finishingPosition == 1) badges.Add("RACE WINNER");
                else if (player.finishingPosition <= 3 && !dnf) badges.Add("PODIUM");
                if (fastest != null && fastest.driverId == player.driverId) badges.Add("FASTEST LAP");
                if (mover != null && mover.driverId == player.driverId) badges.Add("BIGGEST MOVER");
                if (loser != null && loser.driverId == player.driverId) badges.Add("BIGGEST LOSER");
                if (!dnf && player.penaltiesSeconds <= 0f && player.trackLimitWarnings == 0 && player.lockups == 0) badges.Add("CLEAN RACE");
                if (player.penaltiesSeconds > 0f) badges.Add("PENALTY");
                if (dnf) badges.Add("DNF");
                if (race.SafetyCarDeploymentCount > 0 && !dnf && player.finishingPosition > 0 && player.finishingPosition <= 5) badges.Add("SAFETY CAR BENEFICIARY");
                if (player.pitStops > 0 && player.penaltiesSeconds <= 0f && !dnf && player.finishingPosition <= Mathf.Max(1, player.gridPosition)) badges.Add("STRATEGY WIN");
            }

            if (badges.Count == 0)
            {
                return;
            }

            UiFactory.CreateSubHeader(content, "Achievements");

            // Wrapping chip row instead of a single fixed HorizontalLayoutGroup line:
            // a big race (podium + fastest lap + strategy win + safety car beneficiary
            // + clean race, say) can produce five-plus badges, which used to just run
            // off the right edge of the screen with no way to see them. The lead badge
            // (Driver of the Day when present) keeps its own purple accent; the rest
            // wrap in green beneath it. Now sits in a proper rounded card (16px
            // padding on every edge) instead of loose on the content background.
            RectTransform row = UiFactory.CreateRect(content, "Report badges", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            Image rowBackground = row.gameObject.AddComponent<Image>();
            UiFactory.StyleRounded(rowBackground, UiFactory.PanelDarker);
            List<string> leadBadge = new List<string> { badges[0] };
            List<string> restBadges = badges.GetRange(1, badges.Count - 1);
            float innerWidth = ReportContentWidth - 32f;
            float leadHeight = UiFactory.CreateWrappingChipRow(row, "Report badges lead", leadBadge, UiFactory.AccentPurple, 16f, 16f, innerWidth);
            float totalHeight = leadHeight;
            if (restBadges.Count > 0)
            {
                float restOriginY = 16f + leadHeight + 8f;
                float restHeight = UiFactory.CreateWrappingChipRow(row, "Report badges rest", restBadges, UiFactory.AccentGreen, 16f, restOriginY, innerWidth);
                totalHeight = leadHeight + 8f + restHeight;
            }

            UiFactory.SetSize(row, ReportContentWidth, totalHeight + 32f);
        }

        // Section 7: race-control timeline - one card-style row per genuine
        // race-control event (RaceManager.RaceControlHistory - already excludes
        // minor incidents by design), grouped under a "Lap N" sub-header once
        // there are more than ~10 entries so a wild race stays scannable. Every
        // row auto-sizes via UiFactory.CreateAutoText instead of the old
        // `count * 24f + 12f` guess, which drifted the moment a detail string
        // wrapped to two lines. Omits itself entirely for a clean race.
        void BuildRaceControlTimeline(RectTransform content, RaceManager race)
        {
            if (race == null || race.RaceControlHistory == null || race.RaceControlHistory.Count == 0)
            {
                return;
            }

            UiFactory.CreateSubHeader(content, "Race Control Timeline");
            IReadOnlyList<RaceManager.RaceControlHistoryEntry> history = race.RaceControlHistory;
            bool grouped = history.Count > 10;

            RectTransform list = UiFactory.CreateAutoHeightList(content, "Race control timeline list", ReportContentWidth, 6, new RectOffset(16, 16, 16, 16));
            Image listBackground = list.gameObject.AddComponent<Image>();
            UiFactory.StyleRounded(listBackground, UiFactory.PanelDarker);
            float innerWidth = ReportContentWidth - 32f;

            int lastLap = int.MinValue;
            for (int i = 0; i < history.Count; i++)
            {
                RaceManager.RaceControlHistoryEntry entry = history[i];
                int lap = Mathf.Max(1, entry.lap);
                if (grouped && lap != lastLap)
                {
                    Text lapHeader = UiFactory.CreateAutoText(list, "Timeline lap header " + i, "LAP " + lap, 12, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
                    lapHeader.fontStyle = FontStyle.Bold;
                    lastLap = lap;
                }

                Color labelColor = (entry.label == "RED FLAG" || entry.label == "GRID RESET") ? UiFactory.Accent
                    : (entry.label == "PENALTY" ? UiFactory.AccentAmber
                    : ((entry.label == "GREEN FLAG" || entry.label == "RACE RESTART") ? UiFactory.AccentGreen : UiFactory.AccentCyan));
                string lapPrefix = grouped ? "" : ("Lap " + lap + "  ·  ");
                string line = "<color=#" + ColorUtility.ToHtmlStringRGB(labelColor) + "><b>" + entry.label + "</b></color>  " + lapPrefix + entry.detail;
                UiFactory.CreateAutoText(list, "Timeline entry " + i, line, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            }
        }

        // Full Classification table - every entry in `results` (the full,
        // already-sorted field), never filtered/limited. Row height bumped
        // slightly (32f -> 34f) over the old value for a little more breathing
        // room; driver/team cells keep CreateText's default Wrap (not Overflow)
        // so a long name wraps or gets vertically truncated instead of bleeding
        // into the next column or the next row.
        void BuildFullClassificationTable(RectTransform content, RaceManager race, List<RaceResultEntry> results)
        {
            // Layout-audit fix: rows used to be added directly as children of
            // `content`, which inherits content's own 20px inter-SECTION spacing
            // between every single driver row - a 20-car field turned into ~400px
            // of pure gap that read as "spaced horrendously" and made the table
            // look nothing like the tightly-pitched Race Control Timeline a few
            // sections above it. A dedicated container with its own tight spacing
            // (matching the timeline's own list) fixes that without touching row
            // sizing at all.
            RectTransform table = UiFactory.CreateAutoHeightList(content, "Classification rows", ReportContentWidth, 4, new RectOffset(0, 0, 0, 0));
            RectTransform headerRow = UiFactory.CreateTableRow(table, "Results header row", ReportContentWidth, 28f, false, 1);
            headerRow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            UiFactory.AddRowCell(headerRow, "H pos", "POS", 0.0f, 0.05f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H grid", "GRID", 0.05f, 0.1f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H driver", "DRIVER", 0.11f, 0.36f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H team", "TEAM", 0.36f, 0.5f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H tyre", "TYRE", 0.5f, 0.55f, 13, UiFactory.Accent, TextAnchor.MiddleCenter);
            UiFactory.AddRowCell(headerRow, "H gap", "TOTAL / GAP", 0.56f, 0.69f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H best", "BEST LAP", 0.69f, 0.79f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            // Widened from the original 0.81-0.88 so a retired car's DNF reason
            // (collision damage, mechanical failure, stranded, ...) actually has
            // room to render instead of being silently dropped in favor of the
            // bare "DNF" tag already shown in the Gap column.
            UiFactory.AddRowCell(headerRow, "H pen", "PEN / STATUS", 0.79f, 0.9f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H pts", "PTS", 0.9f, 0.97f, 13, UiFactory.Accent, TextAnchor.MiddleRight);

            if (results == null || results.Count == 0)
            {
                Text empty = UiFactory.CreateText(table, "No results", "No results.", 18, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.SetSize(empty, 600f, 30f);
                return;
            }

            float winnerTime = results[0].totalTime + results[0].penaltiesSeconds;
            for (int i = 0; i < results.Count; i++)
            {
                RaceResultEntry entry = results[i];
                float classifiedTime = entry.totalTime + entry.penaltiesSeconds;
                bool dnf = !string.IsNullOrEmpty(entry.penaltyReason) && entry.penaltyReason.Contains("DNF");
                string gap = dnf ? "DNF" : (i == 0 ? UiFactory.FormatTime(classifiedTime) : "+" + (classifiedTime - winnerTime).ToString("0.0") + "s");
                // For classified cars this is just the time penalty, same as
                // before. For retired cars it now shows the actual reason
                // (collision damage, mechanical failure, stranded, ...)
                // instead of leaving the column at a wasted "--".
                string penColumnText = dnf ? DnfReasonLabel(entry.penaltyReason) : (entry.penaltiesSeconds > 0f ? "+" + entry.penaltiesSeconds.ToString("0") + "s" : "--");
                Color penColumnColor = dnf ? UiFactory.TextMuted : (entry.penaltiesSeconds > 0f ? UiFactory.AccentAmber : UiFactory.TextMuted);
                RectTransform row = UiFactory.CreateTableRow(table, "Result row " + i, ReportContentWidth, 34f, entry.isPlayer, i);
                UiFactory.AddPositionBadge(row, entry.finishingPosition, entry.isPlayer);
                Color textColor = entry.isPlayer ? Color.white : (dnf ? UiFactory.TextMuted : new Color(0.9f, 0.95f, 0.98f));
                UiFactory.AddRowCell(row, "Grid", entry.gridPosition > 0 ? entry.gridPosition.ToString() : "--", 0.05f, 0.1f, 14, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Driver", entry.driverName, 0.11f, 0.36f, 15, textColor, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Team", TeamLabel(race, entry.teamId), 0.36f, 0.5f, 14, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.AddRowDot(row, "Tyre dot", 0.525f, 13f, TyreDotColor(entry.tyreCompound));
                UiFactory.AddRowCell(row, "Gap", gap, 0.56f, 0.69f, 14, dnf ? UiFactory.Accent : textColor, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Best", UiFactory.FormatTime(entry.bestLapTime), 0.69f, 0.79f, 14, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Pen", penColumnText, 0.79f, 0.9f, dnf ? 12 : 14, penColumnColor, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Pts", entry.points.ToString(), 0.9f, 0.97f, 15, entry.points > 0 ? UiFactory.AccentGreen : UiFactory.TextMuted, TextAnchor.MiddleRight);

                if (UiFactory.AnimationsEnabled)
                {
                    UiFadeIn reveal = row.gameObject.AddComponent<UiFadeIn>();
                    reveal.startDelay = Mathf.Min(i, 14) * 0.03f;
                }
            }
        }

        // Section 9: championship impact - driver/constructor standing and
        // points before/after this race (StandingsMovementSummary, from the
        // RaceReportRecord CareerManager just appended in ApplyRaceResults), now
        // also showing the points gap to the next-better position for both
        // driver and team, computed from the live, already-updated
        // driverStandings/constructorStandings lists (same convention as
        // FindStandingsRank above: list order is rank order, so the entry one
        // index better than the player's position is literally "next"). Quick
        // Race has no Career/standings context, so this section omits itself
        // entirely rather than showing fabricated numbers - unchanged from
        // before.
        void BuildChampionshipSection(RectTransform content, RaceManager race, RaceResultEntry player)
        {
            if (race == null || !race.IsCareerRace || race.Career == null || player == null)
            {
                return;
            }

            List<RaceReportRecord> reports = race.Career.Save.raceReports;
            RaceReportRecord latest = reports != null && reports.Count > 0 ? reports[reports.Count - 1] : null;
            if (latest == null)
            {
                return;
            }

            UiFactory.CreateSubHeader(content, "Championship Impact");
            StandingsMovementSummary standings = latest.standings;
            string driverLine = standings.driverPositionBefore > 0
                ? "Driver: P" + standings.driverPositionAfter + "  (was P" + standings.driverPositionBefore + ")"
                : "Driver: P" + standings.driverPositionAfter + "  (debut points)";
            string driverMoveLine = standings.driverPositionBefore <= 0 ? ""
                : standings.driverPositionAfter < standings.driverPositionBefore ? "Up " + (standings.driverPositionBefore - standings.driverPositionAfter) + " place(s)"
                : standings.driverPositionAfter > standings.driverPositionBefore ? "Down " + (standings.driverPositionAfter - standings.driverPositionBefore) + " place(s)"
                : "Position held";
            Color driverMoveColor = standings.driverPositionBefore <= 0 ? UiFactory.TextMuted
                : standings.driverPositionAfter < standings.driverPositionBefore ? UiFactory.AccentGreen
                : standings.driverPositionAfter > standings.driverPositionBefore ? UiFactory.Accent
                : UiFactory.TextMuted;
            string pointsLine = "Points: " + standings.driverPointsAfter + "  (+" + (standings.driverPointsAfter - standings.driverPointsBefore) + ")";
            string constructorLine = standings.constructorPositionAfter > 0
                ? "Constructors: P" + standings.constructorPositionAfter + (standings.constructorPositionBefore > 0 && standings.constructorPositionBefore != standings.constructorPositionAfter ? "  (was P" + standings.constructorPositionBefore + ")" : "")
                : "Constructors: --";
            string driverGapLine = ComputeChampionshipGapLine(race.Career.Save.driverStandings, player.driverId, standings.driverPositionAfter);
            string teamGapLine = ComputeChampionshipGapLine(race.Career.Save.constructorStandings, player.teamId, standings.constructorPositionAfter);

            RectTransform contentArea;
            UiFactory.CreateAutoCard(content, "Championship impact", "", UiFactory.AccentPurple, ReportContentWidth, out contentArea);
            float innerWidth = ReportContentWidth - 34f;
            UiFactory.CreateAutoText(contentArea, "Championship driver", driverLine, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            if (!string.IsNullOrEmpty(driverMoveLine))
            {
                UiFactory.CreateAutoText(contentArea, "Championship driver move", driverMoveLine, 14, driverMoveColor, TextAnchor.UpperLeft, innerWidth);
            }

            UiFactory.CreateAutoText(contentArea, "Championship points", pointsLine, 14, UiFactory.AccentGreen, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(contentArea, "Championship driver gap", driverGapLine, 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(contentArea, "Championship constructor", constructorLine, 14, UiFactory.TextPrimary, TextAnchor.UpperLeft, innerWidth);
            UiFactory.CreateAutoText(contentArea, "Championship constructor gap", teamGapLine, 14, UiFactory.TextMuted, TextAnchor.UpperLeft, innerWidth);
        }

        // Points gap between `id`'s standings entry and the entry one place
        // better than `positionAfter` - "Leader" at P1, "--" when there's no
        // usable standings data for this id/position.
        string ComputeChampionshipGapLine(List<StandingEntry> standings, string id, int positionAfter)
        {
            if (standings == null || positionAfter <= 0 || string.IsNullOrEmpty(id))
            {
                return "Gap to next: --";
            }

            if (positionAfter == 1)
            {
                return "Gap to next: Leader";
            }

            int aheadIndex = positionAfter - 2;
            if (aheadIndex < 0 || aheadIndex >= standings.Count)
            {
                return "Gap to next: --";
            }

            StandingEntry mine = standings.Find(entry => entry.id == id);
            if (mine == null)
            {
                return "Gap to next: --";
            }

            StandingEntry ahead = standings[aheadIndex];
            int gap = ahead.points - mine.points;
            return "Gap to P" + (positionAfter - 1) + ": " + gap + " pts";
        }

        // "Where you gained/lost time" cornering breakdown - average actual vs
        // reference speed per corner-risk tier, fed by RaceManager.SampleCorneringTelemetry
        // into RaceParticipant.cornerSpeedSumByRisk / cornerReferenceSumByRisk /
        // cornerSampleCountByRisk (both sums are in km/h, matching the HUD speed
        // readout). Plain-language tier names since "Low/Medium/High risk" from
        // TrackRuntime.CornerRisk would mean nothing to a player without context.
        // Skips itself entirely if the player has no cornering samples at all
        // (e.g. a session that ended before turn 1), and skips a tier
        // individually if that tier alone has no samples this race.
        static readonly string[] CorneringTierLabels = { "Flowing Corners", "Medium Corners", "Tight Corners" };
        static readonly string[] CorneringTierHints = { "low-risk, high-speed corners", "medium-risk corners", "high-risk, low-speed corners" };

        void BuildCorneringTelemetryCard(RectTransform content, RaceManager race)
        {
            if (race == null || race.PlayerParticipant == null)
            {
                return;
            }

            RaceParticipant player = race.PlayerParticipant;
            bool anyData = false;
            for (int i = 0; i < 3; i++)
            {
                if (player.cornerSampleCountByRisk[i] > 0)
                {
                    anyData = true;
                    break;
                }
            }

            if (!anyData)
            {
                return;
            }

            UiFactory.CreateSubHeader(content, "Cornering Analysis");
            RectTransform contentArea;
            UiFactory.CreateAutoCard(content, "Cornering analysis", "", UiFactory.AccentGreen, ReportContentWidth, out contentArea);
            float innerWidth = ReportContentWidth - 34f;

            for (int i = 0; i < 3; i++)
            {
                int samples = player.cornerSampleCountByRisk[i];
                string label = CorneringTierLabels[i] + " (" + CorneringTierHints[i] + ")";
                if (samples <= 0)
                {
                    UiFactory.CreateBreakdownRow(contentArea, label, "Not enough data this race", UiFactory.TextMuted, innerWidth);
                    continue;
                }

                float avgActual = player.cornerSpeedSumByRisk[i] / samples;
                float avgReference = player.cornerReferenceSumByRisk[i] / samples;
                float gapPercent = avgReference > 0.01f ? (avgActual - avgReference) / avgReference * 100f : 0f;
                string valueText = avgActual.ToString("0.0") + " km/h vs " + avgReference.ToString("0.0") + " km/h ref  (" + (gapPercent >= 0f ? "+" : "") + gapPercent.ToString("0.0") + "%)";
                Color valueColor = gapPercent >= -1f ? UiFactory.AccentGreen : (gapPercent >= -4f ? UiFactory.AccentAmber : UiFactory.Accent);
                UiFactory.CreateBreakdownRow(contentArea, label, valueText, valueColor, innerWidth);
            }
        }

        // Telemetry report (#69): braking/ERS/tyre-heat signals the cornering
        // card above doesn't cover, plus one synthesized, actionable
        // recommendation picked from whichever signal reads worst - reuses
        // fields already tracked on the live RaceParticipant/VehicleController/
        // TyreState (lockups, flat spots, ERS/DRS frame counters) rather than
        // adding a second sampling pipeline alongside SampleCorneringTelemetry.
        void BuildDrivingTelemetryCard(RectTransform content, RaceManager race)
        {
            if (race == null || race.PlayerParticipant == null || race.PlayerParticipant.vehicle == null || race.PlayerParticipant.vehicle.Tyres == null)
            {
                return;
            }

            RaceParticipant player = race.PlayerParticipant;
            int laps = player.lapTracker != null ? Mathf.Max(1, player.lapTracker.CompletedLaps) : 1;
            int lockups = player.vehicle.Tyres.TotalLockups;
            float flatSpotPercent = player.vehicle.Tyres.FlatSpotLevel * 100f;
            float wearPercent = player.vehicle.Tyres.WearPercent;
            float lockupsPerLap = lockups / (float)laps;

            int frames = Mathf.Max(1, player.trackedTickFrameCount);
            float ersUsagePercent = player.ersDeployFrameCount / (float)frames * 100f;
            float drsUsagePercent = player.drsActiveFrameCount / (float)frames * 100f;

            UiFactory.CreateSubHeader(content, "Driving Telemetry");
            RectTransform contentArea;
            UiFactory.CreateAutoCard(content, "Driving telemetry", "", UiFactory.AccentCyan, ReportContentWidth, out contentArea);
            float innerWidth = ReportContentWidth - 34f;

            Color lockupColor = lockupsPerLap <= 0.3f ? UiFactory.AccentGreen : (lockupsPerLap <= 1f ? UiFactory.AccentAmber : UiFactory.Accent);
            UiFactory.CreateBreakdownRow(contentArea, "Braking (lockups)", lockups + " total, " + lockupsPerLap.ToString("0.00") + " per lap", lockupColor, innerWidth);

            Color flatSpotColor = flatSpotPercent <= 8f ? UiFactory.AccentGreen : (flatSpotPercent <= 25f ? UiFactory.AccentAmber : UiFactory.Accent);
            UiFactory.CreateBreakdownRow(contentArea, "Tyre Flat-Spotting", flatSpotPercent.ToString("0") + "% - " + (flatSpotPercent <= 8f ? "clean braking" : "heavy lockups scarring the tyre"), flatSpotColor, innerWidth);

            UiFactory.CreateBreakdownRow(contentArea, "Tyre Wear At Finish", wearPercent.ToString("0") + "%", UiFactory.TextPrimary, innerWidth);
            UiFactory.CreateBreakdownRow(contentArea, "ERS Deployment", ersUsagePercent.ToString("0") + "% of the race", UiFactory.TextPrimary, innerWidth);
            UiFactory.CreateBreakdownRow(contentArea, "DRS Activation", drsUsagePercent.ToString("0") + "% of the race", UiFactory.TextPrimary, innerWidth);

            // One concrete recommendation, picked from whichever signal above
            // reads worst - a report full of numbers with nothing actionable
            // was the actual complaint this card exists to fix.
            string recommendation;
            if (lockupsPerLap > 1f)
            {
                recommendation = "Brake earlier into your heaviest braking zones - " + lockups + " lockups this race is scarring your tyres and costing lap time.";
            }
            else if (flatSpotPercent > 25f)
            {
                recommendation = "Ease trail-braking pressure once the front tyres lock - flat-spotting is already past a quarter of the tyre's contact patch.";
            }
            else if (ersUsagePercent < 15f)
            {
                recommendation = "You're leaving ERS in the battery - deploy more on straights and defending, not just for overtakes.";
            }
            else
            {
                recommendation = "Clean session - braking and tyre management were both within a healthy range.";
            }

            UiFactory.CreateAutoText(contentArea, "Telemetry recommendation", recommendation, 14, UiFactory.AccentGreen, TextAnchor.UpperLeft, innerWidth);
        }

        RaceResultEntry FindBiggestLoser(List<RaceResultEntry> results)
        {
            RaceResultEntry worst = null;
            int worstLoss = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].gridPosition <= 0)
                {
                    continue;
                }

                int loss = results[i].finishingPosition - results[i].gridPosition;
                if (loss > worstLoss)
                {
                    worstLoss = loss;
                    worst = results[i];
                }
            }

            return worst;
        }

        // Simple, data-driven "driver of the day": rewards net position gain,
        // overtakes made and a strong finish, penalized by time penalties and
        // track limit warnings - not a random pick.
        RaceResultEntry ComputeDriverOfTheDay(List<RaceResultEntry> results)
        {
            RaceResultEntry best = null;
            float bestScore = float.NegativeInfinity;
            RaceResultEntry fastest = FindFastestLap(results);
            for (int i = 0; i < results.Count; i++)
            {
                RaceResultEntry entry = results[i];
                bool dnf = !string.IsNullOrEmpty(entry.penaltyReason) && entry.penaltyReason.Contains("DNF");
                if (dnf)
                {
                    continue;
                }

                int gained = entry.gridPosition > 0 ? entry.gridPosition - entry.finishingPosition : 0;
                float score = gained * 2f + entry.overtakesMade * 1.5f
                    + (entry.finishingPosition == 1 ? 4f : entry.finishingPosition <= 3 ? 2f : 0f)
                    + (fastest != null && fastest.driverId == entry.driverId ? 2f : 0f)
                    - entry.penaltiesSeconds * 0.15f
                    - entry.trackLimitWarnings * 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = entry;
                }
            }

            return best;
        }

        RaceResultEntry FindFastestLap(List<RaceResultEntry> results)
        {
            RaceResultEntry best = null;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].bestLapTime > 0f && (best == null || results[i].bestLapTime < best.bestLapTime))
                {
                    best = results[i];
                }
            }

            return best;
        }

        RaceResultEntry FindBiggestMover(List<RaceResultEntry> results)
        {
            RaceResultEntry best = null;
            int bestGain = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].gridPosition <= 0)
                {
                    continue;
                }

                int gain = results[i].gridPosition - results[i].finishingPosition;
                if (gain > bestGain)
                {
                    bestGain = gain;
                    best = results[i];
                }
            }

            return best;
        }

        // Single podium slot for the results screen highlight row - the podium
        // card itself is a shared UiFactory widget; this just sources the right
        // team color/time label from the result entry and race data.
        void BuildPodiumSlot(RectTransform parent, RaceManager race, List<RaceResultEntry> results, int index)
        {
            if (results == null || index < 0 || index >= results.Count)
            {
                return;
            }

            RaceResultEntry entry = results[index];
            int position = entry.finishingPosition > 0 ? entry.finishingPosition : index + 1;
            // Bumped up from the old 220x(236/200/180) - "too small for the
            // screen" was the explicit complaint - and the P1/P2/P3 height
            // spread widened too, so the winner's platform reads as clearly
            // taller rather than just marginally so.
            float height = position == 1 ? 300f : (position == 2 ? 254f : 224f);
            TeamData team = race != null && race.Data != null ? race.Data.FindTeam(entry.teamId) : null;
            Color teamColor = team != null ? team.PrimaryUnityColor : new Color(0.6f, 0.66f, 0.72f);
            // Bug fix: this used to always format position 1 as a finishing time,
            // even when results[0] is a DNF (possible in a fully-retired/red-flagged
            // race, where every entry still gets a large synthetic finishTime and
            // sorts by it) - the podium would show "WINNER" next to a nonsense
            // multi-hour time instead of "DNF", while P2/P3 correctly showed DNF via
            // GapLabel below. GapLabel already has the right DNF check; reuse it here
            // too instead of duplicating a P1-only special case that skipped it.
            bool isDnf = !string.IsNullOrEmpty(entry.penaltyReason) && entry.penaltyReason.Contains("DNF");
            string timeLabel = position == 1 && !isDnf ? UiFactory.FormatTime(entry.totalTime + entry.penaltiesSeconds) : GapLabel(results, entry);
            UiFactory.CreatePodiumCard(parent, position, entry.driverName, TeamLabel(race, entry.teamId), teamColor, timeLabel, 280f, height);
        }

        // Gap-to-winner text for a classified entry, or "DNF" for a retirement -
        // the same rule the full classification table already uses, factored out
        // so the podium cards can show it too.
        string GapLabel(List<RaceResultEntry> results, RaceResultEntry entry)
        {
            bool dnf = !string.IsNullOrEmpty(entry.penaltyReason) && entry.penaltyReason.Contains("DNF");
            if (dnf)
            {
                return "DNF";
            }

            float winnerTime = results[0].totalTime + results[0].penaltiesSeconds;
            float classifiedTime = entry.totalTime + entry.penaltiesSeconds;
            return "+" + (classifiedTime - winnerTime).ToString("0.0") + "s";
        }

        public void ShowQualifyingResults(RaceManager race, List<QualifyingResultEntry> results, bool careerRace)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Qualifying background", new Color(0.012f, 0.016f, 0.021f, 1f));
            string resultTitle = race != null && race.LastQualifyingResultWasSimulated ? "Sim Qualifying Classification" : "Qualifying Classification";
            UiFactory.CreateTopNav(background, resultTitle);

            RectTransform highlights = UiFactory.CreateRect(background, "Qualifying highlights", new Vector2(0.06f, 0.76f), new Vector2(0.94f, 0.87f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(highlights, 14, new RectOffset(0, 0, 0, 0));
            if (results != null && results.Count > 0)
            {
                UiFactory.CreateStatCard(highlights, "Pole Position", results[0].driverName + "  " + (results[0].bestLapTime >= 9998f ? "NO TIME" : UiFactory.FormatTime(results[0].bestLapTime)), 420f);
                QualifyingResultEntry player = results.Find(entry => entry.isPlayer);
                if (player != null)
                {
                    UiFactory.CreateStatCard(highlights, "You Qualified", "P" + player.position + (string.IsNullOrEmpty(player.eliminatedIn) ? "" : "  (out in " + player.eliminatedIn + ")"), 340f);
                }
            }

            // When the session was simulated, reserve the right column for the
            // player's itemized lap explanation so P22 always has a stated reason.
            bool showExplanation = race != null && race.LastQualifyingResultWasSimulated && !string.IsNullOrEmpty(race.SimQualifyingExplanation);
            float tableRight = showExplanation ? 0.6f : 0.94f;
            float rowWidth = showExplanation ? 800f : 1240f;
            RectTransform content = UiFactory.CreateScrollPanel(background, "Qualifying table", new Vector2(0.06f, 0.14f), new Vector2(tableRight, 0.74f), 4, new RectOffset(18, 18, 12, 12));
            string lastSection = null;
            if (results != null)
            {
                float pole = results.Count > 0 && results[0].bestLapTime < 9998f ? results[0].bestLapTime : 0f;
                for (int i = 0; i < results.Count; i++)
                {
                    QualifyingResultEntry entry = results[i];
                    string section = string.IsNullOrEmpty(entry.eliminatedIn) ? "Q3 — TOP 10 SHOOTOUT" : "ELIMINATED IN " + entry.eliminatedIn;
                    if (section != lastSection)
                    {
                        lastSection = section;
                        Text sectionText = UiFactory.CreateText(content, "Qualifying section " + i, section, 14, UiFactory.Accent, TextAnchor.MiddleLeft);
                        UiFactory.SetSize(sectionText, rowWidth, 26f);
                    }

                    string lapLabel = entry.bestLapTime >= 9998f ? "NO TIME" : UiFactory.FormatTime(entry.bestLapTime);
                    string gapLabel = entry.bestLapTime >= 9998f || pole <= 0f ? "--"
                        : (i == 0 ? "POLE" : "+" + (entry.bestLapTime - pole).ToString("0.000"));
                    RectTransform row = UiFactory.CreateTableRow(content, "Qualifying row " + i, rowWidth, 32f, entry.isPlayer, i);
                    UiFactory.AddPositionBadge(row, entry.position, entry.isPlayer);
                    Color textColor = entry.isPlayer ? Color.white : new Color(0.9f, 0.95f, 0.98f);
                    UiFactory.AddRowCell(row, "Driver", entry.driverName, 0.06f, 0.42f, 15, textColor, TextAnchor.MiddleLeft);
                    UiFactory.AddRowCell(row, "Team", TeamLabel(race, entry.teamId), 0.42f, 0.6f, 14, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                    UiFactory.AddRowCell(row, "Lap", lapLabel, 0.6f, 0.78f, 14, entry.bestLapTime >= 9998f ? UiFactory.Accent : textColor, TextAnchor.MiddleLeft);
                    UiFactory.AddRowCell(row, "Gap", gapLabel, 0.78f, 0.92f, 14, i == 0 ? UiFactory.AccentPurple : UiFactory.TextMuted, TextAnchor.MiddleLeft);
                    if (entry.invalidated)
                    {
                        UiFactory.AddRowCell(row, "Invalid", "INV", 0.92f, 1f, 13, UiFactory.Accent, TextAnchor.MiddleCenter);
                    }

                    // Grid reveal: rows cascade in top-to-bottom (pole first)
                    // instead of the whole classification just appearing, so
                    // the moment the grid is finally known reads as an event.
                    if (UiFactory.AnimationsEnabled)
                    {
                        UiFadeIn reveal = row.gameObject.AddComponent<UiFadeIn>();
                        reveal.startDelay = Mathf.Min(i, 14) * 0.035f;
                    }
                }
            }

            if (showExplanation)
            {
                RectTransform explainCard = UiFactory.CreateGlassPanel(background, "Sim qualifying explanation", new Vector2(0.62f, 0.14f), new Vector2(0.94f, 0.74f), Vector2.zero, Vector2.zero, UiFactory.PanelDark);
                RectTransform explainAccent = UiFactory.CreateRect(explainCard, "Explain accent", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
                explainAccent.sizeDelta = new Vector2(64f, 4f);
                explainAccent.pivot = new Vector2(0f, 1f);
                explainAccent.anchoredPosition = new Vector2(22f, -14f);
                Image explainAccentImage = explainAccent.gameObject.AddComponent<Image>();
                UiFactory.StyleRoundedSmall(explainAccentImage, UiFactory.AccentCyan);
                Text explainTitle = UiFactory.CreateText(explainCard, "Explain title", "WHY YOU QUALIFIED WHERE YOU DID", 16, UiFactory.AccentCyan, TextAnchor.UpperLeft);
                RectTransform explainTitleRect = explainTitle.GetComponent<RectTransform>();
                explainTitleRect.anchorMin = new Vector2(0f, 1f);
                explainTitleRect.anchorMax = new Vector2(1f, 1f);
                explainTitleRect.offsetMin = new Vector2(22f, -54f);
                explainTitleRect.offsetMax = new Vector2(-18f, -26f);
                // Itemized breakdown - label + signed value per row - instead of
                // one dense multi-line Text blob, so base pace / car effect /
                // driver effect / tyre / weather / mistake / final time and the
                // elimination-or-grid reason each read as their own line.
                RectTransform explainBody = UiFactory.CreateScrollPanel(explainCard, "Explain breakdown", new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.86f), 5, new RectOffset(12, 12, 10, 10));
                RenderQualifyingBreakdown(explainBody, race.SimQualifyingExplanation, 520f);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Weekend", bootstrap.ShowRaceWeekend);
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", bootstrap.ShowMainMenu);
            UiFactory.CreatePrimaryButton(footerRight, "Continue to Race", bootstrap.StartCareerRace);
        }

        void BuildPausePanel(RaceManager race)
        {
            RectTransform root = UiFactory.CreateBackdrop(canvas.transform, "Pause overlay");
            pausePanel = root.gameObject;
            RectTransform card = UiFactory.CreateCard(root, "Pause card", new Vector2(0.35f, 0.22f), new Vector2(0.65f, 0.78f));
            RectTransform menu = UiFactory.CreateRect(card, "Pause menu", Vector2.zero, Vector2.one, new Vector2(28f, 22f), new Vector2(-28f, -22f));
            UiFactory.AddVerticalLayout(menu, 11, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateText(menu, "Paused", "PAUSED", 34, Color.white, TextAnchor.MiddleLeft);
            string sessionLabel = race.IsTimeTrial ? "Time Trial" : (race.CurrentSession == RaceWeekendSession.Qualifying ? "Qualifying" : (race.CurrentSession == RaceWeekendSession.Practice ? "Practice" : "Race"));
            string eventLabel = race.EventData == null ? "Prototype GP" : race.EventData.displayName;
            UiFactory.CreateText(menu, "Pause session", sessionLabel + "  ·  " + eventLabel, 18, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            UiFactory.CreateDivider(menu);
            UiFactory.CreateSubHeader(menu, "Controls");
            Text controls = UiFactory.CreateText(menu, "Pause controls", "W/S throttle & brake   A/D steer\nSpace DRS   R ERS mode (hold: reset car)\nShift ERS override   C camera   P pit\nQ/E manual shift   F1 debug overlay   Esc resume", 16, new Color(0.78f, 0.86f, 0.9f), TextAnchor.UpperLeft);
            controls.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(controls, 460f, 92f);
            UiFactory.CreateDivider(menu);
            // Button hierarchy fix: Resume is the one primary action; Main Menu
            // is neutral navigation (secondary); Restart Session and Quit Game
            // both throw away the current session's progress, so both now use
            // the danger style instead of one being an unstyled default button
            // and the other secondary - the two genuinely destructive choices
            // now look consistently different from "just navigating".
            UiFactory.CreatePrimaryButton(menu, "Resume", race.Resume);
            if (race.CurrentSession == RaceWeekendSession.Practice)
            {
                // Practice has no lap-count finish (RaceManager.RaceLaps returns
                // 999 for it) - this is the only way a session ends, scoring the
                // real telemetry driven so far instead of discarding it.
                UiFactory.CreatePrimaryButton(menu, "End Practice Session", bootstrap.EndPracticeSession);
            }

            UiFactory.CreateSecondaryButton(menu, "Main Menu", () =>
            {
                race.CleanupRaceWorld();
                bootstrap.ShowMainMenu();
            });
            UiFactory.CreateDangerButton(menu, "Restart Session", race.RestartRace);
            UiFactory.CreateDangerButton(menu, "Quit Game", Application.Quit);
            pausePanel.SetActive(false);
        }

        void BuildStandingsRows(GameDataRepository data, RectTransform content, List<StandingEntry> standings, float width)
        {
            int count = Mathf.Min(10, standings.Count);
            for (int i = 0; i < count; i++)
            {
                StandingEntry entry = standings[i];
                TeamData team = data.FindTeam(entry.teamId);
                if (team == null)
                {
                    team = data.FindTeam(entry.id);
                }

                RectTransform row = UiFactory.CreateTableRow(content, "Standing row " + i, width, 28f, false, i);
                UiFactory.AddPositionBadge(row, i + 1, false);
                UiFactory.AddRowDot(row, "Standing team dot", 0.16f, 9f, team != null ? team.PrimaryUnityColor : new Color(0.5f, 0.55f, 0.6f));
                UiFactory.AddRowCell(row, "Name", entry.displayName, 0.2f, 0.72f, 14, new Color(0.92f, 0.96f, 0.99f), TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Points", entry.points + " PTS", 0.72f, 1f, 14, UiFactory.AccentGreen, TextAnchor.MiddleRight);
            }
        }

        string BuildCarPerformanceText(CarPerformanceData baseCar, CarPerformanceData tunedCar)
        {
            return "Car performance\n" +
                   "Top " + StatDelta(baseCar.topSpeed, tunedCar.topSpeed) + " km/h   Acc " + StatDelta(baseCar.acceleration, tunedCar.acceleration) + "\n" +
                   "Corner " + StatDelta(baseCar.cornering, tunedCar.cornering) + "   Brake " + StatDelta(baseCar.braking, tunedCar.braking) + "\n" +
                   "Aero " + StatDelta(baseCar.aeroEfficiency, tunedCar.aeroEfficiency) + "   ERS " + StatDelta(baseCar.ersEfficiency, tunedCar.ersEfficiency) + "\n" +
                   "Tyres " + StatDelta(baseCar.tyreManagement, tunedCar.tyreManagement) + "   Power " + StatDelta(baseCar.enginePower, tunedCar.enginePower);
        }

        string StatDelta(int baseline, int tuned)
        {
            int delta = tuned - baseline;
            return tuned + (delta == 0 ? "" : " <color=#6CFF8D>+" + delta + "</color>");
        }

        // Real per-row grid list for the Race Weekend screen - position badge,
        // driver, team, best lap - replaces the old padded monospace text block.
        // Only ever called once qualifying has actually produced a result; the
        // pre-qualifying state is BuildLockedQualifyingState below, not this.
        void BuildQualifyingGridRows(GameDataRepository data, RectTransform content, List<QualifyingResultEntry> results, float rowWidth)
        {
            if (results == null || results.Count == 0)
            {
                Text empty = UiFactory.CreateText(content, "No grid", "No qualifying data.", 15, UiFactory.TextMuted, TextAnchor.UpperLeft);
                UiFactory.SetSize(empty, rowWidth, 50f);
                empty.verticalOverflow = VerticalWrapMode.Overflow;
                return;
            }

            for (int i = 0; i < results.Count; i++)
            {
                QualifyingResultEntry entry = results[i];
                TeamData team = data.FindTeam(entry.teamId);
                string teamCode = team == null ? (string.IsNullOrEmpty(entry.teamId) ? "--" : entry.teamId.ToUpperInvariant()) : team.shortName.ToUpperInvariant();
                string lapLabel = entry.bestLapTime >= 9998f ? "NO TIME" : UiFactory.FormatTime(entry.bestLapTime);

                RectTransform row = UiFactory.CreateTableRow(content, "Grid row " + i, rowWidth, 30f, entry.isPlayer, i);
                UiFactory.AddPositionBadge(row, entry.position, entry.isPlayer);
                Color textColor = entry.isPlayer ? Color.white : new Color(0.9f, 0.95f, 0.98f);
                UiFactory.AddRowCell(row, "Driver", entry.driverName, 0.13f, 0.55f, 14, textColor, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Team", teamCode, 0.55f, 0.76f, 13, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.AddRowCell(row, "Best", lapLabel, 0.76f, 1f, 13, entry.bestLapTime >= 9998f ? UiFactory.Accent : UiFactory.TextMuted, TextAnchor.MiddleRight);
            }
        }

        // Grid-hiding fix: the locked state shown on the Race Weekend screen
        // before qualifying has been driven or simulated. Deliberately shows no
        // order, positions, or driver list of any kind - a real grid must never
        // be visible (even as a "provisional"/default placeholder) until
        // qualifying has actually produced one.
        void BuildLockedQualifyingState(RectTransform content)
        {
            RectTransform wrap = UiFactory.CreateRect(content, "Qualifying locked state", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            VerticalLayoutGroup layout = UiFactory.AddVerticalLayout(wrap, 14, new RectOffset(36, 36, 0, 0));
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            RectTransform badgeRow = UiFactory.CreateRect(wrap, "Locked badge row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            HorizontalLayoutGroup badgeLayout = UiFactory.AddHorizontalLayout(badgeRow, 8, new RectOffset(0, 0, 0, 0));
            badgeLayout.childAlignment = TextAnchor.MiddleCenter;
            badgeLayout.childForceExpandWidth = false;
            UiFactory.SetSize(badgeRow, 260f, 26f);
            UiFactory.CreatePulsingDot(badgeRow, "Locked dot", 10f, UiFactory.AccentAmber);
            Text badgeText = UiFactory.CreateText(badgeRow, "Locked badge", "GRID LOCKED", 13, UiFactory.AccentAmber, TextAnchor.MiddleCenter);
            badgeText.fontStyle = FontStyle.Bold;
            UiFactory.SetSize(badgeText, 220f, 22f);

            Text title = UiFactory.CreateText(wrap, "Locked title", "QUALIFYING NOT RUN", 22, UiFactory.TextPrimary, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            UiFactory.SetSize(title, 420f, 30f);

            Text body = UiFactory.CreateText(wrap, "Locked body",
                "Starting positions are unknown until qualifying is complete.\nDrive qualifying yourself or simulate it to reveal the grid.",
                16, new Color(0.62f, 0.7f, 0.76f), TextAnchor.MiddleCenter);
            body.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(body, 440f, 60f);
        }

        // Turns the sim qualifying explanation string (built in RaceManager as
        // "Label<spaces>Value" lines) into real label/value rows instead of one
        // dense Text blob - base pace, car/driver/tyre/weather effects, mistake
        // variance, final time, and the elimination-or-grid reason each get
        // their own line, colored by whether the delta cost or gained time.
        void RenderQualifyingBreakdown(RectTransform container, string explanation, float width)
        {
            if (string.IsNullOrEmpty(explanation))
            {
                return;
            }

            string[] lines = explanation.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("YOUR "))
                {
                    Text sectionText = UiFactory.CreateText(container, "Breakdown section " + i, line.Trim(), 14, UiFactory.AccentCyan, TextAnchor.UpperLeft);
                    sectionText.fontStyle = FontStyle.Bold;
                    UiFactory.SetSize(sectionText, width, 22f);
                    continue;
                }

                if (line.StartsWith("Classified"))
                {
                    UiFactory.CreateDivider(container);
                    bool eliminated = line.Contains("ELIMINATED");
                    Text statusText = UiFactory.CreateText(container, "Breakdown status " + i, line.Trim(), 15, eliminated ? UiFactory.AccentAmber : UiFactory.AccentGreen, TextAnchor.UpperLeft);
                    statusText.fontStyle = FontStyle.Bold;
                    statusText.verticalOverflow = VerticalWrapMode.Overflow;
                    UiFactory.SetSize(statusText, width, 42f);
                    continue;
                }

                string[] parts = Regex.Split(line.Trim(), "\\s{2,}");
                string label = parts.Length > 0 ? parts[0] : line.Trim();
                string value = parts.Length > 1 ? string.Join(" ", parts, 1, parts.Length - 1) : "";
                Color valueColor = UiFactory.TextPrimary;
                if (value.StartsWith("+"))
                {
                    valueColor = UiFactory.AccentAmber;
                }
                else if (value.StartsWith("-"))
                {
                    valueColor = UiFactory.AccentGreen;
                }
                else if (value == "clean lap")
                {
                    valueColor = UiFactory.AccentGreen;
                }

                if (label.ToUpperInvariant() == "FINAL LAP")
                {
                    UiFactory.CreateDivider(container);
                    valueColor = UiFactory.TextPrimary;
                }

                UiFactory.CreateBreakdownRow(container, label, value, valueColor, width);
            }
        }

        // Short display label for a team id on classification rows.
        string TeamLabel(RaceManager race, string teamId)
        {
            if (race != null && race.Data != null)
            {
                TeamData team = race.Data.FindTeam(teamId);
                if (team != null && !string.IsNullOrEmpty(team.shortName))
                {
                    return team.shortName.ToUpperInvariant();
                }
            }

            return string.IsNullOrEmpty(teamId) ? "--" : teamId.ToUpperInvariant();
        }

        // RaceParticipant.ResultPenaltyReason() bakes the retirement reason into
        // penaltyReason as either "DNF <reason>" or "<penalty>, DNF <reason>".
        // The results row already shows the bare "DNF" tag in the Gap column;
        // this pulls out just the human reason (e.g. "Collision damage",
        // "Mechanical failure", "Stranded") for the Pen/Status column.
        string DnfReasonLabel(string penaltyReason)
        {
            if (string.IsNullOrEmpty(penaltyReason))
            {
                return "Retired";
            }

            int index = penaltyReason.IndexOf("DNF ");
            if (index < 0)
            {
                return "Retired";
            }

            return penaltyReason.Substring(index + 4);
        }

        Color TyreDotColor(string tyreName)
        {
            if (string.IsNullOrEmpty(tyreName))
            {
                return new Color(0.4f, 0.5f, 0.58f);
            }

            if (tyreName.StartsWith("Soft"))
            {
                return new Color(1f, 0.1f, 0.08f);
            }

            if (tyreName.StartsWith("Medium"))
            {
                return new Color(1f, 0.9f, 0.18f);
            }

            if (tyreName.StartsWith("Hard"))
            {
                return new Color(0.94f, 0.96f, 0.98f);
            }

            if (tyreName.StartsWith("Intermediate"))
            {
                return new Color(0.18f, 1f, 0.28f);
            }

            if (tyreName.StartsWith("Wet"))
            {
                return new Color(0.18f, 0.46f, 1f);
            }

            return new Color(0.34f, 0.78f, 1f);
        }

        string WeatherProfileText(string profile)
        {
            if (string.IsNullOrEmpty(profile))
            {
                return "Dry";
            }

            string normalized = profile.ToLowerInvariant();
            if (normalized.Contains("wet"))
            {
                return "Wet";
            }

            if (normalized.Contains("mixed"))
            {
                return "Mixed / changing";
            }

            if (normalized.Contains("cloud"))
            {
                return "Cloudy";
            }

            if (normalized.Contains("hot"))
            {
                return "Dry, hot track";
            }

            return "Dry";
        }

        string BuildWeatherBriefing(CalendarEventData current, string sessionName, string selectedCompound)
        {
            return BuildWeatherBriefing(current, sessionName, selectedCompound, "");
        }

        // Every circuit runs exactly two DRS zones (see the drsZoneOne/drsZoneTwo
        // pair set per track in TrackManager), so that line is a constant fact
        // rather than something that needs a live TrackRuntime at menu time.
        string BuildWeatherBriefing(CalendarEventData current, string sessionName, string selectedCompound, string objectiveText)
        {
            string profile = current == null ? "" : current.weatherProfile;
            int air;
            int track;
            WeatherTemperatures(profile, out air, out track);
            string text =
                (current == null ? "Prototype GP" : current.displayName) + "\n" +
                "Session: " + sessionName + "\n" +
                "Current weather: " + CurrentWeatherText(profile) + "\n" +
                "Forecast: " + ForecastText(profile) + "\n" +
                "Track condition: " + TrackConditionText(profile) + "\n" +
                "Track temp: " + track + " C   Air temp: " + air + " C\n" +
                "DRS zones: 2\n" +
                "Recommended compound: " + RecommendedTyreText(profile) + "\n" +
                "Selected compound: " + selectedCompound;
            if (!string.IsNullOrEmpty(objectiveText))
            {
                text += "\nObjective: " + objectiveText;
            }

            return text;
        }

        string CurrentWeatherText(string profile)
        {
            string normalized = string.IsNullOrEmpty(profile) ? "" : profile.ToLowerInvariant();
            if (normalized.Contains("wet"))
            {
                return "Light rain";
            }

            if (normalized.Contains("mixed"))
            {
                return "Changeable cloud with damp patches";
            }

            if (normalized.Contains("cloud"))
            {
                return "Cloudy";
            }

            if (normalized.Contains("night"))
            {
                return "Clear night";
            }

            if (normalized.Contains("twilight"))
            {
                return "Clear twilight";
            }

            if (normalized.Contains("hot"))
            {
                return "Clear and hot";
            }

            return WeatherProfileText(profile);
        }

        string ForecastText(string profile)
        {
            string normalized = string.IsNullOrEmpty(profile) ? "" : profile.ToLowerInvariant();
            if (normalized.Contains("wet"))
            {
                return "Rain likely to continue; wet line off-line";
            }

            if (normalized.Contains("mixed"))
            {
                return "Showers possible; track may dry late";
            }

            if (normalized.Contains("cloud"))
            {
                return "Stable cloud cover, low rain risk";
            }

            if (normalized.Contains("hot"))
            {
                return "Dry and abrasive, rear temperatures rising";
            }

            return "Stable dry running expected";
        }

        string TrackConditionText(string profile)
        {
            string normalized = string.IsNullOrEmpty(profile) ? "" : profile.ToLowerInvariant();
            if (normalized.Contains("wet"))
            {
                return "Wet";
            }

            if (normalized.Contains("mixed"))
            {
                return "Damp, drying";
            }

            return "Dry";
        }

        string RecommendedTyreText(string profile)
        {
            string normalized = string.IsNullOrEmpty(profile) ? "" : profile.ToLowerInvariant();
            if (normalized.Contains("wet"))
            {
                return "Intermediate";
            }

            if (normalized.Contains("mixed"))
            {
                return "Intermediate if damp, Medium if drying";
            }

            if (normalized.Contains("hot"))
            {
                return "Medium or Hard";
            }

            return "Soft for qualifying, Medium for race";
        }

        void WeatherTemperatures(string profile, out int air, out int track)
        {
            string normalized = string.IsNullOrEmpty(profile) ? "" : profile.ToLowerInvariant();
            if (normalized.Contains("wet"))
            {
                air = 19;
                track = 23;
                return;
            }

            if (normalized.Contains("mixed"))
            {
                air = 22;
                track = 27;
                return;
            }

            if (normalized.Contains("hot"))
            {
                air = 33;
                track = 47;
                return;
            }

            if (normalized.Contains("warm"))
            {
                air = 27;
                track = 36;
                return;
            }

            if (normalized.Contains("night"))
            {
                air = 24;
                track = 28;
                return;
            }

            air = 22;
            track = 31;
        }

        string OnOff(bool value)
        {
            return value ? "On" : "Off";
        }

        // Shared row builder for the five audio volume categories - a
        // percentage cycle control in 10% steps, matching the existing
        // sensitivity-slider rows (CycleFloat) rather than a new widget type.
        // Applies immediately to SimpleAudioManager so a volume change is
        // audible without leaving the settings screen.
        void AddVolumeRow(RectTransform list, string label, GameSettingsStore settings, System.Func<float> getValue, System.Action<float> setValue, GameDataRepository data, CareerManager career)
        {
            RectTransform control;
            UiFactory.CreateSettingRow(list, label, "", out control);
            UiFactory.CreateCycleControl(control, Mathf.RoundToInt(getValue() * 100f) + "%", () =>
            {
                setValue(CycleFloat(getValue(), 0f, 1f, 0.1f));
                settings.Save();
                SimpleAudioManager.ApplySettings(settings.Current);
                ShowSettings(data, career, settings);
            });
        }

        float CycleFloat(float value, float min, float max, float step)
        {
            value += step;
            if (value > max + 0.001f)
            {
                value = min;
            }

            return value;
        }

        string NextTyreName(string current)
        {
            if (current == "Soft")
            {
                return "Medium";
            }

            if (current == "Medium")
            {
                return "Hard";
            }

            if (current == "Hard")
            {
                return "Intermediate";
            }

            if (current == "Intermediate")
            {
                return "Wet";
            }

            return "Soft";
        }

        // Fuel system pass: cycle order matches the enum's own light-to-heavy
        // ordering (AggressiveUnderfuel -> ... -> Heavy -> wraps back).
        FuelLoadChoice NextFuelLoadChoice(FuelLoadChoice current)
        {
            switch (current)
            {
                case FuelLoadChoice.AggressiveUnderfuel: return FuelLoadChoice.LightUnderfuel;
                case FuelLoadChoice.LightUnderfuel: return FuelLoadChoice.Target;
                case FuelLoadChoice.Target: return FuelLoadChoice.Safe;
                case FuelLoadChoice.Safe: return FuelLoadChoice.Heavy;
                default: return FuelLoadChoice.AggressiveUnderfuel;
            }
        }

        string FuelLoadChoiceLabel(FuelLoadChoice choice)
        {
            switch (choice)
            {
                case FuelLoadChoice.AggressiveUnderfuel: return "Aggressive Underfuel";
                case FuelLoadChoice.LightUnderfuel: return "Light Underfuel";
                case FuelLoadChoice.Safe: return "Safe Fuel";
                case FuelLoadChoice.Heavy: return "Heavy Fuel";
                default: return "Target Fuel";
            }
        }

        string FuelLoadChoiceDescription(FuelLoadChoice choice)
        {
            switch (choice)
            {
                case FuelLoadChoice.AggressiveUnderfuel: return "Lighter, fastest - requires major lift-and-coast to make the finish.";
                case FuelLoadChoice.LightUnderfuel: return "Small pace gain - requires some fuel saving through the race.";
                case FuelLoadChoice.Safe: return "Small margin over target - slightly heavier, safer.";
                case FuelLoadChoice.Heavy: return "Safest load - slower at the start, no fuel-saving needed.";
                default: return "Enough to finish with normal driving, no saving required.";
            }
        }
    }
}
