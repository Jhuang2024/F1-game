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

            TMP_Text label = CreateText(go.transform, "Label", TextStyle.Button, text);
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
            ThemedButton settings = CreateButton(actions, "Btn_Settings", ThemedButton.Variant.Tertiary, "Settings");

            TMP_Text version = CreateText(content, "Version", TextStyle.Caption, "");

            SetUpDownNavigation(new[] { career, quickRace, timeTrial, settings });

            var view = content.parent.gameObject.AddComponent<MainMenuView>();
            view.Bind(hero, context, version, career, quickRace, timeTrial, settings);
            return view;
        }

        public static TrackSelectView BuildTrackSelect(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_TrackSelect", "SELECT CIRCUIT", out TMP_Text header);

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

        public static PreRaceStrategyView BuildStrategy(Transform root)
        {
            UiTheme theme = UiTheme.Active;
            RectTransform content = ScreenScaffold(root, "Screen_PreRaceStrategy", "RACE STRATEGY", out TMP_Text header);

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
