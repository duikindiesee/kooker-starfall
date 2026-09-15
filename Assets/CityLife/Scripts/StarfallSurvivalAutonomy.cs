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
        public string LastOutcome { get; private set; }="no survival outcome yet";
        public int AcceptedDecisions { get; private set; }
        public int FoodOutcomes { get; private set; }
        public int ExploredMetres { get; private set; }
        private string endpoint,model,evidenceDirectory,savePath;
        private Task<StarfallSurvivalThought.Result> pending;
        private CancellationTokenSource cancellation;
        private readonly Queue<Vector3> route=new Queue<Vector3>();
        private List<string> offered;
        private int request, nextRequestTick, routeStartTick, deathAtTick=-1;
        private Vector3 routeOrigin;
        [Serializable] private sealed class Row
        {
            public string world,actor,kind,code,choice,model,requestHash,responseHash,finishReason,deathCause,deathHash;
            public int tick,incarnation,foodDelta,waterDelta,inventoryDelta,request;
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
                if(prior&&!Food.Model.State.body.dead)
                {
                    Vector3 remembered=Food.Model.State.actorPosition;
                    if(!Brain.TerrainNavigation.Walkable(remembered,out var floor))throw new InvalidOperationException("saved-actor-ground-rejected");
                    Brain.Actor.Place(floor+Vector3.up*.06f);
                }
                Enabled=true;Status="Survival mind ready; waiting for ordinary task authority";
                Record("startup",prior?"scoped-food-save-reloaded":"new-scoped-food-journey",null,null);
            }
            catch(Exception error){Enabled=false;Status="Survival mind unavailable: "+error.GetType().Name;}
        }
        private void Update()
        {
            if(pending!=null && (Brain==null||Brain.MenuPaused||Brain.Possessed||!Brain.Running||!Enabled))
                Cancel("control-interruption");
        }
        public void Cancel(string reason)
        {
            if(pending!=null){cancellation.Cancel();cancellation.Dispose();cancellation=null;pending=null;offered=null;Record("cancel",reason,null,null);}
            route.Clear();nextRequestTick=Brain==null?0:Brain.Tick+25;
        }
        private void OnDestroy()=>Cancel("destroyed");
        private void Record(string kind,string code,string choice,StarfallSurvivalThought.Result result,FoodReceipt receipt=null)
        {
            if(string.IsNullOrEmpty(evidenceDirectory)||!Directory.Exists(evidenceDirectory)||Food==null||Brain==null)return;
            var s=Food.Model.State; var row=new Row{world=s.world,actor=s.actorId,kind=kind,code=code,choice=choice,
                model=result==null?null:result.model,requestHash=result==null?null:result.RequestHash,
                responseHash=result==null?null:result.ResponseHash,finishReason=result==null?null:result.finishReason,
                tick=Brain.Tick,incarnation=s.incarnation,request=receipt==null?0:receipt.request,
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
        private List<string> Eligible()
        {
            var choices=new List<string>();var s=Food.Model.State;
            if(Observed("berry-food",out _) && Food.Berry!=null && Food.Berry.WorldId==s.world)
            {
                FoodAccess gate=Food.Inspect("berry");
                if(gate.visible && gate.permitted)
                {
                    if(!gate.inReach)choices.Add("approach berry");
                    else if(!s.knowsBerry)choices.Add("inspect berry");
                    else if(s.fruitStock>0&&s.carriedFruit<4)choices.Add("gather berry");
                }
            }
            if(s.carriedFruit>0&&s.knowsBerry&&s.satiety<8500)choices.Add("eat fruit");
            if(Observed("spring-food",out _) && Food.Spring!=null && Food.Spring.WorldId==s.world)
            {
                FoodAccess gate=Food.Inspect("spring");
                if(gate.visible&&gate.permitted)
                {
                    if(!gate.inReach)choices.Add("approach spring");
                    else if(!s.knowsSpring)choices.Add("inspect spring");
                    else if(s.hydration<8500&&s.freshwaterMl>=250)choices.Add("drink spring");
                }
            }
            choices.Add("explore north");choices.Add("explore east");choices.Add("explore south");choices.Add("explore west");
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
                Record("route","reached",null,null);nextRequestTick=Brain.Tick+10;Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;
            }
            if(Brain.Tick-routeStartTick>1000)
            {route.Clear();Record("route","bounded-route-timeout",null,null);nextRequestTick=Brain.Tick+50;Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;}
            Vector3 delta=route.Peek()-Brain.transform.position;delta.y=0;
            Vector3 direction=Brain.TerrainNavigation.ConstrainMotion(Brain.transform.position,delta.normalized,
                Brain.Actor.WalkSpeed*NpcAutonomy.StepSeconds);
            if(direction==Vector3.zero)
            {route.Clear();Record("route","live-terrain-blocked",null,null);nextRequestTick=Brain.Tick+50;}
            Brain.Actor.Step(direction,NpcAutonomy.StepSeconds);return true;
        }
        private bool TrySafeReturn()
        {
            if(Refuge==null||!Refuge.GeometryVerified)return false;
            Physics.SyncTransforms();
            for(float x=-11.5f;x<=-8.5f;x+=.5f)for(float z=-1f;z<=1f;z+=.5f)
            {
                Vector3 above=Refuge.OriginOffset+new Vector3(x,3,z);
                if(!Physics.Raycast(above,Vector3.down,out var hit,2f,Starfall.Refuge.RefugeRuntime.GeometryMask,QueryTriggerInteraction.Ignore))continue;
                Vector3 safe=hit.point+Vector3.up*.06f;
                if(Physics.CheckCapsule(safe+Vector3.up*.45f,safe+Vector3.up*1.5f,.35f,
                    Starfall.Refuge.RefugeRuntime.GeometryMask,QueryTriggerInteraction.Ignore))continue;
                var s=Food.Model.State;
                FoodReceipt returned=Food.Model.Execute(s.world,s.generation,++request,FoodAction.Return,"inventory",Food);
                if(!returned.success)return false;
                Brain.Actor.Place(safe);Brain.Actor.DeadPose=false;Brain.Actor.RefreshAnimation();
                s.actorPosition=safe;Record("return","safe-refuge-world-preserved",null,null,returned);
                LastChoice="safe return";LastOutcome="Returned after "+s.deaths[s.deaths.Count-1].cause;
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
        private void Execute(string action,StarfallSurvivalThought.Result result)
        {
            // Re-sense after the asynchronous answer. No target survives a world,
            // permission, line-of-sight or reach change during inference.
            Brain.Perception.Sense(Brain.Tick);
            var live=Eligible();
            if(!StarfallSurvivalThought.Parse(action,live,out string accepted))
            {Record("decision","stale-or-ineligible",action,result);nextRequestTick=Brain.Tick+50;return;}
            AcceptedDecisions++;Record("decision","live-admitted",accepted,result);
            LastChoice=accepted;Status="Nano model choice admitted after live validation";
            if(accepted.StartsWith("explore ",StringComparison.Ordinal))
            {
                Vector3 direction=accepted.EndsWith("north")?Vector3.forward:accepted.EndsWith("south")?Vector3.back:
                    accepted.EndsWith("east")?Vector3.right:Vector3.left;
                Vector3 destination=Brain.transform.position+direction*6f;
                if(!StartRoute(destination))Record("route","no-walkable-exploration-route",accepted,result);
            }
            else if(accepted.StartsWith("approach ",StringComparison.Ordinal))
            {
                string target=accepted.EndsWith("berry")?"berry-food":"spring-food";
                if(!Observed(target,out var observation)||!StartRoute(observation.approach))
                    Record("route","live-target-route-rejected",accepted,result);
            }
            else
            {
                FoodAction kind=accepted.StartsWith("inspect")?FoodAction.Inspect:
                    accepted.StartsWith("gather")?FoodAction.Gather:accepted.StartsWith("eat")?FoodAction.Eat:FoodAction.Drink;
                string target=accepted.EndsWith("berry")?"berry":accepted.EndsWith("spring")?"spring":"inventory";
                var s=Food.Model.State;FoodReceipt receipt=Food.Model.Execute(s.world,s.generation,++request,kind,target,Food);
                Record("food",receipt.code,accepted,result,receipt);
                LastOutcome=receipt.success&&kind==FoodAction.Eat?"Meal +"+receipt.foodDelta+" energy / +"+receipt.waterDelta+" water":
                    receipt.success&&kind==FoodAction.Gather?"Gathered one observed fruit":
                    receipt.success&&kind==FoodAction.Inspect?"Observed resource; outcome unproven":receipt.code;
                if(receipt.success){FoodOutcomes++;Food.SyncFruitVisual();Persist();}
            }
            nextRequestTick=Brain.Tick+25;
        }
        public bool StepTick()
        {
            if(!Enabled)return false;
            if(Brain.MenuPaused||Brain.Possessed||!Brain.Running){Cancel("control-interruption");return false;}
            var s=Food.Model.State;
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
            offered=Eligible();cancellation=new CancellationTokenSource();
            pending=StarfallSurvivalThought.Request(endpoint,model,s.satiety,s.hydration,offered,cancellation.Token);
            Status="Local model deciding from live eligible observations";
            Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);return true;
        }
    }
}
