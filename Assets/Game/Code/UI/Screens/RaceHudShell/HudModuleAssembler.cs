using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1Game.UI.Screens.RaceHudShell
{
    /// <summary>
    /// Assembles the telemetry-driven HUD modules into a <see cref="HudRoot"/>'s
    /// docks. Kept separate from HudRoot so the module set can grow without
    /// touching the shell. Widgets are built from theme tokens (no magic pixels).
    /// Every meter in the status column carries a text label and a numeric value
    /// so a bar is never an anonymous coloured stripe.
    /// </summary>
    public static class HudModuleAssembler
    {
        public static void Populate(HudRoot hud)
        {
            if (hud == null)
            {
                return;
            }

            // Top-left: session identity, then position + lap/clock.
            var sessionText = Text(hud.TopLeftDock, "Session", 16f, TextAlignmentOptions.Left);
            hud.gameObject.AddComponent<SessionLabelModule>().Bind(sessionText);
            var posValue = Text(hud.TopLeftDock, "Position", 44f, TextAlignmentOptions.Left);
            hud.gameObject.AddComponent<PositionModule>().Bind(posValue);
            var lapText = Text(hud.TopLeftDock, "Lap", 20f, TextAlignmentOptions.Left);
            var clockText = Numeric(hud.TopLeftDock, "Clock", 22f);
            hud.gameObject.AddComponent<LapClockModule>().Bind(lapText, clockText);

            // Bottom-center: speed + gear + rpm.
            var gearText = Text(hud.BottomCenterDock, "Gear", 64f, TextAlignmentOptions.Center);
            var speedText = Numeric(hud.BottomCenterDock, "Speed", 26f);
            var rpmBar = ProgressBar(hud.BottomCenterDock, "Rpm");
            hud.gameObject.AddComponent<SpeedGearModule>().Bind(gearText, speedText, rpmBar);

            // ---- Top-right status column ----
            // The shell's flag chip already sits first in this dock; the module
            // drives it so there is exactly ONE flag readout.
            hud.gameObject.AddComponent<FlagModule>().Bind(hud.FlagChip);

            // ERS: label + charge bar + percentage, so remaining deployment
            // energy is always readable at a glance.
            var ersRow = Row(hud.TopRightDock, "Ers");
            Label(ersRow, "ERS", 52f);
            var ersBar = ProgressBar(ersRow, "Ers");
            StretchInRow(ersBar.gameObject);
            var ersValue = RowValue(ersRow, "ErsValue", 64f);
            var drsChip = Chip(hud.TopRightDock, "Drs");
            hud.gameObject.AddComponent<ErsDrsModule>().Bind(ersBar, drsChip, ersValue);

            // Tyres: compound chip + labelled wear bar + remaining-life %.
            var tyreRow = Row(hud.TopRightDock, "Tyre");
            var compoundChip = Chip(tyreRow, "Compound");
            var compoundLayout = compoundChip.GetComponent<LayoutElement>();
            compoundLayout.preferredWidth = 34f;
            compoundLayout.minWidth = 34f;
            Label(tyreRow, "TYRE", 52f);
            var wearBar = ProgressBar(tyreRow, "Wear");
            StretchInRow(wearBar.gameObject);
            var wearValue = RowValue(tyreRow, "WearValue", 64f);
            var tyreTempText = Numeric(hud.TopRightDock, "TyreTemp", 16f);
            hud.gameObject.AddComponent<TyresModule>().Bind(compoundChip, wearBar, wearValue, tyreTempText);

            var fuelText = Numeric(hud.TopRightDock, "Fuel", 18f);
            hud.gameObject.AddComponent<FuelModule>().Bind(fuelText);

            // Damage: label + meter on one line.
            var damageRow = Row(hud.TopRightDock, "Damage");
            var damageLabel = RowValue(damageRow, "DamageLabel", 118f);
            damageLabel.fontSize = 16f;
            damageLabel.alignment = TextAlignmentOptions.MidlineLeft;
            var damageBar = ProgressBar(damageRow, "DamageBar");
            StretchInRow(damageBar.gameObject);
            hud.gameObject.AddComponent<DamageModule>().Bind(damageBar, damageLabel);

            var pitChip = Chip(hud.TopRightDock, "PitPenalty");
            hud.gameObject.AddComponent<PitPenaltyModule>().Bind(pitChip);
            var weatherChip = Chip(hud.TopRightDock, "Weather");
            hud.gameObject.AddComponent<WeatherModule>().Bind(weatherChip);

            // Bottom-center: pedal input bars beneath the speed/gear readout,
            // then the slipstream tow pill.
            var throttleBar = ProgressBar(hud.BottomCenterDock, "Throttle");
            var brakeBar = ProgressBar(hud.BottomCenterDock, "Brake");
            hud.gameObject.AddComponent<InputTelemetryModule>().Bind(throttleBar, brakeBar);
            var slipstreamText = Numeric(hud.BottomCenterDock, "Slipstream", 18f);
            hud.gameObject.AddComponent<SlipstreamModule>().Bind(slipstreamText);

            // Qualifying / time-trial delta card in the top-left stack.
            var deltaText = Numeric(hud.TopLeftDock, "Delta", 20f);
            hud.gameObject.AddComponent<DeltaModule>().Bind(deltaText);
            var checkpointsText = Numeric(hud.TopLeftDock, "Checkpoints", 16f);
            hud.gameObject.AddComponent<CheckpointsModule>().Bind(checkpointsText);

            // Track minimap: a fixed square panel in the bottom-right dock; the
            // module positions pooled dots within its rect.
            var mapContainer = Minimap(hud.BottomRightDock);
            hud.gameObject.AddComponent<MinimapModule>().Bind(mapContainer);

            // Top-left continues with the relative gaps under the lap/clock,
            // then the lap-time block (current/last/best/session best).
            var gapsText = Numeric(hud.TopLeftDock, "Gaps", 18f);
            hud.gameObject.AddComponent<GapsModule>().Bind(gapsText);
            var timesText = Numeric(hud.TopLeftDock, "Times", 22f);
            timesText.GetComponent<LayoutElement>().preferredHeight = 72f;
            hud.gameObject.AddComponent<TimesModule>().Bind(timesText);
            var sectorsText = Numeric(hud.TopLeftDock, "Sectors", 18f);
            hud.gameObject.AddComponent<SectorsModule>().Bind(sectorsText);

            // Pit-status headline (box/limiter/queued/cancel) above the pit plan.
            var pitStatusText = Numeric(hud.TopLeftDock, "PitStatus", 18f);
            hud.gameObject.AddComponent<PitStatusModule>().Bind(pitStatusText);

            var pitPlanText = Numeric(hud.TopLeftDock, "PitPlan", 18f);
            hud.gameObject.AddComponent<PitStrategyModule>().Bind(pitPlanText);

            // The one interactive HUD control: cancel an uncommitted pit request.
            var cancelPitButton = F1Game.UI.UiScreenFactory.CreateButton(
                hud.TopLeftDock, "Btn_CancelPit", ThemedButton.Variant.Destructive, "Cancel Pit Request",
                UiTheme.Active.components.buttonHeightCompact);
            hud.gameObject.AddComponent<CancelPitButtonModule>().Bind(cancelPitButton);

            var sessionMessageText = Text(hud.TopLeftDock, "SessionMessage", 16f, TextAlignmentOptions.Left);
            hud.gameObject.AddComponent<SessionMessageModule>().Bind(sessionMessageText);

            // ---- Top-center stage ----
            // Start-light gantry front and centre: five big lamps that build up
            // red one by one, hold, then visibly go dark at lights-out.
            var lightsText = Text(hud.TopCenterDock, "StartLights", 64f, TextAlignmentOptions.Center);
            lightsText.GetComponent<LayoutElement>().preferredHeight = 76f;
            hud.gameObject.AddComponent<StartLightsModule>().Bind(lightsText);

            // Big-moment flash (LIGHTS OUT / GREEN FLAG / FINAL LAP) directly
            // beneath the gantry, so "lights out" appears where the lights died.
            var bigMomentText = Text(hud.TopCenterDock, "BigMoment", 40f, TextAlignmentOptions.Center);
            bigMomentText.GetComponent<LayoutElement>().preferredHeight = 60f;
            hud.gameObject.AddComponent<BigMomentModule>().Bind(bigMomentText);

            // Track-limit warning flash: a brief amber pulse under the big-moment line.
            var trackLimitText = Text(hud.TopCenterDock, "TrackLimit", 22f, TextAlignmentOptions.Center);
            trackLimitText.GetComponent<LayoutElement>().preferredHeight = 34f;
            hud.gameObject.AddComponent<TrackLimitFlashModule>().Bind(trackLimitText);

            // Race-control banner (safety car / restart / red flag detail); it
            // hides itself under ordinary green-flag running.
            var raceControlText = Text(hud.TopCenterDock, "RaceControl", 22f, TextAlignmentOptions.Center);
            raceControlText.GetComponent<LayoutElement>().preferredHeight = 52f;
            hud.gameObject.AddComponent<RaceControlModule>().Bind(raceControlText);

            // Qualifying lap feedback: a centred panel on the same stage.
            var qualiFeedbackText = Text(hud.TopCenterDock, "QualiFeedback", 24f, TextAlignmentOptions.Center);
            qualiFeedbackText.GetComponent<LayoutElement>().preferredHeight = 60f;
            hud.gameObject.AddComponent<QualiFeedbackModule>().Bind(qualiFeedbackText);

            // Timing tower body: one multi-line tabular text, refreshed at the
            // publish cadence rather than per frame (mid-left dock).
            var towerText = Numeric(hud.TimingTowerDock, "TimingTower", 17f);
            towerText.alignment = TextAlignmentOptions.TopLeft;
            // Multi-line body: the shared Text() helper sizes for one line.
            towerText.GetComponent<LayoutElement>().preferredHeight = 260f;
            hud.gameObject.AddComponent<TimingTowerModule>().Bind(towerText);
        }

        static TMP_Text Text(Transform parent, string name, float size, TextAlignmentOptions align)
        {
            var go = new GameObject("Hud" + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = UiTheme.Active.palette.textPrimary;
            if (UiTheme.Active.typography.semiBold != null) tmp.font = UiTheme.Active.typography.semiBold;
            go.AddComponent<LayoutElement>().preferredHeight = size + 6f;
            return tmp;
        }

        static TMP_Text Numeric(Transform parent, string name, float size)
        {
            TMP_Text t = Text(parent, name, size, TextAlignmentOptions.Left);
            if (UiTheme.Active.typography.tabularNumeric != null) t.font = UiTheme.Active.typography.tabularNumeric;
            return t;
        }

        // A horizontal "label + meter + value" line for the status column.
        static RectTransform Row(Transform parent, string name)
        {
            var go = new GameObject("HudRow" + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = UiTheme.Active.spacing.small;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            go.AddComponent<LayoutElement>().preferredHeight = 30f;
            return (RectTransform)go.transform;
        }

        // Small muted caption naming the meter beside it ("ERS", "TYRE").
        static TMP_Text Label(Transform row, string caption, float width)
        {
            TMP_Text t = Text(row, "Label" + caption, 14f, TextAlignmentOptions.MidlineLeft);
            t.text = caption;
            t.color = UiTheme.Active.palette.textMuted;
            var layout = t.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.minWidth = width;
            layout.preferredHeight = 20f;
            return t;
        }

        // Numeric readout slot at the end of a row ("78%").
        static TMP_Text RowValue(Transform row, string name, float width)
        {
            TMP_Text t = Numeric(row, name, 15f);
            t.alignment = TextAlignmentOptions.MidlineRight;
            var layout = t.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.minWidth = width;
            layout.preferredHeight = 20f;
            return t;
        }

        // Let a row child (the meter) absorb the remaining row width.
        static void StretchInRow(GameObject meter)
        {
            var layout = meter.GetComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.minWidth = 60f;
        }

        static UiProgressBar ProgressBar(Transform parent, string name)
        {
            var go = new GameObject("HudBar" + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 10f;

            var trackImg = go.AddComponent<Image>();
            trackImg.color = UiTheme.Active.palette.surface;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(go.transform, false);
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = UiTheme.Active.palette.accent;

            var bar = go.AddComponent<UiProgressBar>();
            bar.Bind(fillImg, trackImg);
            return bar;
        }

        static StatusChip Chip(Transform parent, string name)
        {
            var go = new GameObject("HudChip" + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 30f;
            var bg = go.AddComponent<Image>();

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)labelGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 16f;
            label.color = UiTheme.Active.palette.textPrimary;

            var chip = go.AddComponent<StatusChip>();
            chip.Bind(label, bg);
            return chip;
        }

        // Fixed square panel the MinimapModule positions its pooled dots inside.
        static RectTransform Minimap(Transform parent)
        {
            const float Size = 180f;
            var go = new GameObject("HudMinimap", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            Color surface = UiTheme.Active.palette.surface;
            surface.a = 0.55f;
            bg.color = surface;
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = Size;
            layout.preferredHeight = Size;
            layout.minWidth = Size;
            layout.minHeight = Size;
            return (RectTransform)go.transform;
        }
    }
}
