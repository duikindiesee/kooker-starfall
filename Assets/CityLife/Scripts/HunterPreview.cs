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
  public string Pose="Walk";private string directory;private bool verifying,cold,sitTransition;private GameObject seat;private Report report=new Report();
  [Serializable]class Report {public string status="IN_PROGRESS",scope="Standalone player pose and skinning observations; visual clipping review required",utc;public int frames;public float maxClothingExtent,minClubGroundClearance=100;public bool carrying,delivered;public List<string> captures=new List<string>(),errors=new List<string>();}
  private void Awake(){var args=Environment.GetCommandLineArgs();verifying=Array.IndexOf(args,"-hunterVerify")>=0;
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
  private IEnumerator Verify(){Time.captureDeltaTime=1f/30;QualitySettings.vSyncCount=0;Application.targetFrameRate=30;Actor.Place(new Vector3(0,.03f,-5));
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
   }Cold(false);
   Actor.ExternalDrive=false;Actor.TestControl=false;Actor.RefreshAnimation();Actor.Place(new Vector3(0,.03f,-5));View.Yaw=155;View.Distance=4.1f;Roamer.Begin();Pose="Pickup / carry verification";
   for(int i=0;i<2100&&!Roamer.Complete;i++){Audit();if(Roamer.Carrying&&!report.carrying){report.carrying=true;yield return Capture("06-carry-start");}if(Roamer.Carrying&&i%40==0)yield return Capture("07-carry-"+i);yield return null;}
   report.delivered=Roamer.Complete;yield return Capture("08-delivery");report.status=report.errors.Count==0&&report.carrying&&report.delivered?"MOTION_CAPTURED_VISUAL_REVIEW_REQUIRED":"FAIL";
   File.WriteAllText(Path.Combine(directory,"hunter-runtime.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="FAIL"?1:0);
  }
  private void OnGUI(){GUI.Box(new Rect(Screen.width-460,18,442,166),"");GUI.Label(new Rect(Screen.width-445,30,420,142),"STARFALL / HUNTER CLOTHING PREVIEW\n"+Pose+" · "+(cold?"Cold layers":"Everyday")+"\nWASD walk · V resume · C/X crouch · T sit / stand\nP pickup · R delivery · H optional cold layers\n1 front · 2 side · 3 back · RMB orbit · F8 capture\nNo hunting, combat or weather integration.");}
 }
}
