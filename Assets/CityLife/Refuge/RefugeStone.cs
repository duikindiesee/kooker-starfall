using System.Collections.Generic;
using UnityEngine;
namespace Starfall.Refuge {
 public static class RefugeStone {
  // Deterministic, bevelled angular rock surface; same mesh renders and collides.
  public static Mesh Mesh(){const int rings=7,sides=12;var p=new Vector3[rings,sides];for(int r=0;r<rings;r++){float a=-Mathf.PI/2+r*Mathf.PI/(rings-1);for(int s=0;s<sides;s++){float b=s*Mathf.PI*2/sides;float R=Mathf.Pow(Mathf.Max(0,Mathf.Cos(a)),.55f)*.5f;float Shape(float f)=>Mathf.Sign(f)*Mathf.Pow(Mathf.Abs(f),.65f);p[r,s]=new Vector3(R*Shape(Mathf.Cos(b)),Mathf.Sign(a)*Mathf.Pow(Mathf.Abs(Mathf.Sin(a)),.65f)*.5f,R*Shape(Mathf.Sin(b)));}}
   var v=new List<Vector3>();var t=new List<int>();void Tri(Vector3 a,Vector3 b,Vector3 c){int i=v.Count;v.Add(a);v.Add(b);v.Add(c);t.Add(i);t.Add(i+1);t.Add(i+2);}for(int r=0;r<rings-1;r++)for(int s=0;s<sides;s++){int n=(s+1)%sides;Tri(p[r,s],p[r+1,s],p[r+1,n]);Tri(p[r,s],p[r+1,n],p[r,n]);}var m=new Mesh{name="Refuge angular sandstone v2"};m.SetVertices(v);m.SetTriangles(t,0);var colors=new Color[v.Count];for(int i=0;i<colors.Length;i++)colors[i]=Color.white;m.colors=colors;m.RecalculateNormals();m.RecalculateBounds();return m;}
 }
}
