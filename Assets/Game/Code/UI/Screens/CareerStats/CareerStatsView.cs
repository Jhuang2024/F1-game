using System;
using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.CareerStats
{
    /// <summary>
    /// Production career-stats screen: a grid of career stat tiles and a list of
    /// local track records, both read-only. Pooled StatTiles and TMP rows are filled
    /// from the model; the presenter owns only the Back action. Pure presentation.
    /// </summary>
    public class CareerStatsView : ScreenView
    {
        public const string Id = "career-stats";

        [SerializeField] RectTransform statContainer;
        [SerializeField] StatTile statTemplate;
        [SerializeField] RectTransform recordContainer;
        [SerializeField] TMP_Text recordTemplate;
        [SerializeField] ThemedButton backButton;

        readonly List<StatTile> statTiles = new List<StatTile>();
        readonly List<TMP_Text> recordRows = new List<TMP_Text>();

        public event Action BackClicked;

        public void Bind(RectTransform stats, StatTile statTile, RectTransform records, TMP_Text recordRow, ThemedButton back)
        {
            statContainer = stats;
            statTemplate = statTile;
            recordContainer = records;
            recordTemplate = recordRow;
            backButton = back;

            if (statTemplate != null)
            {
                statTemplate.gameObject.SetActive(false);
            }

            if (recordTemplate != null)
            {
                recordTemplate.gameObject.SetActive(false);
            }

            if (backButton != null)
            {
                backButton.Clicked += () => BackClicked?.Invoke();
            }

            SetScreenId(Id);
            SetDefaultSelection(back != null ? back.gameObject : null);
        }

        public void Render(CareerStatsModel model)
        {
            CareerStatsModel m = model ?? new CareerStatsModel();
            RenderStats(m.stats);
            RenderRecords(m.records, m.emptyRecordsMessage);
        }

        void RenderStats(IReadOnlyList<CareerStatCell> stats)
        {
            if (statContainer == null || statTemplate == null)
            {
                return;
            }

            int count = stats != null ? stats.Count : 0;
            while (statTiles.Count < count)
            {
                statTiles.Add(Instantiate(statTemplate, statContainer));
            }

            for (int i = 0; i < count; i++)
            {
                statTiles[i].gameObject.SetActive(true);
                statTiles[i].Set(stats[i].label, stats[i].value);
            }

            for (int i = count; i < statTiles.Count; i++)
            {
                statTiles[i].gameObject.SetActive(false);
            }
        }

        void RenderRecords(IReadOnlyList<TrackRecordRow> records, string emptyMessage)
        {
            if (recordContainer == null || recordTemplate == null)
            {
                return;
            }

            int count = records != null ? records.Count : 0;
            // One extra row carries the empty-state message when there are no records.
            int rowsNeeded = count > 0 ? count : 1;
            while (recordRows.Count < rowsNeeded)
            {
                recordRows.Add(Instantiate(recordTemplate, recordContainer));
            }

            if (count == 0)
            {
                recordRows[0].gameObject.SetActive(true);
                recordRows[0].text = string.IsNullOrEmpty(emptyMessage) ? "No lap records yet." : emptyMessage;
                for (int i = 1; i < recordRows.Count; i++)
                {
                    recordRows[i].gameObject.SetActive(false);
                }

                return;
            }

            for (int i = 0; i < count; i++)
            {
                TrackRecordRow row = records[i];
                recordRows[i].gameObject.SetActive(true);
                string context = string.IsNullOrEmpty(row.context) ? "" : "   <align=right>" + row.context + "</align>";
                recordRows[i].text = row.track + "   " + row.time + context;
            }

            for (int i = count; i < recordRows.Count; i++)
            {
                recordRows[i].gameObject.SetActive(false);
            }
        }
    }
}
