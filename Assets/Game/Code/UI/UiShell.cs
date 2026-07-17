using F1Game.UI.Navigation;
using F1Game.UI.Screens.CareerHub;
using F1Game.UI.Screens.CareerStandings;
using F1Game.UI.Screens.DriverProfile;
using F1Game.UI.Screens.Results;
using F1Game.UI.Screens.MainMenu;
using F1Game.UI.Screens.PreRaceStrategy;
using F1Game.UI.Screens.RaceHudShell;
using F1Game.UI.Screens.TrackSelect;
using F1Game.UI.Services;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
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
        CanvasScaler scaler;
        GraphicRaycaster raycaster;
        RectTransform screenLayer;
        RectTransform modalLayer;

        /// <summary>The live shell, if one has been created (used by the legacy
        /// settings path to push UI-scale changes into the production canvas).</summary>
        public static UiShell ActiveShell { get; private set; }
        /// <summary>Overlay layer above screens (modals, pause) - used to host the production pause overlay.</summary>
        public RectTransform ModalLayer => modalLayer;

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
            // This is the gate that keeps the production UI off until text can render.
            // It must NEVER throw: TMP_Settings.defaultFontAsset throws a
            // NullReferenceException (rather than returning null) when TMP Essentials
            // have not been imported, and UiTheme/typography may be a bare default. Any
            // failure here means text cannot render yet, so we report not-ready and the
            // legacy frontend is shown instead of crashing bootstrap.
            try
            {
                // TMP components cannot initialize at all without the TMP Settings
                // resource (imported with TMP Essentials) - a theme font alone is
                // not sufficient, and reporting ready here would make the shell
                // build throw and latch the legacy fallback for the session.
                // Probed via Resources.Load rather than TMP_Settings.instance,
                // whose getter pops the TMP importer window in the editor when
                // the asset is missing.
                if (Resources.Load<TMP_Settings>("TMP Settings") == null)
                {
                    return false;
                }

                UiTheme theme = UiTheme.Active;
                if (theme != null && theme.typography != null && theme.typography.regular != null)
                {
                    return true;
                }

                return TMP_Settings.defaultFontAsset != null;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        public static UiShell Create()
        {
            var go = new GameObject("Production UI Shell");
            DontDestroyOnLoad(go);
            var shell = go.AddComponent<UiShell>();
            shell.BuildLayers();
            shell.RegisterScreens();
            ActiveShell = shell;
            return shell;
        }

        void BuildLayers()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;

            scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            raycaster = gameObject.AddComponent<GraphicRaycaster>();

            RectTransform MakeLayer(string name)
            {
                var layerGo = new GameObject(name, typeof(RectTransform));
                layerGo.transform.SetParent(transform, false);
                var rect = (RectTransform)layerGo.transform;
                UiScreenFactory.Stretch(rect);
                return rect;
            }

            screenLayer = MakeLayer("Layer_Screens");
            modalLayer = MakeLayer("Layer_Modals");
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
            // The toast template is built in code (no baked prefab exists yet);
            // passing null here silently killed every toast — they degraded to
            // Debug.Log lines and the toast column stayed permanently empty.
            Toasts = new ToastService(toastColumn, BuildToastTemplate(toastColumn));
            Tooltips = new TooltipService((RectTransform)tooltipGo.transform, tooltipText, tooltipGo.GetComponent<CanvasGroup>());
            Transitions = new TransitionService(this);
            // Navigation honours the theme's motion tokens (and reduced-motion)
            // instead of hard-cutting between screens.
            Router.EnterTransition = view => Transitions.FadeIn(view.CanvasGroup);
            DevicePrompts = new DevicePromptService();
        }

        // Code-built ToastView used as the pool template until a baked prefab
        // exists: raised surface, kind-coloured accent bar on the left, body
        // text. Kept inactive; ToastService instantiates copies from it.
        static ToastView BuildToastTemplate(RectTransform toastColumn)
        {
            UiTheme theme = UiTheme.Active;
            var go = new GameObject("Toast Template", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(toastColumn, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(420f, 56f);

            var background = go.AddComponent<Image>();
            background.color = theme.palette.surfaceRaised;
            background.raycastTarget = false;

            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.minHeight = 56f;
            layoutElement.preferredHeight = 56f;

            var accentGo = new GameObject("Accent", typeof(RectTransform));
            accentGo.transform.SetParent(go.transform, false);
            var accentRect = (RectTransform)accentGo.transform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.sizeDelta = new Vector2(4f, 0f);
            accentRect.anchoredPosition = Vector2.zero;
            var accent = accentGo.AddComponent<Image>();
            accent.color = theme.palette.accent;
            accent.raycastTarget = false;

            TMP_Text text = UiScreenFactory.CreateText(go.transform, "Label", UiScreenFactory.TextStyle.BodySmall, "");
            UiScreenFactory.Stretch(text.rectTransform, 12f);
            text.raycastTarget = false;

            var view = go.AddComponent<ToastView>();
            view.Bind(text, accent, go.GetComponent<CanvasGroup>());
            go.SetActive(false);
            return view;
        }

        /// <summary>
        /// Applies the player's UI-scale setting to the production canvas by
        /// shrinking/growing the reference resolution — the same technique the
        /// legacy canvas uses, so the two stacks scale identically. Previously
        /// the slider only affected the legacy canvas, i.e. it did nothing on
        /// the default production frontend.
        /// </summary>
        public void ApplyUiScale(float scale)
        {
            if (scaler == null)
            {
                return;
            }

            float clamped = Mathf.Clamp(scale, 0.5f, 2f);
            scaler.referenceResolution = new Vector2(1920f, 1080f) / clamped;
        }

        void RegisterScreens()
        {
            RegisterScreen(MainMenuView.Id, "UI/Screens/MainMenu", root => UiScreenFactory.BuildMainMenu(root));
            RegisterScreen(TrackSelectView.Id, "UI/Screens/TrackSelect", root => UiScreenFactory.BuildTrackSelect(root));
            RegisterScreen(PreRaceStrategyView.Id, "UI/Screens/PreRaceStrategy", root => UiScreenFactory.BuildStrategy(root));
            RegisterScreen(HudRoot.Id, "UI/Screens/RaceHudShell", root => UiScreenFactory.BuildHudShell(root));
            RegisterScreen(CareerStandingsView.Id, "UI/Screens/CareerStandings", root => UiScreenFactory.BuildCareerStandings(root));
            RegisterScreen(CareerHubView.Id, "UI/Screens/CareerHub", root => UiScreenFactory.BuildCareerHub(root));
            RegisterScreen(DriverProfileView.Id, "UI/Screens/DriverProfile", root => UiScreenFactory.BuildDriverProfile(root));
            RegisterScreen(F1Game.UI.Screens.CareerCreation.CareerCreationView.Id, "UI/Screens/CareerCreation", root => UiScreenFactory.BuildCareerCreation(root));
            RegisterScreen(F1Game.UI.Screens.CareerStats.CareerStatsView.Id, "UI/Screens/CareerStats", root => UiScreenFactory.BuildCareerStats(root));
            RegisterScreen(F1Game.UI.Screens.CareerStats.CareerStatsView.TrophyId, "UI/Screens/TrophyCabinet", root => UiScreenFactory.BuildTrophyCabinet(root));
            RegisterScreen(F1Game.UI.Screens.DriverRatings.DriverRatingsView.Id, "UI/Screens/DriverRatings", root => UiScreenFactory.BuildDriverRatings(root));
            RegisterScreen(F1Game.UI.Screens.TeamRatings.TeamRatingsView.Id, "UI/Screens/TeamRatings", root => UiScreenFactory.BuildTeamRatings(root));
            RegisterScreen(F1Game.UI.Screens.Rnd.RndView.Id, "UI/Screens/Rnd", root => UiScreenFactory.BuildRnd(root));
            RegisterScreen(F1Game.UI.Screens.PracticePrograms.PracticeProgramsView.Id, "UI/Screens/PracticePrograms", root => UiScreenFactory.BuildPracticePrograms(root));
            RegisterScreen(F1Game.UI.Screens.Championship.ChampionshipChartView.Id, "UI/Screens/Championship", root => UiScreenFactory.BuildChampionshipChart(root));
            RegisterScreen(F1Game.UI.Screens.Settings.SettingsView.Id, "UI/Screens/Settings", root => UiScreenFactory.BuildSettings(root));
            RegisterScreen(ResultsView.Id, "UI/Screens/Results", root => UiScreenFactory.BuildResults(root));
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
            // Belt-and-braces for the dual-stack window: a hidden shell must
            // never swallow clicks aimed at the legacy canvas underneath (the
            // production canvas sorts at 40 with full-screen opaque raycast-
            // catching screen backgrounds; the legacy canvas sorts at 0).
            if (raycaster != null)
            {
                raycaster.enabled = visible;
            }
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
