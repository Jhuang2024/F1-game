using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    public static class UiFactory
    {
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
    }
}
