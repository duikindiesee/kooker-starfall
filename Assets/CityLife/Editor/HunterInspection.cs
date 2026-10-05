using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace CityLife.World.Editor {
public static class HunterInspection {
 public static void Run() {
  Directory.CreateDirectory("evidence/local/hunter");
  var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetImport.Body));
  var sb=new StringBuilder(); var a=go.GetComponent<Animator>();
  foreach(HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones))) { if(b==HumanBodyBones.LastBone)continue;var t=a.GetBoneTransform(b);if(t)sb.AppendLine(b+" "+t.name+" "+go.transform.InverseTransformPoint(t.position).ToString("F4")); }
  foreach(var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) { var m=r.sharedMesh; sb.AppendLine("MESH "+r.name+" "+m.vertexCount+" "+m.bounds+" scale "+r.transform.lossyScale+" local "+r.transform.localPosition+" blendshapes "+m.blendShapeCount);for(int i=0;i<m.blendShapeCount;i++)sb.AppendLine("BLEND "+m.GetBlendShapeName(i)+" "+r.GetBlendShapeWeight(i)); var v=m.vertices;var bw=m.boneWeights;File.WriteAllText("evidence/local/hunter/"+r.name+"-mesh.json",JsonUtility.ToJson(new Data{vertices=v,triangles=m.triangles,weights=bw,bones=r.bones.Select(x=>x.name).ToArray()})); }
  foreach(var c in AssetDatabase.LoadAllAssetsAtPath(CharacterAssetImport.Motions).OfType<AnimationClip>())sb.AppendLine("CLIP "+c.name+" "+c.length);
  File.WriteAllText("evidence/local/hunter/inspection.txt",sb.ToString());UnityEngine.Object.DestroyImmediate(go);
 }
 [Serializable]class Data {public Vector3[] vertices;public int[] triangles;public BoneWeight[] weights; public string[] bones;}
}}
