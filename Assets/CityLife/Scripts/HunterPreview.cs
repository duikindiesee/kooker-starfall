using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
namespace CityLife.World
{
 // Preview-only pose controls and evidence capture; never runs in the main world implicitly.
 public sealed class HunterPreview : MonoBehaviour
 {
  public CharacterPreviewActor Actor;public CharacterPreviewCamera View;public CharacterPreviewRoamer Roamer;public Transform Model;
  public string Pose="Walk";private string directory;private bool verifying,cold,sitTransition,orbiting;private GameObject seat;private Report report=new Report();
  [Serializable]class Report {public string status="IN_PROGRESS",scope="Standalone player pose and skinning observations; visual clipping review required",utc;public int frames;public float maxClothingExtent,minClubGroundClearance=100;public bool carrying,delivered;public List<string> captures=new List<string>(),errors=new List<string>();}
  private void Awake(){var args=Environment.GetCommandLineArgs();verifying=Array.IndexOf(args,"-hunterVerify")>=0;
   gripVerify=Array.IndexOf(args,"-hunterGripVerify")>=0;verifying|=gripVerify;
   int index=Array.IndexOf(args,"-hunterEvidence");directory=index>=0&&index+1<args.Length?args[index+1]:Path.Combine(Application.persistentDataPath,"HunterCaptures",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
   report.utc=DateTime.UtcNow.ToString("O");Application.logMessageReceived+=OnLog;}
  private void OnDestroy(){Application.logMessageReceived-=OnLog;}
  private void OnLog(string condition,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.errors.Add(condition);}
  private IEnumerator Start(){yield return null;Roamer.Pause();seat=GameObject.CreatePrimitive(PrimitiveType.Cube);seat.name="Preview sitting stone";Destroy(seat.GetComponent<Collider>());seat.GetComponent<Renderer>().sharedMaterial=GameObject.Find("Test ground").GetComponent<Renderer>().sharedMaterial;seat.transform.localScale=new Vector3(.6f,.52f,.55f);seat.SetActive(false);if(verifying){Directory.CreateDirectory(directory);View.SuppressInput=true;yield return Verify();}}
  public void SetPose(string pose){Roamer.Pause();Actor.ExternalDrive=true;Pose=pose;Actor.Animator.CrossFadeInFixedTime(pose,.18f);}
  public void Walk(){Actor.ExternalDrive=false;Actor.TestControl=false;Actor.RefreshAnimation();Pose="Walk";Roamer.Pause();if(seat)seat.SetActive(false);}
  public void Cold(bool value){cold=value;Model.Find("Hunter outfit/Hunter cold hood").gameObject.SetActive(value);Model.Find("Hunter outfit/Hunter cold mantle").gameObject.SetActive(value);}
  private void Update(){if(verifying)return;var k=Keyboard.current;if(k==null)return;
   if(k.cKey.wasPressedThisFrame)SetPose("Crouch");if(k.xKey.wasPressedThisFrame)SetPose("CrouchWalk");if(k.tKey.wasPressedThisFrame&&!sitTransition)StartCoroutine(Sit());
   if(k.hKey.wasPressedThisFrame)Cold(!cold);
   if(k.oKey.wasPressedThisFrame)orbiting=!orbiting;if(orbiting)View.Yaw+=Time.deltaTime*45f;
   if(k.pKey.wasPressedThisFrame)SetPose("Pickup");if(k.vKey.wasPressedThisFrame)Walk();
   if(k.digit1Key.wasPressedThisFrame){View.Yaw=180;View.Pitch=8;View.Distance=3.7f;}
   if(k.digit2Key.wasPressedThisFrame){View.Yaw=90;View.Pitch=10;View.Distance=3.7f;}
   if(k.digit3Key.wasPressedThisFrame){View.Yaw=0;View.Pitch=10;View.Distance=3.7f;}
   if(k.f8Key.wasPressedThisFrame){Directory.CreateDirectory(directory);ScreenCapture.CaptureScreenshot(Path.Combine(directory,"native-"+DateTime.UtcNow.ToString("HHmmssfff")+".png"));}
   if(k.rKey.wasPressedThisFrame){Actor.ExternalDrive=false;Actor.TestControl=false;Actor.RefreshAnimation();Pose="Pickup / carry route";if(seat)seat.SetActive(false);}
  }
  private void Seat(bool value){seat.SetActive(value);seat.transform.position=Actor.transform.position-Actor.transform.forward*.24f+Vector3.up*.26f;seat.transform.rotation=Actor.transform.rotation;}
  private IEnumerator Sit(){sitTransition=true;if(Pose=="Sit"){SetPose("SitExit");yield return new WaitForSeconds(1.1f);Walk();}else{Seat(true);SetPose("SitEnter");yield return new WaitForSeconds(1.35f);SetPose("Sit");}sitTransition=false;}
  private IEnumerator Capture(string name){yield return new WaitForEndOfFrame();string file=name+".png";ScreenCapture.CaptureScreenshot(Path.Combine(directory,file));report.captures.Add(file);yield return null;}
  private void Audit(){report.frames++;var club=Model.GetComponent<HunterClubCarry>();if(club)report.minClubGroundClearance=Mathf.Min(report.minClubGroundClearance,club.GroundClearance);foreach(var r in Model.GetComponentsInChildren<SkinnedMeshRenderer>()){if(!r.name.StartsWith("Hunter "))continue;var m=new Mesh();r.BakeMesh(m);var bounds=m.bounds;float e=bounds.extents.magnitude;report.maxClothingExtent=Mathf.Max(report.maxClothingExtent,e);if(float.IsNaN(e)||e>3)throw new InvalidOperationException("Invalid deformation on "+r.name);Destroy(m);}}
  private bool gripVerify;
  [Serializable]class GripTuning {public Vector3 curlAdjustment,thumbAdjustment;public Vector2 anchorAdjustment;public float forearmSlope=-1.5f,elbowOut=.6f,wristDeviation=30f;}
  [Serializable]class GripSample {public string pose;public float maximumHandPenetration,maxFingerPenetration,maxThumbPenetration,maxPalmForearmPenetration;public string deepestBone;}
  private List<GripSample> gripSamples=new List<GripSample>();
  [Serializable]class GripGeometry {public Vector3 handScale,clubScale;public List<Vector3> points=new List<Vector3>();public List<string> bones=new List<string>();}
  private void MeasureGrip(string label) {
   var club=Model.GetComponent<HunterClubCarry>();
   var sample=new GripSample{pose=label};
   var geometry=new GripGeometry{handScale=Actor.Animator.GetBoneTransform(HumanBodyBones.LeftHand).lossyScale,clubScale=club.Club.lossyScale};
   foreach(var body in Model.GetComponentsInChildren<SkinnedMeshRenderer>()) {
    if(body.name.StartsWith("Hunter "))continue;
    var mesh=new Mesh();body.BakeMesh(mesh);var vertices=mesh.vertices;var weights=body.sharedMesh.boneWeights;
    for(int i=0;i<vertices.Length;i++) {
     var weight=weights[i];int boneIndex=weight.boneIndex0;string bone=body.bones[boneIndex].name;
     if(!(bone.EndsWith("_l")&&(bone.Contains("hand")||bone.Contains("thumb")||bone.Contains("index")||bone.Contains("middle")||bone.Contains("ring")||bone.Contains("pinky")||bone.Contains("lowerarm"))))continue;
     Vector3 point=club.Club.InverseTransformPoint(body.transform.TransformPoint(vertices[i]));
     if(label=="Idle-30"){geometry.points.Add(point);geometry.bones.Add(bone);}
     float fraction=(.055f-point.y)/.62f;if(fraction<0||fraction>1)continue;
     float radius=Mathf.Lerp(.016f,.038f,fraction)+.043f*Mathf.Exp(-Mathf.Pow((fraction-.87f)/.17f,2));
     float depth=radius-new Vector2(point.x-.012f*Mathf.Sin(fraction*5),point.z).magnitude;
     if(bone.Contains("thumb"))sample.maxThumbPenetration=Mathf.Max(sample.maxThumbPenetration,depth);
     else if(bone.Contains("hand")||bone.Contains("lowerarm"))sample.maxPalmForearmPenetration=Mathf.Max(sample.maxPalmForearmPenetration,depth);
     else sample.maxFingerPenetration=Mathf.Max(sample.maxFingerPenetration,depth);
     if(depth>sample.maximumHandPenetration){sample.maximumHandPenetration=depth;sample.deepestBone=bone;}
    }Destroy(mesh);
   }
   gripSamples.Add(sample);
   if(label=="Idle-30")File.WriteAllText(Path.Combine(directory,"grip-geometry.json"),JsonUtility.ToJson(geometry,true));
  }
  [Serializable]class GripMeasurements {public string scope="Conservative cylinder-envelope vertex penetration in club local metres; polygon surfaces and edge-only intersections still require visual review.";public List<GripSample> samples;}
  private IEnumerator GripViews(string label) {
   float speed=Actor.Animator.speed;Actor.Animator.speed=0;Time.timeScale=0;
   yield return new WaitForEndOfFrame();MeasureGrip(label);
   foreach(float yaw in new[]{180f,90f,0f,270f}) {
    View.Yaw=yaw;View.Distance=3.2f;View.Pitch=10;View.ExternalView=false;
    yield return Capture("grip-"+label+"-body-"+yaw);
    View.ExternalView=true;var center=Model.GetComponent<HunterClubCarry>().GripCenter;
    View.transform.position=center+Quaternion.Euler(12,yaw,0)*Vector3.back*.48f;View.transform.LookAt(center);
    yield return Capture("grip-"+label+"-hand-"+yaw);
   }
   View.ExternalView=false;Actor.Animator.speed=speed;Time.timeScale=1;
  }
  private IEnumerator GripVerify() {
   var args=Environment.GetCommandLineArgs();int tuningIndex=Array.IndexOf(args,"-hunterGripTuning");
   if(tuningIndex>=0&&tuningIndex+1<args.Length){var tuning=JsonUtility.FromJson<GripTuning>(File.ReadAllText(args[tuningIndex+1]));var grip=Model.GetComponent<HunterClubCarry>();grip.DiagnosticCurlAdjustment=tuning.curlAdjustment;grip.DiagnosticThumbAdjustment=tuning.thumbAdjustment;grip.DiagnosticAnchorAdjustment=tuning.anchorAdjustment;grip.ForearmSlope=tuning.forearmSlope;grip.ElbowOut=tuning.elbowOut;grip.WristDeviation=tuning.wristDeviation;File.Copy(args[tuningIndex+1],Path.Combine(directory,"diagnostic-tuning.json"),true);}
   foreach(string pose in new[]{"Idle","Walk","Crouch","CrouchWalk","SitEnter","Sit","SitExit","Pickup"}) {
    Seat(pose.StartsWith("Sit"));SetPose(pose);Actor.Animator.Play(pose,0,0);
    for(int i=0;i<45;i++){yield return null;Audit();if(i==10||i==30)yield return GripViews(pose+"-"+i);}
    if(Array.IndexOf(args,"-hunterGripQuick")>=0){File.WriteAllText(Path.Combine(directory,"grip-measurements.json"),JsonUtility.ToJson(new GripMeasurements{samples=gripSamples},true));Application.Quit();yield break;}
   }
   Seat(false);Actor.ExternalDrive=false;Actor.TestControl=false;Actor.RefreshAnimation();Actor.Place(new Vector3(0,.03f,-5));Roamer.Begin();
   for(int i=0;i<2100&&!Roamer.Complete;i++) {
    yield return null;Audit();
    if(Roamer.Carrying&&!report.carrying){report.carrying=true;yield return GripViews("carry-start");}
    if(Roamer.Carrying&&i%90==0)yield return GripViews("carry-"+i);
    if(Actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Interact")&&i%15==0)yield return GripViews("interaction-"+i);
   }
   report.delivered=Roamer.Complete;yield return GripViews("delivery");
   for(int i=0;i<45;i++){yield return null;Audit();if(i%15==0)yield return GripViews("delivery-settle-"+i);}
   File.WriteAllText(Path.Combine(directory,"grip-measurements.json"),JsonUtility.ToJson(new GripMeasurements{samples=gripSamples},true));
   report.status=report.errors.Count==0&&report.carrying&&report.delivered?"GRIP_CAPTURED_VISUAL_REVIEW_REQUIRED":"FAIL";
   File.WriteAllText(Path.Combine(directory,"hunter-runtime.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="FAIL"?1:0);
  }
  private IEnumerator Verify(){Time.captureDeltaTime=1f/30;QualitySettings.vSyncCount=0;Application.targetFrameRate=30;Actor.Place(new Vector3(0,.03f,-5));
   if(gripVerify){yield return GripVerify();yield break;}
   View.Yaw=180;View.Pitch=10;View.Distance=3.6f;SetPose("Idle");yield return new WaitForSeconds(.7f);yield return Capture("01-idle-front");
   View.Yaw=0;yield return Capture("02-idle-back");View.Yaw=90;yield return Capture("03-idle-side");
   View.Yaw=155;Actor.ExternalDrive=false;Actor.TestControl=true;Actor.TestDirection=Vector3.forward;Pose="Walk verification";
   for(int i=0;i<210;i++){Audit();if(i%30==15)yield return Capture("04-walk-"+i);yield return null;}
   Actor.TestDirection=Vector3.zero;Actor.TestControl=false;Actor.Place(new Vector3(0,.03f,-5));
   foreach(bool layers in new[]{false,true}) {Cold(layers);string variant=layers?"cold":"default";
    foreach(string pose in new[]{"Idle","Crouch","CrouchWalk","Pickup"}){SetPose(pose);for(int i=0;i<90;i++){Audit();if(i==15||i==40||i==70){View.Yaw=i==15?180:i==40?90:25;yield return Capture("05-"+variant+"-"+pose+"-"+i);}yield return null;}}
    foreach(float yaw in new[]{180f,90f,25f,155f}){View.Yaw=yaw;View.Distance=3.2f;Seat(true);
     foreach(string pose in new[]{"SitEnter","Sit","SitExit"}){SetPose(pose);Actor.Animator.Play(pose,0,0);for(int i=0;i<45;i++){Audit();if(i%10==0)yield return Capture("seated-"+variant+"-"+yaw+"-"+pose+"-"+i);yield return null;}}
    }Seat(false);
   }
   foreach(bool layers in new[]{false,true}){Cold(layers);Seat(true);SetPose("Sit");yield return new WaitForSeconds(.5f);
    for(int i=0;i<360;i++){View.Yaw=i;View.Pitch=12;Audit();if(i%30==0)yield return Capture("orbit-"+(layers?"cold":"default")+"-"+i);yield return null;}
   }Seat(false);Cold(false);
   Actor.ExternalDrive=false;Actor.TestControl=false;Actor.RefreshAnimation();Actor.Place(new Vector3(0,.03f,-5));View.Yaw=155;View.Distance=4.1f;Roamer.Begin();Pose="Pickup / carry verification";
   for(int i=0;i<2100&&!Roamer.Complete;i++){Audit();if(Roamer.Carrying&&!report.carrying){report.carrying=true;yield return Capture("06-carry-start");}if(Roamer.Carrying&&i%40==0)yield return Capture("07-carry-"+i);yield return null;}
   report.delivered=Roamer.Complete;yield return Capture("08-delivery");report.status=report.errors.Count==0&&report.carrying&&report.delivered?"MOTION_CAPTURED_VISUAL_REVIEW_REQUIRED":"FAIL";
   File.WriteAllText(Path.Combine(directory,"hunter-runtime.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="FAIL"?1:0);
  }
  private void OnGUI(){GUI.Box(new Rect(Screen.width-460,18,442,166),"");GUI.Label(new Rect(Screen.width-445,30,420,142),"STARFALL / HUNTER CLOTHING PREVIEW\n"+Pose+" · "+(cold?"Cold layers":"Everyday")+"\nWASD walk · V resume · C/X crouch · T sit / stand\nP pickup · R delivery · H optional cold layers\n1/2/3 views · O orbit · RMB look · F8 capture\nNo hunting, combat or weather integration.");}
 }
}
