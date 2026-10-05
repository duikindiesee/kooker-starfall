using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace CityLife.Stones.Editor {
 public static class StoneVisualReceipt {
  static string output; static Camera camera; static GameObject actor; static int frames;
  [Serializable] public class Entry { public string shape; public Vector3 size; public int vertices; public int triangles; }
  [Serializable] public class Report { public string status; public string error; public string texture; public string actorSource; public Vector3 actorBounds; public Entry[] stones; public string[] images; public bool playerAcceptance=false; }
  static Report report=new Report();
  public static void Run(){
   var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-physicalItemEvidence")+1];
   try{
    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.45f,.48f,.52f);
    var light=new GameObject("Inspection key light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-35,0);
    camera=new GameObject("Evidence camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.08f,.1f);camera.nearClipPlane=.01f;camera.farClipPlane=50;camera.fieldOfView=38;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
    var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="One metre inspection grid ground";floor.transform.localScale=Vector3.one*.5f;floor.GetComponent<Renderer>().sharedMaterial=Material(new Color(.28f,.25f,.20f));
    const string texturePath="Assets/CityLife/Art/Resources/CityLifeArt/DryGround_1K.jpg";
    var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);if(texture==null)throw new Exception("Missing existing UV inspection texture");
    var rockMat=Material(new Color(.62f,.59f,.53f));rockMat.SetTexture("_BaseMap",texture);rockMat.SetTextureScale("_BaseMap",Vector2.one*2);
    report.texture=texturePath+" (existing texture used for UV inspection; not a final authored rock material)";
    report.stones=new Entry[4];
    for(int i=0;i<4;i++){
     var kind=(StoneShapeKind)i;var mesh=StoneMeshGenerator.GenerateMesh(kind,4217,i,1,true);
     var go=new GameObject(kind.ToString());go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=rockMat;go.transform.position=new Vector3((i-1.5f)*.38f,0,0);
     report.stones[i]=new Entry{shape=kind.ToString(),size=mesh.bounds.size,vertices=mesh.vertexCount,triangles=mesh.triangles.Length/3};
    }
    const string bodyPath=CityLife.World.Editor.CharacterAssetImport.Body;
    actor=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath));
    if(actor==null)throw new Exception("Missing existing inhabitant model");
    var skin=Material(new Color(.44f,.25f,.14f));
    foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>()){r.sharedMaterial=skin;r.updateWhenOffscreen=true;}
    string generated="Assets/CityLife/Stones/Editor/GeneratedVisualEvidence36";Directory.CreateDirectory(generated);AssetDatabase.Refresh();
    CityLife.World.Editor.HunterOutfitAuthoring.Attach(actor,generated);
    var animator=actor.GetComponent<Animator>();
    var idle=AssetDatabase.LoadAllAssetsAtPath(CityLife.World.Editor.CharacterAssetImport.Motions).OfType<AnimationClip>().First(c=>c.name=="Idle_Loop" || c.name=="Armature|Idle_Loop");
    var controller=UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(generated+"/InspectionIdle.controller");
    var state=controller.layers[0].stateMachine.AddState("Idle");state.motion=idle;controller.layers[0].stateMachine.defaultState=state;
    animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(.1f);
    actor.transform.localScale=new Vector3(1.15f,1,1.08f);actor.transform.rotation=Quaternion.identity;actor.transform.position=new Vector3(.9f,0,.55f);
    foreach(var r in actor.GetComponentsInChildren<Renderer>()) if(r.name.IndexOf("club",StringComparison.OrdinalIgnoreCase)>=0) r.gameObject.SetActive(false);
    var bounds=new Bounds();bool first=true;foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>()){var baked=new Mesh();r.BakeMesh(baked);foreach(var v in baked.vertices){var p=r.transform.TransformPoint(v);if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}Object.DestroyImmediate(baked);}
    actor.transform.position-=new Vector3(0,bounds.min.y,0);report.actorBounds=bounds.size;report.actorSource=bodyPath+" + existing HunterOutfitAuthoring; same scale as IntegratedCoastalBuild";
    frames=0;EditorApplication.update+=Tick;
   }catch(Exception e){Fail(e);}
  }
  static Material Material(Color c){var s=Shader.Find("Universal Render Pipeline/Lit");if(s==null)throw new Exception("URP Lit unavailable");var m=new Material(s);m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",.12f);return m;}
  static void Tick(){if(++frames<30)return;EditorApplication.update-=Tick;try{
    actor.SetActive(false);Shot("stone-closeup.png",new Vector3(1.1f,1.0f,-1.9f),new Vector3(0,.06f,0));
    actor.SetActive(true);Shot("stone-inhabitant-scale.png",new Vector3(2.6f,1.8f,-5.0f),new Vector3(.35f,1.0f,.1f));
    report.images=new[]{"stone-closeup.png","stone-inhabitant-scale.png"};report.status="RENDERED";Write();EditorApplication.Exit(0);
   }catch(Exception e){Fail(e);}}
  static void Shot(string name,Vector3 position,Vector3 target){camera.transform.position=position;camera.transform.LookAt(target);
   var rt=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);rt.Create();var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
   if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new Exception("URP render request unsupported");
   var previous=RenderTexture.active;Texture2D image=null;try{for(int i=0;i<4;i++)RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name),image.EncodeToPNG());}finally{RenderTexture.active=previous;if(image!=null)Object.DestroyImmediate(image);rt.Release();Object.DestroyImmediate(rt);}
  }
  static void Write(){File.WriteAllText(Path.Combine(output,"summary.json"),JsonUtility.ToJson(report,true));}
  static void Fail(Exception e){EditorApplication.update-=Tick;report.status="FAIL";report.error=e.ToString();Debug.LogException(e);Write();EditorApplication.Exit(1);}
 }
}


