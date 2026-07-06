using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    // One-shot fade-in used for screen transitions; removes itself when done so
    // repeated activations (e.g. the pause overlay) stay cheap.
    public class UiFadeIn : MonoBehaviour
    {
        CanvasGroup group;
        float elapsed;
        const float Duration = 0.18f;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = 0f;
        }

        void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(elapsed / Duration);
            if (elapsed >= Duration)
            {
                Destroy(this);
            }
        }
    }

    public static class UiFactory
    {
        // Set from settings; screens fade in briefly when enabled.
        public static bool AnimationsEnabled = true;

        // Shared dark-motorsport theme so every screen reads as one product.
        public static readonly Color Accent = new Color(0.95f, 0.08f, 0.06f, 1f);
        public static readonly Color AccentCyan = new Color(0.2f, 0.72f, 1f, 1f);
        public static readonly Color PanelDark = new Color(0.018f, 0.026f, 0.034f, 0.94f);
        public static readonly Color PanelDarker = new Color(0.006f, 0.009f, 0.012f, 0.82f);
        public static readonly Color TextPrimary = new Color(0.94f, 0.97f, 1f, 1f);
        public static readonly Color TextMuted = new Color(0.68f, 0.78f, 0.84f, 1f);
        public static readonly Color RowEven = new Color(0.04f, 0.055f, 0.064f, 0.74f);
        public static readonly Color RowOdd = new Color(0.04f, 0.055f, 0.064f, 0.42f);

        static UnityEngine.Font cachedFont;

        public static UnityEngine.Font Font
        {
            get
            {
                if (cachedFont != null)
                {
                    return cachedFont;
                }

                cachedFont = UnityEngine.Font.CreateDynamicFontFromOSFont("Helvetica Neue", 16);
                if (cachedFont == null)
                {
                    cachedFont = UnityEngine.Font.CreateDynamicFontFromOSFont("Avenir Next", 16);
                }

                if (cachedFont == null)
                {
                    cachedFont = LoadBuiltInFont("LegacyRuntime.ttf");
                }

                return cachedFont;
            }
        }

        static UnityEngine.Font LoadBuiltInFont(string resourceName)
        {
            try
            {
                return Resources.GetBuiltinResource<UnityEngine.Font>(resourceName);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static Canvas CreateCanvas(string name)
        {
            GameObject canvasObject = new GameObject(name);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            return canvas;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject("Runtime EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
        }

        public static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            GameObject panelObject = new GameObject(name);
            panelObject.transform.SetParent(parent, false);
            RectTransform rect = panelObject.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = panelObject.AddComponent<Image>();
            image.color = color;
            if (AnimationsEnabled)
            {
                panelObject.AddComponent<UiFadeIn>();
            }

            return rect;
        }

        public static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject rectObject = new GameObject(name);
            rectObject.transform.SetParent(parent, false);
            RectTransform rect = rectObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        public static RectTransform CreateBand(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return rect;
        }

        public static Text CreateText(Transform parent, string name, string value, int size, Color color, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            Text text = textObject.AddComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            RectTransform rect = text.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(420f, 36f);
            return text;
        }

        public static Button CreateButton(Transform parent, string label, UnityAction action)
        {
            GameObject buttonObject = new GameObject(label + " button");
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.018f, 0.026f, 0.033f, 0.96f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.018f, 0.026f, 0.033f, 0.96f);
            colors.highlightedColor = new Color(0.78f, 0.06f, 0.055f, 1f);
            colors.pressedColor = new Color(0.006f, 0.01f, 0.014f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                SimpleAudioManager.PlayClick();
                action.Invoke();
            });
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(296f, 50f);

            CreateBand(buttonObject.transform, "Button top sheen", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), Vector2.zero, new Color(1f, 1f, 1f, 0.16f));
            RectTransform accent = CreateBand(buttonObject.transform, "Accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(5f, 0f), new Color(0.92f, 0.08f, 0.06f, 0.95f));
            accent.SetAsFirstSibling();

            Text text = CreateText(buttonObject.transform, "Label", label, 20, new Color(0.92f, 0.96f, 0.98f), TextAnchor.MiddleCenter);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 0f);
            textRect.offsetMax = new Vector2(-12f, 0f);
            return button;
        }

        public static InputField CreateInputField(Transform parent, string defaultText)
        {
            GameObject inputObject = new GameObject("Driver name input");
            inputObject.transform.SetParent(parent, false);
            Image image = inputObject.AddComponent<Image>();
            image.color = new Color(0.02f, 0.028f, 0.034f, 0.98f);
            InputField input = inputObject.AddComponent<InputField>();
            RectTransform rect = inputObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(340f, 48f);

            Text text = CreateText(inputObject.transform, "Text", defaultText, 20, Color.white, TextAnchor.MiddleLeft);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 0f);
            textRect.offsetMax = new Vector2(-14f, 0f);

            Text placeholder = CreateText(inputObject.transform, "Placeholder", "Driver name", 20, new Color(0.5f, 0.58f, 0.62f), TextAnchor.MiddleLeft);
            RectTransform placeholderRect = placeholder.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(14f, 0f);
            placeholderRect.offsetMax = new Vector2(-14f, 0f);

            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = defaultText;
            return input;
        }

        public static VerticalLayoutGroup AddVerticalLayout(RectTransform rect, int spacing, RectOffset padding)
        {
            VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            return layout;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(RectTransform rect, int spacing, RectOffset padding)
        {
            HorizontalLayoutGroup layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            return layout;
        }

        public static void SetSize(Component component, float width, float height)
        {
            RectTransform rect = component.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
        }

        public static string FormatTime(float seconds)
        {
            if (seconds <= 0f)
            {
                return "--:--.---";
            }

            TimeSpan span = TimeSpan.FromSeconds(seconds);
            return span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00") + "." + span.Milliseconds.ToString("000");
        }

        // ---------- Modern component helpers ----------

        public static RectTransform CreateCard(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform card = CreateBand(parent, name, anchorMin, anchorMax, Vector2.zero, Vector2.zero, PanelDark);
            CreateBand(card, name + " accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -3f), Vector2.zero, Accent);
            return card;
        }

        public static Text CreateHeader(Transform parent, string value)
        {
            Text header = CreateText(parent, value + " header", value, 40, TextPrimary, TextAnchor.MiddleLeft);
            SetSize(header, 720f, 54f);
            return header;
        }

        public static Text CreateSubHeader(Transform parent, string value)
        {
            Text header = CreateText(parent, value + " subheader", value.ToUpperInvariant(), 20, Accent, TextAnchor.MiddleLeft);
            SetSize(header, 620f, 30f);
            return header;
        }

        public static Button CreatePrimaryButton(Transform parent, string label, UnityAction action)
        {
            return CreateButton(parent, label, action);
        }

        public static Button CreateSecondaryButton(Transform parent, string label, UnityAction action)
        {
            Button button = CreateButton(parent, label, action);
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.1f, 0.16f, 0.22f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            Transform accent = button.transform.Find("Accent");
            if (accent != null)
            {
                Image accentImage = accent.GetComponent<Image>();
                if (accentImage != null)
                {
                    accentImage.color = new Color(0.4f, 0.5f, 0.58f, 0.9f);
                }
            }

            return button;
        }

        public static RectTransform CreateDivider(Transform parent)
        {
            RectTransform divider = CreateBand(parent, "Divider", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.1f));
            divider.sizeDelta = new Vector2(560f, 2f);
            return divider;
        }

        public static Text CreateStatCard(Transform parent, string label, string value, float width)
        {
            RectTransform card = CreateRect(parent, label + " stat card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(width, 74f);
            Image background = card.gameObject.AddComponent<Image>();
            background.color = PanelDarker;
            CreateBand(card, "Stat rule", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), Accent);
            Text labelText = CreateText(card, "Stat label", label.ToUpperInvariant(), 13, TextMuted, TextAnchor.UpperLeft);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(14f, -30f);
            labelRect.offsetMax = new Vector2(-8f, -8f);
            Text valueText = CreateText(card, "Stat value", value, 24, TextPrimary, TextAnchor.LowerLeft);
            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = Vector2.zero;
            valueRect.anchorMax = Vector2.one;
            valueRect.offsetMin = new Vector2(14f, 8f);
            valueRect.offsetMax = new Vector2(-8f, -30f);
            return valueText;
        }

        public static Image CreateProgressBar(Transform parent, string name, float width, float height, Color fillColor, float value01)
        {
            RectTransform track = CreateRect(parent, name + " bar", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            track.sizeDelta = new Vector2(width, height);
            Image trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(0.12f, 0.15f, 0.17f, 0.9f);
            RectTransform fill = CreateBand(track, name + " fill", Vector2.zero, new Vector2(Mathf.Clamp01(value01), 1f), Vector2.zero, Vector2.zero, fillColor);
            return fill.GetComponent<Image>();
        }

        public static Text CreatePillLabel(Transform parent, string value, Color color)
        {
            RectTransform pill = CreateRect(parent, value + " pill", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            pill.sizeDelta = new Vector2(Mathf.Max(64f, value.Length * 11f + 26f), 26f);
            Image background = pill.gameObject.AddComponent<Image>();
            background.color = new Color(color.r, color.g, color.b, 0.2f);
            Text text = CreateText(pill, "Pill text", value.ToUpperInvariant(), 13, color, TextAnchor.MiddleCenter);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return text;
        }

        // Scrollable vertical panel. Returns the content RectTransform; add children to it,
        // they stack top-down and the panel scrolls when content overflows.
        public static RectTransform CreateScrollPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, int spacing, RectOffset padding)
        {
            RectTransform viewport = CreateBand(parent, name, anchorMin, anchorMax, Vector2.zero, Vector2.zero, PanelDarker);
            viewport.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            RectTransform content = CreateRect(viewport, name + " content", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            VerticalLayoutGroup layout = AddVerticalLayout(content, spacing, padding);
            layout.childAlignment = TextAnchor.UpperLeft;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            return content;
        }

        public static RectTransform CreateBackdrop(Transform parent, string name)
        {
            return CreatePanel(parent, name, new Color(0f, 0f, 0f, 0.72f));
        }

        // Simple centered modal card on a dimmed backdrop. Returns the card content rect.
        public static RectTransform CreateModal(Transform parent, string title, out GameObject root)
        {
            RectTransform backdrop = CreateBackdrop(parent, title + " modal backdrop");
            root = backdrop.gameObject;
            RectTransform card = CreateCard(backdrop, title + " modal card", new Vector2(0.34f, 0.28f), new Vector2(0.66f, 0.72f));
            Text heading = CreateText(card, "Modal title", title, 30, TextPrimary, TextAnchor.UpperLeft);
            RectTransform headingRect = heading.GetComponent<RectTransform>();
            headingRect.anchorMin = new Vector2(0f, 1f);
            headingRect.anchorMax = new Vector2(1f, 1f);
            headingRect.offsetMin = new Vector2(24f, -64f);
            headingRect.offsetMax = new Vector2(-24f, -16f);
            RectTransform content = CreateRect(card, "Modal content", Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -70f));
            AddVerticalLayout(content, 10, new RectOffset(0, 0, 0, 0));
            return content;
        }

        public static RectTransform CreateTopNav(Transform parent, string title)
        {
            RectTransform nav = CreateBand(parent, "Top nav", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -92f), Vector2.zero, PanelDarker);
            CreateBand(nav, "Top nav rule", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f), Accent);
            Text heading = CreateText(nav, "Nav title", title, 38, TextPrimary, TextAnchor.MiddleLeft);
            RectTransform headingRect = heading.GetComponent<RectTransform>();
            headingRect.anchorMin = new Vector2(0f, 0f);
            headingRect.anchorMax = new Vector2(0.6f, 1f);
            headingRect.offsetMin = new Vector2(64f, 0f);
            headingRect.offsetMax = Vector2.zero;
            return nav;
        }
    }
}
