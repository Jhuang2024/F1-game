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
            UiFactory.CreateBand(background, "Track line red", new Vector2(0f, 0.235f), new Vector2(1f, 0.245f), new Vector2(0f, 0f), Vector2.zero, new Color(0.85f, 0.04f, 0.035f, 0.82f));
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

            RectTransform menu = UiFactory.CreateRect(background, "Menu", new Vector2(0.06f, 0.16f), new Vector2(0.32f, 0.58f), Vector2.zero, Vector2.zero);
            UiFactory.AddVerticalLayout(menu, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateButton(menu, "Quick Race", bootstrap.StartQuickRace);
            UiFactory.CreateButton(menu, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateButton(menu, "Driver Ratings", () => ShowDriverRatings(data, career, settings));
            UiFactory.CreateButton(menu, "Settings", () => ShowSettings(data, career, settings));
            UiFactory.CreateButton(menu, "Controls", () => ShowControls(data, career, settings));
            UiFactory.CreateButton(menu, "Quit", Application.Quit);

            RectTransform summary = UiFactory.CreateBand(background, "Career summary", new Vector2(0.58f, 0.14f), new Vector2(0.92f, 0.7f), Vector2.zero, Vector2.zero, new Color(0.018f, 0.026f, 0.034f, 0.9f));
            UiFactory.CreateBand(summary, "Summary red rule", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), Vector2.zero, new Color(0.95f, 0.04f, 0.035f, 1f));
            Text heading = UiFactory.CreateText(summary, "Summary title", "Next Weekend", 32, Color.white, TextAnchor.UpperLeft);
            heading.GetComponent<RectTransform>().anchoredPosition = new Vector2(28f, -24f);
            TeamData team = data.FindTeam(career.Save.playerTeamId);
            CalendarEventData current = career.CurrentEvent();
            Text details = UiFactory.CreateText(summary, "Summary", career.Save.playerDriverName + "\n" +
                (team == null ? career.Save.playerTeamId : team.name) + "\n" +
                "Season " + career.Save.currentSeason + "  Round " + career.Save.currentRound + "\n" +
                (current == null ? "Prototype GP" : current.displayName) + "\n" +
                "Resource points " + career.Save.resourcePoints,
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
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Career background", new Color(0.015f, 0.02f, 0.025f, 1f));
            Text title = UiFactory.CreateText(background, "Career title", "Career", 44, Color.white, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(80f, -48f);

            RectTransform left = UiFactory.CreateRect(background, "Career actions", new Vector2(0.05f, 0.12f), new Vector2(0.36f, 0.82f), Vector2.zero, Vector2.zero);
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

            RectTransform middle = UiFactory.CreateBand(background, "Standings panel", new Vector2(0.42f, 0.12f), new Vector2(0.66f, 0.82f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.96f));
            Text standings = UiFactory.CreateText(middle, "Standings", BuildStandingsText(career.Save.driverStandings, "Drivers"), 20, Color.white, TextAnchor.UpperLeft);
            RectTransform standingsRect = standings.GetComponent<RectTransform>();
            standingsRect.anchorMin = Vector2.zero;
            standingsRect.anchorMax = Vector2.one;
            standingsRect.offsetMin = new Vector2(22f, 22f);
            standingsRect.offsetMax = new Vector2(-22f, -22f);
            standings.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform right = UiFactory.CreateBand(background, "Upgrades panel", new Vector2(0.7f, 0.12f), new Vector2(0.94f, 0.82f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.96f));
            UiFactory.AddVerticalLayout(right, 10, new RectOffset(22, 22, 22, 22));
            UiFactory.CreateText(right, "R&D", "R&D", 28, Color.white, TextAnchor.MiddleLeft);
            UiFactory.CreateText(right, "Points", "Resource points " + career.Save.resourcePoints, 18, new Color(0.75f, 0.83f, 0.87f), TextAnchor.MiddleLeft);
            TeamData careerTeam = data.FindTeam(career.Save.playerTeamId);
            CarPerformanceData baseCar = careerTeam == null ? null : data.FindCar(careerTeam.carPerformanceId);
            CarPerformanceData tunedCar = baseCar == null ? null : career.ApplyCareerUpgrades(baseCar);
            if (baseCar != null && tunedCar != null)
            {
                Text carStats = UiFactory.CreateText(right, "Car performance", BuildCarPerformanceText(baseCar, tunedCar), 16, new Color(0.72f, 0.84f, 0.9f), TextAnchor.UpperLeft);
                carStats.verticalOverflow = VerticalWrapMode.Overflow;
                UiFactory.SetSize(carStats, 390f, 112f);
            }

            for (int i = 0; i < data.Upgrades.upgrades.Count; i++)
            {
                UpgradeData upgrade = data.Upgrades.upgrades[i];
                string state = career.Save.completedUpgradeIds.Contains(upgrade.id) ? "Done" : (career.Save.failedUpgradeIds.Contains(upgrade.id) ? "Rework" : upgrade.cost + " RP");
                UiFactory.CreateButton(right, upgrade.displayName + "  " + state, () =>
                {
                    career.TryPurchaseUpgrade(upgrade.id);
                    ShowCareerHub(data, career, settings);
                });
            }
        }

        public void ShowSettings(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Settings background", new Color(0.015f, 0.02f, 0.025f, 1f));
            RectTransform panel = UiFactory.CreateRect(background, "Settings panel", new Vector2(0.08f, 0.18f), new Vector2(0.48f, 0.82f), Vector2.zero, Vector2.zero);
            UiFactory.AddVerticalLayout(panel, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateText(panel, "Settings title", "Settings", 42, Color.white, TextAnchor.MiddleLeft);
            UiFactory.CreateButton(panel, "Race Laps: " + settings.Current.laps, () =>
            {
                settings.Current.laps = settings.Current.laps == 3 ? 5 : (settings.Current.laps == 5 ? 14 : 3);
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateText(panel, "Grid size", "Grid: 22 drivers (player + 21 AI)", 20, new Color(0.78f, 0.86f, 0.9f), TextAnchor.MiddleLeft);
            UiFactory.CreateButton(panel, "Difficulty: " + settings.Difficulty, () =>
            {
                settings.Current.difficultyIndex = (settings.Current.difficultyIndex + 1) % 4;
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Tyre: " + settings.Current.tyreCompound, () =>
            {
                settings.Current.tyreCompound = NextTyreName(settings.Current.tyreCompound);
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateButton(panel, "ERS Mode: " + settings.ErsMode, () =>
            {
                settings.Current.ersMode = (settings.Current.ersMode + 1) % 3;
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Assists", () => ShowAssists(data, career, settings));
            UiFactory.CreateButton(panel, "Manual Gears: " + (settings.Current.manualGears ? "On" : "Off"), () =>
            {
                settings.Current.manualGears = !settings.Current.manualGears;
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Camera Shake: " + (settings.Current.cameraShake ? "On" : "Off"), () =>
            {
                settings.Current.cameraShake = !settings.Current.cameraShake;
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Audio: " + (settings.Current.audioEnabled ? "On" : "Off"), () =>
            {
                settings.Current.audioEnabled = !settings.Current.audioEnabled;
                settings.Save();
                ShowSettings(data, career, settings);
            });
            UiFactory.CreateButton(panel, "Back", () => ShowMainMenu(data, career, settings));
        }

        public void ShowControls(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Controls background", new Color(0.015f, 0.02f, 0.025f, 1f));
            Text title = UiFactory.CreateText(background, "Controls title", "Controls", 44, Color.white, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(80f, -52f);

            RectTransform panel = UiFactory.CreateBand(background, "Controls panel", new Vector2(0.08f, 0.14f), new Vector2(0.72f, 0.78f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.96f));
            Text controls = UiFactory.CreateText(panel, "Controls text", BuildControlsText(), 22, new Color(0.86f, 0.92f, 0.95f), TextAnchor.UpperLeft);
            RectTransform controlsRect = controls.GetComponent<RectTransform>();
            controlsRect.anchorMin = Vector2.zero;
            controlsRect.anchorMax = Vector2.one;
            controlsRect.offsetMin = new Vector2(28f, 28f);
            controlsRect.offsetMax = new Vector2(-28f, -28f);
            controls.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform buttons = UiFactory.CreateRect(background, "Controls buttons", new Vector2(0.08f, 0.06f), new Vector2(0.72f, 0.12f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateButton(buttons, "Settings", () => ShowSettings(data, career, settings));
            UiFactory.CreateButton(buttons, "Back", () => ShowMainMenu(data, career, settings));
        }

        public void ShowDriverRatings(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Driver ratings background", new Color(0.006f, 0.009f, 0.014f, 1f));
            UiFactory.CreateBand(background, "Ratings top accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -8f), Vector2.zero, new Color(0.95f, 0.04f, 0.035f, 1f));
            Text title = UiFactory.CreateText(background, "Ratings title", "Driver Ratings", 48, Color.white, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(80f, -54f);
            Text subtitle = UiFactory.CreateText(background, "Ratings subtitle", "Overall is calculated from qualifying, defending, overtaking, and race pace.", 20, new Color(0.72f, 0.82f, 0.86f), TextAnchor.UpperLeft);
            subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(82f, -108f);

            RectTransform panel = UiFactory.CreateBand(background, "Ratings panel", new Vector2(0.08f, 0.16f), new Vector2(0.86f, 0.82f), Vector2.zero, Vector2.zero, new Color(0.018f, 0.026f, 0.034f, 0.94f));
            UiFactory.CreateBand(panel, "Ratings red rule", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), Vector2.zero, new Color(0.95f, 0.04f, 0.035f, 1f));
            Text table = UiFactory.CreateText(panel, "Ratings table", BuildDriverRatingsText(data), 18, new Color(0.88f, 0.94f, 0.97f), TextAnchor.UpperLeft);
            RectTransform tableRect = table.GetComponent<RectTransform>();
            tableRect.anchorMin = Vector2.zero;
            tableRect.anchorMax = Vector2.one;
            tableRect.offsetMin = new Vector2(28f, 22f);
            tableRect.offsetMax = new Vector2(-28f, -22f);
            table.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform buttons = UiFactory.CreateRect(background, "Ratings buttons", new Vector2(0.08f, 0.06f), new Vector2(0.5f, 0.12f), Vector2.zero, Vector2.zero);
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateButton(buttons, "Career", () => ShowCareerHub(data, career, settings));
            UiFactory.CreateButton(buttons, "Main Menu", () => ShowMainMenu(data, career, settings));
        }

        public void ShowAssists(GameDataRepository data, CareerManager career, GameSettingsStore settings)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Assists background", new Color(0.015f, 0.02f, 0.025f, 1f));
            RectTransform panel = UiFactory.CreateRect(background, "Assists panel", new Vector2(0.08f, 0.12f), new Vector2(0.52f, 0.86f), Vector2.zero, Vector2.zero);
            UiFactory.AddVerticalLayout(panel, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateText(panel, "Assists title", "Assists", 42, Color.white, TextAnchor.MiddleLeft);
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
            UiFactory.CreateText(left, "Weekend meta",
                "Track: " + current.displayName + "\n" +
                "Condition: " + WeatherProfileText(profile).ToUpper() + "\n" +
                "Track Temp: " + trackTemp.ToString("0") + "°C\n" +
                "Air Temp: " + (trackTemp - 7f).ToString("0") + "°C\n" +
                "Team: " + (team == null ? "INDEPENDENT" : team.shortName.ToUpper()),
                19, new Color(0.86f, 0.92f, 0.96f), TextAnchor.UpperLeft);

            UiFactory.CreateBand(left, "Spacer", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0f, 20f), new Color(0, 0, 0, 0));
            UiFactory.CreateButton(left, "Go to Qualifying", bootstrap.ShowQualifyingTyreSelect);
            UiFactory.CreateButton(left, "Go to Race", bootstrap.StartCareerRace);
            UiFactory.CreateButton(left, "Sim Qualifying", bootstrap.StartCareerSimQualifying);
            UiFactory.CreateButton(left, "Back", () => ShowCareerHub(data, career, settings));

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
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Results background", new Color(0.015f, 0.02f, 0.025f, 1f));
            RectTransform panel = UiFactory.CreateBand(background, "Results panel", new Vector2(0.18f, 0.12f), new Vector2(0.82f, 0.88f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.96f));
            Text title = UiFactory.CreateText(panel, "Results title", "Race Results", 40, Color.white, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(28f, -24f);

            string resultText = BuildRaceResultsText(results);

            Text list = UiFactory.CreateText(panel, "Results list", resultText, 17, new Color(0.86f, 0.92f, 0.95f), TextAnchor.UpperLeft);
            RectTransform listRect = list.GetComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0f, 0.16f);
            listRect.anchorMax = new Vector2(1f, 0.86f);
            listRect.offsetMin = new Vector2(30f, 0f);
            listRect.offsetMax = new Vector2(-30f, 0f);
            list.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform buttons = UiFactory.CreateRect(panel, "Results buttons", new Vector2(0f, 0f), new Vector2(1f, 0.16f), new Vector2(30f, 24f), new Vector2(-30f, -10f));
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            if (careerRace)
            {
                UiFactory.CreateButton(buttons, "Continue Career", () => bootstrap.ShowCareer());
            }

            UiFactory.CreateButton(buttons, "Race Again", () => bootstrap.StartQuickRace());
            UiFactory.CreateButton(buttons, "Main Menu", () => bootstrap.ShowMainMenu());
        }

        public void ShowQualifyingResults(RaceManager race, List<QualifyingResultEntry> results, bool careerRace)
        {
            Clear();
            RectTransform background = UiFactory.CreatePanel(canvas.transform, "Qualifying background", new Color(0.015f, 0.02f, 0.025f, 1f));
            RectTransform panel = UiFactory.CreateBand(background, "Qualifying panel", new Vector2(0.18f, 0.12f), new Vector2(0.82f, 0.88f), Vector2.zero, Vector2.zero, new Color(0.045f, 0.055f, 0.064f, 0.96f));
            string resultTitle = race != null && race.LastQualifyingResultWasSimulated ? "Sim Qualifying Classification" : "Qualifying Classification";
            Text title = UiFactory.CreateText(panel, "Qualifying title", resultTitle, 40, Color.white, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(28f, -24f);

            Text list = UiFactory.CreateText(panel, "Qualifying list", BuildQualifyingText(results), 17, new Color(0.86f, 0.92f, 0.95f), TextAnchor.UpperLeft);
            RectTransform listRect = list.GetComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0f, 0.16f);
            listRect.anchorMax = new Vector2(1f, 0.86f);
            listRect.offsetMin = new Vector2(30f, 0f);
            listRect.offsetMax = new Vector2(-30f, 0f);
            list.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform buttons = UiFactory.CreateRect(panel, "Qualifying buttons", new Vector2(0f, 0f), new Vector2(1f, 0.16f), new Vector2(30f, 24f), new Vector2(-30f, -10f));
            UiFactory.AddHorizontalLayout(buttons, 14, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateButton(buttons, "Continue to Race", bootstrap.StartCareerRace);
            UiFactory.CreateButton(buttons, "Weekend", bootstrap.ShowRaceWeekend);
            UiFactory.CreateButton(buttons, "Main Menu", bootstrap.ShowMainMenu);
        }

        void BuildPausePanel(RaceManager race)
        {
            RectTransform root = UiFactory.CreatePanel(canvas.transform, "Pause overlay", new Color(0f, 0f, 0f, 0.72f));
            pausePanel = root.gameObject;
            RectTransform menu = UiFactory.CreateRect(root, "Pause menu", new Vector2(0.39f, 0.34f), new Vector2(0.61f, 0.66f), Vector2.zero, Vector2.zero);
            UiFactory.AddVerticalLayout(menu, 12, new RectOffset(0, 0, 0, 0));
            UiFactory.CreateText(menu, "Paused", "Paused", 36, Color.white, TextAnchor.MiddleLeft);
            Text controls = UiFactory.CreateText(menu, "Pause controls", "W/S or Up/Down accelerate/brake\nA/D or Left/Right steer\nSpace DRS toggle   R ERS mode\nShift ERS override   C camera\nP pit request   Q/E shift\nEsc resume/pause", 17, new Color(0.78f, 0.86f, 0.9f), TextAnchor.UpperLeft);
            controls.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.SetSize(controls, 380f, 118f);
            UiFactory.CreateButton(menu, "Resume", race.Resume);
            UiFactory.CreateButton(menu, "Restart Race", race.RestartRace);
            UiFactory.CreateButton(menu, "Main Menu", () =>
            {
                race.CleanupRaceWorld();
                bootstrap.ShowMainMenu();
            });
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

        string BuildDriverRatingsText(GameDataRepository data)
        {
            List<DriverData> drivers = new List<DriverData>(data.Drivers.drivers);
            drivers.Sort((a, b) =>
            {
                int overall = b.OverallRating.CompareTo(a.OverallRating);
                if (overall != 0)
                {
                    return overall;
                }

                return b.pace.CompareTo(a.pace);
            });

            string text = "OVR  QAL  DEF  OVT  RACE   DVR  TEAM  DRIVER\n";
            for (int i = 0; i < drivers.Count; i++)
            {
                DriverData driver = drivers[i];
                TeamData team = data.FindTeam(driver.teamId);
                string teamCode = team == null ? driver.teamId.ToUpperInvariant() : team.shortName.ToUpperInvariant();
                text += driver.OverallRating.ToString("00") + "   " +
                        Mathf.Clamp(driver.qualifying, 1, 100).ToString("00") + "   " +
                        Mathf.Clamp(driver.defending, 1, 100).ToString("00") + "   " +
                        Mathf.Clamp(driver.overtaking, 1, 100).ToString("00") + "   " +
                        Mathf.Clamp(driver.pace, 1, 100).ToString("00") + "     " +
                        Pad(driver.abbreviation.ToUpperInvariant(), 3) + "  " +
                        Pad(teamCode, 4) + "  " +
                        driver.displayName + "\n";
            }

            return text;
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
