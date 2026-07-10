using F1Game.Core.Events;
using F1Game.UI.Navigation;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.RaceHudShell
{
    /// <summary>
    /// Race HUD shell: the layout container the HUD widgets dock into, plus the
    /// event-driven modules that need no per-frame polling (flag state chip and
    /// the pooled notification feed). Per-frame telemetry widgets (speed, gear,
    /// timing tower) attach into the docks as they are migrated off the legacy
    /// RaceHud — this shell is the destination architecture, not a re-skin.
    /// </summary>
    public class HudRoot : ScreenView
    {
        public const string Id = "race-hud-shell";

        [SerializeField] RectTransform topLeftDock;
        [SerializeField] RectTransform topRightDock;
        [SerializeField] RectTransform timingTowerDock;
        [SerializeField] RectTransform bottomCenterDock;
        [SerializeField] RectTransform bottomRightDock;
        [SerializeField] StatusChip flagChip;
        [SerializeField] NotificationFeed notificationFeed;

        public RectTransform TopLeftDock => topLeftDock;
        public RectTransform TopRightDock => topRightDock;
        public RectTransform TimingTowerDock => timingTowerDock;
        public RectTransform BottomCenterDock => bottomCenterDock;
        public RectTransform BottomRightDock => bottomRightDock;
        public NotificationFeed Feed => notificationFeed;

        public void Bind(
            RectTransform topLeft, RectTransform topRight, RectTransform timingTower,
            RectTransform bottomCenter, RectTransform bottomRight,
            StatusChip flag, NotificationFeed feed)
        {
            topLeftDock = topLeft;
            topRightDock = topRight;
            timingTowerDock = timingTower;
            bottomCenterDock = bottomCenter;
            bottomRightDock = bottomRight;
            flagChip = flag;
            notificationFeed = feed;
            SetScreenId(Id);
        }

        public override void OnEntered(object args)
        {
            GameEvents.Subscribe<FlagChangedEvent>(OnFlagChanged);
            if (flagChip != null)
            {
                flagChip.Set("GREEN", StatusChip.Tone.Positive);
            }
        }

        public override void OnExited()
        {
            GameEvents.Unsubscribe<FlagChangedEvent>(OnFlagChanged);
        }

        void OnFlagChanged(FlagChangedEvent evt)
        {
            if (flagChip == null)
            {
                return;
            }

            switch (evt.Current)
            {
                case FlagState.Green:
                    flagChip.Set("GREEN", StatusChip.Tone.Positive);
                    break;
                case FlagState.Yellow:
                case FlagState.DoubleYellow:
                    flagChip.Set("YELLOW", StatusChip.Tone.Warning);
                    break;
                case FlagState.VirtualSafetyCar:
                    flagChip.Set("VSC", StatusChip.Tone.Warning);
                    break;
                case FlagState.SafetyCar:
                    flagChip.Set("SAFETY CAR", StatusChip.Tone.Warning);
                    break;
                case FlagState.Red:
                    flagChip.Set("RED FLAG", StatusChip.Tone.Danger);
                    break;
                case FlagState.Blue:
                    flagChip.Set("BLUE", StatusChip.Tone.Accent);
                    break;
                case FlagState.Chequered:
                    flagChip.Set("CHEQUERED", StatusChip.Tone.Neutral);
                    break;
            }
        }
    }
}
