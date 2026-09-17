using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;
using Starfall.Refuge;

namespace CityLife.World
{
    /// <summary>
    /// Foraging Expedition Cycle: Orchestrates resource-gathering excursions where the inhabitant
    /// equips a woven basket, navigates across the dunes/coast, gathers tinder/cobbles/wood,
    /// and hauls them back to the refuge depot.
    /// </summary>
    public sealed class ForagingExpeditionCycle : MonoBehaviour
    {
        public enum ExpeditionPhase
        {
            Idle,
            AcquireBasket,
            ScoutResources,
            GatherItem,
            HaulToRefuge,
            StoreAndRest
        }

        [Header("System Bindings")]
        public NpcAutonomy Brain;
        public RefugeRuntime Refuge;
        public PhysicalItemBootstrap Bootstrap;
        public NpcDecisionHud Hud;
        public StoneBuildingWorkstation BuildingWorkstation;
        public StoneKnappingWorkstation KnappingWorkstation;

        [Header("Expedition State")]
        public ExpeditionPhase Phase = ExpeditionPhase.Idle;
        public int ExpeditionsCompleted = 0;
        public int TotalItemsGathered = 0;
        public string TargetResourceId = "";
        public Vector3 ForagingWaypoint;

        private float phaseTimer = 0f;
        private int lastLoggedTick = -1;

        private void Update()
        {
            if (Brain == null || !Brain.Running || Brain.MenuPaused)
                return;

            phaseTimer += Time.deltaTime;
            if (phaseTimer >= 8.0f)
            {
                AdvanceExpedition();
                phaseTimer = 0f;
            }
        }

