using F1Game.UI.Navigation;
using F1Game.UI.Widgets;
using UnityEngine;

namespace F1Game.UI.Screens.RaceHudShell
{
    /// <summary>
    /// Race HUD shell: the layout container the HUD widgets dock into, plus the
    /// pooled notification feed. Per-frame telemetry widgets (speed, gear,
    /// timing tower) attach into the docks — this shell is the destination
    /// architecture, not a re-skin. The flag chip is owned here but driven by
    /// the telemetry-polling FlagModule, so exactly one flag surface exists.
    /// </summary>
    public class HudRoot : ScreenView
    {
        public const string Id = "race-hud-shell";

        [SerializeField] RectTransform topLeftDock;
        [SerializeField] RectTransform topCenterDock;
        [SerializeField] RectTransform topRightDock;
        [SerializeField] RectTransform timingTowerDock;
        [SerializeField] RectTransform bottomCenterDock;
        [SerializeField] RectTransform bottomRightDock;
        [SerializeField] StatusChip flagChip;
        [SerializeField] NotificationFeed notificationFeed;

        public RectTransform TopLeftDock => topLeftDock;
        public RectTransform TopCenterDock => topCenterDock;
        public RectTransform TopRightDock => topRightDock;
        public RectTransform TimingTowerDock => timingTowerDock;
        public RectTransform BottomCenterDock => bottomCenterDock;
        public RectTransform BottomRightDock => bottomRightDock;
        public StatusChip FlagChip => flagChip;
        public NotificationFeed Feed => notificationFeed;

        public void Bind(
            RectTransform topLeft, RectTransform topCenter, RectTransform topRight,
            RectTransform timingTower, RectTransform bottomCenter, RectTransform bottomRight,
            StatusChip flag, NotificationFeed feed)
        {
            topLeftDock = topLeft;
            topCenterDock = topCenter;
            topRightDock = topRight;
            timingTowerDock = timingTower;
            bottomCenterDock = bottomCenter;
            bottomRightDock = bottomRight;
            flagChip = flag;
            notificationFeed = feed;
            SetScreenId(Id);
        }
    }
}
