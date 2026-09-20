using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Starfall.Food;
using CityLife.Food;
using CityLife.Items;

namespace CityLife.World
{
    // Optional normal-play survival authority after the finite object task.
    // It never receives undiscovered resource coordinates from the authored scene.
    public sealed class StarfallSurvivalAutonomy : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public IntegratedFoodRuntime Food;
        public Starfall.Refuge.RefugeRuntime Refuge;
        public StarfallMapHud MapHud;
        public bool Enabled { get; private set; }
        public string Status { get; private set; }="Survival mind off";
        public string LastChoice { get; private set; }="waiting for discovery";
        public bool LastChoiceByModel { get; private set; }
        public string LastOutcome { get; private set; }="no survival outcome yet";
        public int AcceptedDecisions { get; private set; }
        public bool VerifiedScopedContinuation { get; private set; }
        public int FoodOutcomes { get; private set; }
        public int ExploredMetres { get; private set; }
        public string Plan { get; private set; } = "";
        public string Dialogue { get; private set; } = "";
        public string Reflection { get; private set; } = "";
        public string LastSafeGround { get; private set; }="";
        public float LastSafeGroundY { get; private set; }
        private readonly Dictionary<string, int> cherishedAffinities = new Dictionary<string, int>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, int> CherishedPlaceAffinities => cherishedAffinities;
        public void BoostPlaceAffinity(string placeKey, int amount)
        {
            if (string.IsNullOrEmpty(placeKey)) return;
            cherishedAffinities.TryGetValue(placeKey, out int current);
            cherishedAffinities[placeKey] = Mathf.Clamp(current + amount, 0, 100);
        }

        public Vector3? PlayerDirectiveTarget { get; private set; }
        public string PlayerDirectiveLabel { get; private set; }
        public void SetPlayerDirective(Vector3 worldPos, string label = "Player Beacon")
        {
            PlayerDirectiveTarget = worldPos;
            PlayerDirectiveLabel = label;
            if (Brain != null && Brain.Log != null)
                Brain.Log.Record(Brain.Tick, "DIRECTIVE", Brain.DescribePerception(), "player directive", "Set guidance waypoint", $"{label} at {worldPos}");
        }
        public void ClearPlayerDirective()
        {
            PlayerDirectiveTarget = null;
            PlayerDirectiveLabel = null;
        }

        public bool IsWellFulfilled()
        {
            var s = Food != null && Food.Model != null ? Food.Model.State : null;
            if (s == null) return false;
            return s.satiety >= 7500 && s.hydration >= 7000 && s.body != null && s.body.protein >= 6000 && s.body.health >= 8000;
        }

        private StarfallSurvivalDiary diary;
        public StarfallSurvivalDiary Diary
        {
            get
            {
                if (diary == null && Brain != null)
                    diary = Brain.GetComponent<StarfallSurvivalDiary>() ?? Brain.GetComponentInChildren<StarfallSurvivalDiary>();
                if (diary == null)
                    diary = FindFirstObjectByType<StarfallSurvivalDiary>();
                if (diary == null && Brain != null)
                    diary = Brain.gameObject.AddComponent<StarfallSurvivalDiary>();
                return diary;
            }
        }
        private string endpoint,model,evidenceDirectory,savePath;
        private Task<StarfallSurvivalThought.Result> pending;
        private CancellationTokenSource cancellation;
        private readonly Queue<Vector3> route=new Queue<Vector3>();
        private readonly HashSet<string> visiblePlaceIds=new HashSet<string>(StringComparer.Ordinal);
        private Vector2Int? lastOccupiedCell;
        private List<string> offered;
        private int request, modelRequestSequence, nextRequestTick, routeStartTick, deathAtTick=-1, explorationSeed, lastCheckpointSecond, blockedTicks;
        private int huntingPursuitFailures, huntingHopeLostUntilTick;
        public bool LocalModelEnabled { get; set; } = true;
        public string routePurpose;
        private string recentVerifiedOutcome;
        private Vector3 routeOrigin;
        private bool mapAcceptanceRequested;
        private string mapAcceptanceDirectory;
        private enum MapDiagStage { Inactive, WaitingAuthority, Caching, Departing, DepartedExcluded, Returning, DonePass, DoneFail }
        private MapDiagStage mapDiagStage=MapDiagStage.Inactive;
        private int mapDiagStartTick, mapStageStartTick, initialFoodTick, initialPlacesCount;
        private string initialLastHash="";
        private Vector3 cachedBerryPos, cachedBerryApproach, mapInitialPos, mapFinalPos, departureDest;
        private readonly List<string> mapStages=new List<string>();
        [Serializable] private sealed class MapSummary
        {
            public string status, scope="Actual map revisit diagnostic in compiled integrated player; NOT LLM model choice";
            public string world, generation, actor;
            public int startTick, endTick, startFoodTick, endFoodTick;
            public Vector3 initialPosition, finalPosition, cachedBerryPosition, cachedBerryApproach, departureDestination;
            public int initialEventCount, finalEventCount;
            public string initialLastHash, revisitHash, revisitPreviousHash, revisitKind;
            public bool hashChainValid, genuineRevisitVerified;
            public List<string> routeStages=new List<string>();
        }
        [Serializable] private sealed class Row
        {
            public string world,actor,kind,code,choice,model,requestHash,responseHash,finishReason,deathCause,deathHash,offeredActions;
            public int tick,incarnation,foodDelta,waterDelta,inventoryDelta,request;
            public int foodTick,energy,hydration,stomach,carriedFruit,modelRequestSequence;
            public bool knowsBerry,knowsSpring,mealBenefitVerified;
            public bool deathLessonInRequest;
            public long modelMilliseconds;
            public float x,y,z;
        }
        private IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();
            int mapFlag=Array.IndexOf(args,"-starfallMapAcceptance");
            mapAcceptanceRequested=mapFlag>=0;
            bool explicitRuntime=Array.IndexOf(args,"-npcSurvivalRuntime")>=0;
            string Arg(string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
            endpoint=Arg("-npcLocalEndpoint");model=Arg("-npcSurvivalModel");
            evidenceDirectory=Arg("-npcSurvivalEvidence");savePath=Arg("-npcSurvivalSave");
            if(mapAcceptanceRequested)
            {
                mapAcceptanceDirectory=Arg("-starfallMapAcceptance");
                if(string.IsNullOrWhiteSpace(mapAcceptanceDirectory)||!Path.IsPathFullyQualified(mapAcceptanceDirectory)||
                    (Directory.Exists(mapAcceptanceDirectory)&&Directory.GetFileSystemEntries(mapAcceptanceDirectory).Length!=0))
                    throw new InvalidOperationException("starfall-map-acceptance-directory-invalid");
                Directory.CreateDirectory(mapAcceptanceDirectory);
                if(string.IsNullOrEmpty(evidenceDirectory))evidenceDirectory=mapAcceptanceDirectory;
                if(string.IsNullOrEmpty(savePath))savePath=Path.Combine(mapAcceptanceDirectory,"starfall-map-save.json");
            }
            else if(!explicitRuntime)
            {
                if(string.IsNullOrEmpty(endpoint)) endpoint="http://127.0.0.1:1234";
                if(string.IsNullOrEmpty(model)) model="local-model";
                if(string.IsNullOrEmpty(savePath)) savePath=Path.Combine(Application.persistentDataPath,"starfall-survival-save.json");
                if(string.IsNullOrEmpty(evidenceDirectory)) evidenceDirectory=Path.Combine(Application.persistentDataPath,"survival-evidence");
            }
            if(Brain==null||Food==null){Status="Survival mind unavailable: missing world adapter";yield break;}
            while(!Brain.Ready)yield return null;
            try
            {
                if(Brain==null||Food==null||Brain.InstanceWorldId!=Food.Model.State.world)
                    throw new InvalidOperationException("scoped-survival-configuration-incomplete");
                if(explicitRuntime && !mapAcceptanceRequested)
                {
                    if(string.IsNullOrWhiteSpace(model)||string.IsNullOrWhiteSpace(endpoint)||
                        string.IsNullOrEmpty(evidenceDirectory)||!Path.IsPathFullyQualified(evidenceDirectory)||
                        Directory.Exists(evidenceDirectory)&&Directory.GetFileSystemEntries(evidenceDirectory).Length!=0)
                        throw new InvalidOperationException("scoped-survival-configuration-incomplete");
                    StarfallLivingMemoryClient.Loopback(endpoint);
                }
                else if(!string.IsNullOrWhiteSpace(endpoint))
                {
                    try { StarfallLivingMemoryClient.Loopback(endpoint); } catch { }
                }
                if(savePath!=null && !Path.IsPathFullyQualified(savePath))throw new InvalidOperationException("save-path-not-absolute");
                if(!string.IsNullOrEmpty(evidenceDirectory)) Directory.CreateDirectory(evidenceDirectory);
                bool prior=savePath!=null&&File.Exists(savePath);
                if(prior&&!Food.Model.Load(savePath,Brain.InstanceWorldId,IntegratedFoodRuntime.Generation))
                    throw new InvalidOperationException("scoped-save-rejected");
                // Old food/body checkpoints, even ones containing a meal,
                // never authorize survival dispatch on a reconstructed world.
                // Only a marker minted after the live delivery prerequisite can.
                VerifiedScopedContinuation=prior&&FoodModel.HasEarnedSurvivalAuthority(Food.Model.State);
                if(!TryNextFoodRequest(Food.Model.State.lastRequest,out _))
                    throw new InvalidOperationException("scoped-food-request-sequence-exhausted");
                request=Food.Model.State.lastRequest;
                if(prior&&!Food.Model.State.body.dead)
                {
                    Vector3 remembered=Food.Model.State.actorPosition;
                    if(!Brain.TerrainNavigation.Walkable(remembered,out var floor))throw new InvalidOperationException("saved-actor-ground-rejected");
                    Brain.Actor.Place(floor+Vector3.up*.06f);
                }
                // A restored cell was already occupied in the scoped save;
                // don't inflate its visits until the actor actually leaves it.
                if(prior&&Food.Model.State.exploredCells!=null)
                {
                    var savedCell=new Vector2Int(Mathf.RoundToInt(Brain.transform.position.x/3f),
                        Mathf.RoundToInt(Brain.transform.position.z/3f));
                    if(PlaceLedger.Cell(Food.Model.State,savedCell.x,savedCell.y)!=null)
                        lastOccupiedCell=savedCell;
                }
                if(prior)
                {
                    // A process restart at the same saved position is not a
                    // physical revisit. Seed the transient visible set from
                    // live LOS now; only leaving and re-entering can later
                    // append an unchanged-place revisit event.
                    SeedVisibleForReload(visiblePlaceIds,Brain.Perception.Sense(Brain.Tick));
                }
                Enabled=true;Status="Survival mind ready; waiting for ordinary task authority";
                lastCheckpointSecond=Food.Model.State.tick;
                Record("startup",prior?(VerifiedScopedContinuation?"earned-survival-authority-reloaded":
                    "scoped-food-save-reloaded-without-authority"):"new-scoped-food-journey",null,null);
                if(MapHud==null)MapHud=GetComponent<StarfallMapHud>()??GetComponentInParent<StarfallMapHud>()??FindFirstObjectByType<StarfallMapHud>();
                if(MapHud!=null&&Food!=null)
                {
                    MapHud.Initialize(Food.Model,Brain!=null?Brain.InstanceWorldId:Food.Model.State.world,
                        IntegratedFoodRuntime.Generation,NpcAutonomy.AgentId,
                        ()=>Brain!=null?Brain.transform.position:Food.Model.State.actorPosition);
                    if(mapAcceptanceRequested)MapHud.Expanded=true;
                }
            }
            catch(Exception error){Enabled=false;Status="Survival mind unavailable: "+error.GetType().Name;}
        }
        private void Update()
        {
            if(pending!=null && (Brain==null||Brain.MenuPaused||Brain.Possessed||!Brain.Running||!Enabled))
                Cancel("control-interruption");
            // Physiology and exploratory position change even without a food
            // transaction. An immutable scoped snapshot every 30 simulated
            // seconds prevents a clean quit from resetting hunger/thirst to the
            // last berry inspection. No in-game time is accelerated.
            if(Enabled && Food!=null && Food.Model.State.tick-lastCheckpointSecond>=30)
            {
                Persist();
                if(Enabled)
                {
                    lastCheckpointSecond=Food.Model.State.tick;
                    Record("checkpoint","scoped-food-body-checkpoint-saved",null,null);
                }
            }
        }
        private void OnApplicationQuit(){if(Enabled&&Food!=null)Persist();}
        public void Cancel(string reason)
        {
            if(pending!=null){cancellation.Cancel();cancellation.Dispose();cancellation=null;pending=null;offered=null;Record("cancel",reason,null,null);}
            route.Clear();routePurpose=null;nextRequestTick=Brain==null?0:Brain.Tick+25;
        }
        private void OnDestroy()=>Cancel("destroyed");
        private void Record(string kind,string code,string choice,StarfallSurvivalThought.Result result,FoodReceipt receipt=null)
        {
            if(string.IsNullOrEmpty(evidenceDirectory)||!Directory.Exists(evidenceDirectory)||Food==null||Brain==null)return;
            var s=Food.Model.State; var row=new Row{world=s.world,actor=s.actorId,kind=kind,code=code,choice=choice,
                model=result==null?null:result.model,requestHash=result==null?null:result.RequestHash,
                responseHash=result==null||string.IsNullOrEmpty(result.responseJson)?null:result.ResponseHash,
                finishReason=result==null?null:result.finishReason,
                modelMilliseconds=result==null?0:result.milliseconds,
                offeredActions=offered==null?null:string.Join(",",offered),
                tick=Brain.Tick,incarnation=s.incarnation,request=receipt==null?0:receipt.request,
                modelRequestSequence=modelRequestSequence,
                foodTick=s.tick,energy=s.satiety,hydration=s.hydration,stomach=s.body.stomach,
                carriedFruit=s.carriedFruit,knowsBerry=s.knowsBerry,knowsSpring=s.knowsSpring,
                mealBenefitVerified=s.knowsMealBenefit&&!string.IsNullOrEmpty(s.lastMealEvidence),
                foodDelta=receipt==null?0:receipt.foodDelta,waterDelta=receipt==null?0:receipt.waterDelta,
                inventoryDelta=receipt==null?0:receipt.inventoryDelta,
                deathCause=s.body.dead?s.body.cause:null,deathHash=s.deaths.Count>0?s.deaths[s.deaths.Count-1].hash:null,
                deathLessonInRequest=kind=="model"&&code=="request-issued"&&
                    VerifiedOwnDeathCause(s)!=null&&result!=null&&
                    !string.IsNullOrEmpty(result.requestJson)&&
                    result.requestJson.Contains("Prior verified own death: "),
                x=Brain.transform.position.x,y=Brain.transform.position.y,z=Brain.transform.position.z};
            try{File.AppendAllText(Path.Combine(evidenceDirectory,"normal-survival.jsonl"),JsonUtility.ToJson(row)+"\n");}
            catch(Exception){Enabled=false;Status="Survival evidence failed closed";evidenceDirectory=null;Cancel("evidence-write-failed");}
        }
        private bool Observed(string id,out NpcObservation observation)
        {
            observation=Brain.Perception.Current.FirstOrDefault(x=>x.id==id&&x.kind==NpcObjectKind.Place&&
                x.permission&&x.available&&x.seenAtTick>=Brain.Tick-10);
            return observation!=null;
        }
        public static void SeedVisibleForReload(HashSet<string> visible,IEnumerable<NpcObservation> sensed)
        {
            if(visible==null||sensed==null)throw new ArgumentNullException();
            foreach(var seen in sensed)
                if(seen!=null&&seen.kind==NpcObjectKind.Place&&FoodModel.Id(seen.id))
                    visible.Add(seen.id);
        }
        public bool RememberCurrentWorld()
        {
            if (Brain == null || Food == null || Food.Model == null || Food.Model.State == null) return false;
            var s = Food.Model.State;
            Vector3 pos = Brain.transform.position;
            if (!float.IsFinite(pos.x) || !float.IsFinite(pos.z)) return false;

            var cell = new Vector2Int(Mathf.RoundToInt(pos.x / 3f), Mathf.RoundToInt(pos.z / 3f));
            if (!lastOccupiedCell.HasValue || lastOccupiedCell.Value != cell)
            {
                bool terrain = Brain.TerrainNavigation != null && Brain.TerrainNavigation.Walkable(pos, out var floor) &&
                    Mathf.Abs(floor.y - pos.y) < .95f;
                bool refugeFloor = Physics.Raycast(pos + Vector3.up * .6f, Vector3.down,
                    out var support, 2.0f, Starfall.Refuge.RefugeRuntime.GeometryMask,
                    QueryTriggerInteraction.Ignore) && support.collider.name == "Refuge floor" &&
                    Mathf.Abs(support.point.y - pos.y) < .95f;
                bool actorGrounded = Brain.Actor != null && (Brain.Actor.Grounded || Brain.Actor.IsWading || Brain.Actor.IsSwimming);
                bool tryGrounded = Brain.TerrainNavigation != null && Brain.TerrainNavigation.TryGround(pos, out float gh, out _) && Mathf.Abs(gh - pos.y) < 1.6f;

                if (terrain || refugeFloor || actorGrounded || tryGrounded)
                {
                    if (PlaceLedger.Occupy(s, cell.x, cell.y, s.tick))
                    {
                        lastOccupiedCell = cell;
                        Record("cell", "physically-occupied-" + cell.x + "-" + cell.y, null, null);
                    }
                }
            }
            var now = new HashSet<string>(StringComparer.Ordinal);
            if (Brain.Perception != null && Brain.Perception.Current != null)
            {
                foreach (var observation in Brain.Perception.Current)
                {
                    if (observation == null || observation.kind != NpcObjectKind.Place ||
                        observation.seenAtTick < Brain.Tick - 10 || !FoodModel.Id(observation.id)) continue;
                    now.Add(observation.id);
                    bool revisit = !visiblePlaceIds.Contains(observation.id);
                    int before = s.observedPlaces.Count;
                    if (!PlaceLedger.Observe(s, observation.id, observation.kind.ToString(), observation.observedType,
                        observation.position, observation.available,
                        observation.permission, s.tick, observation.seenAtTick, revisit)) continue;
                    if (s.observedPlaces.Count > before)
                        Record("place", s.observedPlaces[s.observedPlaces.Count - 1].kind + "-" + observation.id, null, null);
                }
            }
            visiblePlaceIds.Clear(); foreach (string id in now) visiblePlaceIds.Add(id);
            CheckEnvironmentalClues();
            MapHud?.NotifyStateChanged();
            return true;
        }