        /// <summary>
        /// Advances the inhabitant through the stages of a foraging expedition.
        /// </summary>
        public void AdvanceExpedition()
        {
            switch (Phase)
            {
                case ExpeditionPhase.Idle:
                    // Determine next priority: tool knapping -> stone masonry -> survival supplies
                    Phase = ExpeditionPhase.AcquireBasket;
                    if (KnappingWorkstation != null && KnappingWorkstation.TotalToolsCrafted < 2)
                    {
                        TargetResourceId = (KnappingWorkstation.TotalToolsCrafted == 0) ? "tool-stone-blade" : "tool-fire-striker";
                        SetHudStatus($"EXPEDITION: Visiting knapping anvil to craft {TargetResourceId}...");
                        RecordLog("crafting-start", TargetResourceId, "prepare knapping stone");
                    }
                    else if (BuildingWorkstation != null && !BuildingWorkstation.IsCompleted)
                    {
                        TargetResourceId = (ExpeditionsCompleted % 3 == 0) ? "stone-river-cobble" : (ExpeditionsCompleted % 3 == 1) ? "stone-fieldstone" : "stone-flat-slab";
                        SetHudStatus($"EXPEDITION: Gathering {TargetResourceId} for {BuildingWorkstation.CurrentTarget} construction...");
                        RecordLog("masonry-start", TargetResourceId, $"gather stone for {BuildingWorkstation.CurrentTarget}");
                    }
                    else
                    {
                        TargetResourceId = (ExpeditionsCompleted % 3 == 0) ? "fire-tinder-bundle" : (ExpeditionsCompleted % 3 == 1) ? "wood-fallen-branch" : "stone-river-cobble";
                        SetHudStatus($"EXPEDITION: Securing basket for {TargetResourceId} foraging...");
                        RecordLog("expedition-start", TargetResourceId, "prepare basket container");
                    }
                    break;

                case ExpeditionPhase.AcquireBasket:
                    // Basket equipped / secured; scout foraging territory
                    Phase = ExpeditionPhase.ScoutResources;
                    ForagingWaypoint = transform.position + new Vector3(UnityEngine.Random.Range(-15f, 15f), 0, UnityEngine.Random.Range(10f, 25f));
                    SetHudStatus($"EXPEDITION: Scouting terrain at ({ForagingWaypoint.x:F1}, {ForagingWaypoint.z:F1})...");
                    RecordLog("expedition-scout", TargetResourceId, $"scouting terrain toward {ForagingWaypoint}");
                    break;

                case ExpeditionPhase.ScoutResources:
                    // Arrived at site; perform gathering or knapping
                    Phase = ExpeditionPhase.GatherItem;
                    TotalItemsGathered++;
                    if (TargetResourceId.StartsWith("tool-") && KnappingWorkstation != null)
                    {
                        KnappingWorkstation.PerformKnap(out string craftedId);
                        SetHudStatus($"EXPEDITION: Knapped {craftedId}! Equipping tool.");
                        RecordLog("knap-complete", craftedId, $"crafted tool on anvil (total gathered: {TotalItemsGathered})");
                    }
                    else
                    {
                        SetHudStatus($"EXPEDITION: Gathered {TargetResourceId}! Stowing into basket.");
                        RecordLog("expedition-gather", TargetResourceId, $"stowed item in basket (total gathered: {TotalItemsGathered})");
                    }
                    break;

                case ExpeditionPhase.GatherItem:
                    // Haul loaded basket back to refuge or building site
                    Phase = ExpeditionPhase.HaulToRefuge;
                    Vector3 dest = (BuildingWorkstation != null && !BuildingWorkstation.IsCompleted) ? BuildingWorkstation.ConstructionSite : (Refuge != null ? Refuge.Hearth : Vector3.zero);
                    SetHudStatus($"EXPEDITION: Hauling basket back to site.");
                    RecordLog("expedition-haul", TargetResourceId, $"returning to site at {dest}");
                    break;

                case ExpeditionPhase.HaulToRefuge:
                    // Unload items at building site or storage plinth
                    Phase = ExpeditionPhase.StoreAndRest;
                    ExpeditionsCompleted++;

                    if (BuildingWorkstation != null && !BuildingWorkstation.IsCompleted && TargetResourceId.Contains("stone"))
                    {
                        bool deposited = BuildingWorkstation.DepositStone($"foraged-stone-{TotalItemsGathered:D3}", TargetResourceId, out bool structureDone);
                        if (structureDone)
                        {
                            SetHudStatus($"MASONRY COMPLETE: Built {BuildingWorkstation.CurrentTarget}!");
                            RecordLog("masonry-complete", BuildingWorkstation.CurrentTarget.ToString(), "structure finished");

                            // Advance to next structure archetype
                            if (BuildingWorkstation.CurrentTarget == StoneStructureKind.HearthRing)
                                BuildingWorkstation.UpdateRequirementsForTarget(StoneStructureKind.WindbreakWall);
                            else if (BuildingWorkstation.CurrentTarget == StoneStructureKind.WindbreakWall)
                                BuildingWorkstation.UpdateRequirementsForTarget(StoneStructureKind.StorageCairn);
                        }
                        else
                        {
                            SetHudStatus($"MASONRY: Placed stone {BuildingWorkstation.DepositedStonesCount}/{BuildingWorkstation.RequiredStones} for {BuildingWorkstation.CurrentTarget}.");
                        }
                    }
                    else if (Refuge != null && TargetResourceId == "wood-fallen-branch")
                    {
                        Refuge.Fire.AddLog();
                        SetHudStatus($"EXPEDITION COMPLETE: Added fuel log to hearth. Total expeditions: {ExpeditionsCompleted}.");
                    }
                    else
                    {
                        SetHudStatus($"EXPEDITION COMPLETE: Stored {TargetResourceId}. Total expeditions: {ExpeditionsCompleted}.");
                    }

                    RecordLog("expedition-complete", TargetResourceId, $"cycle complete (expeditions: {ExpeditionsCompleted})");
                    break;

                case ExpeditionPhase.StoreAndRest:
                    // Rest briefly before next excursion
                    Phase = ExpeditionPhase.Idle;
                    break;
            }
        }

        private void SetHudStatus(string text)
        {
            if (Hud != null)
                Hud.LivingMemoryText = text;
        }

        private void RecordLog(string action, string target, string result)
        {
            if (Brain != null && Brain.Log != null && Brain.Tick != lastLoggedTick)
            {
                lastLoggedTick = Brain.Tick;
                Brain.Log.Record(
                    Brain.Tick,
                    "foraging",
                    $"phase={Phase}",
                    target,
                    action,
                    result,
                    "foraging expedition progress"
                );
            }
        }

        /// <summary>
        /// Verification entry point for test suites.
        /// </summary>
        public static bool VerifyForagingLogic(out string receipt)
        {
            var go = new GameObject("TestForaging");
            var exp = go.AddComponent<ForagingExpeditionCycle>();
            var initialPhase = exp.Phase;
            exp.AdvanceExpedition();
            var nextPhase = exp.Phase;
            UnityEngine.Object.DestroyImmediate(go);

            receipt = $"initialPhase={initialPhase}, nextPhase={nextPhase}";
            return initialPhase == ExpeditionPhase.Idle && nextPhase == ExpeditionPhase.AcquireBasket;
        }
    }
}
