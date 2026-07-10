using F1Game.UI.Services;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Widgets
{
    /// <summary>
    /// "[A] Select"-style prompt that re-resolves its glyph when the active
    /// input device changes (hot-plug / first press), via DevicePromptService.
    /// Until real glyph icon art lands (Assets/Game/Art/Icons), the prompt
    /// renders the glyph id as text — an explicit placeholder, not final art.
    /// </summary>
    public class ControllerPrompt : MonoBehaviour
    {
        [SerializeField] TMP_Text glyphText;
        [SerializeField] TMP_Text actionText;
        [SerializeField] string logicalButton = "confirm";

        DevicePromptService service;

        public void Bind(TMP_Text glyph, TMP_Text action, string logical)
        {
            glyphText = glyph;
            actionText = action;
            logicalButton = logical;
        }

        public void Attach(DevicePromptService promptService, string actionLabel)
        {
            if (service != null)
            {
                service.DeviceKindChanged -= OnDeviceChanged;
            }

            service = promptService;
            service.DeviceKindChanged += OnDeviceChanged;
            if (actionText != null)
            {
                actionText.text = actionLabel;
            }

            Refresh();
        }

        void OnDestroy()
        {
            if (service != null)
            {
                service.DeviceKindChanged -= OnDeviceChanged;
            }
        }

        void OnDeviceChanged(Core.Events.InputDeviceKind _)
        {
            Refresh();
        }

        void Refresh()
        {
            if (glyphText != null && service != null)
            {
                glyphText.text = "[" + service.GlyphId(logicalButton) + "]";
            }
        }
    }
}
