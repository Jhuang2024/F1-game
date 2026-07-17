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
        // Brief scale punch on the lap counter the instant the lap number
        // actually ticks over - see UiPunch and the trigger in
        // UpdateSlowElements. Round 4: lap/position/lap-time were flagged as
        // the HUD's primary numbers but changed with zero motion at all.
        UiPunch lapCounterPunch;
        int previousDisplayLapForPunch = -1;

        // Timing tower.
        Text tower;
        RectTransform towerHeaderRow;
        Text towerHeaderPos;
        Text towerHeaderDriver;
        Text towerHeaderLap;
        Text towerHeaderGap;
        Text towerHeaderInterval;
        // True while the tower's columns are laid out for qualifying's
        // POS/DVR/BEST/GAP shape (BEST needs real width for a full lap time
        // like "01:23.456", unlike race mode's 2-digit lap counter) rather
        // than race mode's POS/DVR/T/LAP/GAP/INT shape - see
        // SetTowerLayoutForQualifying.
        bool towerQualifyingLayout;
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
        HudPill slipstreamPill;

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
        // Plain-language pit phase pill (Entering pit lane / In pit lane / Box
        // stop / Pit exit) shown above the existing technical status line -
        // see UpdatePitCard/PitPhasePillState. Reuses the same pill widget as
        // the DRS/ERS/fuel state pills instead of a new widget type.
        HudPill pitPhasePill;
        // Brief scale punch whenever the pill's plain-language state actually
        // changes (queued -> approaching -> entering -> box stop -> exit),
        // same UiPunch used for the lap counter - see UpdatePitPhasePill.
        UiPunch pitPhasePillPunch;
        string previousPitPhaseText = "";
        // Cancellable-manual-pit-stop feature: a real clickable button (not
        // another passive HudPill) since this is the HUD's first interactive
        // in-race control - only ever active while
        // RaceManager.CanCancelManualPitRequest() is true (a manual/SC-offer
        // request is queued and not yet committed), hidden the instant that
        // stops being true. previousPitCancelVisible drives the same kind of
        // edge-triggered feedback the phase pill's own UiPunch uses, without
        // needing a second UiPunch component just for a button appearing.
        Button pitCancelButton;
        Text pitCancelButtonLabel;
        bool previousPitCancelVisible;
        // Radio message stacking fix: up to MaxRadioCards independently
        // active, independently fading compact cards (newest at index 0)
        // instead of one shared card/text pair a single message occupied at
        // a time - see RaceManager.activeEngineerMessages and BuildRadioStack/
        // UpdateRadioStack below.
        // Round 4 overlap-risk pass: this stack and the top-right card stack
        // (Car Status/Pit/SC Window/Qualifying) are both independently
        // anchored (bottom-right growing up, top-right growing down) inside
        // the exact same RightStackWidth-wide column - at a busy moment (a
        // safety car period, which is also exactly when the engineer is most
        // likely to be talking) with every top-right card visible AND a full
        // burst of radio messages AND a high hudScale, their worst-case
        // extents can reach far enough into the middle of the screen to meet.
        // Trimmed one card and just over 1.5 wrapped lines off the ceiling
        // here - still generous for a HUD, but meaningfully smaller than a
        // real broadcast overlay would ever need, and it declutters the
        // common case too (3 stacked toasts already reads as "busy").
        const int MaxRadioCards = 3;
        RectTransform radioStackContainer;
        RectTransform[] radioCardSlots = new RectTransform[MaxRadioCards];
        CanvasGroup[] radioCardGroups = new CanvasGroup[MaxRadioCards];
        Text[] radioCardTexts = new Text[MaxRadioCards];
        Image[] radioCardAccents = new Image[MaxRadioCards];
        Vector2[] radioCardRestPositions = new Vector2[MaxRadioCards];
        GameObject qualifyingCard;
        Text qualifyingCardHeader;
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
        bool previousErsDeploying;
        readonly F1Game.Race.Physics.RevLimiterGate revLimiterGate = new F1Game.Race.Physics.RevLimiterGate();
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
        // instead of popping.
        // Stacking fix: this used to be a single slot, so a second event queued
        // while the first was still holding (losing a position right after gaining
        // one, a fastest lap alongside an overtake) sat waiting for the full ~3s
        // fade-in/hold/fade-out cycle to finish before it ever appeared - by the
        // time it showed, the moment it described was already old, reading as
        // "delayed". NotificationSlotCount independent slots (same fade in/hold/
        // fade out state machine each, stacked top-to-bottom) let that many events
        // display at once instead of queuing behind one another.
        const int NotificationSlotCount = 2;
        struct HudNotification
        {
            public string text;
            public Color color;
        }

        readonly Queue<HudNotification> notificationQueue = new Queue<HudNotification>();
        readonly CanvasGroup[] notificationGroups = new CanvasGroup[NotificationSlotCount];
        readonly Text[] notificationTexts = new Text[NotificationSlotCount];
        readonly Image[] notificationAccents = new Image[NotificationSlotCount];
        readonly float[] notificationTimers = new float[NotificationSlotCount];
        readonly int[] notificationPhases = new int[NotificationSlotCount]; // 0 idle, 1 fade in, 2 hold, 3 fade out

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
            // Always show the FULL field on the timing tower (per request - see all
            // 22 cars, not just the top 10). Compact mode still trims the other HUD
            // modules below; only the tower's row cap is decoupled from it.
            visibleTowerRows = TowerRowCount;

            // Per-module HUD toggles (Display Settings): construction-time gating,
            // same pattern `compact` already uses above for the car-status card and
            // input telemetry - a module simply never gets built when its toggle is
            // off, rather than being built and hidden. Radio stack and race-control
            // banner drive their own visibility every frame (see UpdateRadioStack/
            // UpdateRaceControlBanner) so their toggles are read there instead of
            // gating the build call.
            bool showTimingTower = settings == null || settings.hudShowTimingTower;
            bool showTrackMap = settings == null || settings.hudShowTrackMap;
            bool showInputTelemetry = settings == null || settings.hudShowInputTelemetry;
            bool showCarStatus = settings == null || settings.hudShowCarStatus;
            bool showProgressStrip = settings == null || settings.hudShowProgressStrip;

            BuildTopBand();
            BuildRaceControlBanner();
            if (showProgressStrip)
            {
                BuildProgressStrip();
            }

            if (showTrackMap)
            {
                BuildTrackMap();
            }

            BuildNotificationPanel();
            if (showTimingTower)
            {
                BuildTimingTower();
            }

            BuildBottomDash();
            if (showInputTelemetry)
            {
                BuildInputTelemetry();
            }

            BuildTimingCard();
            BuildRightStack(showCarStatus);
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

        // Vertical clearance below the top band's REAL bottom edge: the band is
        // 52 tall at offset 8 and scales down from its top pivot, so anything
        // pinned near the top must clear 8 + 52 * hudScale (plus a small gap)
        // or the scaled band will grow over it - localScale reserves no layout
        // space.
        float TopBandClearance()
        {
            return 8f + 52f * hudScale + 10f;
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
            // Event names are unbounded strings: shrink-to-fit and clip inside
            // the segment instead of overflowing across the divider into the
            // position badge (the segment is 48px tall, so Truncate here can
            // never hide a whole single line).
            eventNameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            eventNameText.verticalOverflow = VerticalWrapMode.Truncate;
            eventNameText.resizeTextForBestFit = true;
            eventNameText.resizeTextMinSize = 11;
            eventNameText.resizeTextMaxSize = 16;
            CreateTopBandDivider(band, 0.4f);
            // Position and lap counter are the two primary numbers this strip
            // exists to show (see the HUD hierarchy pass) - bumped a couple of
            // points bigger and, for the lap counter, bold to match the
            // position badge's weight instead of reading a step lighter than
            // a number of equal importance right next to it.
            positionBadgeText = CreateTopBandSegment(band, "Position segment", 0.41f, 0.51f, UiFactory.AccentCyan, TextAnchor.MiddleCenter, 19, true);
            CreateTopBandDivider(band, 0.51f);
            lapCounterText = CreateTopBandSegment(band, "Lap segment", 0.52f, 0.63f, UiFactory.TextPrimary, TextAnchor.MiddleCenter, 19, true);
            // "LAP 12 / 58" at 19pt bold is right at this segment's ~108px
            // width: shrink-to-fit instead of spilling across the dividers.
            lapCounterText.horizontalOverflow = HorizontalWrapMode.Wrap;
            lapCounterText.verticalOverflow = VerticalWrapMode.Truncate;
            lapCounterText.resizeTextForBestFit = true;
            lapCounterText.resizeTextMinSize = 12;
            lapCounterText.resizeTextMaxSize = 19;
            lapCounterPunch = lapCounterText.gameObject.AddComponent<UiPunch>();
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
        // Baseline banner height (fits a 1-2 line subtitle, matches the prior
        // fixed 118px size) plus how much extra height each additional wrapped
        // subtitle line earns, and a hard ceiling so a pathological message
        // still can't run away with the top-left corner of the HUD.
        const float RaceControlBannerBaseHeight = 118f;
        const float RaceControlBannerExtraLineHeight = 34f;
        const float RaceControlBannerMaxHeight = 210f;
        const float RaceControlBannerWidth = 480f;

        void BuildRaceControlBanner()
        {
            // Sized generously (was 328x68, then 460x118): red-flag/restart
            // subtitles now routinely carry a full sentence plus a live
            // countdown number, and a multi-car red-flag reason additionally
            // lists every involved driver's name plus a sector ("Huge
            // multi-car pileup - track completely blocked - Max Verstappen,
            // Lewis Hamilton and Charles Leclerc in Sector 3 - race suspended.
            // Restart in 8s") - long enough to wrap 3+ lines even in this
            // widened box. Rather than pick one more fixed size that will
            // eventually be too small again, the banner now resizes itself
            // every update (see ApplyRaceControlBannerSize) to the actual
            // subtitle's wrapped line count, and the pace pill below it is
            // repositioned off the banner's real height instead of a fixed
            // offset - so growth here can never bury the pill underneath it.
            raceControlBanner = UiFactory.CreateStatusBanner(transform, "Race Control", RaceControlBannerWidth, RaceControlBannerBaseHeight, out raceControlBannerAccent, out raceControlBannerDot, out raceControlBannerTitle, out raceControlBannerSubtitle);
            raceControlBanner.anchorMin = new Vector2(0f, 1f);
            raceControlBanner.anchorMax = new Vector2(0f, 1f);
            raceControlBanner.pivot = new Vector2(0f, 1f);
            // Below the top band's scaled bottom edge: the old fixed -10 put
            // the left-pinned banner ON TOP of the centre-pinned session band
            // at every aspect ratio of 16:9 or narrower (the two overlap
            // horizontally whenever the reference width is under ~1970), so a
            // caution period hid the session pill/event name exactly when the
            // banner appeared.
            raceControlBanner.anchoredPosition = new Vector2(16f, -TopBandClearance());
            ApplyPanelScale(raceControlBanner);
            raceControlBanner.gameObject.SetActive(false);

            // Smaller companion pill directly under the banner: only ever shown
            // while the banner is, so it never appears as an orphaned "DELTA OK"
            // during a normal green-flag race. Its vertical position is
            // recomputed every frame in ApplyRaceControlBannerSize once the
            // banner's real (possibly grown) height is known; this initial
            // position just matches the banner's un-grown baseline so it isn't
            // momentarily wrong before the first update tick.
            paceCompliancePill = UiFactory.CreatePill(transform, "Pace Compliance", 220f, 30f);
            paceCompliancePill.root.anchorMin = new Vector2(0f, 1f);
            paceCompliancePill.root.anchorMax = new Vector2(0f, 1f);
            paceCompliancePill.root.pivot = new Vector2(0f, 1f);
            paceCompliancePill.root.anchoredPosition = new Vector2(16f, -TopBandClearance() - RaceControlBannerBaseHeight - 12f);
            ApplyPanelScale(paceCompliancePill.root);
            paceCompliancePill.root.gameObject.SetActive(false);
        }

        // Resizes the banner to fit however many lines its current subtitle
        // actually wraps to (using the same greedy word-wrap estimate the
        // radio stack uses to size its own cards), then pins the pace pill
        // directly beneath the banner's real bottom edge - so a long red-flag
        // reason with several driver names can never silently overlap it.
        void ApplyRaceControlBannerSize()
        {
            float subtitleWidth = raceControlBanner.rect.width - 32f;
            float approxCharsPerLine = Mathf.Max(10f, subtitleWidth / 7.6f);
            int lines = EstimateWrappedLineCount(raceControlBannerSubtitle.text, approxCharsPerLine);
            int extraLines = Mathf.Max(0, lines - 2);
            float height = Mathf.Clamp(RaceControlBannerBaseHeight + extraLines * RaceControlBannerExtraLineHeight, RaceControlBannerBaseHeight, RaceControlBannerMaxHeight);
            if (!Mathf.Approximately(raceControlBanner.sizeDelta.y, height))
            {
                raceControlBanner.sizeDelta = new Vector2(raceControlBanner.sizeDelta.x, height);
            }

            if (paceCompliancePill != null)
            {
                paceCompliancePill.root.anchoredPosition = new Vector2(16f, -TopBandClearance() - height - 12f);
            }
        }

        void BuildProgressStrip()
        {
            // Tracks the top band's scaled bottom (at hudScale 1 this is the
            // original -66): the fixed offset let the band grow over the strip
            // at hudScale above ~1.12.
            progressStrip = UiFactory.CreateResponsivePanel(transform, "Track progress strip", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(ProgressStripWidth + 24f, 16f), new Vector2(0f, -(TopBandClearance() - 4f)), new Color(0.006f, 0.009f, 0.012f, 0.66f));
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

            // Track ribbon as a continuous closed polyline - rotated segment
            // rects between resampled centerline points - instead of the old
            // scatter of ~110 separate glow dots, so the circuit reads as one
            // smooth drawn line at any zoom. Still tinted by sector (S1/S2/S3)
            // using the same normalized-progress thirds RaceManager/LapTracker
            // already use for TrackProgress.sector, so the minimap reads like a
            // broadcast track map - no new track data needed, just the split.
            // Sector tint boundaries use ARC-LENGTH fraction, matching how
            // TrackProgress.sector is actually assigned (distance thirds) -
            // the old centerline-INDEX fraction drifted off the true S1/S2/S3
            // boundaries wherever the points weren't uniformly spaced.
            float totalLength = 0f;
            float[] cumulative = new float[line.Count];
            for (int i = 1; i < line.Count; i++)
            {
                totalLength += Vector3.Distance(line[i - 1], line[i]);
                cumulative[i] = totalLength;
            }

            totalLength = Mathf.Max(totalLength, 0.001f);
            int segmentBudget = Mathf.Min(line.Count, 180);
            int step = Mathf.Max(1, line.Count / segmentBudget);
            for (int i = 0; i < line.Count; i += step)
            {
                int next = i + step >= line.Count ? 0 : i + step;
                float normalized = cumulative[i] / totalLength;
                Color sectorTint = normalized < 0.333f ? UiFactory.AccentCyan
                    : normalized < 0.666f ? UiFactory.AccentAmber
                    : UiFactory.AccentPurple;
                Color ribbonColor = new Color(sectorTint.r, sectorTint.g, sectorTint.b, 0.55f);
                CreateMapSegment(WorldToMap(line[i]), WorldToMap(line[next]), 3f, ribbonColor);
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

        // One segment of the track ribbon polyline: a thin rect stretched
        // between two map points and rotated to lie along them. Slightly
        // over-length (by its own thickness) so consecutive segments overlap
        // and corners never show gaps.
        Image CreateMapSegment(Vector2 from, Vector2 to, float thickness, Color color)
        {
            RectTransform segment = UiFactory.CreateRect(trackMap, "Map track segment", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Vector2 delta = to - from;
            segment.sizeDelta = new Vector2(delta.magnitude + thickness, thickness);
            segment.anchoredPosition = (from + to) * 0.5f;
            segment.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            Image image = segment.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
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
            // Scale-aware x offset: this panel and the bottom dash are both
            // centre-anchored, and localScale grows them toward each other -
            // above hudScale ~1.03 the fixed -292 put the telemetry bars over
            // the dash's left edge. The extra shift keeps a gap at any scale.
            float telemetryX = -(292f + 300f * Mathf.Max(0f, hudScale - 1f));
            RectTransform panel = UiFactory.CreateResponsivePanel(transform, "Input telemetry", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(96f, 138f), new Vector2(telemetryX, 12f), new Color(0.008f, 0.012f, 0.018f, 0.8f));
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

        const float NotificationBandHeight = 48f;
        const float NotificationBandSpacing = 8f;

        void BuildNotificationPanel()
        {
            for (int i = 0; i < NotificationSlotCount; i++)
            {
                float yOffset = -92f - i * (NotificationBandHeight + NotificationBandSpacing);
                RectTransform band = UiFactory.CreateResponsivePanel(transform, "HUD notification " + i, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(470f, NotificationBandHeight), new Vector2(0f, yOffset), new Color(0.006f, 0.009f, 0.012f, 0.88f));
                ApplyPanelScale(band);
                notificationAccents[i] = UiFactory.CreateBand(band, "Notification accent " + i, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f), UiFactory.Accent).GetComponent<Image>();
                notificationTexts[i] = UiFactory.CreateText(band, "Notification text " + i, "", 18, UiFactory.TextPrimary, TextAnchor.MiddleCenter);
                RectTransform textRect = notificationTexts[i].GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(16f, 4f);
                textRect.offsetMax = new Vector2(-12f, -4f);
                notificationGroups[i] = band.gameObject.AddComponent<CanvasGroup>();
                notificationGroups[i].alpha = 0f;
                notificationGroups[i].blocksRaycasts = false;
                notificationGroups[i].interactable = false;
            }
        }

        void BuildTimingTower()
        {
            float height = 44f + visibleTowerRows * 21f + 10f;
            RectTransform towerBand = UiFactory.CreateResponsivePanel(transform, "Timing tower", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(324f, height), new Vector2(16f, 40f), new Color(0.006f, 0.009f, 0.012f, 0.72f));
            ApplyPanelScale(towerBand);
            UiFactory.CreateBand(towerBand, "Tower accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.Accent);

            // Time Trial override: a single centered label spans the whole
            // header band instead of columns, since that mode only ever shows
            // one row (the player) and column headers would be meaningless.
            tower = UiFactory.CreateText(towerBand, "Tower header", "", 13, UiFactory.TextMuted, TextAnchor.MiddleLeft);
            RectTransform towerRect = tower.GetComponent<RectTransform>();
            towerRect.anchorMin = new Vector2(0f, 1f);
            towerRect.anchorMax = new Vector2(1f, 1f);
            towerRect.offsetMin = new Vector2(16f, -34f);
            towerRect.offsetMax = new Vector2(-10f, -10f);
            tower.gameObject.SetActive(false);

            // Real per-column headers, aligned to the exact same x-ranges
            // CreateTowerCell uses for the data rows below - alignment fix:
            // the header used to be one hand-typed, space-padded string
            // ("POS  DVR     T  LAP    GAP       INT") which cannot reliably
            // line up with the data cells below it since CreateText renders a
            // normal proportional font, not a monospace one. towerHeaderRow
            // uses the identical 8px left/right margin CreateTowerRow gives
            // each data row, so column x-coordinates match exactly.
            towerHeaderRow = UiFactory.CreateRect(towerBand, "Tower header row", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(8f, -34f), new Vector2(-8f, -10f));
            towerHeaderPos = CreateTowerCell(towerHeaderRow, "Header pos", 6f, 34f, 13, TextAnchor.MiddleLeft, UiFactory.TextMuted, true);
            towerHeaderPos.text = "POS";
            towerHeaderDriver = CreateTowerCell(towerHeaderRow, "Header driver", 36f, 82f, 13, TextAnchor.MiddleLeft, UiFactory.TextMuted, true);
            towerHeaderDriver.text = "DVR";
            towerHeaderLap = CreateTowerCell(towerHeaderRow, "Header lap", 104f, 136f, 13, TextAnchor.MiddleLeft, UiFactory.TextMuted, true);
            towerHeaderLap.text = "LAP";
            towerHeaderGap = CreateTowerCell(towerHeaderRow, "Header gap", 140f, 222f, 13, TextAnchor.MiddleLeft, UiFactory.TextMuted, true);
            towerHeaderGap.text = "GAP";
            towerHeaderInterval = CreateTowerCell(towerHeaderRow, "Header interval", 226f, 292f, 13, TextAnchor.MiddleLeft, UiFactory.TextMuted, true);
            towerHeaderInterval.text = "INT";

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

            Text gearLabel = UiFactory.CreateText(dash, "Gear label", "GEAR", 13, UiFactory.TextMuted, TextAnchor.MiddleCenter);
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

            // Slipstream: hidden entirely below ~20% tow strength rather than
            // showing an "OFF" state like the pills above - it's a minor, constantly
            // fluctuating effect that would just be noise most of the race.
            slipstreamPill = UiFactory.CreatePill(dash, "TOW", 132f, 26f);
            slipstreamPill.root.anchorMin = new Vector2(1f, 1f);
            slipstreamPill.root.anchorMax = new Vector2(1f, 1f);
            slipstreamPill.root.pivot = new Vector2(1f, 1f);
            slipstreamPill.root.anchoredPosition = new Vector2(-14f, -122f);
            slipstreamPill.root.gameObject.SetActive(false);
        }

        void BuildTimingCard()
        {
            // Card grew 14px (was 168 tall) to fit a taller, bigger-font "Lap"
            // row - see UiFactory.CreateHudHeroRow - since the live current-lap
            // time is one of the HUD's three primary numbers (position, lap
            // counter, current lap time) called out in the round 4 hierarchy
            // pass, but used to render at the exact same size as Last/Best
            // below it with nothing to mark it as the one to actually watch
            // while driving. Every row below it shifts down by the same 14px
            // so their existing relative spacing is unchanged.
            RectTransform card = UiFactory.CreateResponsivePanel(transform, "Timing card", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(324f, 182f), new Vector2(16f, 12f), UiFactory.HudCardBackground);
            ApplyPanelScale(card);
            UiFactory.CreateBand(card, "Timing accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), UiFactory.AccentCyan);
            Text title = UiFactory.CreateText(card, "Timing title", "TIMING", 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            RectTransform titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(14f, -24f);
            titleRect.offsetMax = new Vector2(-10f, -6f);

            UiFactory.CreateHudHeroRow(card, "Lap", 28f, 34f, 22, out lapRowValue);
            UiFactory.CreateHudLabelValueRow(card, "Last", 66f, out lastRowValue);
            UiFactory.CreateHudLabelValueRow(card, "Best", 90f, out bestRowValue);

            sectorRow = UiFactory.CreateText(card, "Sectors", "", 13, UiFactory.TextPrimary, TextAnchor.MiddleLeft);
            // Single line, never wrap: with the CreateText default (Wrap +
            // vertical Overflow) the full three-sector string wrapped onto a
            // second line that rendered straight over the GAP/INT row 4px
            // below. Combined with the compacted mm:ss stripping in
            // UpdateTimingCard the row now fits; Overflow is the safety net.
            sectorRow.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform sectorRect = sectorRow.GetComponent<RectTransform>();
            sectorRect.anchorMin = new Vector2(0f, 1f);
            sectorRect.anchorMax = new Vector2(1f, 1f);
            sectorRect.offsetMin = new Vector2(14f, -142f);
            sectorRect.offsetMax = new Vector2(-10f, -118f);

            gapRow = UiFactory.CreateText(card, "Gaps", "", 13, UiFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform gapRect = gapRow.GetComponent<RectTransform>();
            gapRect.anchorMin = new Vector2(0f, 1f);
            gapRect.anchorMax = new Vector2(1f, 1f);
            gapRect.offsetMin = new Vector2(14f, -170f);
            gapRect.offsetMax = new Vector2(-10f, -146f);
        }

        void BuildRightStack(bool showCarStatus)
        {
            // Anchored top-right, just under the track map, instead of the old
            // screen-center anchor - overlap fix: a vertically-centered stack
            // grows both up AND down as cards are added/resized, which used to
            // let Car Status + Pit + SC Window (all real at once during a
            // safety car period) push up into the track map in the corner.
            // Pinning the stack's top-right corner below the map means it can
            // only ever grow downward, away from everything above it. The
            // radio message stack used to live inside this same layout group
            // too (see BuildRadioStack) - it's now a fully independent,
            // bottom-anchored panel of its own for the same reason: it must
            // never depend on how tall these other cards currently are.
            float stackTop = trackMap != null ? -(10f + TrackMapSize + 20f + 14f) : -24f;
            rightStack = UiFactory.CreateResponsivePanel(transform, "Right card stack", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(RightStackWidth, 640f), new Vector2(-16f, stackTop), new Color(0f, 0f, 0f, 0f));
            ApplyPanelScale(rightStack);
            VerticalLayoutGroup layout = rightStack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UiFactory.HudCardSpacing;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            if (!compact && showCarStatus)
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
            RectTransform card = UiFactory.CreateHudCard(rightStack, "Car Status", RightStackWidth, 254f, UiFactory.AccentCyan, out carStatusAccent);

            // 2x2 tyre corner grid on the left half of the card.
            tyreFl = CreateTyreCorner(card, "FL", new Vector2(24f, -36f));
            tyreFr = CreateTyreCorner(card, "FR", new Vector2(74f, -36f));
            tyreRl = CreateTyreCorner(card, "RL", new Vector2(24f, -80f));
            tyreRr = CreateTyreCorner(card, "RR", new Vector2(74f, -80f));

            UiFactory.CreateHudLabelValueRow(card, "Compound", 30f, out tyreCompoundValue);
            MoveRowToRightHalf(tyreCompoundValue, "Compound row label", card);
            // Overflow fix: the value column here is only ~117px wide, but
            // TyreCompound.Intermediate renders as "INTERMEDIATE" (12
            // characters) at the row's normal 14pt fixed size, which overflows
            // left past the column back toward the "COMPOUND" label. Best-fit
            // shrinks just this value down as far as needed for the longest
            // compound name to still read as one line instead of bleeding
            // into the label next to it.
            tyreCompoundValue.resizeTextForBestFit = true;
            tyreCompoundValue.resizeTextMinSize = 10;
            tyreCompoundValue.resizeTextMaxSize = 14;
            UiFactory.CreateHudLabelValueRow(card, "Temp", 56f, out tyreTempValue);
            MoveRowToRightHalf(tyreTempValue, "Temp row label", card);

            // Lockup / flat spot tags: blank most of the time, so it reads as
            // empty space rather than a widget until there's something to say.
            // Layout fix (was overlapping in the wild): the lockup/flat-spot
            // tag line used to span the FULL card width at y -80..-98 - straight
            // across the RL/RR tyre boxes - and the WEAR meter sat at y 116,
            // colliding with the corner grid's labels (which extend to ~-133).
            // Tags now live in the right-hand column under COMPOUND/TEMP, and
            // every row below the grid moved down clear of it (card height
            // grew to match).
            tyreTagText = UiFactory.CreateText(card, "Tyre condition tags", "", 13, UiFactory.AccentAmber, TextAnchor.MiddleLeft);
            RectTransform tagRect = tyreTagText.GetComponent<RectTransform>();
            tagRect.anchorMin = new Vector2(0.38f, 1f);
            tagRect.anchorMax = new Vector2(1f, 1f);
            tagRect.offsetMin = new Vector2(4f, -100f);
            tagRect.offsetMax = new Vector2(-10f, -82f);

            tyreWearFill = UiFactory.CreateHudMeter(card, "Wear", 140f, UiFactory.AccentAmber, out tyreWearValue);

            UiFactory.CreateBand(card, "Car status divider", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -162f), new Vector2(-10f, -160f), new Color(1f, 1f, 1f, 0.08f));

            ersFill = UiFactory.CreateHudMeter(card, "ERS", 172f, UiFactory.AccentCyan, out ersValue);
            fuelFill = UiFactory.CreateHudMeter(card, "Fuel", 198f, new Color(0.7f, 0.95f, 1f), out fuelValue);
            damageFill = UiFactory.CreateHudMeter(card, "Dmg", 224f, UiFactory.Accent, out damageValue);
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

            Text label = UiFactory.CreateText(corner, cornerName + " label", cornerName, 13, UiFactory.TextMuted, TextAnchor.MiddleCenter);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.offsetMin = new Vector2(-10f, -17f);
            labelRect.offsetMax = new Vector2(10f, 0f);
            return image;
        }

        // Pit card fix: the old Status/Plan rows used CreateHudLabelValueRow,
        // a half-card-width single-line value column meant for short values
        // ("1:23.456") - but PitStatusText/BuildPitPlanText return full
        // sentences ("PIT LANE  DRIVING TO BOX 2  LIMITER 80",
        // "STOP 2  Lap 24  ·  MEDIUM  LATE") that silently lost their tail
        // (including the LATE flag) to that column's fixed width/height.
        // Both now run full card width and can wrap to a second line, and the
        // status line is colored to make auto-vs-manual unambiguous at a
        // glance (see UpdatePitCard) instead of only living in the wording.
        void BuildPitCard()
        {
            // Card grew (was 144 tall, then 184) to fit first the plain-
            // language phase pill (Entering pit lane / In pit lane / Box stop
            // / Pit exit) above the existing technical status/plan lines, and
            // now the cancel-manual-pit-request button below the stop-progress
            // meter - see UpdatePitCard/PitPhasePillState and the button
            // built at the bottom of this method.
            RectTransform card = UiFactory.CreateHudCard(rightStack, "Pit", RightStackWidth, 222f, UiFactory.AccentAmber);
            // Time trial has no pit stops, mandatory stop, or strategy - hide the
            // whole pit card so the right stack shows only what's relevant to a lap.
            if (race != null && race.IsTimeTrial)
            {
                card.gameObject.SetActive(false);
            }

            pitPhasePill = UiFactory.CreatePill(card, "Pit Phase", 210f, 26f);
            pitPhasePill.root.anchorMin = new Vector2(0f, 1f);
            pitPhasePill.root.anchorMax = new Vector2(0f, 1f);
            pitPhasePill.root.pivot = new Vector2(0f, 1f);
            pitPhasePill.root.anchoredPosition = new Vector2(14f, -34f);
            pitPhasePill.root.gameObject.SetActive(false);
            pitPhasePillPunch = pitPhasePill.root.gameObject.AddComponent<UiPunch>();

            pitStatusValue = UiFactory.CreateText(card, "Pit status", "", 14, UiFactory.TextPrimary, TextAnchor.UpperLeft);
            pitStatusValue.fontStyle = FontStyle.Bold;
            pitStatusValue.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform statusRect = pitStatusValue.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.offsetMin = new Vector2(14f, -102f);
            statusRect.offsetMax = new Vector2(-10f, -64f);

            // Plan: the strategy's own next lap/compound target, always
            // tagged AUTO (see BuildPitPlanText) since it fires on its own the
            // moment the window opens unless the status line above shows a
            // manual override already queued.
            pitPlanValue = UiFactory.CreateText(card, "Pit plan", "", 13, UiFactory.TextMuted, TextAnchor.UpperLeft);
            pitPlanValue.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform planRect = pitPlanValue.GetComponent<RectTransform>();
            planRect.anchorMin = new Vector2(0f, 1f);
            planRect.anchorMax = new Vector2(1f, 1f);
            planRect.offsetMin = new Vector2(14f, -134f);
            planRect.offsetMax = new Vector2(-10f, -106f);

            UiFactory.CreateBand(card, "Pit card divider", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -142f), new Vector2(-10f, -140f), new Color(1f, 1f, 1f, 0.08f));

            pitFill = UiFactory.CreateHudMeter(card, "Stop", 154f, UiFactory.AccentAmber, out pitFillValue);

            // Cancellable-manual-pit-stop button: only ever active while
            // race.CanCancelManualPitRequest() is true - a manually-queued
            // (P key or accepted SC/VSC offer) stop that hasn't yet committed.
            // Starts inactive/hidden like every other conditional card element
            // here (SC window card, qualifying card) so it contributes nothing
            // until it's actually relevant. Keyboard cancellation (the O key,
            // see PlayerVehicleInput) calls the exact same
            // RaceManager.CanCancelManualPitRequest/CancelManualPitRequest
            // pair this button's own click handler does, so the two can never
            // disagree about when cancelling is legal.
            pitCancelButton = UiFactory.CreateDangerButton(card, "Cancel Pit Request", OnPitCancelButtonClicked);
            RectTransform cancelRect = pitCancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = new Vector2(0f, 1f);
            cancelRect.anchorMax = new Vector2(1f, 1f);
            cancelRect.pivot = new Vector2(0.5f, 1f);
            cancelRect.offsetMin = new Vector2(14f, -216f);
            cancelRect.offsetMax = new Vector2(-10f, -186f);
            pitCancelButtonLabel = pitCancelButton.GetComponentInChildren<Text>();
            pitCancelButton.gameObject.SetActive(false);
        }

        void OnPitCancelButtonClicked()
        {
            // Same validation the O keyboard shortcut uses (PlayerVehicleInput)
            // - CancelManualPitRequest re-checks CanCancelManualPitRequest
            // internally regardless, so mouse and keyboard can never behave
            // differently, and a click that races a same-frame commitment (the
            // button was about to hide but the click event was already queued)
            // can never sneak through.
            race.CancelManualPitRequest();
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
            // Captured before CreateHudLabelValueRow adds more Text children
            // below - this card is reused for the Time Trial ghost delta too
            // (see UpdateQualifyingCard), so its header needs to say which.
            qualifyingCardHeader = card.GetComponentInChildren<Text>();
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
        const float RadioCardMaxHeight = 112f;
        const int RadioCardSpacing = 6;

        // Deliberately not built on CreateHudCard - that always reserves a
        // ~24px header strip for a repeated title label, which would mean
        // "RADIO" printed on every single stacked card. One small accent bar
        // plus a priority dot is enough to read as "this is the radio" once
        // several are stacked together; the message text gets the room back.
        //
        // Overlap fix: this used to be a VerticalLayoutGroup child of
        // rightStack, so a burst of up to MaxRadioCards messages plus an
        // already-tall right stack (Car Status + Pit + SC Window all visible
        // together during a safety car period) summed to well over the
        // stack's nominal height and could bleed into the track map above or
        // off the bottom of the screen.
        //
        // Second overlap fix: pinning it to the bottom of the SAME right-edge
        // column didn't actually bound anything - during a safety car the
        // downward-growing right stack (Car Status + Pit + SC Window, ~568px
        // from y=-240) and a full upward-growing radio burst (~348px from the
        // bottom) still met in the middle and rendered text over text. The
        // radio stack now lives one column inboard (left of the right-edge
        // column), where it has the vertical space to itself.
        void BuildRadioStack()
        {
            radioStackContainer = UiFactory.CreateRect(transform, "Radio stack", new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            radioStackContainer.pivot = new Vector2(1f, 0f);
            radioStackContainer.anchoredPosition = new Vector2(-16f - RightStackWidth - 12f, 16f);
            radioStackContainer.sizeDelta = new Vector2(RightStackWidth, RadioCardMinHeight);
            ApplyPanelScale(radioStackContainer);
            VerticalLayoutGroup stackLayout = UiFactory.AddVerticalLayout(radioStackContainer, RadioCardSpacing, new RectOffset(0, 0, 0, 0));
            stackLayout.childAlignment = TextAnchor.UpperRight;

            for (int i = 0; i < MaxRadioCards; i++)
            {
                RectTransform card = UiFactory.CreateRect(radioStackContainer, "Radio card " + i, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                card.sizeDelta = new Vector2(RightStackWidth, RadioCardMinHeight);
                Image background = card.gameObject.AddComponent<Image>();
                UiFactory.StyleRounded(background, UiFactory.HudCardBackground);
                background.raycastTarget = false;
                radioCardSlots[i] = card;

                // Plain solid accent (3px): the mipmapped rounded sprite reads
                // blurry at this width.
                RectTransform accent = UiFactory.CreateRect(card, "Radio accent " + i, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 8f), new Vector2(3f, -8f));
                Image accentImage = accent.gameObject.AddComponent<Image>();
                accentImage.color = UiFactory.AccentGreen;
                accentImage.raycastTarget = false;
                radioCardAccents[i] = accentImage;

                CanvasGroup group = card.gameObject.AddComponent<CanvasGroup>();
                // Decorative overlay: never let a (possibly fading/invisible)
                // radio card swallow pointer events.
                group.blocksRaycasts = false;
                group.interactable = false;
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
            hint = UiFactory.CreateText(hintBand, "Hint", hintText, 13, new Color(0.7f, 0.8f, 0.85f, 0.9f), TextAnchor.MiddleCenter);
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
            previousDisplayLapForPunch = -1;
            previousPitPhaseText = "";
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
            // HUD toggle: this module drives its own visibility every frame based
            // on whether there's an active message, so the settings toggle folds
            // into that same condition rather than gating the one-time Build call
            // (a plain SetActive(false) here would just get overwritten next tick).
            bool radioEnabled = race.Settings == null || race.Settings.Current.hudShowRadio;
            int activeCount = radioEnabled ? Mathf.Min(race.ActiveEngineerMessageCount, MaxRadioCards) : 0;
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
                // radioStackContainer is a fully independent, bottom-anchored
                // panel now (see BuildRadioStack) rather than a rightStack
                // layout child, so rebuilding rightStack here is no longer
                // needed - only this container's own internal layout matters.
                LayoutRebuilder.ForceRebuildLayoutImmediate(radioStackContainer);
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

            // ERS deploy stinger: play once on the rising edge of the player's
            // deployment (car is the player HUD subject). Non-spammy - one cue per
            // deploy start, not per frame while deploying.
            if (car != null && car.ErsDeploying && !previousErsDeploying)
            {
                SimpleAudioManager.PlayErsDeploy();
            }

            previousErsDeploying = car != null && car.ErsDeploying;

            // Rev-limiter stinger: reconstruct an audio-only normalized RPM from the
            // player's live speed + gear against the authoritative shift schedule, then
            // let the hysteresis+dwell gate fire the cue once per sustained engagement
            // (a momentary upshift touch never fires). Presentation-only - reads speed
            // and gear, changes nothing about the car.
            if (car != null)
            {
                float rpm01 = F1Game.Race.Physics.AudioRpmModel.NormalizedRpm(
                    car.CurrentSpeedKph, car.CurrentGear, VehicleController.AutoShiftUpSchedule);
                if (revLimiterGate.Update(rpm01, Time.deltaTime))
                {
                    SimpleAudioManager.PlayRevLimiter();
                }
            }

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

            UpdateFuelPill(car);

            UpdateSlipstreamPill(car);
        }

        // Slipstream HUD: only shown above a meaningful strength threshold so it
        // doesn't clutter the dash with a constantly-flickering minor effect.
        void UpdateSlipstreamPill(VehicleController car)
        {
            if (car == null || car.SlipstreamStrength < 0.2f)
            {
                if (slipstreamPill.root.gameObject.activeSelf)
                {
                    slipstreamPill.root.gameObject.SetActive(false);
                }

                return;
            }

            if (!slipstreamPill.root.gameObject.activeSelf)
            {
                slipstreamPill.root.gameObject.SetActive(true);
            }

            string sourceCode = string.IsNullOrEmpty(car.SlipstreamSourceCode) ? "" : " " + car.SlipstreamSourceCode.ToUpperInvariant();
            slipstreamPill.SetState("TOW +" + Mathf.RoundToInt(car.SlipstreamBonusKph) + " KM/H" + sourceCode, UiFactory.AccentCyan, true);
        }

        // Fuel system pass: delta-based display replacing the old flat "FUEL 35KG"
        // text - a raw kg number means nothing without knowing how many laps are
        // left, while "will this fuel actually finish the race" is exactly what a
        // driver needs at a glance. Thresholds match the design spec: >+0.25 laps
        // positive, -0.25..+0.25 target, -0.75..-0.25 warning, <-0.75 critical.
        // Two absolute-state overrides sit above the delta bands: an empty tank
        // (FuelStarved) always wins as the most urgent state, and a very low
        // absolute kg figure (regardless of delta - e.g. plenty of margin on a
        // short remaining stint but still only a splash left) still flags FUEL LOW
        // so the player never gets caught out right at the very end.
        void UpdateFuelPill(VehicleController car)
        {
            bool lit = Mathf.PingPong(Time.time * 2.4f, 1f) > 0.5f;
            if (car.FuelStarved)
            {
                fuelPill.SetState("FUEL STARVATION", UiFactory.Accent, lit);
                return;
            }

            if (car.FuelKg < 1.5f)
            {
                fuelPill.SetState("FUEL LOW", UiFactory.AccentAmber, lit);
                return;
            }

            float deltaLaps = car.ProjectedFuelDeltaLaps;
            if (deltaLaps > 0.25f)
            {
                fuelPill.SetState("FUEL +" + deltaLaps.ToString("0.0"), UiFactory.AccentGreen, false);
            }
            else if (deltaLaps >= -0.25f)
            {
                fuelPill.SetState("FUEL TARGET", UiFactory.TextMuted, false);
            }
            else if (deltaLaps >= -0.75f)
            {
                fuelPill.SetState("FUEL " + deltaLaps.ToString("0.0"), UiFactory.AccentAmber, false);
            }
            else
            {
                fuelPill.SetState("FUEL CRITICAL", UiFactory.Accent, lit);
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
            // HUD toggle: same reasoning as UpdateRadioStack above - this banner
            // decides its own visibility every frame, so the settings toggle folds
            // into that condition instead of a one-time Build-time gate.
            bool bannerEnabled = race.Settings == null || race.Settings.Current.hudShowRaceControlBanner;
            bool playerBlueFlagged = race.IsShownBlueFlag(race.PlayerParticipant);
            bool visible = bannerEnabled && (state != RaceManager.RaceControlState.Green || playerAutopilotNow || playerBlueFlagged);
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
                    // Only reached while visible is true: either the ramp tail
                    // after a safety-car restart (state already Green but the
                    // player's car is still race-control driven for a couple
                    // more seconds), or the player being shown a blue flag.
                    if (playerAutopilotNow)
                    {
                        UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                            "GREEN FLAG - POWER BUILDING", "Race control still has the car - full control in a moment", UiFactory.AccentCyan);
                    }
                    else
                    {
                        UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                            "BLUE FLAG", "Lapping car behind - let them through", UiFactory.AccentCyan);
                    }
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
                    // Distinct-states fix: this used to share AccentCyan with
                    // the calm/no-action-needed states below (the green-flag
                    // ramp tail, full-SC autopilot) even though VSC is the
                    // opposite of calm - the player is still driving
                    // themselves and must actively hold the delta or risk a
                    // pace-compliance warning (see UpdatePaceCompliancePill).
                    // Amber now groups it correctly with Yellow/SC-deploying/
                    // SC-active-manual/SC-in-this-lap: every state that
                    // requires the player to do something about it.
                    UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                        "VIRTUAL SAFETY CAR", "Hold the delta - no overtaking anywhere on track", UiFactory.AccentAmber);
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
                        int restartCountdown = Mathf.CeilToInt(race.RestartCountdownSeconds);
                        UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                            "GRID RESET - RACE RESTART", "Grid reset based on the running order when the flag came out - restarting in " + restartCountdown + "s", Color.white);
                    }
                    else
                    {
                        UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                            "RESTART", "Green flag imminent - get ready to race", Color.white);
                    }

                    break;
                case RaceManager.RaceControlState.RedFlagged:
                {
                    // UiFactory.Accent (not AccentAmber) deliberately - the
                    // game's actual red, so a red flag reads as visibly more
                    // severe than an amber caution banner.
                    int holdCountdown = Mathf.CeilToInt(race.RedFlagTimeRemaining);
                    string subtitle = holdCountdown > 0
                        ? race.RedFlagReason + " - race suspended. Restart in " + holdCountdown + "s"
                        : race.RedFlagReason + " - race suspended.";
                    UiFactory.SetStatusBannerState(raceControlBannerAccent, raceControlBannerDot, raceControlBannerTitle, raceControlBannerSubtitle,
                        "RED FLAG - RACE SUSPENDED", subtitle, UiFactory.Accent);
                    break;
                }
            }

            ApplyRaceControlBannerSize();
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

            // Tasteful punch the instant the lap number actually ticks over -
            // skipped on the very first tick (previousDisplayLapForPunch still
            // at its unset -1) so the HUD doesn't punch itself on the opening
            // frame, and gated on the animations setting like every other
            // discrete HUD motion in this file.
            if (lap.DisplayLap != previousDisplayLapForPunch)
            {
                if (previousDisplayLapForPunch >= 0 && UiFactory.AnimationsEnabled && lapCounterPunch != null)
                {
                    lapCounterPunch.Play();
                }

                previousDisplayLapForPunch = lap.DisplayLap;
            }

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

            // CompactSectorTimes drops the redundant "00:" minute prefix from
            // sub-minute sector times ("00:23.456" -> "23.456") so the full
            // three-sector line fits its single row.
            sectorRow.text = CompactSectorTimes(
                "S1 " + SectorText(1, lap.LastSector1Time) +
                "  S2 " + SectorText(2, lap.LastSector2Time) +
                "  S3 " + SectorText(3, lap.LastSector3Time) +
                "   <color=#8A98A2>NOW S" + lap.CurrentSector + " " + race.LiveSectorText(lap.CurrentSectorTime) + "</color>");

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
            // Clarity fix: this used to only ever be amber-or-red (never
            // green), so fresh tyres straight out of the pits read exactly as
            // "cautious" as tyres approaching the danger zone - no state ever
            // told the player wear was actually fine. Now a proper
            // green/amber/red progression like the damage meter below.
            bool tyreCritical = wear01 > 0.85f;
            if (tyreCritical)
            {
                bool lit = Mathf.PingPong(Time.time * 3f, 1f) > 0.5f;
                tyreWearFill.color = lit ? Color.white : UiFactory.Accent;
                tyreWearValue.color = lit ? UiFactory.Accent : UiFactory.TextPrimary;
            }
            else if (wear01 > 0.62f)
            {
                tyreWearFill.color = UiFactory.Accent;
                tyreWearValue.color = UiFactory.TextPrimary;
            }
            else if (wear01 > 0.35f)
            {
                tyreWearFill.color = UiFactory.AccentAmber;
                tyreWearValue.color = UiFactory.TextPrimary;
            }
            else
            {
                tyreWearFill.color = UiFactory.AccentGreen;
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

            // Fuel system pass: the meter fill and its low/critical thresholds now
            // scale to THIS race's own start fuel (car.StartFuelKg) and projected
            // fuel delta instead of a fixed 42kg/7kg/12kg reference tuned for the
            // old flat 35kg start - those numbers were meaningless once a race can
            // legitimately start at 6-10kg. fuelValue's text keeps kg visible
            // (spec: "keep kg available") but adds the delta-in-laps figure right
            // next to it, since that's the number that actually tells the player
            // whether the load will make it.
            float fuel01 = car.StartFuelKg > 0.1f ? Mathf.Clamp01(car.FuelKg / car.StartFuelKg) : 0f;
            UiFactory.SetMeterValueAnimated(fuelFill, fuel01);
            fuelValue.text = car.FuelKg.ToString("0.0") + "kg (" + (car.ProjectedFuelDeltaLaps >= 0f ? "+" : "") + car.ProjectedFuelDeltaLaps.ToString("0.0") + "L)";
            bool fuelCritical = car.FuelStarved || car.ProjectedFuelDeltaLaps < -0.75f;
            bool fuelGettingLow = car.ProjectedFuelDeltaLaps < -0.25f;
            if (fuelCritical)
            {
                bool lit = Mathf.PingPong(Time.time * 2.4f, 1f) > 0.5f;
                fuelFill.color = lit ? UiFactory.AccentAmber : UiFactory.Accent;
                fuelValue.color = lit ? UiFactory.Accent : UiFactory.TextPrimary;
            }
            else if (fuelGettingLow)
            {
                fuelFill.color = UiFactory.AccentAmber;
                fuelValue.color = UiFactory.TextPrimary;
            }
            else
            {
                fuelFill.color = new Color(0.7f, 0.95f, 1f);
                fuelValue.color = UiFactory.TextPrimary;
            }

            float damage01 = Mathf.Clamp01(car.Damage.OverallPercent / 100f);
            UiFactory.SetMeterValueAnimated(damageFill, damage01);
            damageValue.text = Mathf.RoundToInt(car.Damage.OverallPercent) + "%";
            // Clarity fix: this used to only ever be amber-orange-or-red
            // (never green), so a car with no damage at all read exactly as
            // "moderately damaged" as one actually approaching the critical
            // threshold. Thresholds match the damage-band notification
            // watcher below (25/55) instead of introducing a third,
            // inconsistent cutoff.
            bool damageCritical = car.Damage.OverallPercent > 55f;
            bool damageModerate = car.Damage.OverallPercent > 25f;
            if (damageCritical)
            {
                damageFill.color = Mathf.PingPong(Time.time * 3f, 1f) > 0.5f ? Color.white : UiFactory.Accent;
            }
            else if (damageModerate)
            {
                damageFill.color = new Color(1f, 0.55f, 0.1f);
            }
            else
            {
                damageFill.color = UiFactory.AccentGreen;
            }

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
            // Bug fix: this used to check car.PitLimiterActive alone, but the
            // vehicle keeps that flag set for the ENTIRE pit-lane visit (entry,
            // box, release - see RaceManager's SetPitLimiter calls throughout
            // UpdatePitEntry/UpdatePitService/UpdatePitRelease), not just the
            // two narrow windows outside the phase machine (the pit-approach
            // stretch before BeginPitEntry commits, and the brief
            // pitLimiterUntilExit tail after release resets the phase back to
            // None). That meant this line's "else" branch - the actually
            // informative race.PitStatusText with box number/timer/compound -
            // effectively never fired while the phase machine was running,
            // and the whole pit stop read as a flat "LIMITER 80" the entire
            // time. Gating on pitPhase == None restores PitStatusText for
            // Entry/Service/Release exactly as BuildPitPlanText/UpdatePitPhasePill
            // already assume it does.
            bool limiterOnly = car.PitLimiterActive && player.pitPhase == PitPhase.None;
            if (limiterOnly)
            {
                // Reflects VehicleController.PitExitFastLimiter: the cleared
                // exit stretch after release runs at a higher cap (108) than
                // the entry-grade approach limiter (80) - a hardcoded "80"
                // here would silently under-report the real speed once the
                // car is actually released and driving out.
                int limiterCapKph = car.PitExitFastLimiter ? 108 : 105;
                pitStatusValue.text = (player.pitLimiterUntilExit ? "PIT EXIT  LIMITER " : "PIT APPROACH  LIMITER ") + limiterCapKph;
            }
            else
            {
                pitStatusValue.text = race.PitStatusText(player);
            }

            // Color the headline by what's actually driving it, so "did the
            // plan just fire itself or did I just force it" reads instantly
            // instead of only living in the phrasing: cyan while the
            // strategy plan's own request is queued, purple the moment a
            // manual override (the P key, ahead of the plan) is queued
            // instead, neutral otherwise (mid pit-lane phase, nothing
            // queued yet, mandatory stop still pending, etc).
            bool requestQueued = !limiterOnly && player.vehicle != null && player.vehicle.PitRequested &&
                                  player.pitPhase == PitPhase.None && !player.isPitting;
            pitStatusValue.color = requestQueued
                ? (player.pitAutoTriggered ? UiFactory.AccentCyan : UiFactory.AccentPurple)
                : UiFactory.TextPrimary;

            pitPlanValue.text = BuildPitPlanText();
            float progress = race.PitStopProgress01(player);
            UiFactory.SetMeterValueAnimated(pitFill, progress);
            pitFillValue.text = progress > 0.001f ? Mathf.RoundToInt(progress * 100f) + "%" : "";
            UpdatePitPhasePill();
            UpdatePitCancelButton();

            // Brief cancellation confirmation: overrides the status/plan lines
            // for a few seconds right after CancelManualPitRequest runs (see
            // RaceManager.playerManualPitCancelMessageTimer), then falls back to
            // whatever the lines above already computed - never a separate,
            // parallel message queue.
            if (race.PlayerManualPitCancelMessageActive)
            {
                pitStatusValue.text = "MANUAL PIT STOP CANCELLED";
                pitStatusValue.color = UiFactory.AccentAmber;
                pitPlanValue.text = "Original strategy restored";
            }
        }

        // Cancellable-manual-pit-stop button visibility/label: shown only while
        // race.CanCancelManualPitRequest() is true, matching the exact same
        // gate PlayerVehicleInput's O key and RaceManager.CancelManualPitRequest
        // itself re-check - so the button can never be visible in a state where
        // pressing it (or O) would silently no-op, and never linger visible a
        // frame after the manual stop commits.
        void UpdatePitCancelButton()
        {
            if (pitCancelButton == null)
            {
                return;
            }

            bool canCancel = race.CanCancelManualPitRequest();
            if (canCancel != previousPitCancelVisible)
            {
                previousPitCancelVisible = canCancel;
                pitCancelButton.gameObject.SetActive(canCancel);
            }

            if (canCancel && pitCancelButtonLabel != null)
            {
                pitCancelButtonLabel.text = "CANCEL PIT REQUEST [O]";
            }
        }

        // Plain-language headline (Entering pit lane / In pit lane / Box stop /
        // Pit exit) for the player's own car, built directly from
        // RaceParticipant.pitPhase/isPitting/pitAwaitingRelease/pitEntryAligned.
        // A clearer companion to the technical PitStatusText line below it
        // (which keeps the box number/limiter/compound detail) rather than a
        // replacement for it, and reuses the same HudPill widget the DRS/ERS/
        // fuel state pills already use instead of a new UI element type.
        // Hidden entirely outside an actual pit-lane visit so it never
        // competes with the queued-request/plan messaging shown the rest of
        // the race.
        void UpdatePitPhasePill()
        {
            if (pitPhasePill == null)
            {
                return;
            }

            string phaseText = null;
            Color phaseColor = UiFactory.AccentAmber;
            switch (player.pitPhase)
            {
                case PitPhase.Entry:
                    // Distinguish "still turning into the lane" from "aligned
                    // and now rolling to the box" - same distinction
                    // RaceManager.PitStatusText already makes for its own line.
                    phaseText = "IN PIT LANE";
                    phaseColor = UiFactory.AccentAmber;
                    break;
                case PitPhase.Service:
                    phaseText = player.pitAwaitingRelease ? "BOX STOP - HOLD" : "BOX STOP";
                    phaseColor = UiFactory.Accent;
                    break;
                case PitPhase.Release:
                    phaseText = "PIT EXIT";
                    phaseColor = UiFactory.AccentCyan;
                    break;
                case PitPhase.ExitMerge:
                    phaseText = "MERGING ONTO TRACK";
                    phaseColor = UiFactory.AccentCyan;
                    break;
                case PitPhase.QualifyingReturn:
                    // Missing state fix: this phase (driving back to the pits
                    // between qualifying runs) used to fall through to the
                    // default case with nothing to distinguish it from "not
                    // pitting at all" unless isPitting also happened to be
                    // true - it now gets its own explicit label.
                    phaseText = "RETURNING TO PITS";
                    phaseColor = UiFactory.AccentCyan;
                    break;
                default:
                    // isPitting can stay true for a brief tail (limiter-until-
                    // exit) after the phase machine has already reset to
                    // None - still reads as "pit exit" instead of blanking
                    // out a beat early.
                    if (player.isPitting)
                    {
                        phaseText = "PIT EXIT";
                        phaseColor = UiFactory.AccentCyan;
                    }
                    else if (player.vehicle != null && player.vehicle.PitRequested && player.vehicle.PitLimiterActive)
                    {
                        // Missing state fix: once the car is physically inside
                        // the pit-approach stretch (limiter already engaged -
                        // see RaceManager.UpdatePitApproach/pitApproach) but
                        // hasn't yet crossed the entry line and committed to a
                        // box (RaceManager.BeginPitEntry, which is what flips
                        // pitPhase to Entry), there was no pill state at all
                        // for it - distinct from "PIT REQUESTED" below (still
                        // out on the racing line, nothing physically changed
                        // yet) and from the PitPhase.Entry case above (already
                        // committed and turning in).
                        phaseText = "APPROACHING PIT ENTRY";
                        phaseColor = UiFactory.AccentAmber;
                    }
                    else if (player.vehicle != null && player.vehicle.PitRequested)
                    {
                        // Missing state fix: a request (manual P key or the
                        // strategy's own auto-trigger) queued well before the
                        // car reaches the pit lane used to show nothing here
                        // at all - only the small technical status line below
                        // said "PIT REQUEST QUEUED"/"AUTO-PIT QUEUED". Colored
                        // the same auto-vs-manual way as that line so the two
                        // agree instead of one implying urgency the other
                        // doesn't.
                        phaseText = "PIT REQUESTED";
                        phaseColor = player.pitAutoTriggered ? UiFactory.AccentCyan : UiFactory.AccentPurple;
                    }
                    else
                    {
                        // Missing state fix: the pill previously only ever lit
                        // up once the car had physically reached the pit lane
                        // - there was no advance warning that this is the lap
                        // the strategy plan intends to box on, which lived
                        // only in small muted text below (BuildPitPlanText).
                        // Surfacing it as its own pill state gives it the same
                        // at-a-glance visibility as the other pit phases.
                        int plannedLap = race.NextPlannedPitLapFor(player);
                        int currentLap = player.lapTracker != null ? player.lapTracker.DisplayLap : 0;
                        if (!race.IsTimeTrial && race.CurrentSession != RaceWeekendSession.Qualifying && plannedLap > 0 && plannedLap == currentLap)
                        {
                            phaseText = "BOX THIS LAP";
                            phaseColor = UiFactory.AccentAmber;
                        }
                    }

                    break;
            }

            bool show = phaseText != null;
            if (pitPhasePill.root.gameObject.activeSelf != show)
            {
                pitPhasePill.root.gameObject.SetActive(show);
            }

            if (show)
            {
                pitPhasePill.SetState(phaseText, phaseColor, true);
            }

            // Brief punch whenever the plain-language phase actually changes
            // (including appearing/disappearing) so a transition like "PIT
            // REQUESTED" -> "APPROACHING PIT ENTRY" -> "ENTERING PIT LANE"
            // reads as a sequence of events instead of silent text swaps.
            string safePhaseText = phaseText ?? "";
            if (safePhaseText != previousPitPhaseText && UiFactory.AnimationsEnabled && pitPhasePillPunch != null)
            {
                pitPhasePillPunch.Play();
            }

            previousPitPhaseText = safePhaseText;
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
                return "<color=#6CFF8D>STOPS DONE</color>";
            }

            TyreCompound compound = race.NextPlannedPitCompoundFor(player);
            int currentLap = player.lapTracker != null ? player.lapTracker.DisplayLap : 1;
            string status = currentLap > nextLap ? "  <color=#FFC85C>LATE</color>" : "";
            string stopLabel = race.GetPlannedStopCount() >= 2 ? (player.pitStops == 0 ? "STOP 1  " : "STOP 2  ") : "";
            // AUTO tag: this plan fires on its own once the window opens
            // (UpdatePlayerAutoPitStrategy in RaceManager) unless the player
            // overrides it early - the status line above is what actually
            // reports a manual override once one is queued.
            string autoTag = "<color=#" + ColorUtility.ToHtmlStringRGB(UiFactory.AccentCyan) + ">AUTO</color>  ";
            return autoTag + stopLabel + "Lap " + nextLap + "  ·  " + compound.ToString().ToUpperInvariant() + status;
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
            bool qualifying = race.CurrentSession == RaceWeekendSession.Qualifying;
            bool timeTrialGhost = race.IsTimeTrial;
            bool show = qualifying || timeTrialGhost;
            if (qualifyingCard.activeSelf != show)
            {
                qualifyingCard.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            // Reused card (Time Trial ghost delta vs qualifying delta) - only
            // rewrite the header text on an actual mode change, not every
            // frame, to avoid needless Text layout churn.
            string wantedHeader = qualifying ? "QUALIFYING" : "GHOST";
            if (qualifyingCardHeader != null && qualifyingCardHeader.text != wantedHeader)
            {
                qualifyingCardHeader.text = wantedHeader;
            }

            qualifyingDeltaValue.text = qualifying ? race.QualifyingDeltaText(player) : race.GhostDeltaText(player);
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
                // Stewarding depth: trackLimitEventLog's most recent entry (see
                // RaceManager.HandleTrackLimits) carries the lap/sector this
                // specific warning happened in - appended here rather than
                // duplicating that formatting logic on the RaceManager side too.
                string detail = player.trackLimitEventLog.Count > 0 ? " (" + player.trackLimitEventLog[player.trackLimitEventLog.Count - 1] + ")" : "";
                PushNotification("TRACK LIMITS WARNING " + player.trackLimitWarnings + "/3" + detail, UiFactory.AccentAmber);
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
            for (int i = 0; i < NotificationSlotCount; i++)
            {
                UpdateNotificationSlot(i);
            }
        }

        void UpdateNotificationSlot(int slot)
        {
            if (notificationPhases[slot] == 0)
            {
                if (notificationQueue.Count == 0)
                {
                    return;
                }

                HudNotification next = notificationQueue.Dequeue();
                notificationTexts[slot].text = next.text;
                notificationAccents[slot].color = next.color;
                notificationPhases[slot] = 1;
                notificationTimers[slot] = 0f;
            }

            notificationTimers[slot] += Time.deltaTime;
            CanvasGroup group = notificationGroups[slot];
            if (notificationPhases[slot] == 1)
            {
                group.alpha = Mathf.Clamp01(notificationTimers[slot] / 0.25f);
                if (notificationTimers[slot] >= 0.25f)
                {
                    notificationPhases[slot] = 2;
                    notificationTimers[slot] = 0f;
                }
            }
            else if (notificationPhases[slot] == 2)
            {
                group.alpha = 1f;
                if (notificationTimers[slot] >= 2.4f)
                {
                    notificationPhases[slot] = 3;
                    notificationTimers[slot] = 0f;
                }
            }
            else if (notificationPhases[slot] == 3)
            {
                group.alpha = 1f - Mathf.Clamp01(notificationTimers[slot] / 0.4f);
                if (notificationTimers[slot] >= 0.4f)
                {
                    notificationPhases[slot] = 0;
                    group.alpha = 0f;
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
                // RaceControlIncidentCount and friends are the genuine
                // race-control-only counts - never RaceManager.IncidentCount,
                // which is a raw internal diagnostics counter that can read
                // in the hundreds and isn't meaningful to show anywhere.
                "RACE CONTROL  YEL " + race.YellowFlagEventCount + "  VSC " + race.VirtualSafetyCarEventCount + "  PEN " + race.PenaltyEventCount + "  TOTAL " + race.RaceControlIncidentCount + "\n" +
                "CONTACT  CAR " + race.CarContactIncidentCount + "  SOLO " + race.SoloContactIncidentCount + "\n" +
                "Press R to reset to track";
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
            towerLaps[index] = CreateTowerCell(row, "Tower lap " + index, 104f, 136f, 13, TextAnchor.MiddleLeft, new Color(0.7f, 0.78f, 0.83f), false);
            towerGaps[index] = CreateTowerCell(row, "Tower gap " + index, 140f, 222f, 13, TextAnchor.MiddleLeft, new Color(0.85f, 0.91f, 0.94f), false);
            towerIntervals[index] = CreateTowerCell(row, "Tower interval " + index, 226f, 292f, 13, TextAnchor.MiddleLeft, new Color(0.85f, 0.91f, 0.94f), false);

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
            // Overflow rather than the CreateText default of Wrap: every
            // tower cell is a single fixed-height row, so wrapped text would
            // just get vertically truncated back down to unreadable anyway -
            // letting a rare too-wide value spill a few pixels past its
            // column reads better than silently eating characters.
            cell.horizontalOverflow = HorizontalWrapMode.Overflow;
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
                SetTowerHeaderMode(true, false);
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

            SetTowerHeaderMode(false, false);
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
            SetTowerHeaderMode(false, true);
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

        // Switches between the three header/column shapes the tower can show:
        // Time Trial's single centered label, race mode's POS/DVR/T/LAP/GAP/INT
        // columns, and qualifying's POS/DVR/BEST/GAP columns (BEST needs real
        // width - see SetTowerLayoutForQualifying). Centralized here instead of
        // scattered SetActive/text calls so the header and every row always
        // agree on which shape is current.
        void SetTowerHeaderMode(bool timeTrial, bool qualifying)
        {
            if (tower.gameObject.activeSelf != timeTrial)
            {
                tower.gameObject.SetActive(timeTrial);
            }

            if (towerHeaderRow.gameObject.activeSelf == timeTrial)
            {
                towerHeaderRow.gameObject.SetActive(!timeTrial);
            }

            SetTowerLayoutForQualifying(qualifying);
        }

        // Qualifying's "BEST" column carries a full lap time ("01:23.456", ~9
        // characters) instead of race mode's 2-digit lap counter, so it needs
        // real width - the tyre swatch and DRS dot are meaningless in
        // qualifying anyway, so that reclaimed space is handed to BEST/GAP
        // instead of squeezing a full time string into a 32px column built
        // for "12". Clipping fix: previously every session reused the exact
        // same narrow rect, silently truncating the qualifying time.
        void SetTowerLayoutForQualifying(bool qualifying)
        {
            if (towerQualifyingLayout == qualifying)
            {
                return;
            }

            towerQualifyingLayout = qualifying;

            float lapMinX = qualifying ? 88f : 104f;
            float lapMaxX = qualifying ? 208f : 136f;
            float gapMinX = qualifying ? 212f : 140f;
            float gapMaxX = qualifying ? 300f : 222f;

            SetCellRectX(towerHeaderLap, lapMinX, lapMaxX);
            SetCellRectX(towerHeaderGap, gapMinX, gapMaxX);
            towerHeaderLap.text = qualifying ? "BEST" : "LAP";
            towerHeaderInterval.gameObject.SetActive(!qualifying);

            for (int i = 0; i < TowerRowCount; i++)
            {
                SetCellRectX(towerLaps[i], lapMinX, lapMaxX);
                SetCellRectX(towerGaps[i], gapMinX, gapMaxX);
                towerTyres[i].gameObject.SetActive(!qualifying);
                if (towerDrsDots[i] != null)
                {
                    towerDrsDots[i].gameObject.SetActive(!qualifying);
                }
            }
        }

        void SetCellRectX(Text cell, float minX, float maxX)
        {
            RectTransform rect = cell.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(minX, rect.offsetMin.y);
            rect.offsetMax = new Vector2(maxX, rect.offsetMax.y);
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

        // Strips the redundant "00:" minute prefix from sub-minute times in a
        // composed rich-text line (sector times are almost always under a
        // minute; times of 1min+ keep their full form).
        static string CompactSectorTimes(string line)
        {
            return System.Text.RegularExpressions.Regex.Replace(line, @"\b00:(\d\d\.\d\d\d)", "$1");
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

        // Career identity fix: delegates to RaceManager.GetDisplayDriverCode, the
        // one canonical driver-code resolver, instead of keeping a second
        // hand-rolled fallback here that could drift out of sync with it (the old
        // fallback stripped spaces and took the first three characters of the
        // whole name - "Oscar Piastri" -> "OSC" - instead of the real
        // last-name-token convention "PIA").
        string DriverCode(RaceParticipant participant)
        {
            if (participant == null || race == null)
            {
                return "---";
            }

            return race.GetDisplayDriverCode(participant.driverData, participant.driverName);
        }
    }
}
