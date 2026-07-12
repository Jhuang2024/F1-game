using System;
using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.PracticePrograms
{
    /// <summary>
    /// Production practice-programs screen: a list of this round's programs with their
    /// reward and completed state, each with a Run button (hidden once completed).
    /// Running a program raises <see cref="RunProgram"/>; the bridge launches the
    /// gameplay practice session. Pure presentation.
    /// </summary>
    public class PracticeProgramsView : ScreenView
    {
        public const string Id = "practice-programs";

        [SerializeField] TMP_Text summaryLabel;
        [SerializeField] RectTransform rowContainer;
        [SerializeField] CareerActionRow rowTemplate;
        [SerializeField] ThemedButton backButton;

        readonly List<CareerActionRow> rows = new List<CareerActionRow>();

        public event Action<string> RunProgram;
        public event Action BackClicked;

        public void Bind(TMP_Text summary, RectTransform container, CareerActionRow template, ThemedButton back)
        {
            summaryLabel = summary;
            rowContainer = container;
            rowTemplate = template;
            backButton = back;
            if (rowTemplate != null)
            {
                rowTemplate.gameObject.SetActive(false);
            }

            if (backButton != null)
            {
                backButton.Clicked += () => BackClicked?.Invoke();
            }

            SetScreenId(Id);
            SetDefaultSelection(back != null ? back.gameObject : null);
        }

        public void Render(PracticeProgramsModel model)
        {
            PracticeProgramsModel m = model ?? new PracticeProgramsModel();
            if (summaryLabel != null)
            {
                summaryLabel.text = m.summaryLine ?? "";
            }

            if (rowTemplate == null || rowContainer == null)
            {
                return;
            }

            while (rows.Count < m.programs.Count)
            {
                CareerActionRow row = Instantiate(rowTemplate, rowContainer);
                row.Clicked += (id, which) => RunProgram?.Invoke(id);
                rows.Add(row);
            }

            for (int i = 0; i < m.programs.Count; i++)
            {
                RndRow d = m.programs[i];
                rows[i].gameObject.SetActive(true);
                rows[i].Set(d.id, d.label, d.detail, d.primaryLabel, d.primaryShown, d.primaryEnabled);
            }

            for (int i = m.programs.Count; i < rows.Count; i++)
            {
                rows[i].gameObject.SetActive(false);
            }
        }
    }
}
