using System.Collections.Generic;
using F1Game.Core.Events;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.RaceHudShell
{
    /// <summary>
    /// Event-driven, pooled HUD notification feed. Subscribes to penalty,
    /// retirement, pit and radio events on the bus — no polling, no
    /// Instantiate/Destroy churn (rows are recycled), and critical messages
    /// (priority 0) evict informational ones rather than queueing behind them.
    /// </summary>
    public class NotificationFeed : MonoBehaviour
    {
        [SerializeField] RectTransform container;
        [SerializeField] TMP_Text rowTemplate;
        [SerializeField] int maxVisible = 4;
        [SerializeField] float rowSeconds = 4.5f;

        sealed class Row
        {
            public TMP_Text Text;
            public float ExpiresAt;
            public int Priority;
        }

        readonly List<Row> live = new List<Row>();
        readonly Stack<TMP_Text> pool = new Stack<TMP_Text>();

        public void Bind(RectTransform rowContainer, TMP_Text template)
        {
            container = rowContainer;
            rowTemplate = template;
            rowTemplate.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            GameEvents.Subscribe<PenaltyIssuedEvent>(OnPenalty);
            GameEvents.Subscribe<RetirementEvent>(OnRetirement);
            GameEvents.Subscribe<PitRequestChangedEvent>(OnPitRequest);
            GameEvents.Subscribe<RadioMessageEvent>(OnRadio);
        }

        void OnDisable()
        {
            GameEvents.Unsubscribe<PenaltyIssuedEvent>(OnPenalty);
            GameEvents.Unsubscribe<RetirementEvent>(OnRetirement);
            GameEvents.Unsubscribe<PitRequestChangedEvent>(OnPitRequest);
            GameEvents.Unsubscribe<RadioMessageEvent>(OnRadio);
        }

        void OnPenalty(PenaltyIssuedEvent evt)
        {
            Push(string.Format("PENALTY  {0}  +{1:0}s — {2}", evt.DriverId, evt.Seconds, evt.Reason), 0);
        }

        void OnRetirement(RetirementEvent evt)
        {
            Push(string.Format("RETIRED  {0} — {1}", evt.DriverId, evt.Reason), 1);
        }

        void OnPitRequest(PitRequestChangedEvent evt)
        {
            if (evt.State == PitRequestState.Requested)
            {
                Push("PIT CONFIRMED — box this window", 1);
            }
            else if (evt.State == PitRequestState.Cancelled)
            {
                Push("PIT CANCELLED — staying out", 1);
            }
        }

        void OnRadio(RadioMessageEvent evt)
        {
            Push(evt.Message, evt.Priority);
        }

        public void Push(string message, int priority)
        {
            if (container == null || rowTemplate == null)
            {
                return;
            }

            if (live.Count >= maxVisible)
            {
                // Evict the lowest-value row (highest priority number, oldest).
                int evictIndex = 0;
                for (int i = 1; i < live.Count; i++)
                {
                    if (live[i].Priority > live[evictIndex].Priority ||
                        (live[i].Priority == live[evictIndex].Priority && live[i].ExpiresAt < live[evictIndex].ExpiresAt))
                    {
                        evictIndex = i;
                    }
                }

                if (live[evictIndex].Priority < priority)
                {
                    return; // everything visible is more important than the newcomer
                }

                Recycle(evictIndex);
            }

            TMP_Text text = pool.Count > 0 ? pool.Pop() : Instantiate(rowTemplate, container);
            text.gameObject.SetActive(true);
            text.transform.SetAsFirstSibling();
            text.text = message;
            live.Add(new Row { Text = text, ExpiresAt = Time.unscaledTime + rowSeconds, Priority = priority });
        }

        void Update()
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime >= live[i].ExpiresAt)
                {
                    Recycle(i);
                }
            }
        }

        void Recycle(int index)
        {
            Row row = live[index];
            live.RemoveAt(index);
            row.Text.gameObject.SetActive(false);
            pool.Push(row.Text);
        }
    }
}
