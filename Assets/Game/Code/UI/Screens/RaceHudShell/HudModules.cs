using F1Game.Core;
using F1Game.UI.Screens;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.RaceHudShell
{
    /// <summary>
    /// Base for a telemetry-driven HUD module. Modules read the per-frame
    /// <see cref="HudTelemetry"/> snapshot (no polling of the race manager) and
    /// render with TMP. Discrete state (flags, notifications) is handled by the
    /// event-driven widgets on <see cref="HudRoot"/>.
    /// </summary>
    public abstract class HudModule : MonoBehaviour
    {
        protected void Update()
        {
            HudTelemetrySnapshot t = HudTelemetry.Current;
            Render(t);
        }

        protected abstract void Render(in HudTelemetrySnapshot t);
    }

    /// <summary>Position "P3 / 22".</summary>
    public sealed class PositionModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            value.text = t.Valid ? $"P{t.Position}<size=55%>  / {t.FieldSize}</size>" : "--";
        }
    }

    /// <summary>Lap counter "LAP 12 / 58" + session clock.</summary>
    public sealed class LapClockModule : HudModule
    {
        [SerializeField] TMP_Text lap;
        [SerializeField] TMP_Text clock;
        public void Bind(TMP_Text lapText, TMP_Text clockText) { lap = lapText; clock = clockText; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (lap != null) lap.text = t.Valid ? $"LAP {t.Lap} / {t.TotalLaps}" : "--";
            if (clock != null)
            {
                int total = Mathf.Max(0, Mathf.FloorToInt(t.SessionClockSeconds));
                clock.text = $"{total / 60:00}:{total % 60:00}";
            }
        }
    }

    /// <summary>Speed + gear hero readout.</summary>
    public sealed class SpeedGearModule : HudModule
    {
        [SerializeField] TMP_Text gear;
        [SerializeField] TMP_Text speed;
        [SerializeField] UiProgressBar rpm;
        public void Bind(TMP_Text gearText, TMP_Text speedText, UiProgressBar rpmBar) { gear = gearText; speed = speedText; rpm = rpmBar; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (!t.Valid) return;
            if (gear != null) gear.text = t.Gear < 0 ? "R" : (t.Gear == 0 ? "N" : t.Gear.ToString());
            if (speed != null) speed.text = $"{Mathf.RoundToInt(t.SpeedKph)}<size=45%> KPH</size>";
            if (rpm != null)
            {
                // Redline tint as RPM approaches the limit.
                Color c = t.Rpm01 > 0.9f ? UiTheme.Active.palette.danger
                    : (t.Rpm01 > 0.75f ? UiTheme.Active.palette.warning : UiTheme.Active.palette.accent);
                rpm.SetValue(t.Rpm01, c);
            }
        }
    }

    /// <summary>ERS battery + DRS state chips.</summary>
    public sealed class ErsDrsModule : HudModule
    {
        [SerializeField] UiProgressBar ers;
        [SerializeField] StatusChip drs;
        public void Bind(UiProgressBar ersBar, StatusChip drsChip) { ers = ersBar; drs = drsChip; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (!t.Valid) return;
            if (ers != null) ers.SetValue(t.Ers01, UiTheme.Active.palette.accent);
            if (drs != null)
            {
                if (t.DrsActive) drs.Set("DRS", StatusChip.Tone.Positive);
                else if (t.DrsAvailable) drs.Set("DRS", StatusChip.Tone.Accent);
                else drs.Set("DRS", StatusChip.Tone.Neutral);
            }
        }
    }

    /// <summary>Tyre compound + wear meter.</summary>
    public sealed class TyresModule : HudModule
    {
        static readonly string[] CompoundLabels = { "S", "M", "H", "I", "W" };
        [SerializeField] StatusChip compound;
        [SerializeField] UiProgressBar wear;
        public void Bind(StatusChip compoundChip, UiProgressBar wearBar) { compound = compoundChip; wear = wearBar; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (!t.Valid) return;
            if (compound != null)
            {
                int i = Mathf.Clamp(t.TyreCompound, 0, CompoundLabels.Length - 1);
                compound.SetCustom(CompoundLabels[i], CompoundPalette.For(i));
            }

            if (wear != null)
            {
                wear.SetDepletion(1f - t.TyreWear01); // remaining life
            }
        }
    }

    /// <summary>Fuel remaining, in laps.</summary>
    public sealed class FuelModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            value.text = t.Valid ? $"FUEL <mspace=0.6em>{t.FuelLapsRemaining:0.0}</mspace> laps" : "--";
        }
    }
}