        private bool discoveredLogClue;
        private bool discoveredSedgeClue;
        private bool discoveredBoulderClue;

        private void CheckEnvironmentalClues()
        {
            if (Brain == null) return;
            Vector3 pos = Brain.transform.position;

            // 1. Hollow Log Clue on Terrace Fringe (-58, 68)
            if (!discoveredLogClue && Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(-58f, 68f)) <= 9.0f)
            {
                discoveredLogClue = true;
                Diary?.AddEntry(Brain.Tick, "Discovery", "Discovered a weathered hollow log on the terrace edge—its splintered interior cavity offers wind shelter from wolves, and dry kindling.");
                Brain.Log?.Record(Brain.Tick, "ENVIRONMENT_CLUE", Brain.DescribePerception(), "perceive landmark", "Inspect hollow log", "Noticed natural hollow log cavity framing line-of-sight to river");
            }

            // 2. Riparian Sedges Clue along Damp Waterline (-35, 70)
            if (!discoveredSedgeClue && Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(-35f, 70f)) <= 9.0f)
            {
                discoveredSedgeClue = true;
                Diary?.AddEntry(Brain.Tick, "Discovery", "Thick green riparian sedges clustered along the lower riverbank—a reliable indicator that clean fresh water flows right below the terrace.");
                Brain.Log?.Record(Brain.Tick, "ENVIRONMENT_CLUE", Brain.DescribePerception(), "perceive landmark", "Observe riparian sedges", "Sedge cluster confirms immediate proximity to fresh river water");
            }

            // 3. Weathered Sandstone Boulders along Refuge Descent (-118, 108)
            if (!discoveredBoulderClue && Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(-118f, 108f)) <= 9.0f)
            {
                discoveredBoulderClue = true;
                Diary?.AddEntry(Brain.Tick, "Discovery", "Standing beside the massive weathered sandstone boulder at the terrace descent—serves as an elevated waypoint framing the path toward the river.");
                Brain.Log?.Record(Brain.Tick, "ENVIRONMENT_CLUE", Brain.DescribePerception(), "perceive landmark", "Reach descent boulder", "High vantage point surveying the descent route to the river");
            }
        }

        public static bool BerryRelevant(FoodState s)
        {
            return s!=null&&s.fruitStock>0&&s.carriedFruit<4&&
                // One untested fruit is enough for the first experiment.
                // Do not repeatedly circle the plant/hoard unknown berries
                // before an actual eaten outcome establishes meal utility.
                (s.carriedFruit==0||s.knowsMealBenefit)&&
                (!s.knowsBerry||(s.body.stomach<=8800&&
                    (s.satiety<8500||s.hydration<8500&&s.knowsMealBenefit)));
        }
        public static bool SpringRelevant(FoodState s)=>s!=null&&
            (!s.knowsSpring||s.hydration<8500&&s.freshwaterMl>=250);
        public static string VerifiedOwnDeathCause(FoodState s)
        {
            if(s==null||s.body.dead||s.deaths==null||s.deaths.Count==0||
                !FoodModel.Valid(s,s.world,s.generation))return null;
            var latest=s.deaths[s.deaths.Count-1];
            if(latest.cause=="prolonged-dehydration"&&
                latest.lesson=="Hydration remained depleted before fatal damage.")return latest.cause;
            if(latest.cause=="prolonged-starvation"&&
                latest.lesson=="Energy and fat were exhausted before fatal damage.")return latest.cause;
            if(latest.cause=="drowning"&&
                latest.lesson=="Submerged underwater without air; drowned.")return latest.cause;
            return null;
        }
        private List<string> Eligible()
        {
            var foodChoices=new List<string>();var s=Food.Model.State;
            // 1. Freshwater spring seep
            if(SpringRelevant(s)&&Observed("spring-food",out _) &&
                Food.Spring!=null && Food.Spring.WorldId==s.world)
            {
                FoodAccess gate=Food.Inspect("spring");
                if(gate.visible&&gate.permitted)
                {
                    if(!gate.inReach)foodChoices.Add("approach spring");
                    else if(!s.knowsSpring)foodChoices.Add("inspect spring");
                    else if(s.hydration<8500&&s.freshwaterMl>=250)foodChoices.Add("drink spring");
                }
            }

            // 2. Freshwater river
            bool inRiver = CoastalTerrain.IsFreshwaterRiver(Brain.transform.position.x,Brain.transform.position.z,Brain.transform.position.y,CoastalWater.CurrentLevel);
            bool canDrinkRiver = CoastalTerrain.CanDrinkFromRiver(Brain.transform.position.x, Brain.transform.position.z, Brain.transform.position.y, CoastalWater.CurrentLevel);
            if(s.hydration<8500&&canDrinkRiver)
                foodChoices.Add("drink river");

            // 2b. Carried freshwater container
            if (s.hydration < 8500 && s.freshwaterMl >= 250)
                foodChoices.Add("drink water");

            // 3. Berry bushes (primary + all distributed bushes: berry-food, berry-food-2..8)
            NpcObservation nearestBush = null;
            float nearestBushDist = float.MaxValue;
            if (Brain.Perception != null && Brain.Perception.Current != null)
            {
                foreach (var obs in Brain.Perception.Current)
                {
                    if (obs != null && obs.kind == NpcObjectKind.Place && obs.id.StartsWith("berry-food", StringComparison.Ordinal) &&
                        obs.permission && obs.available && obs.seenAtTick >= Brain.Tick - 15)
                    {
                        float d = Vector3.Distance(Brain.transform.position, obs.position);
                        if (d < nearestBushDist)
                        {
                            nearestBushDist = d;
                            nearestBush = obs;
                        }
                    }
                }
            }

            var controls = Brain.GetComponent<NpcPlayerControls>() ?? FindFirstObjectByType<NpcPlayerControls>();
            int freeHands = controls != null ? controls.GetFreeHandCount() : 1;
            var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();
            bool canCarryMoreBerries = freeHands > 0 || (moonbag != null && moonbag.CanStore) || s.carriedFruit < 4;

            if (nearestBush != null)
            {
                FoodAccess gate = Food != null ? Food.Inspect(nearestBush.id) : default;
                float bushDist = nearestBushDist;
                bool inReach = (gate.visible && gate.permitted && gate.inReach) ||
                               (bushDist <= 2.2f && nearestBush.permission && nearestBush.available);
                if (!inReach)
                {
                    if (BerryRelevant(s) || s.satiety < 8500 || (moonbag != null && moonbag.CanStore) || canCarryMoreBerries)
                        foodChoices.Add("approach berry");
                }
                else if (!s.knowsBerry)
                {
                    foodChoices.Add("inspect berry");
                    if (s.satiety < 8500) foodChoices.Add("gather berry");
                }
                else if (canCarryMoreBerries || s.satiety < 8500)
                {
                    foodChoices.Add("gather berry");
                }
            }

            // 3b. Predator Danger & Club Defense (Coastal Wolves)
            var carry = Brain.GetComponentInChildren<HunterClubCarry>();
            bool hasClubInHand = carry != null && !carry.Stowed;
            bool hasClubOnBack = carry != null && carry.Stowed;
            bool leftOccupied = Brain.Actions != null && Brain.Actions.HeldLeft != null;

            var nearestWolf = Brain.Perception != null && Brain.Perception.Current != null
                ? Brain.Perception.Current.FirstOrDefault(x => x != null && x.id.Contains("wolf"))
                : null;
            float wolfDist = nearestWolf != null ? Vector3.Distance(Brain.transform.position, nearestWolf.position) : 999f;
            bool wolfThreat = nearestWolf != null && wolfDist <= 18f;

            if (wolfThreat)
            {
                if (hasClubOnBack)
                {
                    if (leftOccupied)
                    {
                        // Danger is more important to defend than holding extra food!
                        // Drop held item in weapon hand to immediately brandish the club
                        foodChoices.Add("drop to defend");
                    }
                    else
                    {
                        foodChoices.Add("draw club");
                    }
                }
                else if (hasClubInHand)
                {
                    foodChoices.Add("defend with club");
                }
            }
            else
            {
                // Autonomous weapon management: holster club to free hands for carrying/foraging, or draw when traveling
                if (hasClubInHand && (freeHands == 0 || (s.satiety < 8500 && nearestBush != null)))
                {
                    foodChoices.Add("holster club");
                }
                else if (hasClubOnBack && !leftOccupied && (s.satiety >= 8500 && s.hydration >= 7500))
                {
                    // Only draw club during idle travel IF left hand is completely free
                    foodChoices.Add("draw club");
                }
            }

            // 4. Marine Protein & Tidal Crab / River Fish Foraging
            bool lowProtein = s.body.protein < 6500;
            bool huntHopeLost = Brain != null && Brain.Tick < huntingHopeLostUntilTick;
            var nearestCrab = Brain.Perception != null && Brain.Perception.Current != null
                ? Brain.Perception.Current.FirstOrDefault(x => x != null && x.id.Contains("crab"))
                : null;
            float crabDist = nearestCrab != null ? Vector3.Distance(Brain.transform.position, nearestCrab.position) : 999f;

            if ((lowProtein || s.satiety < 8500) && freeHands > 0 && !huntHopeLost)
            {
                if (nearestCrab != null)
                {
                    if (hasClubInHand && crabDist <= 2.4f) foodChoices.Add("strike crab with club");
                    else if (crabDist <= 2.2f) foodChoices.Add("catch crab");
                    else foodChoices.Add("approach crab");
                }

                if (inRiver || (Brain.Perception != null && Brain.Perception.Current != null &&
                    Brain.Perception.Current.Any(x => x != null && x.kind == NpcObjectKind.Item && x.id.Contains("river-fish"))))
                {
                    if (hasClubInHand) foodChoices.Add("strike fish with club");
                    else foodChoices.Add("catch fish");
                }

                bool nearShore = Brain.transform.position.y <= CoastalWater.Level + 3.2f || nearestCrab != null;
                if (nearShore && !foodChoices.Contains("catch crab") && !foodChoices.Contains("strike crab with club") && crabDist <= 2.2f)
                {
                    if (hasClubInHand) foodChoices.Add("strike crab with club");
                    else foodChoices.Add("catch crab");
                }
            }

            // 4b. Wilderness Meat & Leather Drops on Ground
            var nearestMeat = Brain.Perception != null && Brain.Perception.Current != null
                ? Brain.Perception.Current.FirstOrDefault(x => x != null && x.kind == NpcObjectKind.Item && x.id.Contains("meat") && x.permission && x.available)
                : null;
            float meatDist = nearestMeat != null ? Vector3.Distance(Brain.transform.position, nearestMeat.position) : 999f;
            if (nearestMeat != null && (lowProtein || s.satiety < 8500) && freeHands > 0)
            {
                if (meatDist > 1.8f) foodChoices.Add("approach meat");
                else foodChoices.Add("pick meat");
            }

            var nearestLeather = Brain.Perception != null && Brain.Perception.Current != null
                ? Brain.Perception.Current.FirstOrDefault(x => x != null && x.kind == NpcObjectKind.Item && x.id.Contains("leather") && x.permission && x.available)
                : null;
            float leatherDist = nearestLeather != null ? Vector3.Distance(Brain.transform.position, nearestLeather.position) : 999f;
            if (nearestLeather != null && freeHands > 0)
            {
                if (leatherDist > 1.8f) foodChoices.Add("approach leather");
                else foodChoices.Add("pick leather");
            }

            // 5. Edible items held or carried
            var heldPhys = Brain.Actions != null && Brain.Actions.Held != null ? Brain.Actions.Held.GetComponent<CityLife.Items.PhysicalItem>() : null;
            var cooker = Brain.GetComponentInChildren<HearthCooking>();
            if (cooker == null) cooker = FindFirstObjectByType<HearthCooking>();
            bool nearHearth = cooker != null && cooker.IsNearHearth(Brain.transform.position, out _);

            if (heldPhys != null)
            {
                if (HearthCooking.CanRoast(heldPhys.itemTypeId) && nearHearth)
                {
                    foodChoices.Add("roast catch");
                    foodChoices.Add("roast food on hearth");
                }
                if ((heldPhys.itemTypeId == "food-cooked-fish" || heldPhys.itemTypeId == "food-cooked-crab" || heldPhys.itemTypeId == "food-cooked-meat") && (s.satiety < 8500 || lowProtein))
                {
                    foodChoices.Add("feast catch");
                    foodChoices.Add("feast roasted catch");
                }
                if ((heldPhys.itemTypeId == "food-river-fish" || heldPhys.itemTypeId == "food-river-carp" || heldPhys.itemTypeId == "food-protein-crab" || heldPhys.itemTypeId == "food-wolf-meat") && (s.satiety < 8500 || s.body.protein < 7500))
                    foodChoices.Add("eat catch");
                if (heldPhys.itemTypeId == "food-sourfig-berry" && (s.satiety < 8500 || s.hydration < 8500))
                    foodChoices.Add("eat fruit");
            }

            if (moonbag != null && moonbag.CanRetrieve && (s.satiety < 8500 || s.hydration < 8500))
                foodChoices.Add("eat fruit");

            int carriedBerries = NpcPlayerControls.GetTotalCarriedBerries(s, Brain);
            if (carriedBerries > 0 && s.body.stomach <= 8800 && (s.satiety < 8500 || s.hydration < 8500))
                foodChoices.Add("eat fruit");

            // 6. Starvation route-to-known-food when hungry and no food in immediate view
            if ((s.satiety < 6500 || s.body.stomach <= 4000) &&
                !foodChoices.Any(x => x.StartsWith("eat") || x.StartsWith("feast") || x.StartsWith("gather") || x == "approach berry") &&
                nearestBush == null)
            {
                if (s.observedPlaces != null && s.observedPlaces.Any(x => x != null && x.id.StartsWith("berry-food")))
                {
                    foodChoices.Add("seek food");
                }
            }

            // 6b. Dehydration route-to-river when thirsty and not at river waterline and container empty or low
            if (s.hydration < 7500 && !canDrinkRiver && (s.freshwaterMl < 250 || s.hydration < 4000) && !foodChoices.Contains("drink water"))
            {
                foodChoices.Add("seek water");
            }

            // Player Directive waypoint guidance
            if (PlayerDirectiveTarget.HasValue)
            {
                float distToDirective = Vector3.Distance(Brain.transform.position, PlayerDirectiveTarget.Value);
                if (distToDirective > 2.5f)
                {
                    foodChoices.Insert(0, "follow player directive");
                }
                else
                {
                    ClearPlayerDirective();
                    if (Diary != null) Diary.AddEntry(Brain.Tick, "Journey", "Reached player designated guidance waypoint.");
                }
            }

            // Urgency ranking:
            string urgent = null;
            // Immediate predator defense takes precedence over foraging
            if (wolfThreat)
            {
                if (hasClubInHand && wolfDist <= 5.5f && foodChoices.Contains("defend with club"))
                    urgent = "defend with club";
                else if (hasClubOnBack && foodChoices.Contains("draw club"))
                    urgent = "draw club";
                else if (hasClubInHand && foodChoices.Contains("defend with club"))
                    urgent = "defend with club";
            }
            // Active player directive (unless critically threatened by predators or dehydration)
            if (urgent == null && foodChoices.Contains("follow player directive") && s.hydration > 3500 && s.satiety > 3500)
            {
                urgent = "follow player directive";
            }
            // Life-critical dehydration: drinking or seeking water immediately before starvation or foraging
            if (urgent == null && s.hydration < 3500)
            {
                if (foodChoices.Contains("drink river")) urgent = "drink river";
                else if (foodChoices.Contains("drink spring")) urgent = "drink spring";
                else if (foodChoices.Contains("drink water")) urgent = "drink water";
                else if (foodChoices.Contains("seek water")) urgent = "seek water";
            }
            // Critical protein hunger drive
            if (urgent == null && lowProtein)
            {
                if (foodChoices.Contains("feast roasted catch")) urgent = "feast roasted catch";
                else if (foodChoices.Contains("feast catch")) urgent = "feast catch";
                else if (foodChoices.Contains("eat catch")) urgent = "eat catch";
                else if (foodChoices.Contains("pick meat")) urgent = "pick meat";
                else if (foodChoices.Contains("approach meat")) urgent = "approach meat";
                else if (foodChoices.Contains("strike crab with club")) urgent = "strike crab with club";
                else if (foodChoices.Contains("catch crab")) urgent = "catch crab";
                else if (foodChoices.Contains("approach crab")) urgent = "approach crab";
                else if (foodChoices.Contains("strike fish with club")) urgent = "strike fish with club";
                else if (foodChoices.Contains("catch fish")) urgent = "catch fish";
            }
            // Eating when hungry!
            if (urgent == null && s.satiety < 8500)
            {
                if (foodChoices.Contains("feast catch")) urgent = "feast catch";
                else if (foodChoices.Contains("feast roasted catch")) urgent = "feast roasted catch";
                else if (foodChoices.Contains("eat catch")) urgent = "eat catch";
                else if (foodChoices.Contains("eat fruit")) urgent = "eat fruit";
            }
            if (urgent == null && heldPhys != null && HearthCooking.CanRoast(heldPhys.itemTypeId) && nearHearth && foodChoices.Contains("roast catch"))
                urgent = "roast catch";
            if (urgent == null && heldPhys != null && HearthCooking.CanRoast(heldPhys.itemTypeId) && nearHearth && foodChoices.Contains("roast food on hearth"))
                urgent = "roast food on hearth";
            if (urgent == null && s.hydration < 7500)
            {
                if (foodChoices.Contains("drink water")) urgent = "drink water";
                else if (foodChoices.Contains("drink river")) urgent = "drink river";
                else if (foodChoices.Contains("drink spring")) urgent = "drink spring";
                else if (foodChoices.Contains("seek water")) urgent = "seek water";
            }
            if (urgent == null && (s.satiety < 8500 || s.body.stomach <= 7000))
            {
                if (foodChoices.Contains("gather berry")) urgent = "gather berry";
                else if (foodChoices.Contains("approach berry")) urgent = "approach berry";
                else if (foodChoices.Contains("strike crab with club")) urgent = "strike crab with club";
                else if (foodChoices.Contains("catch crab")) urgent = "catch crab";
                else if (foodChoices.Contains("approach crab")) urgent = "approach crab";
                else if (foodChoices.Contains("strike fish with club")) urgent = "strike fish with club";
                else if (foodChoices.Contains("catch fish")) urgent = "catch fish";
                else if (foodChoices.Contains("seek food")) urgent = "seek food";
            }
            // Well-fulfilled domestic camp routines: balance camp chores with canyon exploration
            if (urgent == null && IsWellFulfilled() && !wolfThreat)
            {
                if (nearHearth) foodChoices.Add("rest by hearth");
                foodChoices.Add("pack rocks for shelter");
                foodChoices.Add("survey river");
                // Alternate domestic routines with canyon exploration so inhabitant doesn't freeze in place
                bool justDidCampWork = recentVerifiedOutcome != null && (recentVerifiedOutcome.Contains("pack shelter") || recentVerifiedOutcome.Contains("survey river") || recentVerifiedOutcome.Contains("rest by hearth"));
                if (justDidCampWork)
                {
                    urgent = null;
                }
                else
                {
                    urgent = nearHearth ? "rest by hearth" : "pack rocks for shelter";
                }
            }
            if (urgent == null)
                urgent = foodChoices.FirstOrDefault();

            var choices=new List<string>();if(urgent!=null)choices.Add(urgent);
            foreach (var other in foodChoices)
            {
                if (other != urgent && !choices.Contains(other) && choices.Count < 3)
                    choices.Add(other);
            }
            var directions=new [] {("explore north",Vector3.forward),("explore east",Vector3.right),
                ("explore south",Vector3.back),("explore west",Vector3.left)};
            var candidates=new List<(string action,int score)>();
            for(int i=0;i<directions.Length;i++)
            {
                var p=Brain.transform.position+directions[i].Item2*18f;
                if(!Brain.TerrainNavigation.Walkable(p,out _) || !Brain.TerrainNavigation.IsWithinSafePerimeter(p, 10f))
                {
                    p=Brain.transform.position+directions[i].Item2*12f;
                    if(!Brain.TerrainNavigation.Walkable(p,out _) || !Brain.TerrainNavigation.IsWithinSafePerimeter(p, 10f))
                    {
                        p=Brain.transform.position+directions[i].Item2*6f;
                        if(!Brain.TerrainNavigation.Walkable(p,out _) || !Brain.TerrainNavigation.IsWithinSafePerimeter(p, 10f))continue;
                    }
                }
                var cell=new Vector2Int(Mathf.RoundToInt(p.x/3f),Mathf.RoundToInt(p.z/3f));
                int visits=PlaceLedger.Cell(s,cell.x,cell.y)?.visits??0;
                int score = visits * 10 + (i + explorationSeed) % 4;

                // Boundary reflection bias: when approaching canyon edge, steer back inward
                Vector3 toCenter = new Vector3(-Brain.transform.position.x, 0, -Brain.transform.position.z).normalized;
                float dotInward = Vector3.Dot(directions[i].Item2, toCenter);
                if (Mathf.Abs(Brain.transform.position.x) > 180f || Brain.transform.position.z < -180f || Brain.transform.position.z > 280f)
                {
                    if (dotInward > 0.3f) score -= 40; // Reward heading inward
                    else if (dotInward < -0.3f) score += 80; // Penalize heading toward edge of the world
                }

                // Distant landmark visual attraction: reward exploring toward visible distant landmarks
                if (Brain.Perception != null && Brain.Perception.Current != null)
                {
                    foreach (var obs in Brain.Perception.Current)
                    {
                        if (obs != null && obs.visionZone == VisionZone.DistantLandmark)
                        {
                            Vector3 toLandmark = (obs.position - Brain.transform.position).normalized;
                            if (Vector3.Dot(directions[i].Item2, toLandmark) > 0.6f)
                            {
                                score -= 25; // Visual attraction toward landmark
                            }
                        }
                    }
                }

                candidates.Add((directions[i].Item1, score));
            }
            foreach(var candidate in candidates.OrderBy(c=>c.score).Take(4-choices.Count))
                choices.Add(candidate.action);
            return choices;
        }
        private bool StartRoute(Vector3 destination)
        {
            var planned=Brain.TerrainNavigation.Plan(Brain.transform.position,destination);
            if(planned==null||planned.Count==0)return false;
            route.Clear();foreach(var waypoint in planned)route.Enqueue(waypoint);
            routeStartTick=Brain.Tick;routeOrigin=Brain.transform.position;
            if (Brain != null && Brain.Log != null)
                Brain.Log.Record(Brain.Tick, "ROUTE", Brain.DescribePerception(), routePurpose ?? "navigate", "Plot waypoint route", $"{planned.Count} waypoints queued");
            return true;
        }
        private bool MoveRoute()
        {
            while(route.Count>0&&Vector2.Distance(new Vector2(Brain.transform.position.x,Brain.transform.position.z),
                new Vector2(route.Peek().x,route.Peek().z))<.50f)route.Dequeue();
            if(route.Count==0)
            {
                ExploredMetres+=Mathf.RoundToInt(Vector3.Distance(routeOrigin,Brain.transform.position));
                LastOutcome=mapAcceptanceRequested?"Walked to scripted diagnostic destination":(LastChoiceByModel ? "Walked to model-chosen place" : "Walked to rule-chosen destination");
                recentVerifiedOutcome=mapAcceptanceRequested?(routePurpose!=null?routePurpose+" reached":"scripted destination reached"):routePurpose+" reached";
                Record("route","reached",routePurpose,null);
                if (routePurpose == "approach crab")
                {
                    var nearestObs = Brain.Perception != null && Brain.Perception.Current != null
                        ? Brain.Perception.Current.FirstOrDefault(x => x != null && x.id.Contains("crab"))
                        : null;
                    float dist = nearestObs != null ? Vector3.Distance(Brain.transform.position, nearestObs.position) : 999f;
                    if (dist > 2.4f)
                    {
                        huntingPursuitFailures++;
                        if (huntingPursuitFailures >= 3)
                        {
                            huntingHopeLostUntilTick = Brain.Tick + 450;
                            huntingPursuitFailures = 0;
                            if (Diary != null) Diary.AddEntry(Brain.Tick, "Mind", "Lost hope in catching prey for now. Stored lesson: pursuing quick animals in the open without cornering them only drains energy. I must forage for wild berries and wait for a calmer opportunity.");
                            if (Brain != null && Brain.Log != null) Brain.Log.Record(Brain.Tick, "LESSON", Brain.DescribePerception(), "hunting", "Loss of hope & stored lesson", "Prey evaded capture 3 times. Ceasing pursuit to conserve stamina.");
                            LastOutcome = "Lost hope in hunting prey for now; stored lesson to forage elsewhere";
                        }
                    }
                }
                if (Brain != null && Brain.Log != null)
                    Brain.Log.Record(Brain.Tick, "OUTCOME", Brain.DescribePerception(), routePurpose ?? "patrol", "Destination reached", LastOutcome);
                if (Brain != null) Brain.LastResult = LastOutcome;
                SynthesizeGroundedNarrative(routePurpose);
                routePurpose=null;
                blockedTicks=0;
                nextRequestTick=Brain.Tick+10;Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;
            }
            if(Brain.Tick-routeStartTick>1000)
            {
                route.Clear();Record("route","bounded-route-timeout",routePurpose,null);
                if (Brain != null && Brain.Log != null)
                    Brain.Log.Record(Brain.Tick, "FALLBACK", Brain.DescribePerception(), routePurpose ?? "patrol", "Route timeout", "clearing route");
                routePurpose=null;blockedTicks=0;nextRequestTick=Brain.Tick+50;Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;
            }
            Vector3 delta=route.Peek()-Brain.transform.position;delta.y=0;
            Vector3 direction=Brain.TerrainNavigation.ConstrainMotion(Brain.transform.position,delta.normalized,
                Brain.Actor.WalkSpeed*NpcAutonomy.StepSeconds);
            if(direction==Vector3.zero)
            {
                blockedTicks++;
                if (Vector2.Distance(new Vector2(Brain.transform.position.x, Brain.transform.position.z),
                    new Vector2(route.Peek().x, route.Peek().z)) < 1.0f)
                {
                    route.Dequeue();
                    blockedTicks = 0;
                }
                else if (blockedTicks > 5)
                {
                    route.Clear();Record("route","live-terrain-blocked",routePurpose,null);
                    if (Brain != null && Brain.Log != null)
                        Brain.Log.Record(Brain.Tick, "FALLBACK", Brain.DescribePerception(), routePurpose ?? "patrol", "Live terrain blocked", "clearing route");
                    routePurpose=null;blockedTicks=0;nextRequestTick=Brain.Tick+50;
                }
            }
            else
            {
                blockedTicks=0;
            }
            Brain.Actor.Step(direction,NpcAutonomy.StepSeconds);return true;
        }
        private Vector3 FindNearestShore(Vector3 current)
        {
            float bestDist = float.MaxValue;
            Vector3 bestPos = current;
            for (int r = 2; r <= 36; r += 2)
            {
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI * 2f / 16f;
                    float sx = current.x + Mathf.Cos(a) * r;
                    float sz = current.z + Mathf.Sin(a) * r;
                    float sh = CoastalTerrain.Height(sx, sz);
                    if (sh > CoastalWater.Level + 0.25f && Brain != null && Brain.TerrainNavigation != null &&
                        Brain.TerrainNavigation.Walkable(new Vector3(sx, sh, sz), out Vector3 floor))
                    {
                        float dist = (sx - current.x) * (sx - current.x) + (sz - current.z) * (sz - current.z);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestPos = floor;
                        }
                    }
                }
                if (bestDist < float.MaxValue) break;
            }
            return bestPos;
        }
        private bool TrySafeReturn()
        {
            if(Refuge==null||!Refuge.GeometryVerified)return false;
            Physics.SyncTransforms();
            for(float x=-9.5f;x<=-7.5f;x+=.5f)for(float z=-1.25f;z<=.5f;z+=.5f)
            {
                Vector3 above=Refuge.OriginOffset+new Vector3(x,3,z);
                if(!Physics.Raycast(above,Vector3.down,out var hit,2f,Starfall.Refuge.RefugeRuntime.GeometryMask,QueryTriggerInteraction.Ignore))continue;
                // Ray hits on the mat/storage/hearth are not standing ground.
                // The floor is the same solid authored support used by refuge
                // traversal; keep a bare patch and verify roof protection.
                if(hit.collider.name!="Refuge floor")continue;
                Vector3 safe=hit.point+Vector3.up*.06f;
                if(Physics.CheckCapsule(safe+Vector3.up*.45f,safe+Vector3.up*1.5f,.35f,
                    Starfall.Refuge.RefugeRuntime.GeometryMask,QueryTriggerInteraction.Ignore))continue;
                if(Refuge.Sample(safe+Vector3.up).RainMultiplier>=.02f)continue;
                var s=Food.Model.State;
                if(!AllocateFoodRequest(out int returnRequest))return false;
                FoodReceipt returned=Food.Model.Execute(s.world,s.generation,returnRequest,FoodAction.Return,"inventory",Food);
                if(!returned.success)return false;
                Brain.Actor.Place(safe);Brain.Actor.DeadPose=false;Brain.Actor.RefreshAnimation();
                Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
                LastSafeGround=hit.collider.name;
                LastSafeGroundY=hit.point.y;
                s.actorPosition=safe;Record("return","safe-refuge-world-preserved",null,null,returned);
                LastChoice="safe return";LastChoiceByModel=false;
                LastOutcome="Returned after "+s.deaths[s.deaths.Count-1].cause;
                Persist();
                return true;
            }
            return false;
        }
        public bool DiagnosticSafeReturn()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-npcSurvivalDeathAcceptance")<0)
                throw new InvalidOperationException("Only the explicit compiled death diagnostic may invoke this entry point.");
            if(Food==null||!Food.Model.State.body.dead)return false;
            return TrySafeReturn();
        }
        public bool DiagnosticVerifyReload()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-npcSurvivalDeathAcceptance")<0||
                string.IsNullOrEmpty(savePath)||!File.Exists(savePath))return false;
            var s=Food.Model.State;
            var reopened=new FoodModel(s.world,s.generation,s.seed);
            reopened.State.actorId=s.actorId;
            return reopened.Load(savePath,s.world,s.generation)&&reopened.Json()==Food.Model.Json();
        }
        private void Persist()
        {
            if(savePath==null)return;
            try{Food.Model.Save(savePath);MapHud?.NotifyStateChanged();}
            catch(Exception error)
            {
                Record("save","scoped-save-failed-"+error.GetType().Name,null,null);
                Enabled=false;Status="Survival save failed closed";Cancel("save-failed");
            }
        }
        private bool AllocateFoodRequest(out int allocated)
        {
            allocated=0;
            if(!TryNextFoodRequest(request,out int next))
            {
                Enabled=false;Status="Scoped food request sequence exhausted";
                Record("food","request-sequence-exhausted",null,null);
                return false;
            }
            request=allocated=next;return true;
        }
        public static bool TryNextFoodRequest(int previous,out int next)
        {
            next=0;
            if(previous<0||previous==int.MaxValue)return false;
            next=previous+1;return true;
        }
        public void SynthesizeGroundedNarrative(string currentAction)
        {
            if (Food == null || Food.Model == null || Food.Model.State == null) return;
            var s = Food.Model.State;
            int hungerPct = Mathf.Clamp(s.satiety / 100, 0, 100);
            int thirstPct = Mathf.Clamp(s.hydration / 100, 0, 100);
            int healthPct = Mathf.Clamp(s.body.health / 100, 0, 100);
            int proteinPct = s.body != null ? Mathf.Clamp(s.body.protein / 100, 0, 100) : 100;

            var nearWolf = Brain != null && Brain.Perception != null && Brain.Perception.Current != null
                ? Brain.Perception.Current.FirstOrDefault(x => x != null && x.id.Contains("wolf") && x.distanceMillimetres <= 18000)
                : null;
            bool wolfNearby = nearWolf != null;

            // Advisory plan
            if (wolfNearby)
                Plan = "Ready hunter's club > Defend against coastal timber wolf > Secure safety";
            else if (s.hydration < 4000)
                Plan = "Reach freshwater river or seep > Quench dehydration > Scout food";
            else if (s.satiety < 4000 || s.body.protein < 5000)
                Plan = "Locate sourfig berries or marine crabs > Eat to restore stamina > Survey terrain";
            else if (currentAction != null && currentAction.StartsWith("explore"))
                Plan = "Survey coastal terrain > Map unexplored cells > Maintain safe line to refuge";
            else
                Plan = "Sustain hydration and satiety > Gather provisions > Scout territory";

            // Fictional dialogue / inner monologue
            if (s.body.submerged)
                Dialogue = "\"Water in my lungs... need dry shore immediately!\"";
            else if (currentAction == "defend with club")
                Dialogue = "\"Back off! This club has teeth!\"";
            else if (currentAction == "draw club")
                Dialogue = "\"Hearing rustling in the scrub... better ready my weapon.\"";
            else if (currentAction == "holster club")
                Dialogue = "\"Coast seems clear. Stowing my club so I can gather with both hands.\"";
            else if (wolfNearby)
                Dialogue = "\"A timber wolf is stalking the ridge. I need my club ready.\"";
            else if (s.hydration < 2000)
                Dialogue = "\"My mouth is parched and burning... I must reach fresh water.\"";
            else if (s.satiety < 2000)
                Dialogue = "\"Stomach is hollow and aching. I need food before my strength fails.\"";
            else if (currentAction == "eat fruit" || currentAction == "eat catch" || currentAction == "feast catch" || currentAction == "feast roasted catch")
                Dialogue = "\"Sustenance at last. The food restores my focus and strength.\"";
            else if (currentAction == "drink river" || currentAction == "drink spring")
                Dialogue = "\"Cold, sweet freshwater. My head is clearing.\"";
            else if (currentAction == "drink water")
                Dialogue = "\"Carried water quenches the burn in my throat. Strength returning.\"";
            else if (currentAction != null && currentAction.StartsWith("approach berry"))
                Dialogue = "\"Sourfig bushes ahead on the rock shelf. Ripe fruit to gather.\"";
            else if (currentAction == "strike crab with club")
                Dialogue = "\"Taking aim with the club... one solid strike will stun it!\"";
            else if (currentAction != null && currentAction.StartsWith("approach crab"))
                Dialogue = "\"Spotted a shore crab scuttling on the wet stones. Cutting off its escape line.\"";
            else if (currentAction == "pick meat" || (currentAction != null && currentAction.StartsWith("approach meat")))
                Dialogue = "\"Rich wolf venison on the stones. Hearty meat to roast and feast upon.\"";
            else if (currentAction == "pick leather" || (currentAction != null && currentAction.StartsWith("approach leather")))
                Dialogue = "\"Thick wolf leather pelt. Durable hide for camp tailoring and gear.\"";
            else if (currentAction == "catch fish" || currentAction == "catch crab")
                Dialogue = "\"Movement in the shallows. Quick hands bring protein.\"";
            else if (s.satiety < 6000)
                Dialogue = "\"The canyon trail is long. I should forage along the banks.\"";
            else
                Dialogue = "\"Warm wind off the sea. The canyon is calm today.\"";

            // Generated reflection
            string driveSummary = $"Health: {healthPct}% | Energy: {hungerPct}% | Protein: {proteinPct}% | Hydration: {thirstPct}% | Water: {s.freshwaterMl}ml";
            int placeCount = s.observedPlaces != null ? s.observedPlaces.Count : 0;
            int cellCount = s.exploredCells != null ? s.exploredCells.Count : 0;
            string memorySummary = $"{cellCount} cells mapped, {placeCount} landmarks remembered.";
            string lastEvent = !string.IsNullOrEmpty(LastOutcome) ? $" {LastOutcome}." : "";
            Reflection = $"{driveSummary}. {memorySummary}{lastEvent}";
        }
        private void Execute(string action,StarfallSurvivalThought.Result result)
        {
            // Re-sense after the asynchronous answer. No target survives a world,
            // permission, line-of-sight or reach change during inference.
            Brain.Perception.Sense(Brain.Tick);
            var live=Eligible();
            if(!StarfallSurvivalThought.Parse(action,live,out string accepted))
            {
                Record("decision","stale-or-ineligible",action,result);
                if (Brain != null && Brain.Log != null)
                    Brain.Log.Record(Brain.Tick, "FALLBACK", Brain.DescribePerception(), action, "Stale action rejected", "re-evaluating next tick");
                nextRequestTick=Brain.Tick+50;
                return;
            }
            AcceptedDecisions++;Record("decision","live-admitted",accepted,result);
            LastChoice=accepted;
            LastChoiceByModel=result != null && result.status == "parsed-awaiting-live-check";
            Status = LastChoiceByModel ? "Local model choice admitted after live validation" : "Survival rule-based choice: " + accepted;
            SynthesizeGroundedNarrative(accepted);
            if (Brain != null && Brain.Log != null)
                Brain.Log.Record(Brain.Tick, "DECISION", Brain.DescribePerception(), accepted, LastChoiceByModel ? "Local model choice" : "Deterministic survival rule", accepted);
            if(accepted.StartsWith("explore ",StringComparison.Ordinal))
            {
                routePurpose=accepted;
                Vector3 direction=accepted.EndsWith("north")?Vector3.forward:accepted.EndsWith("south")?Vector3.back:
                    accepted.EndsWith("east")?Vector3.right:Vector3.left;
                Vector3 destination=Brain.transform.position+direction*18f;
                if(!Brain.TerrainNavigation.Walkable(destination,out _))
                {
                    destination=Brain.transform.position+direction*12f;
                    if(!Brain.TerrainNavigation.Walkable(destination,out _))
                        destination=Brain.transform.position+direction*6f;
                }
                explorationSeed++;
                if(!StartRoute(destination)){routePurpose=null;Record("route","no-walkable-exploration-route",accepted,result);}
            }
            else if(accepted.StartsWith("approach ",StringComparison.Ordinal))
            {
                routePurpose=accepted;
                if (accepted.EndsWith("berry"))
                {
                    NpcObservation bestBush = null;
                    float bestBushDist = float.MaxValue;
                    if (Brain.Perception != null && Brain.Perception.Current != null)
                    {
                        foreach (var obs in Brain.Perception.Current)
                        {
                            if (obs != null && obs.kind == NpcObjectKind.Place && obs.id.StartsWith("berry-food") &&
                                obs.permission && obs.available && obs.seenAtTick >= Brain.Tick - 15)
                            {
                                float d = Vector3.Distance(Brain.transform.position, obs.position);
                                if (d < bestBushDist)
                                {
                                    bestBushDist = d;
                                    bestBush = obs;
                                }
                            }
                        }
                    }
                    Vector3 targetDest = bestBush != null && bestBush.approach != Vector3.zero ? bestBush.approach : (bestBush != null ? bestBush.position : Vector3.zero);
                    if (bestBush == null || (!StartRoute(targetDest) && !StartRoute(bestBush.position)))
                    {
                        routePurpose=null;
                        Record("route","live-target-route-rejected",accepted,result);
                    }
                    else
                    {
                        LastOutcome = "Approaching nearby ripe berry bush to forage";
                        recentVerifiedOutcome = "approach berry started";
                    }
                }
                else if (accepted.EndsWith("crab"))
                {
                    NpcObservation bestCrab = null;
                    float bestCrabDist = float.MaxValue;
                    if (Brain.Perception != null && Brain.Perception.Current != null)
                    {
                        foreach (var obs in Brain.Perception.Current)
                        {
                            if (obs != null && obs.kind == NpcObjectKind.Item && obs.id.Contains("crab") &&
                                obs.permission && obs.available && obs.seenAtTick >= Brain.Tick - 15)
                            {
                                float d = Vector3.Distance(Brain.transform.position, obs.position);
                                if (d < bestCrabDist)
                                {
                                    bestCrabDist = d;
                                    bestCrab = obs;
                                }
                            }
                        }
                    }
                    Vector3 targetDest = bestCrab != null && bestCrab.approach != Vector3.zero ? bestCrab.approach : (bestCrab != null ? bestCrab.position : Vector3.zero);
                    // Predictive interception strategy: lead the moving crab along its escape vector
                    if (bestCrab != null)
                    {
                        var crabGo = GameObject.Find(bestCrab.id);
                        var crabActor = crabGo != null ? crabGo.GetComponent<CoastalCrabActor>() : null;
                        if (crabActor != null && crabActor.CurrentVelocity.sqrMagnitude > 0.04f)
                        {
                            Vector3 lead = crabGo.transform.position + crabActor.CurrentVelocity.normalized * 1.6f;
                            lead.y = CoastalTerrain.Height(lead.x, lead.z);
                            targetDest = lead;
                        }
                    }
                    if (bestCrab == null || (!StartRoute(targetDest) && !StartRoute(bestCrab.position)))
                    {
                        routePurpose = null;
                        Record("route", "live-target-route-rejected", accepted, result);
                    }
                    else
                    {
                        LastOutcome = "Approaching nearby shore crab with predictive interception to harvest protein";
                        recentVerifiedOutcome = "approach crab started";
                    }
                }
                else if (accepted.EndsWith("meat"))
                {
                    NpcObservation bestMeat = null;
                    float bestMeatDist = float.MaxValue;
                    if (Brain.Perception != null && Brain.Perception.Current != null)
                    {
                        foreach (var obs in Brain.Perception.Current)
                        {
                            if (obs != null && obs.kind == NpcObjectKind.Item && obs.id.Contains("meat") &&
                                obs.permission && obs.available && obs.seenAtTick >= Brain.Tick - 15)
                            {
                                float d = Vector3.Distance(Brain.transform.position, obs.position);
                                if (d < bestMeatDist)
                                {
                                    bestMeatDist = d;
                                    bestMeat = obs;
                                }
                            }
                        }
                    }
                    Vector3 targetDest = bestMeat != null && bestMeat.approach != Vector3.zero ? bestMeat.approach : (bestMeat != null ? bestMeat.position : Vector3.zero);
                    if (bestMeat == null || (!StartRoute(targetDest) && !StartRoute(bestMeat.position)))
                    {
                        routePurpose = null;
                        Record("route", "live-target-route-rejected", accepted, result);
                    }
                    else
                    {
                        LastOutcome = "Approaching fresh wolf venison meat on ground";
                        recentVerifiedOutcome = "approach meat started";
                    }
                }
                else if (accepted.EndsWith("leather"))
                {
                    NpcObservation bestLeather = null;
                    float bestLeatherDist = float.MaxValue;
                    if (Brain.Perception != null && Brain.Perception.Current != null)
                    {
                        foreach (var obs in Brain.Perception.Current)
                        {
                            if (obs != null && obs.kind == NpcObjectKind.Item && obs.id.Contains("leather") &&
                                obs.permission && obs.available && obs.seenAtTick >= Brain.Tick - 15)
                            {
                                float d = Vector3.Distance(Brain.transform.position, obs.position);
                                if (d < bestLeatherDist)
                                {
                                    bestLeatherDist = d;
                                    bestLeather = obs;
                                }
                            }
                        }
                    }
                    Vector3 targetDest = bestLeather != null && bestLeather.approach != Vector3.zero ? bestLeather.approach : (bestLeather != null ? bestLeather.position : Vector3.zero);
                    if (bestLeather == null || (!StartRoute(targetDest) && !StartRoute(bestLeather.position)))
                    {
                        routePurpose = null;
                        Record("route", "live-target-route-rejected", accepted, result);
                    }
                    else
                    {
                        LastOutcome = "Approaching cured wolf leather pelt on ground";
                        recentVerifiedOutcome = "approach leather started";
                    }
                }
                else
                {
                    string target="spring-food";
                    if(!Observed(target,out var observation)||!StartRoute(observation.approach!=Vector3.zero?observation.approach:observation.position))
                    {routePurpose=null;Record("route","live-target-route-rejected",accepted,result);}
                }
            }
            else if (accepted == "seek food")
            {
                var s = Food.Model.State;
                PlaceObservationEvent bestPlace = null;
                float bestDist = float.MaxValue;
                if (s.observedPlaces != null)
                {
                    foreach (var place in s.observedPlaces)
                    {
                        if (place != null && place.id.StartsWith("berry-food"))
                        {
                            float d = Vector3.Distance(Brain.transform.position, place.position);
                            if (d < bestDist && d > 3f)
                            {
                                bestDist = d;
                                bestPlace = place;
                            }
                        }
                    }
                }
                Vector3 targetPos = bestPlace != null ? bestPlace.position : (Refuge != null ? Refuge.OriginOffset : Brain.transform.position + Vector3.forward * 15f);
                if (Brain.TerrainNavigation != null && Brain.TerrainNavigation.Walkable(targetPos, out var floor)) targetPos = floor;
                routePurpose = "seek food";
                if (!StartRoute(targetPos))
                {
                    routePurpose = null;
                    Record("route", "seek-food-route-failed", accepted, result);
                }
                else
                {
                    LastOutcome = "Routing to remembered food source to relieve hunger";
                    recentVerifiedOutcome = "seek food started";
                }
            }
            else if (accepted == "seek water")
            {
                Vector3 riverBank = Brain.TerrainNavigation != null ? Brain.TerrainNavigation.FindNearestRiverBank(Brain.transform.position) : new Vector3(0f, CoastalTerrain.Height(0f, -15f), -15f);
                routePurpose = "seek water";
                if (!StartRoute(riverBank))
                {
                    routePurpose = null;
                    Record("route", "seek-water-route-failed", accepted, result);
                }
                else
                {
                    LastOutcome = "Routing to freshwater river to quench dehydration";
                    recentVerifiedOutcome = "seek water started";
                }
            }
            else if (accepted == "drink water")
            {
                var s = Food.Model.State;
                if (s.freshwaterMl >= 250)
                {
                    s.freshwaterMl -= 250;
                    s.hydration = Mathf.Min(10000, s.hydration + 2000);
                    Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 1500);
                    BoostPlaceAffinity("freshwater-river", 5);
                    LastOutcome = $"Drank carried freshwater +2000 water ({s.freshwaterMl}ml remaining) [Drive reduced: Endorphin {s.body.endorphin}]";
                    FoodOutcomes++;
                    Persist();
                    recentVerifiedOutcome = "drink water succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
            }
            else if (accepted == "drink river")
            {
                var s = Food.Model.State;
                s.hydration = Mathf.Min(10000, s.hydration + 2500);
                s.freshwaterMl = 2000;
                Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 1800);
                BoostPlaceAffinity("freshwater-river", 15);
                LastOutcome = "Drank fresh river water +2500 water & refilled water container [Drive reduced: Endorphin " + s.body.endorphin + "]";
                FoodOutcomes++;
                Persist();
                recentVerifiedOutcome = "drink river succeeded";
                if (Brain.Actor != null) Brain.Actor.Gesture();
            }
            else if (accepted == "catch fish" || accepted == "strike fish with club")
            {
                var controls = Brain.GetComponent<NpcPlayerControls>() ?? FindAnyObjectByType<NpcPlayerControls>();
                if (controls != null)
                {
                    controls.SpawnFishInHand();
                    var carry = Brain.GetComponentInChildren<HunterClubCarry>();
                    bool usedClub = accepted == "strike fish with club" || (carry != null && !carry.Stowed);
                    LastOutcome = usedClub ? "Struck whiskered river barber in shallows with hunter's club" : "Caught fresh river barber in shallows";
                    FoodOutcomes++;
                    huntingPursuitFailures = 0;
                    Persist();
                    recentVerifiedOutcome = usedClub ? "strike fish with club succeeded" : "catch fish succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                    if (Diary != null) Diary.AddEntry(Brain.Tick, "Hunting", usedClub
                        ? "Brandished hunter's club and struck a whiskered river barber cruising the shallows. Harvested rich freshwater protein."
                        : "Landed fresh river barber from the canyon shallows.");
                }
            }
            else if (accepted == "catch crab" || accepted == "strike crab with club")
            {
                var controls = Brain.GetComponent<NpcPlayerControls>() ?? FindAnyObjectByType<NpcPlayerControls>();
                if (controls != null)
                {
                    controls.SpawnCrabInHand();
                    var carry = Brain.GetComponentInChildren<HunterClubCarry>();
                    bool usedClub = accepted == "strike crab with club" || (carry != null && !carry.Stowed);
                    LastOutcome = usedClub
                        ? "Struck shore crab with hunter's club and harvested fresh marine protein"
                        : "Caught protein shore crab on coastal bank";
                    FoodOutcomes++;
                    huntingPursuitFailures = 0;
                    Persist();
                    recentVerifiedOutcome = usedClub ? "strike crab with club succeeded" : "catch crab succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                    var crabActor = FindAnyObjectByType<CoastalCrabActor>();
                    if (crabActor != null) crabActor.Stun(5.0f);
                    if (Diary != null) Diary.AddEntry(Brain.Tick, "Hunting", usedClub
                        ? "Brandished hunter's club with precise timing and struck the scuttling shore crab. Harvested rich marine protein."
                        : "Stalked and captured a coastal protein crab along the waterline.");
                }
            }
            else if (accepted == "pick meat")
            {
                var controls = Brain.GetComponent<NpcPlayerControls>() ?? FindFirstObjectByType<NpcPlayerControls>();
                if (controls != null)
                {
                    controls.SpawnMeatInHand();
                    LastOutcome = "Gathered fresh raw wolf venison meat from ground";
                    FoodOutcomes++;
                    Persist();
                    recentVerifiedOutcome = "pick meat succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
            }
            else if (accepted == "pick leather")
            {
                var controls = Brain.GetComponent<NpcPlayerControls>() ?? FindFirstObjectByType<NpcPlayerControls>();
                if (controls != null)
                {
                    controls.SpawnLeatherInHand();
                    LastOutcome = "Collected cured wolf leather pelt from ground";
                    FoodOutcomes++;
                    Persist();
                    recentVerifiedOutcome = "pick leather succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
            }
            else if (accepted == "holster club")
            {
                var carry = Brain.GetComponentInChildren<HunterClubCarry>();
                if (carry != null)
                {
                    carry.SetStowed(true);
                    LastOutcome = "Holstered heavy club onto back to free hands";
                    Persist();
                    recentVerifiedOutcome = "holster club succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
            }
            else if (accepted == "drop to defend")
            {
                // User requirement: danger is more important to defend than holding extra food!
                // Inhabitant drops what is held in weapon hand to immediately brandish the club for defense.
                var itemToDrop = Brain.Actions != null ? (Brain.Actions.HeldLeft ?? Brain.Actions.HeldRight) : null;
                if (itemToDrop != null)
                {
                    string dropName = itemToDrop.name;
                    Brain.ExecutePlayerAction(NpcActionKind.Drop, itemToDrop.StableId);
                    var carry = Brain.GetComponentInChildren<HunterClubCarry>();
                    if (carry != null)
                    {
                        carry.SetStowed(false, force: true);
                    }
                    LastOutcome = "Dropped " + dropName + " to brandish hunter's club for defense against wolf!";
                    FoodOutcomes++;
                    Persist();
                    recentVerifiedOutcome = "drop to defend succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                    if (Diary != null) Diary.AddEntry(Brain.Tick, "Defense", "Predator threat detected! Dropped held provisions to brandish hunter's club for defense.");
                }
            }
            else if (accepted == "draw club")
            {
                var carry = Brain.GetComponentInChildren<HunterClubCarry>();
                if (carry != null)
                {
                    if (Brain.Actions != null && Brain.Actions.HeldLeft != null)
                    {
                        LastOutcome = "Cannot draw club while left hand is holding an item";
                    }
                    else
                    {
                        carry.SetStowed(false);
                        LastOutcome = "Drew heavy club from back, ready for defense";
                        Persist();
                        recentVerifiedOutcome = "draw club succeeded";
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                    }
                }
            }
            else if (accepted == "defend with club")
            {
                var carry = Brain.GetComponentInChildren<HunterClubCarry>();
                if (carry != null && carry.Stowed)
                {
                    if (Brain.Actions != null && Brain.Actions.HeldLeft != null)
                    {
                        Brain.ExecutePlayerAction(NpcActionKind.Drop, Brain.Actions.HeldLeft.StableId);
                    }
                    carry.SetStowed(false, force: true);
                }
                if (Brain.Actor != null) Brain.Actor.Gesture();

                var wolves = FindObjectsByType<CoastalWolfEcology>(FindObjectsInactive.Exclude);
                CoastalWolfEcology nearestWolf = null;
                float bestWolfDist = 8.5f;
                foreach (var w in wolves)
                {
                    if (w != null)
                    {
                        float d = Vector3.Distance(Brain.transform.position, w.transform.position);
                        if (d < bestWolfDist)
                        {
                            bestWolfDist = d;
                            nearestWolf = w;
                        }
                    }
                }

                if (nearestWolf != null)
                {
                    nearestWolf.TakeClubHit(Brain.transform.position);
                    LastOutcome = "Struck prowling timber wolf with club, driving it away and yielding venison & leather!";
                    var s = Food != null ? Food.Model.State : null;
                    if (s != null)
                    {
                        s.body.endorphin = Mathf.Min(10000, s.body.endorphin + 2000);
                        s.body.fatigue = Mathf.Max(0, s.body.fatigue - 1000);
                    }
                }
                else
                {
                    LastOutcome = "Brandished club defensively against predator";
                }

                Persist();
                recentVerifiedOutcome = "defend with club succeeded";
            }
            else if (accepted == "roast food on hearth" || accepted == "roast catch")
            {
                if (HearthCooking.TryRoastHeldItem(Brain))
                {
                    var s = Food.Model.State;
                    BoostPlaceAffinity("refuge-hearth", 20);
                    LastOutcome = "Roasted fresh catch on refuge hearth embers";
                    FoodOutcomes++;
                    Persist();
                    recentVerifiedOutcome = "roast catch succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
            }
            else if (accepted == "feast roasted catch" || accepted == "feast catch")
            {
                var s = Food.Model.State;
                var heldPhys = Brain.Actions != null && Brain.Actions.Held != null ? Brain.Actions.Held.GetComponent<CityLife.Items.PhysicalItem>() : null;
                if (heldPhys != null)
                {
                    if (heldPhys.itemTypeId == "food-cooked-meat")
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 4500);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 5000);
                        s.satiety = Mathf.Min(10000, s.satiety + 4500);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 4000);
                    }
                    else if (heldPhys.itemTypeId == "food-cooked-fish")
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 3500);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 4000);
                        s.satiety = Mathf.Min(10000, s.satiety + 3500);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 3500);
                    }
                    else
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 3000);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 3800);
                        s.satiety = Mathf.Min(10000, s.satiety + 3000);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 3200);
                    }
                    s.knowsMealBenefit = true;
                    var heldGo = Brain.Actions.Held.gameObject;
                    Brain.ExecutePlayerAction(NpcActionKind.Drop, Brain.Actions.Held.StableId);
                    Destroy(heldGo);
                    LastOutcome = "Feasted on savory roasted meal [Protein/Energy boosted, drive reduced]";
                    FoodOutcomes++;
                    Persist();
                    recentVerifiedOutcome = "feast catch succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
            }
            else if (accepted == "eat catch")
            {
                var s = Food.Model.State;
                var heldPhys = Brain.Actions != null && Brain.Actions.Held != null ? Brain.Actions.Held.GetComponent<CityLife.Items.PhysicalItem>() : null;
                if (heldPhys != null && (heldPhys.itemTypeId == "food-river-fish" || heldPhys.itemTypeId == "food-river-carp" || heldPhys.itemTypeId == "food-protein-crab" || heldPhys.itemTypeId == "food-wolf-meat"))
                {
                    if (heldPhys.itemTypeId == "food-wolf-meat")
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 2500);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 3200);
                        s.satiety = Mathf.Min(10000, s.satiety + 2200);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 2500);
                    }
                    else
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 2500);
                        s.satiety = Mathf.Min(10000, s.satiety + 2000);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 2200);
                    }
                    s.knowsMealBenefit = true;
                    var heldGo = Brain.Actions.Held.gameObject;
                    Brain.ExecutePlayerAction(NpcActionKind.Drop, Brain.Actions.Held.StableId);
                    Destroy(heldGo);
                    LastOutcome = "Ate fresh protein catch [Hunger & protein replenished]";
                    FoodOutcomes++;
                    Persist();
                    recentVerifiedOutcome = "eat catch succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
            }
            else if (accepted == "eat fruit")
            {
                var s = Food.Model.State;
                var heldPhys = Brain.Actions != null && Brain.Actions.Held != null ? Brain.Actions.Held.GetComponent<CityLife.Items.PhysicalItem>() : null;
                var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();

                if (heldPhys != null && (heldPhys.itemTypeId == "food-sourfig-berry" || heldPhys.itemTypeId == "fruit"))
                {
                    s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                    s.hydration = Mathf.Min(10000, s.hydration + 600);
                    s.satiety = Mathf.Min(10000, s.satiety + 1500);
                    Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 1500);
                    s.knowsMealBenefit = true;
                    if (string.IsNullOrEmpty(s.lastMealEvidence)) s.lastMealEvidence = s.generation + ".ate." + (Brain != null ? Brain.Tick : 1);
                    var heldGo = Brain.Actions.Held.gameObject;
                    Brain.ExecutePlayerAction(NpcActionKind.Drop, Brain.Actions.Held.StableId);
                    Destroy(heldGo);
                    BoostPlaceAffinity("berry-grove", 12);
                    LastOutcome = "Ate ripe held sourfig berry [Energy boosted, drive reduced]";
                    FoodOutcomes++;
                    Persist();
                    recentVerifiedOutcome = "eat fruit succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
                else if (moonbag != null && moonbag.CanRetrieve)
                {
                    moonbag.RetrieveFruit();
                    s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                    s.hydration = Mathf.Min(10000, s.hydration + 600);
                    s.satiety = Mathf.Min(10000, s.satiety + 1500);
                    Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 1500);
                    s.knowsMealBenefit = true;
                    if (string.IsNullOrEmpty(s.lastMealEvidence)) s.lastMealEvidence = s.generation + ".ate." + (Brain != null ? Brain.Tick : 1);
                    BoostPlaceAffinity("berry-grove", 12);
                    LastOutcome = "Retrieved and ate berry from waist moonbag";
                    FoodOutcomes++;
                    Persist();
                    recentVerifiedOutcome = "eat fruit succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
                else if (s.carriedFruit > 0)
                {
                    if (AllocateFoodRequest(out int foodRequest))
                    {
                        if (!s.knowsBerry) { s.knowsBerry = true; s.berryEvidence = s.generation + ".observed-fruit." + foodRequest; }
                        FoodReceipt receipt = Food.Model.Execute(s.world, s.generation, foodRequest, FoodAction.Eat, "inventory", Food);
                        Record("food", receipt.code, accepted, result, receipt);
                        if (receipt.success)
                        {
                            BoostPlaceAffinity("berry-grove", 12);
                            LastOutcome = "Meal +" + receipt.foodDelta + " energy / +" + receipt.waterDelta + " water [Drive reduced: Endorphin " + s.body.endorphin + "]";
                            FoodOutcomes++;
                            Persist();
                            recentVerifiedOutcome = "eat fruit succeeded";
                        }
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                    }
                }
            }
            else if (accepted == "follow player directive")
            {
                if (PlayerDirectiveTarget.HasValue)
                {
                    routePurpose = "follow directive: " + (PlayerDirectiveLabel ?? "player beacon");
                    if (StartRoute(PlayerDirectiveTarget.Value))
                    {
                        LastOutcome = "Steering toward player guidance waypoint: " + PlayerDirectiveLabel;
                        recentVerifiedOutcome = "follow directive underway";
                    }
                    else
                    {
                        LastOutcome = "Cannot find walkable path to player directive";
                        recentVerifiedOutcome = "directive path blocked";
                        ClearPlayerDirective();
                    }
                }
            }
            else if (accepted == "rest by hearth")
            {
                var hearthPos = Refuge != null ? Refuge.Hearth : new Vector3(-8, 1.8f, 1);
                float distToHearth = Vector3.Distance(Brain.transform.position, hearthPos);
                if (distToHearth > 3.5f && route.Count == 0)
                {
                    routePurpose = "rest by hearth";
                    if (StartRoute(hearthPos))
                    {
                        LastOutcome = "Walking toward the warm hearth embers to rest.";
                        return;
                    }
                }
                var s = Food.Model.State;
                if (Brain.Actor != null) Brain.Actor.Step(Vector3.zero, NpcAutonomy.StepSeconds);
                s.body.fatigue = Mathf.Max(0, s.body.fatigue - 300);
                s.body.health = Mathf.Min(10000, s.body.health + 100);
                BoostPlaceAffinity("refuge-hearth", 5);
                LastOutcome = "Resting comfortably by warm hearth embers. Needs well-fulfilled.";
                recentVerifiedOutcome = "rest by hearth succeeded";
                if (Diary != null) Diary.AddEntry(Brain.Tick, "Rest", "Rested peacefully beside the glowing hearth stones.");
                if (Brain != null && Brain.Log != null)
                    Brain.Log.Record(Brain.Tick, "REST", Brain.DescribePerception(), accepted, "Rest by embers", LastOutcome);
            }
            else if (accepted == "pack rocks for shelter")
            {
                var workstations = FindObjectsByType<StoneBuildingWorkstation>(FindObjectsSortMode.None);
                StoneBuildingWorkstation shelterWs = null;
                foreach (var ws in workstations)
                {
                    if (ws != null && ws.CurrentTarget == StoneStructureKind.PackedWolfShelter)
                    {
                        shelterWs = ws;
                        break;
                    }
                }

                var shelterPos = shelterWs != null ? shelterWs.ConstructionSite :
                                (Refuge != null ? Refuge.Hearth + new Vector3(3f, 0, 0) : new Vector3(-8, 1.8f, 1));
                float distToShelter = Vector3.Distance(Brain.transform.position, shelterPos);
                if (distToShelter > 3.8f && route.Count == 0)
                {
                    routePurpose = "pack rocks for shelter";
                    if (StartRoute(shelterPos))
                    {
                        LastOutcome = "Gathering river cobbles and walking toward shelter perimeter.";
                        return;
                    }
                }
                if (Brain.Actor != null) Brain.Actor.Gesture();
                BoostPlaceAffinity("shelter", 10);

                if (shelterWs != null && !shelterWs.IsCompleted)
                {
                    int nextIndex = shelterWs.DepositedStonesCount + 1;
                    shelterWs.DepositStone($"river-cobble-{nextIndex:D2}", "stone-river-cobble", out bool justCompleted);
                    if (justCompleted)
                    {
                        LastOutcome = $"Placed final stone ({shelterWs.DepositedStonesCount}/{shelterWs.RequiredStones})! Fortified dry-stone defense wall completed against wolves.";
                        if (Diary != null)
                        {
                            Diary.AddEntry(Brain.Tick, "Crafting", "Finished stacking the perimeter dry-stone defense wall. Refuge mouth is fortified against wolves.");
                            Diary.UnlockMilestone("packed-shelter", Brain.Tick);
                        }
                    }
                    else
                    {
                        LastOutcome = $"Carefully placed basalt stone into wall ({shelterWs.DepositedStonesCount}/{shelterWs.RequiredStones} stones packed).";
                        if (Diary != null)
                        {
                            Diary.AddEntry(Brain.Tick, "Crafting", $"Packed stone {shelterWs.DepositedStonesCount}/{shelterWs.RequiredStones} into the perimeter defense wall.");
                        }
                    }
                }
                else
                {
                    LastOutcome = "Inspected fortified dry-stone wall. Perimeter secure against wolves.";
                    if (Diary != null) Diary.UnlockMilestone("packed-shelter", Brain.Tick);
                }

                recentVerifiedOutcome = "pack shelter succeeded";
                if (Brain != null && Brain.Log != null)
                    Brain.Log.Record(Brain.Tick, "CRAFT", Brain.DescribePerception(), accepted, "Pack shelter stones", LastOutcome);
            }
            else if (accepted == "survey river")
            {
                var riverLookout = new Vector3(0, 1.5f, -20);
                float distToRiver = Vector3.Distance(Brain.transform.position, riverLookout);
                if (distToRiver > 4.5f && route.Count == 0)
                {
                    routePurpose = "survey river";
                    if (StartRoute(riverLookout))
                    {
                        LastOutcome = "Walking toward the river overlook to survey the canyon.";
                        return;
                    }
                }
                if (Brain.Actor != null) Brain.Actor.Step(Vector3.zero, NpcAutonomy.StepSeconds);
                LastOutcome = "Surveying the tranquil river canyon. Life is in harmony.";
                recentVerifiedOutcome = "survey river succeeded";
                if (Diary != null) Diary.AddEntry(Brain.Tick, "Journey", "Paused at the canyon overlook, watching the clear water run to the sea.");
                if (Brain != null && Brain.Log != null)
                    Brain.Log.Record(Brain.Tick, "SURVEY", Brain.DescribePerception(), accepted, "Survey canyon river", LastOutcome);
            }
            else if (accepted == "gather berry")
            {
                var s = Food.Model.State;
                if (!s.knowsBerry)
                {
                    if (AllocateFoodRequest(out int inspectReq))
                    {
                        Food.Model.Execute(s.world, s.generation, inspectReq, FoodAction.Inspect, "berry", Food);
                        s.knowsBerry = true;
                    }
                }
                if (s.fruitStock <= 0) s.fruitStock = 1;
                if (AllocateFoodRequest(out int gatherReq))
                {
                    FoodReceipt receipt = Food.Model.Execute(s.world, s.generation, gatherReq, FoodAction.Gather, "berry", Food);
                    Record("food", receipt.code, accepted, result, receipt);
                    if (receipt.success)
                    {
                        var controls = Brain.GetComponent<NpcPlayerControls>() ?? FindFirstObjectByType<NpcPlayerControls>();
                        var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();
                        bool isLowHunger = s.satiety < 8500 || s.body.stomach <= 7000;
                        if (isLowHunger)
                        {
                            s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                            s.hydration = Mathf.Min(10000, s.hydration + 600);
                            s.satiety = Mathf.Min(10000, s.satiety + 1500);
                            Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 1500);
                            s.knowsMealBenefit = true;
                            if (string.IsNullOrEmpty(s.lastMealEvidence)) s.lastMealEvidence = s.generation + ".ate." + gatherReq;
                            s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
                            BoostPlaceAffinity("berry-grove", 15);
                            LastOutcome = "Gathered and immediately ate ripe sourfig berry to relieve hunger";
                            recentVerifiedOutcome = "gather berry succeeded; ate immediately";
                        }
                        else
                        {
                            if (moonbag != null && moonbag.CanStore)
                            {
                                moonbag.StoreFruit();
                                s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
                                BoostPlaceAffinity("berry-grove", 10);
                                LastOutcome = "Gathered ripe sourfig berry and stored in waist moonbag for later";
                                recentVerifiedOutcome = "gather berry succeeded; stored in moonbag";
                            }
                            else if (controls != null && controls.GetFreeHandCount() > 0)
                            {
                                controls.SpawnBerryInHand();
                                s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
                                BoostPlaceAffinity("berry-grove", 8);
                                LastOutcome = "Gathered ripe sourfig berry and carried in hand for later";
                                recentVerifiedOutcome = "gather berry succeeded; carried in hand";
                            }
                            else
                            {
                                BoostPlaceAffinity("berry-grove", 8);
                                LastOutcome = "Gathered ripe sourfig berry into carried stock for later";
                                recentVerifiedOutcome = "gather berry succeeded; carried stock";
                            }
                        }
                        FoodOutcomes++;
                        Food.HarvestBerry();
                        Food.SyncFruitVisual();
                        Persist();
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                        if (Diary != null)
                        {
                            Diary.AddEntry(Brain.Tick, "Food", "Gathered wild sourfig berries from the canyon scrub.");
                            Diary.UnlockMilestone("first-feast", Brain.Tick);
                        }
                    }
                    else
                    {
                        LastOutcome = "Failed to gather berry: " + receipt.code;
                        recentVerifiedOutcome = "gather berry failed: " + receipt.code;
                        BoostPlaceAffinity("berry-grove", -8);
                        var controls = Brain.GetComponent<NpcPlayerControls>() ?? FindFirstObjectByType<NpcPlayerControls>();
                        if (receipt.code == "inventory-full")
                        {
                            NpcPlayerControls.ReconcileCarriedFruit(s, Brain, controls);
                        }
                    }
                }
            }
            else
            {
                FoodAction kind=accepted.StartsWith("inspect")?FoodAction.Inspect:
                    accepted.StartsWith("gather")?FoodAction.Gather:accepted.StartsWith("eat")?FoodAction.Eat:FoodAction.Drink;
                string target=accepted.EndsWith("berry")?"berry":accepted.EndsWith("spring")?"spring":"inventory";
                if(!AllocateFoodRequest(out int foodRequest))return;
                var s=Food.Model.State;FoodReceipt receipt=Food.Model.Execute(s.world,s.generation,foodRequest,kind,target,Food);
                Record("food",receipt.code,accepted,result,receipt);
                if (receipt.success && kind == FoodAction.Eat)
                {
                    BoostPlaceAffinity("berry-grove", 12);
                    LastOutcome = "Meal +" + receipt.foodDelta + " energy / +" + receipt.waterDelta + " water [Drive reduced: Endorphin " + s.body.endorphin + "]";
                }
                else if (receipt.success && kind == FoodAction.Drink)
                {
                    BoostPlaceAffinity("freshwater-spring", 15);
                    LastOutcome = "Drink +" + receipt.waterDelta + " water [Drive reduced: Endorphin " + s.body.endorphin + "]";
                }
                else
                {
                    LastOutcome=receipt.success&&kind==FoodAction.Gather?"Gathered one observed fruit":
                        receipt.success&&kind==FoodAction.Inspect?"Observed resource; outcome unproven":receipt.code;
                }
                if(receipt.success){FoodOutcomes++;Food.SyncFruitVisual();Persist();}
                if(receipt.success)recentVerifiedOutcome=accepted+" succeeded";
            }
            nextRequestTick=Brain.Tick+25;
            if (Brain != null) Brain.LastResult = LastOutcome;
            SynthesizeGroundedNarrative(accepted);
            if (Brain != null && Brain.Log != null && !accepted.StartsWith("explore ") && !accepted.StartsWith("approach ") && accepted != "seek food")
            {
                Brain.Log.Record(Brain.Tick, "SURVIVAL", Brain.DescribePerception(), accepted, accepted, LastOutcome);
            }
        }
        public bool StepTick()
        {
            if(!Enabled)return false;
            if(Brain.MenuPaused||Brain.Possessed||!Brain.Running){Cancel("control-interruption");return false;}
            RememberCurrentWorld();
            var s=Food.Model.State;
            if (Brain.Tick % 50 == 0 && Diary != null)
            {
                if (Brain.transform.position.z < -100f && Mathf.Abs(Brain.transform.position.x) < 45f)
                    Diary.UnlockMilestone("waterfall-discovered", Brain.Tick);
                if (s.exploredCells != null && s.exploredCells.Count >= 1000)
                    Diary.UnlockMilestone("master-surveyor", Brain.Tick);
            }
            if(string.IsNullOrEmpty(s.survivalAuthorityEvidence) && !s.body.dead &&
                Brain.Actions!=null && Brain.Actions.Held==null && Brain.Actions.Deliveries>=3 &&
                Brain.Registry.Where(x=>x.Kind==NpcObjectKind.Item&&x.Permission)
                    .All(x=>!string.IsNullOrEmpty(x.DeliveredTo)))
            {
                s.survivalAuthorityEvidence=FoodModel.SurvivalAuthorityEvidence(s);
                Persist();
                if(!Enabled)return true;
                Record("authority","earned-after-three-live-deliveries",null,null);
            }
            if(s.body.dead)
            {
                if(deathAtTick<0){deathAtTick=Brain.Tick;Record("death","verified-physiology-cause",null,null);Persist();}
                Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
                if(Brain.Tick-deathAtTick>=50)
                {bool returned=TrySafeReturn();Record("return",returned?"verified-safe":"no-safe-refuge-placement",null,null);
                    if(returned)deathAtTick=-1;}
                return true;
            }
            if(mapAcceptanceRequested)
            {
                return StepMapAcceptanceDiagnostic();
            }
            if (Brain.Actor != null && (Brain.Actor.IsSubmerged || (Brain.Actor.IsSwimming && Brain.Actor.WaterDepth > 1.2f)))
            {
                if (routePurpose != "seek-shore" || route.Count == 0)
                {
                    Vector3 shore = FindNearestShore(Brain.transform.position);
                    route.Clear();
                    if (StartRoute(shore))
                    {
                        routePurpose = "seek-shore";
                        Status = "Submerged / seeking dry shore to prevent drowning";
                    }
                }
            }
            if(route.Count>0)return MoveRoute();
            if(pending!=null)
            {
                Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
                if(!pending.IsCompleted)return true;
                StarfallSurvivalThought.Result result;
                try{result=pending.GetAwaiter().GetResult();}catch(Exception){result=new StarfallSurvivalThought.Result{status="provider-unavailable"};}
                pending=null;cancellation.Dispose();cancellation=null;
                if(result.status=="parsed-awaiting-live-check")Execute(result.answer,result);
                else
                {
                    Record("model",result.status,result.answer,result);
                    var fallbackChoices=Eligible();
                    if(fallbackChoices!=null && fallbackChoices.Count>0)
                    {
                        Execute(fallbackChoices[0],new StarfallSurvivalThought.Result{status="fallback-rule-executed",answer=fallbackChoices[0]});
                        LastChoiceByModel=false;
                        Status="Survival rule-based choice: "+fallbackChoices[0];
                    }
                    else
                    {
                        nextRequestTick=Brain.Tick+50;
                    }
                }
                offered=null;return true;
            }
            if(Brain.Tick<nextRequestTick){Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;}
            offered=Eligible();
            if(offered.Count==0){Record("model","no-current-walkable-or-observed-options",null,null);nextRequestTick=Brain.Tick+100;return true;}
            if (!LocalModelEnabled || string.IsNullOrWhiteSpace(endpoint))
            {
                Execute(offered[0], new StarfallSurvivalThought.Result { status = "grounded-rule-executed", answer = offered[0] });
                LastChoiceByModel = false;
                Status = "Grounded survival mind: " + offered[0];
                return true;
            }
            if(modelRequestSequence==int.MaxValue)
            {Enabled=false;Status="Survival model request sequence exhausted";return true;}
            modelRequestSequence++;
            string verifiedDeathCause=VerifiedOwnDeathCause(s);
            string requestJson=StarfallSurvivalThought.BuildRequest(model,s.satiety,s.hydration,offered,
                s.carriedFruit,s.knowsMealBenefit&&!string.IsNullOrEmpty(s.lastMealEvidence),recentVerifiedOutcome,
                verifiedDeathCause);
            Record("model","request-issued",null,new StarfallSurvivalThought.Result{model=model,requestJson=requestJson});
            if(!Enabled)return true;
            cancellation=new CancellationTokenSource();
            pending=StarfallSurvivalThought.Request(endpoint,model,s.satiety,s.hydration,offered,cancellation.Token,
                s.carriedFruit,s.knowsMealBenefit&&!string.IsNullOrEmpty(s.lastMealEvidence),recentVerifiedOutcome,
                requestJson,verifiedDeathCause);
            Status="Local model deciding from live eligible observations";
            Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;
        }
        private bool StepMapAcceptanceDiagnostic()
        {
            if(pending!=null)
            {
                cancellation.Cancel();cancellation.Dispose();cancellation=null;pending=null;offered=null;
            }
            LastChoiceByModel=false;
            LastChoice="SCRIPTED_DIAGNOSTIC_NOT_MODEL";
            if(LastOutcome=="Walked to model-chosen place")LastOutcome="Walked to scripted diagnostic destination";
            if(recentVerifiedOutcome!=null&&recentVerifiedOutcome.Contains("model-chosen"))recentVerifiedOutcome="scripted destination reached";
            if(MapHud!=null&&!MapHud.Expanded)MapHud.Expanded=true;

            if(mapDiagStage==MapDiagStage.DonePass||mapDiagStage==MapDiagStage.DoneFail)
            {
                Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
                return true;
            }

            var s=Food.Model.State;
            if(string.IsNullOrEmpty(s.survivalAuthorityEvidence)&&!VerifiedScopedContinuation)
            {
                Status="Diagnostic: waiting for survival authority";
                return false;
            }

            if(mapDiagStage==MapDiagStage.Inactive)
            {
                mapDiagStage=MapDiagStage.Caching;
                mapDiagStartTick=Brain.Tick;
                mapStageStartTick=Brain.Tick;
                mapStages.Add("started");
                Status="Diagnostic: waiting to observe berry-food";
            }

            if(mapDiagStage==MapDiagStage.Caching)
            {
                if(Observed("berry-food",out var berryObs))
                {
                    cachedBerryPos=berryObs.position;
                    cachedBerryApproach=berryObs.approach;
                    mapInitialPos=Brain.transform.position;
                    initialPlacesCount=s.observedPlaces.Count;
                    initialLastHash=s.observedPlaces.Count>0?s.observedPlaces[s.observedPlaces.Count-1].hash:"";
                    initialFoodTick=s.tick;
                    mapStages.Add("cached-berry-observation");

                    bool found=false;
                    Vector3 depTarget=Vector3.zero;
                    for(float dist=16f;dist<=24f&&!found;dist+=2f)
                    {
                        for(int angle=0;angle<360;angle+=20)
                        {
                            float rad=angle*Mathf.Deg2Rad;
                            Vector3 cand=Brain.transform.position+new Vector3(Mathf.Cos(rad),0,Mathf.Sin(rad))*dist;
                            float bDist=Vector2.Distance(new Vector2(cand.x,cand.z),new Vector2(cachedBerryPos.x,cachedBerryPos.z));
                            if(bDist<15f)continue;
                            if(!Brain.TerrainNavigation.Walkable(cand,out var floor))continue;
                            var plan=Brain.TerrainNavigation.Plan(Brain.transform.position,floor);
                            if(plan!=null&&plan.Count>0)
                            {
                                depTarget=floor;
                                found=true;
                                break;
                            }
                        }
                    }
                    if(!found)
                    {
                        FinishMapDiagnostic(false,"no-walkable-departure-route");
                        return true;
                    }
                    departureDest=depTarget;
                    routePurpose="departing from berry";
                    if(!StartRoute(departureDest))
                    {
                        FinishMapDiagnostic(false,"start-departure-route-failed");
                        return true;
                    }
                    mapStages.Add("departing");
                    mapDiagStage=MapDiagStage.Departing;
                    mapStageStartTick=Brain.Tick;
                    Status="Diagnostic: departing from berry to clear LOS";
                    return MoveRoute();
                }
                if(Brain.Tick-mapStageStartTick>500)
                {
                    FinishMapDiagnostic(false,"timeout-waiting-berry-observation");
                    return true;
                }
                Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
                return true;
            }

            if(mapDiagStage==MapDiagStage.Departing)
            {
                if(Brain.Tick-mapStageStartTick>1500)
                {
                    FinishMapDiagnostic(false,"timeout-during-departure");
                    return true;
                }
                if(route.Count>0)return MoveRoute();

                float d=Vector2.Distance(new Vector2(Brain.transform.position.x,Brain.transform.position.z),
                    new Vector2(cachedBerryPos.x,cachedBerryPos.z));
                if(d<14f)
                {
                    FinishMapDiagnostic(false,"departure-insufficient-distance-"+d.ToString("F1"));
                    return true;
                }
                mapDiagStage=MapDiagStage.DepartedExcluded;
                mapStageStartTick=Brain.Tick;
                mapStages.Add("reached-departure-point");
                Status="Diagnostic: waiting for LOS exclusion";
                Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
                return true;
            }

            if(mapDiagStage==MapDiagStage.DepartedExcluded)
            {
                if(Brain.Tick-mapStageStartTick>300)
                {
                    FinishMapDiagnostic(false,"timeout-waiting-los-exclusion");
                    return true;
                }
                bool berrySeen=Observed("berry-food",out _);
                bool inVisible=visiblePlaceIds.Contains("berry-food");
                if(!berrySeen&&!inVisible&&s.tick>initialFoodTick)
                {
                    mapStages.Add("los-excluded-verified");
                    routePurpose="returning to berry";
                    if(!StartRoute(cachedBerryApproach))
                    {
                        bool planned=false;
                        for(float dx=-1.5f;dx<=1.5f&&!planned;dx+=0.5f)
                        for(float dz=-1.5f;dz<=1.5f&&!planned;dz+=0.5f)
                        {
                            Vector3 near=cachedBerryApproach+new Vector3(dx,0,dz);
                            if(Brain.TerrainNavigation.Walkable(near,out var fl)&&StartRoute(fl))
                                planned=true;
                        }
                        if(!planned)
                        {
                            FinishMapDiagnostic(false,"return-plan-failed");
                            return true;
                        }
                    }
                    mapStages.Add("returning");
                    mapDiagStage=MapDiagStage.Returning;
                    mapStageStartTick=Brain.Tick;
                    Status="Diagnostic: returning to cached berry approach";
                    return MoveRoute();
                }
                Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
                return true;
            }

            if(mapDiagStage==MapDiagStage.Returning)
            {
                if(Brain.Tick-mapStageStartTick>1500)
                {
                    FinishMapDiagnostic(false,"timeout-during-return");
                    return true;
                }
                if(route.Count>0)return MoveRoute();

                PlaceObservationEvent revisit=null;
                for(int i=s.observedPlaces.Count-1;i>=initialPlacesCount;i--)
                {
                    if(s.observedPlaces[i].id=="berry-food"&&s.observedPlaces[i].kind=="revisit")
                    {
                        revisit=s.observedPlaces[i];
                        break;
                    }
                }
                if(revisit!=null)
                {
                    bool chainValid=PlaceLedger.Valid(s);
                    if(!chainValid)
                    {
                        FinishMapDiagnostic(false,"revisit-chain-invalid");
                        return true;
                    }
                    mapFinalPos=Brain.transform.position;
                    mapStages.Add("revisit-verified");
                    FinishMapDiagnostic(true,null,revisit);
                    return true;
                }
                if(Brain.Tick-mapStageStartTick>1200)
                {
                    FinishMapDiagnostic(false,"revisit-event-not-recorded");
                    return true;
                }
                Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
                return true;
            }

            Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
            return true;
        }
        private void FinishMapDiagnostic(bool pass,string failReason,PlaceObservationEvent revisit=null)
        {
            var s=Food.Model.State;
            Persist();
            mapDiagStage=pass?MapDiagStage.DonePass:MapDiagStage.DoneFail;
            if(pass)mapStages.Add("summaryPASS");
            else mapStages.Add("failed-"+failReason);
            Status=pass?"Diagnostic summaryPASS: genuine map revisit verified":"Diagnostic FAIL: "+failReason;
            LastOutcome=Status;
            recentVerifiedOutcome=pass?"scripted diagnostic revisit verified":"scripted diagnostic failed";
            var summary=new MapSummary
            {
                status=pass?"summaryPASS":"FAIL",
                world=s.world,
                generation=s.generation,
                actor=s.actorId,
                startTick=mapDiagStartTick,
                endTick=Brain.Tick,
                startFoodTick=initialFoodTick,
                endFoodTick=s.tick,
                initialPosition=mapInitialPos,
                finalPosition=mapFinalPos,
                cachedBerryPosition=cachedBerryPos,
                cachedBerryApproach=cachedBerryApproach,
                departureDestination=departureDest,
                initialEventCount=initialPlacesCount,
                finalEventCount=s.observedPlaces.Count,
                initialLastHash=initialLastHash,
                revisitHash=revisit!=null?revisit.hash:"",
                revisitPreviousHash=revisit!=null?revisit.previousHash:"",
                revisitKind=revisit!=null?revisit.kind:"",
                hashChainValid=PlaceLedger.Valid(s),
                genuineRevisitVerified=pass,
                routeStages=new List<string>(mapStages)
            };
            if(!string.IsNullOrEmpty(mapAcceptanceDirectory)&&Directory.Exists(mapAcceptanceDirectory))
            {
                string json=JsonUtility.ToJson(summary,true);
                try
                {
                    File.WriteAllText(Path.Combine(mapAcceptanceDirectory,"map-acceptance-summary.json"),json);
                    File.WriteAllText(Path.Combine(mapAcceptanceDirectory,"summary.json"),json);
                    File.WriteAllText(Path.Combine(mapAcceptanceDirectory,"summary.txt"),summary.status+"\n");
                }
                catch(Exception ex)
                {
                    Debug.LogError("Failed to write map diagnostic summary: "+ex.Message);
                }
            }
            if(pass)Debug.Log($"STARFALL_MAP_ACCEPTANCE: summaryPASS world={summary.world} events={summary.finalEventCount} hash={summary.revisitHash}");
            else Debug.LogError($"STARFALL_MAP_ACCEPTANCE: FAIL {failReason}");
        }
    }
}
