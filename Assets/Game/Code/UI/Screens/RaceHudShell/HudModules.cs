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

    /// <summary>Session identity: "RACE · Italy-style Speed GP".</summary>
    public sealed class SessionLabelModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            if (!t.Valid)
            {
                value.text = "";
                return;
            }

            string kind;
            switch (t.Session)
            {
                case F1Game.Core.Events.SessionKind.Qualifying:
                    kind = Localization.Get("session.qualifying", "QUALIFYING");
                    break;
                case F1Game.Core.Events.SessionKind.TimeTrial:
                    kind = Localization.Get("session.time_trial", "TIME TRIAL");
                    break;
                case F1Game.Core.Events.SessionKind.Practice:
                    kind = Localization.Get("session.practice", "PRACTICE");
                    break;
                default:
                    kind = Localization.Get("session.race", "RACE");
                    break;
            }

            value.text = string.IsNullOrEmpty(t.EventName) ? kind : kind + " <size=75%>· " + t.EventName + "</size>";
        }
    }

    /// <summary>The race layer's rolling one-line status message.</summary>
    public sealed class SessionMessageModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            bool show = t.Valid && !string.IsNullOrEmpty(t.SessionMessage);
            if (value.gameObject.activeSelf != show)
            {
                value.gameObject.SetActive(show);
            }

            if (show)
            {
                value.text = t.SessionMessage;
            }
        }
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
            if (lap != null) lap.text = t.Valid ? $"{Localization.Get("hud.lap", "LAP")} {t.Lap} / {t.TotalLaps}" : "--";
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
            if (speed != null)
            {
                // Honour the units setting, matching the legacy HUD's mph readout.
                float displaySpeed = t.UseMphUnits ? t.SpeedKph * 0.621371f : t.SpeedKph;
                string unit = t.UseMphUnits ? "MPH" : "KPH";
                speed.text = $"{Mathf.RoundToInt(displaySpeed)}<size=45%> {unit}</size>";
            }
            if (rpm != null)
            {
                // Redline tint as RPM approaches the limit; healthy revs stay on
                // the cool telemetry colour so red always means "shift now".
                Color c = t.Rpm01 > 0.9f ? UiTheme.Active.palette.danger
                    : (t.Rpm01 > 0.75f ? UiTheme.Active.palette.warning : UiTheme.Active.palette.energy);
                rpm.SetValue(t.Rpm01, c);
            }
        }
    }

    /// <summary>ERS battery (bar + percentage readout) + DRS state chip.</summary>
    public sealed class ErsDrsModule : HudModule
    {
        [SerializeField] UiProgressBar ers;
        [SerializeField] StatusChip drs;
        [SerializeField] TMP_Text ersValue;
        public void Bind(UiProgressBar ersBar, StatusChip drsChip, TMP_Text ersValueText)
        {
            ers = ersBar;
            drs = drsChip;
            ersValue = ersValueText;
        }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (!t.Valid) return;

            // Race-control lockout: deployment is disabled under any
            // caution (FlagRules), so the meter dims to say why the
            // button does nothing.
            bool locked = t.PaceLimited || t.Flag == F1Game.Core.Events.FlagState.Yellow;
            Color barColor = locked ? UiTheme.Active.palette.textMuted : UiTheme.Active.palette.energy;
            if (ers != null)
            {
                ers.SetValue(t.Ers01, barColor);
            }

            if (ersValue != null)
            {
                ersValue.text = $"<mspace=0.6em>{t.Ers01 * 100f:0}</mspace>%";
                ersValue.color = locked ? UiTheme.Active.palette.textMuted
                    : (t.Ers01 < 0.15f ? UiTheme.Active.palette.warning : UiTheme.Active.palette.textPrimary);
            }

            if (drs != null)
            {
                if (t.DrsActive) drs.Set("DRS OPEN", StatusChip.Tone.Positive);
                else if (t.DrsAvailable) drs.Set("DRS READY", StatusChip.Tone.Accent);
                else drs.Set("DRS", StatusChip.Tone.Neutral);
            }
        }
    }

    /// <summary>Tyre compound + wear meter (with remaining-life %), temperature status and lockup warning.</summary>
    public sealed class TyresModule : HudModule
    {
        static readonly string[] CompoundLabels = { "S", "M", "H", "I", "W" };
        [SerializeField] StatusChip compound;
        [SerializeField] UiProgressBar wear;
        [SerializeField] TMP_Text wearValue;
        [SerializeField] TMP_Text temp;
        public void Bind(StatusChip compoundChip, UiProgressBar wearBar, TMP_Text wearValueText, TMP_Text tempText)
        {
            compound = compoundChip;
            wear = wearBar;
            wearValue = wearValueText;
            temp = tempText;
        }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (!t.Valid) return;
            float remaining01 = Mathf.Clamp01(1f - t.TyreWear01);
            if (compound != null)
            {
                int i = Mathf.Clamp(t.TyreCompound, 0, CompoundLabels.Length - 1);
                compound.SetCustom(CompoundLabels[i], CompoundPalette.For(i));
            }

            if (wear != null)
            {
                wear.SetDepletion(remaining01); // remaining life
            }

            if (wearValue != null)
            {
                wearValue.text = $"<mspace=0.6em>{remaining01 * 100f:0}</mspace>%";
                wearValue.color = remaining01 > 0.25f ? UiTheme.Active.palette.textPrimary : UiTheme.Active.palette.warning;
            }

            if (temp != null)
            {
                // A heavy lockup outranks the temperature readout - it's the
                // more urgent thing to react to.
                if (t.LockupSeverity > 0.35f)
                {
                    temp.text = $"<color=#{ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.danger)}>LOCKUP</color>";
                }
                else
                {
                    string status = string.IsNullOrEmpty(t.TyreTempStatus) ? "OPT" : t.TyreTempStatus;
                    Color c = status == "HOT" || status == "COLD"
                        ? UiTheme.Active.palette.warning
                        : (status == "OPT" ? UiTheme.Active.palette.positive : UiTheme.Active.palette.textMuted);
                    temp.text = $"TYRE TEMP <color=#{ColorUtility.ToHtmlStringRGB(c)}>{status}</color>";
                }
            }
        }
    }

    /// <summary>
    /// Delta to the reference lap (qualifying pole or time-trial ghost),
    /// green when up, red when down. Hidden when no reference applies.
    /// </summary>
    public sealed class DeltaModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            bool show = t.Valid && t.HasDelta;
            if (value.gameObject.activeSelf != show)
            {
                value.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            string color = ColorUtility.ToHtmlStringRGB(
                t.DeltaSeconds <= 0f ? UiTheme.Active.palette.positive : UiTheme.Active.palette.danger);
            value.text = $"DELTA <color=#{color}>{(t.DeltaSeconds >= 0f ? "+" : "")}{t.DeltaSeconds:0.000}</color>";
        }
    }

    /// <summary>Slipstream tow pill: strength + the code of the car being followed.</summary>
    public sealed class SlipstreamModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            // Slipstream is a secondary readout - hidden in compact HUD mode.
            bool show = t.Valid && !t.CompactHud && t.SlipstreamStrength > 0.05f;
            if (value.gameObject.activeSelf != show)
            {
                value.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            string source = string.IsNullOrEmpty(t.SlipstreamSourceCode) ? "" : " " + t.SlipstreamSourceCode;
            string color = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.accent);
            value.text = $"<color=#{color}>TOW +{t.SlipstreamBonusKph:0}</color><size=75%>{source}</size>";
        }
    }

    /// <summary>Throttle / brake pedal bars (each a labelled row).</summary>
    public sealed class InputTelemetryModule : HudModule
    {
        [SerializeField] UiProgressBar throttle;
        [SerializeField] UiProgressBar brake;
        [SerializeField] GameObject throttleRow;
        [SerializeField] GameObject brakeRow;
        public void Bind(UiProgressBar throttleBar, UiProgressBar brakeBar, GameObject throttleRowGo, GameObject brakeRowGo)
        {
            throttle = throttleBar;
            brake = brakeBar;
            throttleRow = throttleRowGo;
            brakeRow = brakeRowGo;
        }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (!t.Valid) return;
            // Pedal bars are a secondary readout - hidden in compact HUD mode.
            // The row containers carry the captions, so toggle those.
            bool show = !t.CompactHud;
            if (throttleRow != null && throttleRow.activeSelf != show) throttleRow.SetActive(show);
            if (brakeRow != null && brakeRow.activeSelf != show) brakeRow.SetActive(show);
            if (!show) return;

            if (throttle != null) throttle.SetValue(t.Throttle01, UiTheme.Active.palette.positive);
            if (brake != null) brake.SetValue(t.Brake01, UiTheme.Active.palette.danger);
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
            if (!t.Valid)
            {
                value.text = "--";
                return;
            }

            // Emergency states outrank the plain readout (parity with the
            // legacy fuel pill's state machine).
            if (t.FuelStarved)
            {
                value.text = "<color=#E64B3C>FUEL STARVATION</color>";
            }
            else if (t.FuelLapsRemaining > 0f && t.FuelLapsRemaining < 1f)
            {
                value.text = $"<color=#E64B3C>FUEL CRITICAL <mspace=0.6em>{t.FuelLapsRemaining:0.0}</mspace></color>";
            }
            else if (t.FuelLapsRemaining > 0f && t.FuelLapsRemaining < 2.5f)
            {
                value.text = $"<color=#F2B33D>FUEL LOW <mspace=0.6em>{t.FuelLapsRemaining:0.0}</mspace></color>";
            }
            else
            {
                value.text = $"FUEL <mspace=0.6em>{t.FuelLapsRemaining:0.0}</mspace> laps";
            }
        }
    }

    /// <summary>
    /// Race-control banner: the two-line caution surface (state + restart
    /// countdown, then red-flag reason / SC queue slot / pace cap). Hides
    /// itself entirely under ordinary green-flag running - the one-word
    /// FlagModule chip covers that.
    /// </summary>
    public sealed class RaceControlModule : HudModule
    {
        [SerializeField] TMP_Text body;
        public void Bind(TMP_Text bodyText) { body = bodyText; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (body == null) return;
            bool caution = t.Valid &&
                (t.Flag != F1Game.Core.Events.FlagState.Green || t.PaceLimited || t.RestartCountdownSeconds > 0f);
            if (body.gameObject.activeSelf != caution)
            {
                body.gameObject.SetActive(caution);
            }

            if (!caution)
            {
                return;
            }

            string headline;
            switch (t.Flag)
            {
                case F1Game.Core.Events.FlagState.Yellow:
                case F1Game.Core.Events.FlagState.DoubleYellow:
                    headline = "YELLOW FLAG - NO OVERTAKING";
                    break;
                case F1Game.Core.Events.FlagState.VirtualSafetyCar:
                    headline = "VIRTUAL SAFETY CAR";
                    break;
                case F1Game.Core.Events.FlagState.SafetyCar:
                    headline = t.RestartCountdownSeconds > 0f
                        ? $"RESTART IN {Mathf.CeilToInt(t.RestartCountdownSeconds)}"
                        : "SAFETY CAR DEPLOYED";
                    break;
                case F1Game.Core.Events.FlagState.Red:
                    headline = "RED FLAG - SESSION SUSPENDED";
                    break;
                default:
                    headline = t.PaceLimited ? "RACE CONTROL - HOLD PACE" : "GREEN FLAG";
                    break;
            }

            string detail = t.RaceControlDetail ?? "";
            if (t.PaceLimited && t.PaceCapKph < 900f)
            {
                string cap = $"KEEP UNDER {Mathf.RoundToInt(t.PaceCapKph)} KPH";
                detail = string.IsNullOrEmpty(detail) ? cap : detail + " · " + cap;
            }

            Color tone = t.Flag == F1Game.Core.Events.FlagState.Red
                ? UiTheme.Active.palette.danger
                : UiTheme.Active.palette.warning;
            body.text = string.IsNullOrEmpty(detail)
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(tone)}>{headline}</color>"
                : $"<color=#{ColorUtility.ToHtmlStringRGB(tone)}>{headline}</color>\n<size=75%>{detail}</size>";
        }
    }

    /// <summary>Overall damage meter with the legacy thresholds' urgency tints.</summary>
    public sealed class DamageModule : HudModule
    {
        [SerializeField] UiProgressBar bar;
        [SerializeField] TMP_Text label;
        public void Bind(UiProgressBar damageBar, TMP_Text labelText) { bar = damageBar; label = labelText; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (!t.Valid) return;
            if (label != null)
            {
                label.text = t.Damage01 > 0.005f ? $"DAMAGE <mspace=0.6em>{t.Damage01 * 100f:0}</mspace>%" : "NO DAMAGE";
            }

            if (bar != null)
            {
                Color c = t.Damage01 > 0.6f ? UiTheme.Active.palette.danger
                    : (t.Damage01 > 0.25f ? UiTheme.Active.palette.warning : UiTheme.Active.palette.positive);
                bar.SetValue(t.Damage01, c);
            }
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
                    chip.Set("YELLOW FLAG", StatusChip.Tone.Warning);
                    break;
                case F1Game.Core.Events.FlagState.VirtualSafetyCar:
                    chip.Set("VIRTUAL SAFETY CAR", StatusChip.Tone.Warning);
                    break;
                case F1Game.Core.Events.FlagState.SafetyCar:
                    chip.Set("SAFETY CAR", StatusChip.Tone.Warning);
                    break;
                case F1Game.Core.Events.FlagState.Red:
                    chip.Set("RED FLAG", StatusChip.Tone.Danger);
                    break;
                case F1Game.Core.Events.FlagState.Chequered:
                    chip.Set("CHEQUERED FLAG", StatusChip.Tone.Positive);
                    break;
                default:
                    chip.Set("GREEN FLAG", StatusChip.Tone.Positive);
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

            if (t.PitStopProgress01 > 0f)
            {
                chip.Set($"PIT STOP {t.PitStopProgress01 * 100f:0}%", StatusChip.Tone.Accent);
            }
            else if (t.IsPitting)
            {
                chip.Set("IN PIT LANE", StatusChip.Tone.Accent);
            }
            else if (t.PitLimiterActive)
            {
                chip.Set("PIT LIMITER", StatusChip.Tone.Accent);
            }
            else if (t.PenaltySeconds > 0.01f)
            {
                chip.Set($"PENALTY +{t.PenaltySeconds:0}s", StatusChip.Tone.Danger);
            }
            else
            {
                chip.Set("NO PENALTY", StatusChip.Tone.Neutral);
            }
        }
    }

    /// <summary>Lap times: current (struck when invalid last), last, best, session best.</summary>
    public sealed class TimesModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        static string Format(float seconds)
        {
            if (seconds <= 0f)
            {
                return "-:--.---";
            }

            int minutes = (int)(seconds / 60f);
            float rest = seconds - minutes * 60f;
            return $"{minutes}:{rest:00.000}";
        }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            if (!t.Valid)
            {
                value.text = "--";
                return;
            }

            string last = Format(t.LastLapSeconds);
            if (t.LastLapInvalidated && t.LastLapSeconds > 0f)
            {
                last = "<s>" + last + "</s>";
            }

            value.text = $"<mspace=0.62em>{Format(t.CurrentLapSeconds)}</mspace>\n" +
                         $"<size=75%>LAST <mspace=0.62em>{last}</mspace>\n" +
                         $"BEST <mspace=0.62em>{Format(t.BestLapSeconds)}</mspace>   " +
                         $"SB <mspace=0.62em>{Format(t.SessionBestSeconds)}</mspace></size>";
        }
    }

    /// <summary>
    /// The one interactive HUD control: cancels an uncommitted manual/SC pit
    /// request. Visible only while the request can still be cancelled
    /// (snapshot CanCancelPit, which mirrors RaceManager.CanCancelManualPitRequest);
    /// the click routes through HudCommands back to the race layer, where the
    /// same eligibility gate makes a late click a safe no-op.
    /// </summary>
    public sealed class CancelPitButtonModule : HudModule
    {
        [SerializeField] ThemedButton button;

        public void Bind(ThemedButton cancelButton)
        {
            button = cancelButton;
            if (button != null)
            {
                button.Clicked += HudCommands.RequestCancelPit;
                button.gameObject.SetActive(false);
            }
        }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (button == null) return;
            bool show = t.Valid && t.CanCancelPit;
            if (button.gameObject.activeSelf != show)
            {
                button.gameObject.SetActive(show);
            }
        }
    }

    /// <summary>
    /// Pit strategy line: the plan (next stop lap + compound), escalating to
    /// BOX THIS LAP, an SC-window prompt, or the live request state. Hidden
    /// when no plan or request exists.
    /// </summary>
    public sealed class PitStrategyModule : HudModule
    {
        static readonly string[] CompoundLabels = { "Soft", "Medium", "Hard", "Inter", "Wet" };
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            bool show = t.Valid && !t.IsPitting &&
                (t.PitRequested || t.NextPlannedPitLap > 0 || t.ScPitWindowOpen);
            if (value.gameObject.activeSelf != show)
            {
                value.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            int compound = Mathf.Clamp(t.NextPlannedPitCompound, 0, CompoundLabels.Length - 1);
            string compoundLabel = CompoundLabels[compound];
            string warning = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.warning);
            if (t.ScPitWindowOpen)
            {
                value.text = $"<color=#{warning}>SC WINDOW - BOX NOW?</color> <size=80%>{compoundLabel} ready</size>";
            }
            else if (t.PitRequested)
            {
                value.text = $"<color=#{warning}>BOX CONFIRMED</color> <size=80%>{compoundLabel} ready</size>";
            }
            else if (t.BoxThisLap)
            {
                value.text = $"<color=#{warning}>BOX THIS LAP</color> <size=80%>{compoundLabel}</size>";
            }
            else
            {
                value.text = $"PIT PLAN <size=80%>lap {t.NextPlannedPitLap} · {compoundLabel}</size>";
            }
        }
    }

    /// <summary>Sector times, tinted green when matching the session's best sector.</summary>
    public sealed class SectorsModule : HudModule
    {
        [SerializeField] TMP_Text value;
        readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            if (!t.Valid)
            {
                value.text = "--";
                return;
            }

            string best = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.positive);
            sb.Length = 0;
            Append("S1", t.Sector1Seconds, t.BestSector1Seconds, best);
            sb.Append("  ");
            Append("S2", t.Sector2Seconds, t.BestSector2Seconds, best);
            sb.Append("  ");
            Append("S3", t.Sector3Seconds, t.BestSector3Seconds, best);
            value.text = sb.ToString();
        }

        void Append(string label, float seconds, float bestSeconds, string bestColor)
        {
            sb.Append("<size=75%>").Append(label).Append("</size> ");
            if (seconds <= 0f)
            {
                sb.Append("--.-");
                return;
            }

            bool isBest = bestSeconds > 0f && seconds <= bestSeconds + 0.001f;
            if (isBest) sb.Append("<color=#").Append(bestColor).Append(">");
            sb.Append("<mspace=0.62em>").Append(seconds.ToString("0.0")).Append("</mspace>");
            if (isBest) sb.Append("</color>");
        }
    }

    /// <summary>Time-trial checkpoint progress ("CHECKPOINTS 7/16"); TT only.</summary>
    public sealed class CheckpointsModule : HudModule
    {
        const int CheckpointTotal = 16;
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            bool show = t.Valid && t.Session == F1Game.Core.Events.SessionKind.TimeTrial;
            if (value.gameObject.activeSelf != show)
            {
                value.gameObject.SetActive(show);
            }

            if (show)
            {
                value.text = $"CHECKPOINTS <mspace=0.62em>{t.CheckpointsPassed}/{CheckpointTotal}</mspace>";
            }
        }
    }

    /// <summary>Qualifying lap feedback, a centred panel shown after a Q lap.</summary>
    public sealed class QualiFeedbackModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            bool show = t.Valid && t.Session == F1Game.Core.Events.SessionKind.Qualifying &&
                !string.IsNullOrEmpty(t.QualifyingFeedback);
            if (value.gameObject.activeSelf != show)
            {
                value.gameObject.SetActive(show);
            }

            if (show)
            {
                value.text = t.QualifyingFeedback;
            }
        }
    }

    /// <summary>Live weather conditions chip.</summary>
    public sealed class WeatherModule : HudModule
    {
        [SerializeField] StatusChip chip;
        public void Bind(StatusChip weatherChip) { chip = weatherChip; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (chip == null) return;
            if (!t.Valid)
            {
                chip.Set("--", StatusChip.Tone.Neutral);
                return;
            }

            switch (t.Weather)
            {
                case F1Game.Core.Events.WeatherKind.Overcast:
                    chip.Set("WEATHER · OVERCAST", StatusChip.Tone.Neutral);
                    break;
                case F1Game.Core.Events.WeatherKind.LightRain:
                    chip.Set("WEATHER · LIGHT RAIN", StatusChip.Tone.Accent);
                    break;
                case F1Game.Core.Events.WeatherKind.HeavyRain:
                    chip.Set("WEATHER · HEAVY RAIN", StatusChip.Tone.Warning);
                    break;
                default:
                    chip.Set("WEATHER · CLEAR", StatusChip.Tone.Neutral);
                    break;
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
        // Show the whole field (up to the full 22-car grid), per request - the
        // tower used to cap at the top 10 plus the player's row.
        const int VisibleRows = 22;

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

        static readonly string[] CompoundLetters = { "S", "M", "H", "I", "W" };

        void AppendRow(in HudRaceOrderEntry e, string accent, string muted)
        {
            if (e.IsPlayer)
            {
                sb.Append("<color=#").Append(accent).Append(">");
            }

            sb.Append("P").Append(e.Position.ToString("00")).Append(' ').Append(e.Code);

            // Per-row compound letter in its data-identity colour.
            int compound = e.Compound < 0 ? 0 : (e.Compound >= CompoundLetters.Length ? CompoundLetters.Length - 1 : e.Compound);
            sb.Append(" <color=#").Append(ColorUtility.ToHtmlStringRGB(CompoundPalette.For(compound)))
              .Append(">").Append(CompoundLetters[compound]).Append("</color>");

            // Broadcast DRS dot when the wing is open.
            if (e.DrsActive)
            {
                sb.Append(" <color=#").Append(accent).Append(">•</color>");
            }

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
                // Interval to the car ahead (broadcast convention), same slot
                // the legacy tower's INT column used.
                sb.Append("  +").Append(e.IntervalSeconds.ToString("0.0"));
            }

            if (e.IsPlayer)
            {
                sb.Append("</color>");
            }

            sb.Append('\n');
        }
    }

    /// <summary>
    /// Big-moment centre flash: LIGHTS OUT, GREEN FLAG, FINAL LAP. Purely
    /// UI-side edge detection over the telemetry snapshot it already reads -
    /// no new events, no race-layer change. Fades out over a short window.
    /// </summary>
    public sealed class BigMomentModule : HudModule
    {
        const float FlashSeconds = 2f;

        [SerializeField] TMP_Text value;
        bool prevLightsVisible;
        bool prevValid;
        int prevLap = -1;
        F1Game.Core.Events.FlagState prevFlag = F1Game.Core.Events.FlagState.Green;
        float flashTimer;

        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;

            if (t.Valid)
            {
                // Lights out: the start gantry was showing and just went dark.
                if (prevValid && prevLightsVisible && !t.StartLightsVisible &&
                    t.Session == F1Game.Core.Events.SessionKind.Race)
                {
                    Trigger(F1Game.Core.Localization.Get("hud.moment.lights_out", "LIGHTS OUT"), UiTheme.Active.palette.positive);
                }
                // Green flag: a caution just cleared.
                else if (prevValid && prevFlag != F1Game.Core.Events.FlagState.Green &&
                         t.Flag == F1Game.Core.Events.FlagState.Green &&
                         prevFlag != F1Game.Core.Events.FlagState.Chequered)
                {
                    Trigger(F1Game.Core.Localization.Get("hud.moment.green_flag", "GREEN FLAG"), UiTheme.Active.palette.positive);
                }
                // Final lap: entering the last lap of a race.
                else if (prevValid && t.TotalLaps > 0 && t.Lap == t.TotalLaps && prevLap == t.TotalLaps - 1 &&
                         t.Session == F1Game.Core.Events.SessionKind.Race)
                {
                    Trigger(F1Game.Core.Localization.Get("hud.moment.final_lap", "FINAL LAP"), UiTheme.Active.palette.warning);
                }

                prevLightsVisible = t.StartLightsVisible;
                prevFlag = t.Flag;
                prevLap = t.Lap;
            }

            prevValid = t.Valid;

            if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                Color c = value.color;
                c.a = Mathf.Clamp01(flashTimer / FlashSeconds);
                value.color = c;
                if (flashTimer <= 0f && value.gameObject.activeSelf)
                {
                    value.gameObject.SetActive(false);
                }
            }
        }

        void Trigger(string text, Color color)
        {
            value.text = text;
            color.a = 1f;
            value.color = color;
            flashTimer = FlashSeconds;
            if (!value.gameObject.activeSelf)
            {
                value.gameObject.SetActive(true);
            }
        }
    }

    /// <summary>
    /// The five start lights, top-center like the real gantry: lamps build up
    /// red one by one, hold all-lit, then the whole row visibly goes DARK for a
    /// beat at lights-out (instead of vanishing the same frame the race goes
    /// live) so the launch moment is unmissable.
    /// </summary>
    public sealed class StartLightsModule : HudModule
    {
        const float LightsOutHoldSeconds = 1.4f;
        const string LitColor = "#E63329";
        const string UnlitColor = "#3A3F46";

        [SerializeField] TMP_Text value;
        readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(96);
        bool wasShowing;
        int shownLit = -1;   // lamps drawn last frame (-1 = nothing drawn)
        float holdTimer;

        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;

            bool sequence = t.Valid && t.StartLightsVisible;

            // Falling edge while the snapshot is still valid = lights out: keep
            // the gantry up briefly, all lamps dark. (An invalid snapshot means
            // teardown, not a start - hide immediately.)
            if (!sequence && wasShowing && t.Valid)
            {
                holdTimer = LightsOutHoldSeconds;
            }

            wasShowing = sequence;
            if (!t.Valid)
            {
                holdTimer = 0f;
            }
            else if (!sequence && holdTimer > 0f)
            {
                holdTimer -= Time.deltaTime;
            }

            bool show = sequence || holdTimer > 0f;
            if (value.gameObject.activeSelf != show)
            {
                value.gameObject.SetActive(show);
            }

            if (!show)
            {
                shownLit = -1;
                return;
            }

            int lit = sequence ? Mathf.Clamp(t.StartLightCount, 0, 5) : 0;
            if (lit == shownLit)
            {
                return; // string only changes when a lamp does
            }

            shownLit = lit;
            sb.Length = 0;
            for (int i = 0; i < 5; i++)
            {
                sb.Append("<color=").Append(i < lit ? LitColor : UnlitColor).Append(">●</color>");
                if (i < 4) sb.Append(' ');
            }

            value.text = sb.ToString();
        }
    }

    /// <summary>
    /// Pit-status headline (Tier 2 parity): box number / limiter cap / queued
    /// request / cancel confirmation, tinted by the race layer's emphasis. Hidden
    /// when the line is empty (qualifying / idle time trial).
    /// </summary>
    public sealed class PitStatusModule : HudModule
    {
        [SerializeField] TMP_Text value;
        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;
            bool show = t.Valid && !string.IsNullOrEmpty(t.PitStatusText);
            if (value.gameObject.activeSelf != show)
            {
                value.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            value.text = t.PitStatusText;
            value.color = EmphasisColor(t.PitStatusEmphasis);
        }

        static Color EmphasisColor(F1Game.Core.Events.PitEmphasis emphasis)
        {
            // Mirrors the legacy pit card's intent with the production palette:
            // an auto-fired plan reads positive, a manual override reads accent,
            // a cancel confirmation reads warning, everything else neutral.
            switch (emphasis)
            {
                case F1Game.Core.Events.PitEmphasis.Auto:
                    return UiTheme.Active.palette.positive;
                case F1Game.Core.Events.PitEmphasis.Manual:
                    return UiTheme.Active.palette.accent;
                case F1Game.Core.Events.PitEmphasis.Cancelled:
                    return UiTheme.Active.palette.warning;
                default:
                    return UiTheme.Active.palette.textPrimary;
            }
        }
    }

    /// <summary>
    /// Track-limit warning flash (Tier 4 parity): a brief amber pulse when the
    /// player's warning count rises, detected UI-side over the snapshot count
    /// (the same edge-detection shape as BigMomentModule).
    /// </summary>
    public sealed class TrackLimitFlashModule : HudModule
    {
        const float FlashSeconds = 2.2f;

        [SerializeField] TMP_Text value;
        int seenWarnings = -1;
        float flashTimer;

        public void Bind(TMP_Text v) { value = v; }

        protected override void Render(in HudTelemetrySnapshot t)
        {
            if (value == null) return;

            if (t.Valid)
            {
                // First valid frame seeds the baseline so a mid-session HUD spawn
                // (or a count carried over) never flashes spuriously.
                if (seenWarnings < 0)
                {
                    seenWarnings = t.TrackLimitWarnings;
                }
                else if (t.TrackLimitWarnings > seenWarnings)
                {
                    seenWarnings = t.TrackLimitWarnings;
                    flashTimer = FlashSeconds;
                    value.text = F1Game.Core.Localization.Get("hud.moment.track_limits", "TRACK LIMITS");
                    if (!value.gameObject.activeSelf)
                    {
                        value.gameObject.SetActive(true);
                    }
                }
            }

            if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                bool lit = Mathf.PingPong(Time.time * 4f, 1f) > 0.5f;
                Color c = lit ? UiTheme.Active.palette.warning : UiTheme.Active.palette.accent;
                value.color = c;
                if (flashTimer <= 0f && value.gameObject.activeSelf)
                {
                    value.gameObject.SetActive(false);
                }
            }
        }
    }
}
