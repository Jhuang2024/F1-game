using System;
using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.Rnd
{
    /// <summary>
    /// Production R&D command centre: a resource summary and three pooled sections -
    /// departments (upgrade), active projects (rework/abandon) and available upgrades
    /// (start). Display only: each row action raises a typed event the presenter maps
    /// to an authoritative CareerManager command; the view is rebuilt from saved state
    /// after each mutation, which also disables now-ineligible buttons.
    /// </summary>
    public class RndView : ScreenView
    {
        public const string Id = "rnd-center";

        [SerializeField] TMP_Text summaryLabel;
        [SerializeField] RectTransform departmentContainer;
        [SerializeField] RectTransform projectContainer;
        [SerializeField] RectTransform upgradeContainer;
        [SerializeField] TMP_Text emptyProjectsLabel;
        [SerializeField] CareerActionRow departmentTemplate;
        [SerializeField] CareerActionRow projectTemplate;
        [SerializeField] CareerActionRow upgradeTemplate;
        [SerializeField] ThemedButton backButton;

        readonly List<CareerActionRow> departmentRows = new List<CareerActionRow>();
        readonly List<CareerActionRow> projectRows = new List<CareerActionRow>();
        readonly List<CareerActionRow> upgradeRows = new List<CareerActionRow>();

        public event Action<string> UpgradeDepartment;
        public event Action<string> StartUpgrade;
        public event Action<string> ReworkProject;
        public event Action<string> AbandonProject;
        public event Action BackClicked;

        public void Bind(TMP_Text summary, RectTransform departments, CareerActionRow departmentRow,
            RectTransform projects, CareerActionRow projectRow, TMP_Text emptyProjects,
            RectTransform upgrades, CareerActionRow upgradeRow, ThemedButton back)
        {
            summaryLabel = summary;
            departmentContainer = departments;
            departmentTemplate = departmentRow;
            projectContainer = projects;
            projectTemplate = projectRow;
            emptyProjectsLabel = emptyProjects;
            upgradeContainer = upgrades;
            upgradeTemplate = upgradeRow;
            backButton = back;

            HideTemplate(departmentTemplate);
            HideTemplate(projectTemplate);
            HideTemplate(upgradeTemplate);

            if (backButton != null)
            {
                backButton.Clicked += () => BackClicked?.Invoke();
            }

            SetScreenId(Id);
            SetDefaultSelection(back != null ? back.gameObject : null);
        }

        static void HideTemplate(CareerActionRow template)
        {
            if (template != null)
            {
                template.gameObject.SetActive(false);
            }
        }

        public void Render(RndModel model)
        {
            RndModel m = model ?? new RndModel();
            if (summaryLabel != null)
            {
                summaryLabel.text = m.summaryLine ?? "";
            }

            Fill(departmentRows, departmentTemplate, departmentContainer, m.departments,
                (id, which) => UpgradeDepartment?.Invoke(id));
            Fill(projectRows, projectTemplate, projectContainer, m.projects,
                (id, which) => { if (which == 0) ReworkProject?.Invoke(id); else AbandonProject?.Invoke(id); });
            Fill(upgradeRows, upgradeTemplate, upgradeContainer, m.upgrades,
                (id, which) => StartUpgrade?.Invoke(id));

            if (emptyProjectsLabel != null)
            {
                bool empty = m.projects.Count == 0;
                emptyProjectsLabel.gameObject.SetActive(empty);
                if (empty)
                {
                    emptyProjectsLabel.text = string.IsNullOrEmpty(m.emptyProjectsMessage)
                        ? "No projects in development."
                        : m.emptyProjectsMessage;
                }
            }
        }

        static void Fill(List<CareerActionRow> pool, CareerActionRow template, RectTransform container,
            List<RndRow> data, Action<string, int> handler)
        {
            if (template == null || container == null)
            {
                return;
            }

            while (pool.Count < data.Count)
            {
                CareerActionRow row = Instantiate(template, container);
                row.Clicked += handler;
                pool.Add(row);
            }

            for (int i = 0; i < data.Count; i++)
            {
                RndRow d = data[i];
                pool[i].gameObject.SetActive(true);
                pool[i].Set(d.id, d.label, d.detail,
                    d.primaryLabel, d.primaryShown, d.primaryEnabled,
                    d.secondaryLabel, d.secondaryShown, d.secondaryEnabled);
            }

            for (int i = data.Count; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
            }
        }
    }
}
