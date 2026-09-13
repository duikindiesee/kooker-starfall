using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace CityLife.World.Editor
{
 public static class RefugeBuild
 {
  public const string Version="0.0.8-refuge.2";
  public static bool Requested=>System.Environment.GetCommandLineArgs().Contains("-starfallRefuge");
  public static void Run(){if(!Requested||!Application.isBatchMode)throw new InvalidOperationException("Explicit refuge batch required");KokerboomRender.RenderCoastalSlice();}
  public static void Attach(Camera camera,GameObject ground)
  {
   foreach(var c in Object.FindObjectsByType<Collider>())c.gameObject.layer=8;
   ground.layer=8;
   var root=new GameObject("First refuge / authored v1");
   Material Mat(string n,Color c){var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=n,color=c};m.SetFloat("_Smoothness",.06f);return m;}
   var stone=new Material(Shader.Find("CityLife/CoastalRocks")){name="Refuge stratified sandstone"};stone.SetColor("_BaseColor",new Color(.48f,.32f,.20f));var straw=Mat("Refuge woven grass",new Color(.49f,.37f,.18f));var wood=Mat("Refuge dry wood",new Color(.20f,.11f,.055f));
   GameObject Box(string n,Vector3 p,Vector3 s,Material m){var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=n;o.layer=8;o.transform.SetParent(root.transform);o.transform.position=p;o.transform.localScale=s;o.GetComponent<Renderer>().sharedMaterial=m;var boxMesh=Object.Instantiate(o.GetComponent<MeshFilter>().sharedMesh);boxMesh.colors=Enumerable.Repeat(Color.white,boxMesh.vertexCount).ToArray();o.GetComponent<MeshFilter>().sharedMesh=boxMesh;return o;}
   GameObject Rock(string n,Vector3 p,Vector3 s){var o=GameObject.CreatePrimitive(PrimitiveType.Sphere);o.name=n;o.layer=8;o.transform.SetParent(root.transform);o.transform.position=p;o.transform.localScale=s;Object.DestroyImmediate(o.GetComponent<Collider>());o.GetComponent<MeshFilter>().sharedMesh=Starfall.Refuge.RefugeStone.Mesh();o.AddComponent<MeshCollider>().sharedMesh=o.GetComponent<MeshFilter>().sharedMesh;o.GetComponent<Renderer>().sharedMaterial=stone;return o;}
   // Supported floor, wide ramp and a single unobstructed east opening.
   Box("Refuge floor",new Vector3(-10, .8f,0),new Vector3(8,2,6),stone);
   var ramp=new GameObject("Refuge entrance ramp");ramp.layer=8;ramp.transform.SetParent(root.transform);
   var mesh=new Mesh{name="Refuge ramp v1"};mesh.vertices=new[]{new Vector3(-6,1.8f,-2),new Vector3(-6,1.8f,2),new Vector3(-2,0,-2),new Vector3(-2,0,2)};mesh.triangles=new[]{0,1,2,2,1,3};mesh.colors=Enumerable.Repeat(Color.white,mesh.vertexCount).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();ramp.AddComponent<MeshFilter>().sharedMesh=mesh;ramp.AddComponent<MeshRenderer>().sharedMaterial=stone;ramp.AddComponent<MeshCollider>().sharedMesh=mesh;
   Rock("Refuge back enclosure",new Vector3(-14.3f,3,0),new Vector3(3,7,8));
   Rock("Refuge south enclosure",new Vector3(-10.5f,3,-3.8f),new Vector3(10,7,3));
   Rock("Refuge north enclosure",new Vector3(-10.5f,3,3.8f),new Vector3(10,7,3));
   Rock("Refuge roof",new Vector3(-10.5f,6.5f,0),new Vector3(12,2.2f,9));
   for(int i=0;i<9;i++)Rock("Refuge outer strata "+i,new Vector3(-14.4f+i*.6f,5.7f+(i%3)*.3f,3.6f),new Vector3(2.7f,1.6f,2));
   Vector3 hearth=new Vector3(-8,1.8f,1.35f),bed=new Vector3(-11,1.8f,-1.2f),storage=new Vector3(-12,1.8f,1.1f);
   for(int i=0;i<12;i++){float a=i*Mathf.PI/6;Rock("Hearth boundary stone "+i,hearth+new Vector3(Mathf.Cos(a)*.64f,.1f,Mathf.Sin(a)*.64f),new Vector3(.31f,.24f,.27f));}
   for(int i=0;i<4;i++){var log=Box("Hearth fuel log "+i,hearth+new Vector3(0,.13f+i*.055f,0),new Vector3(.8f,.12f,.12f),wood);log.transform.rotation=Quaternion.Euler(0,i*58,0);}
   var mat=Box("Primitive sleeping mat",bed+Vector3.up*.035f,new Vector3(2.2f,.07f,1),straw);
   for(int i=0;i<22;i++)Box("Mat weave "+i,bed+new Vector3(-1.05f+i*.1f,.08f,0),new Vector3(.035f,.025f,1),wood);
   Box("Rolled grass pillow",bed+new Vector3(-.85f,.15f,0),new Vector3(.32f,.2f,.75f),straw);
   Box("Modest stone storage base",storage+Vector3.up*.15f,new Vector3(.85f,.3f,.65f),stone);
   for(int i=0;i<6;i++)Box("Stored fuel "+i,storage+new Vector3((i%2)*.2f-.1f,.4f+(i/2)*.13f,0),new Vector3(.13f,.12f,.6f),wood);
   var runtime=camera.gameObject.AddComponent<Starfall.Refuge.RefugeRuntime>();camera.gameObject.AddComponent<Starfall.Refuge.RefugeRain>().World=runtime;runtime.View=camera;runtime.Hearth=hearth;runtime.Bed=bed;runtime.Storage=storage;runtime.Roof=GameObject.Find("Refuge roof").GetComponent<Collider>();
   var body=new GameObject("Refuge player capsule");body.layer=9;var capsule=body.AddComponent<CharacterController>();capsule.height=1.8f;capsule.center=new Vector3(0,.9f,0);capsule.radius=.3f;capsule.stepOffset=.25f;capsule.slopeLimit=45;capsule.skinWidth=.025f;body.transform.position=new Vector3(-4,.05f,-4);runtime.Body=capsule;
   camera.transform.position=body.transform.position+Vector3.up*1.65f;camera.transform.rotation=Quaternion.LookRotation(new Vector3(-7,2.5f,0)-camera.transform.position);camera.nearClipPlane=.08f;camera.fieldOfView=65;
   var flame=Rock("Contained hearth flame",hearth+Vector3.up*.43f,new Vector3(.26f,.55f,.26f));Object.DestroyImmediate(flame.GetComponent<Collider>());var glow=Mat("Amber fire",new Color(1,.22f,.015f));glow.EnableKeyword("_EMISSION");glow.SetColor("_EmissionColor",new Color(3,.55f,.03f));flame.GetComponent<Renderer>().sharedMaterial=glow;runtime.Flame=flame;
   var light=new GameObject("Hearth light").AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.44f,.13f);light.range=7;light.shadows=LightShadows.Soft;light.transform.position=hearth+Vector3.up*.7f;runtime.FireLight=light;
   var giant=GameObject.Find("Blue gas giant - procedural volumetric cloud bands");if(giant!=null){giant.transform.position=new Vector3(120,420,2800);giant.transform.localScale=Vector3.one*650;camera.farClipPlane=6000;}
   Time.fixedDeltaTime=.02f;Time.maximumDeltaTime=.1f;
  }
 }
}
