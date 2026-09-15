using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Starfall.Food;

namespace CityLife.World
{
    // Optional normal-play survival authority after the finite object task.
    // It never receives undiscovered resource coordinates from the authored scene.
    public sealed class StarfallSurvivalAutonomy : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public IntegratedFoodRuntime Food;
        public Starfall.Refuge.RefugeRuntime Refuge;
        public bool Enabled { get; private set; }
        public string Status { get; private set; }="Survival mind off";
        public string LastChoice { get; private set; }="waiting for discovery";
        public bool LastChoiceByModel { get; private set; }
        public string LastOutcome { get; private set; }="no survival outcome yet";
        public int AcceptedDecisions { get; private set; }
        public bool VerifiedScopedContinuation { get; private set; }
        public int FoodOutcomes { get; private set; }
        public int ExploredMetres { get; private set; }
        public string LastSafeGround { get; private set; }="";
        public float LastSafeGroundY { get; private set; }
        private string endpoint,model,evidenceDirectory,savePath;
        private Task<StarfallSurvivalThought.Result> pending;
        private CancellationTokenSource cancellation;
        private readonly Queue<Vector3> route=new Queue<Vector3>();
        private readonly Dictionary<Vector2Int,int> exploredCells=new Dictionary<Vector2Int,int>();
        private List<string> offered;
        private int request, modelRequestSequence, nextRequestTick, routeStartTick, deathAtTick=-1, explorationSeed, lastCheckpointSecond;
        private string routePurpose, recentVerifiedOutcome;
        private Vector3 routeOrigin;
        [Serializable] private sealed class Row
        {
            public string world,actor,kind,code,choice,model,requestHash,responseHash,finishReason,deathCause,deathHash,offeredActions;
            public int tick,incarnation,foodDelta,waterDelta,inventoryDelta,request;
            public int foodTick,energy,hydration,stomach,carriedFruit,modelRequestSequence;
            public bool knowsBerry,knowsSpring,mealBenefitVerified;
            public long modelMilliseconds;
            public float x,y,z;
        }
        private IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-npcSurvivalRuntime")<0)yield break;
            string Arg(string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
            endpoint=Arg("-npcLocalEndpoint");model=Arg("-npcSurvivalModel");
            evidenceDirectory=Arg("-npcSurvivalEvidence");savePath=Arg("-npcSurvivalSave");
            if(Brain==null||Food==null){Status="Survival mind unavailable: missing world adapter";yield break;}
            while(!Brain.Ready)yield return null;
            try
            {
                if(Brain==null||Food==null||Brain.InstanceWorldId!=Food.Model.State.world||
                    string.IsNullOrWhiteSpace(model)||string.IsNullOrWhiteSpace(endpoint)||
                    string.IsNullOrEmpty(evidenceDirectory)||!Path.IsPathFullyQualified(evidenceDirectory)||
                    Directory.Exists(evidenceDirectory)&&Directory.GetFileSystemEntries(evidenceDirectory).Length!=0)
                    throw new InvalidOperationException("scoped-survival-configuration-incomplete");
                StarfallLivingMemoryClient.Loopback(endpoint);
                if(savePath!=null && !Path.IsPathFullyQualified(savePath))throw new InvalidOperationException("save-path-not-absolute");
                Directory.CreateDirectory(evidenceDirectory);
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
                Enabled=true;Status="Survival mind ready; waiting for ordinary task authority";
                lastCheckpointSecond=Food.Model.State.tick;
                Record("startup",prior?(VerifiedScopedContinuation?"earned-survival-authority-reloaded":
                    "scoped-food-save-reloaded-without-authority"):"new-scoped-food-journey",null,null);
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
        private List<string> Eligible()
        {
            var foodChoices=new List<string>();var s=Food.Model.State;
            // First screen for actually seen, scoped freshwater. Its position
            // is never disclosed; an approach token appears only after live LOS.
            if(Observed("spring-food",out _) && Food.Spring!=null && Food.Spring.WorldId==s.world)
            {
                FoodAccess gate=Food.Inspect("spring");
                if(gate.visible&&gate.permitted)
                {
                    if(!gate.inReach)foodChoices.Add("approach spring");
                    else if(!s.knowsSpring)foodChoices.Add("inspect spring");
                    else if(s.hydration<8500&&s.freshwaterMl>=250)foodChoices.Add("drink spring");
                }
            }
            // Do not repeatedly approach a familiar, exhausted bush or fill
            // inventory when neither measured food nor water needs topping up.
            // Stock and carried fruit are the inhabitant's actual scoped state,
            // not foreknowledge of a new resource's location or effect.
            bool berryRelevant=BerryRelevant(s);
            if(berryRelevant&&Observed("berry-food",out _) && Food.Berry!=null && Food.Berry.WorldId==s.world)
            {
                FoodAccess gate=Food.Inspect("berry");
                if(gate.visible && gate.permitted)
                {
                    if(!gate.inReach)foodChoices.Add("approach berry");
                    else if(!s.knowsBerry)foodChoices.Add("inspect berry");
                    else if(s.fruitStock>0&&s.carriedFruit<4)foodChoices.Add("gather berry");
                }
            }
            if(s.carriedFruit>0&&s.knowsBerry&&s.body.stomach<=8800&&
                (s.satiety<8500||s.hydration<8500))foodChoices.Add("eat fruit");
            // Exact-payload probes proved four exploratory options 8/8
            // length/empty, and the three-option near-berry runtime stalled
            // repeatedly. Two genuinely eligible options generated a final
            // live action in 5/5 separate warm requests. Rotate the offered
            // menu over time; this is capacity selection, not an action taken
            // on the model's behalf or hidden resource knowledge.
            string urgent=s.hydration<6500?foodChoices.FirstOrDefault(x=>x.EndsWith("spring")):null;
            if(urgent==null&&s.carriedFruit>0&&s.satiety<8500&&foodChoices.Contains("eat fruit"))urgent="eat fruit";
            if(urgent==null)urgent=foodChoices.FirstOrDefault();
            var choices=new List<string>();if(urgent!=null)choices.Add(urgent);
            var directions=new [] {("explore north",Vector3.forward),("explore east",Vector3.right),
                ("explore south",Vector3.back),("explore west",Vector3.left)};
            var candidates=new List<(string action,int score)>();
            for(int i=0;i<directions.Length;i++)
            {
                var p=Brain.transform.position+directions[i].Item2*6f;
                if(!Brain.TerrainNavigation.Walkable(p,out _))continue;
                var cell=new Vector2Int(Mathf.RoundToInt(p.x/3f),Mathf.RoundToInt(p.z/3f));
                exploredCells.TryGetValue(cell,out int visits);
                candidates.Add((directions[i].Item1,visits*10+(i+explorationSeed)%4));
            }
            foreach(var candidate in candidates.OrderBy(c=>c.score).Take(2-choices.Count))
                choices.Add(candidate.action);
            return choices;
        }
        private bool StartRoute(Vector3 destination)
        {
            var planned=Brain.TerrainNavigation.Plan(Brain.transform.position,destination);
            if(planned==null||planned.Count==0)return false;
            route.Clear();foreach(var waypoint in planned)route.Enqueue(waypoint);
            routeStartTick=Brain.Tick;routeOrigin=Brain.transform.position;return true;
        }
        private bool MoveRoute()
        {
            while(route.Count>0&&Vector2.Distance(new Vector2(Brain.transform.position.x,Brain.transform.position.z),
                new Vector2(route.Peek().x,route.Peek().z))<.20f)route.Dequeue();
            if(route.Count==0)
            {
                ExploredMetres+=Mathf.RoundToInt(Vector3.Distance(routeOrigin,Brain.transform.position));
                LastOutcome="Walked to model-chosen place";
                recentVerifiedOutcome=routePurpose+" reached";
                Record("route","reached",routePurpose,null);routePurpose=null;
                nextRequestTick=Brain.Tick+10;Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;
            }
            if(Brain.Tick-routeStartTick>1000)
            {route.Clear();Record("route","bounded-route-timeout",routePurpose,null);routePurpose=null;nextRequestTick=Brain.Tick+50;Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;}
            Vector3 delta=route.Peek()-Brain.transform.position;delta.y=0;
            Vector3 direction=Brain.TerrainNavigation.ConstrainMotion(Brain.transform.position,delta.normalized,
                Brain.Actor.WalkSpeed*NpcAutonomy.StepSeconds);
            if(direction==Vector3.zero)
            {route.Clear();Record("route","live-terrain-blocked",routePurpose,null);routePurpose=null;nextRequestTick=Brain.Tick+50;}
            Brain.Actor.Step(direction,NpcAutonomy.StepSeconds);return true;
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
            try{Food.Model.Save(savePath);}
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
        private void Execute(string action,StarfallSurvivalThought.Result result)
        {
            // Re-sense after the asynchronous answer. No target survives a world,
            // permission, line-of-sight or reach change during inference.
            Brain.Perception.Sense(Brain.Tick);
            var live=Eligible();
            if(!StarfallSurvivalThought.Parse(action,live,out string accepted))
            {Record("decision","stale-or-ineligible",action,result);nextRequestTick=Brain.Tick+50;return;}
            AcceptedDecisions++;Record("decision","live-admitted",accepted,result);
            LastChoice=accepted;LastChoiceByModel=true;
            Status="Local model choice admitted after live validation";
            if(accepted.StartsWith("explore ",StringComparison.Ordinal))
            {
                routePurpose=accepted;
                Vector3 direction=accepted.EndsWith("north")?Vector3.forward:accepted.EndsWith("south")?Vector3.back:
                    accepted.EndsWith("east")?Vector3.right:Vector3.left;
                Vector3 destination=Brain.transform.position+direction*6f;
                explorationSeed++;
                var cell=new Vector2Int(Mathf.RoundToInt(destination.x/3f),Mathf.RoundToInt(destination.z/3f));
                exploredCells.TryGetValue(cell,out int visits);exploredCells[cell]=visits+1;
                if(!StartRoute(destination)){routePurpose=null;Record("route","no-walkable-exploration-route",accepted,result);}
            }
            else if(accepted.StartsWith("approach ",StringComparison.Ordinal))
            {
                routePurpose=accepted;
                string target=accepted.EndsWith("berry")?"berry-food":"spring-food";
                if(!Observed(target,out var observation)||!StartRoute(observation.approach))
                {routePurpose=null;Record("route","live-target-route-rejected",accepted,result);}
            }
            else
            {
                FoodAction kind=accepted.StartsWith("inspect")?FoodAction.Inspect:
                    accepted.StartsWith("gather")?FoodAction.Gather:accepted.StartsWith("eat")?FoodAction.Eat:FoodAction.Drink;
                string target=accepted.EndsWith("berry")?"berry":accepted.EndsWith("spring")?"spring":"inventory";
                if(!AllocateFoodRequest(out int foodRequest))return;
                var s=Food.Model.State;FoodReceipt receipt=Food.Model.Execute(s.world,s.generation,foodRequest,kind,target,Food);
                Record("food",receipt.code,accepted,result,receipt);
                LastOutcome=receipt.success&&kind==FoodAction.Eat?"Meal +"+receipt.foodDelta+" energy / +"+receipt.waterDelta+" water":
                    receipt.success&&kind==FoodAction.Gather?"Gathered one observed fruit":
                    receipt.success&&kind==FoodAction.Inspect?"Observed resource; outcome unproven":receipt.code;
                if(receipt.success){FoodOutcomes++;Food.SyncFruitVisual();Persist();}
                if(receipt.success)recentVerifiedOutcome=accepted+" succeeded";
            }
            nextRequestTick=Brain.Tick+25;
        }
        public bool StepTick()
        {
            if(!Enabled)return false;
            if(Brain.MenuPaused||Brain.Possessed||!Brain.Running){Cancel("control-interruption");return false;}
            var s=Food.Model.State;
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
            if(route.Count>0)return MoveRoute();
            if(pending!=null)
            {
                Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
                if(!pending.IsCompleted)return true;
                StarfallSurvivalThought.Result result;
                try{result=pending.GetAwaiter().GetResult();}catch(Exception){result=new StarfallSurvivalThought.Result{status="provider-unavailable"};}
                pending=null;cancellation.Dispose();cancellation=null;
                if(result.status=="parsed-awaiting-live-check")Execute(result.answer,result);
                else {Record("model",result.status,result.answer,result);nextRequestTick=Brain.Tick+100;}
                offered=null;return true;
            }
            if(Brain.Tick<nextRequestTick){Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;}
            offered=Eligible();
            if(offered.Count==0){Record("model","no-current-walkable-or-observed-options",null,null);nextRequestTick=Brain.Tick+100;return true;}
            if(modelRequestSequence==int.MaxValue)
            {Enabled=false;Status="Survival model request sequence exhausted";return true;}
            modelRequestSequence++;
            string requestJson=StarfallSurvivalThought.BuildRequest(model,s.satiety,s.hydration,offered,
                s.carriedFruit,s.knowsMealBenefit&&!string.IsNullOrEmpty(s.lastMealEvidence),recentVerifiedOutcome);
            Record("model","request-issued",null,new StarfallSurvivalThought.Result{model=model,requestJson=requestJson});
            if(!Enabled)return true;
            cancellation=new CancellationTokenSource();
            pending=StarfallSurvivalThought.Request(endpoint,model,s.satiety,s.hydration,offered,cancellation.Token,
                s.carriedFruit,s.knowsMealBenefit&&!string.IsNullOrEmpty(s.lastMealEvidence),recentVerifiedOutcome,requestJson);
            Status="Local model deciding from live eligible observations";
            Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;
        }
    }
}
