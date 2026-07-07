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
            Text title = UiFactory.CreateText(titleArea, "Title", "LOCAL FORMULA", 72, Color.white, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 92f);
            Text subtitle = UiFactory.CreateText(titleArea, "Subtitle", "CAREER RACING", 30, new Color(0.95f, 0.05f, 0.04f), TextAnchor.UpperLeft);
            subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(2f, -88f);
            Text seasonTag = UiFactory.CreateText(titleArea, "Season tag", data.Calendar.events.Count + " ROUND WORLD SEASON", 22, new Color(0.74f, 0.84f, 0.88f), TextAnchor.UpperLeft);
            seasonTag.GetComponent<RectTransform>().anchoredPosition = new Vector2(4f, -132f);

            RectTransform menu = UiFactory.CreateRect(background, "Menu", new Vector2(0.06f, 0.1f), new Vector2(0.32f, 0.6f), Vector2.zero, Vector2.zero);
            UiFactory.AddVerticalLayout(menu, 9, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateButton(menu, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateButton(menu, "Race Weekend", bootstrap.ShowRaceWeekend);
            UiFactory.CreateButton(menu, "Quick Race", bootstrap.StartQuickRace);
            UiFactory.CreateButton(menu, "Time Trial", bootstrap.ShowTimeTrialSetup);
            UiFactory.CreateSecondaryButton(menu, "Track Info", bootstrap.ShowTrackInfo);
            UiFactory.CreateSecondaryButton(menu, "Driver Ratings", () => ShowDriverRatings(data, career, settings));
            UiFactory.CreateSecondaryButton(menu, "Career Stats", () => ShowCareerStats(data, career, settings));
            UiFactory.CreateSecondaryButton(menu, "Settings", () => ShowSettings(data, career, settings));
            UiFactory.CreateSecondaryButton(menu, "Quit", Application.Quit);

            // Bottom status strip: save state, career round, difficulty, build label.
            RectTransform statusStrip = UiFactory.CreateBand(background, "Status strip", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 34f), new Color(0.004f, 0.006f, 0.009f, 0.92f));
            Text status = UiFactory.CreateText(statusStrip, "Status text",
                "CAREER SAVE LOADED   |   SEASON " + career.Save.currentSeason + " ROUND " + career.Save.currentRound +
                "   |   DIFFICULTY " + settings.Difficulty.ToString().ToUpperInvariant() +
                "   |   LOCAL FORMULA PROTOTYPE BUILD", 14, UiFactory.TextMuted, TextAnchor.MiddleCenter);
            RectTransform statusRect = status.GetComponent<RectTransform>();
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;

            RectTransform summary = UiFactory.CreateBand(background, "Career summary", new Vector2(0.58f, 0.14f), new Vector2(0.92f, 0.7f), Vector2.zero, Vector2.zero, new Color(0.018f, 0.026f, 0.034f, 0.9f));
            UiFactory.CreateBand(summary, "Summary red rule", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), Vector2.zero, new Color(0.95f, 0.04f, 0.035f, 1f));
            Text heading = UiFactory.CreateText(summary, "Summary title", "Next Weekend", 32, Color.white, TextAnchor.UpperLeft);
            heading.GetComponent<RectTransform>().anchoredPosition = new Vector2(28f, -24f);
            TeamData team = data.FindTeam(career.Save.playerTeamId);
            CalendarEventData current = career.CurrentEvent();
            PlayerRecordsData records = PlayerRecordsStore.Data;
            Text details = UiFactory.CreateText(summary, "Summary", career.Save.playerDriverName + "\n" +
                (team == null ? career.Save.playerTeamId : team.name) + "\n" +
                "Season " + career.Save.currentSeason + "  Round " + career.Save.currentRound + "\n" +
                (current == null ? "Prototype GP" : current.displayName) + "\n" +
                "Resource points " + career.Save.resourcePoints + "\n" +
                "Wins " + records.raceWins + "   Podiums " + records.podiums + "   Poles " + records.polePositions,
                23, new Color(0.82f, 0.9f, 0.94f), TextAnchor.UpperLeft);
            RectTransform detailsRect = details.GetComponent<RectTransform>();
            detailsRect.anchorMin = new Vector2(0f, 0f);
            detailsRect.anchorMax = new Vector2(1f, 1f);
            detailsRect.offsetMin = new Vector2(28f, 36f);
            detailsRect.offsetMax = new Vector2(-28f, -82f);
            details.verticalOverflow = VerticalWrapMode.Overflow;
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
            UiFactory.CreateTopNav(background, "Career");

            // Profile strip: season, round, reputation, resource points, contract target.
            RectTransform profile = UiFactory.CreateRect(background, "Career profile strip", new Vector2(0.05f, 0.845f), new Vector2(0.95f, 0.905f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(profile, 12, new RectOffset(0, 0, 0, 0));
            TeamData profileTeam = data.FindTeam(career.Save.playerTeamId);
            UiFactory.CreateStatCard(profile, "Driver", career.Save.playerDriverName, 280f);
            UiFactory.CreateStatCard(profile, "Team", profileTeam == null ? career.Save.playerTeamId : profileTeam.shortName, 200f);
            UiFactory.CreateStatCard(profile, "Season / Round", career.Save.currentSeason + " / " + career.Save.currentRound, 200f);
            UiFactory.CreateStatCard(profile, "Reputation", career.Save.reputation.ToString(), 170f);
            UiFactory.CreateStatCard(profile, "Resources", career.Save.resourcePoints + " RP", 190f);
            UiFactory.CreateStatCard(profile, "Contract Target", "P" + career.Save.contractTargetPosition, 190f);

            RectTransform left = UiFactory.CreateRect(background, "Career actions", new Vector2(0.05f, 0.1f), new Vector2(0.36f, 0.82f), Vector2.zero, Vector2.zero);
            UiFactory.AddVerticalLayout(left, 12, new RectOffset(0, 0, 0, 0));
            InputField nameInput = UiFactory.CreateInputField(left, career.Save.playerDriverName);
            UiFactory.CreateText(left, "Team label", "Starting team", 20, new Color(0.72f, 0.8f, 0.84f), TextAnchor.MiddleLeft);
            Text selectedTeam = UiFactory.CreateText(left, "Selected team", data.FindTeam(selectedTeamId).name, 22, Color.white, TextAnchor.MiddleLeft);
            RectTransform teamGrid = UiFactory.CreateRect(left, "Team buttons", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            teamGrid.sizeDelta = new Vector2(620f, 240f);
            GridLayoutGroup grid = teamGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(148f, 36f);
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

            UiFactory.CreateButton(left, "Start New Career", () =>
            {
                career.StartNewCareer(nameInput.text, selectedTeamId, useExistingDriver, selectedDriverId);
                ShowCareerHub(data, career, settings);
            });
            UiFactory.CreateButton(left, "Mode: " + (useExistingDriver ? "Existing Driver" : "Custom Driver"), () =>
            {
                useExistingDriver = !useExistingDriver;
                ShowCareerHub(data, career, settings);
            });
            UiFactory.CreateButton(left, "Race Weekend", bootstrap.ShowRaceWeekend);
            UiFactory.CreateButton(left, "Driver Ratings", () => ShowDriverRatings(data, career, settings));
            UiFactory.CreateButton(left, "Back", () => ShowMainMenu(data, career, settings));

            RectTransform driverGrid = UiFactory.CreateRect(left, "Driver buttons", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            driverGrid.sizeDelta = new Vector2(620f, 126f);
            GridLayoutGroup driverGridLayout = driverGrid.gameObject.AddComponent<GridLayoutGroup>();
            driverGridLayout.cellSize = new Vector2(196f, 34f);
            driverGridLayout.spacing = new Vector2(8f, 8f);
            int driverButtons = Mathf.Min(9, data.Drivers.drivers.Count);
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
                });
            }

            RectTransform middle = UiFactory.CreateCard(background, "Standings panel", new Vector2(0.4f, 0.1f), new Vector2(0.66f, 0.82f));
            Text standings = UiFactory.CreateText(middle, "Standings", BuildStandingsText(career.Save.driverStandings, "Driver Standings") + "\n" + BuildStandingsText(career.Save.constructorStandings, "Constructors"), 18, Color.white, TextAnchor.UpperLeft);
            RectTransform standingsRect = standings.GetComponent<RectTransform>();
            standingsRect.anchorMin = Vector2.zero;
            standingsRect.anchorMax = Vector2.one;
            standingsRect.offsetMin = new Vector2(22f, 22f);
            standingsRect.offsetMax = new Vector2(-22f, -22f);
            standings.verticalOverflow = VerticalWrapMode.Overflow;

            // R&D: scrollable upgrade list grouped by category, with cost/state pills.
            RectTransform right = UiFactory.CreateScrollPanel(background, "Upgrades panel", new Vector2(0.68f, 0.1f), new Vector2(0.95f, 0.82f), 8, new RectOffset(20, 20, 18, 18));
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
            CreateSettingsTab(tabs, "Back", false, () => ShowMainMenu(data, career, settings));
        }

        void CreateSettingsTab(RectTransform parent, string label, bool active, UnityEngine.Events.UnityAction action)
        {
            UnityEngine.UI.Button tab = active
                ? UiFactory.CreatePrimaryButton(parent, label, action)
                : UiFactory.CreateSecondaryButton(parent, label, action);
            UiFactory.SetSize(tab, 224f, 44f);
        }

        public void ShowSettings(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Settings background", new Color(0.012f, 0.016f, 0.021f, 1f));
            BuildSettingsTabBar(background, data, career, settings, 0);

            RectTransform panel = UiFactory.CreateCard(background, "Gameplay card", new Vector2(0.06f, 0.1f), new Vector2(0.52f, 0.76f));
            RectTransform list = UiFactory.CreateRect(panel, "Gameplay list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(list, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(list, "Session");
            UiFactory.CreateButton(list, "Race Laps: " + settings.Current.laps, () =>
            {
                settings.Current.laps = settings.Current.laps == 3 ? 5 : (settings.Current.laps == 5 ? 14 : 3);
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateText(list, "Grid size", "Grid: 22 drivers (player + 21 AI)", 18, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            UiFactory.CreateButton(list, "Difficulty: " + settings.Difficulty, () =>
            {
                settings.Current.difficultyIndex = (settings.Current.difficultyIndex + 1) % 4;
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateButton(list, "Tyre: " + settings.Current.tyreCompound, () =>
            {
                settings.Current.tyreCompound = NextTyreName(settings.Current.tyreCompound);
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateButton(list, "ERS Mode: " + settings.ErsMode, () =>
            {
                settings.Current.ersMode = (settings.Current.ersMode + 1) % 3;
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateButton(list, "Manual Gears: " + OnOff(settings.Current.manualGears), () =>
            {
                settings.Current.manualGears = !settings.Current.manualGears;
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateButton(list, "Audio: " + OnOff(settings.Current.audioEnabled), () =>
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

            RectTransform left = UiFactory.CreateCard(background, "HUD card", new Vector2(0.06f, 0.1f), new Vector2(0.5f, 0.76f));
            RectTransform leftList = UiFactory.CreateRect(left, "HUD list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(leftList, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(leftList, "HUD & Camera");
            UiFactory.CreateButton(leftList, "HUD Scale: " + settings.Current.hudScale.ToString("0.00"), () =>
            {
                settings.Current.hudScale = CycleFloat(settings.Current.hudScale, 0.8f, 1.25f, 0.15f);
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
            UiFactory.CreateText(leftList, "HUD scale note", "Scales every in-race panel around its own screen edge, so nothing clips off.", 15, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            UiFactory.CreateButton(leftList, "Compact HUD: " + OnOff(settings.Current.compactHud), () =>
            {
                settings.Current.compactHud = !settings.Current.compactHud;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
            UiFactory.CreateButton(leftList, "Speed Units: " + (settings.Current.useMphUnits ? "MPH" : "KM/H"), () =>
            {
                settings.Current.useMphUnits = !settings.Current.useMphUnits;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
            UiFactory.CreateButton(leftList, "Camera FOV: " + settings.Current.cameraFov.ToString("0"), () =>
            {
                settings.Current.cameraFov = CycleFloat(settings.Current.cameraFov, 52f, 76f, 4f);
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
            UiFactory.CreateButton(leftList, "Camera Shake: " + OnOff(settings.Current.cameraShake), () =>
            {
                settings.Current.cameraShake = !settings.Current.cameraShake;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
            UiFactory.CreateButton(leftList, "Shake Strength: " + settings.Current.cameraShakeStrength.ToString("0.0"), () =>
            {
                settings.Current.cameraShakeStrength = CycleFloat(settings.Current.cameraShakeStrength, 0.5f, 1.5f, 0.25f);
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
            UiFactory.CreateButton(leftList, "UI Animations: " + OnOff(settings.Current.uiAnimations), () =>
            {
                settings.Current.uiAnimations = !settings.Current.uiAnimations;
                UiFactory.AnimationsEnabled = settings.Current.uiAnimations;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });

            RectTransform right = UiFactory.CreateCard(background, "Graphics card", new Vector2(0.54f, 0.1f), new Vector2(0.94f, 0.76f));
            RectTransform rightList = UiFactory.CreateRect(right, "Graphics list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(rightList, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(rightList, "Graphics");
            string[] qualityNames = { "Low", "Medium", "High", "Ultra" };
            UiFactory.CreateButton(rightList, "Quality: " + qualityNames[Mathf.Clamp(settings.Current.graphicsQuality, 0, 3)], () =>
            {
                settings.Current.graphicsQuality = (settings.Current.graphicsQuality + 1) % 4;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
            UiFactory.CreateButton(rightList, "Particles: " + OnOff(settings.Current.particlesEnabled), () =>
            {
                settings.Current.particlesEnabled = !settings.Current.particlesEnabled;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
            UiFactory.CreateButton(rightList, "Scenery Density: " + settings.Current.sceneryDensity.ToString("0.00"), () =>
            {
                settings.Current.sceneryDensity = CycleFloat(settings.Current.sceneryDensity, 0.5f, 2f, 0.5f);
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
            UiFactory.CreateButton(rightList, "Racing Line: " + OnOff(settings.Current.racingLineAssist), () =>
            {
                settings.Current.racingLineAssist = !settings.Current.racingLineAssist;
                settings.Save();
                ShowDisplaySettings(data, career, settings);
            });
            UiFactory.CreateText(rightList, "Graphics note", "Quality and scenery density apply the next time a track is built.", 16, UiFactory.TextMuted, TextAnchor.MiddleLeft);
        }

        public void ShowControls(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Controls background", new Color(0.012f, 0.016f, 0.021f, 1f));
            BuildSettingsTabBar(background, data, career, settings, 3);

            RectTransform panel = UiFactory.CreateCard(background, "Controls card", new Vector2(0.06f, 0.06f), new Vector2(0.72f, 0.76f));
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
            UiFactory.CreateTopNav(background, "Driver Ratings");
            Text subtitle = UiFactory.CreateText(background, "Ratings subtitle", "Overall is calculated from qualifying, defending, overtaking, and race pace.", 18, UiFactory.TextMuted, TextAnchor.UpperLeft);
            subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(66f, -112f);
            UiFactory.SetSize(subtitle, 1200f, 28f);

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

            RectTransform buttons = UiFactory.CreateRect(background, "Ratings buttons", new Vector2(0.06f, 0.03f), new Vector2(0.5f, 0.1f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSecondaryButton(buttons, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateSecondaryButton(buttons, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        public void ShowAssists(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Assists background", new Color(0.012f, 0.016f, 0.021f, 1f));
            BuildSettingsTabBar(background, data, career, settings, 1);
            RectTransform card = UiFactory.CreateCard(background, "Assists card", new Vector2(0.06f, 0.06f), new Vector2(0.52f, 0.76f));
            RectTransform panel = UiFactory.CreateRect(card, "Assists panel", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(panel, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(panel, "Driving Assists");
            UiFactory.CreateButton(panel, "Auto Brake: " + OnOff(settings.Current.autoBrakeAssist), () =>
            {
                settings.Current.autoBrakeAssist = !settings.Current.autoBrakeAssist;
                settings.Save();
                ShowAssists(data, career, settings);
            });
            UiFactory.CreateButton(panel, "ABS: " + OnOff(settings.Current.absAssist), () =>
            {
                settings.Current.absAssist = !settings.Current.absAssist;
                settings.Save();
                ShowAssists(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Traction Control: " + OnOff(settings.Current.tractionControl), () =>
            {
                settings.Current.tractionControl = !settings.Current.tractionControl;
                settings.Save();
                ShowAssists(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Racing Line: " + OnOff(settings.Current.racingLineAssist), () =>
            {
                settings.Current.racingLineAssist = !settings.Current.racingLineAssist;
                settings.Save();
                ShowAssists(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Steering Sens: " + settings.Current.steeringSensitivity.ToString("0.00"), () =>
            {
                settings.Current.steeringSensitivity = CycleFloat(settings.Current.steeringSensitivity, 0.7f, 1.45f, 0.15f);
                settings.Save();
                ShowAssists(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Throttle Sens: " + settings.Current.throttleSensitivity.ToString("0.00"), () =>
            {
                settings.Current.throttleSensitivity = CycleFloat(settings.Current.throttleSensitivity, 0.7f, 1.45f, 0.15f);
                settings.Save();
                ShowAssists(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Brake Sens: " + settings.Current.brakeSensitivity.ToString("0.00"), () =>
            {
                settings.Current.brakeSensitivity = CycleFloat(settings.Current.brakeSensitivity, 0.7f, 1.45f, 0.15f);
                settings.Save();
                ShowAssists(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Back", () => ShowSettings(data, career, settings));
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
            Text title = UiFactory.CreateText(background, "Weekend title", current.displayName.ToUpper(), 42, Color.white, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(80f, -52f);

            RectTransform main = UiFactory.CreateRect(background, "Weekend main layout", new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.82f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(main, 24, new RectOffset(0, 0, 0, 0));

            // Left: Session Info & Weather
            RectTransform left = UiFactory.CreateBand(main, "Weekend actions", new Vector2(0f, 0f), new Vector2(0.35f, 1f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.96f));
            left.sizeDelta = new Vector2(440f, 0f);
            UiFactory.AddVerticalLayout(left, 12, new RectOffset(22, 22, 22, 22));
            UiFactory.CreateText(left, "Weekend title", "ROUND " + career.Save.currentRound, 24, new Color(0.95f, 0.04f, 0.035f), TextAnchor.MiddleLeft);

            string profile = current.weatherProfile.ToLower();
            float trackTemp = profile.Contains("hot") ? 44f : (profile.Contains("wet") ? 19f : (profile.Contains("cloud") ? 25f : 31f));
            Text weekendMeta = UiFactory.CreateText(left, "Weekend meta",
                "Track: " + current.displayName + "\n" +
                "Condition: " + WeatherProfileText(profile).ToUpper() + "\n" +
                "Track Temp: " + trackTemp.ToString("0") + "°C\n" +
                "Air Temp: " + (trackTemp - 7f).ToString("0") + "°C\n" +
                "Recommended Tyre: " + RecommendedTyreText(profile).ToUpper() + "\n" +
                "Team: " + (team == null ? "INDEPENDENT" : team.shortName.ToUpper()),
                19, new Color(0.86f, 0.92f, 0.96f), TextAnchor.UpperLeft);
            weekendMeta.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(weekendMeta, 390f, 150f);

            bool hasQualifying = career.HasQualifyingForCurrentRound();
            UiFactory.CreateText(left, "Session status",
                hasQualifying ? "Qualifying complete. Grid is set." : "Qualifying required before the race.",
                17, hasQualifying ? new Color(0.55f, 1f, 0.65f) : new Color(1f, 0.85f, 0.4f), TextAnchor.MiddleLeft);
            UiFactory.CreateBand(left, "Spacer", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0f, 12f), new Color(0, 0, 0, 0));
            UiFactory.CreateButton(left, "Practice Programs", () => ShowPracticePrograms(data, career, settings));
            UiFactory.CreateButton(left, "Go to Qualifying", bootstrap.StartCareerQualifying);
            UiFactory.CreateButton(left, hasQualifying ? "Go to Race" : "Go to Race (runs qualifying)", bootstrap.StartCareerRace);
            UiFactory.CreateSecondaryButton(left, "Sim Qualifying", bootstrap.StartCareerSimQualifying);
            UiFactory.CreateSecondaryButton(left, "Car Setup", () => ShowCarSetup(data, career, settings, () => ShowRaceWeekend(data, career, settings)));
            UiFactory.CreateSecondaryButton(left, "Track Info", bootstrap.ShowTrackInfo);
            UiFactory.CreateSecondaryButton(left, "Back", () => ShowCareerHub(data, career, settings));

            // Right: Grid / Standings
            RectTransform right = UiFactory.CreateBand(main, "Weekend grid", new Vector2(0f, 0f), new Vector2(0.65f, 1f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.96f));
            List<QualifyingResultEntry> currentGrid = career.HasQualifyingForCurrentRound() ? career.Save.lastQualifyingResults : null;
            Text grid = UiFactory.CreateText(right, "Grid", BuildQualifyingText(currentGrid), 17, Color.white, TextAnchor.UpperLeft);
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            gridRect.anchorMin = Vector2.zero;
            gridRect.anchorMax = Vector2.one;
            gridRect.offsetMin = new Vector2(28f, 28f);
            gridRect.offsetMax = new Vector2(-28f, -28f);
            grid.verticalOverflow = VerticalWrapMode.Overflow;
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
            UiFactory.CreateBand(background, "Qualifying tyre accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -8f), Vector2.zero, new Color(0.95f, 0.04f, 0.035f, 1f));

            Text title = UiFactory.CreateText(background, "Qualifying tyre title", (simulate ? "Sim Qualifying" : "Q" + Mathf.Clamp(phase, 1, 3)) + " Pre-Session Briefing", 44, Color.white, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(80f, -54f);

            RectTransform main = UiFactory.CreateRect(background, "Qualifying main layout", new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.82f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(main, 24, new RectOffset(0, 0, 0, 0));

            RectTransform left = UiFactory.CreateBand(main, "Weather forecast", new Vector2(0f, 0f), new Vector2(0.35f, 1f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.96f));
            left.sizeDelta = new Vector2(420f, 0f);
            UiFactory.AddVerticalLayout(left, 12, new RectOffset(24, 24, 24, 24));
            UiFactory.CreateText(left, "Condition title", "TRACK CONDITIONS", 22, new Color(0.95f, 0.04f, 0.035f), TextAnchor.MiddleLeft);

            string profile = current.weatherProfile.ToLower();
            string condition = WeatherProfileText(profile);
            float trackTemp = profile.Contains("hot") ? 42f : (profile.Contains("wet") ? 18f : (profile.Contains("cloud") ? 26f : 32f));
            float airTemp = trackTemp - 8f;

            UiFactory.CreateText(left, "Current weather", "Condition: " + condition.ToUpper() + "\nTrack Temp: " + trackTemp.ToString("0") + "°C\nAir Temp: " + airTemp.ToString("0") + "°C\nHumidity: " + (profile.Contains("wet") ? "88%" : "42%"), 19, Color.white, TextAnchor.MiddleLeft);
            UiFactory.CreateBand(left, "Weather spacer", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0f, 20f), new Color(0, 0, 0, 0));
            UiFactory.CreateText(left, "Forecast title", "SESSION FORECAST", 20, new Color(0.72f, 0.82f, 0.86f), TextAnchor.MiddleLeft);
            string forecast = profile.Contains("mixed") ? "Expect variable rain intensity throughout the session." : (profile.Contains("wet") ? "Steady rain expected to continue." : "Dry track expected for the duration.");
            Text forecastText = UiFactory.CreateText(left, "Forecast text", forecast, 17, new Color(0.82f, 0.9f, 0.94f), TextAnchor.UpperLeft);
            forecastText.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform right = UiFactory.CreateBand(main, "Tyre Selection", new Vector2(0f, 0f), new Vector2(0.65f, 1f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.96f));
            UiFactory.AddVerticalLayout(right, 14, new RectOffset(28, 28, 24, 24));
            UiFactory.CreateText(right, "Tyre title", "TYRE SELECTION", 22, new Color(0.95f, 0.04f, 0.035f), TextAnchor.MiddleLeft);

            string currentCompound = settings.Current.tyreCompound;
            string tyreGuidance = GetTyreGuidance(currentCompound, profile);
            UiFactory.CreateText(right, "Selected tyre", "Compound: " + currentCompound.ToUpper(), 19, Color.white, TextAnchor.MiddleLeft);
            Text guidanceText = UiFactory.CreateText(right, "Tyre guidance", tyreGuidance, 17, new Color(0.82f, 0.9f, 0.94f), TextAnchor.UpperLeft);
            guidanceText.verticalOverflow = VerticalWrapMode.Overflow;
            guidanceText.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 80f);

            RectTransform tyreButtons = UiFactory.CreateRect(right, "Tyre buttons container", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            tyreButtons.sizeDelta = new Vector2(0f, 72f);
            UiFactory.AddHorizontalLayout(tyreButtons, 10, new RectOffset(0, 0, 0, 0));
            CreateQualifyingTyreButton(tyreButtons, data, career, settings, phase, "Soft", simulate);
            CreateQualifyingTyreButton(tyreButtons, data, career, settings, phase, "Medium", simulate);
            CreateQualifyingTyreButton(tyreButtons, data, career, settings, phase, "Hard", simulate);
            CreateQualifyingTyreButton(tyreButtons, data, career, settings, phase, "Intermediate", simulate);
            CreateQualifyingTyreButton(tyreButtons, data, career, settings, phase, "Wet", simulate);

            UiFactory.CreateBand(right, "Action spacer", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0f, 32f), new Color(0, 0, 0, 0));
            UiFactory.CreateButton(right, simulate ? "Execute Simulation" : "Start Session", () =>
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
            UiFactory.CreateButton(right, "Back to Weekend", bootstrap.ShowRaceWeekend);
        }

        string GetTyreGuidance(string compound, string weatherProfile)
        {
            bool isWet = weatherProfile.Contains("wet") || weatherProfile.Contains("mixed");
            if (compound == "Soft") return "Maximum grip for qualifying pace. Very high degradation. Recommended only for dry, cool tracks.";
            if (compound == "Medium") return "Balanced performance and durability. Good operating window for most dry conditions.";
            if (compound == "Hard") return "Highest durability but lower peak grip. Best for hot track surfaces and long stints.";
            if (compound == "Intermediate") return isWet ? "Optimal for damp tracks or light rain. Clears moderate water volume." : "Informational: Overheats quickly on dry asphalt.";
            if (compound == "Wet") return isWet ? "Required for heavy rain. Prevents aquaplaning in deep standing water." : "Informational: Massive performance loss on dry tracks.";
            return "";
        }

        public void ShowRaceTyreSelect(GameDataRepository data, CareerManager career, GameSettingsStore settings, bool careerRace)
        {
            Clear();
            CalendarEventData current = careerRace ? career.CurrentEvent() : (data.Calendar.events.Count > 0 ? data.Calendar.events[0] : career.CurrentEvent());
            if (current == null)
            {
                current = new CalendarEventData { displayName = "Prototype GP", weatherProfile = "clear" };
            }

            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Race tyre background", new Color(0.015f, 0.02f, 0.025f, 1f));
            UiFactory.CreateBand(background, "Race tyre accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -8f), Vector2.zero, new Color(0.95f, 0.04f, 0.035f, 1f));

            Text title = UiFactory.CreateText(background, "Race tyre title", "Race Tyre Selection", 44, Color.white, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(80f, -54f);

            RectTransform panel = UiFactory.CreateBand(background, "Race tyre panel", new Vector2(0.12f, 0.14f), new Vector2(0.78f, 0.8f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.96f));
            UiFactory.AddVerticalLayout(panel, 12, new RectOffset(28, 28, 24, 24));
            Text briefing = UiFactory.CreateText(panel, "Race weather briefing", BuildWeatherBriefing(current, "Race", settings.Current.tyreCompound), 20, new Color(0.84f, 0.91f, 0.95f), TextAnchor.UpperLeft);
            briefing.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(briefing, 820f, 174f);

            RectTransform tyreButtons = UiFactory.CreateRect(panel, "Race tyre buttons", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            tyreButtons.sizeDelta = new Vector2(780f, 78f);
            UiFactory.AddHorizontalLayout(tyreButtons, 10, new RectOffset(0, 0, 0, 0));
            CreateRaceTyreButton(tyreButtons, data, career, settings, "Soft", careerRace);
            CreateRaceTyreButton(tyreButtons, data, career, settings, "Medium", careerRace);
            CreateRaceTyreButton(tyreButtons, data, career, settings, "Hard", careerRace);
            CreateRaceTyreButton(tyreButtons, data, career, settings, "Intermediate", careerRace);
            CreateRaceTyreButton(tyreButtons, data, career, settings, "Wet", careerRace);

            // Pit strategy plan: shown on the HUD pit card and used for the
            // engineer's box calls during the race.
            UiFactory.CreateText(panel, "Strategy title", "PIT STRATEGY", 20, new Color(0.95f, 0.04f, 0.035f), TextAnchor.MiddleLeft);
            int raceLaps = Mathf.Max(3, settings.Current.laps);
            string plannedLabel = settings.Current.plannedPitLap <= 0 ? "AUTO (engineer decides)" : "LAP " + settings.Current.plannedPitLap;
            UiFactory.CreateButton(panel, "Planned Stop: " + plannedLabel, () =>
            {
                settings.Current.plannedPitLap = settings.Current.plannedPitLap >= raceLaps - 1 ? 0 : settings.Current.plannedPitLap + 1;
                settings.Save();
                ShowRaceTyreSelect(data, career, settings, careerRace);
            });
            UiFactory.CreateButton(panel, "Second Compound: " + settings.Current.plannedSecondCompound, () =>
            {
                settings.Current.plannedSecondCompound = NextTyreName(settings.Current.plannedSecondCompound);
                settings.Save();
                ShowRaceTyreSelect(data, career, settings, careerRace);
            });
            int stintLength = settings.Current.plannedPitLap <= 0 ? Mathf.Max(1, Mathf.RoundToInt(raceLaps * 0.55f)) : settings.Current.plannedPitLap;
            UiFactory.CreateText(panel, "Strategy estimate",
                "Mandatory stop is active. First stint ~" + stintLength + " of " + raceLaps + " laps, pit lane loss ~22s." +
                (profileIsWet(current) ? " Rain risk: plan for Intermediates." : ""),
                16, UiFactory.TextMuted, TextAnchor.MiddleLeft);

            UiFactory.CreateButton(panel, "Car Setup", () => ShowCarSetup(data, career, settings, () => ShowRaceTyreSelect(data, career, settings, careerRace)));

            UiFactory.CreateButton(panel, "Start Race", () =>
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
            UiFactory.CreateButton(panel, careerRace ? "Back to Weekend" : "Back to Menu", () =>
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
        }

        void CreateQualifyingTyreButton(RectTransform parent, GameDataRepository data, CareerManager career, GameSettingsStore settings, int phase, string tyreName, bool simulate)
        {
            string selected = settings.Current.tyreCompound == tyreName ? "  SELECTED" : "";
            UiFactory.CreateButton(parent, tyreName + selected, () =>
            {
                settings.Current.tyreCompound = tyreName;
                settings.Save();
                ShowQualifyingTyreSelect(data, career, settings, phase, simulate);
            });
        }

        void CreateRaceTyreButton(RectTransform parent, GameDataRepository data, CareerManager career, GameSettingsStore settings, string tyreName, bool careerRace)
        {
            string selected = settings.Current.tyreCompound == tyreName ? "  SELECTED" : "";
            UiFactory.CreateButton(parent, tyreName + selected, () =>
            {
                settings.Current.tyreCompound = tyreName;
                settings.Save();
                ShowRaceTyreSelect(data, career, settings, careerRace);
            });
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

            RectTransform buttons = UiFactory.CreateRect(background, "Time trial buttons", new Vector2(0.06f, 0.03f), new Vector2(0.6f, 0.1f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSecondaryButton(buttons, "Car Setup", () => ShowCarSetup(data, career, settings, () => ShowTimeTrialSetup(data, career, settings)));
            UiFactory.CreateSecondaryButton(buttons, "Back", () => ShowMainMenu(data, career, settings));
        }

        public void ShowTrackInfo(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Track info background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateTopNav(background, "Track Info");

            Text infoSubtitle = UiFactory.CreateText(background, "Track info subtitle", "Select a circuit to start a time trial there. Traits describe how each layout races.", 17, UiFactory.TextMuted, TextAnchor.UpperLeft);
            infoSubtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(66f, -108f);
            UiFactory.SetSize(infoSubtitle, 1300f, 28f);

            RectTransform content = UiFactory.CreateScrollPanel(background, "Track info list", new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.86f), 6, new RectOffset(18, 18, 14, 14));
            for (int i = 0; i < data.Calendar.events.Count; i++)
            {
                CalendarEventData raceEvent = data.Calendar.events[i];
                CreateTrackInfoRow(content, raceEvent);
            }

            RectTransform buttons = UiFactory.CreateRect(background, "Track info buttons", new Vector2(0.06f, 0.03f), new Vector2(0.7f, 0.1f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            UnityEngine.UI.Button trackTest = UiFactory.CreateButton(buttons, "Track Test (F2 cycles circuits)", () =>
            {
                CalendarEventData first = data.Calendar.events.Count > 0 ? data.Calendar.events[0] : null;
                bootstrap.BeginTimeTrial(first);
            });
            UiFactory.SetSize(trackTest, 360f, 50f);
            UiFactory.CreateSecondaryButton(buttons, "Back", () => ShowMainMenu(data, career, settings));
        }

        // Garage setup screen: five simple 1..5 controls with a live trade-off
        // summary. Values persist in settings and apply to the player car only.
        public void ShowCarSetup(GameDataRepository data, CareerManager career, GameSettingsStore settings, UnityEngine.Events.UnityAction backAction)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Car setup background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateTopNav(background, "Car Setup");

            UnityEngine.Events.UnityAction refresh = () => ShowCarSetup(data, career, settings, backAction);

            RectTransform left = UiFactory.CreateCard(background, "Setup card", new Vector2(0.06f, 0.08f), new Vector2(0.5f, 0.82f));
            RectTransform list = UiFactory.CreateRect(left, "Setup list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(list, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(list, "Garage Setup");
            CreateSetupCycleButton(list, "Front Wing", settings.Current.setupFrontWing, value => settings.Current.setupFrontWing = value, settings, refresh);
            CreateSetupCycleButton(list, "Rear Wing", settings.Current.setupRearWing, value => settings.Current.setupRearWing = value, settings, refresh);
            CreateSetupCycleButton(list, "Brake Bias", settings.Current.setupBrakeBias, value => settings.Current.setupBrakeBias = value, settings, refresh);
            CreateSetupCycleButton(list, "Suspension", settings.Current.setupSuspension, value => settings.Current.setupSuspension = value, settings, refresh);
            CreateSetupCycleButton(list, "Ride Height", settings.Current.setupRideHeight, value => settings.Current.setupRideHeight = value, settings, refresh);
            UiFactory.CreateButton(list, "Reset to Neutral", () =>
            {
                settings.Current.setupFrontWing = 3;
                settings.Current.setupRearWing = 3;
                settings.Current.setupBrakeBias = 3;
                settings.Current.setupSuspension = 3;
                settings.Current.setupRideHeight = 3;
                settings.Save();
                refresh();
            });
            UiFactory.CreateSecondaryButton(list, "Back", backAction);

            RectTransform right = UiFactory.CreateCard(background, "Setup summary card", new Vector2(0.54f, 0.28f), new Vector2(0.94f, 0.82f));
            RectTransform summaryList = UiFactory.CreateRect(right, "Setup summary list", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -20f));
            UiFactory.AddVerticalLayout(summaryList, 10, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSubHeader(summaryList, "Predicted Effect");
            Text summary = UiFactory.CreateText(summaryList, "Setup summary", BuildSetupSummary(settings.Current), 19, new Color(0.84f, 0.91f, 0.95f), TextAnchor.UpperLeft);
            summary.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(summary, 560f, 300f);
        }

        void CreateSetupCycleButton(RectTransform parent, string label, int value, System.Action<int> assign, GameSettingsStore settings, UnityEngine.Events.UnityAction refresh)
        {
            UiFactory.CreateButton(parent, label + ": " + SetupStepLabel(value), () =>
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
            return "Cornering grip:  " + SignedPercent(gripPercent) + "\n" +
                   "Top speed:  " + SignedPercent(topSpeedPercent) + "\n" +
                   "Braking power:  " + SignedPercent(brakePercent) + "\n" +
                   "Kerb tolerance:  " + kerbs + "\n" +
                   "Tyre wear:  " + wear + "\n\n" +
                   "More wing gives cornering grip but costs straight-line speed.\n" +
                   "Off-center brake bias stops harder but unsettles the car.\n" +
                   "Stiff suspension and low ride height dislike kerbs.\n\n" +
                   "Applies to your car in every session. AI cars run neutral setups.";
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

            RectTransform buttons = UiFactory.CreateRect(background, "Stats buttons", new Vector2(0.06f, 0.03f), new Vector2(0.5f, 0.1f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSecondaryButton(buttons, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateSecondaryButton(buttons, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        // Practice programs: once-per-round simulated running that pays out resource
        // points and a little reputation, so weekends have more to do than qualify.
        public void ShowPracticePrograms(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Practice background", new Color(0.012f, 0.016f, 0.021f, 1f));
            UiFactory.CreateTopNav(background, "Practice Programs");
            CalendarEventData current = career.CurrentEvent();
            string eventName = current == null ? "Prototype GP" : current.displayName;
            Text subtitle = UiFactory.CreateText(background, "Practice subtitle", "Round " + career.Save.currentRound + " — " + eventName + ". Each program can run once per round.", 18, UiFactory.TextMuted, TextAnchor.UpperLeft);
            subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(66f, -112f);
            UiFactory.SetSize(subtitle, 1300f, 30f);

            RectTransform content = UiFactory.CreateScrollPanel(background, "Practice list", new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.85f), 10, new RectOffset(18, 18, 14, 14));
            CreatePracticeProgramRow(content, data, career, settings, "acclimatisation", "Track Acclimatisation", "Learn the braking points and kerbs. Steady laps, no risks.", 22, 1);
            CreatePracticeProgramRow(content, data, career, settings, "tyreManagement", "Tyre Management", "Long-run stint watching temperatures and wear windows.", 20, 0);
            CreatePracticeProgramRow(content, data, career, settings, "ersManagement", "ERS Management", "Deployment mapping over a full lap for better battery use.", 18, 0);
            CreatePracticeProgramRow(content, data, career, settings, "qualifyingPace", "Qualifying Pace", "Low fuel, maximum attack simulation runs.", 24, 1);
            CreatePracticeProgramRow(content, data, career, settings, "racePace", "Race Pace", "Heavy fuel race simulation with pit stop rehearsal.", 26, 1);

            RectTransform buttons = UiFactory.CreateRect(background, "Practice buttons", new Vector2(0.06f, 0.03f), new Vector2(0.5f, 0.1f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateSecondaryButton(buttons, "Race Weekend", bootstrap.ShowRaceWeekend);
            UiFactory.CreateSecondaryButton(buttons, "Career", () => ShowCareerHub(data, career, settings));
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

            RectTransform content = UiFactory.CreateScrollPanel(background, "Results table", new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.74f), 2, new RectOffset(18, 18, 12, 12));
            string header = Pad("POS", 5) + Pad("GRID", 6) + Pad("DRIVER", 22) + Pad("TEAM", 10) + Pad("TYRE", 7) + Pad("TOTAL/GAP", 12) + Pad("BEST LAP", 12) + Pad("PEN", 6) + "PTS";
            Text headerText = UiFactory.CreateText(content, "Results header", header, 15, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.SetSize(headerText, 1240f, 26f);
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
                    string line = Pad(entry.finishingPosition.ToString("00"), 5) +
                                  Pad(entry.gridPosition > 0 ? entry.gridPosition.ToString("00") : "--", 6) +
                                  Pad(entry.driverName, 22) +
                                  Pad(entry.teamId, 10) +
                                  Pad(string.IsNullOrEmpty(entry.tyreCompound) ? "--" : entry.tyreCompound.Substring(0, 1), 7) +
                                  Pad(gap, 12) +
                                  Pad(UiFactory.FormatTime(entry.bestLapTime), 12) +
                                  Pad(penalties, 6) +
                                  entry.points;
                    Color rowColor = entry.isPlayer ? new Color(1f, 0.55f, 0.5f) : (i % 2 == 0 ? new Color(0.9f, 0.95f, 0.98f) : UiFactory.TextMuted);
                    Text rowText = UiFactory.CreateText(content, "Result row " + i, line, 15, rowColor, TextAnchor.MiddleLeft);
                    UiFactory.SetSize(rowText, 1240f, 24f);
                }
            }
            else
            {
                Text empty = UiFactory.CreateText(content, "No results", "No results.", 18, UiFactory.TextMuted, TextAnchor.MiddleLeft);
                UiFactory.SetSize(empty, 600f, 30f);
            }

            RectTransform buttons = UiFactory.CreateRect(background, "Results buttons", new Vector2(0.06f, 0.03f), new Vector2(0.94f, 0.12f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            if (careerRace)
            {
                UiFactory.CreateButton(buttons, "Continue Career", () => bootstrap.ShowCareer());
            }

            UiFactory.CreateButton(buttons, "Race Again", () => bootstrap.StartQuickRace());
            UiFactory.CreateSecondaryButton(buttons, "Main Menu", () => bootstrap.ShowMainMenu());
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

            RectTransform content = UiFactory.CreateScrollPanel(background, "Qualifying table", new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.74f), 2, new RectOffset(18, 18, 12, 12));
            string lastSection = null;
            if (results != null)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    QualifyingResultEntry entry = results[i];
                    string section = string.IsNullOrEmpty(entry.eliminatedIn) ? "Q3 — TOP 10 SHOOTOUT" : "ELIMINATED IN " + entry.eliminatedIn;
                    if (section != lastSection)
                    {
                        lastSection = section;
                        Text sectionText = UiFactory.CreateText(content, "Qualifying section " + i, section, 15, UiFactory.Accent, TextAnchor.MiddleLeft);
                        UiFactory.SetSize(sectionText, 1240f, 26f);
                    }

                    string lapLabel = entry.bestLapTime >= 9998f ? "NO TIME" : UiFactory.FormatTime(entry.bestLapTime);
                    string line = Pad(entry.position.ToString("00"), 5) + Pad(entry.driverName, 24) + Pad(lapLabel, 13) + (entry.invalidated ? "INVALIDATED" : "");
                    Color rowColor = entry.isPlayer ? new Color(1f, 0.55f, 0.5f) : (i % 2 == 0 ? new Color(0.9f, 0.95f, 0.98f) : UiFactory.TextMuted);
                    Text rowText = UiFactory.CreateText(content, "Qualifying row " + i, line, 15, rowColor, TextAnchor.MiddleLeft);
                    UiFactory.SetSize(rowText, 1240f, 24f);
                }
            }

            RectTransform buttons = UiFactory.CreateRect(background, "Qualifying buttons", new Vector2(0.06f, 0.03f), new Vector2(0.94f, 0.12f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateButton(buttons, "Continue to Race", bootstrap.StartCareerRace);
            UiFactory.CreateSecondaryButton(buttons, "Weekend", bootstrap.ShowRaceWeekend);
            UiFactory.CreateSecondaryButton(buttons, "Main Menu", bootstrap.ShowMainMenu);
        }

        void BuildPausePanel(RaceManager race)
        {
            RectTransform root = UiFactory.CreateBackdrop(canvas.transform, "Pause overlay");
            pausePanel = root.gameObject;
            RectTransform card = UiFactory.CreateCard(root, "Pause card", new Vector2(0.36f, 0.24f), new Vector2(0.64f, 0.76f));
            RectTransform menu = UiFactory.CreateRect(card, "Pause menu", Vector2.zero, Vector2.one, new Vector2(28f, 22f), new Vector2(-28f, -22f));
            UiFactory.AddVerticalLayout(menu, 11, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateText(menu, "Paused", "PAUSED", 34, Color.white, TextAnchor.MiddleLeft);
            string sessionLabel = race.IsTimeTrial ? "Time Trial" : (race.CurrentSession == RaceWeekendSession.Qualifying ? "Qualifying" : "Race");
            string eventLabel = race.EventData == null ? "Prototype GP" : race.EventData.displayName;
            UiFactory.CreateText(menu, "Pause session", sessionLabel + "  |  " + eventLabel, 18, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            UiFactory.CreateDivider(menu);
            Text controls = UiFactory.CreateText(menu, "Pause controls", "W/S throttle & brake   A/D steer\nSpace DRS   R ERS mode (hold: reset car)\nShift ERS override   C camera   P pit\nQ/E manual shift   F1 debug overlay   Esc resume", 16, new Color(0.78f, 0.86f, 0.9f), TextAnchor.UpperLeft);
            controls.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(controls, 460f, 92f);
            UiFactory.CreateButton(menu, "Resume", race.Resume);
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
            string profile = current == null ? "" : current.weatherProfile;
            int air;
            int track;
            WeatherTemperatures(profile, out air, out track);
            return
                (current == null ? "Prototype GP" : current.displayName) + "\n" +
                "Session: " + sessionName + "\n" +
                "Current weather: " + CurrentWeatherText(profile) + "\n" +
                "Forecast: " + ForecastText(profile) + "\n" +
                "Track condition: " + TrackConditionText(profile) + "\n" +
                "Track temp: " + track + " C   Air temp: " + air + " C\n" +
                "Recommended compound: " + RecommendedTyreText(profile) + "\n" +
                "Selected compound: " + selectedCompound;
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
