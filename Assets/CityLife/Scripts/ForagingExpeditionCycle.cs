using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;
using Starfall.Refuge;

namespace CityLife.World
{
    /// <summary>
    /// Foraging Expedition Cycle: Orchestrates continuous resource-gathering excursions where the inhabitant
    /// equips a woven basket, navigates across the canyon terrain, gathers tinder/cobbles/wood,
    /// knaps flint tools at the anvil, tests physical properties at the alien scanner terminal,
    /// and constructs dry-stone masonry structures (hearth, windbreak, cairn) on the terrace.
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
        public AlienArtifactScanner Scanner;

        [Header("Expedition State")]
        public ExpeditionPhase Phase = ExpeditionPhase.Idle;
        public int ExpeditionsCompleted = 0;
        public int TotalItemsGathered = 0;
        public string TargetResourceId = "";
        public Vector3 ForagingWaypoint;

        // Navigation and autonomous stepping state
        private Queue<Vector3> currentRoute;
        private Vector3 targetDestination;
        private float gestureTimer = 0f;
        private float stallTimer = 0f;
        private int stallCount = 0;
        private Vector3 lastPosition;
        private int lastLoggedTick = -1;
        private float fallbackTimer = 0f;

        private void Start()
        {
            ResolveSceneBindings();
        }

        private void Update()
        {
            // If running without NpcAutonomy driving FixedUpdate, advance via timer fallback
            if (Brain == null || !Brain.Ready)
            {
                fallbackTimer += Time.deltaTime;
                if (fallbackTimer >= 8.0f)
                {
                    AdvanceExpedition();
                    fallbackTimer = 0f;
                }
            }
        }

        public void ResetExpedition()
        {
            Phase = ExpeditionPhase.Idle;
            currentRoute = null;
            gestureTimer = 0f;
            stallTimer = 0f;
            stallCount = 0;
            targetDestination = Vector3.zero;
        }

        /// <summary>
        /// Lazy-resolves workstation, scanner, and refuge references in the scene.
        /// </summary>
        public void ResolveSceneBindings()
        {
            if (Brain == null) Brain = GetComponent<NpcAutonomy>();
            if (KnappingWorkstation == null) KnappingWorkstation = FindAnyObjectByType<StoneKnappingWorkstation>();
            if (BuildingWorkstation == null) BuildingWorkstation = FindAnyObjectByType<StoneBuildingWorkstation>();
            if (Scanner == null) Scanner = FindAnyObjectByType<AlienArtifactScanner>();
            if (Refuge == null) Refuge = FindAnyObjectByType<RefugeRuntime>();
            if (Hud == null && Camera.main != null) Hud = Camera.main.GetComponent<NpcDecisionHud>();
            if (Bootstrap == null && Brain != null) Bootstrap = Brain.PhysicalItems;
        }

