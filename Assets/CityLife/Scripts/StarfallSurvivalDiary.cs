using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    /// <summary>
    /// Inhabitant's Illustrated Survival Diary & Gamification System:
    /// Transforms raw debug codes into a rich, immersive first-person survival chronicle.
    /// Tracks achievements, survival milestones, and diurnal day progression.
    /// </summary>
    public sealed class StarfallSurvivalDiary : MonoBehaviour
    {
        [Serializable]
        public struct DiaryEntry
        {
            public int tick;
            public int day;
            public string timeLabel;
            public string category; // Food, Danger, Crafting, Journey, Rest
            public string narrative;
        }

        [Serializable]
        public sealed class SurvivalMilestone
        {
            public string id;
            public string title;
            public string description;
            public bool achieved;
            public int achievedTick;
        }

        public readonly List<DiaryEntry> Entries = new List<DiaryEntry>();
        public readonly List<SurvivalMilestone> Milestones = new List<SurvivalMilestone>();

        private int lastRecordedTick = -1;

        private void Awake()
        {
            InitializeMilestones();
        }

        public void InitializeMilestones()
        {
            if (Milestones.Count > 0) return;
            Milestones.Add(new SurvivalMilestone
            {
                id = "first-feast",
                title = "Sweet Sustenance",
                description = "Forage and eat wild sourfig berries from the canyon scrub.",
                achieved = false
            });
            Milestones.Add(new SurvivalMilestone
            {
                id = "river-harvest",
                title = "Tidepool Hunter",
                description = "Harvest a marine protein crab or catch a river fish in the shallows.",
                achieved = false
            });
            Milestones.Add(new SurvivalMilestone
            {
                id = "wolf-warded",
                title = "Predator Warded",
                description = "Brandish the hunter's club and strike a stalking timber wolf.",
                achieved = false
            });
            Milestones.Add(new SurvivalMilestone
            {
                id = "packed-shelter",
                title = "Stonemason's Redoubt",
                description = "Gather river cobbles and pack a fortified dry-stone wolf shelter.",
                achieved = false
            });
            Milestones.Add(new SurvivalMilestone
            {
                id = "waterfall-discovered",
                title = "The Roaring Headwall",
                description = "Navigate the deep south gorge and discover the Waterfall Cascade.",
                achieved = false
            });
            Milestones.Add(new SurvivalMilestone
            {
                id = "night-survived",
                title = "Survivor of the Dark",
                description = "Endure a full nocturnal chill cycle safely sheltered by fire or packed stone.",
                achieved = false
            });
            Milestones.Add(new SurvivalMilestone
            {
                id = "master-surveyor",
                title = "Cartographer of the Canyon",
                description = "Map over 1,000 cells of coastal terrain and canyon meanders.",
                achieved = false
            });
        }

        public bool UnlockMilestone(string id, int tick)
        {
            if (Milestones.Count == 0) InitializeMilestones();
            for (int i = 0; i < Milestones.Count; i++)
            {
                if (Milestones[i].id == id && !Milestones[i].achieved)
                {
                    Milestones[i].achieved = true;
                    Milestones[i].achievedTick = tick;
                    AddEntry(tick, "Milestone", $"★ Milestone Unlocked: [{Milestones[i].title}] — {Milestones[i].description}");
                    return true;
                }
            }
            return false;
        }

        public bool IsMilestoneUnlocked(string id)
        {
            if (Milestones.Count == 0) InitializeMilestones();
            for (int i = 0; i < Milestones.Count; i++)
            {
                if (Milestones[i].id == id) return Milestones[i].achieved;
            }
            return false;
        }

        public void AddEntry(int tick, string category, string narrative, float timeOfDay01 = 0.5f)
        {
            if (tick <= lastRecordedTick && Entries.Count > 0 && Entries[Entries.Count - 1].category == category)
                return; // Suppress duplicate spam within the same tick

            lastRecordedTick = tick;
            int day = Mathf.Max(1, (tick / 12000) + 1);

            string timeLabel;
            if (timeOfDay01 < 0.20f) timeLabel = "Dawn";
            else if (timeOfDay01 < 0.45f) timeLabel = "Morning";
            else if (timeOfDay01 < 0.55f) timeLabel = "High Noon";
            else if (timeOfDay01 < 0.75f) timeLabel = "Afternoon";
            else if (timeOfDay01 < 0.88f) timeLabel = "Sunset / Dusk";
            else timeLabel = "Night";

            Entries.Add(new DiaryEntry
            {
                tick = tick,
                day = day,
                timeLabel = timeLabel,
                category = category,
                narrative = narrative
            });

            if (Entries.Count > 60)
            {
                Entries.RemoveAt(0);
            }
        }

        public string GetRecentDiarySummary(int count = 4)
        {
            if (Entries.Count == 0)
                return "Day 1 · Morning: Woke by the cold river. The canyon opens before me.";

            var sb = new System.Text.StringBuilder();
            int start = Mathf.Max(0, Entries.Count - count);
            for (int i = start; i < Entries.Count; i++)
            {
                var e = Entries[i];
                sb.Append($"<b>[Day {e.day} · {e.timeLabel}]</b> {e.narrative}\n\n");
            }
            return sb.ToString().TrimEnd();
        }

        public string GetMilestonesSummary()
        {
            var sb = new System.Text.StringBuilder("<b>SURVIVAL MILESTONES & ACHIEVEMENTS:</b>\n");
            int completed = 0;
            for (int i = 0; i < Milestones.Count; i++)
            {
                var m = Milestones[i];
                if (m.achieved) completed++;
                string badge = m.achieved ? "<color=#55FF88>[✓ UNLOCKED]</color>" : "<color=#888888>[░ LOCKED]</color>";
                sb.Append($"{badge} <b>{m.title}</b>: {m.description}\n");
            }
            sb.Append($"\nProgress: <b>{completed} / {Milestones.Count}</b> Mastered");
            return sb.ToString();
        }
    }
}
