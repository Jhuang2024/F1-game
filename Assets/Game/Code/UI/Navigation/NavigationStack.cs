using System.Collections.Generic;

namespace F1Game.UI.Navigation
{
    /// <summary>
    /// Back-stack of screen ids (with the args they were opened with) so Back /
    /// controller-B always returns to the previous screen without each screen
    /// hand-wiring its own "back" target the way the legacy RuntimeUi did.
    /// </summary>
    public sealed class NavigationStack
    {
        public readonly struct Entry
        {
            public readonly string ScreenId;
            public readonly object Args;

            public Entry(string screenId, object args)
            {
                ScreenId = screenId;
                Args = args;
            }
        }

        readonly List<Entry> entries = new List<Entry>();

        public int Count => entries.Count;
        public Entry Current => entries.Count > 0 ? entries[entries.Count - 1] : default;

        public void Push(string screenId, object args)
        {
            entries.Add(new Entry(screenId, args));
        }

        public bool TryPop(out Entry previous)
        {
            previous = default;
            if (entries.Count < 2)
            {
                return false;
            }

            entries.RemoveAt(entries.Count - 1);
            previous = entries[entries.Count - 1];
            return true;
        }

        /// <summary>Replace the current entry (redirects that shouldn't grow the stack).</summary>
        public void ReplaceCurrent(string screenId, object args)
        {
            if (entries.Count > 0)
            {
                entries.RemoveAt(entries.Count - 1);
            }

            entries.Add(new Entry(screenId, args));
        }

        public void Clear()
        {
            entries.Clear();
        }
    }
}
