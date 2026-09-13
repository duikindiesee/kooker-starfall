using UnityEngine;
namespace Starfall.Refuge {
 // Bounded local precipitation pool, sampled per drop. No shelter global affects exterior weather.
 public sealed class RefugeRain:MonoBehaviour {
 public RefugeRuntime World;readonly GameObject[] drops=new GameObject[128];Light sun;float original;
 void Start(){var material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){color=new Color(.4f,.7f,.9f)};for(int i=0;i<drops.Length;i++){var d=GameObject.CreatePrimitive(PrimitiveType.Cube);d.name="Local precipitation "+i;Destroy(d.GetComponent<Collider>());d.transform.localScale=new Vector3(.015f,.22f,.015f);d.GetComponent<Renderer>().sharedMaterial=material;drops[i]=d;}foreach(var l in FindObjectsByType<Light>(FindObjectsSortMode.None))if(l.type==LightType.Directional&&l.enabled){sun=l;original=l.intensity;break;}}
 void Update(){var s=World.Clock.Sample;float time=World.Clock.Tick*.02f;if(sun!=null)sun.intensity=original*Mathf.Lerp(1,.55f,s.precipitation);for(int i=0;i<drops.Length;i++){float x=-16+(i*37%127)/127f*19;float z=-7+(i*53%127)/127f*14;float y=1+Mathf.Repeat(i*.71f-time*8,9);Vector3 p=new Vector3(x,y,z);bool blocked=Physics.Raycast(p,Vector3.up,12,1<<8,QueryTriggerInteraction.Ignore);drops[i].SetActive(!blocked&&i<128*s.precipitation);drops[i].transform.position=p;drops[i].transform.rotation=Quaternion.FromToRotation(Vector3.down,(s.wind*.2f+Vector3.down*8).normalized);}}
 }
}
