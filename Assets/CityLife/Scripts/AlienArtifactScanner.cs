using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    [Serializable]
    public sealed class ScanAnalysis
    {
        public string ItemTypeId;
        public string CommonName;
        public string ScientificClassification;
        public string ChemicalComposition;
        public string PhysicalProperties;
        public string PracticalUtility;
        public float FlammabilityRating;
        public float ThermalMassRating;
        public float FuelEnergyRating;
        public string HolographicReadout;
    }

    /// <summary>
    /// Extraterrestrial scanner terminal discovered on the Starfall coastal terrace.
    /// Provides empirical analysis of natural resources (wood, stones, dry brush, basket)
    /// delivering objective physical facts without directing player decisions.
    /// Integrates with the inhabitant's living memory, decision HUD, and survival planning.
    /// </summary>
    public sealed class AlienArtifactScanner : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public NpcDecisionHud Hud;
        public Light ScanLight;
        public Transform ScanningPad;
        public MeshRenderer VisualRenderer;

        public ScanAnalysis LastScan { get; private set; }
        public int TotalScansCompleted { get; private set; }
        public string CurrentStatus { get; private set; } = "Terminal online: waiting for resource placement.";

        private static readonly Dictionary<string, ScanAnalysis> KnowledgeBase = new Dictionary<string, ScanAnalysis>(StringComparer.Ordinal)
        {
            {
                "fire-tinder-bundle", new ScanAnalysis
                {
                    ItemTypeId = "fire-tinder-bundle",
                    CommonName = "Dried Needle & Brush Bundle",
                    ScientificClassification = "Pinophyta / Larix Fibrous Biomass (Desiccated)",
                    ChemicalComposition = "Cellulose 52%, Lignin 28%, Volatile Terpenes 9%",
                    PhysicalProperties = "Moisture: 4.1% | Porosity: 86% | Flashpoint: 175°C",
                    PracticalUtility = "Primary ignition medium. Catches friction/strike sparks instantly. Burns fast with high initial flame.",
                    FlammabilityRating = 0.98f,
                    ThermalMassRating = 0.05f,
                    FuelEnergyRating = 0.35f,
                    HolographicReadout = "SCAN: TINDER DRY BRUSH\n" +
                                         "• Status: Desiccated fibrous needle matrix\n" +
                                         "• Ignition: Extremely High (~175°C)\n" +
                                         "• Duration: Short (~40-60s)\n" +
                                         "• Fact: Captures initial spark to ignite heavier fuel."
                }
            },
            {
                "stone-river-cobble", new ScanAnalysis
                {
                    ItemTypeId = "stone-river-cobble",
                    CommonName = "Alluvial River Cobble",
                    ScientificClassification = "Vesicular Basalt / Olivine Metamorphic",
                    ChemicalComposition = "SiO2 49%, Al2O3 15%, FeO 11%, CaO 9%",
                    PhysicalProperties = "Density: 2,750 kg/m3 | Specific Heat: 0.84 kJ/kg*K | Thermal Shock: Stable",
                    PracticalUtility = "Refractory boundary stone. Blocks ground wind and prevents fire spread while absorbing and radiating thermal energy.",
                    FlammabilityRating = 0.0f,
                    ThermalMassRating = 0.92f,
                    FuelEnergyRating = 0.0f,
                    HolographicReadout = "SCAN: RIVER COBBLE\n" +
                                         "• Status: Dense water-smoothed basalt\n" +
                                         "• Heat Capacity: High (thermal battery)\n" +
                                         "• Thermal Shock: Highly resistant\n" +
                                         "• Fact: Surrounding embers with stones prevents wind extinction."
                }
            },
            {
                "stone-fieldstone", new ScanAnalysis
                {
                    ItemTypeId = "stone-fieldstone",
                    CommonName = "Angular Canyon Fieldstone",
                    ScientificClassification = "Siliciclastic Sandstone / Feldspathic Quartz",
                    ChemicalComposition = "Quartz 78%, Feldspar 14%, Clay matrix 8%",
                    PhysicalProperties = "Density: 2,400 kg/m3 | Hardness: 6.5 Mohs | Rough Texture",
                    PracticalUtility = "Perimeter windbreak and cooking surface. Elevates and stabilizes the hearth containment wall.",
                    FlammabilityRating = 0.0f,
                    ThermalMassRating = 0.85f,
                    FuelEnergyRating = 0.0f,
                    HolographicReadout = "SCAN: CANYON FIELDSTONE\n" +
                                         "• Status: Fractured angular sandstone\n" +
                                         "• Stability: High friction interlock\n" +
                                         "• Fire Rating: Inert windbreak substrate\n" +
                                         "• Fact: Essential to stack around hearth for canyon storm shielding."
                }
            },
            {
                "wood-fallen-branch", new ScanAnalysis
                {
                    ItemTypeId = "wood-fallen-branch",
                    CommonName = "Fallen Seasoned Hardwood Branch",
                    ScientificClassification = "Acacia / Combretum Lignified Branchwood",
                    ChemicalComposition = "Holocellulose 68%, Lignin 27%, Ash 1.2%",
                    PhysicalProperties = "Moisture: 7.8% | Specific Gravity: 0.74 | Energy Density: 18.8 MJ/kg",
                    PracticalUtility = "Sustained night fuel. Once ignited by tinder embers, produces long-duration radiative heat.",
                    FlammabilityRating = 0.65f,
                    ThermalMassRating = 0.40f,
                    FuelEnergyRating = 0.95f,
                    HolographicReadout = "SCAN: FALLEN HARDWOOD BRANCH\n" +
                                         "• Status: Seasoned branchwood with bark intact\n" +
                                         "• Energy Density: 18.8 MJ/kg (high)\n" +
                                         "• Burn Life: Extended (~300-600s per log)\n" +
                                         "• Fact: Requires established tinder embers to catch; sustains night heat."
                }
            },
            {
                "container-basket", new ScanAnalysis
                {
                    ItemTypeId = "container-basket",
                    CommonName = "Woven Reed Basket",
                    ScientificClassification = "Phragmites Cyperaceae Interwoven Structure",
                    ChemicalComposition = "Woven Phloem Fibres & Plant Sedge",
                    PhysicalProperties = "Mass: 1.0 kg | Internal Volume: 0.1 m3 | Capacity: 4 Grid Slots (10 kg)",
                    PracticalUtility = "Mobile utility storage. Enables transport of multiple natural resources simultaneously across rugged canyon terrain.",
                    FlammabilityRating = 0.70f,
                    ThermalMassRating = 0.10f,
                    FuelEnergyRating = 0.40f,
                    HolographicReadout = "SCAN: WOVEN BASKET\n" +
                                         "• Status: Flexible 4-slot container\n" +
                                         "• Net Capacity: 10.0 kg contained mass\n" +
                                         "• Utility: Allows hauling stones, tinder and wood in single trip\n" +
                                         "• Fact: Essential tool for pre-sunset resource stocking."
                }
            },
            {
                "canyon-stone", new ScanAnalysis
                {
                    ItemTypeId = "canyon-stone",
                    CommonName = "Demonstration Canyon Stone",
                    ScientificClassification = "Metamorphic Quartzite Monolith",
                    ChemicalComposition = "SiO2 95%, Fe2O3 3%",
                    PhysicalProperties = "Mass: 2.5 kg | Volume: 0.015 m3",
                    PracticalUtility = "Standard physical reference item.",
                    FlammabilityRating = 0.0f,
                    ThermalMassRating = 0.80f,
                    FuelEnergyRating = 0.0f,
                    HolographicReadout = "SCAN: CANYON STONE\n" +
                                         "• Status: Solid quartzite mineral\n" +
                                         "• Non-combustible mineral specimen."
                }
            }
        };

        public static bool TryGetAnalysis(string itemTypeId, out ScanAnalysis analysis)
        {
            if (string.IsNullOrEmpty(itemTypeId))
            {
                analysis = null;
                return false;
            }
            return KnowledgeBase.TryGetValue(itemTypeId, out analysis);
        }

        public ScanAnalysis PerformScan(string itemTypeId)
        {
            if (!TryGetAnalysis(itemTypeId, out var analysis))
            {
                analysis = new ScanAnalysis
                {
                    ItemTypeId = itemTypeId,
                    CommonName = "Uncatalogued Specimen",
                    ScientificClassification = "Unknown Terrestrial Matter",
                    ChemicalComposition = "Undetermined",
                    PhysicalProperties = "Standard Matter",
                    PracticalUtility = "Requires further physical observation.",
                    FlammabilityRating = 0.1f,
                    ThermalMassRating = 0.1f,
                    FuelEnergyRating = 0.1f,
                    HolographicReadout = $"SCAN: UNKNOWN [{itemTypeId}]\n• Empirical properties uncatalogued."
                };
            }

            LastScan = analysis;
            TotalScansCompleted++;
            CurrentStatus = $"Scan complete: {analysis.CommonName}";

            if (ScanLight != null)
            {
                ScanLight.color = new Color(0.2f, 0.85f, 1.0f);
                ScanLight.intensity = 3.5f;
            }

            if (Hud != null)
            {
                Hud.LivingMemoryText = "ALIEN ARTIFACT TERMINAL\n" + analysis.HolographicReadout;
            }

            if (Brain != null && Brain.Log != null)
            {
                Brain.Log.Record(Brain.Tick, "Scan", analysis.ItemTypeId, analysis.CommonName, "ScanResource", analysis.PracticalUtility);
            }

            Debug.Log($"[AlienArtifactScanner] {analysis.HolographicReadout.Replace('\n', ' ')}");
            return analysis;
        }

        public void ResetPad()
        {
            if (ScanLight != null) ScanLight.intensity = 0.5f;
            CurrentStatus = "Terminal ready: waiting for resource placement.";
        }
    }
}
