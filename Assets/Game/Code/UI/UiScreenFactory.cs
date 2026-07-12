using System.Collections.Generic;
using F1Game.UI.Screens.MainMenu;
using F1Game.UI.Screens.PreRaceStrategy;
using F1Game.UI.Screens.RaceHudShell;
using F1Game.UI.Screens.TrackSelect;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1Game.UI
{
    /// <summary>
    /// Authoring source for the production screen prefabs. The editor bake tool
    /// (F1 Game → UI → Bake Screen Prefabs) runs these builders once and saves
    /// the results as prefabs under Assets/Game/Prefabs/UI; at runtime the
    /// ScreenRouter prefers those baked prefabs and only falls back to building
    /// directly from here while the prefabs have not been baked yet.
    ///
    /// Layout uses anchors + layout groups and theme tokens exclusively — no
    /// hard-coded pixel positions — so the same construction is responsive from
    /// 16:10 to ultrawide.
    /// </summary>
    public static class UiScreenFactory
    {
        public enum TextStyle { Display, H1, H2, H3, Body, BodySmall, Label, Button, Numeric, Caption }

        // ---------- primitives ----------

        public static TMP_Text CreateText(Transform parent, string name, TextStyle style, string text)
        {
            UiTheme theme = UiTheme.Active;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.color = theme.palette.textPrimary;
            tmp.fontSize = style switch
            {
                TextStyle.Display => theme.typography.display,
                TextStyle.H1 => theme.typography.h1,
                TextStyle.H2 => theme.typography.h2,
                TextStyle.H3 => theme.typography.h3,
                TextStyle.BodySmall => theme.typography.bodySmall,
                TextStyle.Label => theme.typography.label,
                TextStyle.Button => theme.typography.button,
                TextStyle.Numeric => theme.typography.numeric,
                TextStyle.Caption => theme.typography.caption,
                _ => theme.typography.body,
            };

            TMP_FontAsset font = style == TextStyle.Numeric
                ? (theme.typography.tabularNumeric != null ? theme.typography.tabularNumeric : theme.typography.semiBold)
                : (style <= TextStyle.H3 ? theme.typography.semiBold : theme.typography.regular);
            if (font != null)
            {
                tmp.font = font;
            }

            if (style == TextStyle.Label || style == TextStyle.Caption)
            {
                tmp.color = theme.palette.textMuted;
            }

            return tmp;
        }

        public static Image CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static RectTransform CreateLayoutColumn(Transform parent, string name, float spacing, RectOffset padding = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            if (padding != null)
            {
                layout.padding = padding;
            }

            return (RectTransform)go.transform;
        }

        public static ThemedButton CreateButton(Transform parent, string name, ThemedButton.Variant variant, string text, float height = 0f)
        {
            UiTheme theme = UiTheme.Active;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var background = go.AddComponent<Image>();

            // Focus outline as an inset frame built from four edge strips - a
            // sliced Image with no sprite would render as a full opaque quad.
            var outlineGo = new GameObject("FocusOutline", typeof(RectTransform));
            outlineGo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)outlineGo.transform);
            float outlineWidth = theme.states.focusOutlineWidth;
            CreateOutlineEdge(outlineGo.transform, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -outlineWidth), Vector2.zero, theme.palette.focusOutline);
            CreateOutlineEdge(outlineGo.transform, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, outlineWidth), theme.palette.focusOutline);
            CreateOutlineEdge(outlineGo.transform, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(outlineWidth, 0f), theme.palette.focusOutline);
            CreateOutlineEdge(outlineGo.transform, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-outlineWidth, 0f), Vector2.zero, theme.palette.focusOutline);
            outlineGo.SetActive(false);

            // Localize the button label by a key derived from its English text
            // (e.g. "Continue Career" → button.continue_career); the English text
            // is the fallback, so untranslated builds read exactly as before, and
            // dynamic labels (later SetText, tyre names) simply fall through.
            string localized = string.IsNullOrEmpty(text)
                ? text
                : F1Game.Core.Localization.Get("button." + text.ToLowerInvariant().Replace(" ", "_"), text);
            TMP_Text label = CreateText(go.transform, "Label", TextStyle.Button, localized);
            Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = height > 0f ? height : theme.components.buttonHeight;
            layoutElement.minHeight = layoutElement.preferredHeight;

            var button = go.AddComponent<ThemedButton>();
            button.Bind(label, background, outlineGo, null);
            button.SetVariant(variant);
            button.transition = Selectable.Transition.None;
            return button;
        }

        static void CreateOutlineEdge(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        public static void Stretch(RectTransform rect, float margin = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }

        static RectTransform ScreenScaffold(Transform root, string name, string headerText, out TMP_Text header)
        {
            UiTheme theme = UiTheme.Active;
            var screenGo = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            screenGo.transform.SetParent(root, false);
            var rect = (RectTransform)screenGo.transform;
            Stretch(rect);

            Image background = CreatePanel(rect, "Background", theme.palette.background);
            Stretch(background.rectTransform);
            background.raycastTarget = true;

            // Content column, centred with a max width for ultrawide sanity.
            RectTransform content = CreateLayoutColumn(rect, "Content", theme.spacing.normal,
                new RectOffset((int)theme.spacing.hero, (int)theme.spacing.hero, (int)theme.spacing.major, (int)theme.spacing.major));
            content.anchorMin = new Vector2(0.5f, 0f);
            content.anchorMax = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(1280f, 0f);
            content.anchoredPosition = Vector2.zero;

            header = headerText != null ? CreateText(content, "Header", TextStyle.H1, headerText) : null;
            return content;
        }

        // ---------- screens ----------

        public static MainMenuView BuildMainMenu(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_MainMenu", null, out TMP_Text _);

            var accentBar = CreatePanel(content, "AccentBar", theme.palette.accent);
            accentBar.gameObject.AddComponent<LayoutElement>().preferredHeight = 6f;

            TMP_Text hero = CreateText(content, "HeroTitle", TextStyle.Display, "APEX FORMULA");
            TMP_Text context = CreateText(content, "CareerContext", TextStyle.H3, "");
            context.color = theme.palette.textMuted;

            RectTransform actions = CreateLayoutColumn(content, "Actions", theme.spacing.small);
            ThemedButton career = CreateButton(actions, "Btn_Career", ThemedButton.Variant.Primary, "Continue Career");
            ThemedButton quickRace = CreateButton(actions, "Btn_QuickRace", ThemedButton.Variant.Secondary, "Quick Race");
            ThemedButton timeTrial = CreateButton(actions, "Btn_TimeTrial", ThemedButton.Variant.Secondary, "Time Trial");
            ThemedButton standings = CreateButton(actions, "Btn_Standings", ThemedButton.Variant.Secondary, "Standings");
            ThemedButton settings = CreateButton(actions, "Btn_Settings", ThemedButton.Variant.Tertiary, "Settings");

            TMP_Text version = CreateText(content, "Version", TextStyle.Caption, "");

            SetUpDownNavigation(new[] { career, quickRace, timeTrial, standings, settings });

            var view = content.parent.gameObject.AddComponent<MainMenuView>();
            view.Bind(hero, context, version, career, quickRace, timeTrial, standings, settings);
            return view;
        }

        public static TrackSelectView BuildTrackSelect(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_TrackSelect", F1Game.Core.Localization.Get("screen.trackselect.title", "SELECT CIRCUIT"), out TMP_Text header);

            RectTransform listColumn = CreateLayoutColumn(content, "TrackList", theme.spacing.micro);
            ThemedButton rowTemplate = CreateButton(listColumn, "Row_Template", ThemedButton.Variant.Secondary, "Track",
                theme.components.buttonHeightCompact);
            rowTemplate.Label.alignment = TextAlignmentOptions.MidlineLeft;
            rowTemplate.Label.margin = new Vector4(theme.spacing.normal, 0f, theme.spacing.normal, 0f);

            ThemedButton back = CreateButton(content, "Btn_Back", ThemedButton.Variant.Tertiary, "Back");

            var view = content.parent.gameObject.AddComponent<TrackSelectView>();
            view.Bind(header, listColumn, rowTemplate, back);
            return view;
        }

        public static Screens.CareerHub.CareerHubView BuildCareerHub(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_CareerHub", F1Game.Core.Localization.Get("screen.careerhub.title", "CAREER"), out TMP_Text header);

            TMP_Text season = CreateText(content, "SeasonLabel", TextStyle.H3, "");
            season.color = theme.palette.textMuted;
            TMP_Text standing = CreateText(content, "StandingLine", TextStyle.Body, "");

            var card = CreatePanel(content, "NextEventCard", theme.palette.surfaceRaised);
            var cardLayout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            cardLayout.spacing = theme.spacing.micro;
            cardLayout.padding = new RectOffset((int)theme.spacing.normal, (int)theme.spacing.normal,
                (int)theme.spacing.normal, (int)theme.spacing.normal);
            cardLayout.childForceExpandHeight = false;
            cardLayout.childControlHeight = true;
            cardLayout.childControlWidth = true;
            CreateText(card.transform, "NextLabel", TextStyle.Label, "NEXT EVENT");
            TMP_Text eventTitle = CreateText(card.transform, "EventTitle", TextStyle.H2, "");
            TMP_Text eventDetail = CreateText(card.transform, "EventDetail", TextStyle.Body, "");
            eventDetail.color = theme.palette.textMuted;

            RectTransform actions = CreateLayoutColumn(content, "Actions", theme.spacing.small);
            ThemedButton continueBtn = CreateButton(actions, "Btn_Continue", ThemedButton.Variant.Primary, "Continue");
            ThemedButton standings = CreateButton(actions, "Btn_Standings", ThemedButton.Variant.Secondary, "Standings & Calendar");
            ThemedButton profile = CreateButton(actions, "Btn_Profile", ThemedButton.Variant.Secondary, "Driver Profile");
            ThemedButton stats = CreateButton(actions, "Btn_Stats", ThemedButton.Variant.Secondary, "Career Stats");
            ThemedButton ratings = CreateButton(actions, "Btn_Ratings", ThemedButton.Variant.Secondary, "Driver Ratings");
            ThemedButton rnd = CreateButton(actions, "Btn_Rnd", ThemedButton.Variant.Secondary, "R&D Centre");
            ThemedButton practice = CreateButton(actions, "Btn_Practice", ThemedButton.Variant.Secondary, "Practice Programs");
            ThemedButton legacyMenu = CreateButton(actions, "Btn_FullMenu", ThemedButton.Variant.Secondary, "Full Career Menu");
            ThemedButton back = CreateButton(actions, "Btn_Back", ThemedButton.Variant.Tertiary, "Back");

            SetUpDownNavigation(new[] { continueBtn, standings, profile, stats, ratings, rnd, practice, legacyMenu, back });

            var view = content.parent.gameObject.AddComponent<Screens.CareerHub.CareerHubView>();
            view.Bind(season, standing, eventTitle, eventDetail, continueBtn, standings, profile, stats, ratings, rnd, practice, legacyMenu, back);
            return view;
        }

        public static Screens.RaceHudShell.PauseOverlay BuildPauseOverlay(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            var overlayGo = new GameObject("PauseOverlay", typeof(RectTransform));
            overlayGo.transform.SetParent(root, false);
            Stretch((RectTransform)overlayGo.transform);

            // Dimming backdrop that also blocks clicks to the HUD behind it.
            var backdrop = overlayGo.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.72f);
            backdrop.raycastTarget = true;

            RectTransform card = CreatePanel(overlayGo.transform, "PauseCard", theme.palette.surface).rectTransform;
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(520f, 520f);
            RectTransform column = CreateLayoutColumn(card, "PauseMenu", theme.spacing.small,
                new RectOffset((int)theme.spacing.major, (int)theme.spacing.major, (int)theme.spacing.major, (int)theme.spacing.major));
            Stretch(column);

            CreateText(column, "PauseTitle", TextStyle.H1, "PAUSED");
            TMP_Text session = CreateText(column, "PauseSession", TextStyle.H3, "");
            session.color = theme.palette.textMuted;

            ThemedButton resume = CreateButton(column, "Btn_Resume", ThemedButton.Variant.Primary, "Resume");
            ThemedButton endPractice = CreateButton(column, "Btn_EndPractice", ThemedButton.Variant.Primary, "End Practice Session");
            ThemedButton mainMenu = CreateButton(column, "Btn_MainMenu", ThemedButton.Variant.Secondary, "Main Menu");
            ThemedButton restart = CreateButton(column, "Btn_Restart", ThemedButton.Variant.Destructive, "Restart Session");
            ThemedButton quit = CreateButton(column, "Btn_Quit", ThemedButton.Variant.Destructive, "Quit Game");

            SetUpDownNavigation(new[] { resume, endPractice, mainMenu, restart, quit });

            var overlay = overlayGo.AddComponent<Screens.RaceHudShell.PauseOverlay>();
            overlay.Bind(session, resume, endPractice, mainMenu, restart, quit);
            return overlay;
        }

        public static Screens.Results.ResultsView BuildResults(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_Results", "RESULT", out TMP_Text header);

            TMP_Text subtitle = CreateText(content, "Subtitle", TextStyle.H3, "");
            subtitle.color = theme.palette.textMuted;

            RectTransform rowsColumn = CreateLayoutColumn(content, "ResultRows", theme.spacing.micro);
            TMP_Text rowTemplate = CreateText(rowsColumn, "Row_Template", TextStyle.Body, "");
            rowTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.typography.body + 8f;

            var actions = new GameObject("Actions", typeof(RectTransform));
            actions.transform.SetParent(content, false);
            var actionsLayout = actions.AddComponent<HorizontalLayoutGroup>();
            actionsLayout.spacing = theme.spacing.small;
            actionsLayout.childForceExpandWidth = true;
            actionsLayout.childControlWidth = true;
            actionsLayout.childControlHeight = true;
            ThemedButton menu = CreateButton(actions.transform, "Btn_Menu", ThemedButton.Variant.Tertiary, "Main Menu");
            ThemedButton primary = CreateButton(actions.transform, "Btn_Primary", ThemedButton.Variant.Primary, "Continue");

            // Header carries the title; the screen keeps the H1 header and a
            // muted subtitle line below it.
            var view = content.parent.gameObject.AddComponent<Screens.Results.ResultsView>();
            view.Bind(header, subtitle, rowsColumn, rowTemplate, primary, menu);
            return view;
        }

        public static Screens.DriverProfile.DriverProfileView BuildDriverProfile(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_DriverProfile", F1Game.Core.Localization.Get("screen.driverprofile.title", "DRIVER PROFILE"), out TMP_Text header);

            TMP_Text name = CreateText(content, "DriverName", TextStyle.H2, "");
            TMP_Text team = CreateText(content, "TeamLine", TextStyle.H3, "");
            team.color = theme.palette.textMuted;

            RectTransform rowsColumn = CreateLayoutColumn(content, "StatRows", theme.spacing.micro);
            TMP_Text rowTemplate = CreateText(rowsColumn, "Row_Template", TextStyle.Body, "");
            rowTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.typography.body + 8f;

            ThemedButton back = CreateButton(content, "Btn_Back", ThemedButton.Variant.Tertiary, "Back");

            var view = content.parent.gameObject.AddComponent<Screens.DriverProfile.DriverProfileView>();
            view.Bind(name, team, rowsColumn, rowTemplate, back);
            return view;
        }

        public static Screens.Settings.SettingsView BuildSettings(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_Settings", F1Game.Core.Localization.Get("screen.settings.title", "SETTINGS"), out TMP_Text header);

            RectTransform rowsColumn = CreateLayoutColumn(content, "SettingsRows", theme.spacing.micro);
            TMP_Text rowTemplate = CreateText(rowsColumn, "Row_Template", TextStyle.Body, "");
            rowTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.typography.body + 8f;

            CreateText(content, "QuickGameplayLabel", TextStyle.Label, "QUICK GAMEPLAY SETTINGS");
            var gameplayRowGo = new GameObject("GameplayRow", typeof(RectTransform));
            gameplayRowGo.transform.SetParent(content, false);
            var gameplayLayout = gameplayRowGo.AddComponent<HorizontalLayoutGroup>();
            gameplayLayout.spacing = theme.spacing.small;
            gameplayLayout.childForceExpandWidth = true;
            gameplayLayout.childControlWidth = true;
            gameplayLayout.childControlHeight = true;
            ThemedButton difficultyButton = CreateButton(gameplayRowGo.transform, "Btn_Difficulty", ThemedButton.Variant.Secondary, "Difficulty",
                theme.components.buttonHeightCompact);
            ThemedButton ersButton = CreateButton(gameplayRowGo.transform, "Btn_Ers", ThemedButton.Variant.Secondary, "ERS",
                theme.components.buttonHeightCompact);
            ThemedButton manualGearsButton = CreateButton(gameplayRowGo.transform, "Btn_ManualGears", ThemedButton.Variant.Secondary, "Gears",
                theme.components.buttonHeightCompact);

            CreateText(content, "QuickToggleLabel", TextStyle.Label, "QUICK ACCESSIBILITY TOGGLES");
            var toggleRowGo = new GameObject("ToggleRow", typeof(RectTransform));
            toggleRowGo.transform.SetParent(content, false);
            var toggleLayout = toggleRowGo.AddComponent<HorizontalLayoutGroup>();
            toggleLayout.spacing = theme.spacing.small;
            toggleLayout.childForceExpandWidth = true;
            toggleLayout.childControlWidth = true;
            toggleLayout.childControlHeight = true;
            ThemedButton unitsButton = CreateButton(toggleRowGo.transform, "Btn_Units", ThemedButton.Variant.Secondary, "Units",
                theme.components.buttonHeightCompact);
            ThemedButton cameraShakeButton = CreateButton(toggleRowGo.transform, "Btn_CameraShake", ThemedButton.Variant.Secondary, "Camera Shake",
                theme.components.buttonHeightCompact);
            ThemedButton compactHudButton = CreateButton(toggleRowGo.transform, "Btn_CompactHud", ThemedButton.Variant.Secondary, "Compact HUD",
                theme.components.buttonHeightCompact);
            ThemedButton uiAnimationsButton = CreateButton(toggleRowGo.transform, "Btn_UiAnimations", ThemedButton.Variant.Secondary, "UI Animations",
                theme.components.buttonHeightCompact);

            var buttonRowGo = new GameObject("ButtonRow", typeof(RectTransform));
            buttonRowGo.transform.SetParent(content, false);
            var buttonLayout = buttonRowGo.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.spacing = theme.spacing.small;
            buttonLayout.childForceExpandWidth = true;
            buttonLayout.childControlWidth = true;
            buttonLayout.childControlHeight = true;
            ThemedButton classic = CreateButton(buttonRowGo.transform, "Btn_ClassicSettings", ThemedButton.Variant.Secondary, "Classic Settings",
                theme.components.buttonHeightCompact);
            ThemedButton back = CreateButton(buttonRowGo.transform, "Btn_Back", ThemedButton.Variant.Tertiary, "Back",
                theme.components.buttonHeightCompact);

            // Full production settings editor (populated only when the editor switch
            // is on - see ProductionUiBridge). A labelled section of pooled interactive
            // rows built from a hidden template; the whole section hides when empty.
            RectTransform editorSection = CreateLayoutColumn(content, "SettingsEditorSection", theme.spacing.micro);
            CreateText(editorSection, "FullEditorLabel", TextStyle.Label, "FULL SETTINGS EDITOR");
            RectTransform editorColumn = CreateLayoutColumn(editorSection, "SettingsEditorRows", theme.spacing.micro);
            EditableSettingRow editorTemplate = BuildEditableSettingRow(editorColumn, theme, startHidden: true);

            var view = content.parent.gameObject.AddComponent<Screens.Settings.SettingsView>();
            view.Bind(header, rowsColumn, rowTemplate, difficultyButton, ersButton, manualGearsButton,
                unitsButton, cameraShakeButton, compactHudButton, uiAnimationsButton, classic, back);
            view.BindEditor(editorSection.gameObject, editorColumn, editorTemplate);
            return view;
        }

        public static Screens.Championship.ChampionshipChartView BuildChampionshipChart(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_Championship", "CHAMPIONSHIP", out TMP_Text header);

            TMP_Text title = CreateText(content, "ChartTitle", TextStyle.H3, "");

            // Drivers / Constructors toggle.
            var tabRowGo = new GameObject("ModeTabs", typeof(RectTransform));
            tabRowGo.transform.SetParent(content, false);
            var tabLayout = tabRowGo.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = theme.spacing.small;
            tabLayout.childForceExpandWidth = false;
            tabLayout.childControlWidth = true;
            tabLayout.childControlHeight = true;
            ThemedButton drivers = CreateButton(tabRowGo.transform, "Tab_Drivers", ThemedButton.Variant.Secondary, "Drivers",
                theme.components.buttonHeightCompact);
            drivers.GetComponent<LayoutElement>().preferredWidth = 160f;
            ThemedButton constructors = CreateButton(tabRowGo.transform, "Tab_Constructors", ThemedButton.Variant.Secondary, "Constructors",
                theme.components.buttonHeightCompact);
            constructors.GetComponent<LayoutElement>().preferredWidth = 160f;

            // Fixed-size chart area (left margin for y labels, bottom margin for x labels).
            var chartHost = new GameObject("ChartHost", typeof(RectTransform));
            chartHost.transform.SetParent(content, false);
            var hostLayout = chartHost.AddComponent<LayoutElement>();
            hostLayout.preferredHeight = 372f;
            hostLayout.preferredWidth = 964f;
            RectTransform chartArea = CreateRect((RectTransform)chartHost.transform, "ChartArea",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(52f, 26f), new Vector2(952f, 356f));

            TMP_Text empty = CreateText(chartArea, "Empty", TextStyle.Body, "");
            empty.color = theme.palette.textMuted;

            CreateText(content, "LegendLabel", TextStyle.Label, "LEGEND");
            RectTransform legend = CreateLayoutColumn(content, "Legend", theme.spacing.micro);
            TMP_Text legendTemplate = CreateText(legend, "Legend_Template", TextStyle.BodySmall, "");
            legendTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.typography.bodySmall + 6f;

            ThemedButton back = CreateButton(content, "Btn_Back", ThemedButton.Variant.Tertiary, "Back",
                theme.components.buttonHeightCompact);

            var view = content.parent.gameObject.AddComponent<Screens.Championship.ChampionshipChartView>();
            // Plot + both label layers share the same origin rect so their coordinates align.
            view.Bind(title, drivers, constructors, chartArea, chartArea, chartArea, legend, empty, legendTemplate, back);
            return view;
        }

        public static Screens.PracticePrograms.PracticeProgramsView BuildPracticePrograms(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_PracticePrograms", "PRACTICE PROGRAMS", out TMP_Text header);

            TMP_Text summary = CreateText(content, "Summary", TextStyle.H3, "");
            RectTransform rowColumn = CreateLayoutColumn(content, "Programs", theme.spacing.micro);
            CareerActionRow rowTemplate = BuildCareerActionRowTemplate(rowColumn, theme, hasSecondary: false);

            ThemedButton back = CreateButton(content, "Btn_Back", ThemedButton.Variant.Tertiary, "Back",
                theme.components.buttonHeightCompact);

            var view = content.parent.gameObject.AddComponent<Screens.PracticePrograms.PracticeProgramsView>();
            view.Bind(summary, rowColumn, rowTemplate, back);
            return view;
        }

        public static Screens.Rnd.RndView BuildRnd(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_Rnd", "R&D CENTRE", out TMP_Text header);

            TMP_Text summary = CreateText(content, "Summary", TextStyle.H3, "");

            CreateText(content, "DeptLabel", TextStyle.Label, "DEPARTMENTS");
            RectTransform deptColumn = CreateLayoutColumn(content, "Departments", theme.spacing.micro);
            CareerActionRow deptTemplate = BuildCareerActionRowTemplate(deptColumn, theme, hasSecondary: false);

            CreateText(content, "ProjectsLabel", TextStyle.Label, "ACTIVE PROJECTS");
            RectTransform projectColumn = CreateLayoutColumn(content, "Projects", theme.spacing.micro);
            CareerActionRow projectTemplate = BuildCareerActionRowTemplate(projectColumn, theme, hasSecondary: true);
            TMP_Text emptyProjects = CreateText(projectColumn, "NoProjects", TextStyle.Body, "");
            emptyProjects.color = theme.palette.textMuted;

            CreateText(content, "UpgradesLabel", TextStyle.Label, "UPGRADE TREE");
            RectTransform upgradeColumn = CreateLayoutColumn(content, "Upgrades", theme.spacing.micro);
            CareerActionRow upgradeTemplate = BuildCareerActionRowTemplate(upgradeColumn, theme, hasSecondary: false);

            ThemedButton back = CreateButton(content, "Btn_Back", ThemedButton.Variant.Tertiary, "Back",
                theme.components.buttonHeightCompact);

            var view = content.parent.gameObject.AddComponent<Screens.Rnd.RndView>();
            view.Bind(summary, deptColumn, deptTemplate, projectColumn, projectTemplate, emptyProjects,
                upgradeColumn, upgradeTemplate, back);
            return view;
        }

        // Hidden CareerActionRow template: [ label / detail .... primary (secondary) ].
        static CareerActionRow BuildCareerActionRowTemplate(Transform parent, UiTheme theme, bool hasSecondary)
        {
            var rowGo = new GameObject("ActionRow_Template", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = theme.spacing.small;
            layout.childForceExpandWidth = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
            rowGo.AddComponent<LayoutElement>().preferredHeight = theme.components.buttonHeightCompact + 12f;

            RectTransform textCol = CreateLayoutColumn(rowGo.transform, "Text", 0f);
            textCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            TMP_Text label = CreateText(textCol, "Label", TextStyle.Body, "");
            TMP_Text detail = CreateText(textCol, "Detail", TextStyle.Caption, "");
            detail.color = theme.palette.textMuted;

            ThemedButton primary = CreateButton(rowGo.transform, "Btn_Primary", ThemedButton.Variant.Secondary, "Action",
                theme.components.buttonHeightCompact);
            primary.GetComponent<LayoutElement>().preferredWidth = 150f;

            ThemedButton secondary = null;
            if (hasSecondary)
            {
                secondary = CreateButton(rowGo.transform, "Btn_Secondary", ThemedButton.Variant.Tertiary, "Alt",
                    theme.components.buttonHeightCompact);
                secondary.GetComponent<LayoutElement>().preferredWidth = 120f;
            }

            var row = rowGo.AddComponent<CareerActionRow>();
            row.Bind(label, detail, primary, secondary);
            rowGo.SetActive(false);
            return row;
        }

        public static Screens.DriverRatings.DriverRatingsView BuildDriverRatings(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_DriverRatings", "DRIVER RATINGS", out TMP_Text header);

            // Sort tabs.
            var tabRowGo = new GameObject("SortTabs", typeof(RectTransform));
            tabRowGo.transform.SetParent(content, false);
            var tabLayout = tabRowGo.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = theme.spacing.small;
            tabLayout.childForceExpandWidth = true;
            tabLayout.childControlWidth = true;
            tabLayout.childControlHeight = true;
            var sortDefs = new (string label, string key)[]
            {
                ("Overall", "overall"), ("Pace", "pace"), ("Qualifying", "qualifying"),
                ("Racecraft", "racecraft"), ("Potential", "potential"),
            };
            var tabs = new List<(ThemedButton btn, string key)>();
            foreach ((string label, string key) in sortDefs)
            {
                ThemedButton tab = CreateButton(tabRowGo.transform, "Tab_" + key, ThemedButton.Variant.Secondary, label,
                    theme.components.buttonHeightCompact);
                tabs.Add((tab, key));
            }

            RectTransform rowColumn = CreateLayoutColumn(content, "RatingRows", theme.spacing.micro);
            TMP_Text rowTemplate = CreateText(rowColumn, "Rating_Template", TextStyle.Body, "");
            rowTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.typography.body + 8f;

            var navRowGo = new GameObject("NavRow", typeof(RectTransform));
            navRowGo.transform.SetParent(content, false);
            var navLayout = navRowGo.AddComponent<HorizontalLayoutGroup>();
            navLayout.spacing = theme.spacing.small;
            navLayout.childForceExpandWidth = true;
            navLayout.childControlWidth = true;
            navLayout.childControlHeight = true;
            ThemedButton teams = CreateButton(navRowGo.transform, "Btn_Teams", ThemedButton.Variant.Secondary, "Team Ratings",
                theme.components.buttonHeightCompact);
            ThemedButton back = CreateButton(navRowGo.transform, "Btn_Back", ThemedButton.Variant.Tertiary, "Back",
                theme.components.buttonHeightCompact);

            var view = content.parent.gameObject.AddComponent<Screens.DriverRatings.DriverRatingsView>();
            view.Bind(rowColumn, rowTemplate, teams, back);
            foreach ((ThemedButton btn, string key) in tabs)
            {
                view.AddSortTab(btn, key);
            }

            return view;
        }

        public static Screens.TeamRatings.TeamRatingsView BuildTeamRatings(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_TeamRatings", "TEAM RATINGS", out TMP_Text header);

            RectTransform rowColumn = CreateLayoutColumn(content, "TeamRatingRows", theme.spacing.micro);
            TMP_Text rowTemplate = CreateText(rowColumn, "TeamRating_Template", TextStyle.Body, "");
            rowTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.typography.body + 8f;

            var navRowGo = new GameObject("NavRow", typeof(RectTransform));
            navRowGo.transform.SetParent(content, false);
            var navLayout = navRowGo.AddComponent<HorizontalLayoutGroup>();
            navLayout.spacing = theme.spacing.small;
            navLayout.childForceExpandWidth = true;
            navLayout.childControlWidth = true;
            navLayout.childControlHeight = true;
            ThemedButton drivers = CreateButton(navRowGo.transform, "Btn_Drivers", ThemedButton.Variant.Secondary, "Driver Ratings",
                theme.components.buttonHeightCompact);
            ThemedButton back = CreateButton(navRowGo.transform, "Btn_Back", ThemedButton.Variant.Tertiary, "Back",
                theme.components.buttonHeightCompact);

            var view = content.parent.gameObject.AddComponent<Screens.TeamRatings.TeamRatingsView>();
            view.Bind(rowColumn, rowTemplate, drivers, back);
            return view;
        }

        public static Screens.CareerStats.CareerStatsView BuildCareerStats(Transform root) =>
            BuildStatsScreen(root, "Screen_CareerStats", "CAREER STATS", "LOCAL TRACK RECORDS",
                Screens.CareerStats.CareerStatsView.Id);

        public static Screens.CareerStats.CareerStatsView BuildTrophyCabinet(Transform root) =>
            BuildStatsScreen(root, "Screen_TrophyCabinet", "TROPHY CABINET", "TRACK ACHIEVEMENTS",
                Screens.CareerStats.CareerStatsView.TrophyId);

        // Shared stat-grid + record-list screen used by Career Stats and the Trophy Cabinet.
        static Screens.CareerStats.CareerStatsView BuildStatsScreen(Transform root, string name, string title,
            string recordsHeading, string screenId)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, name, title, out TMP_Text header);

            RectTransform statGrid = CreateLayoutColumn(content, "StatGrid", theme.spacing.small);
            var grid = statGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(184f, 90f);
            grid.spacing = new Vector2(theme.spacing.small, theme.spacing.small);
            StatTile statTemplate = BuildStatTileTemplate(statGrid, theme);

            CreateText(content, "RecordsLabel", TextStyle.Label, recordsHeading);
            RectTransform recordColumn = CreateLayoutColumn(content, "RecordRows", theme.spacing.micro);
            TMP_Text recordTemplate = CreateText(recordColumn, "Record_Template", TextStyle.Body, "");
            recordTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.typography.body + 8f;

            var buttonRow = new GameObject("ButtonRow", typeof(RectTransform));
            buttonRow.transform.SetParent(content, false);
            var buttonLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.spacing = theme.spacing.small;
            buttonLayout.childForceExpandWidth = true;
            buttonLayout.childControlWidth = true;
            buttonLayout.childControlHeight = true;
            ThemedButton secondary = CreateButton(buttonRow.transform, "Btn_Secondary", ThemedButton.Variant.Secondary, "More",
                theme.components.buttonHeightCompact);
            ThemedButton back = CreateButton(buttonRow.transform, "Btn_Back", ThemedButton.Variant.Tertiary, "Back",
                theme.components.buttonHeightCompact);

            var view = content.parent.gameObject.AddComponent<Screens.CareerStats.CareerStatsView>();
            view.Bind(screenId, statGrid, statTemplate, recordColumn, recordTemplate, secondary, back);
            return view;
        }

        // Hidden StatTile template: a raised card with a big value over a label.
        static StatTile BuildStatTileTemplate(Transform parent, UiTheme theme)
        {
            var tileGo = new GameObject("StatTile_Template", typeof(RectTransform));
            tileGo.transform.SetParent(parent, false);
            var bg = tileGo.AddComponent<Image>();
            bg.color = theme.palette.surfaceRaised;
            var col = tileGo.AddComponent<VerticalLayoutGroup>();
            col.spacing = 2f;
            col.padding = new RectOffset(12, 12, 10, 10);
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandHeight = false;

            TMP_Text value = CreateText(tileGo.transform, "Value", TextStyle.H2, "");
            TMP_Text label = CreateText(tileGo.transform, "Label", TextStyle.Label, "");
            label.color = theme.palette.textMuted;
            TMP_Text caption = CreateText(tileGo.transform, "Caption", TextStyle.Caption, "");

            var tile = tileGo.AddComponent<StatTile>();
            tile.Bind(label, value, caption);
            tileGo.SetActive(false);
            return tile;
        }

        public static Screens.CareerCreation.CareerCreationView BuildCareerCreation(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_CareerCreation", "CREATE CAREER", out TMP_Text header);

            CreateText(content, "Intro", TextStyle.Body, "Choose your driver name and team, then start your career.");
            TMP_Text summary = CreateText(content, "Summary", TextStyle.H3, "");

            RectTransform choiceColumn = CreateLayoutColumn(content, "CareerChoices", theme.spacing.micro);
            EditableSettingRow nameRow = BuildEditableSettingRow(choiceColumn, theme, startHidden: false);
            EditableSettingRow teamRow = BuildEditableSettingRow(choiceColumn, theme, startHidden: false);

            var buttonRowGo = new GameObject("ButtonRow", typeof(RectTransform));
            buttonRowGo.transform.SetParent(content, false);
            var buttonLayout = buttonRowGo.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.spacing = theme.spacing.small;
            buttonLayout.childForceExpandWidth = true;
            buttonLayout.childControlWidth = true;
            buttonLayout.childControlHeight = true;
            ThemedButton start = CreateButton(buttonRowGo.transform, "Btn_StartCareer", ThemedButton.Variant.Primary, "Start Career",
                theme.components.buttonHeightCompact);
            ThemedButton back = CreateButton(buttonRowGo.transform, "Btn_Back", ThemedButton.Variant.Tertiary, "Back",
                theme.components.buttonHeightCompact);

            var view = content.parent.gameObject.AddComponent<Screens.CareerCreation.CareerCreationView>();
            view.Bind(summary, nameRow, teamRow, start, back);
            return view;
        }

        // One interactive stepper row: [ label ....... value  <  > ]. Hidden when it is
        // a pool template; visible when it is a concrete row (e.g. career creation).
        static EditableSettingRow BuildEditableSettingRow(Transform parent, UiTheme theme, bool startHidden)
        {
            var rowGo = new GameObject("EditorRow_Template", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = theme.spacing.small;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
            rowGo.AddComponent<LayoutElement>().preferredHeight = theme.components.buttonHeightCompact;

            TMP_Text label = CreateText(rowGo.transform, "Label", TextStyle.Body, "");
            var labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;

            TMP_Text value = CreateText(rowGo.transform, "Value", TextStyle.Numeric, "");
            value.alignment = TextAlignmentOptions.Right;
            var valueLayout = value.gameObject.AddComponent<LayoutElement>();
            valueLayout.preferredWidth = 140f;
            valueLayout.flexibleWidth = 0f;

            // CreateButton already attaches a LayoutElement (height); reuse it for width.
            ThemedButton decrement = CreateButton(rowGo.transform, "Btn_Dec", ThemedButton.Variant.Secondary, "<",
                theme.components.buttonHeightCompact);
            decrement.GetComponent<LayoutElement>().preferredWidth = theme.components.buttonHeightCompact * 1.4f;
            ThemedButton increment = CreateButton(rowGo.transform, "Btn_Inc", ThemedButton.Variant.Secondary, ">",
                theme.components.buttonHeightCompact);
            increment.GetComponent<LayoutElement>().preferredWidth = theme.components.buttonHeightCompact * 1.4f;

            var row = rowGo.AddComponent<EditableSettingRow>();
            row.Bind(label, value, decrement, increment);
            if (startHidden)
            {
                rowGo.SetActive(false);
            }

            return row;
        }

        public static Screens.CareerStandings.CareerStandingsView BuildCareerStandings(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_CareerStandings", F1Game.Core.Localization.Get("screen.standings.title", "CHAMPIONSHIP STANDINGS"), out TMP_Text header);

            TMP_Text season = CreateText(content, "SeasonLabel", TextStyle.H3, "");
            season.color = theme.palette.textMuted;

            var tabRowGo = new GameObject("TabRow", typeof(RectTransform));
            tabRowGo.transform.SetParent(content, false);
            var tabLayout = tabRowGo.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = theme.spacing.small;
            tabLayout.childForceExpandWidth = true;
            tabLayout.childControlWidth = true;
            tabLayout.childControlHeight = true;
            ThemedButton driversTab = CreateButton(tabRowGo.transform, "Tab_Drivers", ThemedButton.Variant.Secondary, "Drivers",
                theme.components.buttonHeightCompact);
            ThemedButton teamsTab = CreateButton(tabRowGo.transform, "Tab_Teams", ThemedButton.Variant.Secondary, "Teams",
                theme.components.buttonHeightCompact);
            ThemedButton calendarTab = CreateButton(tabRowGo.transform, "Tab_Calendar", ThemedButton.Variant.Secondary, "Calendar",
                theme.components.buttonHeightCompact);
            var tabs = tabRowGo.AddComponent<TabBar>();
            tabs.Bind(new List<ThemedButton> { driversTab, teamsTab, calendarTab });

            RectTransform rowsColumn = CreateLayoutColumn(content, "StandingsRows", theme.spacing.micro);
            TMP_Text rowTemplate = CreateText(rowsColumn, "Row_Template", TextStyle.Body, "");
            rowTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.typography.body + 10f;

            var navRowGo = new GameObject("NavRow", typeof(RectTransform));
            navRowGo.transform.SetParent(content, false);
            var navLayout = navRowGo.AddComponent<HorizontalLayoutGroup>();
            navLayout.spacing = theme.spacing.small;
            navLayout.childForceExpandWidth = true;
            navLayout.childControlWidth = true;
            navLayout.childControlHeight = true;
            ThemedButton graph = CreateButton(navRowGo.transform, "Btn_Graph", ThemedButton.Variant.Secondary, "Championship Graph",
                theme.components.buttonHeightCompact);
            ThemedButton back = CreateButton(navRowGo.transform, "Btn_Back", ThemedButton.Variant.Tertiary, "Back",
                theme.components.buttonHeightCompact);

            var view = content.parent.gameObject.AddComponent<Screens.CareerStandings.CareerStandingsView>();
            view.Bind(header, season, tabs, rowsColumn, rowTemplate, graph, back);
            return view;
        }

        public static PreRaceStrategyView BuildStrategy(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_PreRaceStrategy", F1Game.Core.Localization.Get("screen.strategy.title", "RACE STRATEGY"), out TMP_Text header);

            TMP_Text context = CreateText(content, "ContextLine", TextStyle.H3, "");
            context.color = theme.palette.textMuted;

            CreateText(content, "CompoundLabel", TextStyle.Label, "STARTING COMPOUND");
            var compoundRowGo = new GameObject("CompoundRow", typeof(RectTransform));
            compoundRowGo.transform.SetParent(content, false);
            var compoundLayout = compoundRowGo.AddComponent<HorizontalLayoutGroup>();
            compoundLayout.spacing = theme.spacing.small;
            compoundLayout.childForceExpandWidth = true;
            compoundLayout.childControlWidth = true;
            compoundLayout.childControlHeight = true;

            var compounds = new List<ThemedButton>();
            string[] names = { "Soft", "Medium", "Hard", "Inter", "Wet" };
            for (int i = 0; i < names.Length; i++)
            {
                compounds.Add(CreateButton(compoundRowGo.transform, "Btn_Compound_" + names[i],
                    ThemedButton.Variant.Secondary, names[i], theme.components.buttonHeightCompact));
            }

            CreateText(content, "PlanLabel", TextStyle.Label, "PIT PLAN");
            RectTransform planRow = MakeStepperRow(content, "Stops", "Planned stops",
                out ThemedButton stopsMinus, out ThemedButton stopsPlus, out TMP_Text stopsValue);
            RectTransform lapOneRow = MakeStepperRow(content, "PitLapOne", "Stop 1",
                out ThemedButton lapOneMinus, out ThemedButton lapOnePlus, out TMP_Text lapOneValue);
            RectTransform lapTwoRow = MakeStepperRow(content, "PitLapTwo", "Stop 2",
                out ThemedButton lapTwoMinus, out ThemedButton lapTwoPlus, out TMP_Text lapTwoValue);

            ThemedButton start = CreateButton(content, "Btn_StartRace", ThemedButton.Variant.Primary, "Start Race");
            ThemedButton back = CreateButton(content, "Btn_Back", ThemedButton.Variant.Tertiary, "Back");

            var view = content.parent.gameObject.AddComponent<PreRaceStrategyView>();
            view.Bind(header, context, compounds,
                stopsMinus, stopsPlus, stopsValue,
                lapOneMinus, lapOnePlus, lapOneValue,
                lapTwoMinus, lapTwoPlus, lapTwoValue, lapTwoRow.gameObject,
                start, back);
            return view;
        }

        public static HudRoot BuildHudShell(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            var screenGo = new GameObject("Screen_RaceHudShell", typeof(RectTransform), typeof(CanvasGroup));
            screenGo.transform.SetParent(root, false);
            Stretch((RectTransform)screenGo.transform);

            RectTransform MakeDock(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(screenGo.transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.pivot = pivot;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(420f, 300f);
                var layout = go.AddComponent<VerticalLayoutGroup>();
                layout.spacing = theme.spacing.small;
                layout.childForceExpandHeight = false;
                return rect;
            }

            // Safe-area margin baked into anchor offsets (3.5% inset).
            RectTransform topLeft = MakeDock("Dock_TopLeft", new Vector2(0.035f, 0.965f), new Vector2(0.035f, 0.965f), new Vector2(0f, 1f));
            RectTransform topRight = MakeDock("Dock_TopRight", new Vector2(0.965f, 0.965f), new Vector2(0.965f, 0.965f), new Vector2(1f, 1f));
            RectTransform timingTower = MakeDock("Dock_TimingTower", new Vector2(0.035f, 0.5f), new Vector2(0.035f, 0.5f), new Vector2(0f, 0.5f));
            RectTransform bottomCenter = MakeDock("Dock_BottomCenter", new Vector2(0.5f, 0.035f), new Vector2(0.5f, 0.035f), new Vector2(0.5f, 0f));
            RectTransform bottomRight = MakeDock("Dock_BottomRight", new Vector2(0.965f, 0.035f), new Vector2(0.965f, 0.035f), new Vector2(1f, 0f));

            // Flag chip (event-driven).
            var chipGo = new GameObject("FlagChip", typeof(RectTransform));
            chipGo.transform.SetParent(topRight, false);
            Image chipBg = chipGo.AddComponent<Image>();
            TMP_Text chipText = CreateText(chipGo.transform, "Label", TextStyle.Label, "GREEN");
            Stretch(chipText.rectTransform);
            chipText.alignment = TextAlignmentOptions.Center;
            chipText.color = theme.palette.textPrimary;
            chipGo.AddComponent<LayoutElement>().preferredHeight = 34f;
            var chip = chipGo.AddComponent<StatusChip>();
            chip.Bind(chipText, chipBg);

            // Notification feed (event-driven, pooled).
            var feedGo = new GameObject("NotificationFeed", typeof(RectTransform));
            feedGo.transform.SetParent(bottomRight, false);
            var feedLayout = feedGo.AddComponent<VerticalLayoutGroup>();
            feedLayout.spacing = theme.spacing.micro;
            feedLayout.childForceExpandHeight = false;
            TMP_Text feedTemplate = CreateText(feedGo.transform, "Row_Template", TextStyle.BodySmall, "");
            var feed = feedGo.AddComponent<NotificationFeed>();
            feed.Bind((RectTransform)feedGo.transform, feedTemplate);

            var hud = screenGo.AddComponent<HudRoot>();
            hud.Bind(topLeft, topRight, timingTower, bottomCenter, bottomRight, chip, feed);

            // Telemetry-driven modules (position, lap/clock, speed/gear/rpm,
            // ERS/DRS, tyres, fuel) dock into the shell.
            F1Game.UI.Screens.RaceHudShell.HudModuleAssembler.Populate(hud);
            return hud;
        }

        static RectTransform MakeStepperRow(Transform parent, string name, string label,
            out ThemedButton minus, out ThemedButton plus, out TMP_Text value)
        {
            UiTheme theme = UiTheme.Active;
            var rowGo = new GameObject("Row_" + name, typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = theme.spacing.small;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleLeft;

            TMP_Text labelText = CreateText(rowGo.transform, "Label", TextStyle.Body, label);
            labelText.gameObject.AddComponent<LayoutElement>().preferredWidth = 300f;

            minus = CreateButton(rowGo.transform, "Btn_Minus", ThemedButton.Variant.Secondary, "−", theme.components.buttonHeightCompact);
            minus.gameObject.GetComponent<LayoutElement>().preferredWidth = 64f;

            value = CreateText(rowGo.transform, "Value", TextStyle.Numeric, "");
            value.alignment = TextAlignmentOptions.Center;
            value.gameObject.AddComponent<LayoutElement>().preferredWidth = 160f;

            plus = CreateButton(rowGo.transform, "Btn_Plus", ThemedButton.Variant.Secondary, "+", theme.components.buttonHeightCompact);
            plus.gameObject.GetComponent<LayoutElement>().preferredWidth = 64f;

            return (RectTransform)rowGo.transform;
        }

        static void SetUpDownNavigation(IReadOnlyList<Selectable> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                UnityEngine.UI.Navigation navigation = items[i].navigation;
                navigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                navigation.selectOnUp = items[(i - 1 + items.Count) % items.Count];
                navigation.selectOnDown = items[(i + 1) % items.Count];
                items[i].navigation = navigation;
            }
        }
    }
}
