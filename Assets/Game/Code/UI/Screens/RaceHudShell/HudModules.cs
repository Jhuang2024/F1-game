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

    /// <summary>Relative gaps to the cars immediately ahead and behind.</summary>
    public sealed class GapsModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            if (!t.Valid)
            {
                value.text = "--";
                return;
            }

            string ahead = t.HasGapAhead ? $"+{t.GapAheadSeconds:0.0}" : "---";
            string behind = t.HasGapBehind ? $"-{t.GapBehindSeconds:0.0}" : "---";
            value.text = $"▲ <mspace=0.6em>{ahead}</mspace>   ▼ <mspace=0.6em>{behind}</mspace>";
        }
    }

    /// <summary>Current flag shown to the player (blue outranks the display of green).</summary>
    public sealed class FlagModule : HudModule
    {
        [SerializeField] StatusChip chip;
        public void Bind(StatusChip flagChip) { chip = flagChip; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (chip == null) return;
            if (!t.Valid)
            {
                chip.Set("--", StatusChip.Tone.Neutral);
                return;
            }

            if (t.BlueFlag)
            {
                chip.Set("BLUE FLAG", StatusChip.Tone.Accent);
                return;
            }

            switch (t.Flag)
            {
                case F1Game.Core.Events.FlagState.Yellow:
                case F1Game.Core.Events.FlagState.DoubleYellow:
                    chip.Set("YELLOW", StatusChip.Tone.Warning);
                    break;
                case F1Game.Core.Events.FlagState.VirtualSafetyCar:
                    chip.Set("VSC", StatusChip.Tone.Warning);
                    break;
                case F1Game.Core.Events.FlagState.SafetyCar:
                    chip.Set("SAFETY CAR", StatusChip.Tone.Warning);
                    break;
                case F1Game.Core.Events.FlagState.Red:
                    chip.Set("RED FLAG", StatusChip.Tone.Danger);
                    break;
                case F1Game.Core.Events.FlagState.Chequered:
                    chip.Set("FINISH", StatusChip.Tone.Positive);
                    break;
                default:
                    chip.Set("GREEN", StatusChip.Tone.Positive);
                    break;
            }
        }
    }

    /// <summary>Pit limiter / in-pit state and accumulated time penalties.</summary>
    public sealed class PitPenaltyModule : HudModule
    {
        [SerializeField] StatusChip chip;
        public void Bind(StatusChip statusChip) { chip = statusChip; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (chip == null) return;
            if (!t.Valid)
            {
                chip.Set("--", StatusChip.Tone.Neutral);
                return;
            }

            if (t.IsPitting)
            {
                chip.Set("IN PIT", StatusChip.Tone.Accent);
            }
            else if (t.PitLimiterActive)
            {
                chip.Set("LIMITER", StatusChip.Tone.Accent);
            }
            else if (t.PenaltySeconds > 0.01f)
            {
                chip.Set($"+{t.PenaltySeconds:0}s", StatusChip.Tone.Danger);
            }
            else
            {
                chip.Set("NO PEN", StatusChip.Tone.Neutral);
            }
        }
    }

    /// <summary>
    /// Timing tower: the top of the running order plus the player's own row
    /// when outside it, refreshed at the cadence the race layer publishes
    /// (HudRaceOrder) rather than per frame.
    /// </summary>
    public sealed class TimingTowerModule : MonoBehaviour
    {
        const int VisibleRows = 10;

        [SerializeField] TMP_Text body;
        readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(512);
        float refreshTimer;

        public void Bind(TMP_Text bodyText) { body = bodyText; }

        void Update()
        {
            refreshTimer -= UnityEngine.Time.deltaTime;
            if (refreshTimer > 0f)
            {
                return;
            }

            refreshTimer = 0.25f;
            Render();
        }

        void Render()
        {
            if (body == null)
            {
                return;
            }

            int count = HudRaceOrder.Count;
            if (count == 0)
            {
                if (body.gameObject.activeSelf)
                {
                    body.gameObject.SetActive(false);
                }

                return;
            }

            if (!body.gameObject.activeSelf)
            {
                body.gameObject.SetActive(true);
            }

            string accent = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.accent);
            string muted = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.textMuted);
            sb.Length = 0;
            int shown = count < VisibleRows ? count : VisibleRows;
            for (int i = 0; i < shown; i++)
            {
                AppendRow(HudRaceOrder.Entries[i], accent, muted);
            }

            // Player outside the visible window: append their row after a gap
            // marker so the tower always answers "where am I".
            for (int i = shown; i < count; i++)
            {
                if (HudRaceOrder.Entries[i].IsPlayer)
                {
                    sb.Append("<color=#").Append(muted).Append(">…</color>\n");
                    AppendRow(HudRaceOrder.Entries[i], accent, muted);
                    break;
                }
            }

            body.text = sb.ToString();
        }

        void AppendRow(in HudRaceOrderEntry e, string accent, string muted)
        {
            if (e.IsPlayer)
            {
                sb.Append("<color=#").Append(accent).Append(">");
            }

            sb.Append("P").Append(e.Position.ToString("00")).Append(' ').Append(e.Code);
            if (e.Retired)
            {
                sb.Append("  <color=#").Append(muted).Append(">OUT</color>");
            }
            else if (e.InPit)
            {
                sb.Append("  <color=#").Append(muted).Append(">PIT</color>");
            }
            else if (e.Position > 1)
            {
                sb.Append("  +").Append(e.GapToLeaderSeconds.ToString("0.0"));
            }

            if (e.IsPlayer)
            {
                sb.Append("</color>");
            }

            sb.Append('\n');
        }
    }

    /// <summary>The five start lights, drawn as filled/hollow dots during the sequence.</summary>
    public sealed class StartLightsModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            bool show = t.Valid && t.StartLightsVisible;
            if (value.gameObject.activeSelf != show)
            {
                value.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            var sb = new System.Text.StringBuilder(24);
            sb.Append("<color=#E63329>");
            for (int i = 0; i < 5; i++)
            {
                sb.Append(i < t.StartLightCount ? '●' : '○');
                if (i < 4) sb.Append(' ');
            }

            sb.Append("</color>");
            value.text = sb.ToString();
        }
    }
}
