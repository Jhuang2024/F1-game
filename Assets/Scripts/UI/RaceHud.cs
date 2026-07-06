using UnityEngine;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    public class RaceHud : MonoBehaviour
    {
        const int TowerRowCount = 22;

        RaceManager race;
        RaceParticipant player;
        Text tower;
        Image[] towerRowBackgrounds = new Image[TowerRowCount];
        Text[] towerPositions = new Text[TowerRowCount];
        Text[] towerDrivers = new Text[TowerRowCount];
        Image[] towerTyres = new Image[TowerRowCount];
        Text[] towerLaps = new Text[TowerRowCount];
        Text[] towerGaps = new Text[TowerRowCount];
        Text[] towerIntervals = new Text[TowerRowCount];
        Text telemetry;
        Text timing;
        Text center;
        Text hint;
        Text speed;
        Text drsFlash;
        Text engineer;
        Text qualifyingFeedback;
        Text pitStatus;
        Image ersFill;
        Image tyreTempFill;
        Image tyreWearFill;
        Image fuelFill;
        Image damageFill;
        Image pitFill;
        Image revBar;
        Text gearText;
        GameObject qualifyingFeedbackPanel;
        GameObject engineerPanel;
        GameObject startLightPanel;
        Image[] startLightImages = new Image[5];
        string previousDrsState = "";
        float drsFlashTimer;

        public void Build(Transform parent, RaceManager raceManager, RaceParticipant playerParticipant)
        {
            race = raceManager;
            player = playerParticipant;
            transform.SetParent(parent, false);
            RectTransform root = gameObject.AddComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            RectTransform topBand = UiFactory.CreateBand(transform, "HUD top band", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -64f), Vector2.zero, new Color(0.006f, 0.009f, 0.012f, 0.78f));
            UiFactory.CreateBand(topBand, "HUD red rule", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f), new Color(0.95f, 0.08f, 0.06f, 0.95f));
            center = UiFactory.CreateText(topBand, "Center", "", 21, new Color(0.94f, 0.98f, 1f), TextAnchor.MiddleCenter);
            RectTransform centerRect = center.GetComponent<RectTransform>();
            centerRect.anchorMin = Vector2.zero;
            centerRect.anchorMax = Vector2.one;
            centerRect.offsetMin = Vector2.zero;
            centerRect.offsetMax = Vector2.zero;

            RectTransform towerBand = UiFactory.CreateBand(transform, "Timing tower", new Vector2(0f, 0.18f), new Vector2(0f, 0.88f), new Vector2(24f, 0f), new Vector2(360f, 0f), new Color(0.006f, 0.009f, 0.012f, 0.72f));
            UiFactory.CreateBand(towerBand, "Tower accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f), new Color(0.95f, 0.08f, 0.06f, 0.95f));
            tower = UiFactory.CreateText(towerBand, "Tower header", "", 13, new Color(0.7f, 0.8f, 0.86f), TextAnchor.MiddleLeft);
            RectTransform towerRect = tower.GetComponent<RectTransform>();
            towerRect.anchorMin = new Vector2(0f, 1f);
            towerRect.anchorMax = new Vector2(1f, 1f);
            towerRect.offsetMin = new Vector2(18f, -34f);
            towerRect.offsetMax = new Vector2(-12f, -10f);
            for (int i = 0; i < TowerRowCount; i++)
            {
                CreateTowerRow(towerBand, i);
            }

            RectTransform speedBand = UiFactory.CreateBand(transform, "Speed panel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-220f, 26f), new Vector2(220f, 154f), new Color(0.006f, 0.009f, 0.012f, 0.82f));
            UiFactory.CreateBand(speedBand, "Dashboard accent", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 4f), new Color(0.95f, 0.08f, 0.06f, 0.95f));

            speed = UiFactory.CreateText(speedBand, "Speed", "", 46, Color.white, TextAnchor.MiddleCenter);
            RectTransform speedRect = speed.GetComponent<RectTransform>();
            speedRect.anchorMin = new Vector2(0.5f, 0.5f);
            speedRect.anchorMax = new Vector2(0.5f, 0.5f);
            speedRect.sizeDelta = new Vector2(200f, 60f);
            speedRect.anchoredPosition = new Vector2(-60f, 10f);

            gearText = UiFactory.CreateText(speedBand, "Gear", "1", 58, new Color(0.95f, 0.08f, 0.06f), TextAnchor.MiddleCenter);
            RectTransform gearRect = gearText.GetComponent<RectTransform>();
            gearRect.anchorMin = new Vector2(0.5f, 0.5f);
            gearRect.anchorMax = new Vector2(0.5f, 0.5f);
            gearRect.sizeDelta = new Vector2(80f, 80f);
            gearRect.anchoredPosition = new Vector2(80f, 10f);

            RectTransform revTrack = UiFactory.CreateBand(speedBand, "Rev track", new Vector2(0.1f, 0.82f), new Vector2(0.9f, 0.92f), Vector2.zero, Vector2.zero, new Color(0.12f, 0.14f, 0.16f, 0.9f));
            RectTransform revFill = UiFactory.CreateBand(revTrack, "Rev fill", new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, Color.white);
            revBar = revFill.GetComponent<Image>();

            RectTransform telemetryBand = UiFactory.CreateBand(transform, "Telemetry panel", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-460f, 26f), new Vector2(-24f, 256f), new Color(0.006f, 0.009f, 0.012f, 0.72f));
            telemetry = UiFactory.CreateText(telemetryBand, "Telemetry", "", 18, new Color(0.92f, 0.96f, 0.98f), TextAnchor.UpperLeft);
            RectTransform telemetryRect = telemetry.GetComponent<RectTransform>();
            telemetryRect.anchorMin = Vector2.zero;
            telemetryRect.anchorMax = Vector2.one;
            telemetryRect.offsetMin = new Vector2(18f, 14f);
            telemetryRect.offsetMax = new Vector2(-18f, -14f);
            telemetry.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform visualBand = UiFactory.CreateBand(transform, "HUD visual meters", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-460f, 266f), new Vector2(-24f, 476f), new Color(0.006f, 0.009f, 0.012f, 0.76f));
            UiFactory.CreateBand(visualBand, "Meters accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -3f), Vector2.zero, new Color(0.95f, 0.08f, 0.06f, 0.95f));
            pitStatus = UiFactory.CreateText(visualBand, "Pit status", "", 16, new Color(0.92f, 0.96f, 0.98f), TextAnchor.MiddleLeft);
            RectTransform pitStatusRect = pitStatus.GetComponent<RectTransform>();
            pitStatusRect.anchorMin = new Vector2(0f, 1f);
            pitStatusRect.anchorMax = new Vector2(1f, 1f);
            pitStatusRect.offsetMin = new Vector2(18f, -34f);
            pitStatusRect.offsetMax = new Vector2(-18f, -8f);
            ersFill = CreateMeter(visualBand, "ERS", 138f, new Color(0.25f, 0.78f, 1f));
            tyreTempFill = CreateMeter(visualBand, "TEMP", 112f, new Color(1f, 0.72f, 0.18f));
            tyreWearFill = CreateMeter(visualBand, "WEAR", 86f, new Color(0.34f, 1f, 0.52f));
            fuelFill = CreateMeter(visualBand, "FUEL", 60f, new Color(0.7f, 0.95f, 1f));
            damageFill = CreateMeter(visualBand, "DMG", 34f, new Color(1f, 0.12f, 0.08f));
            pitFill = CreateMeter(visualBand, "PIT", 8f, new Color(0.95f, 0.08f, 0.06f));

            RectTransform timingBand = UiFactory.CreateBand(transform, "Timing panel", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 26f), new Vector2(430f, 218f), new Color(0.006f, 0.009f, 0.012f, 0.72f));
            timing = UiFactory.CreateText(timingBand, "Timing", "", 18, new Color(0.92f, 0.96f, 0.98f), TextAnchor.UpperLeft);
            RectTransform timingRect = timing.GetComponent<RectTransform>();
            timingRect.anchorMin = Vector2.zero;
            timingRect.anchorMax = Vector2.one;
            timingRect.offsetMin = new Vector2(18f, 14f);
            timingRect.offsetMax = new Vector2(-18f, -14f);
            timing.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform drsBand = UiFactory.CreateBand(transform, "DRS cue", new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f), new Vector2(-148f, -28f), new Vector2(148f, 28f), new Color(0.08f, 0.45f, 0.18f, 0.0f));
            drsFlash = UiFactory.CreateText(drsBand, "DRS cue text", "", 22, new Color(0.7f, 1f, 0.76f), TextAnchor.MiddleCenter);
            RectTransform drsRect = drsFlash.GetComponent<RectTransform>();
            drsRect.anchorMin = Vector2.zero;
            drsRect.anchorMax = Vector2.one;
            drsRect.offsetMin = Vector2.zero;
            drsRect.offsetMax = Vector2.zero;

            RectTransform engineerBand = UiFactory.CreateBand(transform, "Engineer radio", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-540f, -126f), new Vector2(-24f, -76f), new Color(0.006f, 0.009f, 0.012f, 0.82f));
            engineerPanel = engineerBand.gameObject;
            UiFactory.CreateBand(engineerBand, "Engineer accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f), new Color(0.2f, 0.72f, 1f, 0.95f));
            engineer = UiFactory.CreateText(engineerBand, "Engineer radio text", "", 17, new Color(0.82f, 0.94f, 1f), TextAnchor.MiddleLeft);
            RectTransform engineerRect = engineer.GetComponent<RectTransform>();
            engineerRect.anchorMin = Vector2.zero;
            engineerRect.anchorMax = Vector2.one;
            engineerRect.offsetMin = new Vector2(20f, 8f);
            engineerRect.offsetMax = new Vector2(-18f, -8f);
            engineerPanel.SetActive(false);

            RectTransform startBand = UiFactory.CreateBand(transform, "Race start lights", new Vector2(0.5f, 0.83f), new Vector2(0.5f, 0.83f), new Vector2(-178f, -34f), new Vector2(178f, 34f), new Color(0.004f, 0.005f, 0.006f, 0.88f));
            startLightPanel = startBand.gameObject;
            HorizontalLayoutGroup lightLayout = startBand.gameObject.AddComponent<HorizontalLayoutGroup>();
            lightLayout.spacing = 14f;
            lightLayout.padding = new RectOffset(18, 18, 12, 12);
            lightLayout.childAlignment = TextAnchor.MiddleCenter;
            lightLayout.childControlWidth = false;
            lightLayout.childControlHeight = false;
            for (int i = 0; i < startLightImages.Length; i++)
            {
                RectTransform light = UiFactory.CreateBand(startBand, "Start light " + (i + 1), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(0.09f, 0.01f, 0.012f, 1f));
                light.sizeDelta = new Vector2(42f, 42f);
                startLightImages[i] = light.GetComponent<Image>();
            }

            startLightPanel.SetActive(false);

            RectTransform feedbackBand = UiFactory.CreateBand(transform, "Qualifying feedback", new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f), new Vector2(-330f, -62f), new Vector2(330f, 62f), new Color(0.006f, 0.009f, 0.012f, 0.82f));
            qualifyingFeedbackPanel = feedbackBand.gameObject;
            qualifyingFeedback = UiFactory.CreateText(feedbackBand, "Qualifying feedback text", "", 28, new Color(0.96f, 0.98f, 1f), TextAnchor.MiddleCenter);
            RectTransform feedbackRect = qualifyingFeedback.GetComponent<RectTransform>();
            feedbackRect.anchorMin = Vector2.zero;
            feedbackRect.anchorMax = Vector2.one;
            feedbackRect.offsetMin = new Vector2(20f, 12f);
            feedbackRect.offsetMax = new Vector2(-20f, -12f);
            qualifyingFeedbackPanel.SetActive(false);

            RectTransform hintBand = UiFactory.CreateBand(transform, "HUD hint panel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-230f, 144f), new Vector2(230f, 174f), new Color(0.006f, 0.009f, 0.012f, 0.44f));
            hint = UiFactory.CreateText(hintBand, "Hint", "Esc pause   Space DRS   R ERS mode   C camera   P pit", 15, new Color(0.78f, 0.86f, 0.9f), TextAnchor.MiddleCenter);
            RectTransform hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = Vector2.one;
            hintRect.offsetMin = Vector2.zero;
            hintRect.offsetMax = Vector2.zero;
        }

        void Update()
        {
            if (race == null || player == null || player.vehicle == null || player.lapTracker == null)
            {
                return;
            }

            VehicleController car = player.vehicle;
            LapTracker lap = player.lapTracker;
            string session = race.CurrentSession == RaceWeekendSession.Qualifying ? "Qualifying" : "Race";
            int sessionLaps = race.CurrentSession == RaceWeekendSession.Qualifying ? 2 : race.RaceLaps;
            string lapLabel = lap.OutLapActive ? "OUT" : lap.DisplayLap + "/" + sessionLaps;
            string eventName = race.EventData == null ? "Prototype GP" : race.EventData.displayName;
            string reaction = race.RaceStartReactionText;
            center.text = session + "  |  " + eventName + "  |  P" + race.GetPosition(player) + "/" + race.DisplayedEntrantCount + "  |  Lap " + lapLabel + "  |  " + race.SessionMessage + (string.IsNullOrEmpty(reaction) ? "" : "  |  " + reaction);
            speed.text = Mathf.RoundToInt(Mathf.Abs(car.CurrentSpeedKph)) + "\n<size=16><color=#AAB8C0>KM/H</color></size>";
            gearText.text = car.CurrentGear.ToString();

            float speedRatio = Mathf.Clamp01(Mathf.Abs(car.CurrentSpeedKph) / car.TargetTopSpeedKph);
            // Simulate revs based on gear speed windows
            float revs = (Mathf.Abs(car.CurrentSpeedKph) % 60f) / 60f;
            if (car.CurrentGear == 8) revs = speedRatio;
            UpdateMeter(revBar, revs, revs > 0.9f ? Color.red : (revs > 0.7f ? Color.yellow : Color.green));

            string drsState = race.DrsStateText(player);
            if (drsState == "AVAILABLE" && previousDrsState != "AVAILABLE")
            {
                SimpleAudioManager.PlayDrsAvailable();
                drsFlashTimer = 1.6f;
            }

            previousDrsState = drsState;
            drsFlashTimer = Mathf.Max(0f, drsFlashTimer - Time.deltaTime);
            string feedbackText = race.QualifyingFeedbackText;
            bool showFeedback = !string.IsNullOrEmpty(feedbackText);
            qualifyingFeedbackPanel.SetActive(showFeedback);
            qualifyingFeedback.text = showFeedback ? feedbackText : "";
            drsFlash.text = string.IsNullOrEmpty(feedbackText) && drsFlashTimer > 0f ? "DRS AVAILABLE" : "";
            string engineerText = race.EngineerMessageText;
            bool showEngineer = !string.IsNullOrEmpty(engineerText) && string.IsNullOrEmpty(feedbackText);
            engineerPanel.SetActive(showEngineer);
            engineer.text = showEngineer ? engineerText : "";
            UpdateRaceStartLights();

            telemetry.text =
                "DRS  " + drsState + "     ERS  " + ErsModeText() + "\n" +
                "TYRE " + car.Tyres.Compound + "     TEMP " + car.Tyres.TemperatureStatus + "\n" +
                "PIT  " + (car.PitLimiterActive ? "LIMITER 80" : race.PitStatusText(player)) + "\n" +
                "ROAD " + (car.IsOffTrackSlowdown ? "OFF TRACK" : "ON TRACK") + "\n" +
                "SLOW " + car.ActiveSlowdownReason;
            UpdateMeters(car);

            string qualifyingDelta = race.CurrentSession == RaceWeekendSession.Qualifying ? "\nDELTA " + race.QualifyingDeltaText(player) : "";
            timing.text =
                (lap.OutLapActive ? "OUT LAP" : "LAP " + UiFactory.FormatTime(lap.CurrentLapTime) + (lap.CurrentLapInvalidated ? "  INV" : "")) + "\n" +
                "BEST " + UiFactory.FormatTime(lap.BestLapTime) + qualifyingDelta + "\n" +
                "LAST " + UiFactory.FormatTime(lap.LastLapTime) + "\n" +
                "S1 " + SectorText(1, lap.LastSector1Time) + "   S2 " + SectorText(2, lap.LastSector2Time) + "   S3 " + SectorText(3, lap.LastSector3Time) + "\n" +
                "NOW S" + lap.CurrentSector + " " + race.LiveSectorText(lap.CurrentSectorTime) + "\n" +
                "GAP  " + race.GapToLeaderText(player) + "   INT  " + race.IntervalAheadText(player) + "\n" +
                "PEN  +" + player.penaltiesSeconds.ToString("0") + "s   " + race.PitStatusText(player);

            UpdateTowerRows();
        }

        void CreateTowerRow(RectTransform parent, int index)
        {
            float top = -42f - index * 21f;
            RectTransform row = UiFactory.CreateBand(parent, "Timing tower row " + index, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, top - 19f), new Vector2(-10f, top), new Color(0.04f, 0.055f, 0.064f, index % 2 == 0 ? 0.74f : 0.48f));
            towerRowBackgrounds[index] = row.GetComponent<Image>();
            towerPositions[index] = CreateTowerCell(row, "Tower pos " + index, 6f, 36f, 13, TextAnchor.MiddleLeft);
            towerDrivers[index] = CreateTowerCell(row, "Tower driver " + index, 38f, 86f, 13, TextAnchor.MiddleLeft);
            RectTransform tyre = UiFactory.CreateBand(row, "Tower tyre " + index, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(92f, -5f), new Vector2(104f, 5f), Color.white);
            towerTyres[index] = tyre.GetComponent<Image>();
            towerLaps[index] = CreateTowerCell(row, "Tower lap " + index, 112f, 146f, 12, TextAnchor.MiddleLeft);
            towerGaps[index] = CreateTowerCell(row, "Tower gap " + index, 150f, 234f, 12, TextAnchor.MiddleLeft);
            towerIntervals[index] = CreateTowerCell(row, "Tower interval " + index, 238f, 326f, 12, TextAnchor.MiddleLeft);
        }

        Text CreateTowerCell(RectTransform parent, string name, float minX, float maxX, int size, TextAnchor alignment)
        {
            Text cell = UiFactory.CreateText(parent, name, "", size, new Color(0.9f, 0.96f, 0.98f), alignment);
            RectTransform rect = cell.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(minX, 0f);
            rect.offsetMax = new Vector2(maxX, 0f);
            return cell;
        }

        void UpdateTowerRows()
        {
            if (race.CurrentSession == RaceWeekendSession.Qualifying)
            {
                UpdateQualifyingTowerRows();
                return;
            }

            tower.text = "POS   DVR      T   LAP      GAP        INT";
            System.Collections.Generic.List<RaceParticipant> order = race.GetRunningOrderSnapshot();
            int count = Mathf.Min(TowerRowCount, order.Count);
            for (int i = 0; i < TowerRowCount; i++)
            {
                if (i >= count)
                {
                    SetTowerRowVisible(i, false);
                    continue;
                }

                RaceParticipant entry = order[i];
                string tyreName = entry.vehicle == null || entry.vehicle.Tyres == null ? "" : entry.vehicle.Tyres.Compound.ToString();
                string lap = entry.lapTracker == null ? "--" : entry.lapTracker.DisplayLap.ToString("00");
                SetTowerRow(i, (i + 1).ToString("00"), DriverCode(entry), TyreColor(tyreName), lap, race.GapToLeaderText(entry), race.IntervalAheadText(entry), entry == player);
            }
        }

        void UpdateQualifyingTowerRows()
        {
            tower.text = "POS   DVR      BEST          GAP";
            string[] lines = race.BuildQualifyingTimingTowerText(player).Split('\n');
            int row = 0;
            for (int i = 1; i < lines.Length && row < TowerRowCount; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line.Trim()))
                {
                    continue;
                }

                bool highlight = line.TrimStart().StartsWith(">");
                string clean = line.Replace(">", "").Trim();
                string[] parts = clean.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                {
                    continue;
                }

                SetTowerRow(row, parts[0], parts[1], new Color(0.34f, 0.78f, 1f), parts[2], parts[3], "", highlight);
                row++;
            }

            for (int i = row; i < TowerRowCount; i++)
            {
                SetTowerRowVisible(i, false);
            }
        }

        void SetTowerRow(int index, string position, string driver, Color tyreColor, string lap, string gap, string interval, bool highlight)
        {
            SetTowerRowVisible(index, true);
            towerRowBackgrounds[index].color = highlight ? new Color(0.95f, 0.08f, 0.06f, 0.86f) : new Color(0.04f, 0.055f, 0.064f, index % 2 == 0 ? 0.74f : 0.48f);
            towerPositions[index].text = position;
            towerDrivers[index].text = driver;
            towerTyres[index].color = tyreColor;
            towerLaps[index].text = lap;
            towerGaps[index].text = gap;
            towerIntervals[index].text = interval;
        }

        void SetTowerRowVisible(int index, bool visible)
        {
            towerRowBackgrounds[index].gameObject.SetActive(visible);
        }

        Color TyreColor(string tyreName)
        {
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

        string BuildTimingTower()
        {
            System.Collections.Generic.List<RaceParticipant> order = race.GetRunningOrderSnapshot();
            string text = "POS  DVR  TYRE LAP   GAP    INT\n";
            int count = Mathf.Min(22, order.Count);
            for (int i = 0; i < count; i++)
            {
                RaceParticipant entry = order[i];
                string marker = entry == player ? ">" : " ";
                string tyre = entry.vehicle == null || entry.vehicle.Tyres == null ? "--" : entry.vehicle.Tyres.Compound.ToString().Substring(0, 1).ToUpper();
                string lap = entry.lapTracker == null ? "--" : entry.lapTracker.DisplayLap.ToString("00");
                text += marker + race.GetPosition(entry).ToString("00") + "   " + DriverCode(entry) + "    " + tyre + "   " + lap + "   " + race.GapToLeaderText(entry) + "   " + race.IntervalAheadText(entry) + "\n";
            }

            return text;
        }

        Image CreateMeter(RectTransform parent, string label, float y, Color fillColor)
        {
            Text labelText = UiFactory.CreateText(parent, label + " label", label, 13, new Color(0.74f, 0.82f, 0.86f), TextAnchor.MiddleLeft);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0f, 0f);
            labelRect.offsetMin = new Vector2(18f, y);
            labelRect.offsetMax = new Vector2(88f, y + 20f);

            RectTransform track = UiFactory.CreateBand(parent, label + " meter track", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(88f, y + 4f), new Vector2(-18f, y + 16f), new Color(0.12f, 0.15f, 0.17f, 0.86f));
            RectTransform fill = UiFactory.CreateBand(track, label + " meter fill", new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, fillColor);
            return fill.GetComponent<Image>();
        }

        void UpdateMeters(VehicleController car)
        {
            UpdateMeter(ersFill, car.ErsBattery, new Color(0.25f, 0.78f, 1f));
            float temp01 = Mathf.InverseLerp(45f, 115f, car.Tyres.Temperature);
            Color tempColor = car.Tyres.TemperatureStatus == "OPT" ? new Color(0.32f, 1f, 0.45f) : (car.Tyres.TemperatureStatus == "HOT" ? new Color(1f, 0.14f, 0.08f) : (car.Tyres.TemperatureStatus == "COLD" ? new Color(0.22f, 0.52f, 1f) : new Color(1f, 0.82f, 0.12f)));
            UpdateMeter(tyreTempFill, temp01, tempColor);
            UpdateMeter(tyreWearFill, 1f - Mathf.Clamp01(car.Tyres.WearPercent / 100f), new Color(0.34f, 1f, 0.52f));
            UpdateMeter(fuelFill, Mathf.Clamp01(car.FuelKg / 42f), new Color(0.7f, 0.95f, 1f));
            UpdateMeter(damageFill, Mathf.Clamp01(car.Damage.OverallPercent / 100f), car.Damage.OverallPercent > 55f ? new Color(1f, 0.05f, 0.03f) : new Color(1f, 0.55f, 0.1f));
            UpdateMeter(pitFill, race.PitStopProgress01(player), new Color(0.95f, 0.08f, 0.06f));
            if (pitStatus != null)
            {
                pitStatus.text = race.PitStatusText(player);
            }
        }

        void UpdateMeter(Image fill, float value, Color color)
        {
            if (fill == null)
            {
                return;
            }

            RectTransform rect = fill.GetComponent<RectTransform>();
            rect.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            fill.color = color;
        }

        void UpdateRaceStartLights()
        {
            if (startLightPanel == null || race == null)
            {
                return;
            }

            bool visible = race.RaceStartLightsVisible;
            startLightPanel.SetActive(visible);
            if (!visible)
            {
                return;
            }

            int lit = race.RaceStartLightCount;
            for (int i = 0; i < startLightImages.Length; i++)
            {
                if (startLightImages[i] != null)
                {
                    startLightImages[i].color = i < lit ? new Color(1f, 0.04f, 0.025f, 1f) : new Color(0.09f, 0.01f, 0.012f, 1f);
                }
            }
        }

        string SectorText(int sector, float time)
        {
            return race == null ? (time <= 0f ? "--.---" : UiFactory.FormatTime(time)) : race.PlayerSectorText(sector, time);
        }

        string ErsModeText()
        {
            if (race == null || race.Settings == null)
            {
                return "BAL";
            }

            int mode = race.Settings.Current.ersMode;
            if (mode == (int)ErsStrategyMode.Harvest)
            {
                return "HARVEST";
            }

            if (mode == (int)ErsStrategyMode.Attack)
            {
                return "DEPLOY";
            }

            return "BAL";
        }

        string DriverCode(RaceParticipant participant)
        {
            if (participant == null)
            {
                return "---";
            }

            if (participant.driverData != null && !string.IsNullOrEmpty(participant.driverData.abbreviation))
            {
                return participant.driverData.abbreviation.ToUpper();
            }

            string name = string.IsNullOrEmpty(participant.driverName) ? "PLY" : participant.driverName.ToUpper();
            name = name.Replace(" ", "");
            return name.Length > 3 ? name.Substring(0, 3) : name.PadRight(3, '-');
        }
    }
}
