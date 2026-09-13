using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using CityLife.World;

namespace Starfall.EnvironmentFoundation
{
    public sealed class EnvironmentWorld : MonoBehaviour
    {
        public EnvironmentClock Clock=new EnvironmentClock(1904243);
        public IEnvironmentSurface Surface=new CoastalEnvironmentSurface();
        public ExposureState Exposure=new ExposureState();
        public ExposureState PlayerExposure=new ExposureState();
        public EnvironmentWalker Walker;public Camera View;
        public bool Stress;public int BodyCount=>Stress?128:6;public int ParticleCount=>Stress?512:128;
        public float LastStepMs; public bool Auto;
        readonly List<Transform> reeds=new List<Transform>(); readonly List<Vector3> roots=new List<Vector3>();
        readonly List<Transform> drops=new List<Transform>();
        Transform wave,arrow,inhabitant; Material waterMaterial;
        bool sheltered; string notice="";float yaw=20,pitch=12;
        public Vector3 ProbePosition=>inhabitant==null?Vector3.zero:inhabitant.position;
        public bool ProbeSheltered=>sheltered;
        public Vector3 VegetationLean=>reeds.Count==0?Vector3.zero:reeds[0].position-roots[0]-Vector3.up*.9f;
        public Vector3 ArrowDirection=>arrow==null?Vector3.zero:arrow.forward;
        public Vector3 VisualPositionSignature {get{Vector3 sum=Vector3.zero;foreach(var t in reeds)sum+=t.position;foreach(var t in drops)sum+=t.position;return sum;}}
        public static Material Material(Color color){var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.color=color;return m;}
        public static GameObject Cube(string name,Vector3 p,Vector3 scale,Color color)
        {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.position=p;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=Material(color);return g;}
        void Start()
        {
            Auto=Array.IndexOf(System.Environment.GetCommandLineArgs(),"-environmentAuto")>=0;
            Stress=Array.IndexOf(System.Environment.GetCommandLineArgs(),"-environmentStress")>=0;
            if(Auto)Screen.SetResolution(1280,720,FullScreenMode.Windowed);
            Time.fixedDeltaTime=EnvironmentClock.Dt;Time.maximumDeltaTime=.1f;Physics.gravity=new Vector3(0,-9.81f,0);
            QualitySettings.vSyncCount=0;Application.targetFrameRate=60;Application.runInBackground=true;
            var terrain=CoastalTerrain.Create(transform);terrain.name="Preserved coastal terrain fixture";
            var water=CoastalWater.Create(transform);water.name="Coastal water fixture";
            var environmentWater=Shader.Find("Starfall/EnvironmentWater");
            if(environmentWater==null||!environmentWater.isSupported)throw new InvalidOperationException("Environment water shader unavailable");
            foreach(var renderer in water.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=new Material(environmentWater);
            // Shared environment vector overrides material flow through the additive test overlay.
            Cube("Test deck",new Vector3(-25,39.5f,0),new Vector3(34,1,25),new Color(.32f,.29f,.24f));
            Cube("Collision wall",new Vector3(-25,41,10),new Vector3(22,2,.4f),new Color(.65f,.42f,.2f));
            var ramp=Cube("30 degree slope",new Vector3(-32,40.8f,3),new Vector3(5,.3f,5),Color.gray);ramp.transform.rotation=Quaternion.Euler(-30,0,0);
            Cube("Shelter roof",new Vector3(-37,43,-7),new Vector3(7,.3f,6),new Color(.4f,.5f,.5f));
            foreach(float x in new[]{-40f,-34f})foreach(float z in new[]{-9f,-5f})Cube("Shelter post",new Vector3(x,41.5f,z),new Vector3(.2f,3,.2f),Color.gray);
            for(int i=0;i<24;i++)
            {var r=Cube("Flexible vegetation",new Vector3(-40+i*.8f,40.9f,-1),new Vector3(.12f,1.8f,.12f),new Color(.35f,.65f,.35f));Destroy(r.GetComponent<Collider>());reeds.Add(r.transform);roots.Add(r.transform.position-Vector3.up*.9f);}
            for(int i=0;i<ParticleCount;i++){var d=Cube("Precipitation",Vector3.zero,new Vector3(.025f,.18f,.025f),new Color(.65f,.8f,1));Destroy(d.GetComponent<Collider>());drops.Add(d.transform);}
            for(int i=0;i<BodyCount;i++)
            {var pos=Stress?new Vector3(-38+(i%16)*1.1f,41,-9+(i/16)*1.1f):new Vector3(-26+i,41,-4);var g=Cube("Loose wind object "+i,pos,Vector3.one*.45f,new Color(1,.7f,.3f));var b=g.AddComponent<Rigidbody>();b.mass=.5f+i%6;var force=g.AddComponent<EnvironmentBody>();force.World=this;force.DragArea=.08f;}
            wave=Cube("Wind driven water sample",new Vector3(-17,40.1f,3),new Vector3(6,.1f,6),Color.cyan).transform;Destroy(wave.GetComponent<Collider>());waterMaterial=wave.GetComponent<Renderer>().material;
            waterMaterial.shader=environmentWater;
            arrow=Cube("Wind direction and strength",new Vector3(-25,44,0),new Vector3(.2f,.2f,3),Color.yellow).transform;Destroy(arrow.GetComponent<Collider>());
            inhabitant=GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;inhabitant.name="Local exposure probe (not NPC brain)";inhabitant.position=new Vector3(-31,41,-6);Destroy(inhabitant.GetComponent<Collider>());
            inhabitant.GetComponent<Renderer>().sharedMaterial=Material(new Color(.8f,.7f,.5f));
            var player=new GameObject("Environment walker");player.transform.position=new Vector3(0,.2f,0);player.AddComponent<CharacterController>();Walker=player.AddComponent<EnvironmentWalker>();Walker.World=this;
            View=Camera.main;if(View==null){View=new GameObject("Environment camera").AddComponent<Camera>();View.tag="MainCamera";}
            View.farClipPlane=1500;View.clearFlags=CameraClearFlags.SolidColor;View.backgroundColor=new Color(.17f,.25f,.35f);
            var sun=new GameObject("Environment sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(45,-30,0);
            RenderSettings.ambientLight=new Color(.5f,.55f,.65f);
            EnvironmentPresentation.Apply(this);
            if(Auto) gameObject.AddComponent<EnvironmentPlayerEvidence>();
        }
        void FixedUpdate()
        {
            long started=Stopwatch.GetTimestamp();Clock.Step();var s=Clock.Sample;
            sheltered=inhabitant!=null&&inhabitant.position.x<-34;
            if(!Clock.Paused)Exposure.Step(s.temperature,s.wind.magnitude,s.precipitation,sheltered);
            if(!Clock.Paused&&Walker!=null)PlayerExposure.Step(s.temperature,s.wind.magnitude,s.precipitation,Physics.Raycast(Walker.transform.position+Vector3.up*2,Vector3.up,8));
            if(inhabitant!=null&&!Clock.Paused&&Exposure.Cold)inhabitant.position=Vector3.MoveTowards(inhabitant.position,new Vector3(-37,41,-7),1.5f*EnvironmentClock.Dt);
            LastStepMs=(float)((Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency);
        }
        void Update()
        {
            var k=Keyboard.current;
            if(!Auto&&k!=null)
            {
                if(k.pKey.wasPressedThisFrame)SetPause(!Clock.Paused);
                if(k.f5Key.wasPressedThisFrame)Save();if(k.f9Key.wasPressedThisFrame)Load();
                if(k.escapeKey.wasPressedThisFrame)Cursor.lockState=CursorLockMode.None;
                Walker.InputDirection=View.transform.right*((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0))+Vector3.ProjectOnPlane(View.transform.forward,Vector3.up).normalized*((k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
                if(k.spaceKey.wasPressedThisFrame)Walker.Jump=true;
                if(Mouse.current!=null&&Mouse.current.rightButton.isPressed){var d=Mouse.current.delta.ReadValue();yaw+=d.x*.15f;pitch=Mathf.Clamp(pitch-d.y*.15f,-80,80);}
                View.transform.SetPositionAndRotation(Walker.transform.position+Vector3.up*1.65f,Quaternion.Euler(pitch,yaw,0));
            }
            if(Auto){View.transform.position=new Vector3(-9,49,-21);View.transform.LookAt(new Vector3(-28,41,0));}
            var s=Clock.Sample;float seconds=Clock.Tick*EnvironmentClock.Dt;Vector3 direction=s.wind.normalized;
            View.backgroundColor=Color.Lerp(new Color(.17f,.25f,.35f),new Color(.09f,.11f,.14f),s.precipitation);
            if(inhabitant!=null)inhabitant.GetComponent<Renderer>().sharedMaterial.color=Exposure.Cold?new Color(.25f,.6f,1):new Color(.8f,.7f,.5f);
            for(int i=0;i<reeds.Count;i++)
            {Vector3 lean=direction*(s.wind.magnitude*.022f*(1+.15f*Mathf.Sin(seconds*2+i)));reeds[i].position=roots[i]+Vector3.up*.9f+lean*.5f;reeds[i].rotation=Quaternion.FromToRotation(Vector3.up,Vector3.up+lean);}
            for(int i=0;i<drops.Count;i++)
            {drops[i].gameObject.SetActive(i<s.precipitation*ParticleCount);float age=Mathf.Repeat(seconds+i*.173f,2);drops[i].position=new Vector3(-40+(i*7%29),48,-10+(i*11%20))+s.wind*age*.15f+Vector3.down*age*4;drops[i].rotation=Quaternion.FromToRotation(Vector3.up,(-Vector3.up*8+s.wind*.3f).normalized);}
            if(arrow!=null){arrow.rotation=Quaternion.LookRotation(s.wind.sqrMagnitude>.01f?s.wind:Vector3.forward);arrow.localScale=new Vector3(.2f,.2f,Mathf.Max(.3f,s.wind.magnitude*.2f));}
            if(wave!=null)waterMaterial.color=new Color(.04f,.55f,.65f);
            Shader.SetGlobalVector("_StarfallWind",s.wind);Shader.SetGlobalFloat("_StarfallEnvironmentTime",seconds);
            Shader.SetGlobalFloat("_StarfallWeather",s.precipitation);
        }
        public void SetPause(bool value){Clock.Paused=value;Time.timeScale=value?0:1;}
        string SavePath=>Path.Combine(Application.persistentDataPath,"environment-fixture-v1.json");
        public EnvironmentSave CaptureSave(){var s=Clock.Save(Surface.WorldId,Surface.Revision,Exposure.Wetness);s.cold=Exposure.Cold;s.apparentTemperature=Exposure.ApparentTemperature;s.playerWetness=PlayerExposure.Wetness;s.playerCold=PlayerExposure.Cold;s.playerApparentTemperature=PlayerExposure.ApparentTemperature;return s;}
        public bool TryRestore(EnvironmentSave s){if(!Clock.TryLoad(s,Surface.WorldId,Surface.Revision))return false;Exposure.Wetness=s.wetness;Exposure.Cold=s.cold;Exposure.ApparentTemperature=s.apparentTemperature;PlayerExposure.Wetness=s.playerWetness;PlayerExposure.Cold=s.playerCold;PlayerExposure.ApparentTemperature=s.playerApparentTemperature;return true;}
        public void Save(){EnvironmentClock.WriteSave(SavePath,CaptureSave());notice="Environment clock/exposure saved; bodies are transient";}
        public void Load(){try{if(!TryRestore(JsonUtility.FromJson<EnvironmentSave>(File.ReadAllText(SavePath)))){notice="Save rejected; current state retained";return;}notice="Environment restored";}catch(Exception){notice="Save unavailable or invalid; current state retained";}}
        void OnGUI()
        {
            var s=Clock.Sample;GUI.Box(new Rect(12,12,780,176),"");GUI.color=Color.white;
            GUI.Label(new Rect(24,20,670,25),"STARFALL • ENVIRONMENT FOUNDATION v1 • COMPONENT TEST PLAYER");
            GUI.Label(new Rect(24,46,670,25),$"{s.target}  | wind toward X {s.wind.x:F1}, Z {s.wind.z:F1} m/s | air {s.temperature:F1} °C | tick {Clock.Tick}");
            GUI.Label(new Rect(24,72,670,25),$"Local probe: feels {Exposure.ApparentTemperature:F1} °C | wet {Exposure.Wetness:P0} | {(Exposure.Cold?"COLD — SEEK SHELTER":"comfortable")} | shelter {sheltered}");
            GUI.Label(new Rect(24,98,670,25),$"{(Clock.Paused?"PAUSED":"RUNNING")} | WASD / RMB look / Space jump / P pause / F5 save / F9 load");
            GUI.Label(new Rect(24,124,670,25),notice+" "+(Walker==null?"":Walker.Boundary));
            GUI.Label(new Rect(24,148,740,25),$"Player: feels {PlayerExposure.ApparentTemperature:F1} °C | wet {PlayerExposure.Wetness:P0} | {(PlayerExposure.Cold?"COLD — FIND SHELTER":"comfortable")} | physical world is finite");
            GUI.Label(new Rect(15,Screen.height-36,1000,28),"Elevated apparatus = test harness. Coastal terrain below = preserved component fixture. Main world / NPC integration pending.");
        }
    }
}
