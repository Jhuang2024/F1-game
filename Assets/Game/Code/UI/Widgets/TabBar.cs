using System.Collections.Generic;
using UnityEngine;

namespace F1Game.UI.Widgets
{
    /// <summary>
    /// Horizontal tab strip built from ThemedButtons. Selection uses the
    /// buttons' Selected state; bumper/Q-E cycling is wired by the shell's
    /// input handling calling <see cref="Step"/>.
    /// </summary>
    public class TabBar : MonoBehaviour
    {
        [SerializeField] List<ThemedButton> tabs = new List<ThemedButton>();

        int selectedIndex;

        public int SelectedIndex => selectedIndex;
        public event System.Action<int> SelectionChanged;

        public void Bind(List<ThemedButton> tabButtons)
        {
            tabs = tabButtons;
            for (int i = 0; i < tabs.Count; i++)
            {
                int index = i;
                tabs[i].Clicked += () => Select(index);
            }

            Select(0, notify: false);
        }

        public void Select(int index, bool notify = true)
        {
            if (tabs.Count == 0)
            {
                return;
            }

            selectedIndex = Mathf.Clamp(index, 0, tabs.Count - 1);
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].SetSelectedInGroup(i == selectedIndex);
            }

            if (notify)
            {
                SelectionChanged?.Invoke(selectedIndex);
            }
        }

        public void Step(int direction)
        {
            Select((selectedIndex + direction + tabs.Count) % Mathf.Max(1, tabs.Count));
        }
    }
}
