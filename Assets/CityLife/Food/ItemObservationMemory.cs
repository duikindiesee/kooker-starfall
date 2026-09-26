using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starfall.Food
{
    [Serializable] public sealed class KnownItemObservation
    {
        public string world, generation, actor, id, role, hash = "";
        public string source = "scoped-npc-los-perception";
        public Vector3 position;
        public int foodTick, brainTick;
        public bool available, permitted;
    }

    // Last observed positions are beliefs, never authority to manipulate unseen items.
    // Kept separate from the place ledger so movable tools remain movable items.
    public static class ItemObservationMemory
    {
        public const int Capacity = 128;
        public static bool Observe(FoodState state, string id, string role, Vector3 position,
            bool available, bool permitted, int brainTick)
        {
            if (state == null || !FoodModel.Id(id) || (role != "rod" && role != "basket") ||
                !Finite(position) || brainTick < 0) return false;
            state.observedItems ??= new List<KnownItemObservation>();
            var prior = state.observedItems.Find(x => x.id == id);
            if (prior != null && prior.foodTick > state.tick) return false;
            if (prior == null && state.observedItems.Count >= Capacity) return false;
            var entry = new KnownItemObservation { world = state.world, generation = state.generation,
                actor = state.actorId, id = id, role = role, position = position,
                available = available, permitted = permitted, foodTick = state.tick, brainTick = brainTick };
            entry.hash = FoodModel.Hash(JsonUtility.ToJson(entry));
            if (prior != null) state.observedItems[state.observedItems.IndexOf(prior)] = entry;
            else state.observedItems.Add(entry);
            return true;
        }

        public static KnownItemObservation Nearest(FoodState state, string role, Vector3 origin)
        {
            KnownItemObservation nearest = null;
            if (state?.observedItems == null) return null;
            foreach (var entry in state.observedItems)
                if (entry.role == role && entry.available && entry.permitted &&
                    (nearest == null || (entry.position - origin).sqrMagnitude < (nearest.position - origin).sqrMagnitude)) nearest = entry;
            return nearest;
        }

        public static bool Valid(FoodState state)
        {
            if (state.observedItems == null) return true; // Pre-feature save.
            if (state.observedItems.Count > Capacity) return false;
            var ids = new HashSet<string>();
            foreach (var entry in state.observedItems)
            {
                if (entry == null || entry.world != state.world || entry.generation != state.generation ||
                    entry.actor != state.actorId || !FoodModel.Id(entry.id) || !ids.Add(entry.id) ||
                    (entry.role != "rod" && entry.role != "basket") || entry.source != "scoped-npc-los-perception" ||
                    entry.foodTick < 0 || entry.foodTick > state.tick || entry.brainTick < 0 || !Finite(entry.position)) return false;
                var copy = JsonUtility.FromJson<KnownItemObservation>(JsonUtility.ToJson(entry));
                copy.hash = "";
                if (entry.hash != FoodModel.Hash(JsonUtility.ToJson(copy))) return false;
            }
            return true;
        }
        private static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) && p.magnitude < 10000;
    }
}