        /// <summary>
        /// Main autonomous living step called every tick by NpcAutonomy when standard item goals are complete.
        /// Drives the inhabitant continuously through foraging, knapping, building, and canyon exploration.
        /// </summary>
        public bool StepAutonomousLiving(NpcAutonomy brain, CharacterPreviewActor actor, float dt)
        {
            if (brain == null || actor == null) return false;
            Brain = brain;
            ResolveSceneBindings();

            // 1. Gesture in progress (gathering, knapping, placing stone, scanning, or warming at hearth)
            if (gestureTimer > 0f)
            {
                gestureTimer -= dt;
                actor.Step(Vector3.zero, dt);
                if (gestureTimer <= 0f)
                {
                    OnGestureFinished(actor);
                }
                return true;
            }

            // 2. If idle or no route, plan next leg of the expedition
            if (Phase == ExpeditionPhase.Idle || currentRoute == null || currentRoute.Count == 0)
            {
                if (Phase == ExpeditionPhase.Idle)
                {
                    AdvanceExpedition();
                }
                PlanLegForCurrentPhase();
            }

            // Check direct proximity to target destination
            float distToTarget = FlatDistance(transform.position, targetDestination);

            // 3. Step along route waypoints
            if (currentRoute != null && currentRoute.Count > 0)
            {
                while (currentRoute.Count > 0 && FlatDistance(transform.position, currentRoute.Peek()) < 0.45f)
                {
                    currentRoute.Dequeue();
                }

                if (currentRoute.Count == 0 || distToTarget < 1.35f)
                {
                    currentRoute?.Clear();
                    // Reached destination! Turn to face, play interact gesture
                    Vector3 diff = targetDestination - transform.position;
                    diff.y = 0;
                    if (diff.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.LookRotation(diff);

                    actor.Gesture();
                    gestureTimer = 1.8f;
                    stallTimer = 0f;
                    stallCount = 0;
                    OnArrivedAtDestination();
                    actor.Step(Vector3.zero, dt);
                    return true;
                }

                // Steer toward current waypoint
                Vector3 waypoint = currentRoute.Peek();
                Vector3 difference = waypoint - transform.position;
                difference.y = 0;
                Vector3 direction = difference.sqrMagnitude > 0.001f ? difference.normalized : Vector3.zero;
                actor.Step(direction, dt);

                // Anti-stall check
                float moved = FlatDistance(transform.position, lastPosition);
                if (moved < 0.0005f)
                {
                    stallTimer += dt;
                    if (stallTimer > 1.8f)
                    {
                        stallTimer = 0f;
                        stallCount++;
                        if (stallCount >= 2 || distToTarget < 2.5f)
                        {
                            // Stalled or close enough: force arrival and advance
                            currentRoute.Clear();
                            Vector3 diff = targetDestination - transform.position;
                            diff.y = 0;
                            if (diff.sqrMagnitude > 0.01f)
                                transform.rotation = Quaternion.LookRotation(diff);

                            actor.Gesture();
                            gestureTimer = 1.8f;
                            stallCount = 0;
                            OnArrivedAtDestination();
                            actor.Step(Vector3.zero, dt);
                            return true;
                        }
                        else
                        {
                            currentRoute = PlanRouteTo(targetDestination);
                        }
                    }
                }
                else
                {
                    stallTimer = 0f;
                    stallCount = 0;
                }
                lastPosition = transform.position;
                return true;
            }

            actor.Step(Vector3.zero, dt);
            return true;
        }

        private void PlanLegForCurrentPhase()
        {
            switch (Phase)
            {
                case ExpeditionPhase.AcquireBasket:
                    targetDestination = GetBasketPosition();
                    currentRoute = PlanRouteTo(targetDestination);
                    SetHudStatus($"EXPEDITION: Moving to prepare basket for {TargetResourceId}...");
                    break;

                case ExpeditionPhase.ScoutResources:
                    targetDestination = GetResourceSupplyPosition(TargetResourceId);
                    currentRoute = PlanRouteTo(targetDestination);
                    SetHudStatus($"EXPEDITION: Heading to terrace resource site for {TargetResourceId}...");
                    break;

                case ExpeditionPhase.GatherItem:
                case ExpeditionPhase.HaulToRefuge:
                    targetDestination = GetCurrentHaulDestination();
                    currentRoute = PlanRouteTo(targetDestination);
                    SetHudStatus($"EXPEDITION: Hauling {TargetResourceId} to destination...");
                    break;

                case ExpeditionPhase.StoreAndRest:
                    targetDestination = GetRestPosition();
                    currentRoute = PlanRouteTo(targetDestination);
                    SetHudStatus($"LIVING WORLD: Completed expedition {ExpeditionsCompleted}. Moving to canyon terrace overlook...");
                    break;
            }
        }

        private void OnArrivedAtDestination()
        {
            switch (Phase)
            {
                case ExpeditionPhase.AcquireBasket:
                    SetHudStatus($"EXPEDITION: Securing woven basket container for {TargetResourceId}...");
                    RecordLog("acquire-basket", TargetResourceId, "basket secured");
                    break;

                case ExpeditionPhase.ScoutResources:
                    SetHudStatus($"EXPEDITION: Gathering {TargetResourceId} from terrace...");
                    RecordLog("expedition-gather", TargetResourceId, "harvesting resource");
                    break;

                case ExpeditionPhase.HaulToRefuge:
                    if (TargetResourceId.StartsWith("tool-") && KnappingWorkstation != null)
                        SetHudStatus("CRAFTING: Striking stones on anvil to knap tools...");
                    else if (BuildingWorkstation != null && !BuildingWorkstation.IsCompleted && TargetResourceId.Contains("stone"))
                        SetHudStatus($"MASONRY: Fitting stone into {BuildingWorkstation.CurrentTarget}...");
                    else if (Scanner != null && (ExpeditionsCompleted % 4 == 2))
                        SetHudStatus($"SCANNING: Placing sample on extraterrestrial scanner pad...");
                    else
                        SetHudStatus("REFUGE: Adding fuel wood to shelter hearth...");
                    break;

                case ExpeditionPhase.StoreAndRest:
                    SetHudStatus($"LIVING WORLD: Inhabitant observing canyon and cosmic sky. Expeditions: {ExpeditionsCompleted}.");
                    break;
            }
        }

        private void OnGestureFinished(CharacterPreviewActor actor)
        {
            switch (Phase)
            {
                case ExpeditionPhase.AcquireBasket:
                    Phase = ExpeditionPhase.ScoutResources;
                    PlanLegForCurrentPhase();
                    break;

                case ExpeditionPhase.ScoutResources:
                    Phase = ExpeditionPhase.HaulToRefuge;
                    TotalItemsGathered++;
                    PlanLegForCurrentPhase();
                    break;

                case ExpeditionPhase.HaulToRefuge:
                    // Execute action at destination
                    if (TargetResourceId.StartsWith("tool-") && KnappingWorkstation != null)
                    {
                        KnappingWorkstation.PerformKnap(out string toolId);
                        SetHudStatus($"CRAFTED: Knapped {toolId} on anvil!");
                        RecordLog("knap-complete", toolId, "knapped tool on anvil");
                    }
                    else if (Scanner != null && (ExpeditionsCompleted % 4 == 2))
                    {
                        var scan = Scanner.PerformScan(TargetResourceId);
                        SetHudStatus($"SCAN COMPLETE: {scan?.CommonName ?? TargetResourceId} analyzed on alien terminal.");
                        RecordLog("alien-scan", TargetResourceId, "completed scanner analysis");
                    }
                    else if (BuildingWorkstation != null && !BuildingWorkstation.IsCompleted && TargetResourceId.Contains("stone"))
                    {
                        bool dep = BuildingWorkstation.DepositStone($"foraged-stone-{TotalItemsGathered:D3}", TargetResourceId, out bool completed);
                        if (completed)
                        {
                            SetHudStatus($"MASONRY COMPLETE: Finished constructing {BuildingWorkstation.CurrentTarget}!");
                            RecordLog("masonry-complete", BuildingWorkstation.CurrentTarget.ToString(), "structure finished");

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
                        SetHudStatus($"REFUGE COMPLETE: Added fuel branch to hearth. Fire warming refuge.");
                        RecordLog("hearth-fuel", TargetResourceId, "added fuel log to hearth");
                    }
                    else
                    {
                        SetHudStatus($"EXPEDITION COMPLETE: Stored {TargetResourceId}. Total expeditions: {ExpeditionsCompleted + 1}.");
                    }

                    ExpeditionsCompleted++;
                    Phase = ExpeditionPhase.StoreAndRest;
                    PlanLegForCurrentPhase();
                    break;

                case ExpeditionPhase.StoreAndRest:
                    Phase = ExpeditionPhase.Idle;
                    currentRoute = null;
                    break;
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
                    else if (Scanner != null && (ExpeditionsCompleted % 4 == 2))
                    {
                        TargetResourceId = (ExpeditionsCompleted % 2 == 0) ? "stone-river-cobble" : "fire-tinder-bundle";
                        SetHudStatus($"EXPEDITION: Bringing {TargetResourceId} to alien terminal for physical analysis...");
                        RecordLog("scan-start", TargetResourceId, "prepare sample for scanning");
                    }
                    else
                    {
                        TargetResourceId = (ExpeditionsCompleted % 3 == 0) ? "fire-tinder-bundle" : (ExpeditionsCompleted % 3 == 1) ? "wood-fallen-branch" : "stone-river-cobble";
                        SetHudStatus($"EXPEDITION: Securing basket for {TargetResourceId} foraging...");
                        RecordLog("expedition-start", TargetResourceId, "prepare basket container");
                    }
                    break;

                case ExpeditionPhase.AcquireBasket:
                    Phase = ExpeditionPhase.ScoutResources;
                    ForagingWaypoint = GetResourceSupplyPosition(TargetResourceId);
                    SetHudStatus($"EXPEDITION: Scouting terrain toward {TargetResourceId}...");
                    RecordLog("expedition-scout", TargetResourceId, $"scouting terrain toward {ForagingWaypoint}");
                    break;

                case ExpeditionPhase.ScoutResources:
                    Phase = ExpeditionPhase.GatherItem;
                    TotalItemsGathered++;
                    SetHudStatus($"EXPEDITION: Gathered {TargetResourceId}!");
                    RecordLog("expedition-gather", TargetResourceId, $"stowed item in basket");
                    break;

                case ExpeditionPhase.GatherItem:
                    Phase = ExpeditionPhase.HaulToRefuge;
                    SetHudStatus($"EXPEDITION: Hauling basket back to site.");
                    RecordLog("expedition-haul", TargetResourceId, $"returning to site");
                    break;

                case ExpeditionPhase.HaulToRefuge:
                    Phase = ExpeditionPhase.StoreAndRest;
                    ExpeditionsCompleted++;
                    SetHudStatus($"EXPEDITION COMPLETE: Stored {TargetResourceId}. Total expeditions: {ExpeditionsCompleted}.");
                    RecordLog("expedition-complete", TargetResourceId, $"cycle complete (expeditions: {ExpeditionsCompleted})");
                    break;

                case ExpeditionPhase.StoreAndRest:
                    Phase = ExpeditionPhase.Idle;
                    break;
            }
        }

        private Vector3 GetBasketPosition()
        {
            Vector3 pos = (Refuge != null) ? Refuge.Hearth + new Vector3(1.2f, 0, 0.8f) : new Vector3(CoastalTerrain.RefugeCentre.x - 0.8f, 0, CoastalTerrain.RefugeCentre.y - 0.6f);
            pos.y = GroundHeight(pos);
            return pos;
        }

        private Vector3 GetResourceSupplyPosition(string resourceId)
        {
            Vector3 pos;
            if (resourceId.Contains("cobble"))
            {
                // Cobbles on dry canyon shelf near refuge
                pos = new Vector3(-158f, 0, 122f);
            }
            else if (resourceId.Contains("driftwood") || (resourceId.Contains("wood") && (ExpeditionsCompleted % 3 == 1)))
            {
                // Driftwood along the riverbank
                pos = new Vector3(22.0f, 0, -65.0f);
            }
            else if (resourceId.Contains("wood"))
            {
                // Fallen wood along refuge shelf
                pos = new Vector3(-148f, 0, 105f);
            }
            else if (resourceId.Contains("fieldstone"))
            {
                pos = new Vector3(-170f, 0, 110f);
            }
            else if (resourceId.Contains("slab"))
            {
                pos = new Vector3(-138f, 0, 132f);
            }
            else if (resourceId.Contains("tinder"))
            {
                pos = (Refuge != null) ? Refuge.Hearth + new Vector3(0.6f, 0, 0.4f) : new Vector3(CoastalTerrain.RefugeCentre.x + 0.6f, 0, CoastalTerrain.RefugeCentre.y + 0.4f);
            }
            else
            {
                pos = new Vector3(-152f, 0, 110f);
            }

            pos.y = GroundHeight(pos);
            if (pos.y < CoastalWater.Level + 0.2f)
                pos.y = CoastalWater.Level + 0.2f;
            return pos;
        }

        private Vector3 GetCurrentHaulDestination()
        {
            if (TargetResourceId.Contains("wood") && (ExpeditionsCompleted % 3 == 1))
            {
                // Haul river driftwood directly up to the Refuge Cave hearth
                Vector3 caveHearth = new Vector3(CoastalTerrain.RefugeCentre.x + 2.5f, 0, CoastalTerrain.RefugeCentre.y - 1.0f);
                caveHearth.y = GroundHeight(caveHearth);
                return caveHearth;
            }

            if (TargetResourceId.StartsWith("tool-") && KnappingWorkstation != null)
            {
                return KnappingWorkstation.AnvilPoint != null ? KnappingWorkstation.AnvilPoint.position : KnappingWorkstation.transform.position;
            }

            if (Scanner != null && (ExpeditionsCompleted % 4 == 2))
            {
                return Scanner.ScanningPad != null ? Scanner.ScanningPad.position : Scanner.transform.position;
            }

            if (BuildingWorkstation != null && !BuildingWorkstation.IsCompleted && TargetResourceId.Contains("stone"))
            {
                return BuildingWorkstation.ConstructionSite != Vector3.zero ? BuildingWorkstation.ConstructionSite : BuildingWorkstation.transform.position;
            }

            if (BuildingWorkstation != null)
            {
                return BuildingWorkstation.ConstructionSite != Vector3.zero ? BuildingWorkstation.ConstructionSite : BuildingWorkstation.transform.position;
            }

            Vector3 refugeHearth = (Refuge != null) ? Refuge.Hearth : new Vector3(CoastalTerrain.RefugeCentre.x, 0, CoastalTerrain.RefugeCentre.y);
            refugeHearth.y = GroundHeight(refugeHearth);
            return refugeHearth;
        }

        private Vector3 GetRestPosition()
        {
            Vector3 pos = (Refuge != null) ? Refuge.Hearth + new Vector3(2.5f, 0, -0.5f) : new Vector3(CoastalTerrain.RefugeCentre.x + 2.5f, 0, CoastalTerrain.RefugeCentre.y - 0.5f);
            pos.y = GroundHeight(pos);
            return pos;
        }

        private float GroundHeight(Vector3 pos)
        {
            if (Brain != null && Brain.TerrainNavigation != null && Brain.TerrainNavigation.TryGround(pos, out float h, out _))
                return h;
            return CoastalTerrain.Height(pos.x, pos.z);
        }

        private Queue<Vector3> PlanRouteTo(Vector3 dest)
        {
            if (Brain != null && Brain.TerrainNavigation != null)
            {
                var route = Brain.TerrainNavigation.Plan(transform.position, dest);
                if (route != null && route.Count > 0)
                    return route;
            }

            // Fallback safe waypoints along walkable terrain: never route into deep water!
            var direct = new Queue<Vector3>();
            Vector3 origin = transform.position;
            int segments = Mathf.Max(1, Mathf.RoundToInt(FlatDistance(origin, dest) / 1.5f));
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 pt = Vector3.Lerp(origin, dest, t);
                pt.y = GroundHeight(pt);
                if (pt.y < CoastalWater.Level + 0.1f)
                    break;
                direct.Enqueue(pt);
            }
            return direct;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0;
            return Vector3.Distance(a, b);
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
