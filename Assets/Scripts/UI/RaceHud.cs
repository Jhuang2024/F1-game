using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    public class RaceHud : MonoBehaviour
    {
        const int TowerRowCount = 22;
        const float SlowUpdateInterval = 0.2f;

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
        Text speedUnit;
        Text drsFlash;
        Text engineer;
        Text qualifyingFeedback;
        Text pitStatus;
        Image ersFill;
        Image fuelFill;
        Image damageFill;
        Image pitFill;
        Image revBar;
        Text gearText;
        Image tyreFl, tyreFr, tyreRl, tyreRr;
        Image carSilhouette;
        GameObject qualifyingFeedbackPanel;
        GameObject engineerPanel;
        GameObject startLightPanel;
        Image[] startLightImages = new Image[5];
        string previousDrsState = "";
        float drsFlashTimer;
        float slowUpdateTimer;

        // Queued notification system: short race events fade in, hold, and fade out
        // instead of popping. One notification is visible at a time.
        struct HudNotification
        {
            public string text;
            public Color color;
        }

        readonly Queue<HudNotification> notificationQueue = new Queue<HudNotification>();
        CanvasGroup notificationGroup;
        Text notificationText;
        Image notificationAccent;
        float notificationTimer;
        int notificationPhase; // 0 idle, 1 fade in, 2 hold, 3 fade out

        // Watched values for notification triggers.
        string watchedTyreTemp = "";
        int watchedTrackLimitWarnings;
        float watchedBestLap;
        int watchedPitStops;
        bool watchedLowFuel;
        int watchedDamageBand;
        bool watchedFinalLap;
        bool watchedPitWindow;

        // Track progress strip (minimap-lite): start/finish marker plus live dots.
        RectTransform progressStrip;
        Image playerProgressDot;
        readonly List<Image> aiProgressDots = new List<Image>();
        const float ProgressStripWidth = 520f;

        // Lights-out "GO" moment.
        Text goFlash;
        float goFlashTimer;
        bool lightsWereVisible;

        // Debug overlay (F1).
        GameObject debugPanel;
        Text debugText;
        float debugTimer;
        float fpsSmoothed = 60f;

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
            float hudScale = race != null && race.Settings != null ? Mathf.Clamp(race.Settings.Current.hudScale, 0.75f, 1.3f) : 1f;
            root.localScale = new Vector3(hudScale, hudScale, 1f);

            RectTransform topBand = UiFactory.CreateBand(transform, "HUD top band", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -58f), Vector2.zero, UiFactory.PanelDarker);
            UiFactory.CreateBand(topBand, "HUD red rule", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f), UiFactory.Accent);
            center = UiFactory.CreateText(topBand, "Center", "", 20, UiFactory.TextPrimary, TextAnchor.MiddleCenter);
            RectTransform centerRect = center.GetComponent<RectTransform>();
            centerRect.anchorMin = Vector2.zero;
            centerRect.anchorMax = Vector2.one;
            centerRect.offsetMin = Vector2.zero;
            centerRect.offsetMax = Vector2.zero;

            BuildNotificationPanel();

            RectTransform towerBand = UiFactory.CreateBand(transform, "Timing tower", new Vector2(0f, 0.18f), new Vector2(0f, 0.88f), new Vector2(24f, 0f), new Vector2(360f, 0f), new Color(0.006f, 0.009f, 0.012f, 0.72f));
            UiFactory.CreateBand(towerBand, "Tower accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f), UiFactory.Accent);
            tower = UiFactory.CreateText(towerBand, "Tower header", "", 13, UiFactory.TextMuted, TextAnchor.MiddleLeft);
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
            UiFactory.CreateBand(speedBand, "Dashboard accent", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 4f), UiFactory.Accent);

            speed = UiFactory.CreateText(speedBand, "Speed", "0", 48, Color.white, TextAnchor.MiddleCenter);
            RectTransform speedRect = speed.GetComponent<RectTransform>();
            speedRect.anchorMin = new Vector2(0.5f, 0.5f);
            speedRect.anchorMax = new Vector2(0.5f, 0.5f);
            speedRect.sizeDelta = new Vector2(190f, 60f);
            speedRect.anchoredPosition = new Vector2(-62f, 6f);

            speedUnit = UiFactory.CreateText(speedBand, "Speed unit", "KM/H", 14, UiFactory.TextMuted, TextAnchor.MiddleCenter);
            RectTransform unitRect = speedUnit.GetComponent<RectTransform>();
            unitRect.anchorMin = new Vector2(0.5f, 0.5f);
            unitRect.anchorMax = new Vector2(0.5f, 0.5f);
            unitRect.sizeDelta = new Vector2(120f, 24f);
            unitRect.anchoredPosition = new Vector2(-62f, -30f);

            gearText = UiFactory.CreateText(speedBand, "Gear", "1", 58, UiFactory.Accent, TextAnchor.MiddleCenter);
            RectTransform gearRect = gearText.GetComponent<RectTransform>();
            gearRect.anchorMin = new Vector2(0.5f, 0.5f);
            gearRect.anchorMax = new Vector2(0.5f, 0.5f);
            gearRect.sizeDelta = new Vector2(80f, 80f);
            gearRect.anchoredPosition = new Vector2(80f, 6f);

            RectTransform revTrack = UiFactory.CreateBand(speedBand, "Rev track", new Vector2(0.08f, 0.84f), new Vector2(0.92f, 0.93f), Vector2.zero, Vector2.zero, new Color(0.12f, 0.14f, 0.16f, 0.9f));
            RectTransform revFill = UiFactory.CreateBand(revTrack, "Rev fill", new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, Color.white);
            revBar = revFill.GetComponent<Image>();

            RectTransform telemetryBand = UiFactory.CreateBand(transform, "Telemetry panel", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-460f, 26f), new Vector2(-24f, 256f), new Color(0.006f, 0.009f, 0.012f, 0.72f));
            telemetry = UiFactory.CreateText(telemetryBand, "Telemetry", "", 18, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            RectTransform telemetryRect = telemetry.GetComponent<RectTransform>();
            telemetryRect.anchorMin = Vector2.zero;
            telemetryRect.anchorMax = Vector2.one;
            telemetryRect.offsetMin = new Vector2(18f, 14f);
            telemetryRect.offsetMax = new Vector2(-18f, -14f);
            telemetry.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform visualBand = UiFactory.CreateBand(transform, "HUD visual meters", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-460f, 266f), new Vector2(-24f, 620f), new Color(0.006f, 0.009f, 0.012f, 0.82f));
            UiFactory.CreateBand(visualBand, "Meters accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -3f), Vector2.zero, UiFactory.Accent);

            RectTransform tyreContainer = UiFactory.CreateRect(visualBand, "Tyres", new Vector2(0.1f, 0.55f), new Vector2(0.45f, 0.92f), Vector2.zero, Vector2.zero);
            tyreFl = CreateTyreIcon(tyreContainer, "FL", new Vector2(0, 1));
            tyreFr = CreateTyreIcon(tyreContainer, "FR", new Vector2(1, 1));
            tyreRl = CreateTyreIcon(tyreContainer, "RL", new Vector2(0, 0));
            tyreRr = CreateTyreIcon(tyreContainer, "RR", new Vector2(1, 0));

            RectTransform damageContainer = UiFactory.CreateRect(visualBand, "Damage", new Vector2(0.55f, 0.55f), new Vector2(0.9f, 0.92f), Vector2.zero, Vector2.zero);
            RectTransform silhouette = UiFactory.CreateBand(damageContainer, "Silhouette", new Vector2(0.35f, 0.1f), new Vector2(0.65f, 0.9f), Vector2.zero, Vector2.zero, new Color(0.2f, 0.22f, 0.24f, 1f));
            carSilhouette = silhouette.GetComponent<Image>();

            pitStatus = UiFactory.CreateText(visualBand, "Pit status", "", 16, UiFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform pitStatusRect = pitStatus.GetComponent<RectTransform>();
            pitStatusRect.anchorMin = new Vector2(0f, 0f);
            pitStatusRect.anchorMax = new Vector2(1f, 0f);
            pitStatusRect.offsetMin = new Vector2(18f, 185f);
            pitStatusRect.offsetMax = new Vector2(-18f, 215f);

            ersFill = CreateMeter(visualBand, "ERS", 155f, new Color(0.25f, 0.78f, 1f));
            fuelFill = CreateMeter(visualBand, "FUEL", 125f, new Color(0.7f, 0.95f, 1f));
            damageFill = CreateMeter(visualBand, "DMG", 95f, new Color(1f, 0.12f, 0.08f));
            pitFill = CreateMeter(visualBand, "PIT", 65f, UiFactory.Accent);

            RectTransform timingBand = UiFactory.CreateBand(transform, "Timing panel", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 26f), new Vector2(430f, 218f), new Color(0.006f, 0.009f, 0.012f, 0.72f));
            timing = UiFactory.CreateText(timingBand, "Timing", "", 18, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            RectTransform timingRect = timing.GetComponent<RectTransform>();
            timingRect.anchorMin = Vector2.zero;
            timingRect.anchorMax = Vector2.one;
            timingRect.offsetMin = new Vector2(18f, 14f);
            timingRect.offsetMax = new Vector2(-18f, -14f);
            timing.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform drsBand = UiFactory.CreateBand(transform, "DRS cue", new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f), new Vector2(-148f, -28f), new Vector2(148f, 28f), new Color(0.08f, 0.45f, 0.18f, 0f));
            drsFlash = UiFactory.CreateText(drsBand, "DRS cue text", "", 22, new Color(0.7f, 1f, 0.76f), TextAnchor.MiddleCenter);
            RectTransform drsRect = drsFlash.GetComponent<RectTransform>();
            drsRect.anchorMin = Vector2.zero;
            drsRect.anchorMax = Vector2.one;
            drsRect.offsetMin = Vector2.zero;
            drsRect.offsetMax = Vector2.zero;

            RectTransform engineerBand = UiFactory.CreateBand(transform, "Engineer radio", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-540f, -126f), new Vector2(-24f, -76f), new Color(0.006f, 0.009f, 0.012f, 0.82f));
            engineerPanel = engineerBand.gameObject;
            UiFactory.CreateBand(engineerBand, "Engineer accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f), UiFactory.AccentCyan);
            engineer = UiFactory.CreateText(engineerBand, "Engineer radio text", "", 17, new Color(0.82f, 0.94f, 1f), TextAnchor.MiddleLeft);
            RectTransform engineerRect = engineer.GetComponent<RectTransform>();
            engineerRect.anchorMin = Vector2.zero;
            engineerRect.anchorMax = Vector2.one;
            engineerRect.offsetMin = new Vector2(20f, 8f);
            engineerRect.offsetMax = new Vector2(-18f, -8f);
            engineerPanel.SetActive(false);

            RectTransform startBand = UiFactory.CreateBand(transform, "Race start lights", new Vector2(0.5f, 0.83f), new Vector2(0.5f, 0.83f), new Vector2(-224f, -44f), new Vector2(224f, 44f), new Color(0.004f, 0.005f, 0.006f, 0.92f));
            startLightPanel = startBand.gameObject;
            UiFactory.CreateBand(startBand, "Start lights rule", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f), UiFactory.Accent);
            HorizontalLayoutGroup lightLayout = startBand.gameObject.AddComponent<HorizontalLayoutGroup>();
            lightLayout.spacing = 18f;
            lightLayout.padding = new RectOffset(24, 24, 14, 14);
            lightLayout.childAlignment = TextAnchor.MiddleCenter;
            lightLayout.childControlWidth = false;
            lightLayout.childControlHeight = false;
            for (int i = 0; i < startLightImages.Length; i++)
            {
                RectTransform light = UiFactory.CreateBand(startBand, "Start light " + (i + 1), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(0.09f, 0.01f, 0.012f, 1f));
                light.sizeDelta = new Vector2(58f, 58f);
                startLightImages[i] = light.GetComponent<Image>();
            }

            startLightPanel.SetActive(false);

            goFlash = UiFactory.CreateText(transform, "Lights out flash", "", 52, new Color(0.35f, 1f, 0.45f), TextAnchor.MiddleCenter);
            RectTransform goRect = goFlash.GetComponent<RectTransform>();
            goRect.anchorMin = new Vector2(0.5f, 0.83f);
            goRect.anchorMax = new Vector2(0.5f, 0.83f);
            goRect.sizeDelta = new Vector2(560f, 80f);
            goRect.anchoredPosition = Vector2.zero;

            BuildProgressStrip();

            RectTransform feedbackBand = UiFactory.CreateBand(transform, "Qualifying feedback", new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f), new Vector2(-330f, -62f), new Vector2(330f, 62f), new Color(0.006f, 0.009f, 0.012f, 0.82f));
            qualifyingFeedbackPanel = feedbackBand.gameObject;
            qualifyingFeedback = UiFactory.CreateText(feedbackBand, "Qualifying feedback text", "", 28, UiFactory.TextPrimary, TextAnchor.MiddleCenter);
            RectTransform feedbackRect = qualifyingFeedback.GetComponent<RectTransform>();
            feedbackRect.anchorMin = Vector2.zero;
            feedbackRect.anchorMax = Vector2.one;
            feedbackRect.offsetMin = new Vector2(20f, 12f);
            feedbackRect.offsetMax = new Vector2(-20f, -12f);
            qualifyingFeedbackPanel.SetActive(false);

            RectTransform hintBand = UiFactory.CreateBand(transform, "HUD hint panel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-286f, 144f), new Vector2(286f, 174f), new Color(0.006f, 0.009f, 0.012f, 0.44f));
            string hintText = race != null && race.IsTimeTrial
                ? "Esc pause   R ERS (hold: reset car)   C camera   F1 debug   F2 next track"
                : "Esc pause   Space DRS   R ERS (hold: reset car)   C camera   P pit   F1 debug";
            hint = UiFactory.CreateText(hintBand, "Hint", hintText, 14, new Color(0.78f, 0.86f, 0.9f), TextAnchor.MiddleCenter);
            RectTransform hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = Vector2.one;
            hintRect.offsetMin = Vector2.zero;
            hintRect.offsetMax = Vector2.zero;

            BuildDebugOverlay();
            ResetWatchers();
        }

        void BuildNotificationPanel()
        {
            RectTransform band = UiFactory.CreateBand(transform, "HUD notification", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-240f, -122f), new Vector2(240f, -70f), new Color(0.006f, 0.009f, 0.012f, 0.88f));
            notificationAccent = UiFactory.CreateBand(band, "Notification accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f), UiFactory.Accent).GetComponent<Image>();
            notificationText = UiFactory.CreateText(band, "Notification text", "", 19, UiFactory.TextPrimary, TextAnchor.MiddleCenter);
            RectTransform textRect = notificationText.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 4f);
            textRect.offsetMax = new Vector2(-12f, -4f);
            notificationGroup = band.gameObject.AddComponent<CanvasGroup>();
            notificationGroup.alpha = 0f;
            notificationGroup.blocksRaycasts = false;
            notificationGroup.interactable = false;
        }

        void BuildProgressStrip()
        {
            progressStrip = UiFactory.CreateBand(transform, "Track progress strip", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-ProgressStripWidth * 0.5f - 12f, -152f), new Vector2(ProgressStripWidth * 0.5f + 12f, -132f), new Color(0.006f, 0.009f, 0.012f, 0.7f));

            // Start/finish marker at the left edge of the strip.
            UiFactory.CreateBand(progressStrip, "Start finish marker", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(10f, 3f), new Vector2(13f, -3f), Color.white);

            for (int i = 0; i < TowerRowCount - 1; i++)
            {
                Image dot = CreateProgressDot("AI dot " + i, 6f, new Color(0.75f, 0.82f, 0.86f, 0.85f));
                aiProgressDots.Add(dot);
            }

            playerProgressDot = CreateProgressDot("Player dot", 11f, UiFactory.Accent);
        }

        Image CreateProgressDot(string name, float size, Color color)
        {
            RectTransform dot = UiFactory.CreateBand(progressStrip, name, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, color);
            dot.sizeDelta = new Vector2(size, size);
            dot.anchoredPosition = new Vector2(12f, 0f);
            return dot.GetComponent<Image>();
        }

        void UpdateProgressStrip()
        {
            if (progressStrip == null || race.Track == null)
            {
                return;
            }

            int aiIndex = 0;
            for (int i = 0; i < race.Participants.Count; i++)
            {
                RaceParticipant entry = race.Participants[i];
                if (entry == null || entry.lapTracker == null)
                {
                    continue;
                }

                float x = 12f + entry.lapTracker.CurrentProgress.normalized * ProgressStripWidth;
                if (entry == player)
                {
                    playerProgressDot.rectTransform.anchoredPosition = new Vector2(x, 0f);
                }
                else if (aiIndex < aiProgressDots.Count)
                {
                    Image dot = aiProgressDots[aiIndex];
                    if (!dot.gameObject.activeSelf)
                    {
                        dot.gameObject.SetActive(true);
                    }

                    dot.rectTransform.anchoredPosition = new Vector2(x, 0f);
                    dot.color = entry.retired ? new Color(0.4f, 0.4f, 0.4f, 0.5f) : new Color(0.75f, 0.82f, 0.86f, 0.85f);
                    aiIndex++;
                }
            }

            for (int i = aiIndex; i < aiProgressDots.Count; i++)
            {
                if (aiProgressDots[i].gameObject.activeSelf)
                {
                    aiProgressDots[i].gameObject.SetActive(false);
                }
            }
        }

        void BuildDebugOverlay()
        {
            RectTransform panel = UiFactory.CreateBand(transform, "Debug overlay", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -420f), new Vector2(420f, -100f), new Color(0f, 0f, 0f, 0.78f));
            debugPanel = panel.gameObject;
            debugText = UiFactory.CreateText(panel, "Debug text", "", 15, new Color(0.6f, 1f, 0.65f), TextAnchor.UpperLeft);
            RectTransform textRect = debugText.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 10f);
            textRect.offsetMax = new Vector2(-14f, -10f);
            debugText.verticalOverflow = VerticalWrapMode.Overflow;
            debugPanel.SetActive(false);
        }

        public void PushNotification(string text, Color accentColor)
        {
            if (string.IsNullOrEmpty(text) || notificationQueue.Count > 5)
            {
                return;
            }

            notificationQueue.Enqueue(new HudNotification { text = text, color = accentColor });
        }

        void ResetWatchers()
        {
            watchedTyreTemp = "";
            watchedTrackLimitWarnings = 0;
            watchedBestLap = 0f;
            watchedPitStops = 0;
            watchedLowFuel = false;
            watchedDamageBand = 0;
            watchedFinalLap = false;
            watchedPitWindow = false;
        }

        void Update()
        {
            if (race == null || player == null || player.vehicle == null || player.lapTracker == null)
            {
                return;
            }

            fpsSmoothed = Mathf.Lerp(fpsSmoothed, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.08f);
            VehicleController car = player.vehicle;
            LapTracker lap = player.lapTracker;

            UpdateFastElements(car);
            UpdateNotificationAnimation();
            UpdateRaceStartLights();
            UpdateProgressStrip();
            UpdateGoFlash();

            if (Input.GetKeyDown(KeyCode.F1))
            {
                debugPanel.SetActive(!debugPanel.activeSelf);
            }

            if (debugPanel.activeSelf)
            {
                debugTimer -= Time.deltaTime;
                if (debugTimer <= 0f)
                {
                    debugTimer = 0.25f;
                    UpdateDebugOverlay(car, lap);
                }
            }

            slowUpdateTimer -= Time.deltaTime;
            if (slowUpdateTimer > 0f)
            {
                return;
            }

            slowUpdateTimer = SlowUpdateInterval;
            UpdateSlowElements(car, lap);
            UpdateNotificationWatchers(car, lap);
        }

        void UpdateFastElements(VehicleController car)
        {
            bool mph = race.Settings != null && race.Settings.Current.useMphUnits;
            float displaySpeed = Mathf.Abs(car.CurrentSpeedKph) * (mph ? 0.621371f : 1f);
            speed.text = Mathf.RoundToInt(displaySpeed).ToString();
            speedUnit.text = mph ? "MPH" : "KM/H";
            gearText.text = car.CurrentGear.ToString();

            float speedRatio = Mathf.Clamp01(Mathf.Abs(car.CurrentSpeedKph) / car.TargetTopSpeedKph);
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
        }

        void UpdateSlowElements(VehicleController car, LapTracker lap)
        {
            string session = race.IsTimeTrial ? "Time Trial" : (race.CurrentSession == RaceWeekendSession.Qualifying ? "Qualifying" : "Race");
            int sessionLaps = race.CurrentSession == RaceWeekendSession.Qualifying ? 2 : race.RaceLaps;
            string lapLabel = lap.OutLapActive ? "OUT" : (race.IsTimeTrial ? "L" + lap.DisplayLap : lap.DisplayLap + "/" + sessionLaps);
            string eventName = race.EventData == null ? "Prototype GP" : race.EventData.displayName;
            string reaction = race.RaceStartReactionText;
            string position = race.IsTimeTrial ? "" : "  |  P" + race.GetPosition(player) + "/" + race.DisplayedEntrantCount;
            center.text = session + "  |  " + eventName + position + "  |  Lap " + lapLabel + "  |  " + race.SessionMessage + (string.IsNullOrEmpty(reaction) ? "" : "  |  " + reaction);

            telemetry.text =
                "DRS  " + ColoredDrsText(race.DrsStateText(player)) + "     ERS  " + ColoredErsText() + "\n" +
                "TYRE " + ColoredTyreText(car.Tyres) + "\n" +
                "PIT  " + (car.PitLimiterActive ? "<color=#F5D45C>LIMITER 80</color>" : race.PitStatusText(player)) + "\n" +
                "ROAD " + (car.IsOffTrackSlowdown ? "<color=#FF6C6C>OFF TRACK</color>" : "<color=#8CE99A>ON TRACK</color>") + "\n" +
                "SLOW " + car.ActiveSlowdownReason;
            UpdateMeters(car);

            string qualifyingDelta = race.CurrentSession == RaceWeekendSession.Qualifying ? "\nDELTA " + race.QualifyingDeltaText(player) : "";
            string recordLine = "";
            if (race.IsTimeTrial && race.EventData != null)
            {
                float record = PlayerRecordsStore.GetBestLap(race.EventData.trackId);
                recordLine = "\nRECORD " + UiFactory.FormatTime(record);
            }

            timing.text =
                (lap.OutLapActive ? "OUT LAP" : "LAP " + UiFactory.FormatTime(lap.CurrentLapTime) + (lap.CurrentLapInvalidated ? "  INV" : "")) + "\n" +
                "BEST " + UiFactory.FormatTime(lap.BestLapTime) + qualifyingDelta + recordLine + "\n" +
                "LAST " + UiFactory.FormatTime(lap.LastLapTime) + "\n" +
                "S1 " + SectorText(1, lap.LastSector1Time) + "   S2 " + SectorText(2, lap.LastSector2Time) + "   S3 " + SectorText(3, lap.LastSector3Time) + "\n" +
                "NOW S" + lap.CurrentSector + " " + race.LiveSectorText(lap.CurrentSectorTime) + "\n" +
                (race.IsTimeTrial
                    ? "CHECKPOINTS " + lap.CheckpointsPassed + "/16"
                    : "GAP  " + race.GapToLeaderText(player) + "   INT  " + race.IntervalAheadText(player) + "\n" +
                      "PEN  +" + player.penaltiesSeconds.ToString("0") + "s   " + race.PitStatusText(player));

            UpdateTowerRows();
        }

        void UpdateNotificationWatchers(VehicleController car, LapTracker lap)
        {
            if (!race.CanDrive)
            {
                return;
            }

            string tyreTemp = car.Tyres.TemperatureStatus;
            if (tyreTemp == "HOT" && watchedTyreTemp != "HOT")
            {
                PushNotification("TYRES OVERHEATING", new Color(1f, 0.55f, 0.1f));
            }

            watchedTyreTemp = tyreTemp;

            if (player.trackLimitWarnings > watchedTrackLimitWarnings)
            {
                PushNotification("TRACK LIMITS WARNING " + player.trackLimitWarnings + "/3", new Color(1f, 0.85f, 0.2f));
            }

            watchedTrackLimitWarnings = player.trackLimitWarnings;

            if (lap.BestLapTime > 0f && (watchedBestLap <= 0f || lap.BestLapTime < watchedBestLap - 0.001f))
            {
                if (watchedBestLap > 0f)
                {
                    PushNotification("NEW BEST LAP  " + UiFactory.FormatTime(lap.BestLapTime), new Color(0.55f, 0.4f, 1f));
                }

                watchedBestLap = lap.BestLapTime;
            }

            if (player.pitStops > watchedPitStops)
            {
                PushNotification("PIT STOP COMPLETE", UiFactory.AccentCyan);
            }

            watchedPitStops = player.pitStops;

            bool lowFuel = car.FuelKg < 7f;
            if (lowFuel && !watchedLowFuel)
            {
                PushNotification("LOW FUEL", new Color(1f, 0.85f, 0.2f));
            }

            watchedLowFuel = lowFuel;

            int damageBand = car.Damage.OverallPercent > 55f ? 2 : (car.Damage.OverallPercent > 25f ? 1 : 0);
            if (damageBand > watchedDamageBand)
            {
                PushNotification(damageBand == 2 ? "HEAVY DAMAGE" : "CAR DAMAGE", new Color(1f, 0.12f, 0.08f));
            }

            watchedDamageBand = damageBand;

            if (!race.IsTimeTrial && race.CurrentSession != RaceWeekendSession.Qualifying)
            {
                bool finalLap = lap.DisplayLap >= race.RaceLaps && !lap.OutLapActive;
                if (finalLap && !watchedFinalLap)
                {
                    PushNotification("FINAL LAP", Color.white);
                }

                watchedFinalLap = finalLap;

                bool pitWindow = player.pitStops == 0 && lap.CompletedLaps >= race.RecommendedPitLap(player);
                if (pitWindow && !watchedPitWindow)
                {
                    PushNotification("PIT WINDOW OPEN", UiFactory.AccentCyan);
                }

                watchedPitWindow = pitWindow;
            }
        }

        void UpdateNotificationAnimation()
        {
            if (notificationPhase == 0)
            {
                if (notificationQueue.Count == 0)
                {
                    return;
                }

                HudNotification next = notificationQueue.Dequeue();
                notificationText.text = next.text;
                notificationAccent.color = next.color;
                notificationPhase = 1;
                notificationTimer = 0f;
            }

            notificationTimer += Time.deltaTime;
            if (notificationPhase == 1)
            {
                notificationGroup.alpha = Mathf.Clamp01(notificationTimer / 0.25f);
                if (notificationTimer >= 0.25f)
                {
                    notificationPhase = 2;
                    notificationTimer = 0f;
                }
            }
            else if (notificationPhase == 2)
            {
                notificationGroup.alpha = 1f;
                if (notificationTimer >= 2.4f)
                {
                    notificationPhase = 3;
                    notificationTimer = 0f;
                }
            }
            else if (notificationPhase == 3)
            {
                notificationGroup.alpha = 1f - Mathf.Clamp01(notificationTimer / 0.4f);
                if (notificationTimer >= 0.4f)
                {
                    notificationPhase = 0;
                    notificationGroup.alpha = 0f;
                }
            }
        }

        void UpdateDebugOverlay(VehicleController car, LapTracker lap)
        {
            TrackProgress progress = lap.CurrentProgress;
            bool roadColliderOk = race.Track != null && race.Track.roadCollider != null && race.Track.roadCollider.sharedMesh != null;
            debugText.text =
                "FPS " + fpsSmoothed.ToString("0") + "\n" +
                "PROGRESS " + progress.normalized.ToString("0.000") + "  DIST " + progress.distance.ToString("0") + "m\n" +
                "SECTOR " + lap.CurrentSector + "  LAP " + lap.DisplayLap + "  CHECKPOINT " + lap.CurrentCheckpointIndex + " (" + lap.CheckpointsPassed + " passed)\n" +
                "LATERAL " + progress.lateralDistance.ToString("0.00") + "m\n" +
                "SPEED " + car.CurrentSpeedKph.ToString("0") + " kph  GEAR " + car.CurrentGear + "\n" +
                "TYRE " + car.Tyres.Compound + "  WEAR " + car.Tyres.WearPercent.ToString("0") + "%  " + car.Tyres.TemperatureStatus + "\n" +
                "FUEL " + car.FuelKg.ToString("0.0") + "kg  DMG " + car.Damage.OverallPercent.ToString("0") + "%\n" +
                "AI COUNT " + Mathf.Max(0, race.Participants.Count - 1) + "  STATE " + (race.IsPaused ? "PAUSED" : (race.IsRaceFinished ? "FINISHED" : "RUNNING")) + "\n" +
                "ROAD COLLIDER " + (roadColliderOk ? "OK" : "MISSING") + "\n" +
                "SLOWDOWN " + car.ActiveSlowdownReason + "\n" +
                "Hold R to reset to track";
        }

        void CreateTowerRow(RectTransform parent, int index)
        {
            float top = -42f - index * 21f;
            RectTransform row = UiFactory.CreateBand(parent, "Timing tower row " + index, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, top - 19f), new Vector2(-10f, top), index % 2 == 0 ? UiFactory.RowEven : UiFactory.RowOdd);
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

            if (race.IsTimeTrial)
            {
                tower.text = "TIME TRIAL";
                LapTracker lap = player.lapTracker;
                SetTowerRow(0, "P1", DriverCode(player), TyreColor(player.vehicle != null && player.vehicle.Tyres != null ? player.vehicle.Tyres.Compound.ToString() : ""), lap.DisplayLap.ToString("00"), UiFactory.FormatTime(lap.BestLapTime), "", true);
                for (int i = 1; i < TowerRowCount; i++)
                {
                    SetTowerRowVisible(i, false);
                }

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
            Color leaderTint = index == 0 && !highlight ? new Color(0.1f, 0.14f, 0.2f, 0.86f) : (index % 2 == 0 ? UiFactory.RowEven : UiFactory.RowOdd);
            towerRowBackgrounds[index].color = highlight ? new Color(0.95f, 0.08f, 0.06f, 0.86f) : leaderTint;
            towerPositions[index].text = position;
            towerDrivers[index].text = driver;
            towerTyres[index].color = tyreColor;
            towerLaps[index].text = lap;
            towerGaps[index].text = gap;
            towerIntervals[index].text = interval;
        }

        void SetTowerRowVisible(int index, bool visible)
        {
            if (towerRowBackgrounds[index] != null && towerRowBackgrounds[index].gameObject.activeSelf != visible)
            {
                towerRowBackgrounds[index].gameObject.SetActive(visible);
            }
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
            UpdateMeter(fuelFill, Mathf.Clamp01(car.FuelKg / 42f), new Color(0.7f, 0.95f, 1f));
            UpdateMeter(damageFill, Mathf.Clamp01(car.Damage.OverallPercent / 100f), car.Damage.OverallPercent > 55f ? new Color(1f, 0.05f, 0.03f) : new Color(1f, 0.55f, 0.1f));
            UpdateMeter(pitFill, race.PitStopProgress01(player), UiFactory.Accent);

            Color tyreColor = GetTyreColorByCondition(car.Tyres);
            tyreFl.color = tyreColor;
            tyreFr.color = tyreColor;
            tyreRl.color = tyreColor;
            tyreRr.color = tyreColor;

            carSilhouette.color = Color.Lerp(new Color(0.2f, 0.22f, 0.24f), Color.red, car.Damage.OverallPercent / 100f);

            if (pitStatus != null)
            {
                pitStatus.text = race.PitStatusText(player);
            }
        }

        Color GetTyreColorByCondition(TyreState tyres)
        {
            if (tyres.TemperatureStatus == "HOT") return Color.red;
            if (tyres.TemperatureStatus == "COLD") return Color.cyan;
            if (tyres.Wear < 0.4f) return new Color(1f, 0.5f, 0f);
            return Color.green;
        }

        Image CreateTyreIcon(RectTransform parent, string name, Vector2 pivot)
        {
            RectTransform icon = UiFactory.CreateBand(parent, name, pivot, pivot, new Vector2(-15, -25), new Vector2(15, 25), Color.green);
            return icon.GetComponent<Image>();
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

            float pulse = value < 0.15f ? 0.75f + Mathf.Sin(Time.time * 12f) * 0.25f : 1f;
            fill.color = color * pulse;
        }

        void UpdateRaceStartLights()
        {
            if (startLightPanel == null || race == null)
            {
                return;
            }

            bool visible = race.RaceStartLightsVisible;
            if (lightsWereVisible && !visible && race.CanDrive)
            {
                goFlashTimer = 1.5f;
                SimpleAudioManager.PlayDrsAvailable();
            }

            lightsWereVisible = visible;
            if (startLightPanel.activeSelf != visible)
            {
                startLightPanel.SetActive(visible);
            }

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

        void UpdateGoFlash()
        {
            if (goFlash == null)
            {
                return;
            }

            goFlashTimer = Mathf.Max(0f, goFlashTimer - Time.deltaTime);
            if (goFlashTimer <= 0f)
            {
                if (!string.IsNullOrEmpty(goFlash.text))
                {
                    goFlash.text = "";
                }

                return;
            }

            goFlash.text = "LIGHTS OUT";
            float alpha = Mathf.Clamp01(goFlashTimer / 0.6f);
            goFlash.color = new Color(0.35f, 1f, 0.45f, alpha);
        }

        string SectorText(int sector, float time)
        {
            return race == null ? (time <= 0f ? "--.---" : UiFactory.FormatTime(time)) : race.PlayerSectorText(sector, time);
        }

        string ColoredDrsText(string state)
        {
            if (state == "ACTIVE")
            {
                return "<color=#5CFF8A>ACTIVE</color>";
            }

            if (state == "AVAILABLE")
            {
                return "<color=#5CD6FF>AVAILABLE</color>";
            }

            return "<color=#8A98A2>OFF</color>";
        }

        string ColoredErsText()
        {
            string mode = ErsModeText();
            if (mode == "DEPLOY")
            {
                return "<color=#5CD6FF>DEPLOY</color>";
            }

            if (mode == "HARVEST")
            {
                return "<color=#F5D45C>HARVEST</color>";
            }

            return "BAL";
        }

        string ColoredTyreText(TyreState tyres)
        {
            string temp = tyres.TemperatureStatus;
            string tempColored = temp == "HOT"
                ? "<color=#FF6C6C>HOT</color>"
                : (temp == "COLD" ? "<color=#5CD6FF>COLD</color>" : "<color=#8CE99A>" + temp + "</color>");
            return tyres.Compound + "  " + tempColored + "  WEAR " + Mathf.RoundToInt(tyres.WearPercent) + "%";
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
