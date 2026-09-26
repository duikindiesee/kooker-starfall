using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace CityLife.World.Editor
{
 // Original garment construction. Body-fit shell derives only from the retained CC0 body.
 // Must run in the unanimated imported bind pose, before an Animator evaluates.
 public static class HunterOutfitAuthoring
 {
  private sealed class Surface {
   public List<Vector3> v=new List<Vector3>(); public List<Vector2> uv=new List<Vector2>();
   public List<int> t=new List<int>(); public List<BoneWeight> w=new List<BoneWeight>();
   public int Add(Vector3 p,Vector2 u,BoneWeight b){v.Add(p);uv.Add(u);w.Add(b);return v.Count-1;}
   public void Quad(int a,int b,int c,int d){t.AddRange(new[]{a,b,c,a,c,d});}
  }
  public static void Attach(GameObject model,string folder)
  {
   if(model.transform.Find("Hunter outfit"))throw new InvalidOperationException("Outfit already exists.");
   var body=model.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r=>r.sharedMesh.vertexCount).First();
   var source=UnityEngine.Object.Instantiate(body.sharedMesh);source.name="Hunter robust body"; var bones=body.bones; var animator=model.GetComponent<Animator>();
   body.quality=SkinQuality.Bone4;
   var root=new GameObject("Hunter outfit");root.transform.SetParent(model.transform,false);
   Vector3[] p=source.vertices.Select(x=>model.transform.InverseTransformPoint(body.transform.TransformPoint(x))).ToArray();
   var bodyNormals=source.normals;
   for(int i=0;i<p.Length;i++){
    var n=model.transform.InverseTransformDirection(body.transform.TransformDirection(bodyNormals[i])).normalized;
    float bulk=p[i].y<1.50f&&p[i].y>.25f&&Mathf.Abs(p[i].x)<.70f?.010f:0;
    p[i]+=n*bulk;
   }
   source.vertices=p.Select(v=>body.transform.InverseTransformPoint(model.transform.TransformPoint(v))).ToArray();source.RecalculateNormals();source.RecalculateBounds();AssetDatabase.CreateAsset(source,folder+"/Hunter robust body.asset");body.sharedMesh=source;
   var weights=source.boneWeights;
   var normal=source.normals.Select(n=>model.transform.InverseTransformDirection(body.transform.TransformDirection(n)).normalized).ToArray();
   Vector3 Bone(HumanBodyBones id)=>model.transform.InverseTransformPoint(animator.GetBoneTransform(id).position);
   int Index(HumanBodyBones id)=>Array.IndexOf(bones,animator.GetBoneTransform(id));
   BoneWeight One(int i)=>new BoneWeight{boneIndex0=i,weight0=1};
   BoneWeight TorsoWeight(BoneWeight bw){
    int lArm=Index(HumanBodyBones.LeftUpperArm),rightArm=Index(HumanBodyBones.RightUpperArm);
    int chest=Index(HumanBodyBones.Chest),lSh=Index(HumanBodyBones.LeftShoulder),rSh=Index(HumanBodyBones.RightShoulder);
    if(bw.boneIndex0==lArm)bw.boneIndex0=lSh>=0?lSh:chest;
    if(bw.boneIndex1==lArm)bw.boneIndex1=lSh>=0?lSh:chest;
    if(bw.boneIndex0==rightArm)bw.boneIndex0=rSh>=0?rSh:chest;
    if(bw.boneIndex1==rightArm)bw.boneIndex1=rSh>=0?rSh:chest;
    return bw;
   }
   BoneWeight NearSide(Vector3 pt,int side){
    int leftLeg=Index(HumanBodyBones.LeftUpperLeg),rightLeg=Index(HumanBodyBones.RightUpperLeg);
    int forbiddenLeg=side==0?rightLeg:leftLeg;
    int best=0;float d=float.MaxValue;
    for(int i=0;i<p.Length;i++){
     var bw=weights[i];
     if(bw.boneIndex0==forbiddenLeg&&bw.weight0>.3f)continue;
     if(bw.boneIndex1==forbiddenLeg&&bw.weight1>.3f)continue;
     float n=(p[i]-pt).sqrMagnitude;if(n<d){d=n;best=i;}
    }
    return weights[best];
   }
   float hip=Bone(HumanBodyBones.Hips).y, shoulder=Bone(HumanBodyBones.LeftUpperArm).y;
   float knee=(Bone(HumanBodyBones.LeftLowerLeg).y+Bone(HumanBodyBones.RightLowerLeg).y)*.5f;
   float top=Bone(HumanBodyBones.Neck).y+.005f, waist=hip+.09f, hem=Mathf.Lerp(knee,hip,.70f);
   var hide=new Surface();var dark=new Surface();var trim=new Surface();var wraps=new Surface();var trousers=new Surface();var hood=new Surface();var mantle=new Surface();
   // Tailored sleeveless tunic: drapes smoothly over the torso, clavicle, and trapezius with zero armhole tearing.
   float armX=Mathf.Abs(Bone(HumanBodyBones.LeftUpperArm).x);
   int leftUpperArm=Index(HumanBodyBones.LeftUpperArm), rightUpperArm=Index(HumanBodyBones.RightUpperArm);
   bool IsArmVertex(int vIdx){
    var bw=weights[vIdx];
    return (bw.boneIndex0==leftUpperArm&&bw.weight0>.20f)||(bw.boneIndex0==rightUpperArm&&bw.weight0>.20f)||
           (bw.boneIndex1==leftUpperArm&&bw.weight1>.35f)||(bw.boneIndex1==rightUpperArm&&bw.weight1>.35f);
   }
   float limitX(float y)=>(y>=shoulder-.05f)?(armX-.012f):(armX-.010f);
   bool Torso(int vIdx)=>p[vIdx].y>=hip+.015f && p[vIdx].y<=top+.005f && Mathf.Abs(p[vIdx].x)<limitX(p[vIdx].y) && !IsArmVertex(vIdx);
   int[] old=source.triangles;
   for(int i=0;i<old.Length;i+=3){int a=old[i],b=old[i+1],c=old[i+2];if(!Torso(a)||!Torso(b)||!Torso(c))continue;
    int start=hide.v.Count;foreach(int j in new[]{a,b,c}){
     var q=p[j]+normal[j]*.016f;
     hide.Add(q,new Vector2(Mathf.Atan2(q.z,q.x)/6.28318f+.5f,(q.y-hip)*1.5f),TorsoWeight(weights[j]));
    }hide.t.AddRange(new[]{start,start+1,start+2});}
   // Athletic wrap skirt: front is tailored to abdomen; rear has generous clearance for buttocks and gait.
   for(int side=0;side<2;side++){
    const int cols=16,rows=5;int start=hide.v.Count;float angleStart=side==0?-.22f:Mathf.PI-.22f;
    for(int row=0;row<=rows;row++){
     float f=row/(float)rows;float y=Mathf.Lerp(waist,hem,f);
     float rx=Mathf.Lerp(.215f,.250f,f);
     float rzFront=Mathf.Lerp(.145f,.172f,f);
     float rzBack=Mathf.Lerp(.185f,.225f,f);
     const float zCenter=0.018f;
     for(int col=0;col<=cols;col++){
      float ang=angleStart+(Mathf.PI+.44f)*col/cols;
      float rz=Mathf.Cos(ang)>=0?rzBack:rzFront;
      var q=new Vector3(Mathf.Sin(ang)*rx,y+(row==rows?.012f*Mathf.Sin(ang*5):0),zCenter+Mathf.Cos(ang)*rz);
      hide.Add(q,new Vector2(col/(float)cols,f),NearSide(q,side));
      if(row<rows&&col<cols){int n=start+row*(cols+1)+col;hide.Quad(n,n+cols+1,n+cols+2,n+1);}
     }
    }
   }
   // A compact braided-looking belt and knot, all bound to hips.
   void Tube(Surface s,Vector3 a,Vector3 b,float radius,BoneWeight weight){Vector3 dir=(b-a).normalized;Vector3 u=Vector3.Cross(dir,Vector3.up);if(u.sqrMagnitude<.01f)u=Vector3.Cross(dir,Vector3.forward);u.Normalize();var v=Vector3.Cross(dir,u);int n=s.v.Count;for(int end=0;end<2;end++)for(int k=0;k<6;k++){float ang=k*Mathf.PI/3; s.Add((end==0?a:b)+(u*Mathf.Cos(ang)+v*Mathf.Sin(ang))*radius,new Vector2(k/6f,end),weight);}for(int k=0;k<6;k++)s.Quad(n+k,n+(k+1)%6,n+6+(k+1)%6,n+6+k);}
   for(int i=0;i<40;i++){float a=i*Mathf.PI/20,b=(i+1)*Mathf.PI/20;for(int strand=0;strand<2;strand++){
     Vector3 A=new Vector3(Mathf.Sin(a)*.220f,waist+.01f+strand*.019f+Mathf.Sin(a*16)*.003f,0.018f+Mathf.Cos(a)*(Mathf.Cos(a)>=0?.192f:.152f));
     Vector3 B=new Vector3(Mathf.Sin(b)*.220f,waist+.01f+strand*.019f+Mathf.Sin(b*16)*.003f,0.018f+Mathf.Cos(b)*(Mathf.Cos(b)>=0?.192f:.152f));
     Tube(dark,A,B,.010f,One(Index(HumanBodyBones.Hips)));}}
   for(int i=0;i<2;i++)Tube(dark,new Vector3(.065f,waist+.015f,-.140f),new Vector3(.08f+i*.015f,waist-.075f,-.155f),.007f,One(Index(HumanBodyBones.Hips)));
   // Small tied gathering pouch, flattened against the left hip.
   {int start=hide.v.Count;const int cols=12,rows=6;for(int row=0;row<=rows;row++)for(int col=0;col<=cols;col++){
     float u=col/(float)cols*Mathf.PI*2,v=row/(float)rows*Mathf.PI;
     var q=new Vector3(.215f+Mathf.Sin(v)*Mathf.Cos(u)*.048f,waist-.077f+Mathf.Cos(v)*.070f,0.018f+Mathf.Sin(v)*Mathf.Sin(u)*.030f);
     hide.Add(q,new Vector2(col/(float)cols,row/(float)rows),One(Index(HumanBodyBones.Hips)));
     if(row<rows&&col<cols){int n=start+row*(cols+1)+col;hide.Quad(n,n+1,n+cols+2,n+cols+1);}
    }Tube(dark,new Vector3(.205f,waist+.01f,0.015f),new Vector3(.215f,waist-.025f,0.015f),.006f,One(Index(HumanBodyBones.Hips)));}
   // Clean tunic chest: subtle leather seam stitching along the hip gathering pouch seam.
   for(int i=0;i<4;i++){
     float f=i/3f;
     var q=new Vector3(.220f,waist-.05f-f*.04f,0.018f);
     Tube(trim,q+new Vector3(0,0,-.004f),q+new Vector3(0,0,.004f),.0018f,One(Index(HumanBodyBones.Hips)));
   }
   // Soft foot coverings preserve toe deformation; no rigid boot geometry or collider.
   bool Foot(Vector3 a)=>a.y<.25f;
   // Optional cold layers use the same existing skin, keeping a generous open face.
   bool Hood(Vector3 q)=>q.y>1.48f&&Mathf.Abs(q.x)<.15f&&!(q.z<-.025f&&q.y<1.775f&&Mathf.Abs(q.x)<.10f);
   bool Mantle(Vector3 q)=>q.y>1.30f&&q.y<top&&Mathf.Abs(q.x)<.32f;
   for(int i=0;i<old.Length;i+=3)foreach(bool isHood in new[]{true,false}){
    var s=isHood?hood:mantle;bool include=true;for(int k=0;k<3;k++)if(!(isHood?Hood(p[old[i+k]]):Mantle(p[old[i+k]])))include=false;if(!include)continue;
    int n=s.v.Count;foreach(int j in new[]{old[i],old[i+1],old[i+2]}){var q=p[j]+normal[j]*(isHood?.027f:.035f);s.Add(q,new Vector2(q.x*2,q.y*2),weights[j]);}s.t.AddRange(new[]{n,n+1,n+2});
   }
   // Continuous fitted inner garment includes the complete pelvis and seat topology.
   // It shares the body's exact vertex weights and bind space, so panel separation cannot expose skin.
   for(int i=0;i<old.Length;i+=3){if(p[old[i]].y>waist+.035f||p[old[i+1]].y>waist+.035f||p[old[i+2]].y>waist+.035f)continue;
    int n=trousers.v.Count;foreach(int j in new[]{old[i],old[i+1],old[i+2]}){var q=p[j]+normal[j]*.012f;trousers.Add(q,new Vector2(q.x*2,q.y*2),weights[j]);}trousers.t.AddRange(new[]{n,n+1,n+2});}
   for(int i=0;i<old.Length;i+=3){if(!Foot(p[old[i]])||!Foot(p[old[i+1]])||!Foot(p[old[i+2]]))continue;int n=wraps.v.Count;foreach(int j in new[]{old[i],old[i+1],old[i+2]}){
    var q=p[j];float side=q.x<0?-1:1;var axis=new Vector3(side*Mathf.Abs(Bone(HumanBodyBones.LeftFoot).x),.07f,-.04f);q+=(q-axis).normalized*.009f;q.y=Mathf.Max(.005f,q.y);
    wraps.Add(q,new Vector2(q.z*5,q.y*10),weights[j]);}wraps.t.AddRange(new[]{n,n+1,n+2});}
   Material Mat(string name,Color color,bool textured){var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.06f);m.SetFloat("_Cull",0);
    if(textured){var tex=new Texture2D(128,128,TextureFormat.RGBA32,false);tex.name="Original handworked hide grain";for(int y=0;y<128;y++)for(int x=0;x<128;x++){uint h=(uint)(x*374761393+y*668265263);h=(h^(h>>13))*1274126177;float n=.82f+(h%1000)/5000f;tex.SetPixel(x,y,new Color(n,n*.97f,n*.91f));}tex.Apply();AssetDatabase.CreateAsset(tex,folder+"/"+name+"-grain.asset");m.SetTexture("_BaseMap",tex);}AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
   var hideMat=Mat("Hunter sand hide",new Color(.62f,.43f,.25f),true);var cordMat=Mat("Hunter charcoal cord",new Color(.13f,.09f,.055f),false);var seamMat=Mat("Hunter ochre repair",new Color(.36f,.18f,.07f),false);
   int vertices=0,triangles=0;
   GameObject Save(Surface s,string name,Material mat){var mesh=new Mesh{name=name};mesh.SetVertices(s.v.Select(v=>body.transform.InverseTransformPoint(model.transform.TransformPoint(v))).ToList());mesh.SetUVs(0,s.uv);mesh.SetTriangles(s.t,0);mesh.boneWeights=s.w.ToArray();mesh.bindposes=source.bindposes;mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,folder+"/"+name+".asset");var go=new GameObject(name);go.transform.SetParent(root.transform,false);go.transform.SetPositionAndRotation(body.transform.position,body.transform.rotation);go.transform.localScale=body.transform.localScale;var r=go.AddComponent<SkinnedMeshRenderer>();r.sharedMesh=mesh;r.bones=bones;r.rootBone=body.rootBone;r.sharedMaterial=mat;r.quality=SkinQuality.Bone4;r.updateWhenOffscreen=true;r.localBounds=new Bounds(new Vector3(0,0,1),new Vector3(3,3,3));vertices+=mesh.vertexCount;triangles+=s.t.Count/3;return go;}
   Save(hide,"Hunter tunic",hideMat);Save(dark,"Hunter cord belt",cordMat);Save(trim,"Hunter repair stitches",seamMat);Save(trousers,"Hunter inner trousers",hideMat);Save(wraps,"Hunter foot wraps",hideMat);
   int baseVertices=vertices,baseTriangles=triangles;
   var hoodMat=Mat("Hunter sand hood",new Color(.64f,.55f,.38f),true);var mantleMat=Mat("Hunter smoke hide",new Color(.24f,.15f,.075f),true);
   Save(hood,"Hunter cold hood",hoodMat).SetActive(false);Save(mantle,"Hunter cold mantle",mantleMat).SetActive(false);
   // Locally authored tapered wooden club. Grip is the origin; heavy end points down.
   var clubMesh=CreateClubMesh();AssetDatabase.CreateAsset(clubMesh,folder+"/Hunter wooden club.asset");
   var club=new GameObject("Hunter wooden club");club.transform.SetParent(root.transform,false);club.AddComponent<MeshFilter>().sharedMesh=clubMesh;club.AddComponent<MeshRenderer>().sharedMaterial=mantleMat;
   var carry=model.AddComponent<HunterClubCarry>();carry.Animator=animator;carry.Actor=model.transform.parent;carry.Club=club.transform;carry.AuthorGrip();
   // Mask only fully covered body triangles, on a clone; original FBX and mesh stay intact.
   var masked=UnityEngine.Object.Instantiate(source);masked.name="Hunter body covered-face mask";
   var kept=new List<int>();
   for(int i=0;i<old.Length;i+=3){
    bool cover=true;
    for(int j=0;j<3;j++){
     var q=p[old[i+j]];
     bool vertexCovered=q.y<waist||(q.y>hip+.025f&&q.y<shoulder-.055f&&Mathf.Abs(q.x)<(armX-.042f));
     if(!vertexCovered)cover=false;
    }
    if(!cover)kept.AddRange(new[]{old[i],old[i+1],old[i+2]});
   }
   masked.triangles=kept.ToArray();AssetDatabase.CreateAsset(masked,folder+"/Hunter masked body.asset");body.sharedMesh=masked;
   Directory.CreateDirectory("evidence/local/hunter");File.WriteAllText("evidence/local/hunter/outfit-inventory.json","{\"baseVertices\":"+baseVertices+",\"baseTriangles\":"+baseTriangles+",\"allLayerVertices\":"+vertices+",\"allLayerTriangles\":"+triangles+",\"clubTriangles\":"+clubMesh.triangles.Length/3+",\"baseGarmentRenderers\":5,\"allGarmentRenderers\":7,\"materials\":5,\"hipY\":"+hip.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"hemY\":"+hem.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"bodyTrianglesRemoved\":"+((old.Length-kept.Count)/3)+"}");
  }
  public static Mesh CreateClubMesh() {
   var clubMesh=new Mesh{name="Original primitive wooden club"};var cv=new List<Vector3>();var ct=new List<int>();var cu=new List<Vector2>();
   const int clubRings=12,clubSides=10;for(int row=0;row<=clubRings;row++)for(int col=0;col<clubSides;col++){
    float f=row/(float)clubRings,ang=col*Mathf.PI*2/clubSides;float radius=HunterClubCarry.ClubRadius(f);
    cv.Add(new Vector3(Mathf.Cos(ang)*radius+HunterClubCarry.ClubCurve(f),.055f-f*HunterClubCarry.ClubLength,Mathf.Sin(ang)*radius));cu.Add(new Vector2(col/(float)clubSides,f));
    if(row<clubRings){int a=row*clubSides+col,b=row*clubSides+(col+1)%clubSides;ct.AddRange(new[]{a,b,b+clubSides,a,b+clubSides,a+clubSides});}}
   int cap=cv.Count;cv.Add(new Vector3(0,.055f,0));cu.Add(Vector2.zero);cv.Add(new Vector3(HunterClubCarry.ClubCurve(1),.055f-HunterClubCarry.ClubLength,0));cu.Add(Vector2.one);
   for(int col=0;col<clubSides;col++){ct.AddRange(new[]{cap,(col+1)%clubSides,col,cap+1,clubRings*clubSides+col,clubRings*clubSides+(col+1)%clubSides});}
   clubMesh.SetVertices(cv);clubMesh.SetUVs(0,cu);clubMesh.SetTriangles(ct,0);clubMesh.RecalculateNormals();clubMesh.RecalculateBounds();return clubMesh;
  }

 }
}
