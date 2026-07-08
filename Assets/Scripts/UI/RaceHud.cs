using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    // In-race HUD, rebuilt around edge-pinned responsive panels:
    //   top center   - session band (session, event, lap, position, messages)
    //   left center  - timing tower
    //   bottom left  - lap timing card
    //   bottom center- speed/gear dash with rev bar and state pills
    //   right center - vertical card stack (tyres, car, pit, radio)
    // Every panel is anchored and pivoted to its own screen edge, so hudScale and
    // odd resolutions can never push widgets offscreen.
    public class RaceHud : MonoBehaviour
    {
        const int TowerRowCount = 22;
        const float SlowUpdateInterval = 0.2f;
        const float RightStackWidth = 340f;

        RaceManager race;
        RaceParticipant player;
        bool compact;
        float hudScale = 1f;

        // Top band.
        Text sessionLabelText;
        Text eventNameText;
        Text positionBadgeText;
        Text lapCounterText;
        Text sessionMessageText;
        Image topAccent;
        float trackLimitFlashTimer;
        int seenTrackLimitWarnings;

        // Timing tower.
        Text tower;
        Image[] towerRowBackgrounds = new Image[TowerRowCount];
        Text[] towerPositions = new Text[TowerRowCount];
        Text[] towerDrivers = new Text[TowerRowCount];
        Image[] towerTyres = new Image[TowerRowCount];
        Text[] towerLaps = new Text[TowerRowCount];
        Text[] towerGaps = new Text[TowerRowCount];
        Text[] towerIntervals = new Text[TowerRowCount];
        // Small breathing dot beside the interval column: lit green while that
        // car has DRS active/available, hidden otherwise - a broadcast-style
        // "DRS detection zone" cue built from RaceManager.DrsStateText, which
        // already exists for any participant, not just the player.
        Image[] towerDrsDots = new Image[TowerRowCount];
        int visibleTowerRows = TowerRowCount;

        // Bottom dash.
        Text speed;
        Text speedUnit;
        Text gearText;
        Image revBar;
        HudPill drsPill;
        HudPill ersPill;
        HudPill fuelPill;

        // Bottom-left timing card.
        Text lapRowValue;
        Text lastRowValue;
        Text bestRowValue;
        Text sectorRow;
        Text gapRow;

        // Right stack cards.
        RectTransform rightStack;
        Image tyreFl, tyreFr, tyreRl, tyreRr;
        Text tyreCompoundValue;
        Text tyreTempValue;
        Image tyreWearFill;
        Text tyreWearValue;
        Image ersFill;
        Text ersValue;
        Image fuelFill;
        Text fuelValue;
        Image damageFill;
        Text damageValue;
        // Accent bar of the merged tyre + car-systems card, flashed briefly
        // white on a sudden damage jump (a collision) so the spike reads as an
        // event distinct from the steady critical-damage flicker below.
        Image carStatusAccent;
        float damageSpikeFlashTimer;
        float previousDamagePercent = -1f;
        Text pitStatusValue;
        Text pitPlanValue;
        Image pitFill;
        Text pitFillValue;
        // Radio message stacking fix: up to MaxRadioCards independently
        // active, independently fading compact cards (newest at index 0)
        // instead of one shared card/text pair a single message occupied at
        // a time - see RaceManager.activeEngineerMessages and BuildRadioStack/
        // UpdateRadioStack below.
        const int MaxRadioCards = 4;
        RectTransform radioStackContainer;
        RectTransform[] radioCardSlots = new RectTransform[MaxRadioCards];
        CanvasGroup[] radioCardGroups = new CanvasGroup[MaxRadioCards];
        Text[] radioCardTexts = new Text[MaxRadioCards];
        Image[] radioCardAccents = new Image[MaxRadioCards];
        Vector2[] radioCardRestPositions = new Vector2[MaxRadioCards];
        GameObject qualifyingCard;
        Text qualifyingDeltaValue;
        Text tyreTagText;
        GameObject scWindowCard;
        Text scWindowText;

        // Race control status banner (yellow flag / VSC / safety car / restart),
        // pinned near the top session strip. Hidden entirely under green-flag
        // racing so it never clutters the normal case. A two-line banner (state +
        // what it means right now) rather than a single-line pill, so SC/VSC/
        // yellow states read as a designed callout.
        RectTransform raceControlBanner;
        Image raceControlBannerAccent;
        Image raceControlBannerDot;
        Text raceControlBannerTitle;
        Text raceControlBannerSubtitle;
        bool raceControlBannerWasVisible;
        RaceManager.RaceControlState previousRaceControlState;
        bool previousRaceControlStateCaptured;
        // Safety car restart fix: race control now holds autopilot a little
        // past the Green flip itself (a short ramp so the field accelerates
        // away together instead of snapping to full control on the same
        // frame - see RaceManager.IsRaceControlAutopilotHoldPeriod). The
        // banner and "control returned" flash both key off this instead of
        // the raw state so they never tell the player they have the car back
        // before they actually do.
        bool previousPlayerAutopilot;
        bool previousPlayerAutopilotCaptured;
        // Brief white pulse on the banner's accent bar whenever its headline
        // actually changes (e.g. yellow flag escalating to a full safety car)
        // while it stays on screen - UiFadeIn already covers the first
        // appearance, this covers an in-place escalation reading as an event.
        string previousBannerTitle = "";
        float bannerChangeFlashTimer;
        // Pace-limiter compliance readout, directly beneath the banner - only
        // ever visible alongside it, since compliance is meaningless outside a
        // pace-limited period.
        HudPill paceCompliancePill;

        // Center overlays.
        Text drsFlash;
        Text qualifyingFeedback;
        GameObject qualifyingFeedbackPanel;
        GameObject startLightPanel;
        Image[] startLightImages = new Image[5];
        // Per-light scale punch the instant each light comes on, decaying back
        // to rest - turns "lights out" from a flat color swap into a sequence
        // with a bit of mechanical weight, like the real gantry.
        float[] startLightPunch = new float[5];
        int previousLitLightCount;
        // Shared "big moment" flash text - reused for lights-out, the SC/VSC
        // restart green flag, final lap, and the chequered finish moment instead
        // of a dedicated Text/timer pair per moment.
        UiBigFlash bigFlash;
        bool lightsWereVisible;
        string previousDrsState = "";
        float drsFlashTimer;
        float slowUpdateTimer;
        Text hint;

        // Chequered finish flourish: a brief checkered banner plus position
        // callout the moment the player's own car crosses the line.
        GameObject finishFlourishPanel;
        CanvasGroup finishFlourishGroup;
        Text finishFlourishTitle;
        Text finishFlourishSubtitle;
        float finishFlourishTimer;
        const float FinishFlourishDuration = 4.5f;
        bool watchedPlayerFinished;

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
        int watchedTotalLockups;

        // Track progress strip (minimap-lite): start/finish marker plus live dots.
        RectTransform progressStrip;
        Image playerProgressDot;
        readonly List<Image> aiProgressDots = new List<Image>();
        const float ProgressStripWidth = 460f;

        // Real track-shape minimap built from Track.centerLine projected to UI space.
        RectTransform trackMap;
        Image mapPlayerDot;
        readonly List<Image> mapCarDots = new List<Image>();
        readonly List<RaceParticipant> mapCarOwners = new List<RaceParticipant>();
        Vector3 mapWorldCenter;
        float mapWorldScale;
        const float TrackMapSize = 196f;

        // Driver input telemetry bars beside the dash.
        Image throttleBar;
        Image brakeBar;
        Image ersInputBar;

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

            GameSettingsData settings = race != null && race.Settings != null ? race.Settings.Current : null;
            hudScale = settings != null ? Mathf.Clamp(settings.hudScale, 0.75f, 1.3f) : 1f;
            compact = settings != null && settings.compactHud;
            visibleTowerRows = compact ? 10 : TowerRowCount;

            BuildTopBand();
            BuildRaceControlBanner();
            BuildProgressStrip();
            BuildTrackMap();
            BuildNotificationPanel();
            BuildTimingTower();
            BuildBottomDash();
            BuildInputTelemetry();
            BuildTimingCard();
            BuildRightStack();
            BuildCenterOverlays();
            BuildFinishFlourish();
            BuildHintBar();
            BuildDebugOverlay();
            ResetWatchers();
        }

        void ApplyPanelScale(RectTransform panel)
        {
            panel.localScale = new Vector3(hudScale, hudScale, 1f);
        }

        // ---------- construction ----------

        // Distinct segments instead of one long pipe-joined string: a session
        // pill, event name, position badge, lap counter, and a message segment
        // that takes the remaining width. Thin vertical rules separate them.
        void BuildTopBand()
        {
            RectTransform band = UiFactory.CreateResponsivePanel(transform, "HUD top band", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(980f, 52f), new Vector2(0f, -8f), UiFactory.PanelDarker);
            ApplyPanelScale(band);
            topAccent = UiFactory.CreateBand(band, "HUD top rule", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f), UiFactory.Accent).GetComponent<Image>();

            sessionLabelText = CreateTopBandSegment(band, "Session segment", 0f, 0.13f, UiFactory.Accent, TextAnchor.MiddleLeft, 15, true);
            CreateTopBandDivider(band, 0.13f);
            eventNameText = CreateTopBandSegment(band, "Event segment", 0.14f, 0.4f, UiFactory.TextPrimary, TextAnchor.MiddleLeft, 16, false);
            CreateTopBandDivider(band, 0.4f);
            positionBadgeText = CreateTopBandSegment(band, "Position segment", 0.41f, 0.51f, UiFactory.AccentCyan, TextAnchor.MiddleCenter, 17, true);
            CreateTopBandDivider(band, 0.51f);
            lapCounterText = CreateTopBandSegment(band, "Lap segment", 0.52f, 0.63f, UiFactory.TextPrimary, TextAnchor.MiddleCenter, 17, false);
            CreateTopBandDivider(band, 0.63f);
            sessionMessageText = CreateTopBandSegment(band, "Message segment", 0.64f, 1f, new Color(0.85f, 0.9f, 0.94f), TextAnchor.MiddleLeft, 16, false);
        }

        Text CreateTopBandSegment(RectTransform band, string name, float anchorX0, float anchorX1, Color color, TextAnchor alignment, int size, bool bold)
        {
            Text text = UiFactory.CreateText(band, name, "", size, color, alignment);
            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorX0, 0f);
            rect.anchorMax = new Vector2(anchorX1, 1f);
            rect.offsetMin = new Vector2(14f, 2f);
            rect.offsetMax = new Vector2(-10f, -2f);
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        void CreateTopBandDivider(RectTransform band, float anchorX)
        {
            RectTransform divider = UiFactory.CreateBand(band, "Top band divider", new Vector2(anchorX, 0.22f), new Vector2(anchorX, 0.78f), new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f), new Color(1f, 1f, 1f, 0.1f));
            divider.sizeDelta = new Vector2(1.5f, 0f);
        }

        // Two-line status banner for non-green race control states (yellow flag,
        // VSC, safety car, restart), pinned top-left where it can't collide with
        // the top band, progress strip, or notification panel. Starts hidden and
        // only appears while a state is actually active.
        void BuildRaceControlBanner()
        {
            raceControlBanner = UiFactory.CreateStatusBanner(transform, "Race Control", 328f, 68f, out raceControlBannerAccent, out raceControlBannerDot, out raceControlBannerTitle, out raceControlBannerSubtitle);
            raceControlBanner.anchorMin = new Vector2(0f, 1f);
            raceControlBanner.anchorMax = new Vector2(0f, 1f);
            raceControlBanner.pivot = new Vector2(0f, 1f);
            raceControlBanner.anchoredPosition = new Vector2(16f, -10f);
            ApplyPanelScale(raceControlBanner);
            raceControlBanner.gameObject.SetActive(false);

            // Smaller companion pill directly under the banner: only ever shown
            // while the banner is, so it never appears as an orphaned "DELTA OK"
            // during a normal green-flag race.
            paceCompliancePill = UiFactory.CreatePill(transform, "Pace Compliance", 220f, 30f);
            paceCompliancePill.root.anchorMin = new Vector2(0f, 1f);
            paceCompliancePill.root.anchorMax = new Vector2(0f, 1f);
            paceCompliancePill.root.pivot = new Vector2(0f, 1f);
            paceCompliancePill.root.anchoredPosition = new Vector2(16f, -82f);
            ApplyPanelScale(paceCompliancePill.root);
            paceCompliancePill.root.gameObject.SetActive(false);
        }

        void BuildProgressStrip()
        {
            progressStrip = UiFactory.CreateResponsivePanel(transform, "Track progress strip", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(ProgressStripWidth + 24f, 16f), new Vector2(0f, -66f), new Color(0.006f, 0.009f, 0.012f, 0.66f));
            ApplyPanelScale(progressStrip);
            UiFactory.CreateBand(progressStrip, "Start finish marker", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(10f, 3f), new Vector2(13f, -3f), Color.white);

            for (int i = 0; i < TowerRowCount - 1; i++)
            {
                Image dot = CreateProgressDot("AI dot " + i, 5f, new Color(0.75f, 0.82f, 0.86f, 0.85f));
                aiProgressDots.Add(dot);
            }

            playerProgressDot = CreateProgressDot("Player dot", 9f, UiFactory.Accent);
            if (compact)
            {
                progressStrip.gameObject.SetActive(false);
            }
        }

        // Top-right minimap: the actual circuit outline drawn from centerLine
        // samples, with live car dots in team colors and the player highlighted.
        void BuildTrackMap()
        {
            if (race.Track == null || race.Track.centerLine.Count < 8)
            {
                return;
            }

            trackMap = UiFactory.CreateResponsivePanel(transform, "Track map", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(TrackMapSize + 20f, TrackMapSize + 20f), new Vector2(-14f, -10f), new Color(0.008f, 0.012f, 0.018f, 0.74f));
            ApplyPanelScale(trackMap);

            // Fit the layout into the panel preserving aspect.
            List<Vector3> line = race.Track.centerLine;
            Vector3 min = line[0];
            Vector3 max = line[0];
            for (int i = 1; i < line.Count; i++)
            {
                min = Vector3.Min(min, line[i]);
                max = Vector3.Max(max, line[i]);
            }

            mapWorldCenter = (min + max) * 0.5f;
            float span = Mathf.Max(1f, Mathf.Max(max.x - min.x, max.z - min.z));
            mapWorldScale = (TrackMapSize - 26f) / span;

            // Track ribbon as dense dots; cheap, readable, and shape-accurate.
            // Tinted by sector (S1/S2/S3) using the same normalized-progress
            // thirds RaceManager/LapTracker already use for TrackProgress.sector,
            // so the minimap reads like a broadcast track map instead of a flat
            // gray outline - no new track data needed, just the same split.
            int step = Mathf.Max(1, line.Count / 110);
            for (int i = 0; i < line.Count; i += step)
            {
                float normalized = i / (float)line.Count;
                Color sectorTint = normalized < 0.333f ? UiFactory.AccentCyan
                    : normalized < 0.666f ? UiFactory.AccentAmber
                    : UiFactory.AccentPurple;
                Color ribbonColor = new Color(sectorTint.r, sectorTint.g, sectorTint.b, 0.55f);
                Image dot = CreateMapDot("Map track dot", 3.4f, ribbonColor);
                dot.rectTransform.anchoredPosition = WorldToMap(line[i]);
                dot.raycastTarget = false;
            }

            // Start/finish marker.
            Image startDot = CreateMapDot("Map start marker", 7f, Color.white);
            startDot.rectTransform.anchoredPosition = WorldToMap(line[0]);

            // One dot per live car, tinted by team.
            for (int i = 0; i < race.Participants.Count; i++)
            {
                RaceParticipant entry = race.Participants[i];
                if (entry == null)
                {
                    continue;
                }

                if (entry == player)
                {
                    continue;
                }

                Color teamColor = entry.teamData != null ? entry.teamData.PrimaryUnityColor : new Color(0.8f, 0.85f, 0.9f);
                Image carDot = CreateMapDot("Map car " + i, 6f, teamColor);
                mapCarDots.Add(carDot);
                mapCarOwners.Add(entry);
            }

            mapPlayerDot = CreateMapDot("Map player dot", 9.5f, UiFactory.Accent);
            if (UiFactory.AnimationsEnabled)
            {
                // A subtle breathing highlight so the player's own car is never
                // ambiguous with an AI dot at a glance, even on a busy map.
                UiPulse playerPulse = mapPlayerDot.gameObject.AddComponent<UiPulse>();
                playerPulse.speed = 2.4f;
                playerPulse.minAlpha = 0.6f;
                playerPulse.maxAlpha = 1f;
            }

            if (compact)
            {
                trackMap.gameObject.SetActive(false);
            }
        }

        Image CreateMapDot(string name, float size, Color color)
        {
            RectTransform dot = UiFactory.CreateRect(trackMap, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dot.sizeDelta = new Vector2(size, size);
            Image image = dot.gameObject.AddComponent<Image>();
            image.sprite = UiFactory.GlowSprite;
            image.color = color;
            return image;
        }

        Vector2 WorldToMap(Vector3 world)
        {
            return new Vector2((world.x - mapWorldCenter.x) * mapWorldScale, (world.z - mapWorldCenter.z) * mapWorldScale);
        }

        void UpdateTrackMap()
        {
            if (trackMap == null || !trackMap.gameObject.activeSelf)
            {
                return;
            }

            if (mapPlayerDot != null && player != null)
            {
                mapPlayerDot.rectTransform.anchoredPosition = WorldToMap(player.transform.position);
            }

            for (int i = 0; i < mapCarDots.Count; i++)
            {
                RaceParticipant entry = mapCarOwners[i];
                Image dot = mapCarDots[i];
                if (entry == null || !entry.gameObject.activeSelf || entry.retired)
                {
                    if (dot.gameObject.activeSelf)
                    {
                        dot.gameObject.SetActive(false);
                    }

                    continue;
                }

                dot.rectTransform.anchoredPosition = WorldToMap(entry.transform.position);
            }
        }

        // Throttle/brake/ERS bars beside the dash: instant read of what the car is
        // being asked to do, like a broadcast telemetry insert.
        void BuildInputTelemetry()
        {
            RectTransform panel = UiFactory.CreateResponsivePanel(transform, "Input telemetry", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(96f, 138f), new Vector2(-292f, 12f), new Color(0.008f, 0.012f, 0.018f, 0.8f));
            ApplyPanelScale(panel);
            Text throttleLabel;
            Text brakeLabel;
            Text ersLabel;
            throttleBar = UiFactory.CreateVerticalBar(panel, "T", 16f, 92f, UiFactory.AccentGreen, out throttleLabel);
            RectTransform throttleRect = throttleBar.rectTransform.parent.GetComponent<RectTransform>();
            throttleRect.anchorMin = new Vector2(0f, 0.5f);
            throttleRect.anchorMax = new Vector2(0f, 0.5f);
            throttleRect.anchoredPosition = new Vector2(22f, 10f);

            brakeBar = UiFactory.CreateVerticalBar(panel, "B", 16f, 92f, UiFactory.Accent, out brakeLabel);
            RectTransform brakeRect = brakeBar.rectTransform.parent.GetComponent<RectTransform>();
            brakeRect.anchorMin = new Vector2(0f, 0.5f);
            brakeRect.anchorMax = new Vector2(0f, 0.5f);
            brakeRect.anchoredPosition = new Vector2(48f, 10f);

            ersInputBar = UiFactory.CreateVerticalBar(panel, "E", 16f, 92f, UiFactory.AccentCyan, out ersLabel);
            RectTransform ersRect = ersInputBar.rectTransform.parent.GetComponent<RectTransform>();
            ersRect.anchorMin = new Vector2(0f, 0.5f);
            ersRect.anchorMax = new Vector2(0f, 0.5f);
            ersRect.anchoredPosition = new Vector2(74f, 10f);

            if (compact)
            {
                panel.gameObject.SetActive(false);
                throttleBar = null;
                brakeBar = null;
                ersInputBar = null;
            }
        }

        void BuildNotificationPanel()
        {
            RectTransform band = UiFactory.CreateResponsivePanel(transform, "HUD notification", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(470f, 48f), new Vector2(0f, -92f), new Color(0.006f, 0.009f, 0.012f, 0.88f));
            ApplyPanelScale(band);
            notificationAccent = UiFactory.CreateBand(band, "Notification accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f), UiFactory.Accent).GetComponent<Image>();
            notificationText = UiFactory.CreateText(band, "Notification text", "", 18, UiFactory.TextPrimary, TextAnchor.MiddleCenter);
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

        void BuildTimingTower()
        {
            float height = 44f + visibleTowerRows * 21f + 10f;
            RectTransform towerBand = UiFactory.CreateResponsivePanel(transform, "Timing tower", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(324f, height), new Vector2(16f, 40f), new Color(0.006f, 0.009f, 0.012f, 0.72f));
            ApplyPanelScale(towerBand);
            UiFactory.CreateBand(towerBand, "Tower accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.Accent);
            tower = UiFactory.CreateText(towerBand, "Tower header", "", 12, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            RectTransform towerRect = tower.GetComponent<RectTransform>();
            towerRect.anchorMin = new Vector2(0f, 1f);
            towerRect.anchorMax = new Vector2(1f, 1f);
            towerRect.offsetMin = new Vector2(16f, -34f);
            towerRect.offsetMax = new Vector2(-10f, -10f);
            for (int i = 0; i < TowerRowCount; i++)
            {
                CreateTowerRow(towerBand, i);
            }
        }

        void BuildBottomDash()
        {
            RectTransform dash = UiFactory.CreateResponsivePanel(transform, "Speed dash", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(470f, 138f), new Vector2(0f, 12f), new Color(0.006f, 0.009f, 0.012f, 0.84f));
            ApplyPanelScale(dash);
            UiFactory.CreateBand(dash, "Dash accent", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f), UiFactory.Accent);

            RectTransform revTrack = UiFactory.CreateBand(dash, "Rev track", new Vector2(0.04f, 1f), new Vector2(0.96f, 1f), new Vector2(0f, -18f), new Vector2(0f, -8f), UiFactory.MeterTrack);
            RectTransform revFill = UiFactory.CreateBand(revTrack, "Rev fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.white);
            revBar = revFill.GetComponent<Image>();

            speed = UiFactory.CreateText(dash, "Speed", "0", 52, Color.white, TextAnchor.MiddleRight);
            RectTransform speedRect = speed.GetComponent<RectTransform>();
            speedRect.anchorMin = new Vector2(0f, 0.5f);
            speedRect.anchorMax = new Vector2(0.42f, 0.5f);
            speedRect.offsetMin = new Vector2(16f, -32f);
            speedRect.offsetMax = new Vector2(0f, 34f);

            speedUnit = UiFactory.CreateText(dash, "Speed unit", "KM/H", 13, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            RectTransform unitRect = speedUnit.GetComponent<RectTransform>();
            unitRect.anchorMin = new Vector2(0.44f, 0.5f);
            unitRect.anchorMax = new Vector2(0.6f, 0.5f);
            unitRect.offsetMin = new Vector2(0f, -30f);
            unitRect.offsetMax = new Vector2(0f, 0f);

            gearText = UiFactory.CreateText(dash, "Gear", "1", 56, UiFactory.Accent, TextAnchor.MiddleCenter);
            RectTransform gearRect = gearText.GetComponent<RectTransform>();
            gearRect.anchorMin = new Vector2(0.44f, 0.5f);
            gearRect.anchorMax = new Vector2(0.6f, 0.5f);
            gearRect.offsetMin = new Vector2(0f, -12f);
            gearRect.offsetMax = new Vector2(0f, 40f);

            Text gearLabel = UiFactory.CreateText(dash, "Gear label", "GEAR", 11, UiFactory.TextMuted, TextAnchor.MiddleCenter);
            RectTransform gearLabelRect = gearLabel.GetComponent<RectTransform>();
            gearLabelRect.anchorMin = new Vector2(0.44f, 0.5f);
            gearLabelRect.anchorMax = new Vector2(0.6f, 0.5f);
            gearLabelRect.offsetMin = new Vector2(0f, -34f);
            gearLabelRect.offsetMax = new Vector2(0f, -14f);

            // State pills stacked on the right of the dash.
            drsPill = UiFactory.CreatePill(dash, "DRS", 132f, 26f);
            drsPill.root.anchorMin = new Vector2(1f, 1f);
            drsPill.root.anchorMax = new Vector2(1f, 1f);
            drsPill.root.pivot = new Vector2(1f, 1f);
            drsPill.root.anchoredPosition = new Vector2(-14f, -26f);

            ersPill = UiFactory.CreatePill(dash, "ERS", 132f, 26f);
            ersPill.root.anchorMin = new Vector2(1f, 1f);
            ersPill.root.anchorMax = new Vector2(1f, 1f);
            ersPill.root.pivot = new Vector2(1f, 1f);
            ersPill.root.anchoredPosition = new Vector2(-14f, -58f);

            fuelPill = UiFactory.CreatePill(dash, "FUEL", 132f, 26f);
            fuelPill.root.anchorMin = new Vector2(1f, 1f);
            fuelPill.root.anchorMax = new Vector2(1f, 1f);
            fuelPill.root.pivot = new Vector2(1f, 1f);
            fuelPill.root.anchoredPosition = new Vector2(-14f, -90f);
        }

        void BuildTimingCard()
        {
            RectTransform card = UiFactory.CreateResponsivePanel(transform, "Timing card", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(324f, 168f), new Vector2(16f, 12f), UiFactory.HudCardBackground);
            ApplyPanelScale(card);
            UiFactory.CreateBand(card, "Timing accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.AccentCyan);
            Text title = UiFactory.CreateText(card, "Timing title", "TIMING", 12, UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(14f, -24f);
            titleRect.offsetMax = new Vector2(-10f, -6f);

            UiFactory.CreateHudLabelValueRow(card, "Lap", 28f, out lapRowValue);
            UiFactory.CreateHudLabelValueRow(card, "Last", 52f, out lastRowValue);
            UiFactory.CreateHudLabelValueRow(card, "Best", 76f, out bestRowValue);

            sectorRow = UiFactory.CreateText(card, "Sectors", "", 13, UiFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform sectorRect = sectorRow.GetComponent<RectTransform>();
            sectorRect.anchorMin = new Vector2(0f, 1f);
            sectorRect.anchorMax = new Vector2(1f, 1f);
            sectorRect.offsetMin = new Vector2(14f, -128f);
            sectorRect.offsetMax = new Vector2(-10f, -104f);

            gapRow = UiFactory.CreateText(card, "Gaps", "", 13, UiFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform gapRect = gapRow.GetComponent<RectTransform>();
            gapRect.anchorMin = new Vector2(0f, 1f);
            gapRect.anchorMax = new Vector2(1f, 1f);
            gapRect.offsetMin = new Vector2(14f, -156f);
            gapRect.offsetMax = new Vector2(-10f, -132f);
        }

        void BuildRightStack()
        {
            rightStack = UiFactory.CreateResponsivePanel(transform, "Right card stack", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(RightStackWidth, 640f), new Vector2(-16f, 0f), new Color(0f, 0f, 0f, 0f));
            ApplyPanelScale(rightStack);
            VerticalLayoutGroup layout = rightStack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UiFactory.HudCardSpacing;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            if (!compact)
            {
                BuildCarStatusCard();
            }

            BuildPitCard();
            BuildScWindowCard();
            BuildQualifyingCard();
            BuildRadioStack();
        }

        // Merged tyre + car-systems card. These used to be two separate panels
        // but were always built and hidden together (both skip entirely in
        // compact HUD), so one header/accent replaces two and a thin divider
        // separates the tyre block from ERS/fuel/damage below it - a tighter
        // right stack with the exact same readouts, just grouped by what they
        // actually are (tyres vs. everything else about the car).
        void BuildCarStatusCard()
        {
            RectTransform card = UiFactory.CreateHudCard(rightStack, "Car Status", RightStackWidth, 236f, UiFactory.AccentCyan, out carStatusAccent);

            // 2x2 tyre corner grid on the left half of the card.
            tyreFl = CreateTyreCorner(card, "FL", new Vector2(24f, -36f));
            tyreFr = CreateTyreCorner(card, "FR", new Vector2(74f, -36f));
            tyreRl = CreateTyreCorner(card, "RL", new Vector2(24f, -80f));
            tyreRr = CreateTyreCorner(card, "RR", new Vector2(74f, -80f));

            UiFactory.CreateHudLabelValueRow(card, "Compound", 30f, out tyreCompoundValue);
            MoveRowToRightHalf(tyreCompoundValue, "Compound row label", card);
            UiFactory.CreateHudLabelValueRow(card, "Temp", 56f, out tyreTempValue);
            MoveRowToRightHalf(tyreTempValue, "Temp row label", card);

            // Lockup / flat spot tags: blank most of the time, so it reads as
            // empty space rather than a widget until there's something to say.
            tyreTagText = UiFactory.CreateText(card, "Tyre condition tags", "", 12, UiFactory.AccentAmber, TextAnchor.MiddleLeft);
            RectTransform tagRect = tyreTagText.GetComponent<RectTransform>();
            tagRect.anchorMin = new Vector2(0f, 1f);
            tagRect.anchorMax = new Vector2(1f, 1f);
            tagRect.offsetMin = new Vector2(14f, -98f);
            tagRect.offsetMax = new Vector2(-10f, -80f);

            tyreWearFill = UiFactory.CreateHudMeter(card, "Wear", 116f, UiFactory.AccentAmber, out tyreWearValue);

            UiFactory.CreateBand(card, "Car status divider", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -142f), new Vector2(-10f, -140f), new Color(1f, 1f, 1f, 0.08f));

            ersFill = UiFactory.CreateHudMeter(card, "ERS", 152f, UiFactory.AccentCyan, out ersValue);
            fuelFill = UiFactory.CreateHudMeter(card, "Fuel", 178f, new Color(0.7f, 0.95f, 1f), out fuelValue);
            damageFill = UiFactory.CreateHudMeter(card, "Dmg", 204f, UiFactory.Accent, out damageValue);
        }

        // The tyre card shares its left half with the corner grid, so shift the
        // generated label/value rows into the right half.
        void MoveRowToRightHalf(Text valueText, string labelName, RectTransform card)
        {
            Transform label = card.Find(labelName);
            if (label != null)
            {
                RectTransform labelRect = label.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0.38f, labelRect.anchorMin.y);
                labelRect.anchorMax = new Vector2(0.66f, labelRect.anchorMax.y);
                labelRect.offsetMin = new Vector2(4f, labelRect.offsetMin.y);
            }

            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.62f, valueRect.anchorMin.y);
        }

        Image CreateTyreCorner(RectTransform card, string cornerName, Vector2 position)
        {
            RectTransform corner = UiFactory.CreateRect(card, "Tyre " + cornerName, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            corner.sizeDelta = new Vector2(22f, 36f);
            corner.pivot = new Vector2(0f, 1f);
            corner.anchoredPosition = position;
            Image image = corner.gameObject.AddComponent<Image>();
            image.color = UiFactory.AccentGreen;

            Text label = UiFactory.CreateText(corner, cornerName + " label", cornerName, 10, UiFactory.TextMuted, TextAnchor.MiddleCenter);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.offsetMin = new Vector2(-6f, -14f);
            labelRect.offsetMax = new Vector2(6f, 0f);
            return image;
        }

        void BuildPitCard()
        {
            RectTransform card = UiFactory.CreateHudCard(rightStack, "Pit", RightStackWidth, 100f, UiFactory.AccentAmber);
            UiFactory.CreateHudLabelValueRow(card, "Status", 28f, out pitStatusValue);
            UiFactory.CreateHudLabelValueRow(card, "Plan", 50f, out pitPlanValue);
            pitFill = UiFactory.CreateHudMeter(card, "Stop", 76f, UiFactory.AccentAmber, out pitFillValue);
        }

        // Non-intrusive box-now suggestion during a safety car / VSC period.
        // Built exactly like the radio card (title + free text) and, like the
        // qualifying card, starts inactive so it contributes nothing to the
        // right stack's vertical layout until it actually has something to say.
        void BuildScWindowCard()
        {
            RectTransform card = UiFactory.CreateHudCard(rightStack, "SC Window", RightStackWidth, 72f, UiFactory.AccentAmber);
            scWindowCard = card.gameObject;
            scWindowText = UiFactory.CreateText(card, "SC window message", "", 14, UiFactory.AccentAmber, TextAnchor.UpperLeft);
            RectTransform scRect = scWindowText.GetComponent<RectTransform>();
            scRect.anchorMin = Vector2.zero;
            scRect.anchorMax = Vector2.one;
            scRect.offsetMin = new Vector2(14f, 6f);
            scRect.offsetMax = new Vector2(-10f, -26f);
            scWindowCard.SetActive(false);
        }

        void BuildQualifyingCard()
        {
            RectTransform card = UiFactory.CreateHudCard(rightStack, "Qualifying", RightStackWidth, 56f, UiFactory.AccentPurple);
            qualifyingCard = card.gameObject;
            UiFactory.CreateHudLabelValueRow(card, "Delta", 28f, out qualifyingDeltaValue);
            qualifyingCard.SetActive(false);
        }

        // Radio card sizing (2-line clipping + stacking fix): CreateHudCard's
        // fixed height combined with CreateText's default
        // VerticalWrapMode.Truncate meant any engineer message longer than
        // what fit in ~72px (about two lines) was silently cut off with no
        // visual sign anything was missing - and only one message could ever
        // be on screen at a time regardless. Each stacked card now grows to
        // fit its own message (see UpdateRadioStack), bounded so a short
        // message still reads as a compact pill and a pathological one can't
        // run away with the whole HUD stack; up to MaxRadioCards of them can
        // be visible together, newest closest to the top.
        const float RadioCardMinHeight = 54f;
        const float RadioCardMaxHeight = 140f;
        const int RadioCardSpacing = 6;

        // Deliberately not built on CreateHudCard - that always reserves a
        // ~24px header strip for a repeated title label, which would mean
        // "RADIO" printed on every single stacked card. One small accent bar
        // plus a priority dot is enough to read as "this is the radio" once
        // several are stacked together; the message text gets the room back.
        void BuildRadioStack()
        {
            radioStackContainer = UiFactory.CreateRect(rightStack, "Radio stack", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            radioStackContainer.sizeDelta = new Vector2(RightStackWidth, RadioCardMinHeight);
            VerticalLayoutGroup stackLayout = UiFactory.AddVerticalLayout(radioStackContainer, RadioCardSpacing, new RectOffset(0, 0, 0, 0));
            stackLayout.childAlignment = TextAnchor.UpperRight;

            for (int i = 0; i < MaxRadioCards; i++)
            {
                RectTransform card = UiFactory.CreateRect(radioStackContainer, "Radio card " + i, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                card.sizeDelta = new Vector2(RightStackWidth, RadioCardMinHeight);
                Image background = card.gameObject.AddComponent<Image>();
                UiFactory.StyleRounded(background, UiFactory.HudCardBackground);
                radioCardSlots[i] = card;

                RectTransform accent = UiFactory.CreateRect(card, "Radio accent " + i, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 8f), new Vector2(3f, -8f));
                Image accentImage = accent.gameObject.AddComponent<Image>();
                UiFactory.StyleRoundedSmall(accentImage, UiFactory.AccentGreen);
                radioCardAccents[i] = accentImage;

                CanvasGroup group = card.gameObject.AddComponent<CanvasGroup>();
                radioCardGroups[i] = group;

                Text text = UiFactory.CreateText(card, "Radio text " + i, "", 13, new Color(0.86f, 0.95f, 1f), TextAnchor.UpperLeft);
                // Overflow, not the CreateText default of Truncate - the card
                // is sized to fit the text (below), but Overflow is kept as a
                // second safety net so even a message that somehow exceeds
                // RadioCardMaxHeight still reads in full instead of clipping.
                text.verticalOverflow = VerticalWrapMode.Overflow;
                RectTransform textRect = text.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(14f, 6f);
                textRect.offsetMax = new Vector2(-10f, -6f);
                radioCardTexts[i] = text;
                card.gameObject.SetActive(false);
            }

            radioStackContainer.gameObject.SetActive(false);
        }

        // Greedy word-wrap simulation matching Unity's own Text wrapping
        // closely enough to size the card correctly, without depending on
        // TextGenerator/ContentSizeFitter APIs whose exact behavior varies
        // with canvas scaling. Slightly over-estimating line count is the
        // safe direction (a touch of extra empty space) - under-estimating is
        // the one that would reintroduce clipping, which Overflow above
        // already guards against regardless.
        static int EstimateWrappedLineCount(string text, float approxCharsPerLine)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 1;
            }

            string[] words = text.Split(' ');
            int lines = 1;
            float lineLength = 0f;
            for (int i = 0; i < words.Length; i++)
            {
                float wordLength = words[i].Length + 1;
                if (lineLength > 0f && lineLength + wordLength > approxCharsPerLine)
                {
                    lines++;
                    lineLength = 0f;
                }

                lineLength += wordLength;
            }

            return lines;
        }

        void BuildCenterOverlays()
        {
            RectTransform drsBand = UiFactory.CreateResponsivePanel(transform, "DRS cue", new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.5f), new Vector2(300f, 52f), Vector2.zero, new Color(0f, 0f, 0f, 0f));
            drsFlash = UiFactory.CreateText(drsBand, "DRS cue text", "", 22, new Color(0.7f, 1f, 0.76f), TextAnchor.MiddleCenter);
            RectTransform drsRect = drsFlash.GetComponent<RectTransform>();
            drsRect.anchorMin = Vector2.zero;
            drsRect.anchorMax = Vector2.one;
            drsRect.offsetMin = Vector2.zero;
            drsRect.offsetMax = Vector2.zero;

            RectTransform startBand = UiFactory.CreateResponsivePanel(transform, "Race start lights", new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.5f), new Vector2(448f, 88f), Vector2.zero, new Color(0.004f, 0.005f, 0.006f, 0.92f));
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

            bigFlash = UiFactory.CreateBigFlashText(transform, "Big moment flash", new Vector2(0.5f, 0.8f), 52);

            RectTransform feedbackBand = UiFactory.CreateResponsivePanel(transform, "Qualifying feedback", new Vector2(0.5f, 0.54f), new Vector2(0.5f, 0.5f), new Vector2(640f, 116f), Vector2.zero, new Color(0.006f, 0.009f, 0.012f, 0.82f));
            qualifyingFeedbackPanel = feedbackBand.gameObject;
            qualifyingFeedback = UiFactory.CreateText(feedbackBand, "Qualifying feedback text", "", 26, UiFactory.TextPrimary, TextAnchor.MiddleCenter);
            RectTransform feedbackRect = qualifyingFeedback.GetComponent<RectTransform>();
            feedbackRect.anchorMin = Vector2.zero;
            feedbackRect.anchorMax = Vector2.one;
            feedbackRect.offsetMin = new Vector2(20f, 12f);
            feedbackRect.offsetMax = new Vector2(-20f, -12f);
            qualifyingFeedbackPanel.SetActive(false);
        }

        // Chequered-flag finish flourish: checkered top/bottom bands framing a
        // "RACE FINISHED" title and a live position line, shown briefly the
        // moment the player's own car crosses the line. Faded manually (not via
        // UiFadeIn) since it needs to fade back out on a timer, not just in once.
        void BuildFinishFlourish()
        {
            RectTransform panel = UiFactory.CreateResponsivePanel(transform, "Finish flourish", new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.5f), new Vector2(620f, 148f), Vector2.zero, new Color(0.004f, 0.005f, 0.006f, 0.92f));
            finishFlourishPanel = panel.gameObject;
            finishFlourishGroup = panel.gameObject.AddComponent<CanvasGroup>();
            finishFlourishGroup.blocksRaycasts = false;
            finishFlourishGroup.interactable = false;

            UiFactory.CreateCheckeredBand(panel, "Finish flourish top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -14f), Vector2.zero);
            UiFactory.CreateCheckeredBand(panel, "Finish flourish bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 14f));

            finishFlourishTitle = UiFactory.CreateText(panel, "Finish flourish title", "RACE FINISHED", 40, Color.white, TextAnchor.MiddleCenter);
            finishFlourishTitle.fontStyle = FontStyle.Bold;
            RectTransform titleRect = finishFlourishTitle.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.44f);
            titleRect.anchorMax = new Vector2(1f, 0.86f);
            titleRect.offsetMin = new Vector2(20f, 0f);
            titleRect.offsetMax = new Vector2(-20f, 0f);

            finishFlourishSubtitle = UiFactory.CreateText(panel, "Finish flourish subtitle", "", 20, UiFactory.AccentGreen, TextAnchor.MiddleCenter);
            RectTransform subtitleRect = finishFlourishSubtitle.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 0.16f);
            subtitleRect.anchorMax = new Vector2(1f, 0.44f);
            subtitleRect.offsetMin = new Vector2(20f, 0f);
            subtitleRect.offsetMax = new Vector2(-20f, 0f);

            finishFlourishGroup.alpha = 0f;
            finishFlourishPanel.SetActive(false);
        }

        void BuildHintBar()
        {
            RectTransform hintBand = UiFactory.CreateResponsivePanel(transform, "HUD hint bar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(620f, 24f), new Vector2(0f, 154f), new Color(0.006f, 0.009f, 0.012f, 0.4f));
            ApplyPanelScale(hintBand);
            string hintText = race != null && race.IsTimeTrial
                ? "Esc pause   Shift ERS deploy   R mode (hold: reset car)   C camera   F1 debug   F2 next track"
                : "Esc pause   Space DRS   Shift ERS deploy   R mode (hold: reset car)   C camera   P pit   F1 debug";
            hint = UiFactory.CreateText(hintBand, "Hint", hintText, 12, new Color(0.7f, 0.8f, 0.85f, 0.9f), TextAnchor.MiddleCenter);
            RectTransform hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = Vector2.one;
            hintRect.offsetMin = Vector2.zero;
            hintRect.offsetMax = Vector2.zero;
            if (compact)
            {
                hintBand.gameObject.SetActive(false);
            }
        }

        void BuildDebugOverlay()
        {
            RectTransform panel = UiFactory.CreateResponsivePanel(transform, "Debug overlay", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(400f, 320f), new Vector2(16f, -80f), new Color(0f, 0f, 0f, 0.78f));
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

        Image CreateProgressDot(string name, float size, Color color)
        {
            RectTransform dot = UiFactory.CreateBand(progressStrip, name, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, color);
            dot.sizeDelta = new Vector2(size, size);
            dot.anchoredPosition = new Vector2(12f, 0f);
            return dot.GetComponent<Image>();
        }

        // ---------- runtime updates ----------

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
            watchedTotalLockups = 0;
            seenTrackLimitWarnings = 0;
            watchedPlayerFinished = false;
            previousRaceControlStateCaptured = false;
            previousPlayerAutopilotCaptured = false;
            raceControlBannerWasVisible = false;
            previousBannerTitle = "";
            bannerChangeFlashTimer = 0f;
            previousDamagePercent = -1f;
            damageSpikeFlashTimer = 0f;
            previousLitLightCount = 0;
            for (int i = 0; i < startLightPunch.Length; i++)
            {
                startLightPunch[i] = 0f;
            }
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
            UpdateTrackMap();
            UpdateTopAccentFlash();
            UpdateFinishFlourish();
            UpdateDamageSpikeFlash();

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
            UiFactory.SetMeterValue(revBar, revs);
            revBar.color = revs > 0.9f ? UiFactory.Accent : (revs > 0.7f ? UiFactory.AccentAmber : UiFactory.AccentGreen);

            UiFactory.SetVerticalBarValue(throttleBar, car.EffectiveThrottle);
            UiFactory.SetVerticalBarValue(brakeBar, car.EffectiveBrake);
            // The ERS bar swaps color with what the battery is actually doing right
            // now (draining vs regenerating) instead of staying flat cyan regardless
            // of state, so a glance tells you which way the charge is moving.
            Color ersBarColor = car.ErsDeploying ? UiFactory.AccentCyan : (car.ErsHarvesting ? UiFactory.AccentGreen : UiFactory.TextMuted);
            UiFactory.SetVerticalBarValue(ersInputBar, car.ErsBattery, ersBarColor);

            UpdateStatePills(car);
            UpdateRaceControlBanner();

            string feedbackText = race.QualifyingFeedbackText;
            bool showFeedback = !string.IsNullOrEmpty(feedbackText);
            qualifyingFeedbackPanel.SetActive(showFeedback);
            qualifyingFeedback.text = showFeedback ? feedbackText : "";
            drsFlash.text = string.IsNullOrEmpty(feedbackText) && drsFlashTimer > 0f ? "DRS AVAILABLE" : "";

            UpdateRadioStack();
        }

        // Radio message stacking fix: as many active engineer messages as
        // RaceManager currently has (up to MaxRadioCards) render as their own
        // independently sized, independently fading cards, newest at the top,
        // instead of a single card that could only ever show one message at a
        // time while everything else silently waited its turn. Sizing/2-line
        // clipping fix carried over per-card: each one grows to fit however
        // many lines its own message wraps to (min/max bounded) rather than a
        // fixed height.
        void UpdateRadioStack()
        {
            int activeCount = Mathf.Min(race.ActiveEngineerMessageCount, MaxRadioCards);
            bool anyVisible = activeCount > 0;
            if (radioStackContainer.gameObject.activeSelf != anyVisible)
            {
                radioStackContainer.gameObject.SetActive(anyVisible);
            }

            if (!anyVisible)
            {
                return;
            }

            bool animate = race.Settings == null || race.Settings.Current.uiAnimations;
            float textWidth = RightStackWidth - 24f;
            float approxCharsPerLine = Mathf.Max(10f, textWidth / 7.2f);
            float totalHeight = 0f;
            bool layoutDirty = false;

            for (int i = 0; i < MaxRadioCards; i++)
            {
                bool slotActive = i < activeCount;
                if (radioCardSlots[i].gameObject.activeSelf != slotActive)
                {
                    radioCardSlots[i].gameObject.SetActive(slotActive);
                    layoutDirty = true;
                }

                if (!slotActive)
                {
                    continue;
                }

                string rawText = race.GetActiveEngineerMessageText(i);
                string displayText = rawText.StartsWith("ENGINEER: ") ? rawText.Substring(10) : rawText;
                if (radioCardTexts[i].text != displayText)
                {
                    radioCardTexts[i].text = displayText;
                    layoutDirty = true;
                }

                bool priority = race.GetActiveEngineerMessagePriority(i);
                radioCardAccents[i].color = priority ? UiFactory.AccentAmber : UiFactory.AccentGreen;

                int wrappedLines = EstimateWrappedLineCount(displayText, approxCharsPerLine);
                float contentHeight = 18f + wrappedLines * 17f + 12f;
                float cardHeight = Mathf.Clamp(contentHeight, RadioCardMinHeight, RadioCardMaxHeight);
                if (!Mathf.Approximately(radioCardSlots[i].sizeDelta.y, cardHeight))
                {
                    radioCardSlots[i].sizeDelta = new Vector2(RightStackWidth, cardHeight);
                    layoutDirty = true;
                }

                totalHeight += cardHeight + (i > 0 ? RadioCardSpacing : 0f);

                float progress = animate ? race.GetActiveEngineerMessageFade(i) : 1f;
                radioCardGroups[i].alpha = Mathf.Clamp01(progress);
            }

            if (!Mathf.Approximately(radioStackContainer.sizeDelta.y, totalHeight))
            {
                radioStackContainer.sizeDelta = new Vector2(RightStackWidth, totalHeight);
                layoutDirty = true;
            }

            if (layoutDirty)
            {
                // Immediate, not deferred - the slide-in offset read right
                // after depends on each card's real post-layout position for
                // this same frame, exactly like the single-card version did.
                LayoutRebuilder.ForceRebuildLayoutImmediate(radioStackContainer);
                LayoutRebuilder.ForceRebuildLayoutImmediate(rightStack);
                for (int i = 0; i < activeCount; i++)
                {
                    radioCardRestPositions[i] = radioCardSlots[i].anchoredPosition;
                }
            }

            for (int i = 0; i < activeCount; i++)
            {
                float progress = animate ? race.GetActiveEngineerMessageFade(i) : 1f;
                radioCardSlots[i].anchoredPosition = radioCardRestPositions[i] + new Vector2(Mathf.Lerp(24f, 0f, progress), 0f);
            }
        }

        void UpdateStatePills(VehicleController car)
        {
            string drsState = race.DrsStateText(player);
            if (drsState == "AVAILABLE" && previousDrsState != "AVAILABLE")
            {
                SimpleAudioManager.PlayDrsAvailable();
                drsFlashTimer = 1.6f;
            }

            previousDrsState = drsState;
            drsFlashTimer = Mathf.Max(0f, drsFlashTimer - Time.deltaTime);

            if (drsState == "ACTIVE")
            {
                drsPill.SetState("DRS ACTIVE", UiFactory.AccentGreen, true);
            }
            else if (drsState == "AVAILABLE")
            {
                // Flash while freshly available so the eye catches it at speed.
                bool lit = drsFlashTimer <= 0f || Mathf.PingPong(Time.time * 3f, 1f) > 0.5f;
                drsPill.SetState("DRS READY", UiFactory.AccentCyan, lit);
            }
            else
            {
                drsPill.SetState("DRS OFF", UiFactory.TextMuted, false);
            }

            UpdateErsPill(car);

            bool lowFuel = car.FuelKg < 7f;
            if (lowFuel)
            {
                bool lit = Mathf.PingPong(Time.time * 2.4f, 1f) > 0.5f;
                fuelPill.SetState("FUEL LOW", UiFactory.AccentAmber, lit);
            }
            else
            {
                fuelPill.SetState("FUEL " + car.FuelKg.ToString("0") + "KG", UiFactory.TextMuted, false);
            }
        }

        // Reflects RaceManager's race control state machine. Hidden entirely on
        // Green so a normal race never shows anything here; only the handful of
        // caution/safety-car/restart states light the banner up. When the player
        // is under full-SC autopilot the banner deliberately reads calm ("we're
        // driving, sit back") rather than alarming, since nothing is required of
        // the player in that state - contrast with the amber "hold position"
        // wording used for states that still need the player's attention.
        void UpdateRaceControlBanner()
        {
            if (raceControlBanner == null)
            {
                return;
            }

            RaceManager.RaceControlState state = race.CurrentRaceControlState;
            if (!previousRaceControlStateCaptured)
            {
                previousRaceControlState = state;
                previousRaceControlStateCaptured = true;
            }

            bool playerAutopilotNow = race.PlayerParticipant != null && race.PlayerParticipant.isRaceControlAutopilot;
            if (!previousPlayerAutopilotCaptured)
            {
                previousPlayerAutopilot = playerAutopilotNow;
                previousPlayerAutopilotCaptured = true;
            }

            // A satisfying "control returned" moment fires the instant the
            // player's own car actually stops being driven by race control -
            // NOT just when the state flips to Green, since a safety car
            // restart now keeps autopilot engaged a little longer than that
            // (see RaceManager.IsRaceControlAutopilotHoldPeriod) for a
            // controlled ramp rather than an instant snap. A VSC or yellow
            // clearing (which never sets isRaceControlAutopilot at all) still
            // gets the same flourish off the Green transition itself.
            bool controlJustReturned = previousPlayerAutopilot && !playerAutopilotNow;
            bool cautionJustCleared = state == RaceManager.RaceControlState.Green && previousRaceControlState != RaceManager.RaceControlState.Green && !playerAutopilotNow;
            if ((controlJustReturned || cautionJustCleared) && race.CanDrive)
            {
                bigFlash.Show("GREEN FLAG - CONTROL RETURNED", UiFactory.AccentGreen, 1f);
                SimpleAudioManager.PlayDrsAvailable();
            }

            previousRaceControlState = state;
            previousPlayerAutopilot = playerAutopilotNow;

            bool nearLocalYellow = state == RaceManager.RaceControlState.YellowSector && race.PlayerParticipant != null && race.IsNearLocalYellowIncident(race.PlayerParticipant);
            // Stays visible through the green-flag ramp tail too (state is
            // already Green but the player doesn't have the car back yet) -
            // hiding it there would read as "you have control" a beat early.
            bool visible = state != RaceManager.RaceControlState.Green || playerAutopilotNow;
            if (raceControlBanner.gameObject.activeSelf != visible)
            {
                raceControlBanner.gameObject.SetActive(visible);
            }

            bool wasVisibleBefore = raceControlBannerWasVisible;
            if (visible && !raceControlBannerWasVisible && UiFactory.AnimationsEnabled)
            {
                // Reuse the same fade/slide-in used for every panel at HUD build
                // time so the banner arrives with motion instead of popping.
                raceControlBanner.gameObject.AddComponent<UiFadeIn>();
            }

            raceControlBannerWasVisible = visible;

            if (!visible)
            {
                // Still refresh the pace pill on the green transition itself
                // (nearLocalYellow is already false here) so it hides instead of
                // lingering on whatever it last showed during the caution period.
                UpdatePaceCompliancePill(false);
                return;
            }

            switch (state)
            {
                case RaceManager.RaceControlState.Green:
                    // Only reached while visible is true, i.e. the ramp tail
                    // after a safety-car restart: state already flipped to
                    // Green but the player's car is still race-control driven
                    // for a couple more seconds.
                    UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                        "GREEN FLAG - POWER BUILDING", "Race control still has the car - full control in a moment", UiFactory.AccentCyan);
                    break;
                case RaceManager.RaceControlState.YellowSector:
                    // Only reads "slow" for the car actually near the incident -
                    // everyone else still sees the sector is flagged, but without
                    // implying a speed restriction that doesn't apply to them.
                    if (nearLocalYellow)
                    {
                        UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                            "YELLOW FLAG - SLOW", "Incident ahead - ease off, no overtaking here", UiFactory.AccentAmber);
                    }
                    else
                    {
                        UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                            "YELLOW FLAG", "Caution in sector " + race.YellowFlagSector + " - stay ready", UiFactory.AccentAmber);
                    }

                    break;
                case RaceManager.RaceControlState.VirtualSafetyCar:
                    UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                        "VIRTUAL SAFETY CAR", "Hold the delta - no overtaking anywhere on track", UiFactory.AccentCyan);
                    break;
                case RaceManager.RaceControlState.SafetyCarDeploying:
                    UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                        "SAFETY CAR DEPLOYED", "Forming up - hold position, no overtaking", UiFactory.AccentAmber);
                    break;
                case RaceManager.RaceControlState.SafetyCarActive:
                    if (playerAutopilotNow)
                    {
                        // Deliberately calm, not a warning: race control is
                        // driving the car for the player right now.
                        UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                            "SAFETY CAR - AUTOPILOT ENGAGED", "Race control has the car - sit back" + SafetyCarQueueSuffix(), UiFactory.AccentCyan);
                    }
                    else
                    {
                        UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                            "SAFETY CAR", "No overtaking - hold your position", UiFactory.AccentAmber);
                    }

                    break;
                case RaceManager.RaceControlState.SafetyCarInThisLap:
                    UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                        "SAFETY CAR IN THIS LAP", "Control returns at the restart - tyres will be cold", UiFactory.AccentAmber);
                    break;
                case RaceManager.RaceControlState.Restart:
                    if (race.RestartFollowsRedFlag)
                    {
                        UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                            "RESTART - FORM UP", "Rolling restart after the red flag - hold your grid order", Color.white);
                    }
                    else
                    {
                        UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                            "RESTART", "Green flag imminent - get ready to race", Color.white);
                    }

                    break;
                case RaceManager.RaceControlState.RedFlagged:
                    // UiFactory.Accent (not AccentAmber) deliberately - the
                    // game's actual red, so a red flag reads as visibly more
                    // severe than an amber caution banner.
                    UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                        "RED FLAG - RACE SUSPENDED", race.RedFlagReason + " - bring the car under control", UiFactory.Accent);
                    break;
            }

            UpdatePaceCompliancePill(nearLocalYellow);

            // Escalation pulse: if the headline actually changed while the
            // banner was already on screen (yellow -> VSC -> full SC, etc.),
            // flash the accent bar toward white and let it decay back to the
            // new state's color, so the change reads as an event rather than a
            // silent text swap. Skipped on first appearance since UiFadeIn
            // already animates that.
            string currentBannerTitle = raceControlBannerTitle.text;
            if (wasVisibleBefore && !string.IsNullOrEmpty(previousBannerTitle) && previousBannerTitle != currentBannerTitle && UiFactory.AnimationsEnabled)
            {
                bannerChangeFlashTimer = 0.4f;
            }

            previousBannerTitle = currentBannerTitle;

            if (bannerChangeFlashTimer > 0f && raceControlBannerAccent != null)
            {
                bannerChangeFlashTimer -= Time.deltaTime;
                float flashT = Mathf.Clamp01(bannerChangeFlashTimer / 0.4f);
                raceControlBannerAccent.color = Color.Lerp(raceControlBannerAccent.color, Color.white, flashT);
            }
        }

        // Live queue position/gap-to-slot suffix for the full-SC banner - only
        // meaningful once the player is actually under convoy autopilot; the
        // exact position/gap only once a real queue slot has been assigned, so
        // the banner text doesn't flicker "P0" during the brief deployment
        // window before slots are handed out.
        string SafetyCarQueueSuffix()
        {
            if (race.PlayerParticipant == null || !race.PlayerParticipant.isRaceControlAutopilot)
            {
                return "";
            }

            int position = race.PlayerSafetyCarQueuePosition;
            if (position <= 0)
            {
                return ", following the queue";
            }

            float gap = race.PlayerSafetyCarGapToTargetMeters;
            string gapText = gap >= 0f ? " +" + gap.ToString("0") + "m" : "";
            return ", queue P" + position + gapText;
        }

        // Meaningful whenever the player's own car is actually under a
        // race-control speed cap right now - VSC, full SC, or being near enough
        // to a local yellow incident - hidden otherwise, including a sector
        // yellow the player isn't actually near.
        void UpdatePaceCompliancePill(bool nearLocalYellow)
        {
            if (paceCompliancePill == null)
            {
                return;
            }

            bool show = race.IsRaceControlPaceLimited || nearLocalYellow;
            if (paceCompliancePill.root.gameObject.activeSelf != show)
            {
                paceCompliancePill.root.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            // Target Delta (Part 2): the live speed cap the player is actually
            // being held to right now, not just a status word. During a full SC
            // period with the queue in range, the gap to the actual safety car
            // is the more useful number.
            string capLabel = race.PlayerParticipant != null
                ? " " + race.RaceControlSpeedCapKphFor(race.PlayerParticipant).ToString("0") + " KPH"
                : "";
            float scGap = race.PlayerGapToSafetyCarMeters();
            if (scGap >= 0f)
            {
                capLabel = " SC +" + scGap.ToString("0") + "m";
            }

            bool playerUnderAutopilot = race.PlayerParticipant != null && race.PlayerParticipant.isRaceControlAutopilot;
            if (playerUnderAutopilot)
            {
                // Race control is driving the car - never show penalty-panic
                // ("PACE WARNING"/"SLOW DOWN") UI while autopilot is in control,
                // even if a stale flag from just before deployment is still set.
                paceCompliancePill.SetState("AUTOPILOT - RELAX", UiFactory.AccentCyan, false);
            }
            else if (race.IsPlayerRaceControlWarningActive)
            {
                bool lit = Mathf.PingPong(Time.time * 3f, 1f) > 0.5f;
                paceCompliancePill.SetState("PACE WARNING" + capLabel, UiFactory.Accent, lit);
            }
            else if (race.IsPlayerOverRaceControlPace)
            {
                paceCompliancePill.SetState("SLOW DOWN" + capLabel, UiFactory.AccentAmber, true);
            }
            else if (scGap >= 0f && scGap < 40f)
            {
                paceCompliancePill.SetState("FOLLOW SAFETY CAR" + capLabel, UiFactory.AccentCyan, false);
            }
            else
            {
                paceCompliancePill.SetState("DELTA OK" + capLabel, UiFactory.AccentGreen, false);
            }
        }

        void UpdateSlowElements(VehicleController car, LapTracker lap)
        {
            string session = race.IsTimeTrial ? "TIME TRIAL" : (race.CurrentSession == RaceWeekendSession.Qualifying ? "QUALIFYING" : "RACE");
            int sessionLaps = race.CurrentSession == RaceWeekendSession.Qualifying ? 2 : race.RaceLaps;
            string lapLabel = lap.OutLapActive ? "OUT" : (race.IsTimeTrial ? "L" + lap.DisplayLap : lap.DisplayLap + " / " + sessionLaps);
            string eventName = race.EventData == null ? "Prototype GP" : race.EventData.displayName;
            string reaction = race.RaceStartReactionText;

            sessionLabelText.text = session;
            eventNameText.text = eventName;
            positionBadgeText.text = race.IsTimeTrial ? "" : "P" + race.GetPosition(player) + " / " + race.DisplayedEntrantCount;
            lapCounterText.text = "LAP " + lapLabel;
            sessionMessageText.text = race.SessionMessage + (string.IsNullOrEmpty(reaction) ? "" : "   " + reaction);

            UpdateTimingCard(car, lap);
            UpdateTyreCard(car);
            UpdateCarCard(car);
            UpdatePitCard(car);
            UpdateScWindowCard();
            UpdateQualifyingCard();
            UpdateTowerRows();
        }

        void UpdateTimingCard(VehicleController car, LapTracker lap)
        {
            lapRowValue.text = lap.OutLapActive
                ? "OUT LAP"
                : UiFactory.FormatTime(lap.CurrentLapTime) + (lap.CurrentLapInvalidated ? "  <color=#FF6C6C>INV</color>" : "");
            lastRowValue.text = UiFactory.FormatTime(lap.LastLapTime);

            string best = UiFactory.FormatTime(lap.BestLapTime);
            if (race.IsTimeTrial && race.EventData != null)
            {
                float record = PlayerRecordsStore.GetBestLap(race.EventData.trackId);
                if (record > 0f)
                {
                    best += "  <color=#8A98A2>REC " + UiFactory.FormatTime(record) + "</color>";
                }
            }

            bestRowValue.text = best;

            sectorRow.text = "S1 " + SectorText(1, lap.LastSector1Time) +
                             "  S2 " + SectorText(2, lap.LastSector2Time) +
                             "  S3 " + SectorText(3, lap.LastSector3Time) +
                             "   <color=#8A98A2>NOW S" + lap.CurrentSector + " " + race.LiveSectorText(lap.CurrentSectorTime) + "</color>";

            if (race.IsTimeTrial)
            {
                gapRow.text = "CHECKPOINTS " + lap.CheckpointsPassed + "/16";
            }
            else if (race.CurrentSession == RaceWeekendSession.Qualifying)
            {
                gapRow.text = "DELTA " + race.QualifyingDeltaText(player);
            }
            else
            {
                string penalty = player.penaltiesSeconds > 0.01f ? "   <color=#FF6C6C>PEN +" + player.penaltiesSeconds.ToString("0") + "s</color>" : "";
                gapRow.text = "GAP " + race.GapToLeaderText(player) + "   INT " + race.IntervalAheadText(player) + penalty;
            }
        }

        void UpdateTyreCard(VehicleController car)
        {
            if (tyreFl == null)
            {
                return;
            }

            Color tyreColor = GetTyreColorByCondition(car.Tyres);
            tyreFl.color = tyreColor;
            tyreFr.color = tyreColor;
            tyreRl.color = tyreColor;
            tyreRr.color = tyreColor;

            tyreCompoundValue.text = car.Tyres.Compound.ToString().ToUpperInvariant();
            tyreCompoundValue.color = TyreColor(car.Tyres.Compound.ToString());
            string temp = car.Tyres.TemperatureStatus;
            tyreTempValue.text = temp;
            tyreTempValue.color = temp == "HOT" ? UiFactory.Accent : (temp == "COLD" ? UiFactory.AccentCyan : UiFactory.AccentGreen);

            if (tyreTagText != null)
            {
                string tags = "";
                if (car.Tyres.LockupSeverity > 0.12f)
                {
                    tags = "<color=#FFC85C>LOCKUP</color>";
                }

                if (car.Tyres.FlatSpotLevel > 0.15f)
                {
                    tags += (string.IsNullOrEmpty(tags) ? "" : "   ") + "<color=#FF6C6C>FLAT SPOT</color>";
                }

                tyreTagText.text = tags;
            }

            float wear01 = Mathf.Clamp01(car.Tyres.WearPercent / 100f);
            UiFactory.SetMeterValueAnimated(tyreWearFill, wear01);
            // Past the critical threshold the meter itself flashes (fill and
            // value swap between white and red) instead of just sitting at a
            // static red - matches the same urgency language already used for
            // low fuel and critical damage below.
            bool tyreCritical = wear01 > 0.85f;
            if (tyreCritical)
            {
                bool lit = Mathf.PingPong(Time.time * 3f, 1f) > 0.5f;
                tyreWearFill.color = lit ? Color.white : UiFactory.Accent;
                tyreWearValue.color = lit ? UiFactory.Accent : UiFactory.TextPrimary;
            }
            else
            {
                tyreWearFill.color = wear01 > 0.62f ? UiFactory.Accent : UiFactory.AccentAmber;
                tyreWearValue.color = UiFactory.TextPrimary;
            }

            tyreWearValue.text = Mathf.RoundToInt(car.Tyres.WearPercent) + "%";
        }

        void UpdateCarCard(VehicleController car)
        {
            if (ersFill == null)
            {
                return;
            }

            UiFactory.SetMeterValueAnimated(ersFill, car.ErsBattery);
            ersValue.text = Mathf.RoundToInt(car.ErsBattery * 100f) + "%";

            float fuel01 = Mathf.Clamp01(car.FuelKg / 42f);
            UiFactory.SetMeterValueAnimated(fuelFill, fuel01);
            fuelValue.text = car.FuelKg.ToString("0.0") + "kg";
            fuelFill.color = car.FuelKg < 7f && Mathf.PingPong(Time.time * 2.4f, 1f) > 0.5f
                ? UiFactory.AccentAmber
                : new Color(0.7f, 0.95f, 1f);

            float damage01 = Mathf.Clamp01(car.Damage.OverallPercent / 100f);
            UiFactory.SetMeterValueAnimated(damageFill, damage01);
            damageValue.text = Mathf.RoundToInt(car.Damage.OverallPercent) + "%";
            bool critical = car.Damage.OverallPercent > 55f;
            damageFill.color = critical && Mathf.PingPong(Time.time * 3f, 1f) > 0.5f
                ? Color.white
                : (critical ? UiFactory.Accent : new Color(1f, 0.55f, 0.1f));

            // A sudden jump (a real hit, not gradual wear) fires a one-shot
            // white flash on the whole card's accent bar - see
            // UpdateDamageSpikeFlash - distinct from the steady flicker above,
            // which only communicates "damage is currently high".
            if (previousDamagePercent >= 0f && car.Damage.OverallPercent - previousDamagePercent > 4f)
            {
                damageSpikeFlashTimer = 0.5f;
            }

            previousDamagePercent = car.Damage.OverallPercent;
        }

        // Decays the damage-spike flash triggered in UpdateCarCard - runs every
        // frame (not just on the slow tick) so the fade-back-to-normal reads as
        // smooth motion rather than a stepped animation.
        void UpdateDamageSpikeFlash()
        {
            if (carStatusAccent == null)
            {
                return;
            }

            if (damageSpikeFlashTimer > 0f)
            {
                damageSpikeFlashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(damageSpikeFlashTimer / 0.5f);
                carStatusAccent.color = Color.Lerp(UiFactory.AccentCyan, Color.white, t);
            }
            else if (carStatusAccent.color != UiFactory.AccentCyan)
            {
                carStatusAccent.color = UiFactory.AccentCyan;
            }
        }

        void UpdatePitCard(VehicleController car)
        {
            pitStatusValue.text = car.PitLimiterActive ? "LIMITER 80" : race.PitStatusText(player);
            pitPlanValue.text = BuildPitPlanText();
            float progress = race.PitStopProgress01(player);
            UiFactory.SetMeterValueAnimated(pitFill, progress);
            pitFillValue.text = progress > 0.001f ? Mathf.RoundToInt(progress * 100f) + "%" : "";
        }

        string BuildPitPlanText()
        {
            if (race.IsTimeTrial || race.CurrentSession == RaceWeekendSession.Qualifying)
            {
                return "N/A";
            }

            int nextLap = race.NextPlannedPitLapFor(player);
            if (nextLap <= 0)
            {
                return "STOPS DONE";
            }

            TyreCompound compound = race.NextPlannedPitCompoundFor(player);
            int currentLap = player.lapTracker != null ? player.lapTracker.DisplayLap : 1;
            string status = currentLap > nextLap ? "  <color=#FFC85C>LATE</color>" : "";
            string stopLabel = race.GetPlannedStopCount() >= 2 ? (player.pitStops == 0 ? "STOP 1  " : "STOP 2  ") : "";
            return stopLabel + "Lap " + nextLap + "  ·  " + compound.ToString().ToUpperInvariant() + status;
        }

        // Surfaces the pit-under-safety-car recommendation from RaceManager next
        // to the existing pit card. Purely informational - the player still uses
        // the existing pit request input to actually box, this just tells them
        // when the strategy call favors it.
        void UpdateScWindowCard()
        {
            if (scWindowCard == null)
            {
                return;
            }

            RaceManager.RaceControlState state = race.CurrentRaceControlState;
            bool scPeriod = state == RaceManager.RaceControlState.SafetyCarActive ||
                            state == RaceManager.RaceControlState.SafetyCarDeploying ||
                            state == RaceManager.RaceControlState.VirtualSafetyCar;
            bool recommend = scPeriod && !race.IsTimeTrial && race.CurrentSession != RaceWeekendSession.Qualifying &&
                              !player.isPitting && race.RecommendedPitUnderSafetyCar(player);

            if (scWindowCard.activeSelf != recommend)
            {
                scWindowCard.SetActive(recommend);
            }

            if (recommend)
            {
                TyreCompound compound = race.NextPlannedPitCompoundFor(player);
                scWindowText.text = "SC WINDOW: BOX NOW?\n" + compound.ToString().ToUpperInvariant();
            }
        }

        void UpdateQualifyingCard()
        {
            bool show = race.CurrentSession == RaceWeekendSession.Qualifying;
            if (qualifyingCard.activeSelf != show)
            {
                qualifyingCard.SetActive(show);
            }

            if (show)
            {
                qualifyingDeltaValue.text = race.QualifyingDeltaText(player);
            }
        }

        void UpdateTopAccentFlash()
        {
            if (player.trackLimitWarnings > seenTrackLimitWarnings)
            {
                seenTrackLimitWarnings = player.trackLimitWarnings;
                trackLimitFlashTimer = 2.2f;
            }

            if (trackLimitFlashTimer > 0f)
            {
                trackLimitFlashTimer -= Time.deltaTime;
                bool lit = Mathf.PingPong(Time.time * 4f, 1f) > 0.5f;
                topAccent.color = lit ? UiFactory.AccentAmber : UiFactory.Accent;
                if (trackLimitFlashTimer <= 0f)
                {
                    topAccent.color = UiFactory.Accent;
                }
            }
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
                PushNotification("TRACK LIMITS WARNING " + player.trackLimitWarnings + "/3", UiFactory.AccentAmber);
            }

            watchedTrackLimitWarnings = player.trackLimitWarnings;

            if (lap.BestLapTime > 0f && (watchedBestLap <= 0f || lap.BestLapTime < watchedBestLap - 0.001f))
            {
                if (watchedBestLap > 0f)
                {
                    PushNotification("NEW BEST LAP  " + UiFactory.FormatTime(lap.BestLapTime), UiFactory.AccentPurple);
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
                PushNotification("LOW FUEL", UiFactory.AccentAmber);
            }

            watchedLowFuel = lowFuel;

            if (car.Tyres.TotalLockups > watchedTotalLockups)
            {
                watchedTotalLockups = car.Tyres.TotalLockups;
                if (car.Tyres.LastLockupSeverity > 0.35f)
                {
                    PushNotification("HEAVY LOCKUP", UiFactory.AccentAmber);
                }
            }

            int damageBand = car.Damage.OverallPercent > 55f ? 2 : (car.Damage.OverallPercent > 25f ? 1 : 0);
            if (damageBand > watchedDamageBand)
            {
                PushNotification(damageBand == 2 ? "HEAVY DAMAGE" : "CAR DAMAGE", UiFactory.Accent);
            }

            watchedDamageBand = damageBand;

            if (!race.IsTimeTrial && race.CurrentSession != RaceWeekendSession.Qualifying)
            {
                bool finalLap = lap.DisplayLap >= race.RaceLaps && !lap.OutLapActive;
                if (finalLap && !watchedFinalLap)
                {
                    // Final lap earns the big center-screen moment rather than a
                    // small queued toast - it's rare (once per race) and worth
                    // more visual weight than "PIT STOP COMPLETE" etc.
                    bigFlash.Show("FINAL LAP", UiFactory.Accent, 1.6f);
                }

                watchedFinalLap = finalLap;

                // DisplayLap (1-based), matching BuildPitPlanText's own "LATE"
                // comparison and RaceManager's auto-pit trigger - NextPlannedPitLapFor
                // returns a 1-based lap number, so comparing it against raw
                // CompletedLaps (0-based) would flag the window open a lap late.
                bool pitWindow = race.ShouldPromptPlannedStop(player) && lap.DisplayLap >= race.NextPlannedPitLapFor(player);
                if (pitWindow && !watchedPitWindow)
                {
                    PushNotification("PIT WINDOW OPEN", UiFactory.AccentCyan);
                }

                watchedPitWindow = pitWindow;
            }

            // Part 1: overtake/position-lost, session-fastest-lap, teammate-battle
            // and podium/finish callouts are computed in RaceManager (it has the
            // full field/rivalry context); relay them into the same toast queue
            // as everything else here so they read as one consistent feed.
            string toastText;
            int toastColorKind;
            while (race.TryDequeueHudToast(out toastText, out toastColorKind))
            {
                PushNotification(toastText, ToastColor(toastColorKind));
            }
        }

        Color ToastColor(int colorKind)
        {
            switch (colorKind)
            {
                case RaceManager.ToastColorGreen: return UiFactory.AccentGreen;
                case RaceManager.ToastColorAmber: return UiFactory.AccentAmber;
                case RaceManager.ToastColorCyan: return UiFactory.AccentCyan;
                case RaceManager.ToastColorPurple: return UiFactory.AccentPurple;
                case RaceManager.ToastColorAccent: return UiFactory.Accent;
                default: return Color.white;
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

        void UpdateProgressStrip()
        {
            if (progressStrip == null || !progressStrip.gameObject.activeSelf || race.Track == null)
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
                "ROAD " + (car.IsOffTrackSlowdown ? "OFF TRACK" : "ON TRACK") + "\n" +
                "Hold R to reset to track";
        }

        // ---------- timing tower ----------

        void CreateTowerRow(RectTransform parent, int index)
        {
            float top = -42f - index * 21f;
            RectTransform row = UiFactory.CreateBand(parent, "Timing tower row " + index, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(8f, top - 19f), new Vector2(-8f, top), index % 2 == 0 ? UiFactory.RowEven : UiFactory.RowOdd);
            towerRowBackgrounds[index] = row.GetComponent<Image>();
            // Position + driver code are the primary read of a timing tower row,
            // so they're bold and near-white; lap/gap/interval are secondary and
            // sit a step down in contrast - the same label>value>hint hierarchy
            // used elsewhere in the HUD, applied to a dense table.
            Color primaryCellColor = new Color(0.96f, 0.98f, 1f);
            towerPositions[index] = CreateTowerCell(row, "Tower pos " + index, 6f, 34f, 13, TextAnchor.MiddleLeft, primaryCellColor, true);
            towerDrivers[index] = CreateTowerCell(row, "Tower driver " + index, 36f, 82f, 13, TextAnchor.MiddleLeft, primaryCellColor, true);
            RectTransform tyre = UiFactory.CreateBand(row, "Tower tyre " + index, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(88f, -5f), new Vector2(98f, 5f), Color.white);
            towerTyres[index] = tyre.GetComponent<Image>();
            towerLaps[index] = CreateTowerCell(row, "Tower lap " + index, 104f, 136f, 12, TextAnchor.MiddleLeft, new Color(0.7f, 0.78f, 0.83f), false);
            towerGaps[index] = CreateTowerCell(row, "Tower gap " + index, 140f, 222f, 12, TextAnchor.MiddleLeft, new Color(0.85f, 0.91f, 0.94f), false);
            towerIntervals[index] = CreateTowerCell(row, "Tower interval " + index, 226f, 292f, 12, TextAnchor.MiddleLeft, new Color(0.85f, 0.91f, 0.94f), false);

            // Small DRS-detection dot at the row's right edge: lit green while
            // that car has DRS active/available (from RaceManager.DrsStateText,
            // already generic per-participant), invisible otherwise. Reads as a
            // broadcast-style "DRS zone" cue without adding a new column.
            RectTransform drsDot = UiFactory.CreateRect(row, "Tower drs " + index, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            drsDot.sizeDelta = new Vector2(8f, 8f);
            drsDot.anchoredPosition = new Vector2(299f, 0f);
            Image drsDotImage = drsDot.gameObject.AddComponent<Image>();
            drsDotImage.sprite = UiFactory.GlowSprite;
            drsDotImage.color = new Color(0f, 0f, 0f, 0f);
            drsDotImage.raycastTarget = false;
            towerDrsDots[index] = drsDotImage;
        }

        Text CreateTowerCell(RectTransform parent, string name, float minX, float maxX, int size, TextAnchor alignment, Color color, bool bold)
        {
            Text cell = UiFactory.CreateText(parent, name, "", size, color, alignment);
            RectTransform rect = cell.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(minX, 0f);
            rect.offsetMax = new Vector2(maxX, 0f);
            cell.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
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
                string ttDrsState = race.DrsStateText(player);
                bool ttDrsHot = ttDrsState == "ACTIVE" || ttDrsState == "AVAILABLE";
                SetTowerRow(0, "P1", DriverCode(player), TyreColor(player.vehicle != null && player.vehicle.Tyres != null ? player.vehicle.Tyres.Compound.ToString() : ""), lap.DisplayLap.ToString("00"), UiFactory.FormatTime(lap.BestLapTime), "", true, false, ttDrsHot);
                for (int i = 1; i < TowerRowCount; i++)
                {
                    SetTowerRowVisible(i, false);
                }

                return;
            }

            tower.text = "POS  DVR     T  LAP    GAP       INT";
            List<RaceParticipant> order = race.GetRunningOrderSnapshot();
            int count = Mathf.Min(visibleTowerRows, order.Count);
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
                string drsState = race.DrsStateText(entry);
                bool drsHot = drsState == "ACTIVE" || drsState == "AVAILABLE";
                SetTowerRow(i, (i + 1).ToString("00"), DriverCode(entry), TyreColor(tyreName), lap, race.GapToLeaderText(entry), race.IntervalAheadText(entry), entry == player, entry.isPitting, drsHot);
            }
        }

        void UpdateQualifyingTowerRows()
        {
            tower.text = "POS  DVR     BEST         GAP";
            List<RaceManager.QualifyingTowerRow> rows = race.BuildQualifyingTowerRows(visibleTowerRows);
            for (int i = 0; i < rows.Count; i++)
            {
                RaceManager.QualifyingTowerRow row = rows[i];
                SetTowerRow(i, row.position.ToString("00"), row.driverCode, new Color(0.34f, 0.78f, 1f), row.bestTimeText, row.gapText, "", row.isPlayer);
            }

            for (int i = rows.Count; i < TowerRowCount; i++)
            {
                SetTowerRowVisible(i, false);
            }
        }

        void SetTowerRow(int index, string position, string driver, Color tyreColor, string lap, string gap, string interval, bool highlight, bool pit = false, bool drsHot = false)
        {
            SetTowerRowVisible(index, true);
            Color leaderTint = index == 0 && !highlight ? new Color(0.1f, 0.14f, 0.2f, 0.86f) : (index % 2 == 0 ? UiFactory.RowEven : UiFactory.RowOdd);
            towerRowBackgrounds[index].color = highlight ? new Color(0.95f, 0.08f, 0.06f, 0.86f) : leaderTint;
            towerPositions[index].text = position;
            towerDrivers[index].text = driver;
            towerTyres[index].color = tyreColor;
            towerLaps[index].text = lap;
            towerGaps[index].text = gap;
            // Pitting takes over the interval slot with a clear "PIT" tag - the
            // interval-to-the-car-ahead is meaningless while a car is in the
            // pit lane, so this is strictly more useful than a stale number.
            towerIntervals[index].text = pit ? "PIT" : interval;
            towerIntervals[index].color = pit ? UiFactory.AccentAmber : (highlight ? Color.white : new Color(0.85f, 0.91f, 0.94f));
            towerIntervals[index].fontStyle = pit ? FontStyle.Bold : FontStyle.Normal;
            if (towerDrsDots[index] != null)
            {
                towerDrsDots[index].color = pit
                    ? new Color(0f, 0f, 0f, 0f)
                    : (drsHot ? new Color(UiFactory.AccentGreen.r, UiFactory.AccentGreen.g, UiFactory.AccentGreen.b, 0.95f) : new Color(0f, 0f, 0f, 0f));
            }
        }

        void SetTowerRowVisible(int index, bool visible)
        {
            if (towerRowBackgrounds[index] != null && towerRowBackgrounds[index].gameObject.activeSelf != visible)
            {
                towerRowBackgrounds[index].gameObject.SetActive(visible);
            }
        }

        // ---------- start lights / flashes ----------

        void UpdateRaceStartLights()
        {
            if (startLightPanel == null || race == null)
            {
                return;
            }

            bool visible = race.RaceStartLightsVisible;
            if (lightsWereVisible && !visible && race.CanDrive)
            {
                bigFlash.Show("LIGHTS OUT", new Color(0.35f, 1f, 0.45f), 0.8f);
                SimpleAudioManager.PlayDrsAvailable();
            }

            lightsWereVisible = visible;
            if (startLightPanel.activeSelf != visible)
            {
                startLightPanel.SetActive(visible);
            }

            if (!visible)
            {
                // Reset so the next sequence (e.g. a safety-car restart) starts
                // its punch animation from the first light again instead of
                // silently skipping it because the counter never rewound.
                previousLitLightCount = 0;
                return;
            }

            int lit = race.RaceStartLightCount;
            // A quick scale punch on each light the instant it comes on, decaying
            // back to rest - turns the sequence from a flat color swap into
            // something with a bit of mechanical weight, like the real gantry.
            if (lit > previousLitLightCount && UiFactory.AnimationsEnabled)
            {
                for (int i = previousLitLightCount; i < lit && i < startLightPunch.Length; i++)
                {
                    startLightPunch[i] = 1f;
                }
            }

            previousLitLightCount = lit;

            for (int i = 0; i < startLightImages.Length; i++)
            {
                if (startLightImages[i] == null)
                {
                    continue;
                }

                startLightImages[i].color = i < lit ? new Color(1f, 0.04f, 0.025f, 1f) : new Color(0.09f, 0.01f, 0.012f, 1f);
                RectTransform lightRect = startLightImages[i].rectTransform;
                if (startLightPunch[i] > 0f)
                {
                    startLightPunch[i] = Mathf.Max(0f, startLightPunch[i] - Time.deltaTime * 3.2f);
                    float scale = 1f + startLightPunch[i] * 0.22f;
                    lightRect.localScale = new Vector3(scale, scale, 1f);
                }
                else if (lightRect.localScale != Vector3.one)
                {
                    lightRect.localScale = Vector3.one;
                }
            }
        }

        // Chequered finish flourish: fires once when the player's own car
        // transitions to finished, independent of the CanDrive-gated notification
        // watchers below (CanDrive goes false the moment the race finishes, which
        // would otherwise swallow the detection).
        void UpdateFinishFlourish()
        {
            if (finishFlourishPanel == null || player == null)
            {
                return;
            }

            if (player.finished && !watchedPlayerFinished)
            {
                watchedPlayerFinished = true;
                int position = race.GetPosition(player);
                finishFlourishTitle.text = "RACE FINISHED";
                finishFlourishSubtitle.text = position == 1 ? "P1 - VICTORY" : "FINISHED P" + position;
                finishFlourishSubtitle.color = position == 1 ? new Color(1f, 0.82f, 0.24f) : (position <= 3 ? UiFactory.AccentGreen : UiFactory.TextPrimary);
                finishFlourishPanel.SetActive(true);
                finishFlourishTimer = FinishFlourishDuration;
                bigFlash.Show(position == 1 ? "VICTORY!" : "CHEQUERED FLAG", position == 1 ? new Color(1f, 0.82f, 0.24f) : Color.white, 1.4f);
                SimpleAudioManager.PlayDrsAvailable();
            }

            if (finishFlourishTimer <= 0f)
            {
                return;
            }

            finishFlourishTimer -= Time.deltaTime;
            const float fadeIn = 0.3f;
            const float fadeOut = 0.7f;
            float elapsed = FinishFlourishDuration - finishFlourishTimer;
            float alpha = elapsed < fadeIn ? elapsed / fadeIn : (finishFlourishTimer < fadeOut ? finishFlourishTimer / fadeOut : 1f);
            finishFlourishGroup.alpha = Mathf.Clamp01(alpha);
            if (finishFlourishTimer <= 0f)
            {
                finishFlourishPanel.SetActive(false);
            }
        }

        // ---------- helpers ----------

        string SectorText(int sector, float time)
        {
            return race == null ? (time <= 0f ? "--.---" : UiFactory.FormatTime(time)) : race.PlayerSectorText(sector, time);
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

        Color GetTyreColorByCondition(TyreState tyres)
        {
            if (tyres.TemperatureStatus == "HOT") return new Color(1f, 0.28f, 0.2f);
            if (tyres.TemperatureStatus == "COLD") return new Color(0.36f, 0.8f, 1f);
            if (tyres.Wear < 0.4f) return new Color(1f, 0.55f, 0.12f);
            return UiFactory.AccentGreen;
        }

        // Reflects the car's real-time ERS state rather than just the strategy
        // dial: DEPLOY only while actually drawing down the battery, HARVEST only
        // while actually regenerating (or whenever the dial is set to Harvest),
        // LOW/EMPTY when charge is running out, otherwise an idle state that still
        // names the active strategy so BAL/Attack ready still reads clearly.
        void UpdateErsPill(VehicleController car)
        {
            // Race control (VSC/SC/local yellow) force-disables ERS deploy for the
            // player exactly like it already does for AI - show that explicitly
            // rather than letting it read as "ready but the driver chose not to
            // deploy".
            if (race != null && (race.IsRaceControlPaceLimited || (race.PlayerParticipant != null && race.IsNearLocalYellowIncident(race.PlayerParticipant))))
            {
                ersPill.SetState("ERS DISABLED", UiFactory.TextMuted, false);
                return;
            }

            int mode = race == null || race.Settings == null ? (int)ErsStrategyMode.Balanced : race.Settings.Current.ersMode;
            if (car.ErsDeploying)
            {
                ersPill.SetState("ERS DEPLOY", UiFactory.AccentCyan, true);
                return;
            }

            if (car.ErsHarvesting || mode == (int)ErsStrategyMode.Harvest)
            {
                ersPill.SetState("ERS HARVEST", UiFactory.AccentAmber, false);
                return;
            }

            if (car.ErsBattery < 0.05f)
            {
                bool lit = Mathf.PingPong(Time.time * 2.4f, 1f) > 0.5f;
                ersPill.SetState("ERS EMPTY", UiFactory.AccentAmber, lit);
                return;
            }

            if (car.ErsBattery < 0.2f)
            {
                ersPill.SetState("ERS LOW", UiFactory.AccentAmber, false);
                return;
            }

            if (mode == (int)ErsStrategyMode.Attack)
            {
                ersPill.SetState("ERS READY", UiFactory.AccentGreen, false);
                return;
            }

            ersPill.SetState("ERS BAL", UiFactory.TextMuted, false);
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
