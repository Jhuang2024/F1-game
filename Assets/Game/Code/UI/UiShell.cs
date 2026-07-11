using F1Game.UI.Navigation;
using F1Game.UI.Screens.CareerHub;
using F1Game.UI.Screens.CareerStandings;
using F1Game.UI.Screens.MainMenu;
using F1Game.UI.Screens.PreRaceStrategy;
using F1Game.UI.Screens.RaceHudShell;
using F1Game.UI.Screens.TrackSelect;
using F1Game.UI.Services;
using F1Game.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace F1Game.UI
{
    /// <summary>
    /// Root of the production UI: one persistent canvas with dedicated layers
    /// (screens, modals, toasts, tooltip — modals always render above screens),
    /// the ScreenRouter, and the shared UI services. Screens are instantiated
    /// once and toggled; navigation never destroys the canvas.
    ///
    /// Baked screen prefabs (Resources/UI/Screens/*) are preferred; while they
    /// haven't been baked in-editor yet, screens are constructed once at boot
    /// from UiScreenFactory — the same authoring source the bake tool uses.
    /// </summary>
    public sealed class UiShell : MonoBehaviour
    {
        public ScreenRouter Router { get; private set; }
        public ModalService Modals { get; private set; }
        public ToastService Toasts { get; private set; }
        public TooltipService Tooltips { get; private set; }
        public TransitionService Transitions { get; private set; }
        public DevicePromptService DevicePrompts { get; private set; }

        Canvas canvas;
        RectTransform screenLayer;

        /// <summary>
        /// Set by the race layer's session coordinator while a session is starting
        /// or live, so global Back / controller-East cannot pop the frontend stack
        /// (and reopen the strategy screen) during/after the transition. Modals may
        /// still be closed.
        /// </summary>
        public static bool NavigationLocked;

        /// <summary>
        /// TMP must be usable (essentials imported or theme fonts assigned)
        /// before the production UI can render text at all.
        /// </summary>
        public static bool TextPipelineReady()
        {
            if (UiTheme.Active.typography.regular != null)
            {
                return true;
            }

            return TMP_Settings.defaultFontAsset != null;
        }

        public static UiShell Create()
        {
            var go = new GameObject("Production UI Shell");
            DontDestroyOnLoad(go);
            var shell = go.AddComponent<UiShell>();
            shell.BuildLayers();
            shell.RegisterScreens();
            return shell;
        }

        void BuildLayers()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            RectTransform MakeLayer(string name)
            {
                var layerGo = new GameObject(name, typeof(RectTransform));
                layerGo.transform.SetParent(transform, false);
                var rect = (RectTransform)layerGo.transform;
                UiScreenFactory.Stretch(rect);
                return rect;
            }

            screenLayer = MakeLayer("Layer_Screens");
            RectTransform modalLayer = MakeLayer("Layer_Modals");
            RectTransform toastLayer = MakeLayer("Layer_Toasts");
            RectTransform tooltipLayer = MakeLayer("Layer_Tooltip");

            // Toast column pinned top-right.
            var toastColumn = new GameObject("ToastColumn", typeof(RectTransform)).GetComponent<RectTransform>();
            toastColumn.SetParent(toastLayer, false);
            toastColumn.anchorMin = toastColumn.anchorMax = new Vector2(0.985f, 0.97f);
            toastColumn.pivot = new Vector2(1f, 1f);
            toastColumn.sizeDelta = new Vector2(420f, 0f);
            var toastStack = toastColumn.gameObject.AddComponent<VerticalLayoutGroup>();
            toastStack.spacing = UiTheme.Active.spacing.small;
            toastStack.childForceExpandHeight = false;

            // Tooltip visual.
            var tooltipGo = new GameObject("Tooltip", typeof(RectTransform), typeof(CanvasGroup));
            tooltipGo.transform.SetParent(tooltipLayer, false);
            var tooltipBg = tooltipGo.AddComponent<Image>();
            tooltipBg.color = UiTheme.Active.palette.surfaceRaised;
            var tooltipText = UiScreenFactory.CreateText(tooltipGo.transform, "Text", UiScreenFactory.TextStyle.BodySmall, "");
            UiScreenFactory.Stretch(tooltipText.rectTransform, 8f);

            EnsureEventSystem();

            Router = new ScreenRouter(screenLayer);
            Modals = new ModalService(modalLayer);
            Toasts = new ToastService(toastColumn, null);
            Tooltips = new TooltipService((RectTransform)tooltipGo.transform, tooltipText, tooltipGo.GetComponent<CanvasGroup>());
            Transitions = new TransitionService(this);
            DevicePrompts = new DevicePromptService();
        }

        void RegisterScreens()
        {
            RegisterScreen(MainMenuView.Id, "UI/Screens/MainMenu", root => UiScreenFactory.BuildMainMenu(root));
            RegisterScreen(TrackSelectView.Id, "UI/Screens/TrackSelect", root => UiScreenFactory.BuildTrackSelect(root));
            RegisterScreen(PreRaceStrategyView.Id, "UI/Screens/PreRaceStrategy", root => UiScreenFactory.BuildStrategy(root));
            RegisterScreen(HudRoot.Id, "UI/Screens/RaceHudShell", root => UiScreenFactory.BuildHudShell(root));
            RegisterScreen(CareerStandingsView.Id, "UI/Screens/CareerStandings", root => UiScreenFactory.BuildCareerStandings(root));
            RegisterScreen(CareerHubView.Id, "UI/Screens/CareerHub", root => UiScreenFactory.BuildCareerHub(root));
        }

        void RegisterScreen(string id, string prefabResourcePath, System.Func<Transform, ScreenView> factoryFallback)
        {
            Router.Register(id, root =>
            {
                var prefab = Resources.Load<ScreenView>(prefabResourcePath);
                if (prefab != null)
                {
                    return Instantiate(prefab, root);
                }

                Debug.Log("[UI] Screen prefab not baked yet (" + prefabResourcePath + ") - building from UiScreenFactory.");
                return factoryFallback(root);
            });
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem));
            // Works with either backend while activeInputHandler is set to Both.
            go.AddComponent<StandaloneInputModule>();
            DontDestroyOnLoad(go);
        }

        public void SetShellVisible(bool visible)
        {
            canvas.enabled = visible;
        }

        void Update()
        {
            Tooltips?.Tick();

            // Global back: Escape / gamepad East. Modals close before screens pop.
            bool backPressed =
                (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

            if (backPressed && canvas.enabled)
            {
                if (Modals.AnyOpen)
                {
                    Modals.CloseTop();
                }
                else if (!NavigationLocked)
                {
                    // While a session is starting/live the race owns pause/back;
                    // never pop the frontend stack (which would reopen strategy).
                    Router.Back();
                }
            }
        }
    }
}
