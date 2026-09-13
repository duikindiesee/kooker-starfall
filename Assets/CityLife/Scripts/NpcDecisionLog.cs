using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    [Serializable] public sealed class NpcDecisionEvent
    {
        public int sequence, tick;
        public string phase, perception, goal, action, result, fallback;
        public Vector3 position;
    }
    public sealed class NpcDecisionLog : MonoBehaviour
    {
        public const int Capacity = 128;
        public List<NpcDecisionEvent> Entries = new List<NpcDecisionEvent>();
        public int Total { get; private set; }
        public void Record(int tick, string phase, string perception, string goal, string action, string result, string fallback = "")
        {
            if (Entries.Count == Capacity) Entries.RemoveAt(0);
            Entries.Add(new NpcDecisionEvent { sequence = ++Total, tick = tick, phase = phase,
                perception = perception, goal = goal, action = action, result = result, fallback = fallback, position = transform.position });
        }
        public void ResetLog() { Entries.Clear(); Total = 0; }
    }
}
