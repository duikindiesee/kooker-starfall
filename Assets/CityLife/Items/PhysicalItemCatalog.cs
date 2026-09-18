using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.Items
{
    /// <summary>
    /// Authoritative catalog of immutable item definitions for the physical foundation.
    /// Provides configured definition lookups and model population to ensure cold bootstrap
    /// and save restoration validate strictly against known authoritative types rather than
    /// fabricating definitions, capacities, or physical attributes from untrusted save payloads.
    /// </summary>
    public sealed class PhysicalItemCatalog
    {
        private readonly Dictionary<string, ItemDefinition> definitions =
            new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

        public int Count => definitions.Count;

        public bool Register(ItemDefinition def)
        {
            if (def == null || !def.IsValid() || definitions.ContainsKey(def.itemTypeId))
                return false;

            // Store detached clone to prevent caller aliasing
            definitions.Add(def.itemTypeId, def.Clone());
            return true;
        }

        public bool RegisterOrUpdate(ItemDefinition def)
        {
            if (def == null || !def.IsValid())
                return false;

            // Store detached clone to prevent caller aliasing
            definitions[def.itemTypeId] = def.Clone();
            return true;
        }

        public bool TryGet(string itemTypeId, out ItemDefinition def)
        {
            if (string.IsNullOrEmpty(itemTypeId))
            {
                def = default;
                return false;
            }
            if (definitions.TryGetValue(itemTypeId, out var stored))
            {
                def = stored.Clone();
                return true;
            }
            def = default;
            return false;
        }

        public bool Contains(string itemTypeId)
        {
            return !string.IsNullOrEmpty(itemTypeId) && definitions.ContainsKey(itemTypeId);
        }

        public IEnumerable<ItemDefinition> GetAll()
        {
            foreach (var def in definitions.Values)
            {
                yield return def.Clone();
            }
        }

        public void PopulateModel(ItemModel model)
        {
            if (model == null) return;
            foreach (var def in definitions.Values)
            {
                model.RegisterDefinition(def);
            }
        }

        public static PhysicalItemCatalog CreateDefaultCatalog()
        {
            var catalog = new PhysicalItemCatalog();

            // Demonstration canyon stone
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "canyon-stone",
                massKg = 2.5f,
                dimensions = new PhysicalDimensions(0.25f, 0.25f, 0.25f),
                isContainer = false,
                isAnchored = false,
                requiresSupportToPlace = false
            });

            // Standard container items
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "container-chest",
                dimensions = new PhysicalDimensions(0.5f, 0.4f, 0.4f),
                massKg = 3.0f,
                isContainer = true,
                maxContainedSlots = 6,
                maxContainedVolumeM3 = 0.5f,
                maxContainedMassKg = 30.0f
            });

            catalog.Register(new ItemDefinition
            {
                itemTypeId = "container-basket",
                dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                massKg = 1.0f,
                isContainer = true,
                maxContainedSlots = 4,
                maxContainedVolumeM3 = 0.1f,
                maxContainedMassKg = 10.0f
            });

            catalog.Register(new ItemDefinition
            {
                itemTypeId = "container-pouch",
                dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                massKg = 0.2f,
                isContainer = true,
                maxContainedSlots = 2,
                maxContainedVolumeM3 = 0.01f,
                maxContainedMassKg = 2.0f
            });

            catalog.Register(new ItemDefinition
            {
                itemTypeId = "tool-chisel",
                dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                massKg = 0.5f,
                isContainer = false
            });

            catalog.Register(new ItemDefinition
            {
                itemTypeId = "gem-ruby",
                dimensions = new PhysicalDimensions(0.02f, 0.02f, 0.02f),
                massKg = 0.1f,
                isContainer = false
            });

            // Environment block types
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "type-wood-block",
                dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                massKg = 2.0f,
                isContainer = false
            });

            catalog.Register(new ItemDefinition
            {
                itemTypeId = "type-boulder",
                dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                massKg = 30.0f,
                isContainer = false
            });

            catalog.Register(new ItemDefinition
            {
                itemTypeId = "type-anvil",
                dimensions = new PhysicalDimensions(0.4f, 0.3f, 0.4f),
                massKg = 50.0f,
                isAnchored = true
            });

            // Natural Starfall Survival Resources (AG3 - Natural Stones)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "stone-river-cobble",
                dimensions = new PhysicalDimensions(0.24f, 0.16f, 0.20f),
                massKg = 1.8f,
                isContainer = false
            });

            catalog.Register(new ItemDefinition
            {
                itemTypeId = "stone-fieldstone",
                dimensions = new PhysicalDimensions(0.28f, 0.22f, 0.25f),
                massKg = 2.4f,
                isContainer = false
            });

            // Natural Starfall Survival Resources (AG4 - Tinder Dry Brush)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "fire-tinder-bundle",
                dimensions = new PhysicalDimensions(0.35f, 0.25f, 0.25f),
                massKg = 0.35f,
                isContainer = false
            });

            // Natural Starfall Survival Resources (AG5 - Fallen Wood Branch)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "wood-fallen-branch",
                dimensions = new PhysicalDimensions(0.85f, 0.18f, 0.18f),
                massKg = 3.2f,
                isContainer = false
            });

            // Handcrafted Stone Tools (Knapping Workstation)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "tool-stone-blade",
                dimensions = new PhysicalDimensions(0.15f, 0.08f, 0.03f),
                massKg = 0.25f,
                isContainer = false
            });

            catalog.Register(new ItemDefinition
            {
                itemTypeId = "tool-fire-striker",
                dimensions = new PhysicalDimensions(0.12f, 0.07f, 0.04f),
                massKg = 0.30f,
                isContainer = false
            });

            // Smooth River Pebbles (Shallow river bed collection & knapping stock)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "stone-river-pebble",
                dimensions = new PhysicalDimensions(0.12f, 0.08f, 0.10f),
                massKg = 0.65f,
                isContainer = false
            });

            // Leather Gathering Bag (Equipped on hip, expanding pebble & resource carry capacity)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "container-leather-bag",
                dimensions = new PhysicalDimensions(0.25f, 0.20f, 0.20f),
                massKg = 0.45f,
                isContainer = true,
                maxContainedSlots = 6,
                maxContainedVolumeM3 = 0.05f,
                maxContainedMassKg = 15.0f
            });

            // Marine Protein Crab (Harvested in coastal shallows & river delta)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "food-protein-crab",
                dimensions = new PhysicalDimensions(0.22f, 0.16f, 0.09f),
                massKg = 0.45f,
                isContainer = false
            });

            // Coastal Driftwood Log (Washed ashore by ocean tides, high thermal hearth fuel)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "wood-driftwood-log",
                dimensions = new PhysicalDimensions(0.95f, 0.22f, 0.22f),
                massKg = 4.2f,
                isContainer = false
            });

            // Ripe Sourfig Berry (Hand-held botanical fruit, nutritious drive reduction)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "food-sourfig-berry",
                dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                massKg = 0.08f,
                isContainer = false
            });

            // Freshwater River Fish (Nutritious salmonoid swimming in river channel)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "food-river-fish",
                dimensions = new PhysicalDimensions(0.35f, 0.12f, 0.08f),
                massKg = 0.65f,
                isContainer = false
            });

            // Hunter Waist Moonbag (Side belt pouch holding up to 2 fruits for long journeys)
            catalog.Register(new ItemDefinition
            {
                itemTypeId = "container-waist-bag",
                dimensions = new PhysicalDimensions(0.18f, 0.14f, 0.12f),
                massKg = 0.28f,
                isContainer = true,
                maxContainedSlots = 2,
                maxContainedVolumeM3 = 0.015f,
                maxContainedMassKg = 1.0f
            });

            return catalog;
        }
    }
}
