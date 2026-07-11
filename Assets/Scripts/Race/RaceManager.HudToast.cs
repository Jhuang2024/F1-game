using System.Collections.Generic;
using F1Game.Core.Events;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager HUD-toast subsystem (partial). The small toast queue and its
    /// UI-agnostic colour-kind constants (RaceManager has no UI dependency; RaceHud
    /// maps the ints back to its palette when it drains the queue), plus enqueue
    /// (which also publishes a HudToastEvent for the production notification feed)
    /// and dequeue. Split out of the RaceManager monolith verbatim - same class,
    /// same members, identical queue cap and tone mapping; the HudToast struct and
    /// the public colour consts stay nested (RaceManager.ToastColor*) so external
    /// callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        struct HudToast { public string text; public int colorKind; }
        readonly Queue<HudToast> hudToastQueue = new Queue<HudToast>();
        const int HudToastQueueCap = 6;

        // HUD toast color kinds - kept as small ints so RaceManager (which has no
        // UI dependency) doesn't need to reference UiFactory colors directly;
        // RaceHud maps these back to its own palette when it drains the queue.
        public const int ToastColorNeutral = 0;
        public const int ToastColorGreen = 1;
        public const int ToastColorAmber = 2;
        public const int ToastColorCyan = 3;
        public const int ToastColorPurple = 4;
        public const int ToastColorAccent = 5;

        void QueueHudToast(string text, int colorKind)
        {
            if (string.IsNullOrEmpty(text) || hudToastQueue.Count >= HudToastQueueCap)
            {
                return;
            }

            hudToastQueue.Enqueue(new HudToast { text = text, colorKind = colorKind });

            // Publish at the source (in addition to the legacy queue) so the
            // production notification feed sees the same toasts without draining
            // the queue the legacy HUD still consumes - exactly one HUD is live,
            // but both paths stay correct. Tone maps the legacy colour kinds:
            // green -> positive, amber -> caution, everything else -> neutral.
            int tone = colorKind == ToastColorGreen ? 0 : (colorKind == ToastColorAmber ? 1 : 2);
            GameEvents.Publish(new HudToastEvent(tone, text));
        }

        public bool TryDequeueHudToast(out string text, out int colorKind)
        {
            if (hudToastQueue.Count == 0)
            {
                text = "";
                colorKind = ToastColorNeutral;
                return false;
            }

            HudToast toast = hudToastQueue.Dequeue();
            text = toast.text;
            colorKind = toast.colorKind;
            return true;
        }

    }
}
