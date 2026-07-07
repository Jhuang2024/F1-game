using System.Collections.Generic;
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
            RectTransform menu = UiFactory.CreateRect(background, "Menu", new Vector2(0.06f, 0.16f), new Vector2(0.32f, 0.6f), Vector2.zero, Vector2.zero);
            UiFactory.AddVerticalLayout(menu, 9, new RectOffset(0, 0, 0, 0));
            UiFactory.CreatePrimaryButton(menu, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateButton(menu, "Quick Race", bootstrap.StartQuickRace);
            UiFactory.CreateButton(menu, "Time Trial", bootstrap.ShowTimeTrialSetup);
            UiFactory.CreateSecondaryButton(menu, "Settings", () => ShowSettings(data, career, settings));
            UiFactory.CreateSecondaryButton(menu, "Quit", Application.Quit);

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
            RectTransform secondaryCard = UiFactory.CreateGlassPanel(background, "Secondary actions card", new Vector2(0.05f, 0.14f), new Vector2(0.36f, 0.33f), Vector2.zero, Vector2.zero, UiFactory.PanelDarker);
            RectTransform secondaryGrid = UiFactory.CreateRect(secondaryCard, "Secondary actions grid", Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -16f));
            GridLayoutGroup secondaryLayout = secondaryGrid.gameObject.AddComponent<GridLayoutGroup>();
            secondaryLayout.spacing = new Vector2(10f, 10f);
            secondaryLayout.cellSize = new Vector2(255f, 46f);
            secondaryLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            secondaryLayout.constraintCount = 2;
            UiFactory.CreateSecondaryButton(secondaryGrid, "Track Info", bootstrap.ShowTrackInfo);
            UiFactory.CreateSecondaryButton(secondaryGrid, "Driver Ratings", () => ShowDriverRatings(data, career, settings));
            UiFactory.CreateSecondaryButton(secondaryGrid, "Career Stats", () => ShowCareerStats(data, career, settings));
            UiFactory.CreateSecondaryButton(secondaryGrid, "Driver & Team", () => ShowCareerSetup(data, career, settings));

            RectTransform middle = UiFactory.CreateCard(background, "Standings panel", new Vector2(0.39f, 0.14f), new Vector2(0.66f, 0.77f));
            Text standings = UiFactory.CreateText(middle, "Standings", BuildStandingsText(career.Save.driverStandings, "Driver Standings") + "\n" + BuildStandingsText(career.Save.constructorStandings, "Constructors"), 18, Color.white, TextAnchor.UpperLeft);
            RectTransform standingsRect = standings.GetComponent<RectTransform>();
            standingsRect.anchorMin = Vector2.zero;
            standingsRect.anchorMax = Vector2.one;
            standingsRect.offsetMin = new Vector2(22f, 22f);
            standingsRect.offsetMax = new Vector2(-22f, -22f);
            standings.verticalOverflow = VerticalWrapMode.Overflow;

            // R&D: scrollable upgrade list grouped by category, with cost/state pills.
            RectTransform right = UiFactory.CreateScrollPanel(background, "Upgrades panel", new Vector2(0.69f, 0.14f), new Vector2(0.95f, 0.77f), 8, new RectOffset(20, 20, 18, 18));
            UiFactory.CreateSubHeader(right, "R&D Development");
            TeamData careerTeam = data.FindTeam(career.Save.playerTeamId);
            CarPerformanceData baseCar = careerTeam == null ? null : data.FindCar(careerTeam.carPerformanceId);
            CarPerformanceData tunedCar = baseCar == null ? null : career.ApplyCareerUpgrades(baseCar);
            if (baseCar != null && tunedCar != null)
            {
                Text carStats = UiFactory.CreateText(right, "Car performance", BuildCarPerformanceText(baseCar, tunedCar), 15, new Color(0.72f, 0.84f, 0.9f), TextAnchor.UpperLeft);
                carStats.verticalOverflow = VerticalWrapMode.Overflow;
                UiFactory.SetSize(carStats, 390f, 104f);
            }

            string lastCategory = null;
            for (int i = 0; i < data.Upgrades.upgrades.Count; i++)
            {
                UpgradeData upgrade = data.Upgrades.upgrades[i];
                if (!string.IsNullOrEmpty(upgrade.category) && upgrade.category != lastCategory)
                {
                    lastCategory = upgrade.category;
                    Text categoryText = UiFactory.CreateText(right, "Upgrade category " + i, lastCategory.ToUpperInvariant(), 14, UiFactory.Accent, TextAnchor.MiddleLeft);
                    UiFactory.SetSize(categoryText, 360f, 22f);
                }

                CreateUpgradeCard(right, data, career, settings, upgrade);
            }

            // Race Weekend already lives on the event card above - the footer is
            // for navigation, not a second path to the same primary action.
            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
            UiFactory.CreateSecondaryButton(footerLeft, "Settings", () => ShowSettings(data, career, settings));
        }

        // Separate onboarding/setup flow for choosing a driver name, team, or an
        // existing driver to play as - kept out of the day-to-day career dashboard.
        public void ShowCareerSetup(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Career setup background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "Driver & Team", "Choose who you race as, then start a new career with that setup.");

            RectTransform left = UiFactory.CreateCard(background, "Setup identity card", new Vector2(0.05f, 0.14f), new Vector2(0.5f, 0.82f));
            RectTransform identityList = UiFactory.CreateRect(left, "Identity list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(identityList, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(identityList, "Driver Name");
            InputField nameInput = UiFactory.CreateInputField(identityList, career.Save.playerDriverName);

            UiFactory.CreateSubHeader(identityList, "Starting Team");
            Text selectedTeam = UiFactory.CreateText(identityList, "Selected team", data.FindTeam(selectedTeamId).name, 20, Color.white, TextAnchor.MiddleLeft);
            RectTransform teamGrid = UiFactory.CreateRect(identityList, "Team buttons", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            teamGrid.sizeDelta = new Vector2(560f, 240f);
            GridLayoutGroup grid = teamGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(134f, 36f);
            grid.spacing = new Vector2(8f, 8f);
            for (int i = 0; i < data.Teams.teams.Count; i++)
            {
                TeamData team = data.Teams.teams[i];
                UiFactory.CreateButton(teamGrid, team.shortName, () =>
                {
                    selectedTeamId = team.id;
                    selectedTeam.text = team.name;
                });
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
                ShowCareerSetup(data, career, settings);
            });
            UiFactory.SetSize(modeButton, 300f, 40f);

            RectTransform driverGrid = UiFactory.CreateRect(driverList, "Driver buttons", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            driverGrid.sizeDelta = new Vector2(560f, 260f);
            GridLayoutGroup driverGridLayout = driverGrid.gameObject.AddComponent<GridLayoutGroup>();
            driverGridLayout.cellSize = new Vector2(176f, 34f);
            driverGridLayout.spacing = new Vector2(8f, 8f);
            int driverButtons = Mathf.Min(15, data.Drivers.drivers.Count);
            for (int i = 0; i < driverButtons; i++)
            {
                DriverData driver = data.Drivers.drivers[i];
                UiFactory.CreateButton(driverGrid, driver.abbreviation + " " + driver.displayName, () =>
                {
                    selectedDriverId = driver.id;
                    useExistingDriver = true;
                    TeamData driverTeam = data.FindTeam(driver.teamId);
                    selectedTeamId = driver.teamId;
                    selectedTeam.text = driverTeam == null ? driver.teamId : driverTeam.name;
                    nameInput.text = driver.displayName;
                    ShowCareerSetup(data, career, settings);
                });
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Cancel", () => ShowCareerHub(data, career, settings));
            UiFactory.CreatePrimaryButton(footerRight, "Start New Career", () =>
            {
                career.StartNewCareer(nameInput.text, selectedTeamId, useExistingDriver, selectedDriverId);
                ShowCareerHub(data, career, settings);
            });
        }

        // One R&D node: name + state pill on top, stat deltas and cost underneath.
        // Available nodes are clickable; done/failed/locked nodes explain themselves.
        void CreateUpgradeCard(RectTransform parent, GameDataRepository data, CareerManager career, GameSettingsStore settings, UpgradeData upgrade)
        {
            bool done = career.Save.completedUpgradeIds.Contains(upgrade.id);
            bool failed = career.Save.failedUpgradeIds.Contains(upgrade.id);
            bool locked = !string.IsNullOrEmpty(upgrade.requiredUpgradeId) && !career.Save.completedUpgradeIds.Contains(upgrade.requiredUpgradeId);
            bool affordable = career.Save.resourcePoints >= upgrade.cost;
            bool available = !done && !failed && !locked;

            RectTransform card = UiFactory.CreateRect(parent, upgrade.id + " card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(384f, 78f);
            Image background = card.gameObject.AddComponent<Image>();
            background.color = available ? UiFactory.PanelDark : new Color(0.014f, 0.02f, 0.026f, 0.7f);

            Color stateColor = done ? UiFactory.AccentGreen : (failed ? UiFactory.Accent : (locked ? UiFactory.TextMuted : UiFactory.AccentCyan));
            UiFactory.CreateBand(card, "Upgrade state rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), stateColor);

            Text nameText = UiFactory.CreateText(card, "Upgrade name", upgrade.displayName, 16, available ? UiFactory.TextPrimary : UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform nameRect = nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0.7f, 1f);
            nameRect.offsetMin = new Vector2(14f, -28f);
            nameRect.offsetMax = new Vector2(0f, -6f);

            string state = done ? "DONE" : (failed ? "FAILED" : (locked ? "LOCKED" : (affordable ? "BUY" : "NEED RP")));
            Text stateText = UiFactory.CreateText(card, "Upgrade state", state, 13, stateColor, TextAnchor.UpperRight);
            RectTransform stateRect = stateText.GetComponent<RectTransform>();
            stateRect.anchorMin = new Vector2(0.7f, 1f);
            stateRect.anchorMax = new Vector2(1f, 1f);
            stateRect.offsetMin = new Vector2(0f, -28f);
            stateRect.offsetMax = new Vector2(-12f, -6f);

            string costLine = locked
                ? "Requires earlier project in this category"
                : upgrade.cost + " RP   " + Mathf.RoundToInt(upgrade.successChance * 100f) + "% success";
            Text costText = UiFactory.CreateText(card, "Upgrade cost", costLine, 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform costRect = costText.GetComponent<RectTransform>();
            costRect.anchorMin = new Vector2(0f, 1f);
            costRect.anchorMax = new Vector2(1f, 1f);
            costRect.offsetMin = new Vector2(14f, -50f);
            costRect.offsetMax = new Vector2(-12f, -30f);

            Text deltaText = UiFactory.CreateText(card, "Upgrade deltas", BuildUpgradeDeltaText(upgrade), 13, new Color(0.55f, 0.95f, 0.65f), TextAnchor.UpperLeft);
            RectTransform deltaRect = deltaText.GetComponent<RectTransform>();
            deltaRect.anchorMin = new Vector2(0f, 1f);
            deltaRect.anchorMax = new Vector2(1f, 1f);
            deltaRect.offsetMin = new Vector2(14f, -72f);
            deltaRect.offsetMax = new Vector2(-12f, -52f);

            if (available && affordable)
            {
                Button buy = card.gameObject.AddComponent<Button>();
                buy.targetGraphic = background;
                ColorBlock colors = buy.colors;
                colors.normalColor = UiFactory.PanelDark;
                colors.highlightedColor = new Color(0.1f, 0.16f, 0.22f, 1f);
                colors.pressedColor = new Color(0.006f, 0.01f, 0.014f, 1f);
                colors.selectedColor = colors.highlightedColor;
                buy.colors = colors;
                buy.onClick.AddListener(() =>
                {
                    SimpleAudioManager.PlayClick();
                    career.TryPurchaseUpgrade(upgrade.id);
                    ShowCareerHub(data, career, settings);
                });
            }
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
            RectTransform list = UiFactory.CreateRect(panel, "Gameplay list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(list, 8, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(list, "Session");

            RectTransform lapsControl;
            UiFactory.CreateSettingRow(list, "Race Laps", "Shorter for quick sessions, longer for a full-length race.", out lapsControl);
            UiFactory.CreateCycleControl(lapsControl, settings.Current.laps.ToString(), () =>
            {
                settings.Current.laps = settings.Current.laps == 3 ? 5 : (settings.Current.laps == 5 ? 14 : 3);
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
                ShowSettings(data, career, settings);
            });
        }

        public void ShowDisplaySettings(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Display settings background", new Color(0.012f, 0.016f, 0.021f, 1f));
            BuildSettingsTabBar(background, data, career, settings, 2);

            RectTransform left = UiFactory.CreateCard(background, "HUD card", new Vector2(0.06f, 0.12f), new Vector2(0.5f, 0.76f));
            RectTransform leftList = UiFactory.CreateRect(left, "HUD list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(leftList, 8, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(leftList, "HUD & Camera");

            RectTransform hudScaleControl;
            UiFactory.CreateSettingRow(leftList, "HUD Scale", "Scales every in-race panel around its own screen edge, so nothing clips off.", out hudScaleControl);
            UiFactory.CreateCycleControl(hudScaleControl, settings.Current.hudScale.ToString("0.00"), () =>
            {
                settings.Current.hudScale = CycleFloat(settings.Current.hudScale, 0.8f, 1.25f, 0.15f);
                settings.Save();
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

            RectTransform shakeAmountControl;
            UiFactory.CreateSettingRow(leftList, "Camera Movement", "How strongly speed, kerbs, braking, and impacts move the camera.", out shakeAmountControl);
            string[] shakeLabels = { "0.0", "0.1", "0.2", "0.3", "0.4", "0.5" };
            int shakeIndex = Mathf.Clamp(Mathf.RoundToInt(settings.Current.cameraShakeStrength * 10f), 0, 5);
            UiFactory.CreateSegmentedControl(shakeAmountControl, shakeLabels, shakeIndex, index =>
            {
                settings.Current.cameraShakeStrength = index * 0.1f;
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

            RectTransform right = UiFactory.CreateCard(background, "Graphics card", new Vector2(0.54f, 0.12f), new Vector2(0.94f, 0.76f));
            RectTransform rightList = UiFactory.CreateRect(right, "Graphics list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(rightList, 8, new RectOffset(0, 0, 0, 0));
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

        public void ShowControls(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Controls background", new Color(0.012f, 0.016f, 0.021f, 1f));
            BuildSettingsTabBar(background, data, career, settings, 3);

            RectTransform panel = UiFactory.CreateCard(background, "Controls card", new Vector2(0.06f, 0.12f), new Vector2(0.72f, 0.76f));
            Text controls = UiFactory.CreateText(panel, "Controls text", BuildControlsText(), 19, new Color(0.86f, 0.92f, 0.95f), TextAnchor.UpperLeft);
            RectTransform controlsRect = controls.GetComponent<RectTransform>();
            controlsRect.anchorMin = Vector2.zero;
            controlsRect.anchorMax = Vector2.one;
            controlsRect.offsetMin = new Vector2(28f, 28f);
            controlsRect.offsetMax = new Vector2(-28f, -28f);
            controls.verticalOverflow = VerticalWrapMode.Overflow;
        }

        public void ShowDriverRatings(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Driver ratings background", new Color(0.006f, 0.009f, 0.014f, 1f));
            UiFactory.CreateScreenHeader(background, "Driver Ratings", "Overall is calculated from qualifying, defending, overtaking, and race pace.");

            RectTransform content = UiFactory.CreateScrollPanel(background, "Ratings table", new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.85f), 3, new RectOffset(18, 18, 12, 12));
            string header = Pad("OVR", 6) + Pad("DVR", 6) + Pad("TEAM", 7) + Pad("DRIVER", 26) + Pad("QUAL", 7) + Pad("DEF", 7) + Pad("OVT", 7) + "PACE";
            Text headerText = UiFactory.CreateText(content, "Ratings header", header, 16, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.SetSize(headerText, 1240f, 28f);

            List<DriverData> drivers = new List<DriverData>(data.Drivers.drivers);
            drivers.Sort((a, b) =>
            {
                int overall = b.OverallRating.CompareTo(a.OverallRating);
                return overall != 0 ? overall : b.pace.CompareTo(a.pace);
            });

            for (int i = 0; i < drivers.Count; i++)
            {
                DriverData driver = drivers[i];
                TeamData team = data.FindTeam(driver.teamId);
                string teamCode = team == null ? driver.teamId.ToUpperInvariant() : team.shortName.ToUpperInvariant();
                string ovrColor = driver.OverallRating >= 90 ? "#B86CFF" : (driver.OverallRating >= 85 ? "#63FF82" : (driver.OverallRating >= 78 ? "#FFD45C" : "#AAB8C0"));
                string line = "<color=" + ovrColor + ">" + Pad(driver.OverallRating.ToString("00"), 6) + "</color>" +
                              Pad(driver.abbreviation.ToUpperInvariant(), 6) +
                              Pad(teamCode, 7) +
                              Pad(driver.displayName, 26) +
                              Pad(driver.qualifying.ToString("00"), 7) +
                              Pad(driver.defending.ToString("00"), 7) +
                              Pad(driver.overtaking.ToString("00"), 7) +
                              driver.pace.ToString("00");
                Text row = UiFactory.CreateText(content, "Ratings row " + i, line, 16, i % 2 == 0 ? new Color(0.9f, 0.95f, 0.98f) : UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.SetSize(row, 1240f, 26f);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        public void ShowAssists(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Assists background", new Color(0.012f, 0.016f, 0.021f, 1f));
            BuildSettingsTabBar(background, data, career, settings, 1);
            RectTransform card = UiFactory.CreateCard(background, "Assists card", new Vector2(0.06f, 0.12f), new Vector2(0.6f, 0.76f));
            RectTransform panel = UiFactory.CreateRect(card, "Assists panel", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(panel, 8, new RectOffset(0, 0, 0, 0));
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

            // Middle: the current grid / qualifying result.
            RectTransform gridPanel = UiFactory.CreateCard(background, "Weekend grid", new Vector2(0.36f, 0.14f), new Vector2(0.64f, 0.76f));
            List<QualifyingResultEntry> currentGrid = hasQualifying ? career.Save.lastQualifyingResults : null;
            Text grid = UiFactory.CreateText(gridPanel, "Grid", BuildQualifyingText(currentGrid), 16, Color.white, TextAnchor.UpperLeft);
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            gridRect.anchorMin = Vector2.zero;
            gridRect.anchorMax = Vector2.one;
            gridRect.offsetMin = new Vector2(22f, 20f);
            gridRect.offsetMax = new Vector2(-22f, -20f);
            grid.verticalOverflow = VerticalWrapMode.Overflow;

            // Right: session actions grouped by what they actually are, instead of
            // one long vertical stack of eight identical-looking buttons.
            RectTransform actions = UiFactory.CreateRect(background, "Weekend actions", new Vector2(0.66f, 0.14f), new Vector2(0.95f, 0.76f), Vector2.zero, Vector2.zero);
            UiFactory.AddVerticalLayout(actions, 12, new RectOffset(0, 0, 0, 0));

            CreateWeekendActionGroup(actions, "Race", hasQualifying ? "Grid is set from qualifying." : "Runs qualifying first if needed.",
                hasQualifying ? "Go to Race" : "Go to Race (runs qualifying)", bootstrap.StartCareerRace, null, null);
            CreateWeekendActionGroup(actions, "Qualifying", "Drive the session yourself, or simulate it.",
                "Go to Qualifying", bootstrap.StartCareerQualifying, "Sim Qualifying", bootstrap.StartCareerSimQualifying);
            CreateWeekendActionGroup(actions, "Practice", "Optional programs for resource points.",
                "Practice Programs", () => ShowPracticePrograms(data, career, settings), null, null);
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
            CalendarEventData current = careerRace ? career.CurrentEvent() : (data.Calendar.events.Count > 0 ? data.Calendar.events[0] : career.CurrentEvent());
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
            // keep a 1-stop plan compact). Card height below is sized for the
            // worst case (2-stop, every row present) so it never clips.
            RectTransform pitCard = UiFactory.CreateGlassPanel(bodyMargin, "Pit strategy card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, UiFactory.PanelDarker);
            UiFactory.SetFixedRowHeight(pitCard, 452f);
            RectTransform pitList = UiFactory.CreateRect(pitCard, "Pit strategy list", Vector2.zero, Vector2.one, new Vector2(28f, 16f), new Vector2(-28f, -16f));
            UiFactory.AddVerticalLayout(pitList, 6, new RectOffset(0, 0, 0, 0));
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
            // 2-stop, "Start Soft -> Lap 4 Medium -> Lap 8 Hard". Auto laps are
            // estimated the same way the old single-stop row did (55% of race
            // distance for stop 1), and a 2-stop's auto stop-2 lap mirrors
            // RaceManager.GetPlannedPitLapForStop(2)'s "two-thirds of what's left
            // after stop 1" reasoning so this preview roughly matches the race.
            int stop1LapEstimate = stop1Lap > 0 ? stop1Lap : Mathf.Max(1, Mathf.RoundToInt(raceLaps * 0.55f));
            string summaryLine = "Start " + settings.Current.tyreCompound + " → Lap " + stop1LapEstimate + " " + settings.Current.plannedStopOneCompound;
            if (stopCount == 2)
            {
                int stop2LapValue = settings.Current.plannedPitLapTwo;
                int stop2LapEstimate = stop2LapValue > 0
                    ? stop2LapValue
                    : Mathf.Clamp(stop1LapEstimate + Mathf.RoundToInt(Mathf.Max(1, raceLaps - stop1LapEstimate) * 0.66f), stop1LapEstimate + 1, raceLaps - 1);
                summaryLine += " → Lap " + stop2LapEstimate + " " + settings.Current.plannedStopTwoCompound;
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
            UiFactory.CreateSecondaryButton(footerLeft, careerRace ? "Back to Weekend" : "Back to Menu", () =>
            {
                if (careerRace)
                {
                    bootstrap.ShowRaceWeekend();
                }
                else
                {
                    bootstrap.ShowMainMenu();
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

            Text descriptorText = UiFactory.CreateText(card, tyreName + " descriptor", TyreShortDescriptor(tyreName), 12, selected ? new Color(1f, 0.88f, 0.86f) : UiFactory.TextMuted, TextAnchor.UpperLeft);
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

        public void ShowTimeTrialSetup(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Time trial background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateTopNav(background, "Time Trial");
            Text subtitle = UiFactory.CreateText(background, "Time trial subtitle", "Pick a circuit. No AI, unlimited laps, best lap saved locally. Tyre: " + settings.Current.tyreCompound + " (change in Settings).", 18, UiFactory.TextMuted, TextAnchor.UpperLeft);
            subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(66f, -112f);
            UiFactory.SetSize(subtitle, 1300f, 30f);

            RectTransform content = UiFactory.CreateScrollPanel(background, "Time trial track list", new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.84f), 8, new RectOffset(18, 18, 14, 14));
            for (int i = 0; i < data.Calendar.events.Count; i++)
            {
                CalendarEventData raceEvent = data.Calendar.events[i];
                float best = PlayerRecordsStore.GetBestLap(raceEvent.trackId);
                string bestLabel = best > 0f ? "BEST " + UiFactory.FormatTime(best) : "NO RECORD";
                UnityEngine.UI.Button row = UiFactory.CreateButton(
                    content,
                    "R" + raceEvent.round.ToString("00") + "   " + raceEvent.displayName + "    " + WeatherProfileText(raceEvent.weatherProfile).ToUpper() + "    " + bestLabel,
                    () => bootstrap.BeginTimeTrial(raceEvent));
                UiFactory.SetSize(row, 1240f, 46f);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Car Setup", () => ShowCarSetup(data, career, settings, () => ShowTimeTrialSetup(data, career, settings)));
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        public void ShowTrackInfo(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Track info background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateScreenHeader(background, "Track Info", "Select a circuit to start a time trial there. Traits describe how each layout races.");

            RectTransform content = UiFactory.CreateScrollPanel(background, "Track info list", new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.86f), 6, new RectOffset(18, 18, 14, 14));
            for (int i = 0; i < data.Calendar.events.Count; i++)
            {
                CalendarEventData raceEvent = data.Calendar.events[i];
                CreateTrackInfoRow(content, raceEvent);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
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
            RectTransform list = UiFactory.CreateRect(left, "Setup list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(list, 8, new RectOffset(0, 0, 0, 0));
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

                string line = Pad(trackName, 40) + Pad(UiFactory.FormatTime(entry.bestLapTime), 14) + (string.IsNullOrEmpty(entry.context) ? "" : entry.context.ToUpperInvariant());
                Text row = UiFactory.CreateText(content, "Record row " + i, line, 16, i % 2 == 0 ? new Color(0.9f, 0.95f, 0.98f) : UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.SetSize(row, 1100f, 26f);
            }

            RectTransform footerLeft;
            RectTransform footerRight;
            UiFactory.CreateFooterBar(background, out footerLeft, out footerRight);
            UiFactory.CreateSecondaryButton(footerLeft, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateSecondaryButton(footerLeft, "Main Menu", () => ShowMainMenu(data, career, settings));
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
                UnityEngine.UI.Button run = UiFactory.CreateButton(card, "Run", () =>
                {
                    career.Save.completedPracticePrograms.Add(key);
                    career.Save.resourcePoints += resourceReward;
                    career.Save.reputation += reputationReward;
                    career.Write();
                    ShowPracticePrograms(data, career, settings);
                });
                RectTransform runRect = run.GetComponent<RectTransform>();
                runRect.anchorMin = new Vector2(0.88f, 0.5f);
                runRect.anchorMax = new Vector2(0.88f, 0.5f);
                runRect.sizeDelta = new Vector2(110f, 44f);
                runRect.anchoredPosition = new Vector2(55f, 0f);
            }
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
            RectTransform card = UiFactory.CreateRect(parent, raceEvent.trackId + " info card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(1180f, 72f);
            Image background = card.gameObject.AddComponent<Image>();
            background.color = UiFactory.PanelDark;
            UiFactory.CreateBand(card, "Track info rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.Accent);

            Text titleText = UiFactory.CreateText(card, "Track title", "R" + raceEvent.round.ToString("00") + "  " + raceEvent.displayName, 18, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            RectTransform titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(0.52f, 1f);
            titleRect.offsetMin = new Vector2(16f, -30f);
            titleRect.offsetMax = new Vector2(0f, -6f);

            Text metaText = UiFactory.CreateText(card, "Track meta", raceEvent.country + "   ·   " + WeatherProfileText(raceEvent.weatherProfile).ToUpperInvariant() + "   ·   " + raceEvent.laps25Percent + " LAPS", 14, UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform metaRect = metaText.GetComponent<RectTransform>();
            metaRect.anchorMin = new Vector2(0f, 0f);
            metaRect.anchorMax = new Vector2(0.52f, 1f);
            metaRect.offsetMin = new Vector2(16f, 8f);
            metaRect.offsetMax = new Vector2(0f, -34f);

            Text traitsText = UiFactory.CreateText(card, "Track traits", TrackTraits(raceEvent), 14, UiFactory.AccentCyan, TextAnchor.MiddleLeft);
            RectTransform traitsRect = traitsText.GetComponent<RectTransform>();
            traitsRect.anchorMin = new Vector2(0.52f, 0f);
            traitsRect.anchorMax = new Vector2(0.84f, 1f);
            traitsRect.offsetMin = Vector2.zero;
            traitsRect.offsetMax = Vector2.zero;

            float best = PlayerRecordsStore.GetBestLap(raceEvent.trackId);
            Text bestText = UiFactory.CreateText(card, "Track best", best > 0f ? "BEST " + UiFactory.FormatTime(best) : "NO RECORD", 14, best > 0f ? UiFactory.AccentPurple : UiFactory.TextMuted, TextAnchor.MiddleRight);
            RectTransform bestRect = bestText.GetComponent<RectTransform>();
            bestRect.anchorMin = new Vector2(0.84f, 0f);
            bestRect.anchorMax = new Vector2(1f, 1f);
            bestRect.offsetMin = Vector2.zero;
            bestRect.offsetMax = new Vector2(-14f, 0f);

            Button launch = card.gameObject.AddComponent<Button>();
            launch.targetGraphic = background;
            ColorBlock colors = launch.colors;
            colors.normalColor = UiFactory.PanelDark;
            colors.highlightedColor = new Color(0.1f, 0.16f, 0.22f, 1f);
            colors.pressedColor = new Color(0.006f, 0.01f, 0.014f, 1f);
            colors.selectedColor = colors.highlightedColor;
            launch.colors = colors;
            launch.onClick.AddListener(() =>
            {
                SimpleAudioManager.PlayClick();
                bootstrap.BeginTimeTrial(raceEvent);
            });
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

        public void ShowResults(RaceManager race, List<RaceResultEntry> results, bool careerRace)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Results background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateTopNav(background, "Race Classification");

            // Highlight cards: winner, fastest lap, biggest mover, player result.
            RectTransform highlights = UiFactory.CreateRect(background, "Result highlights", new Vector2(0.06f, 0.76f), new Vector2(0.94f, 0.87f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(highlights, 14, new RectOffset(0, 0, 0, 0));
            if (results != null && results.Count > 0)
            {
                UiFactory.CreateStatCard(highlights, "Winner", results[0].driverName, 300f);
                RaceResultEntry fastest = FindFastestLap(results);
                if (fastest != null)
                {
                    UiFactory.CreateStatCard(highlights, "Fastest Lap", fastest.driverName + "  " + UiFactory.FormatTime(fastest.bestLapTime), 380f);
                }

                RaceResultEntry mover = FindBiggestMover(results);
                if (mover != null)
                {
                    UiFactory.CreateStatCard(highlights, "Biggest Mover", mover.driverName + "  +" + (mover.gridPosition - mover.finishingPosition), 320f);
                }

                RaceResultEntry player = results.Find(entry => entry.isPlayer);
                if (player != null)
                {
                    UiFactory.CreateStatCard(highlights, "You Finished", "P" + player.finishingPosition + "  (" + player.points + " pts)", 280f);
                }
            }

            RectTransform content = UiFactory.CreateScrollPanel(background, "Results table", new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.74f), 4, new RectOffset(18, 18, 12, 12));
            RectTransform headerRow = UiFactory.CreateTableRow(content, "Results header row", 1240f, 26f, false, 1);
            headerRow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            UiFactory.AddRowCell(headerRow, "H pos", "POS", 0.0f, 0.05f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H grid", "GRID", 0.05f, 0.1f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H driver", "DRIVER", 0.11f, 0.36f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H team", "TEAM", 0.36f, 0.5f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H tyre", "TYRE", 0.5f, 0.55f, 13, UiFactory.Accent, TextAnchor.MiddleCenter);
            UiFactory.AddRowCell(headerRow, "H gap", "TOTAL / GAP", 0.56f, 0.69f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H best", "BEST LAP", 0.69f, 0.81f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H pen", "PEN", 0.81f, 0.88f, 13, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.AddRowCell(headerRow, "H pts", "PTS", 0.88f, 0.97f, 13, UiFactory.Accent, TextAnchor.MiddleRight);
            if (results != null && results.Count > 0)
            {
                float winnerTime = results[0].totalTime + results[0].penaltiesSeconds;
                for (int i = 0; i < results.Count; i++)
                {
                    RaceResultEntry entry = results[i];
                    float classifiedTime = entry.totalTime + entry.penaltiesSeconds;
                    bool dnf = !string.IsNullOrEmpty(entry.penaltyReason) && entry.penaltyReason.Contains("DNF");
                    string gap = dnf ? "DNF" : (i == 0 ? UiFactory.FormatTime(classifiedTime) : "+" + (classifiedTime - winnerTime).ToString("0.0") + "s");
                    string penalties = entry.penaltiesSeconds > 0f ? "+" + entry.penaltiesSeconds.ToString("0") + "s" : "--";
                    RectTransform row = UiFactory.CreateTableRow(content, "Result row " + i, 1240f, 32f, entry.isPlayer, i);
                    UiFactory.AddPositionBadge(row, entry.finishingPosition, entry.isPlayer);
                    Color textColor = entry.isPlayer ? Color.white : (dnf ? UiFactory.TextMuted : new Color(0.9f, 0.95f, 0.98f));
                    UiFactory.AddRowCell(row, "Grid", entry.gridPosition > 0 ? entry.gridPosition.ToString() : "--", 0.05f, 0.1f, 14, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                    UiFactory.AddRowCell(row, "Driver", entry.driverName, 0.11f, 0.36f, 15, textColor, TextAnchor.MiddleLeft);
                    UiFactory.AddRowCell(row, "Team", TeamLabel(race, entry.teamId), 0.36f, 0.5f, 14, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                    UiFactory.AddRowDot(row, "Tyre dot", 0.525f, 13f, TyreDotColor(entry.tyreCompound));
                    UiFactory.AddRowCell(row, "Gap", gap, 0.56f, 0.69f, 14, dnf ? UiFactory.Accent : textColor, TextAnchor.MiddleLeft);
                    UiFactory.AddRowCell(row, "Best", UiFactory.FormatTime(entry.bestLapTime), 0.69f, 0.81f, 14, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                    UiFactory.AddRowCell(row, "Pen", penalties, 0.81f, 0.88f, 14, entry.penaltiesSeconds > 0f ? UiFactory.AccentAmber : UiFactory.TextMuted, TextAnchor.MiddleLeft);
                    UiFactory.AddRowCell(row, "Pts", entry.points.ToString(), 0.88f, 0.97f, 15, entry.points > 0 ? UiFactory.AccentGreen : UiFactory.TextMuted, TextAnchor.MiddleRight);
                }
            }
            else
            {
                Text empty = UiFactory.CreateText(content, "No results", "No results.", 18, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.SetSize(empty, 600f, 30f);
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
                Text explainBody = UiFactory.CreateText(explainCard, "Explain body", race.SimQualifyingExplanation, 16, new Color(0.85f, 0.92f, 0.96f), TextAnchor.UpperLeft);
                RectTransform explainBodyRect = explainBody.GetComponent<RectTransform>();
                explainBodyRect.anchorMin = Vector2.zero;
                explainBodyRect.anchorMax = Vector2.one;
                explainBodyRect.offsetMin = new Vector2(22f, 18f);
                explainBodyRect.offsetMax = new Vector2(-18f, -62f);
                explainBody.verticalOverflow = VerticalWrapMode.Overflow;
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
            string sessionLabel = race.IsTimeTrial ? "Time Trial" : (race.CurrentSession == RaceWeekendSession.Qualifying ? "Qualifying" : "Race");
            string eventLabel = race.EventData == null ? "Prototype GP" : race.EventData.displayName;
            UiFactory.CreateText(menu, "Pause session", sessionLabel + "  ·  " + eventLabel, 18, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            UiFactory.CreateDivider(menu);
            UiFactory.CreateSubHeader(menu, "Controls");
            Text controls = UiFactory.CreateText(menu, "Pause controls", "W/S throttle & brake   A/D steer\nSpace DRS   R ERS mode (hold: reset car)\nShift ERS override   C camera   P pit\nQ/E manual shift   F1 debug overlay   Esc resume", 16, new Color(0.78f, 0.86f, 0.9f), TextAnchor.UpperLeft);
            controls.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(controls, 460f, 92f);
            UiFactory.CreateDivider(menu);
            UiFactory.CreatePrimaryButton(menu, "Resume", race.Resume);
            UiFactory.CreateButton(menu, "Restart Session", race.RestartRace);
            UiFactory.CreateSecondaryButton(menu, "Main Menu", () =>
            {
                race.CleanupRaceWorld();
                bootstrap.ShowMainMenu();
            });
            UiFactory.CreateSecondaryButton(menu, "Quit Game", Application.Quit);
            pausePanel.SetActive(false);
        }

        string BuildStandingsText(List<StandingEntry> standings, string title)
        {
            string text = title + "\n\n";
            int count = Mathf.Min(10, standings.Count);
            for (int i = 0; i < count; i++)
            {
                StandingEntry entry = standings[i];
                text += (i + 1) + ". " + entry.displayName + "  " + entry.points + "\n";
            }

            return text;
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

        string BuildQualifyingText(List<QualifyingResultEntry> results)
        {
            if (results == null || results.Count == 0)
            {
                return "No qualifying run yet.\nRace will use the default grid.";
            }

            string text = "FINAL GRID\n\nPOS  DRIVER                 BEST        SESSION\n";
            for (int i = 0; i < results.Count; i++)
            {
                QualifyingResultEntry entry = results[i];
                string phase = string.IsNullOrEmpty(entry.eliminatedIn) ? entry.session : entry.eliminatedIn;
                if (string.IsNullOrEmpty(phase))
                {
                    phase = "Q";
                }

                string lap = entry.bestLapTime >= 9998f ? "NO TIME" : UiFactory.FormatTime(entry.bestLapTime);
                text += entry.position.ToString("00") + "   " + Pad(entry.driverName, 20) + "  " + Pad(lap, 10) + "  " + phase + (entry.invalidated ? " INV" : "") + "\n";
            }

            return text;
        }

        string BuildRaceResultsText(List<RaceResultEntry> results)
        {
            if (results == null || results.Count == 0)
            {
                return "No results.";
            }

            float winnerTime = results[0].totalTime + results[0].penaltiesSeconds;
            string text = "POS  DRIVER                 TOTAL/GAP   BEST        PEN   PTS\n";
            for (int i = 0; i < results.Count; i++)
            {
                RaceResultEntry entry = results[i];
                float classifiedTime = entry.totalTime + entry.penaltiesSeconds;
                string gap = i == 0 ? UiFactory.FormatTime(classifiedTime) : "+" + (classifiedTime - winnerTime).ToString("0.0") + "s";
                string penalties = entry.penaltiesSeconds > 0f ? "+" + entry.penaltiesSeconds.ToString("0") : "--";
                text += entry.finishingPosition.ToString("00") + "   " + Pad(entry.driverName, 20) + "  " + Pad(gap, 9) + "  " + UiFactory.FormatTime(entry.bestLapTime) + "  " + Pad(penalties, 4) + "  " + entry.points + "\n";
            }

            return text;
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

        string Pad(string value, int length)
        {
            if (string.IsNullOrEmpty(value))
            {
                value = "";
            }

            if (value.Length > length)
            {
                return value.Substring(0, length);
            }

            return value.PadRight(length);
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

        string BuildControlsText()
        {
            return
                "Driving\n" +
                "Throttle: W or Up Arrow, gamepad Vertical axis up\n" +
                "Brake / Reverse: S or Down Arrow, gamepad Vertical axis down\n" +
                "Steer Left / Right: A/D or Left/Right Arrow, gamepad Horizontal axis\n" +
                "ERS Mode Cycle: R\n" +
                "ERS Manual Override: Left Shift or Right Shift\n" +
                "DRS Toggle: Space, only when race rules allow it\n" +
                "Camera Toggle: C\n" +
                "Pit Request: P\n" +
                "Manual Shift Down / Up: Q / E, only when Manual Gears is enabled\n\n" +
                "Session\n" +
                "Pause / Resume: Esc\n" +
                "Restart Race: Pause menu button\n" +
                "Return To Menu: Pause menu button\n\n" +
                "Assists\n" +
                "Auto-brake, ABS, traction control, racing line, input sensitivity, ERS mode, and manual gears are changed from Settings / Assists. ERS mode can also be cycled in-race with R.";
        }

        string OnOff(bool value)
        {
            return value ? "On" : "Off";
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
    }
}
