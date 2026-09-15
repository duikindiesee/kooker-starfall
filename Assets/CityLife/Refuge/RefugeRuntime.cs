using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Starfall.EnvironmentFoundation;
using Starfall.EnvironmentZones;
using CityLife.World;
namespace Starfall.Refuge
{
 public sealed class RefugeRuntime:MonoBehaviour
 {
  public const int GeometryMask=(1<<8)|(1<<10);
  public const string WorldId="starfall.refuge-regional.v1", Revision="terrain-r2-weathered-banks.refuge2";
  public Camera View;public CharacterController Body;public Vector3 Hearth,Bed,Storage;public Collider Roof;public GameObject Flame;public Light FireLight;
  public bool IntegratedMode; public Vector3 OriginOffset;
  public readonly HearthState Fire=new HearthState();public readonly EnvironmentClock Clock=new EnvironmentClock(1904243);
  readonly CaveZonePolicy policy=new CaveZonePolicy(WorldId,Revision,"first-refuge",3,.1f,.01f);
  public bool GeometryVerified,WaterVerified,Resting;public float FloorY,IngressY,MaximumDesignWaterY=float.NaN;public int WaterMeshCount;public string Notice="Explore the first refuge";
  int restTicks;public bool Sleeping=>Resting&&restTicks>=150;GameObject[] storedLogs;float yaw,pitch,fall;bool paused;ZoneExposure exposure;public ZoneWeather Local;string output;
  bool Automated=>System.Environment.GetCommandLineArgs().Contains("-refugeAcceptance");
  [Serializable] public class CheckResult {public string name;public bool pass;public float value;}
  [Serializable] public class Report {public string world=WorldId,revision=Revision,scope="Compiled regional refuge player, scripted controller acceptance; not human input or integrated NPC acceptance";public List<CheckResult> checks=new List<CheckResult>();public float floor,ingress,waterUpper=-1.889f;public int frames;public float frameP95;}
  readonly Report report=new Report();readonly List<float> frames=new List<float>();
  void Start(){storedLogs=Enumerable.Range(0,6).Select(i=>GameObject.Find("Stored fuel "+i)).ToArray();yaw=View.transform.eulerAngles.y;pitch=View.transform.eulerAngles.x;Application.targetFrameRate=60;ValidateGeometry();Fire.Extinguish();if(Automated)StartCoroutine(Accept());}
  public void ValidateGeometry()
  {
   Physics.SyncTransforms();FloorY=float.PositiveInfinity;bool valid=true;
   for(float x=-12;x<=-7;x+=.5f)for(float z=-1.5f;z<=1.5f;z+=.5f){Vector3 p=OriginOffset+new Vector3(x,2.1f,z);if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(Hearth.x,Hearth.z))<1.15f||Vector2.Distance(new Vector2(p.x,p.z),new Vector2(Storage.x,Storage.z))<.95f)continue;if(!Physics.Raycast(p,Vector3.down,out var hit,2,GeometryMask,QueryTriggerInteraction.Ignore)){valid=false;continue;}FloorY=Mathf.Min(FloorY,hit.point.y);var overlaps=Physics.OverlapCapsule(hit.point+Vector3.up*.36f,hit.point+Vector3.up*1.5f,.3f,GeometryMask,QueryTriggerInteraction.Ignore);if(overlaps.Length>0){valid=false;Debug.Log("REFUGE_CLEARANCE "+p+" blocked by "+string.Join(",",overlaps.Select(c=>c.name)));}}
   IngressY=OriginOffset.y+1.8f; // Closed solid floor perimeter: only connected opening is the east ramp crest.
   bool crest=true;for(float z=-1.8f;z<=1.8f;z+=.2f){if(!Physics.Raycast(OriginOffset+new Vector3(-6.05f,2.1f,z),Vector3.down,out var h,1,GeometryMask))crest=false;else IngressY=Mathf.Min(IngressY,h.point.y);}
   GeometryVerified=valid&&crest&&Roof!=null&&Roof.enabled;
   WaterVerified=true;WaterMeshCount=0;float upper=float.NegativeInfinity;
   foreach(var r in FindObjectsByType<MeshRenderer>())
   {
    var m=r.sharedMaterial;if(m==null)continue;
    bool regional=m.shader.name=="CityLife/CoastalWater";
    var source=r.GetComponentInParent<NpcInteractable>();
    bool freshwater=r.gameObject.name=="Terrain-following shallow seep water"&&
     source!=null&&source.StableId=="spring-food"&&m.name=="Seep clear shallow freshwater";
    if(!regional&&!freshwater)continue;
    WaterMeshCount++;
    if(regional)
    {
     float strength=m.GetFloat("_WaveStrength");
     if(!CaveZonePolicy.Finite(strength)||strength<0||strength>1)WaterVerified=false;
    }
    var filter=r.GetComponent<MeshFilter>();var mesh=filter==null?null:filter.sharedMesh;
    if(mesh==null||mesh.vertexCount==0){WaterVerified=false;continue;}
    // Conservatively consider both water optics: the regional water shader's
    // displacement bound is .047 m; the bounded Lit seep has none. .111 m
    // retains the prior storm-design margin without a guessed exclusion.
    foreach(var v in mesh.vertices)
    {
     float y=r.transform.TransformPoint(v).y;
     if(!CaveZonePolicy.Finite(y)||Mathf.Abs(y)>10000){WaterVerified=false;continue;}
     upper=Mathf.Max(upper,y+.111f);
    }
   }
   MaximumDesignWaterY=upper;
   WaterVerified &= WaterMeshCount>0&&CaveZonePolicy.Finite(upper);
  }
  public ZoneWeather Sample(Vector3 p)
  {
   if(IntegratedMode&&Body!=null)
   {
    var regional=Body.GetComponent<IntegratedEnvironment>();
    if(regional!=null)return Sample(p,regional.OutdoorSample());
   }
   var s=Clock.Sample;var outside=new OutdoorWeather{WindX=s.wind.x,WindY=s.wind.y,WindZ=s.wind.z,AirC=s.temperature,Rain01=s.precipitation};
   return Sample(p,outside);
  }
  public ZoneWeather Sample(Vector3 p,OutdoorWeather outside)
  {
   Vector3 local=p-OriginOffset;
   bool inside=local.x<=-6&&local.x>=-12.6f&&Mathf.Abs(local.z)<=2&&local.y>=1.75f&&local.y<5;
   bool roof=inside&&Roof!=null&&Roof.enabled&&Physics.Raycast(p+Vector3.up*.1f,Vector3.up,8,GeometryMask,QueryTriggerInteraction.Ignore);
   var windDirection=new Vector3(outside.WindX,outside.WindY,outside.WindZ);
   float wind=0;if(inside&&windDirection.sqrMagnitude>.001f&&Physics.Raycast(p,-windDirection.normalized,10,GeometryMask,QueryTriggerInteraction.Ignore))wind=1;
   float heat=Fire.HeatAt(Vector3.Distance(p,Hearth+Vector3.up*.8f));
   var probe=new CaveProbe{WorldId=WorldId,WorldRevision=Revision,ZoneId="first-refuge",MetresInside=inside?-6-local.x:0,GeometryVerified=GeometryVerified&&roof,WindOcclusion01=wind,RainOcclusion01=roof?1:0,ThermalVerified=heat>0,RockAirTargetC=Mathf.Clamp(outside.AirC+heat,-30,30),WaterBoundKnown=WaterVerified,FloorKnown=GeometryVerified,IngressKnown=GeometryVerified,LowestRefugeFloorY=FloorY,LowestConnectedIngressY=IngressY,MaximumDesignWaterY=MaximumDesignWaterY};
   return CaveZoneEvaluator.Evaluate(policy,outside,probe);
  }
  bool SafeFire()=>GeometryVerified&&WaterVerified&&Roof!=null&&Roof.enabled&&
   Mathf.Min(FloorY,IngressY)-MaximumDesignWaterY>=policy.FreeboardMetres&&
   Vector3.Distance(Bed,Hearth)>2.5f;
  public void TogglePause(){paused=!paused;}
  public bool ToggleFire(){if(Vector3.Distance(Body.transform.position,Hearth)>=2.5f||Resting)return false;if(Fire.Burning){Fire.Extinguish();return true;}return Ignite();}
  public bool TransferLog()=>!Resting&&Vector3.Distance(Body.transform.position,Storage)<2&&Fire.AddLog();
  public bool Ignite(){var w=Sample(Hearth+Vector3.up*.5f);return Fire.Ignite(w.Rain01,w.WindSpeed,SafeFire()&&w.Valid);}
  void FixedUpdate(){if(IntegratedMode||paused)return;if(Resting&&restTicks<150)restTicks++;Clock.Step();var w=Sample(Hearth+Vector3.up*.5f);Fire.Step(w.Rain01,w.WindSpeed,SafeFire()&&w.Valid);Local=Sample(Body.transform.position+Vector3.up);exposure.Step(Local,false);}
  // Called once by the regional clock owner, never while its pause gate is closed.
  public void StepIntegrated(OutdoorWeather outside)
  {
   if(!IntegratedMode)return;
   if(Resting&&restTicks<150)restTicks++;
   var w=Sample(Hearth+Vector3.up*.5f,outside);
   Fire.Step(w.Rain01,w.WindSpeed,SafeFire()&&w.Valid);
   Local=Sample(Body.transform.position+Vector3.up,outside);exposure.Step(Local,false);
  }
  public void Move(Vector3 direction,float seconds){if(Resting||paused)return;fall=Body.isGrounded?-2:Mathf.Max(-30,fall-9.81f*seconds);Body.Move((Vector3.ClampMagnitude(direction,1)*3+Vector3.up*fall)*seconds);if(Body.transform.position.y < -1||Mathf.Abs(Body.transform.position.x)>87||Body.transform.position.z < -52||Body.transform.position.z>142){Place(new Vector3(-4,.05f,-4));Notice="Recovered to dry spawn";}}
  public void Place(Vector3 p){Body.enabled=false;Body.transform.position=p;Body.enabled=true;fall=0;}
  public bool Rest(){if(Vector3.Distance(Body.transform.position,Bed)>2)return false;Resting=!Resting;restTicks=0;Notice=Resting?"Resting on the mat — dreams and memory are planned":"Awake";return true;}
  void Update()
  {
   if(Automated&&frames.Count<100000)frames.Add(Time.unscaledDeltaTime*1000);for(int i=0;i<storedLogs.Length;i++)if(storedLogs[i]!=null)storedLogs[i].SetActive(i<Fire.ReserveLogs);Flame.SetActive(Fire.Burning);FireLight.enabled=Fire.Burning;FireLight.intensity=Fire.Burning?2.5f+.15f*Mathf.Sin(Fire.Tick*.17f):0;
   if(!IntegratedMode&&!Automated&&Application.isFocused){var k=Keyboard.current;var m=Mouse.current;if(k!=null){if(k.pKey.wasPressedThisFrame)TogglePause();if(k.eKey.wasPressedThisFrame)ToggleFire();if(k.rKey.wasPressedThisFrame)Rest();if(k.tKey.wasPressedThisFrame&&Vector3.Distance(Body.transform.position,Storage)<2)Notice=TransferLog()?"Moved one stored log to hearth":"Storage empty or hearth full";if(k.escapeKey.wasPressedThisFrame)Resting=false;Vector3 d=new Vector3((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),0,(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));Move(Quaternion.Euler(0,yaw,0)*d,Mathf.Min(Time.deltaTime,.05f));}if(m!=null&&m.rightButton.isPressed){var delta=m.delta.ReadValue();yaw+=delta.x*.12f;pitch=Mathf.Clamp(pitch-delta.y*.12f,-65,65);}}
   if(!IntegratedMode&&!Automated){View.transform.position=Body.transform.position+Vector3.up*(Resting?.45f:1.65f);View.transform.rotation=Quaternion.Euler(pitch,yaw,Resting?12:0);}
  }
  void OnGUI(){if(IntegratedMode)return;GUI.Box(new Rect(12,12,690,130),"STARFALL — FIRST REFUGE 0.0.8-refuge.2");GUI.Label(new Rect(25,38,670,24),"WASD walk · RMB look · E fire · T stored fuel · R rest/wake · P pause");GUI.Label(new Rect(25,62,670,24),$"{Fire.Reason} · fuel {Fire.FuelTicks/50f:F0}s · stored logs {Fire.ReserveLogs} · {(Sleeping?"SLEEPING":Resting?"RESTING":"AWAKE")}");GUI.Label(new Rect(25,86,670,24),$"{Clock.Sample.target}: local wind {Local.WindSpeed:F1}m/s · rain {Local.Rain01:P0} · feels {exposure.ApparentC:F1}C");GUI.Label(new Rect(25,110,670,24),Notice);if(Resting){GUI.Box(new Rect(Screen.width/2-190,Screen.height-100,380,70),(Sleeping?"Sleeping in the first refuge":"Settling onto the mat")+"\nR / Escape to wake · dream integration planned");}}
  void Check(string n,bool p,float v=0){report.checks.Add(new CheckResult{name=n,pass=p,value=v});Debug.Log("REFUGE_CHECK "+n+" "+p+" "+v);}
  IEnumerator Route(Vector3 target){int steps=0;while(Vector2.Distance(new Vector2(Body.transform.position.x,Body.transform.position.z),new Vector2(target.x,target.z))>.15f&&steps++<650){Move(new Vector3(target.x-Body.transform.position.x,0,target.z-Body.transform.position.z),.02f);View.transform.position=Body.transform.position+Vector3.up*1.65f;View.transform.LookAt(Hearth+Vector3.up*.7f);yield return new WaitForFixedUpdate();}Check("route "+target,steps<650,steps);}
  IEnumerator Capture(string name,Vector3 p,Vector3 target){View.transform.position=p;View.transform.LookAt(target);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSeconds(.25f);}
  IEnumerator Accept()
  {
   string[] args=System.Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-refugeEvidence");output=at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"refuge-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
   yield return new WaitForSeconds(2);Check("geometry standing clearance",GeometryVerified);Check("regional water material and mesh invariants",WaterVerified);Check("floor freeboard",FloorY+1.889f>=.75f,FloorY+1.889f);Check("connected ingress freeboard",IngressY+1.889f>=.75f,IngressY+1.889f);
   yield return Capture("01-entrance",new Vector3(-2,3.7f,-5),new Vector3(-10,3,0));
   yield return Route(new Vector3(-2,0,-4));yield return Route(new Vector3(-2,0,0));yield return Route(new Vector3(-7,1.8f,0));yield return Route(new Vector3(-11,1.8f,0));yield return Route(new Vector3(-7,1.8f,0));yield return Route(new Vector3(-2,0,0));yield return Route(new Vector3(-2,0,-4));yield return Route(new Vector3(-4,0,-4));
   Check("back wall blocks body",Physics.CapsuleCast(new Vector3(-11,2.2f,0),new Vector3(-11,3.3f,0),.3f,Vector3.left,3,GeometryMask));Check("roof blocks camera",Physics.SphereCast(new Vector3(-10,3.4f,0),.2f,Vector3.up,out var roofHit,4,GeometryMask));
   Check("round trip stayed dry",Body.transform.position.y>-.2f,Body.transform.position.y);
   Check("remote fire action refused",!ToggleFire());yield return Route(new Vector3(-2,0,-4));yield return Route(new Vector3(-2,0,0));yield return Route(new Vector3(-7,1.8f,0));Clock.Tick=400;Check("hearth ignition through interaction gate",ToggleFire()&&Fire.Burning);yield return Capture("02-hearth-interior",new Vector3(-6.8f,3.3f,-1.5f),new Vector3(-11,2.5f,.4f));
   Check("visible bounded hearth",Flame.activeSelf&&FireLight.enabled&&Flame.GetComponent<Renderer>().bounds.extents.x<.55f);Check("fuel consumed in player",Fire.FuelTicks<6000);int savedFuel=Fire.FuelTicks;long savedTick=Fire.Tick;TogglePause();yield return new WaitForSeconds(.25f);Check("pause freezes hearth",Fire.FuelTicks==savedFuel&&Fire.Tick==savedTick);TogglePause();
   var sheltered=Sample(new Vector3(-11,2.8f,0));var exposed=Sample(new Vector3(-4,2.8f,0));Check("directional wind attenuation",sheltered.WindSpeed<exposed.WindSpeed*.15f,sheltered.WindSpeed);Check("hearth local warming",Sample(new Vector3(-9,2.6f,1.35f)).AirC>Clock.Sample.temperature,Sample(new Vector3(-9,2.6f,1.35f)).AirC-Clock.Sample.temperature);Check("roof rain attenuation",sheltered.RainMultiplier<.02f,sheltered.RainMultiplier);
   Clock.Tick=4900;var storm=Sample(new Vector3(-11,2.8f,0));Check("storm interior rain attenuation",storm.Rain01<.02f,storm.Rain01);Check("exterior remains exposed",Sample(new Vector3(-4,2.8f,0)).RainMultiplier==1);
   yield return Capture("03-storm-shelter",new Vector3(-11,3,-1),new Vector3(-3,3.2f,0));
   Check("storm extinguishes exposed hearth",!Fire.Burning);
   bool previous=Roof.enabled;Roof.enabled=false;var missing=Sample(new Vector3(-11,2.8f,0));Fire.Step(0,0,SafeFire());Check("removed roof withholds shelter and kills fire",!missing.GeometryCredited&&!Fire.Burning);Roof.enabled=previous;
   yield return Route(new Vector3(-2,0,-4));yield return Route(new Vector3(-2,0,0));yield return Route(new Vector3(-7,1.8f,0));yield return Route(new Vector3(-10.5f,1.8f,-.7f));Check("rest interaction",Rest());yield return Capture("04-rest",Bed+new Vector3(.8f,.45f,0),Hearth+Vector3.up*.5f);yield return new WaitForSeconds(3.2f);Check("rest state visible",Resting);Check("sleep after settling",Sleeping);yield return Capture("04b-sleep",Bed+new Vector3(.8f,.45f,0),Hearth+Vector3.up*.5f);Rest();Check("wake releases movement",!Resting);
   yield return Route(new Vector3(-11,1.8f,.5f));int logs=Fire.ReserveLogs;Check("finite storage interaction",TransferLog()&&Fire.ReserveLogs==logs-1);yield return Capture("05-storage",new Vector3(-9,3,0),Storage);
   yield return Capture("06-regional-context",new Vector3(-5,4,-7),new Vector3(0,10,80));
   report.floor=FloorY;report.ingress=IngressY;report.frames=frames.Count;var sorted=frames.Skip(120).OrderBy(x=>x).ToArray();report.frameP95=sorted.Length>0?sorted[(int)(sorted.Length*.95f)]:0;File.WriteAllText(Path.Combine(output,"player-report.json"),JsonUtility.ToJson(report,true));Application.Quit(report.checks.All(c=>c.pass)?0:2);
  }
 }
}
