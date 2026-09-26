using System;
using UnityEngine;
using CityLife.Items;

namespace CityLife.Food
{
    /// <summary>
    /// Trusted opt-in physical item definitions for harvestable ecological resources.
    /// Provides strict physical mass and volume dimensions for fruit and seed units.
    /// Does not alter default physical catalog or mint items automatically.
    /// </summary>
    public static class FoodPhysicalCatalog
    {
        public const string FruitTypeId = "food.fruit.v1";
        public const string SeedTypeId = "food.seed.v1";

        public static ItemDefinition CreateFruitDefinition()
        {
            return new ItemDefinition
            {
                itemTypeId = FruitTypeId,
                massKg = 0.020f,
                dimensions = new PhysicalDimensions(0.035f, 0.035f, 0.035f),
                isContainer = false,
                maxContainedSlots = 0,
                maxContainedVolumeM3 = 0f,
                maxContainedMassKg = 0f,
                isAnchored = false
            };
        }

        public static ItemDefinition CreateSeedDefinition()
        {
            return new ItemDefinition
            {
                itemTypeId = SeedTypeId,
                massKg = 0.0002f,
                dimensions = new PhysicalDimensions(0.006f, 0.006f, 0.006f),
                isContainer = false,
                maxContainedSlots = 0,
                maxContainedVolumeM3 = 0f,
                maxContainedMassKg = 0f,
                isAnchored = false
            };
        }

        public static void RegisterToModel(ItemModel model)
        {
            if (model == null) return;
            if (!model.TryGetDefinition(FruitTypeId, out _))
                model.RegisterDefinition(CreateFruitDefinition());
            if (!model.TryGetDefinition(SeedTypeId, out _))
                model.RegisterDefinition(CreateSeedDefinition());
        }
    }
}
