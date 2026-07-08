using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    // Screen transition: fade plus a short upward slide so screens arrive with
    // motion instead of popping. Removes itself when done so repeated
    // activations (e.g. the pause overlay) stay cheap.
    public class UiFadeIn : MonoBehaviour
    {
        CanvasGroup group;
        RectTransform rect;
        Vector2 restPosition;
        float elapsed;
        const float Duration = 0.22f;
        const float SlideDistance = 18f;

        // Optional stagger before this element starts fading in - set right
        // after AddComponent<UiFadeIn>() to build a cascading reveal (e.g. a
        // qualifying/race results table appearing row by row) out of the same
        // single-element fade instead of a bespoke coroutine per screen.
        public float startDelay;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = gameObject.AddComponent<CanvasGroup>();
            }

            rect = GetComponent<RectTransform>();
            if (rect != null)
            {
                restPosition = rect.anchoredPosition;
                rect.anchoredPosition = restPosition - new Vector2(0f, SlideDistance);
            }

            group.alpha = 0f;
        }

        void Update()
        {
            if (startDelay > 0f)
            {
                startDelay -= Time.unscaledDeltaTime;
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Duration);
            float eased = 1f - (1f - t) * (1f - t);
            group.alpha = eased;
            if (rect != null)
            {
                rect.anchoredPosition = Vector2.Lerp(restPosition - new Vector2(0f, SlideDistance), restPosition, eased);
            }

            if (t >= 1f)
            {
                if (rect != null)
                {
                    rect.anchoredPosition = restPosition;
                }

                Destroy(this);
            }
        }
    }

    // Handle for a HUD pill widget so callers can restyle background and label together.
    public class HudPill
    {
        public RectTransform root;
        public Image background;
        public Text label;

        public void SetState(string text, Color color, bool lit)
        {
            if (label != null)
            {
                label.text = text;
                label.color = lit ? Color.white : color;
            }

            if (background != null)
            {
                background.color = lit
                    ? new Color(color.r, color.g, color.b, 0.92f)
                    : new Color(color.r * 0.24f, color.g * 0.24f, color.b * 0.24f, 0.6f);
            }
        }
    }

    // Slow alpha pulse used for animated accents and live status dots; runs on
    // unscaled time so it keeps breathing while the game is paused.
    public class UiPulse : MonoBehaviour
    {
        public float speed = 1.2f;
        public float minAlpha = 0.25f;
        public float maxAlpha = 0.8f;
        Graphic graphic;

        void Awake()
        {
            graphic = GetComponent<Graphic>();
        }

        void Update()
        {
            if (graphic == null)
            {
                return;
            }

            float wave = (Mathf.Sin(Time.unscaledTime * speed) + 1f) * 0.5f;
            Color color = graphic.color;
            color.a = Mathf.Lerp(minAlpha, maxAlpha, wave);
            graphic.color = color;
        }
    }

    // One-shot scale "punch" for a value that just changed - a lap counter
    // ticking over, a pit-phase pill flipping to its next state, and similar
    // discrete HUD events that used to update silently with no motion at all.
    // Call Play() and the element snaps slightly larger then eases back to
    // rest, the same mechanical-weight cue RaceHud's start-light sequence
    // hand-rolls per light (a decaying float driving localScale), but as a
    // reusable component so callers don't need their own per-frame timer.
    // Runs on unscaled time so it still plays while the game is paused,
    // matching UiPulse/UiFadeIn. Callers should gate the Play() call itself on
    // UiFactory.AnimationsEnabled so the "reduce motion" setting is respected,
    // the same convention CreatePulsingDot/UiFadeIn already use.
    public class UiPunch : MonoBehaviour
    {
        public float strength = 0.22f;
        public float decaySpeed = 3.2f;
        RectTransform rect;
        float punch;

        void Awake()
        {
            rect = GetComponent<RectTransform>();
        }

        public void Play()
        {
            punch = 1f;
        }

        void Update()
        {
            if (rect == null || punch <= 0f)
            {
                return;
            }

            punch = Mathf.Max(0f, punch - Time.unscaledDeltaTime * decaySpeed);
            float scale = 1f + punch * strength;
            rect.localScale = new Vector3(scale, scale, 1f);
            if (punch <= 0f)
            {
                rect.localScale = Vector3.one;
            }
        }
    }

    // Glides a meter fill's right edge (anchorMax.x) toward a target value each
    // frame instead of letting callers snap it, so tyre wear/fuel/damage/pit
    // meters read as animating rather than teleporting between states.
    public class UiFillLerp : MonoBehaviour
    {
        public float target = 1f;
        public float speed = 5f;
        RectTransform rect;

        void Awake()
        {
            rect = GetComponent<RectTransform>();
        }

        void Update()
        {
            if (rect == null)
            {
                return;
            }

            float current = rect.anchorMax.x;
            if (Mathf.Abs(current - target) < 0.0008f)
            {
                return;
            }

            float next = Mathf.Lerp(current, target, Time.unscaledDeltaTime * speed);
            rect.anchorMax = new Vector2(Mathf.Clamp01(next), rect.anchorMax.y);
        }
    }

    // Button microinteractions: hover pop, press squash, and a light sheen band
    // that sweeps across the face on pointer enter. Pure transform/alpha work on
    // unscaled time, so it stays responsive in pause menus.
    public class UiButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public float hoverScale = 1.025f;
        public float pressScale = 0.97f;
        public RectTransform sheen;
        public CanvasGroup sheenGroup;

        RectTransform rect;
        bool hovered;
        bool pressed;
        float sheenProgress = 1f;
        float faceWidth = 300f;

        void Awake()
        {
            rect = GetComponent<RectTransform>();
        }

        void OnEnable()
        {
            if (rect != null)
            {
                rect.localScale = Vector3.one;
            }

            sheenProgress = 1f;
            if (sheenGroup != null)
            {
                sheenGroup.alpha = 0f;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            if (UiFactory.AnimationsEnabled)
            {
                sheenProgress = 0f;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
        }

        void Update()
        {
            if (rect == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            float targetScale = pressed ? pressScale : (hovered ? hoverScale : 1f);
            float current = Mathf.Lerp(rect.localScale.x, targetScale, dt * 14f);
            rect.localScale = new Vector3(current, current, 1f);

            if (sheen == null || sheenGroup == null)
            {
                return;
            }

            if (sheenProgress < 1f)
            {
                faceWidth = Mathf.Max(80f, rect.rect.width);
                sheenProgress = Mathf.Min(1f, sheenProgress + dt * 2.4f);
                sheen.anchoredPosition = new Vector2(Mathf.Lerp(-faceWidth * 0.55f, faceWidth * 0.55f, sheenProgress), 0f);
                sheenGroup.alpha = Mathf.Sin(sheenProgress * Mathf.PI) * 0.55f;
            }
            else if (sheenGroup.alpha > 0f)
            {
                sheenGroup.alpha = 0f;
            }
        }
    }

    // Reusable center-screen "big moment" flash text: fades in fast, holds, then
    // fades out. One instance is created per HUD and reused across lights-out,
    // the safety-car restart green flag, final lap, and the chequered finish
    // moment, instead of each caller hand-rolling its own timer/alpha logic.
    public class UiBigFlash : MonoBehaviour
    {
        Text text;
        Color baseColor = Color.white;
        float timer;
        float totalDuration = 1f;
        const float FadeInTime = 0.12f;
        const float FadeOutTime = 0.6f;

        void Awake()
        {
            text = GetComponent<Text>();
        }

        public void Show(string message, Color color, float holdSeconds)
        {
            if (text == null)
            {
                return;
            }

            text.text = message;
            baseColor = color;
            totalDuration = FadeInTime + Mathf.Max(0.1f, holdSeconds) + FadeOutTime;
            timer = totalDuration;
        }

        void Update()
        {
            if (text == null || timer <= 0f)
            {
                return;
            }

            timer -= Time.unscaledDeltaTime;
            float elapsed = totalDuration - timer;
            float alpha = elapsed < FadeInTime ? elapsed / FadeInTime : (timer < FadeOutTime ? timer / FadeOutTime : 1f);
            Color color = baseColor;
            color.a = Mathf.Clamp01(alpha);
            text.color = color;

            if (timer <= 0f)
            {
                text.text = "";
            }
        }
    }

    public static class UiFactory
    {
        // Set from settings; screens fade in briefly when enabled.
        public static bool AnimationsEnabled = true;

        // Reference resolution every screen in this file is hand-laid-out against
        // (font sizes, card widths, margins - all the literal pixel values scattered
        // through this file and RuntimeUi/RaceHud assume this canvas). Exposed as a
        // constant so ApplyUiScale can derive a scaled reference resolution from the
        // same source CreateCanvas uses, instead of duplicating the 1920x1080 literal.
        public static readonly Vector2 BaseReferenceResolution = new Vector2(1920f, 1080f);

        // Global "UI Scale" player setting (see GameSettingsStore.UiScale): a single
        // chokepoint multiplier applied to the whole runtime canvas via
        // ApplyUiScale, rather than touching every individual font-size call site.
        // Kept as a static so any screen can read the current value without a
        // settings reference on hand; RuntimeUi keeps it in sync with the store.
        public static float GlobalUiScale = 1f;

        // Rescales an already-created runtime canvas by adjusting its
        // CanvasScaler's reference resolution instead of the (ScaleWithScreenSize
        // mode ignores CanvasScaler.scaleFactor entirely, so that field is not a
        // usable hook here). Shrinking the reference resolution by `scale` makes
        // every fixed-pixel element in the canvas render `scale`x larger and vice
        // versa - the same effect a player expects from a "UI Scale" slider,
        // applied uniformly to every screen built on this canvas (menus AND the
        // race HUD, since both are built under the one canvas RuntimeUi owns).
        public static void ApplyUiScale(Canvas canvas, float scale)
        {
            if (canvas == null)
            {
                return;
            }

            GlobalUiScale = Mathf.Clamp(scale, 0.5f, 2f);
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.referenceResolution = BaseReferenceResolution / GlobalUiScale;
            }
        }

        // Shared dark-glass motorsport theme so every screen reads as one product.
        public static readonly Color Accent = new Color(0.95f, 0.08f, 0.06f, 1f);
        public static readonly Color AccentCyan = new Color(0.2f, 0.72f, 1f, 1f);
        public static readonly Color AccentGreen = new Color(0.35f, 0.95f, 0.5f, 1f);
        public static readonly Color AccentAmber = new Color(1f, 0.78f, 0.22f, 1f);
        public static readonly Color AccentPurple = new Color(0.72f, 0.42f, 1f, 1f);
        public static readonly Color PanelDark = new Color(0.028f, 0.038f, 0.05f, 0.93f);
        public static readonly Color PanelDarker = new Color(0.012f, 0.017f, 0.024f, 0.86f);
        public static readonly Color HudCardBackground = new Color(0.014f, 0.02f, 0.028f, 0.84f);
        public static readonly Color MeterTrack = new Color(0.1f, 0.13f, 0.155f, 0.9f);
        public static readonly Color TextPrimary = new Color(0.94f, 0.97f, 1f, 1f);
        public static readonly Color TextMuted = new Color(0.62f, 0.72f, 0.8f, 1f);
        public static readonly Color RowEven = new Color(0.055f, 0.075f, 0.095f, 0.78f);
        public static readonly Color RowOdd = new Color(0.045f, 0.06f, 0.078f, 0.5f);
        public static readonly Color GlassHighlight = new Color(1f, 1f, 1f, 0.07f);

        // Shared spacing rhythm for HUD card stacks and reference-style row lists,
        // so panels read as one consistent grid instead of every call site picking
        // its own gap.
        public const float HudCardSpacing = 10f;

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

        // ---------- generated sprites ----------
        // All chrome is generated at runtime: rounded glass rectangles with a baked
        // vertical gradient, a small-radius variant for pills, and a soft radial
        // glow for status dots. One texture each, cached forever, tinted per use.

        static Sprite roundedSprite;
        static Sprite roundedSmallSprite;
        static Sprite glowSprite;

        public static Sprite RoundedSprite
        {
            get
            {
                if (roundedSprite == null)
                {
                    roundedSprite = BuildRoundedSprite(64, 16, 1.0f, 0.82f, "Ui rounded glass");
                }

                return roundedSprite;
            }
        }

        public static Sprite RoundedSmallSprite
        {
            get
            {
                if (roundedSmallSprite == null)
                {
                    roundedSmallSprite = BuildRoundedSprite(32, 8, 1.0f, 0.9f, "Ui rounded small");
                }

                return roundedSmallSprite;
            }
        }

        public static Sprite GlowSprite
        {
            get
            {
                if (glowSprite == null)
                {
                    glowSprite = BuildGlowSprite(64);
                }

                return glowSprite;
            }
        }

        static Sprite BuildRoundedSprite(int size, int radius, float topBrightness, float bottomBrightness, string spriteName)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = spriteName;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float brightness = Mathf.Lerp(bottomBrightness, topBrightness, y / (float)(size - 1));
                for (int x = 0; x < size; x++)
                {
                    float alpha = RoundedRectAlpha(x + 0.5f, y + 0.5f, size, radius);
                    pixels[y * size + x] = new Color(brightness, brightness, brightness, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            float border = radius + 4;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        static float RoundedRectAlpha(float x, float y, int size, int radius)
        {
            float left = radius;
            float right = size - radius;
            float bottom = radius;
            float top = size - radius;
            float cx = Mathf.Clamp(x, left, right);
            float cy = Mathf.Clamp(y, bottom, top);
            float distance = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return Mathf.Clamp01(radius - distance + 1f);
        }

        static Sprite BuildGlowSprite(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Ui soft glow";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, falloff * falloff);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        static Sprite checkeredSprite;

        // Alternating black/white checker tile - the finish-line motif, generated
        // once and reused for the chequered finish flourish (and sparingly as a
        // results-screen accent) instead of drawing bespoke geometry per caller.
        public static Sprite CheckeredSprite
        {
            get
            {
                if (checkeredSprite == null)
                {
                    checkeredSprite = BuildCheckeredSprite(40, 10);
                }

                return checkeredSprite;
            }
        }

        static Sprite BuildCheckeredSprite(int size, int cell)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Ui checkered";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Point;
            Color[] pixels = new Color[size * size];
            Color dark = new Color(0.05f, 0.06f, 0.07f, 1f);
            Color light = new Color(0.92f, 0.94f, 0.97f, 1f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool darkSquare = ((x / cell) + (y / cell)) % 2 == 0;
                    pixels[y * size + x] = darkSquare ? dark : light;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // Tiled checker strip - reused by the chequered finish flourish and by
        // the results screen header.
        public static Image CreateCheckeredBand(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = CheckeredSprite;
            image.type = Image.Type.Tiled;
            image.raycastTarget = false;
            return image;
        }

        // Applies the rounded glass sprite to an image and tints it.
        public static Image StyleRounded(Image image, Color color)
        {
            image.sprite = RoundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        public static Image StyleRoundedSmall(Image image, Color color)
        {
            image.sprite = RoundedSmallSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        // ---------- canvas / primitives ----------

        public static Canvas CreateCanvas(string name)
        {
            GameObject canvasObject = new GameObject(name);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = BaseReferenceResolution;
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

        // Rounded glass panel: gradient face, hairline top highlight. The building
        // block for every card/panel in menus and HUD.
        public static RectTransform CreateGlassPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            Image image = rect.gameObject.AddComponent<Image>();
            StyleRounded(image, color);
            RectTransform highlight = CreateRect(rect, name + " glass highlight", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -3f), new Vector2(-10f, -1f));
            Image highlightImage = highlight.gameObject.AddComponent<Image>();
            highlightImage.color = GlassHighlight;
            highlightImage.raycastTarget = false;
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
            // A GameObject.AddComponent<Text>() (as opposed to the Editor's GameObject
            // > UI menu) leaves the RectTransform at Unity's raw default anchors,
            // which are NOT top-left. Every call site in this codebase positions text
            // with anchoredPosition assuming (x right, y down) from a top-left corner,
            // so without this the whole file's positioning math silently misfires -
            // this single default is the root cause behind more than one "overlap"
            // report. Callers that need different anchors already set them explicitly
            // afterward, so this only fixes the many call sites that didn't.
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(420f, 36f);
            return text;
        }

        // ---------- buttons ----------

        enum ButtonVariant
        {
            Standard,
            Primary,
            Secondary,
            Danger
        }

        public static Button CreateButton(Transform parent, string label, UnityAction action)
        {
            return CreateStyledButton(parent, label, action, ButtonVariant.Standard);
        }

        public static Button CreatePrimaryButton(Transform parent, string label, UnityAction action)
        {
            return CreateStyledButton(parent, label, action, ButtonVariant.Primary);
        }

        public static Button CreateSecondaryButton(Transform parent, string label, UnityAction action)
        {
            return CreateStyledButton(parent, label, action, ButtonVariant.Secondary);
        }

        public static Button CreateDangerButton(Transform parent, string label, UnityAction action)
        {
            return CreateStyledButton(parent, label, action, ButtonVariant.Danger);
        }

        static Button CreateStyledButton(Transform parent, string label, UnityAction action, ButtonVariant variant)
        {
            GameObject buttonObject = new GameObject(label + " button");
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.AddComponent<Image>();

            Color face;
            Color hover;
            Color accentColor;
            Color textColor = new Color(0.93f, 0.96f, 0.99f);
            switch (variant)
            {
                case ButtonVariant.Primary:
                    face = new Color(0.62f, 0.05f, 0.045f, 0.98f);
                    hover = new Color(0.85f, 0.1f, 0.08f, 1f);
                    accentColor = new Color(1f, 0.42f, 0.32f, 1f);
                    break;
                case ButtonVariant.Secondary:
                    face = new Color(0.05f, 0.075f, 0.1f, 0.88f);
                    hover = new Color(0.11f, 0.17f, 0.23f, 0.98f);
                    accentColor = new Color(0.4f, 0.52f, 0.62f, 0.9f);
                    textColor = new Color(0.78f, 0.86f, 0.92f);
                    break;
                case ButtonVariant.Danger:
                    face = new Color(0.16f, 0.03f, 0.03f, 0.94f);
                    hover = new Color(0.4f, 0.05f, 0.05f, 1f);
                    accentColor = new Color(1f, 0.25f, 0.2f, 1f);
                    break;
                default:
                    face = new Color(0.04f, 0.06f, 0.085f, 0.94f);
                    hover = new Color(0.62f, 0.07f, 0.06f, 0.98f);
                    accentColor = Accent;
                    break;
            }

            StyleRounded(image, face);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = face;
            colors.highlightedColor = hover;
            colors.pressedColor = new Color(face.r * 0.55f, face.g * 0.55f, face.b * 0.55f, 1f);
            colors.selectedColor = hover;
            colors.disabledColor = new Color(face.r, face.g, face.b, 0.32f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                SimpleAudioManager.PlayClick();
                action.Invoke();
            });
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(296f, 50f);

            // Accent chip on the left edge.
            RectTransform accent = CreateRect(buttonObject.transform, "Accent", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            accent.sizeDelta = new Vector2(4f, 26f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.anchoredPosition = new Vector2(7f, 0f);
            Image accentImage = accent.gameObject.AddComponent<Image>();
            StyleRoundedSmall(accentImage, accentColor);
            accentImage.raycastTarget = false;

            // Sheen band swept by UiButtonFx on hover.
            RectTransform sheenMask = CreateRect(buttonObject.transform, "Sheen mask", Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            sheenMask.gameObject.AddComponent<RectMask2D>();
            RectTransform sheen = CreateRect(sheenMask, "Sheen", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            sheen.sizeDelta = new Vector2(58f, 0f);
            sheen.localRotation = Quaternion.Euler(0f, 0f, 14f);
            Image sheenImage = sheen.gameObject.AddComponent<Image>();
            sheenImage.color = new Color(1f, 1f, 1f, 0.35f);
            sheenImage.raycastTarget = false;
            CanvasGroup sheenGroup = sheen.gameObject.AddComponent<CanvasGroup>();
            sheenGroup.alpha = 0f;
            sheenGroup.blocksRaycasts = false;

            UiButtonFx fx = buttonObject.AddComponent<UiButtonFx>();
            fx.sheen = sheen;
            fx.sheenGroup = sheenGroup;

            Text text = CreateText(buttonObject.transform, "Label", label, 19, textColor, TextAnchor.MiddleCenter);
            text.raycastTarget = false;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 0f);
            textRect.offsetMax = new Vector2(-12f, 0f);
            return button;
        }

        // Marks a button as the currently active choice (tab, selected tyre, etc.).
        public static void SetButtonSelected(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            Image face = button.targetGraphic as Image;
            if (face != null && selected)
            {
                face.color = new Color(0.62f, 0.05f, 0.045f, 0.98f);
                ColorBlock colors = button.colors;
                colors.normalColor = face.color;
                colors.highlightedColor = new Color(0.85f, 0.1f, 0.08f, 1f);
                colors.selectedColor = colors.highlightedColor;
                button.colors = colors;
            }

            Transform accent = button.transform.Find("Accent");
            if (accent != null)
            {
                Image accentImage = accent.GetComponent<Image>();
                if (accentImage != null)
                {
                    accentImage.color = selected ? Color.white : Accent;
                }
            }
        }

        // Mode-selection card: title, optional one-line description, full hover
        // pop/press/sheen feedback via the same UiButtonFx used by buttons, but
        // shaped and styled as a small designed card rather than a bar. Used for
        // the main menu's primary entry points so the front door reads as a set
        // of choices, not a stacked button list.
        public static Button CreateModeCard(Transform parent, string title, string description, Color accentColor, bool primary, UnityAction action)
        {
            GameObject cardObject = new GameObject(title + " mode card");
            cardObject.transform.SetParent(parent, false);
            Image image = cardObject.AddComponent<Image>();

            Color face = primary ? new Color(0.55f, 0.048f, 0.04f, 0.96f) : new Color(0.035f, 0.05f, 0.068f, 0.92f);
            Color hover = primary ? new Color(0.78f, 0.09f, 0.07f, 1f) : new Color(0.08f, 0.12f, 0.16f, 1f);
            StyleRounded(image, face);

            Button button = cardObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = face;
            colors.highlightedColor = hover;
            colors.pressedColor = new Color(face.r * 0.55f, face.g * 0.55f, face.b * 0.55f, 1f);
            colors.selectedColor = hover;
            colors.disabledColor = new Color(face.r, face.g, face.b, 0.32f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                SimpleAudioManager.PlayClick();
                action.Invoke();
            });

            RectTransform rect = cardObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(440f, string.IsNullOrEmpty(description) ? 54f : 72f);

            RectTransform accent = CreateRect(cardObject.transform, "Accent", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 8f), new Vector2(4f, -8f));
            Image accentImage = accent.gameObject.AddComponent<Image>();
            StyleRoundedSmall(accentImage, accentColor);
            accentImage.raycastTarget = false;

            RectTransform sheenMask = CreateRect(cardObject.transform, "Sheen mask", Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            sheenMask.gameObject.AddComponent<RectMask2D>();
            RectTransform sheen = CreateRect(sheenMask, "Sheen", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            sheen.sizeDelta = new Vector2(72f, 0f);
            sheen.localRotation = Quaternion.Euler(0f, 0f, 14f);
            Image sheenImage = sheen.gameObject.AddComponent<Image>();
            sheenImage.color = new Color(1f, 1f, 1f, 0.3f);
            sheenImage.raycastTarget = false;
            CanvasGroup sheenGroup = sheen.gameObject.AddComponent<CanvasGroup>();
            sheenGroup.alpha = 0f;
            sheenGroup.blocksRaycasts = false;

            UiButtonFx fx = cardObject.AddComponent<UiButtonFx>();
            fx.hoverScale = 1.018f;
            fx.sheen = sheen;
            fx.sheenGroup = sheenGroup;

            Text titleText = CreateText(cardObject.transform, "Title", title, 21, primary ? Color.white : TextPrimary, TextAnchor.UpperLeft);
            titleText.fontStyle = FontStyle.Bold;
            titleText.raycastTarget = false;
            RectTransform titleRect = titleText.GetComponent<RectTransform>();
            if (string.IsNullOrEmpty(description))
            {
                titleRect.anchorMin = new Vector2(0f, 0f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.offsetMin = new Vector2(24f, 0f);
                titleRect.offsetMax = new Vector2(-16f, 0f);
            }
            else
            {
                titleRect.anchorMin = new Vector2(0f, 0.5f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.offsetMin = new Vector2(24f, 0f);
                titleRect.offsetMax = new Vector2(-16f, -8f);

                Text descriptionText = CreateText(cardObject.transform, "Description", description, 13, primary ? new Color(1f, 0.86f, 0.82f) : TextMuted, TextAnchor.UpperLeft);
                descriptionText.raycastTarget = false;
                RectTransform descriptionRect = descriptionText.GetComponent<RectTransform>();
                descriptionRect.anchorMin = new Vector2(0f, 0f);
                descriptionRect.anchorMax = new Vector2(1f, 0.5f);
                descriptionRect.offsetMin = new Vector2(24f, 4f);
                descriptionRect.offsetMax = new Vector2(-16f, 0f);
            }

            return button;
        }

        public static InputField CreateInputField(Transform parent, string defaultText)
        {
            GameObject inputObject = new GameObject("Driver name input");
            inputObject.transform.SetParent(parent, false);
            Image image = inputObject.AddComponent<Image>();
            StyleRounded(image, new Color(0.03f, 0.045f, 0.06f, 0.98f));
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

        // A LayoutGroup ALWAYS overwrites its direct children's anchors, so any
        // child that previously relied on a stretched/fractional anchor (e.g. "35%
        // of parent width") silently collapses once it's placed inside one of the
        // layout groups above - this was the root cause of tyre selection panels
        // squeezing text to a single character per line. Use this pair instead for
        // any horizontal split where a column needs a real, guaranteed width:
        // AddResponsiveHorizontalLayout on the row, SetLayoutWidth on each column.
        public static HorizontalLayoutGroup AddResponsiveHorizontalLayout(RectTransform rect, int spacing, RectOffset padding)
        {
            HorizontalLayoutGroup layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            return layout;
        }

        // Vertical list of full-width setting rows. AddVerticalLayout defaults to
        // childControlWidth/childForceExpandWidth = false so its many other callers
        // (tyre grids, mode-card menus, table lists) keep their own hand-picked row
        // widths - flipping those defaults globally would break every one of them.
        // Setting rows need the opposite: CreateSettingRow no longer hardcodes a
        // pixel width, so whatever list holds them must actually stretch each row to
        // fill the available width instead of leaving it at zero. Use this instead of
        // AddVerticalLayout for any list built specifically to hold CreateSettingRow
        // children.
        public static VerticalLayoutGroup AddSettingsListLayout(RectTransform rect, int spacing, RectOffset padding)
        {
            VerticalLayoutGroup layout = AddVerticalLayout(rect, spacing, padding);
            StretchListChildrenWidth(rect);
            return layout;
        }

        // Switches an already-built VerticalLayoutGroup (from AddVerticalLayout, or
        // the one CreateScrollPanel attaches to its content rect internally) so its
        // children stretch to the full available width. Lets a CreateScrollPanel
        // result be turned into a settings list without duplicating its layout setup.
        public static void StretchListChildrenWidth(RectTransform listWithLayout)
        {
            VerticalLayoutGroup layout = listWithLayout.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
            }
        }

        // Fixed-width column: give it a firm preferred width so it never collapses.
        public static LayoutElement SetFixedColumnWidth(Component component, float width)
        {
            LayoutElement element = component.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = component.gameObject.AddComponent<LayoutElement>();
            }

            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
            return element;
        }

        // Flexible column: takes whatever width remains after fixed columns and
        // spacing, with a floor so it never gets squeezed below usability.
        public static LayoutElement SetFlexibleColumnWidth(Component component, float minWidth)
        {
            LayoutElement element = component.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = component.gameObject.AddComponent<LayoutElement>();
            }

            element.minWidth = minWidth;
            element.flexibleWidth = 1f;
            return element;
        }

        // Fixed-height row for a VerticalLayoutGroup with childControlHeight=true -
        // the vertical-axis counterpart to SetFixedColumnWidth.
        public static LayoutElement SetFixedRowHeight(Component component, float height)
        {
            LayoutElement element = component.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = component.gameObject.AddComponent<LayoutElement>();
            }

            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
            return element;
        }

        // Flexible-height row: takes remaining vertical space after fixed rows,
        // with a floor so it never collapses.
        public static LayoutElement SetFlexibleRowHeight(Component component, float minHeight)
        {
            LayoutElement element = component.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = component.gameObject.AddComponent<LayoutElement>();
            }

            element.minHeight = minHeight;
            element.flexibleHeight = 1f;
            return element;
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

        // ---------- modern component helpers ----------

        public static RectTransform CreateCard(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform card = CreateGlassPanel(parent, name, anchorMin, anchorMax, Vector2.zero, Vector2.zero, PanelDark);
            RectTransform accentBar = CreateRect(card, name + " accent", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            accentBar.sizeDelta = new Vector2(64f, 3f);
            accentBar.pivot = new Vector2(0f, 1f);
            accentBar.anchoredPosition = new Vector2(18f, -8f);
            Image accentImage = accentBar.gameObject.AddComponent<Image>();
            StyleRoundedSmall(accentImage, Accent);
            accentImage.raycastTarget = false;
            return card;
        }

        // Card with a real header row (accent-colored title) plus a returned
        // content area beneath it, for panels that show a list or stat block
        // under a heading - replaces the older pattern of a bare CreateCard
        // with a Text dumped straight onto it with hand-tuned offsets.
        public static RectTransform CreateModernCard(Transform parent, string title, Color accentColor, Vector2 anchorMin, Vector2 anchorMax, out RectTransform contentArea)
        {
            RectTransform card = CreateGlassPanel(parent, title + " modern card", anchorMin, anchorMax, Vector2.zero, Vector2.zero, PanelDark);
            RectTransform accent = CreateRect(card, "Modern card accent", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            accent.sizeDelta = new Vector2(56f, 3f);
            accent.pivot = new Vector2(0f, 1f);
            accent.anchoredPosition = new Vector2(20f, -14f);
            Image accentImage = accent.gameObject.AddComponent<Image>();
            StyleRoundedSmall(accentImage, accentColor);
            accentImage.raycastTarget = false;

            Text titleText = CreateText(card, "Modern card title", title.ToUpperInvariant(), 16, accentColor, TextAnchor.UpperLeft);
            RectTransform titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(20f, -44f);
            titleRect.offsetMax = new Vector2(-16f, -20f);

            contentArea = CreateRect(card, "Modern card content", Vector2.zero, Vector2.one, new Vector2(0f, 8f), new Vector2(0f, -52f));
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
            Text header = CreateText(parent, value + " subheader", value.ToUpperInvariant(), 18, Accent, TextAnchor.MiddleLeft);
            SetSize(header, 620f, 28f);
            return header;
        }

        public static RectTransform CreateDivider(Transform parent)
        {
            RectTransform divider = CreateBand(parent, "Divider", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.08f));
            divider.sizeDelta = new Vector2(560f, 1.5f);
            return divider;
        }

        public static Text CreateStatCard(Transform parent, string label, string value, float width)
        {
            RectTransform card = CreateRect(parent, label + " stat card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(width, 74f);
            Image background = card.gameObject.AddComponent<Image>();
            StyleRounded(background, PanelDarker);
            RectTransform rule = CreateRect(card, "Stat rule", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 10f), new Vector2(3f, -10f));
            Image ruleImage = rule.gameObject.AddComponent<Image>();
            StyleRoundedSmall(ruleImage, Accent);
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
            // Layout-audit fix: this card is a fixed 74px height with only a 36px
            // value box at 24pt - a real driver name concatenated with a lap time
            // ("Gabriel Bortoleto  1:23.456") reliably needs to wrap, and
            // CreateText's default vertical Truncate then drops the wrapped
            // second line entirely (the actual time/points, not just the name).
            // Best-fit keeps the value on one line at a smaller size instead of
            // ever losing it.
            valueText.horizontalOverflow = HorizontalWrapMode.Overflow;
            valueText.verticalOverflow = VerticalWrapMode.Overflow;
            valueText.resizeTextForBestFit = true;
            valueText.resizeTextMinSize = 12;
            valueText.resizeTextMaxSize = 24;
            return valueText;
        }

        public static Image CreateProgressBar(Transform parent, string name, float width, float height, Color fillColor, float value01)
        {
            RectTransform track = CreateRect(parent, name + " bar", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            track.sizeDelta = new Vector2(width, height);
            Image trackImage = track.gameObject.AddComponent<Image>();
            StyleRoundedSmall(trackImage, new Color(0.12f, 0.15f, 0.17f, 0.9f));
            RectTransform fill = CreateBand(track, name + " fill", Vector2.zero, new Vector2(Mathf.Clamp01(value01), 1f), Vector2.zero, Vector2.zero, fillColor);
            return fill.GetComponent<Image>();
        }

        public static Text CreatePillLabel(Transform parent, string value, Color color)
        {
            RectTransform pill = CreateRect(parent, value + " pill", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            pill.sizeDelta = new Vector2(Mathf.Max(64f, value.Length * 11f + 26f), 26f);
            Image background = pill.gameObject.AddComponent<Image>();
            StyleRoundedSmall(background, new Color(color.r, color.g, color.b, 0.2f));
            Text text = CreateText(pill, "Pill text", value.ToUpperInvariant(), 13, color, TextAnchor.MiddleCenter);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return text;
        }

        // Reusable wrapping chip row (card overflow fix): lays out short pill
        // labels left-to-right inside `maxWidth`, wrapping onto additional rows
        // instead of ever spilling past it - the plain CreatePillLabel above has
        // no width ceiling at all, which is exactly how long driver/team tags
        // used to stretch outside their card and clip against the neighboring
        // one in the ratings grid. A single chip that still can't fit `maxWidth`
        // on its own gets truncated with an ellipsis rather than overflowing,
        // and every chip's text uses best-fit sizing as a second safety net
        // against width-estimation error. Returns the total height consumed
        // (top of first row to bottom of last row) so callers can position
        // whatever comes next per-card instead of reserving one fixed worst-case
        // gap for every card regardless of how many rows it actually needed.
        public static float CreateWrappingChipRow(Transform parent, string name, IList<string> values, Color color, float originX, float originYFromTop, float maxWidth)
        {
            if (values == null || values.Count == 0)
            {
                return 0f;
            }

            const float chipHeight = 26f;
            const float rowSpacing = 6f;
            const float chipSpacing = 6f;
            const float charWidth = 8.2f;
            const float chipPadding = 24f;
            const float maxChipWidth = 190f;

            float cursorX = 0f;
            float cursorY = 0f;
            for (int i = 0; i < values.Count; i++)
            {
                string label = (values[i] ?? "").ToUpperInvariant();
                float naturalWidth = Mathf.Max(56f, label.Length * charWidth + chipPadding);
                if (naturalWidth > maxWidth)
                {
                    int maxChars = Mathf.Max(3, Mathf.FloorToInt((maxWidth - chipPadding) / charWidth) - 1);
                    if (label.Length > maxChars)
                    {
                        label = label.Substring(0, maxChars) + "...";
                    }
                    naturalWidth = maxWidth;
                }

                float chipWidth = Mathf.Min(naturalWidth, Mathf.Min(maxChipWidth, maxWidth));
                if (cursorX > 0f && cursorX + chipWidth > maxWidth)
                {
                    cursorX = 0f;
                    cursorY += chipHeight + rowSpacing;
                }

                RectTransform pill = CreateRect(parent, name + " chip " + i, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
                pill.pivot = new Vector2(0f, 1f);
                pill.sizeDelta = new Vector2(chipWidth, chipHeight);
                pill.anchoredPosition = new Vector2(originX + cursorX, -(originYFromTop + cursorY));
                Image background = pill.gameObject.AddComponent<Image>();
                StyleRoundedSmall(background, new Color(color.r, color.g, color.b, 0.2f));
                Text text = CreateText(pill, "chip text", label, 13, color, TextAnchor.MiddleCenter);
                RectTransform textRect = text.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(5f, 0f);
                textRect.offsetMax = new Vector2(-5f, 0f);
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 9;
                text.resizeTextMaxSize = 13;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;

                cursorX += chipWidth + chipSpacing;
            }

            return cursorY + chipHeight;
        }

        // ---------- auto-sizing report primitives ----------
        // The Race Report screen (RuntimeUi.ShowResults) stacks everything inside
        // one CreateScrollPanel content rect whose VerticalLayoutGroup has
        // childControlHeight/Width = false - which means it positions each direct
        // child using that child's *raw* RectTransform.sizeDelta, never anything
        // computed from actual rendered content (wrapped text, nested rows, ...).
        // Every hardcoded "guess how tall this will render" sizeDelta was a latent
        // overflow bug: once real content (a long detail string, an extra
        // classification row, a two-line wrap) exceeded the guess, it rendered past
        // its own box with nothing clipping it, silently bleeding into whatever the
        // layout group stacked next - which is why sections further down the report
        // (most visibly Full Classification) could end up overlapped, clipped, or
        // scrolled out of the reachable range entirely. These helpers close that gap
        // by wiring a real ContentSizeFitter (PreferredSize) onto both plain wrapped
        // text and whole card containers, so their sizeDelta always reflects what
        // Unity's layout system actually computed for their content - not a
        // hand-typed constant. They report accurate height once a layout pass has
        // run; callers that read sizes synchronously right after building the tree
        // should follow with a single LayoutRebuilder.ForceRebuildLayoutImmediate on
        // the outermost container after all children exist (ShowResults does this
        // once, at the end, exactly like RaceHud's radio stack already does).

        // Wrapping paragraph text that reports its own true rendered height instead
        // of a fixed guess - safe to nest inside any VerticalLayoutGroup (including
        // one with childControlHeight = false) or inside a CreateAutoCard/
        // CreateAutoHeightList content area.
        public static Text CreateAutoText(Transform parent, string name, string value, int size, Color color, TextAnchor alignment, float width)
        {
            Text text = CreateText(parent, name, value, size, color, alignment);
            RectTransform rect = text.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, size + 6f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            // Overflow rather than Truncate: the ContentSizeFitter below computes
            // this rect's height from the wrapped text every rebuild, so nothing
            // ever actually needs to be clipped - Truncate would just hide a line
            // for one stale frame before the fitter catches up.
            text.verticalOverflow = VerticalWrapMode.Overflow;
            ContentSizeFitter fitter = text.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return text;
        }

        // A vertical list container (fixed width, auto height) for any run of rows
        // whose combined height shouldn't be hand-computed by the caller (e.g. "count
        // * rowHeight + padding", which drifts the moment a row wraps to two lines).
        // Add children via AddVerticalLayout's own convention (they still need their
        // own correct sizeDelta.y - use CreateAutoText/CreateAutoCard/fixed-height
        // rows as appropriate) and this container's own ContentSizeFitter reports the
        // real total back up to whatever stacked it.
        public static RectTransform CreateAutoHeightList(Transform parent, string name, float width, int spacing, RectOffset padding)
        {
            RectTransform list = CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            list.sizeDelta = new Vector2(width, 10f);
            AddVerticalLayout(list, spacing, padding);
            ContentSizeFitter fitter = list.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return list;
        }

        // Horizontal counterpart: a row of cards/widgets (fixed width, auto height)
        // that reports the tallest child's real height instead of a guessed row
        // height - the "highlight cards" / "report cards" rows used to hardcode a
        // row height that had no relationship to the (also auto-sizing) cards inside
        // it. Caller can restyle childAlignment on the returned HorizontalLayoutGroup
        // afterward (e.g. LowerCenter for the podium row).
        public static RectTransform CreateAutoHeightRow(Transform parent, string name, float width, int spacing, RectOffset padding)
        {
            RectTransform row = CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            row.sizeDelta = new Vector2(width, 10f);
            AddHorizontalLayout(row, spacing, padding);
            ContentSizeFitter fitter = row.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return row;
        }

        // Auto-sizing report card: rounded panel, small accent title, and a content
        // area beneath it that stacks whatever the caller adds (auto-height text,
        // stat bars, comparison bars, ...) and reports its own real total height.
        // Replaces the old BuildReportCard's fixed 190f-tall card body, which
        // silently clipped/overlapped the moment a card's line count or a single
        // long wrapped line exceeded that guess.
        public static RectTransform CreateAutoCard(Transform parent, string name, string title, Color accent, float width, out RectTransform contentArea)
        {
            RectTransform card = CreateRect(parent, name + " auto card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(width, 10f);
            Image background = card.gameObject.AddComponent<Image>();
            StyleRounded(background, PanelDarker);
            AddVerticalLayout(card, 10, new RectOffset(18, 16, 16, 16));
            ContentSizeFitter cardFitter = card.gameObject.AddComponent<ContentSizeFitter>();
            cardFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // title is optional: sections that already have their own
            // CreateSubHeader immediately above (most of the Race Report's
            // required 9 sections) pass an empty title so the card doesn't draw
            // a second, redundant heading directly under the real one.
            if (!string.IsNullOrEmpty(title))
            {
                RectTransform titleRow = CreateRect(card, name + " auto title row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                titleRow.sizeDelta = new Vector2(width - 34f, 20f);
                RectTransform rule = CreateRect(titleRow, name + " auto rule", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
                rule.pivot = new Vector2(0f, 0.5f);
                rule.sizeDelta = new Vector2(26f, 3f);
                Image ruleImage = rule.gameObject.AddComponent<Image>();
                StyleRoundedSmall(ruleImage, accent);
                Text titleText = CreateText(titleRow, name + " auto title", title.ToUpperInvariant(), 13, accent, TextAnchor.MiddleLeft);
                titleText.fontStyle = FontStyle.Bold;
                RectTransform titleTextRect = titleText.GetComponent<RectTransform>();
                titleTextRect.anchorMin = Vector2.zero;
                titleTextRect.anchorMax = Vector2.one;
                titleTextRect.offsetMin = new Vector2(36f, 0f);
                titleTextRect.offsetMax = Vector2.zero;
            }

            contentArea = CreateAutoHeightList(card, name + " auto content", width - 34f, 8, new RectOffset(0, 0, 0, 0));
            return card;
        }

        // Small breathing status dot for live/attention states.
        public static Image CreatePulsingDot(Transform parent, string name, float size, Color color)
        {
            RectTransform dot = CreateRect(parent, name + " status dot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            dot.sizeDelta = new Vector2(size, size);
            Image image = dot.gameObject.AddComponent<Image>();
            image.sprite = GlowSprite;
            image.color = color;
            if (AnimationsEnabled)
            {
                UiPulse pulse = dot.gameObject.AddComponent<UiPulse>();
                pulse.speed = 2.2f;
                pulse.minAlpha = 0.35f;
                pulse.maxAlpha = 1f;
            }

            return image;
        }

        // Scrollable vertical panel. Returns the content RectTransform; add children to it,
        // they stack top-down and the panel scrolls when content overflows.
        public static RectTransform CreateScrollPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, int spacing, RectOffset padding)
        {
            RectTransform viewport = CreateRect(parent, name, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            StyleRounded(viewportImage, PanelDarker);
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

        // ---------- data table rows ----------
        // Real row components for classification screens: position badge, cells,
        // tyre dots. Replaces the old monospace padded-string tables.

        public static RectTransform CreateTableRow(Transform parent, string name, float width, float height, bool highlight, int zebraIndex)
        {
            RectTransform row = CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            row.sizeDelta = new Vector2(width, height);
            Image background = row.gameObject.AddComponent<Image>();
            StyleRoundedSmall(background, highlight
                ? new Color(0.62f, 0.06f, 0.05f, 0.66f)
                : (zebraIndex % 2 == 0 ? RowEven : RowOdd));
            return row;
        }

        // Text cell positioned by fractional horizontal anchors inside a row.
        public static Text AddRowCell(RectTransform row, string name, string value, float anchorX0, float anchorX1, int size, Color color, TextAnchor alignment)
        {
            Text cell = CreateText(row, name, value, size, color, alignment);
            RectTransform rect = cell.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorX0, 0f);
            rect.anchorMax = new Vector2(anchorX1, 1f);
            rect.offsetMin = new Vector2(4f, 0f);
            rect.offsetMax = new Vector2(-4f, 0f);
            // Classification-table fix: CreateTableRow is a fixed-height row that
            // can never grow to fit a wrapped second line, so a long driver/team
            // name or DNF reason (e.g. "Mechanical failure" in the narrow PEN/
            // STATUS column) that wrapped got silently cut off by CreateText's
            // default vertical Truncate - the exact "content not displaying"
            // symptom reported. Best-fit keeps every cell on one line, shrinking
            // the font instead of ever dropping content.
            cell.horizontalOverflow = HorizontalWrapMode.Overflow;
            cell.verticalOverflow = VerticalWrapMode.Overflow;
            cell.resizeTextForBestFit = true;
            cell.resizeTextMinSize = 9;
            cell.resizeTextMaxSize = size;
            return cell;
        }

        // Rounded position chip at the left edge of a row: P1-P3 get medal tints.
        public static void AddPositionBadge(RectTransform row, int position, bool isPlayer)
        {
            RectTransform badge = CreateRect(row, "Position badge", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            badge.sizeDelta = new Vector2(34f, 24f);
            badge.pivot = new Vector2(0f, 0.5f);
            badge.anchoredPosition = new Vector2(8f, 0f);
            Image image = badge.gameObject.AddComponent<Image>();
            Color badgeColor = position == 1 ? new Color(1f, 0.8f, 0.2f, 0.92f)
                : position == 2 ? new Color(0.78f, 0.82f, 0.88f, 0.85f)
                : position == 3 ? new Color(0.82f, 0.52f, 0.25f, 0.85f)
                : (isPlayer ? new Color(0.95f, 0.08f, 0.06f, 0.9f) : new Color(0.14f, 0.18f, 0.23f, 0.9f));
            StyleRoundedSmall(image, badgeColor);
            Text label = CreateText(badge, "Badge label", position.ToString(), 14, position <= 3 ? new Color(0.06f, 0.06f, 0.07f) : Color.white, TextAnchor.MiddleCenter);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        // Small colored dot cell (tyre compound, team color).
        public static Image AddRowDot(RectTransform row, string name, float anchorX, float size, Color color)
        {
            RectTransform dot = CreateRect(row, name, new Vector2(anchorX, 0.5f), new Vector2(anchorX, 0.5f), Vector2.zero, Vector2.zero);
            dot.sizeDelta = new Vector2(size, size);
            Image image = dot.gameObject.AddComponent<Image>();
            image.sprite = GlowSprite;
            image.color = color;
            return image;
        }

        // Label + value row for itemized breakdowns (qualifying lap composition,
        // strategy deltas, anything that used to be one dense multi-line Text
        // block). Caller decides the value color so signed-delta callers can
        // tint by sign while flat informational rows stay neutral.
        public static RectTransform CreateBreakdownRow(Transform parent, string label, string value, Color valueColor, float width)
        {
            RectTransform row = CreateRect(parent, label + " breakdown row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            row.sizeDelta = new Vector2(width, 24f);
            Text labelText = CreateText(row, "Breakdown label", label, 14, TextMuted, TextAnchor.MiddleLeft);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.6f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            Text valueText = CreateText(row, "Breakdown value", value, 14, valueColor, TextAnchor.MiddleRight);
            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.6f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = new Vector2(-4f, 0f);
            valueText.horizontalOverflow = HorizontalWrapMode.Overflow;
            return row;
        }

        // ---------- HUD component helpers ----------
        // These build the compact card widgets used by the in-race HUD so RaceHud
        // composes small pieces instead of giant text dumps.

        // Card with a small uppercase header and a colored side accent. Children are
        // positioned inside using the returned rect; the card itself is sized by the caller.
        public static RectTransform CreateHudCard(Transform parent, string title, float width, float height, Color accentColor)
        {
            return CreateHudCard(parent, title, width, height, accentColor, out _);
        }

        // Overload that also hands back the accent bar Image, for callers that
        // need to animate it directly (e.g. a brief white flash on a sudden
        // state change) instead of re-deriving it with a Transform.Find lookup.
        public static RectTransform CreateHudCard(Transform parent, string title, float width, float height, Color accentColor, out Image accentImage)
        {
            RectTransform card = CreateRect(parent, title + " hud card", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(width, height);
            Image background = card.gameObject.AddComponent<Image>();
            StyleRounded(background, HudCardBackground);
            RectTransform accent = CreateRect(card, title + " card accent", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 8f), new Vector2(3f, -8f));
            accentImage = accent.gameObject.AddComponent<Image>();
            StyleRoundedSmall(accentImage, accentColor);
            Text header = CreateText(card, title + " card title", title.ToUpperInvariant(), 13, TextMuted, TextAnchor.UpperLeft);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.offsetMin = new Vector2(14f, -26f);
            headerRect.offsetMax = new Vector2(-10f, -6f);
            return card;
        }

        // Label on the left, value on the right, placed at a vertical offset from the card top.
        public static Text CreateHudLabelValueRow(RectTransform card, string label, float topOffset, out Text valueText)
        {
            Text labelText = CreateText(card, label + " row label", label.ToUpperInvariant(), 13, TextMuted, TextAnchor.MiddleLeft);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0.5f, 1f);
            labelRect.offsetMin = new Vector2(14f, -topOffset - 20f);
            labelRect.offsetMax = new Vector2(0f, -topOffset);

            valueText = CreateText(card, label + " row value", "", 14, TextPrimary, TextAnchor.MiddleRight);
            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.5f, 1f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = new Vector2(0f, -topOffset - 20f);
            valueRect.offsetMax = new Vector2(-12f, -topOffset);
            return labelText;
        }

        // Bigger variant of CreateHudLabelValueRow for a card's single "hero"
        // stat - same label-left/value-right shape, but with a caller-chosen
        // row height and a larger bold value so one number in a card can
        // visually lead instead of every row reading at the same weight.
        // Round 4 hierarchy pass: the primary numbers a player needs at a
        // glance (current lap time, chiefly) were sized identically to
        // secondary detail (last/best lap, sector deltas) with nothing to
        // separate them - this is the shared primitive for fixing that
        // without hand-rolling one-off rects at every call site.
        public static Text CreateHudHeroRow(RectTransform card, string label, float topOffset, float rowHeight, int valueFontSize, out Text valueText)
        {
            Text labelText = CreateText(card, label + " row label", label.ToUpperInvariant(), 13, TextMuted, TextAnchor.MiddleLeft);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0.38f, 1f);
            labelRect.offsetMin = new Vector2(14f, -topOffset - rowHeight);
            labelRect.offsetMax = new Vector2(0f, -topOffset);

            valueText = CreateText(card, label + " row value", "", valueFontSize, TextPrimary, TextAnchor.MiddleRight);
            valueText.fontStyle = FontStyle.Bold;
            // Overflow rather than the CreateText default of Wrap: a hero
            // value routinely carries an inline color tag (e.g. an "INV"
            // marker appended to a lap time) that must never wrap onto a
            // second line and get vertically truncated inside this row.
            valueText.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.34f, 1f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = new Vector2(0f, -topOffset - rowHeight);
            valueRect.offsetMax = new Vector2(-12f, -topOffset);
            return labelText;
        }

        // Small labeled horizontal meter inside a HUD card. Returns the fill image;
        // resize with SetMeterValue so anchors stay clean.
        public static Image CreateHudMeter(RectTransform card, string label, float topOffset, Color fillColor, out Text valueText)
        {
            Text labelText = CreateText(card, label + " meter label", label.ToUpperInvariant(), 13, TextMuted, TextAnchor.MiddleLeft);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.offsetMin = new Vector2(14f, -topOffset - 18f);
            labelRect.offsetMax = new Vector2(64f, -topOffset);

            valueText = CreateText(card, label + " meter value", "", 13, TextPrimary, TextAnchor.MiddleRight);
            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(1f, 1f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = new Vector2(-62f, -topOffset - 18f);
            valueRect.offsetMax = new Vector2(-12f, -topOffset);

            RectTransform track = CreateRect(card, label + " meter track", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(68f, -topOffset - 14f), new Vector2(-66f, -topOffset - 4f));
            Image trackImage = track.gameObject.AddComponent<Image>();
            StyleRoundedSmall(trackImage, MeterTrack);
            RectTransform fill = CreateBand(track, label + " meter fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, fillColor);
            return fill.GetComponent<Image>();
        }

        public static void SetMeterValue(Image fill, float value01)
        {
            if (fill == null)
            {
                return;
            }

            RectTransform rect = fill.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(Mathf.Clamp01(value01), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Smoothly glides a meter fill toward a target instead of snapping - use
        // for slow-changing readouts (tyre wear, fuel, damage, pit progress)
        // where an instant jump reads as a glitch. Fast telemetry (rev counter,
        // throttle/brake bars) should keep calling SetMeterValue directly so it
        // stays perfectly responsive.
        public static void SetMeterValueAnimated(Image fill, float value01)
        {
            if (fill == null)
            {
                return;
            }

            float target = Mathf.Clamp01(value01);
            UiFillLerp lerp = fill.GetComponent<UiFillLerp>();
            if (lerp == null)
            {
                // First call: snap to the real value so the widget doesn't sweep
                // in from empty the moment the HUD appears.
                SetMeterValue(fill, target);
                lerp = fill.gameObject.AddComponent<UiFillLerp>();
            }

            lerp.target = target;
        }

        // Thin vertical input/telemetry bar (throttle, brake, ERS). Returns the
        // fill image; drive it with SetVerticalBarValue.
        public static Image CreateVerticalBar(Transform parent, string name, float width, float height, Color fillColor, out Text label)
        {
            RectTransform track = CreateRect(parent, name + " vbar", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            track.sizeDelta = new Vector2(width, height);
            Image trackImage = track.gameObject.AddComponent<Image>();
            StyleRoundedSmall(trackImage, MeterTrack);
            RectTransform fill = CreateBand(track, name + " vbar fill", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero, fillColor);
            label = CreateText(track, name + " vbar label", name.ToUpperInvariant(), 13, TextMuted, TextAnchor.UpperCenter);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.offsetMin = new Vector2(-16f, -18f);
            labelRect.offsetMax = new Vector2(16f, -2f);
            return fill.GetComponent<Image>();
        }

        public static void SetVerticalBarValue(Image fill, float value01)
        {
            if (fill == null)
            {
                return;
            }

            RectTransform rect = fill.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, Mathf.Clamp01(value01));
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Overload that also recolors the fill - used by bars whose meaning changes
        // with state (e.g. the ERS bar swapping color between drain and regen)
        // instead of staying one flat color regardless of what's happening.
        public static void SetVerticalBarValue(Image fill, float value01, Color color)
        {
            SetVerticalBarValue(fill, value01);
            if (fill != null)
            {
                fill.color = color;
            }
        }

        // Small state pill (background + label) for DRS/ERS style indicators.
        public static HudPill CreatePill(Transform parent, string name, float width, float height)
        {
            RectTransform rect = CreateRect(parent, name + " pill", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.sizeDelta = new Vector2(width, height);
            Image background = rect.gameObject.AddComponent<Image>();
            StyleRoundedSmall(background, MeterTrack);
            Text label = CreateText(rect, name + " pill label", name.ToUpperInvariant(), 13, TextMuted, TextAnchor.MiddleCenter);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return new HudPill { root = rect, background = background, label = label };
        }

        // Two-line race-control callout: bold state title, muted detail line
        // beneath it, a colored accent bar and a small breathing status dot - the
        // richer replacement for a single-line pill so SC/VSC/yellow states read
        // as a designed banner instead of a status word. Caller drives visibility
        // and restyles per state with SetStatusBannerState.
        public static RectTransform CreateStatusBanner(Transform parent, string name, float width, float height, out Image accentBar, out Image statusDot, out Text titleText, out Text subtitleText)
        {
            RectTransform rect = CreateRect(parent, name + " status banner", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.sizeDelta = new Vector2(width, height);
            Image background = rect.gameObject.AddComponent<Image>();
            StyleRounded(background, HudCardBackground);

            RectTransform accent = CreateRect(rect, name + " banner accent", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 6f), new Vector2(4f, -6f));
            accentBar = accent.gameObject.AddComponent<Image>();
            StyleRoundedSmall(accentBar, Accent);
            accentBar.raycastTarget = false;

            statusDot = CreatePulsingDot(rect, name, 9f, Accent);
            RectTransform dotRect = statusDot.rectTransform;
            dotRect.anchorMin = new Vector2(1f, 1f);
            dotRect.anchorMax = new Vector2(1f, 1f);
            dotRect.anchoredPosition = new Vector2(-14f, -14f);

            titleText = CreateText(rect, name + " banner title", "", 19, TextPrimary, TextAnchor.UpperLeft);
            titleText.fontStyle = FontStyle.Bold;
            // Overflow rather than Wrap: the title is a short all-caps state
            // name (RED FLAG - RACE SUSPENDED, GRID RESET - RACE RESTART) that
            // must always read as one line - wrapping it would eat into the
            // subtitle's already-tight vertical budget below.
            titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(18f, 0f);
            titleRect.offsetMax = new Vector2(-26f, -10f);

            // Subtitle now regularly carries a full sentence plus a live
            // countdown number (e.g. "Grid reset based on the running order
            // when the flag came out - restarting in 8s"), so it needs real
            // wrap room - see the taller banner size passed by RaceHud's
            // BuildRaceControlBanner - and Overflow vertically so a third
            // wrapped line still renders instead of being silently truncated.
            subtitleText = CreateText(rect, name + " banner subtitle", "", 14, TextMuted, TextAnchor.UpperLeft);
            subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            subtitleText.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform subtitleRect = subtitleText.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 0f);
            subtitleRect.anchorMax = new Vector2(1f, 0.5f);
            subtitleRect.offsetMin = new Vector2(18f, 10f);
            subtitleRect.offsetMax = new Vector2(-14f, -4f);

            return rect;
        }

        // Restyles a status banner in one call - title, subtitle, and accent/dot
        // color together - mirroring HudPill.SetState's single-call ergonomics.
        public static void SetStatusBannerState(Image accentBar, Image statusDot, Text titleText, Text subtitleText, string title, string subtitle, Color color)
        {
            if (titleText != null)
            {
                titleText.text = title;
            }

            if (subtitleText != null)
            {
                subtitleText.text = subtitle;
            }

            if (accentBar != null)
            {
                accentBar.color = color;
            }

            if (statusDot != null)
            {
                statusDot.color = color;
            }
        }

        // Center-screen "big moment" flash text (lights-out, green-flag restart,
        // final lap, chequered finish) - anchor doubles as the rect's point anchor,
        // matching the point-anchor pattern already used for center overlays.
        public static UiBigFlash CreateBigFlashText(Transform parent, string name, Vector2 anchor, int fontSize)
        {
            RectTransform rect = CreateRect(parent, name, anchor, anchor, Vector2.zero, Vector2.zero);
            rect.sizeDelta = new Vector2(760f, fontSize + 30f);
            rect.anchoredPosition = Vector2.zero;
            Text text = CreateText(rect, name + " text", "", fontSize, Color.white, TextAnchor.MiddleCenter);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text.gameObject.AddComponent<UiBigFlash>();
        }

        public static Image CreateIconDot(Transform parent, string name, float size, Color color)
        {
            RectTransform dot = CreateRect(parent, name + " dot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            dot.sizeDelta = new Vector2(size, size);
            Image image = dot.gameObject.AddComponent<Image>();
            image.sprite = GlowSprite;
            image.color = color;
            return image;
        }

        // Panel pinned to a screen edge/corner by anchor + pivot so it can never sit
        // half offscreen regardless of resolution or HUD scale.
        public static RectTransform CreateResponsivePanel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 margin, Color color)
        {
            RectTransform rect = CreateRect(parent, name, anchor, anchor, Vector2.zero, Vector2.zero);
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = margin;
            if (color.a > 0.001f)
            {
                Image image = rect.gameObject.AddComponent<Image>();
                StyleRounded(image, color);
            }

            return rect;
        }

        public static RectTransform CreateTopNav(Transform parent, string title)
        {
            RectTransform nav = CreateBand(parent, "Top nav", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -92f), Vector2.zero, PanelDarker);
            CreateBand(nav, "Top nav rule", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f), Accent);
            RectTransform accentTab = CreateRect(nav, "Nav accent tab", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            accentTab.sizeDelta = new Vector2(58f, 6f);
            accentTab.pivot = new Vector2(0f, 0f);
            accentTab.anchoredPosition = new Vector2(64f, 0f);
            Image accentTabImage = accentTab.gameObject.AddComponent<Image>();
            accentTabImage.color = Color.white;
            Text heading = CreateText(nav, "Nav title", title, 38, TextPrimary, TextAnchor.MiddleLeft);
            RectTransform headingRect = heading.GetComponent<RectTransform>();
            headingRect.anchorMin = new Vector2(0f, 0f);
            headingRect.anchorMax = new Vector2(0.6f, 1f);
            headingRect.offsetMin = new Vector2(64f, 0f);
            headingRect.offsetMax = Vector2.zero;
            return nav;
        }

        // ---------- reusable screen structure ----------
        // One header and one footer style used everywhere, so every screen shares
        // the same navigation chrome instead of hand-rolled title bars and
        // scattered Back/Main Menu buttons in the body.

        // Header bar with a title and an optional muted subtitle line beneath it -
        // use for every top-level screen instead of a one-off title Text.
        public static RectTransform CreateScreenHeader(Transform parent, string title, string subtitle)
        {
            float height = string.IsNullOrEmpty(subtitle) ? 92f : 108f;
            RectTransform nav = CreateBand(parent, "Screen header", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -height), Vector2.zero, PanelDarker);
            CreateBand(nav, "Screen header rule", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f), Accent);
            RectTransform accentTab = CreateRect(nav, "Header accent tab", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            accentTab.sizeDelta = new Vector2(58f, 6f);
            accentTab.pivot = new Vector2(0f, 0f);
            accentTab.anchoredPosition = new Vector2(64f, 0f);
            Image accentTabImage = accentTab.gameObject.AddComponent<Image>();
            accentTabImage.color = Color.white;

            if (string.IsNullOrEmpty(subtitle))
            {
                Text heading = CreateText(nav, "Header title", title, 38, TextPrimary, TextAnchor.MiddleLeft);
                RectTransform headingRect = heading.GetComponent<RectTransform>();
                headingRect.anchorMin = new Vector2(0f, 0f);
                headingRect.anchorMax = new Vector2(0.72f, 1f);
                headingRect.offsetMin = new Vector2(64f, 0f);
                headingRect.offsetMax = Vector2.zero;
                return nav;
            }

            Text titleText = CreateText(nav, "Header title", title, 34, TextPrimary, TextAnchor.LowerLeft);
            RectTransform titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(0.72f, 1f);
            titleRect.offsetMin = new Vector2(64f, 0f);
            titleRect.offsetMax = new Vector2(0f, -10f);

            Text subtitleText = CreateText(nav, "Header subtitle", subtitle, 16, TextMuted, TextAnchor.UpperLeft);
            RectTransform subtitleRect = subtitleText.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 0f);
            subtitleRect.anchorMax = new Vector2(0.72f, 0.5f);
            subtitleRect.offsetMin = new Vector2(64f, 8f);
            subtitleRect.offsetMax = Vector2.zero;
            return nav;
        }

        // Bottom-docked action bar: secondary/back actions on the left, primary
        // forward actions on the right. Replaces scattered per-screen button rows
        // so navigation always lives in the same place.
        public static void CreateFooterBar(Transform parent, out RectTransform leftGroup, out RectTransform rightGroup)
        {
            RectTransform footer = CreateBand(parent, "Footer bar", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 78f), PanelDarker);
            CreateBand(footer, "Footer rule", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), Vector2.zero, new Color(1f, 1f, 1f, 0.06f));

            leftGroup = CreateRect(footer, "Footer left group", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            leftGroup.pivot = new Vector2(0f, 0.5f);
            leftGroup.anchoredPosition = new Vector2(48f, 0f);
            leftGroup.sizeDelta = new Vector2(760f, 60f);
            AddHorizontalLayout(leftGroup, 12, new RectOffset(0, 0, 0, 0));

            rightGroup = CreateRect(footer, "Footer right group", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
            rightGroup.pivot = new Vector2(1f, 0.5f);
            rightGroup.anchoredPosition = new Vector2(-48f, 0f);
            rightGroup.sizeDelta = new Vector2(760f, 60f);
            HorizontalLayoutGroup rightLayout = AddHorizontalLayout(rightGroup, 12, new RectOffset(0, 0, 0, 0));
            rightLayout.childAlignment = TextAnchor.MiddleRight;
        }

        // Screen body area sized to sit cleanly between a header and a footer, so
        // callers don't hand-tune anchor math on every screen.
        public static RectTransform CreateScreenBody(Transform parent, float headerHeight, float footerHeight)
        {
            return CreateRect(parent, "Screen body", Vector2.zero, Vector2.one, new Vector2(0f, footerHeight), new Vector2(0f, -headerHeight));
        }

        // ---------- settings rows ----------
        // A settings screen built from these reads as label / value / description
        // rows with a compact control, not a wall of full-width buttons.

        public static RectTransform CreateSettingRow(Transform parent, string label, string description, out RectTransform controlSlot)
        {
            bool hasDescription = !string.IsNullOrEmpty(description);
            // Fixed height only - width used to be hardcoded to 760px regardless of
            // the parent's actual size, which guaranteed overflow in any card
            // narrower than that (e.g. the Gameplay tab's Race Control card, ~530px
            // interior). The row now anchor-stretches across whatever it's placed in
            // (anchorMin/anchorMax span 0..1 on X) and a LayoutElement pins only the
            // height, so the list layout that owns this row - see
            // AddSettingsListLayout / StretchListChildrenWidth - controls the width.
            // Description rows are taller than before (84 vs the old 60) because a
            // description can run to two wrapped lines once a row is genuinely
            // narrow instead of floating at a fixed 760px; every settings list is
            // scroll-wrapped now so the extra height never costs clipped content.
            float height = hasDescription ? 84f : 46f;
            RectTransform row = CreateRect(parent, label + " setting row", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, height);
            LayoutElement layoutElement = row.gameObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = height;
            layoutElement.preferredHeight = height;
            Image background = row.gameObject.AddComponent<Image>();
            StyleRounded(background, new Color(1f, 1f, 1f, 0.02f));

            Text labelText = CreateText(row, "Row label", label, 18, TextPrimary, TextAnchor.UpperLeft);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, hasDescription ? 0.5f : 0f);
            labelRect.anchorMax = new Vector2(0.58f, 1f);
            labelRect.offsetMin = new Vector2(16f, 0f);
            labelRect.offsetMax = new Vector2(-8f, -6f);

            if (hasDescription)
            {
                Text descriptionText = CreateText(row, "Row description", description, 13, TextMuted, TextAnchor.LowerLeft);
                // Overflow rather than Truncate: on a narrow card a long description
                // can still run past two lines, and silently swallowing the tail of
                // the sentence is worse than it occasionally drawing a hair below its
                // box (rows are spaced with enough gap - see AddSettingsListLayout
                // callers - that this doesn't overlap the next row in practice).
                descriptionText.verticalOverflow = VerticalWrapMode.Overflow;
                RectTransform descriptionRect = descriptionText.GetComponent<RectTransform>();
                descriptionRect.anchorMin = Vector2.zero;
                descriptionRect.anchorMax = new Vector2(0.58f, 0.5f);
                descriptionRect.offsetMin = new Vector2(16f, 8f);
                descriptionRect.offsetMax = new Vector2(-8f, 0f);
            }

            controlSlot = CreateRect(row, "Row control", new Vector2(0.58f, 0f), new Vector2(1f, 1f), new Vector2(0f, 8f), new Vector2(-14f, -8f));
            return row;
        }

        // Compact single-button control that cycles through a value on click -
        // used inside a setting row's control slot for numeric/enum settings.
        public static Button CreateCycleControl(RectTransform controlSlot, string valueText, UnityAction onCycle)
        {
            Button button = CreateSecondaryButton(controlSlot, valueText, onCycle);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(Mathf.Clamp(valueText.Length * 11f + 48f, 120f, 260f), 40f);
            return button;
        }

        // Compact on/off switch styled as a two-state pill button.
        public static Button CreateToggleControl(RectTransform controlSlot, bool value, UnityAction onToggle)
        {
            Button button = CreateButton(controlSlot, value ? "ON" : "OFF", onToggle);
            Image face = button.targetGraphic as Image;
            if (face != null)
            {
                Color onColor = new Color(0.1f, 0.5f, 0.22f, 0.95f);
                Color offColor = new Color(0.1f, 0.13f, 0.17f, 0.9f);
                StyleRounded(face, value ? onColor : offColor);
                ColorBlock colors = button.colors;
                colors.normalColor = face.color;
                colors.highlightedColor = value ? new Color(0.14f, 0.62f, 0.28f, 1f) : new Color(0.16f, 0.2f, 0.26f, 1f);
                colors.selectedColor = colors.highlightedColor;
                button.colors = colors;
            }

            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(84f, 38f);
            return button;
        }

        // Row of small pill buttons, one per option, with the active option lit -
        // the segmented alternative to a single "click to cycle" button, good for
        // a handful of mutually exclusive choices (difficulty, quality, tyre).
        public static void CreateSegmentedControl(RectTransform controlSlot, string[] options, int selectedIndex, Action<int> onSelect)
        {
            HorizontalLayoutGroup layout = controlSlot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                bool selected = index == selectedIndex;
                Button segment = CreateButton(controlSlot, options[i], () => onSelect(index));
                UiFactory.SetSize(segment, Mathf.Clamp(options[i].Length * 9f + 28f, 56f, 150f), 38f);
                UiFactory.SetButtonSelected(segment, selected);
            }
        }

        // Simple titled body-text card - for weather briefings, guidance text,
        // and other paragraph content that needs a defined container rather than
        // loose text dropped straight onto the background.
        public static RectTransform CreateInfoCard(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string title, string body, Color accentColor)
        {
            RectTransform card = CreateGlassPanel(parent, name, anchorMin, anchorMax, Vector2.zero, Vector2.zero, PanelDark);
            RectTransform accent = CreateRect(card, name + " accent", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            accent.sizeDelta = new Vector2(56f, 3f);
            accent.pivot = new Vector2(0f, 1f);
            accent.anchoredPosition = new Vector2(20f, -14f);
            Image accentImage = accent.gameObject.AddComponent<Image>();
            StyleRoundedSmall(accentImage, accentColor);

            if (!string.IsNullOrEmpty(title))
            {
                Text titleText = CreateText(card, name + " title", title.ToUpperInvariant(), 15, accentColor, TextAnchor.UpperLeft);
                RectTransform titleRect = titleText.GetComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0f, 1f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.offsetMin = new Vector2(20f, -42f);
                titleRect.offsetMax = new Vector2(-16f, -22f);
            }

            Text bodyText = CreateText(card, name + " body", body, 16, new Color(0.85f, 0.91f, 0.95f), TextAnchor.UpperLeft);
            RectTransform bodyRect = bodyText.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(20f, 16f);
            bodyRect.offsetMax = new Vector2(-16f, string.IsNullOrEmpty(title) ? -18f : -48f);
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            return card;
        }

        // ---------- rating cards (driver ratings / team ratings) ----------
        // Shared building blocks for the two "browse and compare" screens so a
        // driver card and a team card read as the same visual language: a
        // procedural avatar/color header, a stat-bar block, and chip-style tags.

        static Sprite circleSprite;

        // Hard-edged circle (vs. the soft-falloff GlowSprite) used as the base
        // shape for procedural driver avatars and other flat circular chrome.
        public static Sprite CircleSprite
        {
            get
            {
                if (circleSprite == null)
                {
                    circleSprite = BuildCircleSprite(64);
                }

                return circleSprite;
            }
        }

        static Sprite BuildCircleSprite(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Ui circle";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance + 1f));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // Procedural avatar: no external art, just a team-colored circle with a
        // darker "visor" band and the driver's initials/number composited on top.
        public static RectTransform CreateProceduralHelmetIcon(Transform parent, string label, Color teamColor, float size)
        {
            RectTransform root = CreateRect(parent, "Helmet icon", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            root.sizeDelta = new Vector2(size, size);
            Image baseImage = root.gameObject.AddComponent<Image>();
            baseImage.sprite = CircleSprite;
            baseImage.color = teamColor;

            RectTransform visor = CreateRect(root, "Helmet visor", new Vector2(0f, 0.34f), new Vector2(1f, 0.62f), Vector2.zero, Vector2.zero);
            Image visorImage = visor.gameObject.AddComponent<Image>();
            visorImage.color = new Color(0.03f, 0.04f, 0.05f, 0.85f);
            visorImage.raycastTarget = false;

            Text labelText = CreateText(root, "Helmet label", label, Mathf.RoundToInt(size * 0.3f), Color.white, TextAnchor.MiddleCenter);
            labelText.fontStyle = FontStyle.Bold;
            labelText.raycastTarget = false;
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return root;
        }

        // A regular card with a colored outline ring baked from two nested rounded
        // rects, instead of a flat panel - used to give the player's own driver
        // card (and teammate) a visibly distinct border without a separate asset.
        // Returns the inner content rect; callers build the card's contents there.
        public static RectTransform CreateBorderedCard(Transform parent, string name, float width, float height, Color borderColor, bool highlighted)
        {
            RectTransform outer = CreateRect(parent, name + " outer", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            outer.sizeDelta = new Vector2(width, height);
            Image outerImage = outer.gameObject.AddComponent<Image>();
            StyleRounded(outerImage, highlighted ? borderColor : new Color(borderColor.r, borderColor.g, borderColor.b, 0.22f));

            float inset = highlighted ? 3f : 1.5f;
            RectTransform inner = CreateRect(outer, name + " inner", Vector2.zero, Vector2.one, new Vector2(inset, inset), new Vector2(-inset, -inset));
            Image innerImage = inner.gameObject.AddComponent<Image>();
            StyleRounded(innerImage, PanelDark);
            return inner;
        }

        // Labeled stat bar: uppercase label, numeric value, thin fill track. The
        // same visual language for every stat on both driver and team cards.
        public static RectTransform CreateStatBar(Transform parent, string label, float value, float maxValue, Color fillColor, float width)
        {
            RectTransform row = CreateRect(parent, label + " stat bar", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            row.sizeDelta = new Vector2(width, 33f);

            Text labelText = CreateText(row, "Stat bar label", label.ToUpperInvariant(), 13, TextMuted, TextAnchor.UpperLeft);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0.7f, 1f);
            labelRect.offsetMin = new Vector2(0f, -16f);
            labelRect.offsetMax = new Vector2(0f, 0f);

            // Bug fix: offsetMin/offsetMax on the y-axis here used to both be
            // zero while anchorMin.y == anchorMax.y == 1 (a point anchor on
            // that axis) - that collapses the rect to zero height, so this
            // value text never actually had room to render. Mirrors the
            // label rect's height above instead.
            Text valueText = CreateText(row, "Stat bar value", Mathf.RoundToInt(value).ToString(), 13, TextPrimary, TextAnchor.UpperRight);
            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.7f, 1f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = new Vector2(0f, -16f);
            valueRect.offsetMax = Vector2.zero;

            RectTransform track = CreateRect(row, "Stat bar track", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 3f), new Vector2(0f, 13f));
            Image trackImage = track.gameObject.AddComponent<Image>();
            StyleRoundedSmall(trackImage, MeterTrack);
            float value01 = maxValue <= 0f ? 0f : Mathf.Clamp01(value / maxValue);
            CreateBand(track, "Stat bar fill", Vector2.zero, new Vector2(value01, 1f), Vector2.zero, Vector2.zero, fillColor);
            return row;
        }

        // Podium-style card for a top-3 finisher: procedural helmet avatar in team
        // color, medal-tinted top rule, driver/team name and a time/gap line.
        // Three of these (P2, P1, P3 order, P1 tallest) replace a flat "Winner"
        // stat card on the results screen with an actual podium shape.
        public static RectTransform CreatePodiumCard(Transform parent, int position, string driverName, string teamLabel, Color teamColor, string timeLabel, float width, float height)
        {
            RectTransform card = CreateRect(parent, "Podium P" + position, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(width, height);
            Image background = card.gameObject.AddComponent<Image>();
            StyleRounded(background, PanelDarker);

            Color medal = position == 1 ? new Color(1f, 0.82f, 0.24f, 1f)
                : position == 2 ? new Color(0.8f, 0.85f, 0.9f, 1f)
                : new Color(0.82f, 0.53f, 0.26f, 1f);

            // The winner reads clearly as the winner: a bigger avatar, medal tag
            // and name than P2/P3, not just a taller platform underneath them.
            bool winner = position == 1;
            float avatarSize = winner ? 76f : 56f;
            int medalFontSize = winner ? 15 : 13;
            int nameFontSize = winner ? 22 : 17;

            RectTransform top = CreateRect(card, "Podium rule", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), Vector2.zero);
            top.sizeDelta = new Vector2(0f, 4f);
            Image topImage = top.gameObject.AddComponent<Image>();
            StyleRoundedSmall(topImage, medal);
            topImage.raycastTarget = false;

            RectTransform avatar = CreateProceduralHelmetIcon(card, position.ToString(), teamColor, avatarSize);
            avatar.anchorMin = new Vector2(0.5f, 1f);
            avatar.anchorMax = new Vector2(0.5f, 1f);
            avatar.anchoredPosition = new Vector2(0f, -16f - avatarSize * 0.5f);

            float cursorTop = 26f + avatarSize;

            Text medalLabel = CreateText(card, "Podium medal", position == 1 ? "WINNER" : "P" + position, medalFontSize, medal, TextAnchor.UpperCenter);
            medalLabel.fontStyle = FontStyle.Bold;
            RectTransform medalRect = medalLabel.GetComponent<RectTransform>();
            medalRect.anchorMin = new Vector2(0f, 1f);
            medalRect.anchorMax = new Vector2(1f, 1f);
            medalRect.offsetMin = new Vector2(6f, -(cursorTop + 22f));
            medalRect.offsetMax = new Vector2(-6f, -cursorTop);
            cursorTop += 26f;

            Text nameText = CreateText(card, "Podium name", driverName, nameFontSize, TextPrimary, TextAnchor.UpperCenter);
            nameText.fontStyle = FontStyle.Bold;
            nameText.resizeTextForBestFit = true;
            nameText.resizeTextMinSize = 13;
            nameText.resizeTextMaxSize = nameFontSize;
            RectTransform nameRect = nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(6f, -(cursorTop + 28f));
            nameRect.offsetMax = new Vector2(-6f, -cursorTop);
            cursorTop += 32f;

            // Team name stays visible but secondary - smaller and muted below
            // the driver name, never competing with it for attention.
            Text teamText = CreateText(card, "Podium team", teamLabel, 13, TextMuted, TextAnchor.UpperCenter);
            RectTransform teamRect = teamText.GetComponent<RectTransform>();
            teamRect.anchorMin = new Vector2(0f, 1f);
            teamRect.anchorMax = new Vector2(1f, 1f);
            teamRect.offsetMin = new Vector2(6f, -(cursorTop + 20f));
            teamRect.offsetMax = new Vector2(-6f, -cursorTop);

            Text timeText = CreateText(card, "Podium time", timeLabel, 14, medal, TextAnchor.LowerCenter);
            timeText.fontStyle = FontStyle.Bold;
            RectTransform timeRect = timeText.GetComponent<RectTransform>();
            timeRect.anchorMin = Vector2.zero;
            timeRect.anchorMax = new Vector2(1f, 0f);
            timeRect.offsetMin = new Vector2(6f, 12f);
            timeRect.offsetMax = new Vector2(-6f, 36f);

            return card;
        }

        // Two-value comparison bar: two labeled figures (e.g. player vs teammate)
        // rendered as two half-height fills on the same track so they're directly
        // comparable at a glance instead of two separate stat rows.
        public static RectTransform CreateComparisonBar(Transform parent, string label, float valueA, Color colorA, float valueB, Color colorB, float maxValue, float width)
        {
            RectTransform row = CreateRect(parent, label + " comparison bar", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            row.sizeDelta = new Vector2(width, 36f);

            Text labelText = CreateText(row, "Comparison label", label.ToUpperInvariant(), 13, TextMuted, TextAnchor.UpperLeft);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(0f, -16f);
            labelRect.offsetMax = Vector2.zero;

            RectTransform track = CreateRect(row, "Comparison track", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 4f), new Vector2(0f, 18f));
            Image trackImage = track.gameObject.AddComponent<Image>();
            StyleRoundedSmall(trackImage, MeterTrack);
            float a01 = maxValue <= 0f ? 0f : Mathf.Clamp01(valueA / maxValue);
            float b01 = maxValue <= 0f ? 0f : Mathf.Clamp01(valueB / maxValue);
            CreateBand(track, "Comparison fill A", new Vector2(0f, 0.52f), new Vector2(a01, 1f), Vector2.zero, Vector2.zero, colorA);
            CreateBand(track, "Comparison fill B", new Vector2(0f, 0f), new Vector2(b01, 0.48f), Vector2.zero, Vector2.zero, colorB);
            return row;
        }

        // Small filter/category tab used above a card grid (Overall / Pace /
        // Qualifying, ...). Distinct from CreateButton so the selected tab reads
        // as a segmented control rather than a stack of standalone buttons.
        public static Button CreateFilterTab(Transform parent, string label, bool selected, UnityAction onClick)
        {
            Button button = CreateButton(parent, label, onClick);
            SetSize(button, Mathf.Clamp(label.Length * 10f + 40f, 96f, 220f), 40f);
            SetButtonSelected(button, selected);
            return button;
        }

        // Scrollable, wrapping card grid: unlike CreateScrollPanel's fixed single
        // column, this lays children out with GridLayoutGroup.Constraint.Flexible
        // so however many cards fit the available width per row is computed from
        // the viewport size - cards reflow instead of sitting at fixed positions.
        public static RectTransform CreateScrollGridPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 cellSize, Vector2 spacing, RectOffset padding)
        {
            RectTransform viewport = CreateRect(parent, name, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            StyleRounded(viewportImage, PanelDarker);
            viewport.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            RectTransform content = CreateRect(viewport, name + " content", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.padding = padding;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
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

        // ---------- line chart primitives ----------
        // Minimal polyline chart built from thin rotated Image segments plus small
        // circular point markers - not a general charting library, just enough to
        // plot N series of (round, cumulative points) on a shared axis for the
        // championship progression graph (RuntimeUi.ShowChampionshipGraphs).
        // Callers convert data values to local pixel positions within the plot
        // area's own rect (0,0 = the rect's bottom-left corner) and pass those in;
        // these helpers only draw. Reading plotArea.rect.width/height synchronously
        // right after it's built is safe here because it is purely anchor/offset
        // positioned (no LayoutGroup driving its size) - the same pattern
        // RaceHud.ApplyRaceControlBannerSize already relies on.

        // Plain dark panel used as the backing/clip area for a chart's plotted
        // lines - just a styled CreateRect, kept as its own named helper so chart
        // call sites read as "build a plot area" rather than a bare CreateRect.
        public static RectTransform CreateChartPlotArea(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform plot = CreateRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            Image background = plot.gameObject.AddComponent<Image>();
            StyleRounded(background, PanelDarker);
            return plot;
        }

        // Generic color-cycling palette for chart series that aren't the
        // player/player-team (which always get Accent) or the player's teammate
        // (AccentCyan) - there was no existing UI-side palette to reuse (the only
        // Color[] palette in the codebase, TrackManager.SponsorPalette, is for 3D
        // trackside sponsor boards, not UI).
        public static readonly Color[] ChartPalette =
        {
            new Color(0.36f, 0.62f, 1f),
            new Color(1f, 0.55f, 0.25f),
            new Color(0.55f, 0.85f, 0.35f),
            new Color(0.85f, 0.4f, 0.85f),
            new Color(0.95f, 0.85f, 0.3f),
            new Color(0.4f, 0.85f, 0.85f),
            new Color(0.75f, 0.55f, 0.35f),
            new Color(0.6f, 0.6f, 0.95f),
            new Color(0.9f, 0.5f, 0.5f),
            new Color(0.5f, 0.9f, 0.65f),
        };

        // Faint full-width horizontal reference lines at evenly spaced fractions
        // of the plot's height, each with a small value label on the left edge -
        // a bare set of data lines with no reference grid is very hard to read.
        public static void DrawChartGridlines(RectTransform plotArea, int lineCount, Func<float, string> labelForFraction)
        {
            for (int i = 0; i <= lineCount; i++)
            {
                float fraction = lineCount <= 0 ? 0f : i / (float)lineCount;
                RectTransform line = CreateBand(plotArea, "Grid line " + i, new Vector2(0f, fraction), new Vector2(1f, fraction), new Vector2(52f, 0f), new Vector2(0f, 1f), new Color(1f, 1f, 1f, 0.06f));
                line.GetComponent<Image>().raycastTarget = false;
                if (labelForFraction != null)
                {
                    Text label = CreateText(plotArea, "Grid label " + i, labelForFraction(fraction), 12, TextMuted, TextAnchor.MiddleRight);
                    RectTransform labelRect = label.GetComponent<RectTransform>();
                    labelRect.anchorMin = new Vector2(0f, fraction);
                    labelRect.anchorMax = new Vector2(0f, fraction);
                    labelRect.pivot = new Vector2(0f, 0.5f);
                    labelRect.anchoredPosition = new Vector2(2f, 0f);
                    labelRect.sizeDelta = new Vector2(46f, 16f);
                    label.raycastTarget = false;
                }
            }
        }

        // One thin rotated Image between two points in the plot area's local
        // space (points measured from its bottom-left corner, +x right, +y up) -
        // the basic segment primitive every polyline below is built from.
        public static Image DrawChartLineSegment(RectTransform parent, string name, Vector2 pointA, Vector2 pointB, float thickness, Color color)
        {
            RectTransform segment = CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            segment.pivot = new Vector2(0f, 0.5f);
            segment.anchoredPosition = pointA;
            float distance = Vector2.Distance(pointA, pointB);
            segment.sizeDelta = new Vector2(distance, thickness);
            float angle = Mathf.Atan2(pointB.y - pointA.y, pointB.x - pointA.x) * Mathf.Rad2Deg;
            segment.localRotation = Quaternion.Euler(0f, 0f, angle);
            Image image = segment.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        // Full polyline through an ordered list of local-space points.
        public static List<Image> DrawChartPolyline(RectTransform parent, string namePrefix, List<Vector2> points, float thickness, Color color)
        {
            List<Image> segments = new List<Image>();
            if (points == null)
            {
                return segments;
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                segments.Add(DrawChartLineSegment(parent, namePrefix + " seg " + i, points[i], points[i + 1], thickness, color));
            }

            return segments;
        }

        // Small tappable circular marker at a chart point - the "tap a point for
        // detail" interaction (full pointer-hover tooltips would need extra
        // IPointerEnter plumbing this simple widget set doesn't have).
        public static Button CreateChartPoint(RectTransform parent, string name, Vector2 pointLocal, float size, Color color, UnityAction onTap)
        {
            RectTransform dot = CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            dot.sizeDelta = new Vector2(size, size);
            dot.anchoredPosition = pointLocal;
            Image image = dot.gameObject.AddComponent<Image>();
            image.sprite = CircleSprite;
            image.color = color;
            Button button = dot.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            if (onTap != null)
            {
                button.onClick.AddListener(() =>
                {
                    SimpleAudioManager.PlayClick();
                    onTap.Invoke();
                });
            }

            return button;
        }
    }
}
