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

            // Bottom-right column already hosts the notification feed; add
            // ERS/DRS, tyres and fuel to the top-right dock.
            var ersBar = ProgressBar(hud.TopRightDock, "Ers");
            var drsChip = Chip(hud.TopRightDock, "Drs");
            hud.gameObject.AddComponent<ErsDrsModule>().Bind(ersBar, drsChip);

            var compoundChip = Chip(hud.TopRightDock, "Compound");
            var wearBar = ProgressBar(hud.TopRightDock, "Wear");
            var tyreTempText = Numeric(hud.TopRightDock, "TyreTemp", 16f);
            hud.gameObject.AddComponent<TyresModule>().Bind(compoundChip, wearBar, tyreTempText);

            var fuelText = Numeric(hud.TopRightDock, "Fuel", 18f);
            hud.gameObject.AddComponent<FuelModule>().Bind(fuelText);

            var damageLabel = Numeric(hud.TopRightDock, "Damage", 16f);
            var damageBar = ProgressBar(hud.TopRightDock, "DamageBar");
            hud.gameObject.AddComponent<DamageModule>().Bind(damageBar, damageLabel);

            // Bottom-center: pedal input bars beneath the speed/gear readout.
            var throttleBar = ProgressBar(hud.BottomCenterDock, "Throttle");
            var brakeBar = ProgressBar(hud.BottomCenterDock, "Brake");
            hud.gameObject.AddComponent<InputTelemetryModule>().Bind(throttleBar, brakeBar);

            // Top-left continues with the relative gaps under the lap/clock,
            // then the lap-time block (current/last/best/session best).
            var gapsText = Numeric(hud.TopLeftDock, "Gaps", 18f);
            hud.gameObject.AddComponent<GapsModule>().Bind(gapsText);
            var timesText = Numeric(hud.TopLeftDock, "Times", 22f);
            timesText.GetComponent<LayoutElement>().preferredHeight = 72f;
            hud.gameObject.AddComponent<TimesModule>().Bind(timesText);
            var sectorsText = Numeric(hud.TopLeftDock, "Sectors", 18f);
            hud.gameObject.AddComponent<SectorsModule>().Bind(sectorsText);

            var pitPlanText = Numeric(hud.TopLeftDock, "PitPlan", 18f);
            hud.gameObject.AddComponent<PitStrategyModule>().Bind(pitPlanText);

            // The one interactive HUD control: cancel an uncommitted pit request.
            var cancelPitButton = F1Game.UI.UiScreenFactory.CreateButton(
                hud.TopLeftDock, "Btn_CancelPit", ThemedButton.Variant.Destructive, "Cancel Pit Request",
                UiTheme.Active.components.buttonHeightCompact);
            hud.gameObject.AddComponent<CancelPitButtonModule>().Bind(cancelPitButton);

            var sessionMessageText = Text(hud.TopLeftDock, "SessionMessage", 16f, TextAlignmentOptions.Left);
            hud.gameObject.AddComponent<SessionMessageModule>().Bind(sessionMessageText);

            // Flag + pit/penalty chips live with the other status chips.
            var flagChip = Chip(hud.TopRightDock, "Flag");
            hud.gameObject.AddComponent<FlagModule>().Bind(flagChip);
            var pitChip = Chip(hud.TopRightDock, "PitPenalty");
            hud.gameObject.AddComponent<PitPenaltyModule>().Bind(pitChip);
            var weatherChip = Chip(hud.TopRightDock, "Weather");
            hud.gameObject.AddComponent<WeatherModule>().Bind(weatherChip);

            // Race-control banner sits above the tower in the top-center dock;
            // it hides itself under ordinary green-flag running.
            var raceControlText = Text(hud.TimingTowerDock, "RaceControl", 22f, TextAlignmentOptions.Center);
            raceControlText.GetComponent<LayoutElement>().preferredHeight = 52f;
            hud.gameObject.AddComponent<RaceControlModule>().Bind(raceControlText);

            // Start lights sit in the timing-tower dock (top-center of the
            // shell) so they read like the gantry; the module hides itself
            // outside the build-up sequence.
            var lightsText = Text(hud.TimingTowerDock, "StartLights", 34f, TextAlignmentOptions.Center);
            hud.gameObject.AddComponent<StartLightsModule>().Bind(lightsText);

            // Timing tower body: one multi-line tabular text, refreshed at the
            // publish cadence rather than per frame.
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
    }
}
