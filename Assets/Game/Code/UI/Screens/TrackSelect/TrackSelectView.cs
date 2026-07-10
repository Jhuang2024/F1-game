using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.TrackSelect
{
    /// <summary>
    /// Quick-race track selection: scrollable list of track rows. Rows are
    /// pooled ThemedButton entries created against the row template once and
    /// re-bound on data changes, never destroyed per refresh.
    /// </summary>
    public class TrackSelectView : ScreenView
    {
        public const string Id = "quick-race-track-select";

        [SerializeField] TMP_Text header;
        [SerializeField] RectTransform rowContainer;
        [SerializeField] ThemedButton rowTemplate;
        [SerializeField] ThemedButton backButton;

        readonly List<ThemedButton> rows = new List<ThemedButton>();

        public ThemedButton BackButton => backButton;
        public event System.Action<int> RowActivated;

        public void Bind(TMP_Text headerText, RectTransform container, ThemedButton template, ThemedButton back)
        {
            header = headerText;
            rowContainer = container;
            rowTemplate = template;
            backButton = back;
            rowTemplate.gameObject.SetActive(false);
            SetScreenId(Id);
            SetDefaultSelection(back != null ? back.gameObject : null);
        }

        public void Render(IReadOnlyList<TrackCardModel> tracks)
        {
            EnsureRowCount(tracks.Count);
            for (int i = 0; i < tracks.Count; i++)
            {
                TrackCardModel model = tracks[i];
                ThemedButton row = rows[i];
                row.gameObject.SetActive(true);
                row.SetText(string.Format(
                    "{0}   <color=#9EA2AA>{1} · {2:0.0} km · {3} laps · {4}</color>",
                    model.trackName, model.location, model.lengthKm, model.laps, model.weatherHint));
            }

            for (int i = tracks.Count; i < rows.Count; i++)
            {
                rows[i].gameObject.SetActive(false);
            }

            if (rows.Count > 0 && rows[0].gameObject.activeSelf)
            {
                SetDefaultSelection(rows[0].gameObject);
            }
        }

        void EnsureRowCount(int count)
        {
            while (rows.Count < count)
            {
                int index = rows.Count;
                ThemedButton row = Instantiate(rowTemplate, rowContainer);
                row.Clicked += () => RowActivated?.Invoke(index);
                rows.Add(row);
            }
        }
    }
}
