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
        public int FoodOutcomes { get; internal set; }
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

        // ==========================================
        // Grounded Natural Language Command Controller
        // ==========================================
        public sealed class ActiveCommandStep
        {
            public string Title;
            public float TimeoutSeconds;
            public float ElapsedSeconds;
            public Func<StarfallSurvivalAutonomy, bool> Execute;
        }

        private List<ActiveCommandStep> activeCommandSteps;
        private int activeCommandStepIndex;
        private System.Threading.CancellationTokenSource activeInterpretationCts;
        public bool IsInterpreting => activeInterpretationCts != null;
        public bool HasExecutableSteps => activeCommandSteps != null && activeCommandStepIndex < activeCommandSteps.Count;
        public bool HasActiveCommand => HasExecutableSteps || IsInterpreting;
        public string ActiveCommandTitle { get; private set; } = "";
        public string ActiveCommandCurrentStep => HasExecutableSteps ? activeCommandSteps[activeCommandStepIndex].Title : (IsInterpreting ? "Interpreting natural language request..." : "");
        public int ActiveCommandStepIndex => activeCommandStepIndex;
        public int ActiveCommandTotalSteps => activeCommandSteps != null ? activeCommandSteps.Count : 0;
        public IReadOnlyList<string> ActiveCommandStepTitles
        {
            get
            {
                if (activeCommandSteps == null) return Array.Empty<string>();
                var list = new string[activeCommandSteps.Count];
                for (int i = 0; i < activeCommandSteps.Count; i++) list[i] = activeCommandSteps[i].Title;
                return list;
            }
        }
        public float ActiveCommandProgress => ActiveCommandTotalSteps > 0 ? Mathf.Clamp01((float)activeCommandStepIndex / ActiveCommandTotalSteps) : (IsInterpreting ? 0.05f : 0f);
        public int ActiveCommandGeneration { get; private set; }
        public string ActiveCommandStatus { get; private set; } = "idle";
        public string LastCommandReceipt { get; private set; } = "none";
        private RiverFishSchool.RiverFishInstance commandTargetFish;
        private string commandTargetFishId;
        private Vector3 commandChosenBank;
        private Vector3 commandCastTarget;
        private string commandRodAcquiredCode;

        private void Awake()
        {
            if (Brain == null) Brain = GetComponent<NpcAutonomy>() ?? GetComponentInParent<NpcAutonomy>();
            if (Brain != null && Brain.Survival == null) Brain.Survival = this;
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
        public string SavePath => savePath;
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
        public string recentVerifiedOutcome;
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
                bool chkRestored = FoodConsumptionBridge.TryRestoreFromAuthoritativeCheckpoint(Brain, out string chkMsg);
                if (!chkRestored && !string.Equals(chkMsg, "no-authoritative-pointer", StringComparison.Ordinal))
                {
                    // Review 23:28: Distinguish clean uninitialized repo from corrupt/indeterminate acknowledged pointer.
                    // Corrupt or invalid acknowledged combined checkpoints must fail closed, strictly preventing standalone fallback.
                    throw new InvalidOperationException("authoritative-combined-checkpoint-corrupt-fail-closed: " + chkMsg);
                }
                bool prior = chkRestored || (savePath != null && File.Exists(savePath));
                if (!chkRestored && prior && !Food.Model.Load(savePath, Brain.InstanceWorldId, IntegratedFoodRuntime.Generation))
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
                    if(!Brain.TerrainNavigation.Walkable(remembered,out var floor))
                    {
                        if (!TryFindWalkableNear(remembered, 8f, out floor))
                        {
                            floor = new Vector3(2.5f, CoastalTerrain.Height(2.5f, -10f), -10f);
                        }
                    }
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

        private bool TryFindWalkableNear(Vector3 center, float maxRadius, out Vector3 floor)
        {
            floor = center;
            if (Brain == null || Brain.TerrainNavigation == null) return false;
            if (Brain.TerrainNavigation.Walkable(center, out floor)) return true;

            for (float r = 1.0f; r <= maxRadius; r += 1.5f)
            {
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * (Mathf.PI * 2f / 8f);
                    Vector3 offset = new Vector3(Mathf.Cos(angle) * r, 0, Mathf.Sin(angle) * r);
                    Vector3 testP = center + offset;
                    if (Brain.TerrainNavigation.Walkable(testP, out floor))
                    {
                        return true;
                    }
                }
            }
            return false;
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
            if (HasActiveCommand) CancelActiveCommand(reason);
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
                    if (observation != null && observation.kind == NpcObjectKind.Item &&
                        observation.seenAtTick >= Brain.Tick - 10)
                    {
                        string role = observation.id.Contains("basket") ? "basket" : observation.id.Contains("rod") ? "rod" : null;
                        if (role != null) ItemObservationMemory.Observe(s, observation.id, role,
                            observation.approach, observation.available, observation.permission, observation.seenAtTick);
                    }
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
            TryGetHeldFoodOrCatch(out var heldFoodNi, out var heldPhys, out bool isHeldFoodLeft);
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

                bool holdingFish = heldPhys != null && (heldPhys.itemTypeId == "food-river-fish" || heldPhys.itemTypeId == "food-river-carp");
                bool hasFishInPerception = Brain.Perception != null && Brain.Perception.Current != null &&
                    Brain.Perception.Current.Any(x => x != null && x.kind == NpcObjectKind.Item && (x.id.Contains("river-fish") || x.id.Contains("river-carp")));
                bool nearShore = Brain.transform.position.y <= CoastalWater.Level + 3.2f || nearestCrab != null;

                var fishing = Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>();
                bool isHoldingRod = fishing != null && fishing.IsHoldingFishingRod(out _);
                var nearestGroundRod = (Brain.Perception != null && Brain.Perception.Current != null)
                    ? Brain.Perception.Current.FirstOrDefault(x => x != null && x.kind == NpcObjectKind.Item && x.id.Contains("rod") && x.permission && x.available)
                    : null;
                float rodDist = nearestGroundRod != null ? Vector3.Distance(Brain.transform.position, nearestGroundRod.position) : 999f;

                if (fishing != null && isHoldingRod)
                {
                    if (fishing.State == FishingState.Bite)
                    {
                        foodChoices.Insert(0, "strike & reel fish");
                    }
                    else if (fishing.State == FishingState.Floating || fishing.State == FishingState.Nibble)
                    {
                        foodChoices.Add("wait for fish bite");
                    }
                    else if ((lowProtein || s.satiety < 8500) && !holdingFish && (inRiver || nearShore || hasFishInPerception))
                    {
                        foodChoices.Add("cast fishing rod");
                    }
                }
                else if (!isHoldingRod && nearestGroundRod != null && freeHands > 0 && (lowProtein || s.satiety < 8500) && !holdingFish)
                {
                    if (rodDist <= 2.2f) foodChoices.Add("equip fishing rod");
                    else foodChoices.Add("approach fishing rod");
                }
                else
                {
                    if (!holdingFish && (inRiver || hasFishInPerception))
                    {
                        if (hasClubInHand) foodChoices.Add("strike fish with club");
                        else foodChoices.Add("catch fish");
                    }

                    if (nearShore && !foodChoices.Contains("catch crab") && !foodChoices.Contains("strike crab with club") && crabDist <= 2.2f)
                    {
                        if (hasClubInHand) foodChoices.Add("strike crab with club");
                        else foodChoices.Add("catch crab");
                    }
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
            if (heldPhys == null)
            {
                TryGetHeldFoodOrCatch(out heldFoodNi, out heldPhys, out isHeldFoodLeft);
            }
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

                // Stash harvested fish or protein into camp storage basket if not starving
                bool isCatch = heldPhys.itemTypeId == "food-river-fish" || heldPhys.itemTypeId == "food-river-carp" || heldPhys.itemTypeId == "food-cooked-fish";
                var nearestBasket = (Brain.Perception != null && Brain.Perception.Current != null)
                    ? Brain.Perception.Current.FirstOrDefault(x => x != null && x.kind == NpcObjectKind.Item && x.id.Contains("basket") && x.permission && x.available)
                    : null;
                float basketDist = nearestBasket != null ? Vector3.Distance(Brain.transform.position, nearestBasket.position) : 999f;
                if (isCatch && nearestBasket != null && s.satiety >= 6500)
                {
                    if (basketDist <= 2.4f) foodChoices.Add("store catch in basket");
                    else foodChoices.Add("approach basket");
                }
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
            // Review Item 3: Life-critical dehydration strictly precedes fishing and all other actions!
            // When dehydrated, prioritize drinking and safely cancel/release active fishing.
            if (urgent == null && (s.hydration < 3500 || (s.hydration < 4500 && (foodChoices.Contains("drink river") || foodChoices.Contains("drink spring") || foodChoices.Contains("drink water")))))
            {
                if (foodChoices.Contains("drink river")) urgent = "drink river";
                else if (foodChoices.Contains("drink spring")) urgent = "drink spring";
                else if (foodChoices.Contains("drink water")) urgent = "drink water";
                else if (foodChoices.Contains("seek water")) urgent = "seek water";

                if (urgent != null)
                {
                    var fComp = Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>();
                    if (fComp != null && fComp.IsFishingActive)
                    {
                        fComp.CancelFishing("urgent-hydration-quench-thirst");
                    }
                }
            }

            // Active bite window has instantaneous execution urgency (when not critically dehydrated)
            if (urgent == null && foodChoices.Contains("strike & reel fish"))
            {
                urgent = "strike & reel fish";
            }
            if (urgent == null && foodChoices.Contains("wait for fish bite"))
            {
                urgent = "wait for fish bite";
            }
            // Critical protein hunger drive
            if (urgent == null && lowProtein)
            {
                if (foodChoices.Contains("feast roasted catch")) urgent = "feast roasted catch";
                else if (foodChoices.Contains("feast catch")) urgent = "feast catch";
                else if (foodChoices.Contains("eat catch")) urgent = "eat catch";
                else if (foodChoices.Contains("cast fishing rod")) urgent = "cast fishing rod";
                else if (foodChoices.Contains("equip fishing rod")) urgent = "equip fishing rod";
                else if (foodChoices.Contains("approach fishing rod")) urgent = "approach fishing rod";
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
            if (urgent == null && foodChoices.Contains("store catch in basket"))
                urgent = "store catch in basket";
            else if (urgent == null && foodChoices.Contains("approach basket"))
                urgent = "approach basket";
            if (urgent == null && s.hydration < 7500)
            {
                if (foodChoices.Contains("drink water")) urgent = "drink water";
                else if (foodChoices.Contains("drink river")) urgent = "drink river";
                else if (foodChoices.Contains("drink spring")) urgent = "drink spring";
                else if (foodChoices.Contains("seek water")) urgent = "seek water";
            }
            if (urgent == null && (s.satiety < 8500 || s.body.stomach <= 7000))
            {
                if (foodChoices.Contains("cast fishing rod")) urgent = "cast fishing rod";
                else if (foodChoices.Contains("equip fishing rod")) urgent = "equip fishing rod";
                else if (foodChoices.Contains("approach fishing rod")) urgent = "approach fishing rod";
                else if (foodChoices.Contains("gather berry")) urgent = "gather berry";
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
            if (Brain == null || Brain.TerrainNavigation == null) return false;
            var planned=Brain.TerrainNavigation.Plan(Brain.transform.position,destination);
            if(planned==null||planned.Count==0)return false;
            route.Clear();foreach(var waypoint in planned)route.Enqueue(waypoint);
            routeStartTick=Brain.Tick;routeOrigin=Brain.transform.position;
            if (HasExecutableSteps && Brain.Actor != null)
            {
                float metres = 0f;
                var previous = routeOrigin;
                foreach (var waypoint in planned) { metres += Vector3.Distance(previous, waypoint); previous = waypoint; }
                var step = activeCommandSteps[activeCommandStepIndex];
                // Budget the actual path around the river, not the straight-line
                // distance. Keep a hard deadline even if repeated replans occur.
                if (step.TimeoutSeconds > 0f)
                    step.TimeoutSeconds = Mathf.Min(180f, Mathf.Max(step.TimeoutSeconds,
                        step.ElapsedSeconds + metres / Mathf.Max(.5f, Brain.Actor.WalkSpeed) * 1.5f + 10f));
            }
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
        public void Persist()
        {
            if(savePath==null)return;
            try
            {
                if (Brain != null)
                {
                    bool chkOk = FoodConsumptionBridge.TryCommitCoordinatedCheckpoint(Brain, "autonomy-persist", out string chkCode);
                    if (!chkOk)
                    {
                        Record("save", "checkpoint-sync-refused-" + chkCode, null, null);
                        // If coordinated checkpoint commit fails, fail closed: do not advance standalone pointer conflictingly
                        Enabled = false; Status = "Survival checkpoint sync failed closed"; Cancel("save-failed");
                        return;
                    }
                }
                Food.Model.Save(savePath);
                MapHud?.NotifyStateChanged();
            }
            catch(Exception error)
            {
                Record("save","scoped-save-failed-"+error.GetType().Name,null,null);
                Enabled=false;Status="Survival save failed closed";Cancel("save-failed");
            }
        }
        public void SyncFoodRequestHighWatermark(int highWatermark)
        {
            if (highWatermark > request) request = highWatermark;
        }
        private bool AllocateFoodRequest(out int allocated)
        {
            allocated=0;
            int highWatermark = Food != null && Food.Model != null && Food.Model.State != null
                ? Math.Max(request, Food.Model.State.lastRequest)
                : request;
            if(!TryNextFoodRequest(highWatermark,out int next))
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
                else if (accepted.EndsWith("rod"))
                {
                    NpcObservation bestRod = null;
                    float bestRodDist = float.MaxValue;
                    if (Brain.Perception != null && Brain.Perception.Current != null)
                    {
                        foreach (var obs in Brain.Perception.Current)
                        {
                            if (obs != null && obs.kind == NpcObjectKind.Item && obs.id.Contains("rod") &&
                                obs.permission && obs.available && obs.seenAtTick >= Brain.Tick - 15)
                            {
                                float d = Vector3.Distance(Brain.transform.position, obs.position);
                                if (d < bestRodDist)
                                {
                                    bestRodDist = d;
                                    bestRod = obs;
                                }
                            }
                        }
                    }
                    Vector3 targetDest = bestRod != null && bestRod.approach != Vector3.zero ? bestRod.approach : (bestRod != null ? bestRod.position : Vector3.zero);
                    if (bestRod == null || (!StartRoute(targetDest) && !StartRoute(bestRod.position)))
                    {
                        routePurpose = null;
                        Record("route", "live-target-route-rejected", accepted, result);
                    }
                    else
                    {
                        LastOutcome = "Approaching river fishing rod on ground";
                        recentVerifiedOutcome = "approach fishing rod started";
                    }
                }
                else if (accepted.EndsWith("basket"))
                {
                    NpcObservation bestBasket = null;
                    float bestBasketDist = float.MaxValue;
                    if (Brain.Perception != null && Brain.Perception.Current != null)
                    {
                        foreach (var obs in Brain.Perception.Current)
                        {
                            if (obs != null && obs.kind == NpcObjectKind.Item && obs.id.Contains("basket") &&
                                obs.permission && obs.available && obs.seenAtTick >= Brain.Tick - 15)
                            {
                                float d = Vector3.Distance(Brain.transform.position, obs.position);
                                if (d < bestBasketDist)
                                {
                                    bestBasketDist = d;
                                    bestBasket = obs;
                                }
                            }
                        }
                    }
                    Vector3 targetDest = bestBasket != null && bestBasket.approach != Vector3.zero ? bestBasket.approach : (bestBasket != null ? bestBasket.position : Vector3.zero);
                    if (bestBasket == null || (!StartRoute(targetDest) && !StartRoute(bestBasket.position)))
                    {
                        routePurpose = null;
                        Record("route", "live-target-route-rejected", accepted, result);
                    }
                    else
                    {
                        LastOutcome = "Approaching storage basket to store catch";
                        recentVerifiedOutcome = "approach basket started";
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
                var fComp = Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>();
                if (fComp != null && fComp.IsFishingActive) fComp.CancelFishing("urgent-hydration-quench-thirst");

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
                var fComp = Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>();
                if (fComp != null && fComp.IsFishingActive) fComp.CancelFishing("urgent-hydration-quench-thirst");

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
            else if (accepted == "drink river" || accepted == "drink spring")
            {
                var fComp = Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>();
                if (fComp != null && fComp.IsFishingActive) fComp.CancelFishing("urgent-hydration-quench-thirst");

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
            else if (accepted == "cast fishing rod")
            {
                var fishing = Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>();
                if (fishing != null && fishing.CanStartCast(Brain.transform.position, out var targetWater, out _))
                {
                    fishing.StartCast(targetWater);
                    LastOutcome = "Cast fishing rod out into freshwater river run";
                    recentVerifiedOutcome = "cast fishing rod succeeded";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
                else
                {
                    LastOutcome = "Attempted cast but shore angle was unsuitable";
                    recentVerifiedOutcome = "cast fishing rod failed";
                }
            }
            else if (accepted == "wait for fish bite")
            {
                LastOutcome = "Watching bobber float on clear river pool; awaiting bite";
                recentVerifiedOutcome = "wait for fish bite ongoing";
            }
            else if (accepted == "strike & reel fish")
            {
                var fishing = Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>();
                if (fishing != null && fishing.StrikeAndReel(out string species, out float scale, out string receipt))
                {
                    LastOutcome = (species == "food-river-carp")
                        ? "Struck with keen reflex; hooked a trophy Golden River Carp and reeling line!"
                        : "Struck with keen reflex; hooked a whiskered freshwater Catfish and reeling line!";
                    recentVerifiedOutcome = "hooked fish reeling";
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                }
                else
                {
                    LastOutcome = "Reeled line but fish got away";
                    recentVerifiedOutcome = "strike & reel fish missed";
                }
            }
            else if (accepted == "equip fishing rod")
            {
                var nearestGroundRod = (Brain.Perception != null && Brain.Perception.Current != null)
                    ? Brain.Perception.Current.FirstOrDefault(x => x != null && x.kind == NpcObjectKind.Item && x.id.Contains("rod") && x.permission && x.available)
                    : null;
                if (nearestGroundRod != null)
                {
                    var rodNi = Brain.Registry != null ? Brain.Registry.FirstOrDefault(x => x != null && x.StableId == nearestGroundRod.id) : null;
                    if (rodNi != null && Brain.Actions != null)
                    {
                        Brain.Actions.HoldItemDirect(rodNi, false);
                        LastOutcome = "Equipped handcrafted river fishing rod in hand";
                        recentVerifiedOutcome = "equip fishing rod succeeded";
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                    }
                }
            }
            else if (accepted == "store catch in basket")
            {
                var nearestBasket = (Brain.Perception != null && Brain.Perception.Current != null)
                    ? Brain.Perception.Current.FirstOrDefault(x => x != null && x.kind == NpcObjectKind.Item && x.id.Contains("basket") && x.permission && x.available)
                    : null;
                if (nearestBasket != null && TryGetHeldFoodOrCatch(out var heldFoodNi, out var heldPhys, out bool isLeft))
                {
                    if (Brain.Actions != null && Brain.TryAllocateRequestId(out int req))
                    {
                        var storeResult = Brain.Actions.Store(req, nearestBasket.id, heldPhys.itemId);
                        if (storeResult.success)
                        {
                            LastOutcome = "Safely stored fresh catch into camp basket for future sustenance";
                            recentVerifiedOutcome = "store catch in basket succeeded";
                            if (Brain.Actor != null) Brain.Actor.Gesture();
                        }
                        else
                        {
                            LastOutcome = "Store refused: " + storeResult.code;
                        }
                    }
                }
            }
            else if (accepted == "catch fish" || accepted == "strike fish with club")
            {
                var school = RiverFishSchool.Instance ?? FindFirstObjectByType<RiverFishSchool>();
                RiverFishSchool.RiverFishInstance caughtFish = null;
                if (school != null && school.TryReserveFishNear(Brain.transform.position, 6.0f, out caughtFish))
                {
                    var fishing = Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>();
                    bool transferred = false;
                    string transferCode = null;
                    if (fishing != null)
                    {
                        transferred = fishing.TryTransferCatch(caughtFish, out transferCode);
                    }
                    if (transferred)
                    {
                        school.CompleteCatch(caughtFish);
                        var carry = Brain.GetComponentInChildren<HunterClubCarry>();
                        bool usedClub = accepted == "strike fish with club" || (carry != null && !carry.Stowed);
                        string speciesName = (caughtFish.physicalItem != null && caughtFish.physicalItem.itemTypeId == "food-river-carp") ? "Golden River Carp" : "whiskered river barber";
                        LastOutcome = usedClub ? $"Struck {speciesName} in shallows with hunter's club" : $"Caught fresh {speciesName} in shallows";
                        FoodOutcomes++;
                        huntingPursuitFailures = 0;
                        Persist();
                        recentVerifiedOutcome = usedClub ? "strike fish with club succeeded" : "catch fish succeeded";
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                        if (Diary != null) Diary.AddEntry(Brain.Tick, "Hunting", usedClub
                            ? $"Brandished hunter's club and struck a {speciesName} cruising the shallows. Harvested rich freshwater protein."
                            : $"Caught and retrieved a fresh {speciesName} from the canyon shallows.");
                    }
                    else
                    {
                        school.ReleaseReservation(caughtFish);
                        LastOutcome = $"Attempted to harvest fish in shallows but transfer failed ({transferCode})";
                        recentVerifiedOutcome = "catch fish transfer failed";
                    }
                }
                else
                {
                    LastOutcome = "Swiped at shallows but the fish darted away into deep water";
                    recentVerifiedOutcome = "catch fish missed";
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
                if (TryGetHeldFoodOrCatch(out var foodNi, out var heldPhys, out bool isLeft))
                {
                    if (FoodConsumptionBridge.TryConsumeHeldFood(Brain, foodNi, heldPhys, isLeft, "autonomous-feast-meal", out string code))
                    {
                        LastOutcome = "Feasted on savory roasted meal [Protein/Energy boosted, drive reduced]";
                        FoodOutcomes++;
                        recentVerifiedOutcome = "feast catch succeeded";
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                    }
                    else
                    {
                        LastOutcome = "Feast refused: " + code;
                    }
                }
            }
            else if (accepted == "eat catch")
            {
                if (TryGetHeldFoodOrCatch(out var foodNi, out var heldPhys, out bool isLeft) &&
                    (heldPhys.itemTypeId == "food-river-fish" || heldPhys.itemTypeId == "food-river-carp" || heldPhys.itemTypeId == "food-protein-crab" || heldPhys.itemTypeId == "food-wolf-meat"))
                {
                    if (FoodConsumptionBridge.TryConsumeHeldFood(Brain, foodNi, heldPhys, isLeft, "autonomous-eat-catch", out string code))
                    {
                        LastOutcome = "Ate fresh protein catch [Hunger & protein replenished]";
                        FoodOutcomes++;
                        recentVerifiedOutcome = "eat catch succeeded";
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                    }
                    else
                    {
                        LastOutcome = "Consume refused: " + code;
                    }
                }
            }
            else if (accepted == "eat fruit")
            {
                var s = Food.Model.State;
                var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();

                if (TryGetHeldFoodOrCatch(out var foodNi, out var heldPhys, out bool isLeft) &&
                    (heldPhys.itemTypeId == "food-sourfig-berry" || heldPhys.itemTypeId == "fruit"))
                {
                    if (FoodConsumptionBridge.TryConsumeHeldFood(Brain, foodNi, heldPhys, isLeft, "autonomous-eat-fruit", out string code))
                    {
                        BoostPlaceAffinity("berry-grove", 12);
                        LastOutcome = "Ate ripe held sourfig berry [Energy boosted, drive reduced]";
                        FoodOutcomes++;
                        recentVerifiedOutcome = "eat fruit succeeded";
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                    }
                    else
                    {
                        LastOutcome = "Consume berry refused: " + code;
                    }
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
            if (HasActiveCommand)
            {
                return StepActiveCommand();
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
        }

        public void RecordCatchLanded(string species, float scale, string transferCode)
        {
            FoodOutcomes++;
            huntingPursuitFailures = 0;
            LastOutcome = (species == "food-river-carp")
                ? "Landed a magnificent Golden River Carp safely into hand!"
                : "Landed a sturdy freshwater Catfish safely into hand!";
            recentVerifiedOutcome = "land catch succeeded";
            if (Brain != null && Brain.Actor != null) Brain.Actor.Gesture();
            if (Diary != null)
            {
                Diary.AddEntry(Brain != null ? Brain.Tick : 1, "Fishing", (species == "food-river-carp")
                    ? "Timed the bite perfectly on the deep pool and landed a glistening Golden River Carp."
                    : "Patiently worked the river run and landed a sturdy freshwater catfish.");
            }
            Persist();
        }

        public bool TryGetHeldFoodOrCatch(out NpcInteractable foodNi, out PhysicalItem foodPhys, out bool isLeft)
        {
            foodNi = null;
            foodPhys = null;
            isLeft = false;
            if (Brain == null || Brain.Actions == null) return false;

            // Check Right Hand for food or catch
            if (Brain.Actions.HeldRight != null)
            {
                var phys = Brain.Actions.HeldRight.GetComponent<PhysicalItem>();
                if (phys != null && IsFoodOrCatchTypeId(phys.itemTypeId))
                {
                    foodNi = Brain.Actions.HeldRight;
                    foodPhys = phys;
                    isLeft = false;
                    return true;
                }
            }

            // Check Left Hand for food or catch
            if (Brain.Actions.HeldLeft != null)
            {
                var phys = Brain.Actions.HeldLeft.GetComponent<PhysicalItem>();
                if (phys != null && IsFoodOrCatchTypeId(phys.itemTypeId))
                {
                    foodNi = Brain.Actions.HeldLeft;
                    foodPhys = phys;
                    isLeft = true;
                    return true;
                }
            }

            // Fallback: any non-tool physical item in right hand
            if (Brain.Actions.HeldRight != null)
            {
                var phys = Brain.Actions.HeldRight.GetComponent<PhysicalItem>();
                if (phys != null && phys.itemTypeId != "tool-fishing-rod" && phys.itemTypeId != "tool-club")
                {
                    foodNi = Brain.Actions.HeldRight;
                    foodPhys = phys;
                    isLeft = false;
                    return true;
                }
            }

            // Fallback: any non-tool physical item in left hand
            if (Brain.Actions.HeldLeft != null)
            {
                var phys = Brain.Actions.HeldLeft.GetComponent<PhysicalItem>();
                if (phys != null && phys.itemTypeId != "tool-fishing-rod" && phys.itemTypeId != "tool-club")
                {
                    foodNi = Brain.Actions.HeldLeft;
                    foodPhys = phys;
                    isLeft = true;
                    return true;
                }
            }

            return false;
        }

        public static bool IsFoodOrCatchTypeId(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return false;
            return typeId == "food-river-fish" ||
                   typeId == "food-river-carp" ||
                   typeId == "food-cooked-fish" ||
                   typeId == "food-protein-crab" ||
                   typeId == "food-cooked-crab" ||
                   typeId == "food-wolf-meat" ||
                   typeId == "food-cooked-meat" ||
                   typeId == "food-sourfig-berry" ||
                   typeId == "fruit";
        }

        public string LocalEndpoint => endpoint;
        public string LocalModel => model;

        // ==========================================
        // Grounded Command Execution & Helper Methods
        // ==========================================
        private bool PreparePlayerDirective()
        {
            if (Application.isPlaying && !Enabled)
            {
                LastCommandReceipt = "command-refused-survival-unavailable: " + Status;
                ActiveCommandStatus = LastCommandReceipt;
                return false;
            }
            Cancel("player-directive-takes-priority");
            if (Brain != null)
            {
                if (Brain.Possessed) Brain.SetPossession(false);
                Brain.Running = true;
                Brain.ManualDirection = Vector3.zero;
                Brain.OptionalPlanner?.Cancel("player-directive");
            }
            return true;
        }

        public bool SubmitNaturalLanguageCommand(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var interp = StarfallSemanticInterpreter.InterpretDeterministic(text, ActiveCommandGeneration);
            if (interp.Action == SemanticActionKind.Cancel)
            {
                CancelActiveCommand("directive-cancel");
                return true;
            }
            if (interp.Action == SemanticActionKind.Rejected)
            {
                LastCommandReceipt = interp.Reason;
                ActiveCommandStatus = "Command rejected: " + interp.Reason;
                return false;
            }

            if (!TryBuildStepsForAction(interp.Action, text, out var title, out var steps, out var failReason))
            {
                LastCommandReceipt = failReason;
                ActiveCommandStatus = failReason;
                return false;
            }

            if (!PreparePlayerDirective()) return false;
            ActiveCommandGeneration++;
            CancelActiveCommand("new-command-submitted");

            activeCommandSteps = steps;
            activeCommandStepIndex = 0;
            ActiveCommandTitle = title;
            ActiveCommandStatus = $"Executing: {title} - Step 1/{steps.Count}: {steps[0].Title}";
            LastCommandReceipt = "command-started: " + title;
            if (Brain != null && Brain.Log != null)
                Brain.Log.Record(Brain.Tick, "COMMAND", Brain.DescribePerception(), title, "Natural language command accepted", text);
            return true;
        }

        public async System.Threading.Tasks.Task<bool> SubmitNaturalLanguageCommandAsync(
            string text,
            string ep = null,
            string mdl = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            ActiveCommandGeneration++;
            int myGen = ActiveCommandGeneration;
            ActiveCommandStatus = $"Interpreting directive: \"{text}\"...";

            // Cancel any old active steps so old action does not keep executing during new interpretation
            if (activeCommandSteps != null)
            {
                var fishing = Brain != null ? (Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>()) : null;
                if (fishing != null && fishing.IsFishingActive)
                {
                    fishing.CancelFishing("superseded-by-new-interpretation");
                }
                route.Clear();
                routePurpose = null;
                activeCommandSteps = null;
                activeCommandStepIndex = 0;
            }

            string useEndpoint = !string.IsNullOrEmpty(ep) ? ep : endpoint;
            string useModel = !string.IsNullOrEmpty(mdl) ? mdl : model;

            if (activeInterpretationCts != null)
            {
                activeInterpretationCts.Cancel();
                activeInterpretationCts.Dispose();
                activeInterpretationCts = null;
            }
            activeInterpretationCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var linkedToken = activeInterpretationCts.Token;

            SemanticInterpretationResult interp;
            try
            {
                interp = await StarfallSemanticInterpreter.InterpretAsync(text, useEndpoint, useModel, myGen, linkedToken);
            }
            finally
            {
                if (activeInterpretationCts != null && activeInterpretationCts.Token == linkedToken)
                {
                    activeInterpretationCts.Dispose();
                    activeInterpretationCts = null;
                }
            }

            // Stale generation discard: if user cancelled or submitted newer command while model ran, drop response
            if (myGen != ActiveCommandGeneration)
            {
                return false;
            }

            if (interp.Action == SemanticActionKind.Cancel)
            {
                CancelActiveCommand("directive-cancel");
                return true;
            }
            if (interp.Action == SemanticActionKind.Rejected)
            {
                LastCommandReceipt = interp.Reason;
                ActiveCommandStatus = "Directive rejected: " + interp.Reason;
                return false;
            }

            if (!TryBuildStepsForAction(interp.Action, text, out var title, out var steps, out var failReason))
            {
                LastCommandReceipt = failReason;
                ActiveCommandStatus = failReason;
                return false;
            }

            if (!PreparePlayerDirective()) return false;
            CancelActiveCommand("new-command-submitted");

            activeCommandSteps = steps;
            activeCommandStepIndex = 0;
            ActiveCommandTitle = title;
            ActiveCommandStatus = $"Executing: {title} - Step 1/{steps.Count}: {steps[0].Title}";
            LastCommandReceipt = "command-started: " + title;
            if (Brain != null && Brain.Log != null)
                Brain.Log.Record(Brain.Tick, "COMMAND", Brain.DescribePerception(), title, "Natural language directive accepted via " + interp.Source, text);
            return true;
        }

        private bool TryBuildStepsForAction(SemanticActionKind action, string originalText, out string title, out List<ActiveCommandStep> steps, out string failReason)
        {
            title = "";
            steps = new List<ActiveCommandStep>();
            failReason = null;

            switch (action)
            {
                case SemanticActionKind.GoToRiver:
                    title = "Go to river";
                    AddGoToRiverSteps(steps);
                    return true;

                case SemanticActionKind.CatchFish:
                    title = "Catch a fish";
                    AddCatchSteps(steps);
                    return true;

                case SemanticActionKind.RoastCatch:
                    title = "Roast catch";
                    AddRoastSteps(steps);
                    return true;

                case SemanticActionKind.StoreFishInBasket:
                    title = "Store fish in basket";
                    AddStoreSteps(steps);
                    return true;

                case SemanticActionKind.EatCatch:
                    title = "Eat catch";
                    AddEatSteps(steps);
                    return true;

                case SemanticActionKind.CatchThenEat:
                    title = "Catch then eat";
                    AddCatchSteps(steps);
                    AddRoastSteps(steps);
                    AddEatSteps(steps);
                    return true;

                case SemanticActionKind.CatchThenStore:
                    title = "Catch then store";
                    AddCatchSteps(steps);
                    AddStoreSteps(steps);
                    return true;

                case SemanticActionKind.GoRiverThenCatch:
                    title = "Go to river then catch";
                    AddGoToRiverSteps(steps);
                    AddCatchSteps(steps);
                    return true;

                case SemanticActionKind.CatchThenRoast:
                    title = "Catch then roast";
                    AddCatchSteps(steps);
                    AddRoastSteps(steps);
                    return true;

                default:
                    failReason = "unrecognized-command: " + originalText;
                    return false;
            }
        }

        private void AddGoToRiverSteps(List<ActiveCommandStep> steps)
        {
            steps.Add(new ActiveCommandStep
            {
                Title = "Navigate to river bank",
                TimeoutSeconds = 25f,
                Execute = self =>
                {
                    Vector3 target = self.FindRiverBankTarget(self.Brain.transform.position);
                    if (target == Vector3.zero)
                    {
                        self.ActiveCommandStatus = "Cannot find river: no river seen or remembered in territory";
                        self.CancelActiveCommand("command-failed-river-unknown");
                        return true;
                    }
                    float dist = Vector3.Distance(self.Brain.transform.position, target);
                    if (dist <= 3.0f)
                    {
                        self.ActiveCommandStatus = "Arrived at river bank";
                        return true;
                    }
                    if (self.route.Count == 0 || self.routePurpose != "go-to-river")
                    {
                        self.StartRoute(target);
                        self.routePurpose = "go-to-river";
                    }
                    self.ActiveCommandStatus = $"Navigating to river ({dist:F1}m)...";
                    return false;
                }
            });
        }

        public void CancelActiveCommand(string reason = "cancelled-by-user")
        {
            ActiveCommandGeneration++;
            if (activeInterpretationCts != null)
            {
                activeInterpretationCts.Cancel();
                activeInterpretationCts.Dispose();
                activeInterpretationCts = null;
            }
            if (commandTargetFish != null)
            {
                if (RiverFishSchool.Instance != null)
                {
                    RiverFishSchool.Instance.ReleaseReservation(commandTargetFish);
                }
                else
                {
                    commandTargetFish.isReserved = false;
                }
                commandTargetFish = null;
            }
            commandTargetFishId = null;
            commandChosenBank = Vector3.zero;
            commandCastTarget = Vector3.zero;
            commandRodAcquiredCode = null;

            if (activeCommandSteps != null)
            {
                var fishing = Brain != null ? (Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>()) : null;
                if (fishing != null && fishing.IsFishingActive)
                {
                    fishing.CancelFishing(reason);
                }
                route.Clear();
                routePurpose = null;
                activeCommandSteps = null;
                activeCommandStepIndex = 0;
            }
            LastCommandReceipt = "command-cancelled: " + reason;
            ActiveCommandStatus = "Command cancelled: " + reason;
            if (Brain != null && Brain.Log != null)
                Brain.Log.Record(Brain.Tick, "COMMAND", Brain.DescribePerception(), ActiveCommandTitle, "Command cancelled", reason);
        }

        public bool StepActiveCommand()
        {
            if (!HasExecutableSteps)
            {
                if (IsInterpreting)
                {
                    if (Brain != null && Brain.Actor != null) Brain.Actor.Step(Vector3.zero, NpcAutonomy.StepSeconds);
                    return true;
                }
                return false;
            }

            var currentStep = activeCommandSteps[activeCommandStepIndex];
            currentStep.ElapsedSeconds += NpcAutonomy.StepSeconds;

            if (currentStep.TimeoutSeconds > 0 && currentStep.ElapsedSeconds >= currentStep.TimeoutSeconds)
            {
                string timeoutCode = $"command-timeout-{currentStep.Title.ToLowerInvariant().Replace(' ', '-')}";
                CancelActiveCommand(timeoutCode);
                LastCommandReceipt = timeoutCode;
                if (Brain != null && Brain.Actor != null) Brain.Actor.Step(Vector3.zero, NpcAutonomy.StepSeconds);
                return true;
            }

            bool stepDone = currentStep.Execute(this);
            if (activeCommandSteps == null)
            {
                if (Brain != null && Brain.Actor != null) Brain.Actor.Step(Vector3.zero, NpcAutonomy.StepSeconds);
                return true;
            }

            if (stepDone)
            {
                route.Clear();
                routePurpose = null;
                activeCommandStepIndex++;
                if (activeCommandStepIndex >= activeCommandSteps.Count)
                {
                    LastCommandReceipt = "command-completed: " + ActiveCommandTitle;
                    ActiveCommandStatus = "Command completed: " + ActiveCommandTitle;
                    if (Brain != null && Brain.Log != null)
                        Brain.Log.Record(Brain.Tick, "COMMAND", Brain.DescribePerception(), ActiveCommandTitle, "Command completed", LastCommandReceipt);
                    activeCommandSteps = null;
                    activeCommandStepIndex = 0;
                    if (Brain != null && Brain.Actor != null) Brain.Actor.Step(Vector3.zero, NpcAutonomy.StepSeconds);
                    return true;
                }
                else
                {
                    ActiveCommandStatus = $"Executing: {ActiveCommandTitle} - Step {activeCommandStepIndex + 1}/{ActiveCommandTotalSteps}: {activeCommandSteps[activeCommandStepIndex].Title}";
                }
            }

            if (route.Count > 0)
            {
                return MoveRoute();
            }

            if (Brain != null && Brain.Actor != null) Brain.Actor.Step(Vector3.zero, NpcAutonomy.StepSeconds);
            return true;
        }

        /// <summary>
        /// Finds a dry riverbank position strictly through grounded inhabitant knowledge:
        /// 1. Current visual perception of water/river/fish
        /// 2. Remembered places in FoodState.observedPlaces
        /// 3. Explored river cells in FoodState.exploredCells
        /// 4. Direct proximity to river water (<12m)
        /// Avoids omniscient world scans or registry iteration.
        /// </summary>
        public Vector3 FindRiverBankTarget(Vector3 fromPos)
        {
            // 1. Current visual perception
            if (Brain != null && Brain.Perception != null && Brain.Perception.Current != null)
            {
                for (int i = 0; i < Brain.Perception.Current.Count; i++)
                {
                    var obs = Brain.Perception.Current[i];
                    if (obs != null && (obs.id.Contains("river") || obs.id.Contains("water") || obs.id.Contains("fish")))
                    {
                        if (TryFindDryBankNear(obs.position, out var bank)) return bank;
                    }
                }
            }

            // 2. Remembered places
            var food = Food != null ? Food.Model.State : null;
            if (food != null && food.observedPlaces != null && food.observedPlaces.Count > 0)
            {
                float bestPlaceDist = float.MaxValue;
                Vector3 bestPlace = Vector3.zero;
                for (int i = 0; i < food.observedPlaces.Count; i++)
                {
                    var p = food.observedPlaces[i];
                    if (p != null && (p.id.Contains("river") || p.id.Contains("water") || p.id.Contains("spring") || (p.observedType != null && p.observedType.Contains("river"))))
                    {
                        float d = Vector3.Distance(fromPos, p.position);
                        if (d < bestPlaceDist)
                        {
                            bestPlaceDist = d;
                            bestPlace = p.position;
                        }
                    }
                }
                if (bestPlace != Vector3.zero && TryFindDryBankNear(bestPlace, out var bank))
                {
                    return bank;
                }
            }

            // 3. Explored river cells
            if (food != null && food.exploredCells != null && food.exploredCells.Count > 0)
            {
                float bestCellDist = float.MaxValue;
                Vector3 bestCellPos = Vector3.zero;
                for (int i = 0; i < food.exploredCells.Count; i++)
                {
                    var c = food.exploredCells[i];
                    if (c == null) continue;
                    float wx = c.x * 3f;
                    float wz = c.z * 3f;
                    float wy = CoastalTerrain.Height(wx, wz);
                    if (CoastalTerrain.IsFreshwaterRiver(wx, wz, wy, CoastalWater.Level))
                    {
                        float d = Vector2.Distance(new Vector2(fromPos.x, fromPos.z), new Vector2(wx, wz));
                        if (d < bestCellDist)
                        {
                            bestCellDist = d;
                            bestCellPos = new Vector3(wx, wy, wz);
                        }
                    }
                }
                if (bestCellPos != Vector3.zero && TryFindDryBankNear(bestCellPos, out var bank))
                {
                    return bank;
                }
            }

            // 4. Local proximity check (<12m to river)
            float curY = CoastalTerrain.Height(fromPos.x, fromPos.z);
            if (CoastalTerrain.IsFreshwaterRiver(fromPos.x, fromPos.z, curY, CoastalWater.Level) ||
                CoastalTerrain.IsFreshwaterRiver(fromPos.x + 6f, fromPos.z, CoastalTerrain.Height(fromPos.x + 6f, fromPos.z), CoastalWater.Level) ||
                CoastalTerrain.IsFreshwaterRiver(fromPos.x - 6f, fromPos.z, CoastalTerrain.Height(fromPos.x - 6f, fromPos.z), CoastalWater.Level))
            {
                if (TryFindDryBankNear(fromPos, out var bank)) return bank;
            }

            // River unknown to inhabitant
            return Vector3.zero;
        }

        /// <summary>
        /// Locates grounded river water position known to the inhabitant via perception,
        /// remembered places, explored cells, or immediate proximity.
        /// Returns Vector3.zero if the river is unknown to the inhabitant.
        /// </summary>
        public Vector3 FindGroundedRiverWaterTarget(Vector3 fromPos)
        {
            // 1. Current visual perception of river/water/fish
            if (Brain != null && Brain.Perception != null && Brain.Perception.Current != null)
            {
                Vector3 nearest = Vector3.zero;
                float nearestDistance = float.MaxValue;
                for (int i = 0; i < Brain.Perception.Current.Count; i++)
                {
                    var obs = Brain.Perception.Current[i];
                    if (obs != null && !string.IsNullOrEmpty(obs.id) && !obs.id.Contains("rod") &&
                        (obs.id.Contains("river") || obs.id.Contains("water") || obs.id.Contains("fish")) &&
                        CoastalTerrain.Height(obs.position.x, obs.position.z) < CoastalWater.CurrentLevel - 0.25f)
                    {
                        float distance = Vector3.SqrMagnitude(obs.position - fromPos);
                        if (distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            nearest = new Vector3(obs.position.x, CoastalWater.CurrentLevel, obs.position.z);
                        }
                    }
                }
                if (nearest != Vector3.zero) return nearest;
            }

            // 2. Remembered places
            var food = Food != null ? Food.Model.State : null;
            if (food != null && food.observedPlaces != null && food.observedPlaces.Count > 0)
            {
                float bestPlaceDist = float.MaxValue;
                Vector3 bestPlace = Vector3.zero;
                for (int i = 0; i < food.observedPlaces.Count; i++)
                {
                    var p = food.observedPlaces[i];
                    if (p != null && (p.id.Contains("river") || p.id.Contains("water") || p.id.Contains("spring") || (p.observedType != null && p.observedType.Contains("river"))))
                    {
                        float d = Vector3.Distance(fromPos, p.position);
                        if (d < bestPlaceDist)
                        {
                            bestPlaceDist = d;
                            bestPlace = p.position;
                        }
                    }
                }
                if (bestPlace != Vector3.zero)
                {
                    float cx = CoastalTerrain.RiverCenterlineX(bestPlace.z);
                    if (CoastalTerrain.Height(cx, bestPlace.z) < CoastalWater.CurrentLevel - .25f)
                        return new Vector3(cx, CoastalWater.CurrentLevel, bestPlace.z);
                }
            }

            // 3. Explored river cells
            if (food != null && food.exploredCells != null && food.exploredCells.Count > 0)
            {
                float bestCellDist = float.MaxValue;
                Vector3 bestCellPos = Vector3.zero;
                for (int i = 0; i < food.exploredCells.Count; i++)
                {
                    var c = food.exploredCells[i];
                    if (c == null) continue;
                    float wx = c.x * 3f;
                    float wz = c.z * 3f;
                    float wy = CoastalTerrain.Height(wx, wz);
                    if (wy < CoastalWater.CurrentLevel - .25f && CoastalTerrain.IsFreshwaterRiver(wx, wz, wy, CoastalWater.CurrentLevel))
                    {
                        float d = Vector2.Distance(new Vector2(fromPos.x, fromPos.z), new Vector2(wx, wz));
                        if (d < bestCellDist)
                        {
                            bestCellDist = d;
                            bestCellPos = new Vector3(wx, CoastalWater.CurrentLevel, wz);
                        }
                    }
                }
                if (bestCellPos != Vector3.zero) return bestCellPos;
            }

            // 4. Sense actual nearby water, not the broad river-region label:
            // dry explored cells in that region are not evidence of a water surface.
            Vector3 localWater = Vector3.zero;
            float localDistance = float.MaxValue;
            float localRadius = Mathf.Min(25f, Brain?.Perception != null ? Brain.Perception.Radius : 15f);
            for (float dx = -localRadius; dx <= localRadius; dx += 2f)
            for (float dz = -localRadius; dz <= localRadius; dz += 2f)
            {
                float distance = dx * dx + dz * dz;
                if (distance > localRadius * localRadius || distance >= localDistance) continue;
                float x = fromPos.x + dx, z = fromPos.z + dz;
                float y = CoastalTerrain.Height(x, z);
                if (y >= CoastalWater.CurrentLevel - .25f || !CoastalTerrain.IsFreshwaterRiver(x, z, y, CoastalWater.CurrentLevel)) continue;
                Vector3 point = new Vector3(x, CoastalWater.CurrentLevel, z);
                Vector3 eye = fromPos + Vector3.up * 1.6f;
                Vector3 ray = point + Vector3.up * .05f - eye;
                if (Physics.Raycast(eye, ray.normalized, ray.magnitude, (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore)) continue;
                localWater = point;
                localDistance = distance;
            }
            return localWater;
        }

        private bool TryFindDryBankNear(Vector3 waterCenter, out Vector3 bank)
        {
            bank = Vector3.zero;
            for (float r = 2.5f; r <= 18.0f; r += 1.0f)
            {
                for (float ang = 0f; ang < Mathf.PI * 2f; ang += Mathf.PI / 8f)
                {
                    Vector3 test = waterCenter + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    float y = CoastalTerrain.Height(test.x, test.z);
                    if (y >= CoastalWater.CurrentLevel + 0.15f)
                    {
                        if (Brain != null && Brain.TerrainNavigation != null)
                        {
                            if (Brain.TerrainNavigation.Walkable(new Vector3(test.x, y, test.z), out Vector3 floor) && floor.y >= CoastalWater.Level + 0.15f)
                            {
                                bank = floor;
                                return true;
                            }
                        }
                        else
                        {
                            test.y = y;
                            bank = test;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool EnsureRightHandFreeForRod()
        {
            if (Brain == null || Brain.Actions == null) return false;
            if (Brain.Actions.HeldRight == null) return true;

            var held = Brain.Actions.HeldRight;
            var carry = Brain.GetComponentInChildren<HunterClubCarry>();

            // Is right hand holding a club?
            if (held.StableId.ToLowerInvariant().Contains("club") || (carry != null && held.transform == carry.Club))
            {
                // Authoritative club handling: deliberately stow to back, swap to left hand, or drop
                if (carry != null)
                {
                    Brain.Actions.HoldItemDirect(null, false);
                    carry.SetStowed(true, true);
                    carry.AttachToBack();
                    if (Brain.Log != null)
                        Brain.Log.Record(Brain.Tick, "DECISION", Brain.DescribePerception(), "stow club", "Equip rod", "Hunter club stowed to back");
                    return true;
                }
                if (Brain.Actions.HeldLeft == null)
                {
                    Brain.Actions.HoldItemDirect(null, false);
                    Brain.Actions.HoldItemDirect(held, true);
                    return true;
                }
                var dropResult = Brain.ExecutePlayerAction(NpcActionKind.Drop, held.StableId);
                return dropResult.success;
            }

            // Is right hand holding a fruit/berry that can be stored in moonbag?
            var phys = held.GetComponent<PhysicalItem>();
            if (phys != null && (phys.itemTypeId == "food-sourfig-berry" || phys.itemTypeId == "fruit"))
            {
                var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();
                if (moonbag != null && moonbag.CanStore)
                {
                    moonbag.StoreFruit();
                    Brain.Actions.HoldItemDirect(null, false);
                    return true;
                }
            }

            // Otherwise drop item authoritatively
            var dropRes = Brain.ExecutePlayerAction(NpcActionKind.Drop, held.StableId);
            return dropRes.success;
        }

        private bool EnsureLeftHandFreeForLanding()
        {
            if (Brain == null || Brain.Actions == null) return false;
            if (Brain.Actions.HeldLeft == null) return true;

            var held = Brain.Actions.HeldLeft;
            var carry = Brain.GetComponentInChildren<HunterClubCarry>();

            if (held.StableId.ToLowerInvariant().Contains("club") || (carry != null && held.transform == carry.Club))
            {
                if (carry != null)
                {
                    Brain.Actions.HoldItemDirect(null, true);
                    carry.SetStowed(true, true);
                    carry.AttachToBack();
                    if (Brain.Log != null)
                        Brain.Log.Record(Brain.Tick, "DECISION", Brain.DescribePerception(), "stow club", "Free left hand", "Hunter club stowed to back");
                    return true;
                }
                var dropResult = Brain.ExecutePlayerAction(NpcActionKind.Drop, held.StableId);
                return dropResult.success;
            }

            var phys = held.GetComponent<PhysicalItem>();
            if (phys != null && (phys.itemTypeId == "food-sourfig-berry" || phys.itemTypeId == "fruit"))
            {
                var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();
                if (moonbag != null && moonbag.CanStore)
                {
                    moonbag.StoreFruit();
                    Brain.Actions.HoldItemDirect(null, true);
                    return true;
                }
            }

            var dropRes = Brain.ExecutePlayerAction(NpcActionKind.Drop, held.StableId);
            return dropRes.success;
        }

        public Vector3 CommandCastingBank => commandChosenBank;

        private void AddCatchSteps(List<ActiveCommandStep> steps)
        {
            Vector3? searchOrigin = null;
            int searchWaypoint = 0, searchTurns = 0;
            steps.Add(new ActiveCommandStep
            {
                Title = "Equip fishing rod",
                TimeoutSeconds = 180f,
                Execute = self =>
                {
                    // If already holding fishing rod in right hand:
                    if (self.Brain.Actions != null && self.Brain.Actions.HeldRight != null &&
                        (self.Brain.Actions.HeldRight.StableId.Contains("rod") ||
                         self.Brain.Actions.HeldRight.GetComponent<FishingRodItem>() != null ||
                         (self.Brain.Actions.HeldRight.GetComponent<PhysicalItem>() != null && self.Brain.Actions.HeldRight.GetComponent<PhysicalItem>().itemTypeId == FishingRodItem.ItemTypeId)))
                    {
                        self.EnsureLeftHandFreeForLanding();
                        self.commandRodAcquiredCode = "already-holding-rod";
                        self.ActiveCommandStatus = "Holding fishing rod";
                        return true;
                    }

                    // If rod is held in left hand, swap to right hand!
                    if (self.Brain.Actions != null && self.Brain.Actions.HeldLeft != null &&
                        (self.Brain.Actions.HeldLeft.StableId.Contains("rod") ||
                         self.Brain.Actions.HeldLeft.GetComponent<FishingRodItem>() != null ||
                         (self.Brain.Actions.HeldLeft.GetComponent<PhysicalItem>() != null && self.Brain.Actions.HeldLeft.GetComponent<PhysicalItem>().itemTypeId == FishingRodItem.ItemTypeId)))
                    {
                        var rodItem = self.Brain.Actions.HeldLeft;
                        self.Brain.Actions.HoldItemDirect(null, true);
                        self.EnsureRightHandFreeForRod();
                        self.Brain.Actions.HoldItemDirect(rodItem, false);
                        self.EnsureLeftHandFreeForLanding();
                        self.commandRodAcquiredCode = "swapped-rod-to-right-hand";
                        self.ActiveCommandStatus = "Swapped fishing rod to right hand";
                        return true;
                    }

                    // Make sure right hand is free to hold the rod
                    if (!self.EnsureRightHandFreeForRod())
                    {
                        self.ActiveCommandStatus = "Hands full: drop or stow items before fishing";
                        self.CancelActiveCommand("command-failed-hands-full");
                        return true;
                    }

                    // Search for accessible fishing rod:
                    NpcObservation groundRod = null;
                    if (self.Brain.Perception != null && self.Brain.Perception.Current != null)
                    {
                        groundRod = self.Brain.Perception.Current.FirstOrDefault(x => x != null && x.kind == NpcObjectKind.Item && x.id.Contains("rod") && x.permission && x.available);
                    }
                    if (groundRod == null)
                    {
                        var food = self.Food != null ? self.Food.Model.State : null;
                        if (food != null)
                        {
                            var remembered = ItemObservationMemory.Nearest(food, "rod", self.Brain.transform.position);
                            if (remembered != null)
                            {
                                groundRod = new NpcObservation { id = remembered.id, position = remembered.position, approach = remembered.position };
                            }
                        }
                    }

                    if (groundRod == null)
                    {
                        // Search with ordinary local vision, never hidden registry coordinates.
                        // The search is bounded to four nearby rings and reachable dry ground.
                        if (self.Brain.Actor != null && self.Brain.TerrainNavigation != null && self.Brain.Perception != null)
                        {
                            if (!searchOrigin.HasValue) searchOrigin = self.Brain.transform.position;
                            self.ActiveCommandStatus = "Looking for a fishing rod on nearby ground";
                            if (self.route.Count > 0 && self.routePurpose == "search-fishing-rod") return false;
                            if (searchTurns < 8)
                            {
                                self.Brain.transform.Rotate(Vector3.up, 45f);
                                self.Brain.Perception.Sense(self.Brain.Tick);
                                searchTurns++;
                                return false;
                            }
                            while (searchWaypoint < 32)
                            {
                                int probe = searchWaypoint++;
                                float radius = 12f * (1 + probe / 8);
                                Vector3 destination = searchOrigin.Value + Quaternion.Euler(0, -45f * (probe % 8), 0) * Vector3.forward * radius;
                                var nav = self.Brain.TerrainNavigation;
                                if (!nav.IsWithinSafePerimeter(destination) || !nav.TryGround(destination, out float height, out _) ||
                                    height < CoastalWater.CurrentLevel + .15f || !nav.Walkable(destination, out _)) continue;
                                destination.y = height;
                                var path = nav.Plan(self.Brain.transform.position, destination);
                                if (path == null || path.Count == 0 || Vector3.Distance(path.Last(), destination) > 1.25f) continue;
                                self.route.Clear();
                                foreach (var point in path) self.route.Enqueue(point);
                                self.routeStartTick = self.Brain.Tick;
                                self.routeOrigin = self.Brain.transform.position;
                                self.routePurpose = "search-fishing-rod";
                                searchTurns = 0;
                                return false;
                            }
                        }
                        self.ActiveCommandStatus = "Cannot find fishing rod: no rod seen or remembered in territory";
                        self.CancelActiveCommand("command-failed-no-rod-available");
                        return true;
                    }

                    float dist = Vector3.Distance(self.Brain.transform.position, groundRod.position);
                    if (dist <= 2.2f)
                    {
                        NpcInteractable rNi = null;
                        if (self.Brain.Actions != null && self.Brain.Actions.TryGetRegisteredInteractable(groundRod.id, out var regNi))
                        {
                            rNi = regNi;
                        }
                        if (rNi == null)
                        {
                            var pItems = UnityEngine.Object.FindObjectsByType<PhysicalItem>(FindObjectsSortMode.None);
                            for (int pi = 0; pi < pItems.Length; pi++)
                            {
                                if (pItems[pi] != null && pItems[pi].itemId == groundRod.id)
                                {
                                    rNi = pItems[pi].GetComponent<NpcInteractable>();
                                    break;
                                }
                            }
                        }

                        if (rNi != null && self.Brain.Actions != null)
                        {
                            self.Brain.Actions.HoldItemDirect(rNi, false);
                            self.EnsureLeftHandFreeForLanding();
                            self.commandRodAcquiredCode = "equipped-ground-rod";
                            self.ActiveCommandStatus = "Equipped fishing rod";
                            return true;
                        }
                    }
                    else
                    {
                        if (self.route.Count == 0 || self.routePurpose != "approach-rod")
                        {
                            self.StartRoute(groundRod.position);
                            self.routePurpose = "approach-rod";
                        }
                        self.ActiveCommandStatus = $"Approaching rod ({dist:F1}m)...";
                        return false;
                    }

                    self.ActiveCommandStatus = "Searching for fishing rod...";
                    return false;
                }
            });

            steps.Add(new ActiveCommandStep
            {
                Title = "Select active fish & calculate casting bank",
                TimeoutSeconds = 35f,
                Execute = self =>
                {
                    var school = RiverFishSchool.Instance ?? UnityEngine.Object.FindFirstObjectByType<RiverFishSchool>();

                    // 1. Check if any active fish is perceived right now
                    List<RiverFishSchool.RiverFishInstance> candidateFish = new List<RiverFishSchool.RiverFishInstance>();
                    if (school != null && school.ActiveFish != null && self.Brain.Perception != null && self.Brain.Perception.Current != null)
                    {
                        for (int i = 0; i < school.ActiveFish.Count; i++)
                        {
                            var f = school.ActiveFish[i];
                            if (f == null || f.gameObject == null || !f.gameObject.activeSelf || f.isReserved) continue;
                            string fishStableId = f.interactable != null ? f.interactable.StableId : (f.gameObject != null ? f.gameObject.name : null);
                            if (string.IsNullOrEmpty(fishStableId)) continue;

                            bool perceived = self.Brain.Perception.Current.Any(p => p != null && p.id == fishStableId);
                            if (perceived)
                            {
                                candidateFish.Add(f);
                            }
                        }
                    }

                    // 2. If no fish currently perceived, inhabitant routes to known river water to observe
                    if (candidateFish.Count == 0)
                    {
                        Vector3 riverWater = self.FindGroundedRiverWaterTarget(self.Brain.transform.position);
                        if (riverWater == Vector3.zero)
                        {
                            self.ActiveCommandStatus = "Cannot find river: no river seen or remembered in territory";
                            self.CancelActiveCommand("command-failed-river-unknown");
                            return true;
                        }

                        if (!self.TryFindDryBankNear(riverWater, out Vector3 riverBank))
                        {
                            if (self.Brain != null && self.Brain.TerrainNavigation != null)
                            {
                                riverBank = self.Brain.TerrainNavigation.FindNearestRiverBank(riverWater);
                            }
                        }

                        if (riverBank == Vector3.zero || riverBank.y < CoastalWater.Level + 0.15f)
                        {
                            self.ActiveCommandStatus = "No reachable dry bank near known river water";
                            self.CancelActiveCommand("command-failed-no-reachable-bank");
                            return true;
                        }

                        float distToBank = Vector3.Distance(self.Brain.transform.position, riverBank);
                        if (distToBank > 3.2f)
                        {
                            if (self.route.Count == 0 || self.routePurpose != "approach-river-to-observe")
                            {
                                if (!self.StartRoute(riverBank))
                                {
                                    self.CancelActiveCommand("command-failed-no-reachable-bank");
                                    return true;
                                }
                                self.routePurpose = "approach-river-to-observe";
                            }
                            self.ActiveCommandStatus = $"Seeking river to spot active fish ({distToBank:F1}m)...";
                            return false;
                        }

                        // Arrived at river bank: orient toward river water and sense
                        Vector3 fwd = riverWater - self.Brain.transform.position;
                        fwd.y = 0;
                        if (fwd.sqrMagnitude > 0.01f) self.Brain.transform.rotation = Quaternion.LookRotation(fwd);
                        if (self.Brain.Perception != null) self.Brain.Perception.Sense(self.Brain.Tick);

                        // Re-query perceived active fish after sensing at bank
                        if (school != null && school.ActiveFish != null && self.Brain.Perception != null && self.Brain.Perception.Current != null)
                        {
                            for (int i = 0; i < school.ActiveFish.Count; i++)
                            {
                                var f = school.ActiveFish[i];
                                if (f == null || f.gameObject == null || !f.gameObject.activeSelf || f.isReserved) continue;
                                string fishStableId = f.interactable != null ? f.interactable.StableId : (f.gameObject != null ? f.gameObject.name : null);
                                if (string.IsNullOrEmpty(fishStableId)) continue;

                                bool perceived = self.Brain.Perception.Current.Any(p => p != null && p.id == fishStableId);
                                if (perceived)
                                {
                                    candidateFish.Add(f);
                                }
                            }
                        }
                    }

                    if (candidateFish.Count == 0)
                    {
                        self.ActiveCommandStatus = "No active fish visible in river run";
                        self.CancelActiveCommand("command-failed-no-fish-perceived");
                        return true;
                    }

                    // Sort candidates by proximity to inhabitant
                    candidateFish.Sort((a, b) =>
                    {
                        float da = Vector3.Distance(self.Brain.transform.position, a.gameObject.transform.position);
                        float db = Vector3.Distance(self.Brain.transform.position, b.gameObject.transform.position);
                        return da.CompareTo(db);
                    });

                    // Find first perceived fish with a reachable dry casting bank
                    for (int i = 0; i < candidateFish.Count; i++)
                    {
                        var fish = candidateFish[i];
                        Vector3 fishPos = fish.gameObject.transform.position;
                        if (self.Brain.TerrainNavigation != null &&
                            self.Brain.TerrainNavigation.TryFindCastingBankForFish(self.Brain.transform.position, fishPos, out Vector3 bankFloor))
                        {
                            self.commandTargetFish = fish;
                            self.commandTargetFish.isReserved = true;
                            self.commandTargetFishId = fish.interactable != null ? fish.interactable.StableId : (fish.gameObject != null ? fish.gameObject.name : "river-fish");
                            self.commandChosenBank = bankFloor;
                            self.commandCastTarget = fishPos;
                            self.commandCastTarget.y = CoastalWater.CurrentLevel;

                            self.ActiveCommandStatus = $"Targeted {self.commandTargetFishId} at casting bank";
                            if (self.Brain.Log != null)
                                self.Brain.Log.Record(self.Brain.Tick, "COMMAND", self.Brain.DescribePerception(), self.ActiveCommandTitle, "Fish targeted", $"Target: {self.commandTargetFishId} at {self.commandChosenBank}");
                            return true;
                        }
                    }

                    self.ActiveCommandStatus = "No reachable dry casting bank found for perceived fish";
                    self.CancelActiveCommand("command-failed-no-reachable-bank");
                    return true;
                }
            });

            steps.Add(new ActiveCommandStep
            {
                Title = "Route to casting bank",
                TimeoutSeconds = 30f,
                Execute = self =>
                {
                    if (self.commandChosenBank == Vector3.zero)
                    {
                        self.CancelActiveCommand("command-failed-no-reachable-bank");
                        return true;
                    }

                    float dist = Vector2.Distance(new Vector2(self.Brain.transform.position.x, self.Brain.transform.position.z),
                        new Vector2(self.commandChosenBank.x, self.commandChosenBank.z));
                    if (dist <= .55f)
                    {
                        self.ActiveCommandStatus = "In position at casting bank";
                        return true;
                    }

                    if (self.route.Count == 0 || self.routePurpose != "route-to-casting-bank")
                    {
                        self.StartRoute(self.commandChosenBank);
                        self.routePurpose = "route-to-casting-bank";
                    }
                    self.ActiveCommandStatus = $"Routing to casting bank ({dist:F1}m)...";
                    return false;
                }
            });

            steps.Add(new ActiveCommandStep
            {
                Title = "Face fish & cast fishing line",
                TimeoutSeconds = 45f,
                Execute = self =>
                {
                    if (self.commandTargetFish == null || self.commandTargetFish.gameObject == null || !self.commandTargetFish.gameObject.activeSelf)
                    {
                        self.CancelActiveCommand("command-failed-target-fish-lost");
                        return true;
                    }

                    // Finish a bank adjustment before casting; range alone must not
                    // interrupt the route while crossing shallow water.
                    if (self.route.Count > 0 && self.routePurpose == "adjust-casting-bank") return false;

                    // Rotate smoothly toward fish
                    Vector3 fwd = self.commandCastTarget - self.Brain.transform.position;
                    fwd.y = 0;
                    if (fwd.sqrMagnitude > 0.01f)
                    {
                        self.Brain.transform.rotation = Quaternion.LookRotation(fwd);
                    }

                    // Re-observe from the bank. A swimming target may have moved
                    // during travel; prefer a currently visible fish in casting
                    // range instead of chasing one individual across the river.
                    self.Brain.Perception?.Sense(self.Brain.Tick);
                    var visibleFish = RiverFishSchool.Instance?.ActiveFish
                        .Where(f => f?.gameObject != null && f.gameObject.activeSelf &&
                            (!f.isReserved || f == self.commandTargetFish) &&
                            self.Brain.Perception != null && self.Brain.Perception.Current.Any(p => p.id == f.interactable?.StableId) &&
                            RiverFishSchool.CanFishInRiver(self.Brain.transform.position, f.gameObject.transform.position, out _))
                        .OrderBy(f => (f.gameObject.transform.position - self.Brain.transform.position).sqrMagnitude).FirstOrDefault();
                    if (visibleFish != null)
                    {
                        self.commandTargetFish.isReserved = false;
                        self.commandTargetFish = visibleFish;
                        visibleFish.isReserved = true;
                        self.commandTargetFishId = visibleFish.interactable.StableId;
                        self.commandCastTarget = visibleFish.gameObject.transform.position;
                        self.commandCastTarget.y = CoastalWater.CurrentLevel;
                    }

                    var fishing = self.Brain.GetComponent<FishingInteraction>() ?? self.Brain.GetComponentInChildren<FishingInteraction>();
                    if (fishing == null)
                    {
                        self.CancelActiveCommand("command-failed-fishing-component-missing");
                        return true;
                    }

                    if (fishing.IsFishingActive) return true;

                    float castDistance = Vector2.Distance(new Vector2(self.Brain.transform.position.x, self.Brain.transform.position.z),
                        new Vector2(self.commandCastTarget.x, self.commandCastTarget.z));
                    bool unsafeBank = CoastalTerrain.Height(self.Brain.transform.position.x, self.Brain.transform.position.z) <
                        CoastalWater.CurrentLevel + .15f;
                    if (castDistance < 2.25f || castDistance > 18f || unsafeBank || (self.Brain.Actor != null && self.Brain.Actor.IsSwimming))
                    {
                        if (self.route.Count > 0 && self.routePurpose == "adjust-casting-bank") return false;
                        if (self.Brain.TerrainNavigation != null && self.Brain.TerrainNavigation.TryFindCastingBankForFish(
                            self.Brain.transform.position, self.commandCastTarget, out var updatedBank) && self.StartRoute(updatedBank))
                        {
                            self.commandChosenBank = updatedBank;
                            self.routePurpose = "adjust-casting-bank";
                            self.ActiveCommandStatus = "Fish moved: approaching a safe casting position";
                            return false;
                        }
                        self.CancelActiveCommand("command-failed-no-reachable-bank");
                        return true;
                    }

                    if (visibleFish == null)
                    {
                        self.ActiveCommandStatus = "Watching for a visible fish in casting range";
                        return false;
                    }
                    if (fishing.StartCast(self.commandCastTarget, self.commandTargetFish))
                    {
                        self.ActiveCommandStatus = $"Cast fishing line toward {self.commandTargetFishId}";
                        if (self.Brain.Actor != null) self.Brain.Actor.Gesture();
                        if (self.Brain.Log != null)
                            self.Brain.Log.Record(self.Brain.Tick, "COMMAND", self.Brain.DescribePerception(), self.ActiveCommandTitle, "Cast line", $"Target: {self.commandTargetFishId}");
                        return true;
                    }

                    self.CancelActiveCommand("command-failed-cast-rejected: " + fishing.LastReceipt);
                    return true;
                }
            });

            steps.Add(new ActiveCommandStep
            {
                Title = "Wait for fish bite & strike",
                TimeoutSeconds = 90f,
                Execute = self =>
                {
                    var fishing = self.Brain.GetComponent<FishingInteraction>() ?? self.Brain.GetComponentInChildren<FishingInteraction>();
                    if (fishing == null) return false;

                    if (fishing.State == FishingState.Bite)
                    {
                        if (fishing.StrikeAndReel(out string species, out float scale, out string receipt))
                        {
                            self.ActiveCommandStatus = $"Struck bite! Reeling {species}!";
                            if (self.Brain.Actor != null) self.Brain.Actor.Gesture();
                            if (self.Brain.Log != null)
                                self.Brain.Log.Record(self.Brain.Tick, "COMMAND", self.Brain.DescribePerception(), self.ActiveCommandTitle, "Strike bite", $"Hooked {species}");
                            return true;
                        }
                        self.CancelActiveCommand("command-failed-strike: " + receipt);
                        return true;
                    }
                    else if (fishing.State == FishingState.Reeling)
                    {
                        return true;
                    }
                    else if (fishing.State == FishingState.Cancelled || fishing.State == FishingState.Idle)
                    {
                        self.CancelActiveCommand("command-failed-fishing-" + fishing.LastReceipt);
                        return true;
                    }

                    self.ActiveCommandStatus = $"Watching bobber ({fishing.State})...";
                    return false;
                }
            });

            steps.Add(new ActiveCommandStep
            {
                Title = "Land catch into hand",
                TimeoutSeconds = 15f,
                Execute = self =>
                {
                    if (self.TryGetHeldFoodOrCatch(out var catchNi, out var catchPhys, out bool isLeft))
                    {
                        if (catchPhys != null && (catchPhys.itemTypeId.Contains("fish") || catchPhys.itemTypeId.Contains("carp") || catchPhys.itemTypeId.Contains("crab")))
                        {
                            self.ActiveCommandStatus = $"Landed {catchPhys.itemTypeId} into {(isLeft ? "left" : "right")} hand";
                            self.LastCommandReceipt = $"command-completed: caught-{catchPhys.itemTypeId}";
                            if (self.Brain.Log != null)
                                self.Brain.Log.Record(self.Brain.Tick, "COMMAND", self.Brain.DescribePerception(), self.ActiveCommandTitle, "Catch landed", self.LastCommandReceipt);
                            return true;
                        }
                    }

                    var fishing = self.Brain.GetComponent<FishingInteraction>() ?? self.Brain.GetComponentInChildren<FishingInteraction>();
                    if (fishing != null && (fishing.State == FishingState.Reeling || fishing.State == FishingState.Casting || fishing.State == FishingState.Floating || fishing.State == FishingState.Nibble || fishing.State == FishingState.Bite))
                    {
                        self.ActiveCommandStatus = $"Fishing in progress ({fishing.State})...";
                        return false;
                    }
                    else if (fishing != null && fishing.State == FishingState.Landed)
                    {
                        if (self.TryGetHeldFoodOrCatch(out catchNi, out catchPhys, out isLeft))
                        {
                            self.ActiveCommandStatus = $"Landed {catchPhys.itemTypeId} into {(isLeft ? "left" : "right")} hand";
                            return true;
                        }
                        return false;
                    }

                    self.CancelActiveCommand("command-failed-landing: " + (fishing != null ? fishing.LastReceipt : "unknown"));
                    return true;
                }
            });
        }

        private void AddRoastSteps(List<ActiveCommandStep> steps)
        {
            steps.Add(new ActiveCommandStep
            {
                Title = "Ensure holding catch",
                TimeoutSeconds = 5f,
                Execute = self =>
                {
                    if (self.TryGetHeldFoodOrCatch(out var ni, out var phys, out _))
                    {
                        return true;
                    }
                    self.ActiveCommandStatus = "Must hold catch to roast";
                    return false;
                }
            });

            steps.Add(new ActiveCommandStep
            {
                Title = "Navigate to refuge hearth",
                TimeoutSeconds = 25f,
                Execute = self =>
                {
                    var cooking = self.Brain.GetComponent<HearthCooking>() ?? self.Brain.GetComponentInChildren<HearthCooking>() ?? UnityEngine.Object.FindAnyObjectByType<HearthCooking>();
                    Vector3 hearthPos = cooking != null ? cooking.GetHearthPosition() : new Vector3(CoastalTerrain.RefugeCentre.x, 0f, CoastalTerrain.RefugeCentre.y);
                    float dist = Vector3.Distance(self.Brain.transform.position, hearthPos);
                    if (dist <= 2.8f)
                    {
                        self.ActiveCommandStatus = "Arrived at refuge hearth";
                        return true;
                    }
                    if (self.route.Count == 0 || self.routePurpose != "go-to-hearth")
                    {
                        self.StartRoute(hearthPos);
                        self.routePurpose = "go-to-hearth";
                    }
                    self.ActiveCommandStatus = $"Walking to hearth ({dist:F1}m)...";
                    return false;
                }
            });

            steps.Add(new ActiveCommandStep
            {
                Title = "Roast on hearth embers",
                TimeoutSeconds = 10f,
                Execute = self =>
                {
                    if (self.TryGetHeldFoodOrCatch(out var ni, out var phys, out bool isLeft))
                    {
                        if (phys.itemTypeId == "food-cooked-fish" || phys.itemTypeId == "food-cooked-meat" || phys.itemTypeId == "food-cooked-crab")
                        {
                            self.ActiveCommandStatus = "Catch already roasted";
                            return true;
                        }
                    }
                    if (HearthCooking.TryRoastHeldItem(self.Brain))
                    {
                        self.ActiveCommandStatus = "Roasted catch on hearth embers";
                        self.recentVerifiedOutcome = "roast catch succeeded";
                        if (self.Brain.Actor != null) self.Brain.Actor.Gesture();
                        return true;
                    }
                    return false;
                }
            });
        }

        private void AddStoreSteps(List<ActiveCommandStep> steps)
        {
            string chosenBasketId = null;

            steps.Add(new ActiveCommandStep
            {
                Title = "Ensure holding catch",
                TimeoutSeconds = 5f,
                Execute = self =>
                {
                    if (self.TryGetHeldFoodOrCatch(out var ni, out var phys, out _))
                    {
                        return true;
                    }
                    self.ActiveCommandStatus = "Must hold catch to store";
                    return false;
                }
            });

            steps.Add(new ActiveCommandStep
            {
                Title = "Approach storage basket",
                TimeoutSeconds = 25f,
                Execute = self =>
                {
                    NpcObservation basket = null;
                    if (self.Brain.Perception != null && self.Brain.Perception.Current != null)
                    {
                        basket = self.Brain.Perception.Current.FirstOrDefault(x => x != null && x.kind == NpcObjectKind.Item && x.id.Contains("basket") && x.permission && x.available);
                    }
                    if (basket == null)
                    {
                        var food = self.Food != null ? self.Food.Model.State : null;
                        if (food != null)
                        {
                            var remembered = ItemObservationMemory.Nearest(food, "basket", self.Brain.transform.position);
                            if (remembered != null)
                            {
                                basket = new NpcObservation { id = remembered.id, position = remembered.position, approach = remembered.position };
                            }
                        }
                    }
                    if (basket == null)
                    {
                        self.ActiveCommandStatus = "No storage basket seen or remembered in territory";
                        self.CancelActiveCommand("command-failed-no-basket-available");
                        return true;
                    }
                    chosenBasketId = basket.id;
                    Vector3 basketApproach = basket.kind == NpcObjectKind.Item ? basket.approach : basket.position;
                    float dist = Vector3.Distance(self.Brain.transform.position, basketApproach);
                    if (dist <= .6f)
                    {
                        self.ActiveCommandStatus = "Reached storage basket";
                        return true;
                    }
                    if (self.route.Count == 0 || self.routePurpose != "approach-basket")
                    {
                        self.StartRoute(basketApproach);
                        self.routePurpose = "approach-basket";
                    }
                    self.ActiveCommandStatus = $"Approaching basket ({dist:F1}m)...";
                    return false;
                }
            });

            steps.Add(new ActiveCommandStep
            {
                Title = "Store catch into basket",
                TimeoutSeconds = 10f,
                Execute = self =>
                {
                    if (string.IsNullOrEmpty(chosenBasketId))
                    {
                        self.CancelActiveCommand("command-failed-no-target-basket");
                        return true;
                    }

                    if (self.TryGetHeldFoodOrCatch(out var heldFoodNi, out var heldPhys, out bool isLeft))
                    {
                        if (self.Brain.Actions != null && self.Brain.TryAllocateRequestId(out int req))
                        {
                            // Authoritative store using live action authority and container reach/capacity
                            var storeResult = self.Brain.Actions.Store(req, chosenBasketId, heldPhys.itemId);
                            if (storeResult.success)
                            {
                                self.Persist();
                                if (!self.Enabled) return true;
                                self.ActiveCommandStatus = "Stored catch in basket";
                                self.recentVerifiedOutcome = "store catch in basket succeeded";
                                if (self.Brain.Actor != null) self.Brain.Actor.Gesture();
                                return true;
                            }
                            else
                            {
                                self.ActiveCommandStatus = "Store refused: " + storeResult.code;
                                return false;
                            }
                        }
                    }
                    return false;
                }
            });
        }

        private void AddEatSteps(List<ActiveCommandStep> steps)
        {
            steps.Add(new ActiveCommandStep
            {
                Title = "Ensure holding catch",
                TimeoutSeconds = 5f,
                Execute = self =>
                {
                    if (self.TryGetHeldFoodOrCatch(out var ni, out var phys, out _))
                    {
                        return true;
                    }
                    self.ActiveCommandStatus = "Must hold catch to eat";
                    return false;
                }
            });

            steps.Add(new ActiveCommandStep
            {
                Title = "Consume catch",
                TimeoutSeconds = 5f,
                Execute = self =>
                {
                    if (self.TryGetHeldFoodOrCatch(out var foodNi, out var foodPhys, out bool isLeft))
                    {
                        bool ok = FoodConsumptionBridge.TryConsumeHeldFood(self.Brain, foodNi, foodPhys, isLeft, "consumed-catch-meal", out string code);
                        if (ok)
                        {
                            bool isCooked = foodPhys != null && (foodPhys.itemTypeId == "food-cooked-fish" || foodPhys.itemTypeId == "food-cooked-meat" || foodPhys.itemTypeId == "food-cooked-crab");
                            self.ActiveCommandStatus = isCooked ? "Feasted on roasted catch" : "Consumed raw river catch";
                            return true;
                        }
                        else
                        {
                            self.ActiveCommandStatus = "Consume refused: " + code;
                            return false;
                        }
                    }
                    return false;
                }
            });
        }
    }
}
