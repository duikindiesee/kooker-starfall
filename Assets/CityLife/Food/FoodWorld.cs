using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CityLife.World;

namespace Starfall.Food
{
    public sealed class FoodWorld:MonoBehaviour,IFoodAccess
    {
        public FoodModel Model {get;private set;}
        public bool Paused,Auto;
        public readonly Dictionary<string,NpcInteractable> Targets=new Dictionary<string,NpcInteractable>();
        public Transform Actor;public NpcDecisionLog Log;public NpcPerception Perception;
        CharacterController controller;Camera cameraView;GameObject[] berries=new GameObject[3];GameObject garden;
        Material ripe,bud,unripe;string root,evidence,status="Ready. Purple-red fruit is UNKNOWN.",goal="",target="";FoodAction action;
        bool pending;float pendingTime;int lastStage,lastStock,seenEcology;readonly List<float> frames=new List<float>();
        readonly Dictionary<int,GameObject> wildVisuals=new Dictionary<int,GameObject>();readonly Dictionary<int,GameObject> dropVisuals=new Dictionary<int,GameObject>();
        [Serializable]sealed class ActiveSave{public string world,generation;public int seed;}
        [Serializable]sealed class RunReport{public string status,scope,limitations;public List<string> checks;public float p95;public int frames,width,height;}
        string Slot=>Path.Combine(root,Model.State.world+"-"+Model.State.generation+".json");
        static string Arg(string key){var args=System.Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]==key)return args[i+1];return null;}
        static bool Has(string key)=>Array.IndexOf(System.Environment.GetCommandLineArgs(),key)>=0;
        void Start()
        {
            Application.targetFrameRate=60;Time.fixedDeltaTime=.02f;
            evidence=Arg("-foodEvidence");if(evidence!=null)Directory.CreateDirectory(evidence);
            root=evidence!=null?Path.Combine(evidence,"saves"):Path.Combine(Application.persistentDataPath,"food-eden-2");Directory.CreateDirectory(root);
            MakeScene();
            string active=Path.Combine(root,"active.json");bool loaded=false;
            if(File.Exists(active))try{var a=JsonUtility.FromJson<ActiveSave>(File.ReadAllText(active));Model=new FoodModel(a.world,a.generation,a.seed);loaded=Model.Load(Slot,a.world,a.generation);}catch{status="Active save invalid; not overwritten. Use New World.";}
            if(Model==null)Model=new FoodModel("food-"+Guid.NewGuid().ToString("N"),"g-"+Guid.NewGuid().ToString("N"),int.TryParse(Arg("-foodSeed"),out int requestedSeed)?requestedSeed:4242);
            BindScope();if(loaded){SetActor(Model.State.actorPosition);Record("reload","Loaded world-scoped needs, berry stock, knowledge and garden.");}
            else{SetActor(new Vector3(0,0,-2));Record("perception","Purple-red berries visible; safety UNKNOWN. Read the signed lesson nearby.");}
            RefreshVisuals();if(Has("-foodTest"))StartCoroutine(Acceptance());else if(Has("-foodResume"))StartCoroutine(ResumeAcceptance());else if(Has("-foodEden"))StartCoroutine(EdenAcceptance());else if(Has("-foodMortality"))StartCoroutine(MortalityAcceptance());
        }
        void BindScope(){Perception.WorldId=Model.State.world+"."+Model.State.generation;foreach(var x in Targets.Values)x.WorldId=Perception.WorldId;Log.ResetLog();Perception.Current.Clear();pending=false;Auto=false;seenEcology=Model.State.ecologySequence;lastStock=Model.State.fruitStock;lastStage=Model.State.gardenStage;}
        Material Mat(Color c){var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.color=c;return m;}
        GameObject Shape(string name,PrimitiveType type,Vector3 pos,Vector3 scale,Material mat,bool solid=false)
        {
            var o=GameObject.CreatePrimitive(type);o.name=name;o.transform.position=pos;o.transform.localScale=scale;o.GetComponent<Renderer>().sharedMaterial=mat;if(!solid)Destroy(o.GetComponent<Collider>());return o;
        }
        void AddTarget(string id,Vector3 position,Vector3 approach)
        {
            var o=new GameObject(id);o.transform.position=position;o.layer=11;var c=o.AddComponent<SphereCollider>();c.radius=.5f;c.isTrigger=true;
            var n=o.AddComponent<NpcInteractable>();n.StableId=id;n.Kind=NpcObjectKind.Item;var a=new GameObject(id+" approach");a.transform.position=approach;n.Approach=a.transform;Targets.Add(id,n);
        }
        void MakeScene()
        {
            var backdrop=new GameObject("Opaque HUD background").AddComponent<Camera>();backdrop.depth=-100;backdrop.clearFlags=CameraClearFlags.SolidColor;backdrop.backgroundColor=Color.black;backdrop.cullingMask=0;
            RenderSettings.ambientLight=new Color(.52f,.57f,.67f);RenderSettings.fog=true;RenderSettings.fogColor=new Color(.19f,.29f,.38f);RenderSettings.fogDensity=.012f;
            var light=new GameObject("Warm desert sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.transform.rotation=Quaternion.Euler(48,-30,0);light.shadows=LightShadows.Soft;
            cameraView=new GameObject("Food camera").AddComponent<Camera>();cameraView.tag="MainCamera";cameraView.backgroundColor=new Color(.12f,.22f,.31f);cameraView.clearFlags=CameraClearFlags.SolidColor;cameraView.transform.position=new Vector3(11,12,-19);cameraView.transform.LookAt(new Vector3(-1,0,2));cameraView.fieldOfView=48;cameraView.farClipPlane=120;cameraView.rect=new Rect(0,.20f,.70f,.64f);cameraView.gameObject.AddComponent<AudioListener>();
            var sand=Mat(new Color(.64f,.36f,.22f));var stone=Mat(new Color(.43f,.23f,.19f));var teal=Mat(new Color(.07f,.52f,.56f));var leaf=Mat(new Color(.09f,.23f,.25f));ripe=Mat(new Color(.67f,.045f,.28f));bud=Mat(new Color(.35f,.24f,.49f));unripe=Mat(new Color(.58f,.30f,.58f));
            Shape("Sandstone ground",PrimitiveType.Cube,new Vector3(-2,-5,2),new Vector3(30,10,25),sand,true);
            for(int i=0;i<9;i++)Shape("Canyon buttress",PrimitiveType.Cylinder,new Vector3(-11+i*3,-1,12+(i%2)),new Vector3(3,3+i%3,3),stone,true);
            Shape("Turquoise sea - salt water",PrimitiveType.Cube,new Vector3(8,.03f,5),new Vector3(7,.04f,13),teal);
            Shape("Spring stone",PrimitiveType.Cylinder,new Vector3(2,.12f,3),new Vector3(2,.15f,2),stone);
            Shape("Maintained fresh spring",PrimitiveType.Cylinder,new Vector3(2,.29f,3),new Vector3(1.7f,.04f,1.7f),teal);
            Shape("Berry trunk",PrimitiveType.Cylinder,new Vector3(-4,.65f,3),new Vector3(.20f,.65f,.20f),stone);
            for(int i=0;i<5;i++){float angle=i*1.26f;Shape("Blue-green succulent foliage",PrimitiveType.Sphere,new Vector3(-4+Mathf.Cos(angle)*.55f,1.1f+i%2*.25f,3+Mathf.Sin(angle)*.45f),new Vector3(.9f,.55f,.7f),leaf);}
            for(int i=0;i<3;i++)berries[i]=Shape("Purple red berry "+i,PrimitiveType.Sphere,new Vector3(-4.65f+i*.6f,1.45f,2.55f),Vector3.one*.40f,ripe);
            Shape("Irrigated cultivation bed",PrimitiveType.Cube,new Vector3(-3,.12f,-1),new Vector3(2,.24f,1.5f),stone);
            garden=Shape("Cultivated berry seedling",PrimitiveType.Sphere,new Vector3(-3,.6f,-1),Vector3.one*.5f,leaf);
            AddTarget("berry",new Vector3(-4,1,3),new Vector3(-4,0,1.85f));AddTarget("spring",new Vector3(2,.5f,3),new Vector3(2,0,1.85f));AddTarget("sea",new Vector3(5,.5f,3),new Vector3(4.8f,0,1.85f));AddTarget("bed",new Vector3(-3,.5f,-1),new Vector3(-3,0,-2.05f));
            AddTarget("refuge",new Vector3(0,.5f,-2),new Vector3(0,0,-2));
            Actor=new GameObject("Inhabitant - food authority").transform;controller=Actor.gameObject.AddComponent<CharacterController>();controller.height=1.8f;controller.radius=.28f;controller.center=Vector3.up*.9f;
            var body=Shape("Inhabitant ochre coat",PrimitiveType.Capsule,Vector3.up*.9f,new Vector3(.55f,.65f,.55f),Mat(new Color(.90f,.63f,.29f)));body.transform.SetParent(Actor,false);
            var head=Shape("Head",PrimitiveType.Sphere,Vector3.up*1.6f,Vector3.one*.40f,Mat(new Color(.75f,.52f,.39f)));head.transform.SetParent(Actor,false);
            Log=Actor.gameObject.AddComponent<NpcDecisionLog>();Perception=Actor.gameObject.AddComponent<NpcPerception>();Perception.Radius=12;
        }
        public FoodAccess Inspect(string id)
        {
            if(id=="inventory")return new FoodAccess{visible=true,inReach=true,permitted=true};
            if(!Targets.TryGetValue(id,out var n))return default;
            var seen=Perception.Sense(Model.State.tick);bool visible=seen.Exists(x=>x.id==id);
            if(seen.Exists(x=>x.id=="bed"))Model.State.seenBed=true;
            return new FoodAccess{visible=visible,inReach=Vector3.Distance(Actor.position,n.Approach.position)<.65f,permitted=n.Permission,verifiedFreshwater=id=="spring"};
        }
        public void Queue(FoodAction next,string id,string reason)
        {
            if(pending||Paused)return;Model.State.body.resting=false;action=next;target=id;goal=reason;pending=true;pendingTime=0;
            var seen=Perception.Sense(Model.State.tick);Record("decision",reason+" -> "+next+" "+id+"; observed "+seen.Count+" nearby targets.");
        }
        public FoodReceipt ExecuteNow(FoodAction next,string id)
        {
            var s=Model.State;var r=Model.Execute(s.world,s.generation,s.lastRequest+1,next,id,this);
            Record("action",r.code+" | food "+r.foodDelta+", hydration "+r.waterDelta+", berries "+r.inventoryDelta);
            if(!r.success&&Auto){Auto=false;Record("recovery","Rejected. Inspect alternatives or request explicit camp aid.");}
            if(r.success&&next==FoodAction.Eat)Record("memory","Remembered nutrition outcome: "+s.lastMealEvidence);
            if(r.success&&next==FoodAction.Return){SetActor(s.actorPosition);Record("return","Same inhabitant, new body incarnation. World and history were not reset.");}
            if(evidence!=null)File.AppendAllText(Path.Combine(evidence,"receipts.jsonl"),JsonUtility.ToJson(r)+"\n");RefreshVisuals();return r;
        }
        void FixedUpdate()
        {
            if(Model==null||Paused)return;Model.State.body.active=pending;Model.State.body.sheltered=Vector3.Distance(Actor.position,new Vector3(0,0,-2))<2;Model.FixedStep(false);
            if(Model.State.body.dead){pending=false;Auto=false;RefreshVisuals();return;}
            if(Model.State.fruitStock!=lastStock){Record("ecology",Model.State.fruitStock>lastStock?"A berry ripened; seasonal water budget consumed.":"Individual fruit removed; bush persists.");lastStock=Model.State.fruitStock;}
            if(Model.State.gardenStage!=lastStage){Record("growth","Garden stage: "+Stage(Model.State.gardenStage,Model.State.gardenEstablished));lastStage=Model.State.gardenStage;}
            if(pending)
            {
                pendingTime+=Time.fixedDeltaTime;
                if(target!="inventory"&&Targets.TryGetValue(target,out var n))
                {
                    Vector3 delta=n.Approach.position-Actor.position;delta.y=0;
                    if(delta.magnitude>.18f){controller.Move(Vector3.ClampMagnitude(delta,1)*2.5f*FoodPhysiology.MovementFactor(Model.State.body,Model.State.satiety)*Time.fixedDeltaTime+Vector3.down*.1f);if(pendingTime>12){pending=false;Record("recovery","Path timeout. Action cancelled; no inventory change.");}return;}
                }
                ExecuteNow(action,target);pending=false;
            }
            else if(Auto)
            {
                var s=Model.State;
                if(s.hydration<6000)Queue(s.knowsSpring?FoodAction.Drink:FoodAction.Inspect,"spring",s.knowsSpring?"Thirst: revisit remembered verified spring":"Thirst: inspect marked freshwater source");
                else if(s.satiety<7500)
                {
                    if(!s.knowsBerry)Queue(FoodAction.Inspect,"berry","Hungry: seek signed food lesson; never taste unknown fruit");
                    else if(s.carriedFruit>0)Queue(FoodAction.Eat,"inventory",s.deaths.Count>0&&s.knowsMealBenefit?"Use remembered death/meal evidence: known berries restore energy":"Hungry: try a safely taught berry and observe its effect");
                    else Queue(FoodAction.Gather,"berry","Hungry: revisit remembered bush; verify stock on arrival");
                }
                else if(s.plantedAt<0&&s.seeds>0)Queue(s.knowsPlanting?FoodAction.Plant:FoodAction.Inspect,"bed","Needs met: learn moist-soil cultivation, then plant saved seed");
                else {Auto=false;Record("decision","Needs met. Rest and observe growth.");}
            }
            RefreshVisuals();
        }
        void Update(){if(Model!=null&&frames.Count<18000)frames.Add(Time.unscaledDeltaTime*1000);}
        void RefreshVisuals()
        {
            var s=Model.State;for(int i=0;i<berries.Length;i++)
            {
                bool full=!s.primaryDead&&i<s.fruitStock;bool growing=!s.primaryDead&&i==s.fruitStock&&i<2+(int)((uint)s.seed%2);
                berries[i].SetActive(full||growing);berries[i].GetComponent<Renderer>().sharedMaterial=full?ripe:s.regrowthProgress<EdenEcology.Regrow/2?bud:unripe;
                berries[i].transform.localScale=Vector3.one*(full?.40f:s.regrowthProgress<EdenEcology.Regrow/2?.13f:.26f);
            }
            garden.SetActive(s.gardenStage>0);garden.transform.localScale=Vector3.one*(s.gardenEstablished?.8f:.20f+s.gardenStage*.20f);
            garden.GetComponent<Renderer>().sharedMaterial=s.gardenStage==3?ripe:bud;
            foreach(var b in s.bushes)
            {
                var pos=Site(b.site);if(!wildVisuals.TryGetValue(b.site,out var v))
                {
                    v=new GameObject("Wild berry site "+b.site);v.transform.position=pos;
                    var foliage=Shape("Wild foliage",PrimitiveType.Sphere,pos+Vector3.up,new Vector3(1.4f,.7f,1.1f),Mat(new Color(.08f,.24f,.25f)));foliage.transform.SetParent(v.transform,true);
                    for(int i=0;i<2;i++){var fruit=Shape("Wild ripe berry "+i,PrimitiveType.Sphere,pos+new Vector3(-.35f+i*.7f,1.35f,-.4f),Vector3.one*.4f,ripe);fruit.transform.SetParent(v.transform,true);}
                    wildVisuals.Add(b.site,v);string id="berry"+(b.site+1);if(!Targets.ContainsKey(id))AddTarget(id,pos+Vector3.up,pos+Vector3.back*1.1f);Targets[id].WorldId=Perception.WorldId;
                }
                v.SetActive(!b.dead);Targets["berry"+(b.site+1)].gameObject.SetActive(!b.dead);v.transform.localScale=Vector3.one*(b.growth>=EdenEcology.Mature?1:.2f+.6f*b.growth/EdenEcology.Mature);
                for(int i=0;i<2;i++)v.transform.GetChild(i+1).gameObject.SetActive(i<b.stock);
            }
            foreach(var pair in wildVisuals)if(!s.bushes.Exists(b=>b.site==pair.Key&&!b.dead)){pair.Value.SetActive(false);Targets["berry"+(pair.Key+1)].gameObject.SetActive(false);}
            foreach(var d in s.drops)
            {
                if(!dropVisuals.TryGetValue(d.id,out var v)){v=Shape("Dropped seed "+d.id,PrimitiveType.Sphere,Site(d.site)+new Vector3(.25f, .18f,-.3f),Vector3.one*.16f,Mat(new Color(.94f,.73f,.47f)));dropVisuals.Add(d.id,v);}
                v.SetActive(d.outcome=="dropped");
            }
            var expired=new List<int>();foreach(var pair in dropVisuals)if(!s.drops.Exists(d=>d.id==pair.Key&&d.outcome=="dropped")){Destroy(pair.Value);expired.Add(pair.Key);}foreach(int id in expired)dropVisuals.Remove(id);
            foreach(var ev in s.ecologyEvents)
            {
                if(ev.sequence<=seenEcology)continue;seenEcology=ev.sequence;Record("ecology",ev.kind+": "+ev.detail);
                if(Perception.Sense(s.tick).Exists(o=>o.id==ev.target))
                {s.causalMemory=s.generation+".observed-ecology."+ev.sequence;Record("memory","Observed "+ev.kind+"; evidence "+s.causalMemory+". Food safety still requires the lesson.");}
            }
        }
        static Vector3 Site(int id)=>id==1?new Vector3(-7,0,0):id==2?new Vector3(-6,0,6):new Vector3(-1,0,6);
        void Record(string phase,string text)
        {
            status=text;Log.Record(Model.State.tick,phase,"visible world + scoped memory",goal,action.ToString(),text);
            if(evidence!=null)File.AppendAllText(Path.Combine(evidence,"decisions.jsonl"),JsonUtility.ToJson(Log.Entries[Log.Entries.Count-1])+"\n");
        }
        void SetActor(Vector3 p){controller.enabled=false;Actor.position=p;controller.enabled=true;Physics.SyncTransforms();}
        public void Save()
        {
            pending=false;Model.State.actorPosition=Actor.position;Model.Save(Slot);
            string manifest=Path.Combine(root,"active.json");string tmp=manifest+".tmp";File.WriteAllText(tmp,JsonUtility.ToJson(new ActiveSave{world=Model.State.world,generation=Model.State.generation,seed=Model.State.seed}));
            if(File.Exists(manifest))File.Replace(tmp,manifest,manifest+".bak");else File.Move(tmp,manifest);
            Record("save","Saved ecology, nutrition, seed, position and knowledge to this world.");
        }
        public void Reload(){pending=false;Auto=false;string w=Model.State.world,g=Model.State.generation;if(Model.Load(Slot,w,g)){SetActor(Model.State.actorPosition);BindScope();RefreshVisuals();Record("reload","Exact world state reloaded; no offline growth.");}else Record("reload","Load rejected; existing state retained.");}
        public void Fresh(bool reset)
        {
            string w=reset?Model.State.world:"food-"+Guid.NewGuid().ToString("N");int seed=Model.State.seed;Model=new FoodModel(w,"g-"+Guid.NewGuid().ToString("N"),seed);BindScope();SetActor(new Vector3(0,0,-2));RefreshVisuals();Record("reset","Clean seeded world. Acquired food knowledge and inventory cleared; old saves preserved.");
        }
        static string Stage(int i,bool established=false)=>i==0?"empty":i==1?(established?"regrowing":"seedling"):i==2?"growing":i==3?"ripe":"dead";
        void OnGUI()
        {
            if(Model==null)return;GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1280f,Screen.height/720f,1));var s=Model.State;
            var title=new GUIStyle(GUI.skin.label){fontSize=25,fontStyle=FontStyle.Bold};var text=new GUIStyle(GUI.skin.label){fontSize=16,wordWrap=true};var small=new GUIStyle(text){fontSize=14};
            GUI.Box(new Rect(0,0,1280,115),"");GUI.Label(new Rect(22,12,850,38),"STARFALL  /  THE FIRST BERRY",title);
            GUI.Label(new Rect(22,48,850,38),$"Energy {s.satiety/100f:F1}% • Hydration {s.hydration/100f:F1}% • Fullness {s.body.stomach/100f:F0}% • Berries {s.carriedFruit} • Seeds {s.seeds}",text);
            GUI.Label(new Rect(22,75,850,38),$"Reserve {s.body.fat/100f:F0}% • Protein {s.body.protein/100f:F0}% • Health {s.body.health/100f:F0}% • Fatigue {s.body.fatigue/100f:F0}% | {(s.body.dead?"AWAITING RETURN":s.hydration<2000?"THIRST: seek known freshwater":s.satiety<2000?"HUNGRY: use known food":s.knowsBerry?"Berry known":"Berry UNKNOWN")} | Tick {s.tick}",small);
            GUI.Box(new Rect(895,115,385,605),"");GUI.Label(new Rect(910,128,350,30),"PERCEIVE → DECIDE → ACT",new GUIStyle(title){fontSize=19});
            GUI.Label(new Rect(910,165,350,70),$"Bush: {s.fruitStock}/{2+(int)((uint)s.seed%2)} ripe • next {s.regrowthProgress}/30 s\nSeason: {(s.wetSeason?"wet":"dry")} • soil water {s.soilWater}\nGarden: {Stage(s.gardenStage,s.gardenEstablished)} • freshwater {s.freshwaterMl} ml",small);
            float used=0;int first=Log.Entries.Count;
            while(first>0){var e=Log.Entries[first-1];float h=small.CalcHeight(new GUIContent($"{e.tick:000}  {e.phase.ToUpperInvariant()}\n{e.result}"),350)+10;if(used+h>420&&first<Log.Entries.Count)break;used+=h;first--;}
            GUI.BeginGroup(new Rect(910,244,350,420));float y=0;
            for(int i=first;i<Log.Entries.Count;i++){var e=Log.Entries[i];string line=$"{e.tick:000}  {e.phase.ToUpperInvariant()}\n{e.result}";float h=small.CalcHeight(new GUIContent(line),350);GUI.Label(new Rect(0,y,350,h),line,small);y+=h+10;}
            GUI.EndGroup();
            GUI.Box(new Rect(0,576,895,144),"");GUI.enabled=!pending&&!Paused;
            if(GUI.Button(new Rect(15,590,165,32),"Learn berry (lesson)"))Queue(FoodAction.Inspect,"berry","Read safe designed discovery lesson");
            if(GUI.Button(new Rect(188,590,130,32),"Gather one"))Queue(FoodAction.Gather,"berry","Gather one ripe berry");
            if(GUI.Button(new Rect(326,590,110,32),"Eat berry"))Queue(FoodAction.Eat,"inventory","Eat known carried food");
            if(GUI.Button(new Rect(444,590,165,32),"Verify / drink fresh"))Queue(s.knowsSpring?FoodAction.Drink:FoodAction.Inspect,"spring","Visit maintained freshwater source");
            if(GUI.Button(new Rect(617,590,125,32),"Try seawater"))Queue(FoodAction.Drink,"sea","Inspect unsafe water rejection");
            if(GUI.Button(new Rect(750,590,130,32),s.gardenStage==3?"Harvest bed":"Plant seed"))Queue(s.gardenStage==3?FoodAction.Harvest:s.knowsPlanting?FoodAction.Plant:FoodAction.Inspect,"bed","Tend labelled irrigated berry bed");
            GUI.enabled=true;
            if(GUI.Button(new Rect(15,630,135,30),Auto?"Stop autonomy":"Meet needs"))Auto=!Auto;
            if(GUI.Button(new Rect(158,630,90,30),Paused?"Resume":"Pause"))Paused=!Paused;
            if(GUI.Button(new Rect(256,630,85,30),"Save"))Save();if(GUI.Button(new Rect(349,630,85,30),"Reload"))Reload();
            if(GUI.Button(new Rect(442,630,105,30),"New world"))Fresh(false);if(GUI.Button(new Rect(555,630,105,30),"Clean reset"))Fresh(true);
            if(GUI.Button(new Rect(668,630,98,30),"Wet / dry")){s.wetSeason=!s.wetSeason;Record("environment","Fixture season changed; regrowth obeys it.");}
            if(GUI.Button(new Rect(774,630,106,30),"Water bed"))Queue(FoodAction.Water,"bed","Maintain moist soil using finite freshwater");
            if(GUI.Button(new Rect(910,680,110,28),s.body.dead?"Return":"Camp aid")){if(s.body.dead)ExecuteNow(FoodAction.Return,"inventory");else Queue(FoodAction.CampAid,"inventory","Explicit assisted recovery; counted in save");}
            if(GUI.Button(new Rect(1025,680,110,28),"Rest / wake")){pending=false;s.body.resting=!s.body.resting;Auto=false;Record("rest","Rest toggled; shelter verified by location.");}
            if(GUI.Button(new Rect(1140,680,125,28),s.bags.Count>0?"Recover bag":"Other bush"))Queue(s.bags.Count>0?FoodAction.Recover:FoodAction.Gather,s.bags.Count>0?"refuge":"berry2","Visit observed resource / owned recovery chest");
            GUI.Label(new Rect(15,672,860,40),"Isolated Eden v0.1.3 • compressed growth: 30 s/berry, 60 s/garden • no model required",small);
            if(cameraView!=null)
            {
                WorldLabel("berry",s.knowsBerry?"STARFALL BERRY":"UNKNOWN BERRY\nRead signed lesson");
                if(pending&&target!="berry"&&Targets.ContainsKey(target))WorldLabel(target,target=="sea"?"SEA • NOT DRINKABLE":target.ToUpperInvariant());
            }
        }
        void WorldLabel(string id,string label)
        {
            var p=cameraView.WorldToScreenPoint(Targets[id].transform.position+Vector3.up*.6f);if(p.z<0)return;
            if(p.x<0||p.x>Screen.width*.70f||p.y<Screen.height*.20f||p.y>Screen.height*.84f)return;
            float x=p.x*1280/Screen.width,y=(Screen.height-p.y)*720/Screen.height;
            if(id!="berry")y+=45;
            var style=new GUIStyle(GUI.skin.box){fontSize=12,wordWrap=true};GUI.Box(new Rect(x-75,y-65,150,34),label,style);
        }
        IEnumerator Go(FoodAction a,string t,string why)
        {Queue(a,t,why);float start=Time.realtimeSinceStartup;while(pending&&Time.realtimeSinceStartup-start<15)yield return null;if(pending)throw new Exception("Action timed out");yield return new WaitForSeconds(.3f);}
        IEnumerator Capture(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(evidence,name+".png"));yield return new WaitForSeconds(.5f);}
        IEnumerator Acceptance()
        {
            yield return new WaitForSeconds(2);var checks=FoodChecks.Run(Path.Combine(evidence,"model-checks"));
            void Check(bool ok,string name){if(!ok)throw new Exception("PLAYER FAIL: "+name);checks.Add("player: "+name);}
            Check(!Model.State.knowsBerry,"starts unknown");yield return Capture("01-unknown-bush");
            yield return Go(FoodAction.Gather,"berry","Hungry, unknown berry: authority must reject");Check(Model.State.carriedFruit==0&&!Model.State.knowsBerry,"unknown gathering rejected at actual bush");
            yield return Go(FoodAction.Inspect,"berry","Learn from visible signed lesson");Check(Model.State.knowsBerry,"perception and lesson establish knowledge");
            yield return Go(FoodAction.Gather,"berry","Gather one purple-red berry");Check(Model.State.carriedFruit==1,"actual approach gathered one");
            int food=Model.State.satiety;yield return Go(FoodAction.Eat,"inventory","Hungry: eat known berry");Check(Model.State.satiety>food&&Model.State.carriedFruit==0&&Model.State.lastMealEvidence!="","eat outcome remembered");
            yield return Go(FoodAction.Gather,"berry","Deliberately revisit known bush");Check(Model.State.fruitStock==0,"bush depleted, not deleted");yield return Capture("02-depleted-bush");
            yield return Go(FoodAction.Gather,"berry","Depleted target rejection");Check(Model.State.lastCode=="depleted","no infinite harvest");
            Save();string snapshot=Model.Json();File.WriteAllText(Path.Combine(evidence,"saved-expected.json"),snapshot);Reload();Check(Model.Json()==snapshot,"live reload preserves exact state");
            yield return Go(FoodAction.Drink,"sea","Reject salt water");Check(Model.State.lastCode=="unsafe-water","actual seawater rejection");
            yield return Go(FoodAction.Inspect,"spring","Inspect maintained freshwater sign");int hydration=Model.State.hydration;
            yield return Go(FoodAction.Drink,"spring","Drink verified freshwater");Check(Model.State.hydration>hydration&&Model.State.freshwaterMl==1750,"actual freshwater effect");
            yield return Go(FoodAction.Inspect,"bed","Learn moist-soil cultivation lesson");int seedBefore=Model.State.seeds;yield return Go(FoodAction.Plant,"bed","Remember food outcome; plant tutorial seed");Check(Model.State.seeds==seedBefore-1,"planting consumes seed");yield return Capture("03-seedling");
            yield return new WaitForSeconds(61);Check(Model.State.gardenStage==3&&Model.State.fruitStock>0,"visible garden growth and bush regrowth");yield return Capture("04-ripe-regrowth");
            yield return Go(FoodAction.Harvest,"bed","Harvest mature berry and seed");Check(Model.State.seeds>=1,"garden harvest");
            Save();string saved=Model.Json();File.WriteAllText(Path.Combine(evidence,"saved-expected.json"),saved);var savedModel=Model;Fresh(false);
            Check(!Model.State.knowsBerry&&Model.State.carriedFruit==0&&Model.State.seeds==1&&Model.State.tick==0,"fresh world clean isolation");
            Model=savedModel;BindScope();SetActor(Model.State.actorPosition);Paused=true;Record("acceptance","PASS: loop, safety, growth and isolation. Quit/relaunch verification next.");
            yield return Capture("05-loop-complete");Closeup();yield return Capture("07-bush-closeup");WriteReport("runtime-report.json",checks);Application.Quit(0);
        }
        void Closeup(){cameraView.transform.position=new Vector3(-.4f,3,-2.5f);cameraView.transform.LookAt(new Vector3(-4,1,3));cameraView.fieldOfView=42;}
        IEnumerator EdenAcceptance()
        {
            yield return new WaitForSeconds(91);
            if(Model.State.nextSeed<1||Model.State.drops.Count==0)throw new Exception("No actual fruit drop");
            cameraView.transform.position=Site(2)+new Vector3(3,2.5f,-4);cameraView.transform.LookAt(Site(2)+Vector3.up*.4f);cameraView.fieldOfView=40;
            Record("acceptance","Seed drop observed from unharvested fruit, not injected.");yield return Capture("eden-01-fallen-seeds");
            Save();string before=Model.Json();Reload();if(Model.Json()!=before)throw new Exception("Dropped-seed reload mismatch");
            yield return new WaitForSeconds(31);
            if(!Model.State.bushes.Exists(b=>b.site>=2))throw new Exception("Expected seeded rare germination absent");
            Record("acceptance","Rare natural germination observed for declared fixture seed "+Model.State.seed);yield return Capture("eden-02-rare-germination");
            yield return new WaitForSeconds(61);Paused=true;
            if(!Model.State.bushes.Exists(b=>b.site>=2&&b.growth==EdenEcology.Mature))throw new Exception("Natural seedling failed to mature");
            yield return Capture("eden-03-new-bush");Save();int population=EdenEcology.Living(Model.State);Fresh(false);
            if(Perception.Sense(Model.State.tick).Exists(o=>o.id=="berry3"||o.id=="berry4")||Model.State.knowsBerry||Model.State.drops.Count!=0)throw new Exception("Natural ecology interaction target leaked across fresh world");
            yield return Capture("eden-04-fresh-world-isolation");Save();WriteReport("eden-report.json",new List<string>{"unharvested fruit dropped finite seeds","seed age and outcome survived actual reload","rare natural germination seed "+Model.State.seed,"natural seedling matured over time","population <=4: "+population,"fresh world clears natural plants, seed state, knowledge and interaction targets"});Application.Quit(0);
        }
        IEnumerator MortalityAcceptance()
        {
            yield return new WaitForSeconds(2);var s=Model.State;s.knowsBerry=true;s.berryEvidence=s.generation+".lesson.boundary-fixture";s.seenBed=true;s.carriedFruit=1;s.body.health=1;s.body.fat=0;s.body.deficitSeconds=3600;s.satiety=0;s.hydration=10000;
            Record("fixture","Synthetic final starvation boundary after prolonged deficit; not normal starting physiology.");
            yield return new WaitForSeconds(1.1f);Paused=true;if(!s.body.dead||s.deaths.Count!=1)throw new Exception("Death transition failed");
            yield return Capture("mortality-01-verified-death");Save();Reload();s=Model.State;int tick=s.tick;int stock=s.fruitStock;string hash=s.deaths[0].hash;
            ExecuteNow(FoodAction.Return,"inventory");if(s.tick!=tick||s.fruitStock!=stock||s.deaths[0].hash!=hash||s.body.dead)throw new Exception("Return rewound world");
            ExecuteNow(FoodAction.Recover,"refuge");if(s.carriedFruit!=1)throw new Exception("Recovery bag failed");
            Record("remembered insight",s.deaths[0].lesson);yield return Capture("mortality-02-return-and-insight");
            int energy=s.satiety;Paused=false;yield return Go(FoodAction.Eat,"inventory","Remembered death insight "+s.deaths[0].id+": use known berry to restore energy");Paused=true;
            if(s.satiety<=energy||s.lastMealEvidence=="")throw new Exception("Remembered lesson did not guide a verified meal");
            yield return Capture("mortality-03-lesson-used");Save();
            WriteReport("mortality-report.json",new List<string>{"synthetic prolonged-starvation boundary caused verified death","death saved/reloaded","same-world same-inhabitant return without ecology/time rewind","owned inventory recovered once","one grounded existing-mechanic lesson","remembered death lesson followed by verified energy-restoring meal"});Application.Quit(0);
        }
        IEnumerator ResumeAcceptance()
        {
            Paused=true;yield return new WaitForSeconds(2);string expected=File.ReadAllText(Path.Combine(evidence,"saved-expected.json"));
            // Start restored before the first physics step, and this coroutine pauses synchronously.
            if(Model.Json()!=expected)throw new Exception("Quit/relaunch persistence mismatch");
            Record("acceptance","PASS: separate executable process restored exact saved ecology and knowledge.");yield return Capture("06-relaunch-restored");
            WriteReport("relaunch-report.json",new List<string>{"separate-process exact snapshot match"});Application.Quit(0);
        }
        void WriteReport(string name,List<string> checks)
        {
            frames.Sort();File.WriteAllText(Path.Combine(evidence,name),JsonUtility.ToJson(new RunReport{status="PASS",scope="compiled isolated food fixture",limitations="Scripted runtime; human input acceptance and combined-world integration separate",checks=checks,frames=frames.Count,p95=frames.Count==0?0:frames[(int)((frames.Count-1)*.95)],width=Screen.width,height=Screen.height},true));
        }
    }
}
