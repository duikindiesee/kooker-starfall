using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    /// <summary>A bounded, deterministic shore study. Separate from the versioned island field.</summary>
    public static class CoastalRocks
    {
        public const int Seed = 4242;

        public static GameObject Create(Transform parent)
        {
            var root = new GameObject("Coastal stratified rock bank and succulent study");
            root.transform.SetParent(parent, false);
            var shader = Shader.Find("CityLife/CoastalRocks");
            if (shader == null) throw new InvalidOperationException("Missing CityLife/CoastalRocks shader.");
            var rock = new Material(shader) { name = "Warm charcoal iron-brown fractured shore stone" };
            rock.SetColor("_BaseColor", new Color(.18f, .135f, .11f, 1));
            var plants = new Material(shader) { name = "Blue green succulent wax and small flowers" };
            plants.SetColor("_BaseColor", Color.white);
            plants.SetFloat("_Vegetation", 1);

            // Interlocking slabs form the visible bank; unequal proportions and offsets avoid a boulder necklace.
            var banks = new[]
            {
                new Vector4(4.8f,-4.9f,3.0f,1.35f), new Vector4(7.1f,-2.8f,2.45f,2.1f),
                new Vector4(3.0f,-7.0f,3.6f,.95f), new Vector4(-.6f,-7.6f,2.8f,1.3f),
                new Vector4(-4.0f,-6.1f,2.2f,.7f), new Vector4(-6.8f,-3.4f,2.8f,1.8f),
                new Vector4(7.7f,1.0f,2.6f,1.45f), new Vector4(5.8f,5.3f,3.2f,1.1f),
                new Vector4(2.1f,7.9f,2.1f,.75f), new Vector4(-2.2f,7.6f,2.6f,1.55f),
                new Vector4(-6.0f,5.4f,2.15f,1.3f), new Vector4(-3.8f,2.4f,2.0f,1.65f),
                new Vector4(3.8f,3.1f,1.5f,.6f), new Vector4(5.7f,-7.9f,2.25f,1.3f),
                new Vector4(8.4f,-5.3f,2.0f,1.5f), new Vector4(9.8f,-.8f,1.7f,1.35f),
                new Vector4(7.9f,7.2f,2.7f,1.05f), new Vector4(.4f,-10.7f,2.9f,1.7f),
                new Vector4(4.0f,-10.0f,2.3f,.95f), new Vector4(-4.5f,-9.7f,1.7f,.8f)
            };
            for (int i = 0; i < banks.Length; i++)
            {
                Vector4 b = banks[i];
                AddRock(root.transform, rock, b.x, b.y, b.z*.72f, b.w*.72f, b.z * Lerp(.38f,.61f,i,2), i);
            }
            // Smaller fractures collect at the foot of the large blocks, including below the waterline.
            for (int i = 0; i < 48; i++)
            {
                int pocket=i/8;
                float angle = -2.85f+pocket*1.12f+Lerp(-.16f,.16f,i,10), radius = Lerp(7.4f, 12.5f, i, 11);
                float x = Mathf.Cos(angle) * radius, z = Mathf.Sin(angle) * radius;
                float width = Lerp(.20f, .68f, i, 12);
                AddRock(root.transform, rock, x, z, width, width * Lerp(.35f,.85f,i,13),
                    width * Lerp(.45f,.95f,i,14), 100+i);
            }
            // A few grouped shore outcrops repeat the geology farther down the river, at different scales.
            var shore = new[] { new Vector2(19,21), new Vector2(-24,37), new Vector2(21,47), new Vector2(-27,60) };
            for (int group = 0; group < shore.Length; group++)
            for (int i = 0; i < 4; i++)
            {
                int id = 200 + group * 7 + i;
                Vector2 p = shore[group] + new Vector2(Lerp(-2.5f,2.5f,id,1),Lerp(-3,3,id,2));
                float width = Lerp(1.2f,3.2f,id,3);
                AddRock(root.transform,rock,p.x,p.y,width,Lerp(.8f,2.6f,id,4),width*.65f,id);
            }

            var flora = new MeshData();
            var plantSites = new[]
            {
                new Vector2(3.9f,-5.2f), new Vector2(5.7f,-3.5f), new Vector2(1.9f,-6.2f),
                new Vector2(-2.1f,-5.8f), new Vector2(-5.6f,-2.6f), new Vector2(6.5f,.9f),
                new Vector2(3.8f,5.6f), new Vector2(-3.5f,6.1f), new Vector2(-5.4f,3.5f),
                new Vector2(14,18), new Vector2(-22,33), new Vector2(18,43),
                // Deterministic foothill pockets: clustered around existing outcrops,
                // leaving broad sand lanes and all gameplay flats unobstructed.
                new Vector2(22,19), new Vector2(17,22), new Vector2(-27,35), new Vector2(-20,39),
                new Vector2(25,45), new Vector2(15,48), new Vector2(-44,82), new Vector2(-39,86),
                new Vector2(48,118), new Vector2(54,121), new Vector2(-74,176), new Vector2(-68,181),
                new Vector2(94,238), new Vector2(101,242), new Vector2(-112,305), new Vector2(-105,310)
            };
            for (int i = 0; i < plantSites.Length; i++)
            {
                Vector2 p = plantSites[i];
                var position = new Vector3(p.x, CoastalTerrain.Height(p.x,p.y) - .025f, p.y);
                float scale = i < 12 ? Lerp(.62f,1.12f,i,33) : Lerp(1.20f,2.20f,i,33);
                if (i == 1 || i == 7 || i == 10) AddColumnSucculent(flora,position,scale,i);
                else AddRosette(flora,position,scale,i);
            }
            MeshObject("Sparse fleshy succulents and pink purple flowers",flora.ToMesh("Coastal succulent group"),plants,root.transform,false);

            // Render-only submerged life uses the same deterministic succulent geometry,
            // rooted at actual river-bed samples. It conveys a living shallow bed without
            // claiming swimming, harvesting or navigation authority.
            var aquatic = new MeshData();
            var aquaticSites = new[] { new Vector2(-2,34), new Vector2(17,118), new Vector2(-18,270), new Vector2(63,408), new Vector2(4,585) };
            for(int i=0;i<aquaticSites.Length;i++)
            {
                Vector2 p=aquaticSites[i]; Vector3 bed=new Vector3(p.x,CoastalTerrain.Height(p.x,p.y)+.05f,p.y);
                var bedRock=MeshObject("Submerged dark reef shelf "+i,RockMesh(4.2f+i*.55f,1.15f+i*.12f,3.2f+i*.42f,360+i),rock,root.transform,false);
                bedRock.transform.localPosition=bed-Vector3.up*.10f;
                bedRock.transform.localRotation=Quaternion.Euler(0,Lerp(-180,180,i,123),0);
                // Two overlapping fractured ledges form one connected shelf silhouette.
                // They remain render-only and stay below the same sampled bed level.
                for(int ledge=0;ledge<2;ledge++)
                {
                    int id=470+i*11+ledge;
                    Vector2 offset=ledge==0?new Vector2(2.7f,1.0f):new Vector2(-2.4f,-1.2f);
                    Vector2 q=p+offset;
                    float floor=CoastalTerrain.Height(q.x,q.y);
                    if(floor>-2.28f) continue;
                    var connected=MeshObject("Submerged connected reef ledge "+i+"-"+ledge,
                        RockMesh(2.8f+ledge*.55f,1.02f+ledge*.12f,2.15f+ledge*.55f,id),rock,root.transform,false);
                    connected.transform.localPosition=new Vector3(q.x,floor-.12f,q.y);
                    connected.transform.localRotation=Quaternion.Euler(0,Lerp(-35,35,id,124),0);
                }
                // Broken shelf rubble gives the clear-water view real silhouettes and
                // scale variation instead of one isolated slab on a featureless bed.
                // These are visual ecology only: no collider is authored here.
                for(int fragment=0;fragment<12;fragment++)
                {
                    int id=600+i*31+fragment;
                    float angle=Lerp(-Mathf.PI,Mathf.PI,id,201);
                    float radius=Lerp(2.2f,8.4f,id,202);
                    Vector2 q=p+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                    float width=Lerp(.38f,1.35f,id,203),height=Lerp(.22f,.72f,id,204),depth=width*Lerp(.55f,1.25f,id,205);
                    float floor=CoastalTerrain.Height(q.x,q.y);
                    if(floor>-2.28f) continue; // never label exposed shore rubble as submerged ecology
                    var rubble=MeshObject("Submerged fractured reef fragment "+i+"-"+fragment,RockMesh(width,height,depth,id),rock,root.transform,false);
                    rubble.transform.localPosition=new Vector3(q.x,floor-height*.10f,q.y);
                    rubble.transform.localRotation=Quaternion.Euler(Lerp(-8,8,id,206),Lerp(-180,180,id,207),Lerp(-6,6,id,208));
                }
                // Low irregular rosettes sit between rubble; tall flower spikes are
                // intentionally excluded from the primary shallow-bed silhouette.
                for(int clump=0;clump<7;clump++)
                {
                    int id=700+i*23+clump;
                    float angle=Lerp(-Mathf.PI,Mathf.PI,id,211),radius=Lerp(1.4f,7.1f,id,212);
                    Vector2 q=p+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                    float floor=CoastalTerrain.Height(q.x,q.y);
                    if(floor>-2.72f) continue; // keep the complete low rosette beneath the surface
                    Vector3 rooted=new Vector3(q.x,floor+.035f,q.y);
                    AddRosette(aquatic,rooted,Lerp(.62f,1.30f,id,213),id,false);
                }
            }
            MeshObject("Submerged blue green river plants - render only",aquatic.ToMesh("Coastal aquatic plant pockets"),plants,root.transform,false);

            // Sparse small silhouettes step across distant terraces. They are decorative,
            // collider-free secondary Kookerbooms; the accepted hero tree remains unchanged.
            var terraceTrees=new MeshData();
            var treeSites=new[] { new Vector2(-245,-150),new Vector2(-225,-132),new Vector2(258,120),new Vector2(282,137),new Vector2(-275,330),new Vector2(302,365) };
            for(int i=0;i<treeSites.Length;i++) AddTerraceKookerboom(terraceTrees,treeSites[i],9f+Lerp(0,4f,i,140),400+i);
            MeshObject("Sparse terrace Kookerboom groups - decorative",terraceTrees.ToMesh("Terrace Kookerboom silhouettes"),plants,root.transform,false);
            return root;
        }

        static void AddTerraceKookerboom(MeshData mesh,Vector2 site,float height,int id)
        {
            // Exclude the authored gameplay terraces even if sites are later revised.
            if(Vector2.Distance(site,CoastalTerrain.ActivityCentre)<55 || Vector2.Distance(site,CoastalTerrain.RefugeCentre)<55) return;
            Vector3 root=new Vector3(site.x,CoastalTerrain.Height(site.x,site.y),site.y);
            Color bark=new Color(.47f,.30f,.16f),pale=new Color(.70f,.49f,.27f);
            Vector3 fork=root+Vector3.up*height*.58f;
            Stem(mesh,root,fork,height*.075f,bark);
            for(int branch=0;branch<3;branch++)
            {
                float angle=branch*2.094f+id*.31f;
                Vector3 tip=fork+new Vector3(Mathf.Cos(angle)*height*.24f,height*(.28f+(branch%2)*.05f),Mathf.Sin(angle)*height*.24f);
                Stem(mesh,fork,tip,height*.047f,pale);
                AddRosette(mesh,tip,height*.30f,id*7+branch);
            }
        }

        static void AddRock(Transform parent, Material material, float x, float z, float width, float height, float depth, int id)
        {
            // Bottom is embedded, while the visible top and collider come from the same mesh.
            var go = MeshObject("Stratified shore rock " + id,RockMesh(width,height,depth,id),material,parent,true);
            // R01 camera03 exposed an excessive downslope overhang on this one block.
            // Its sampled lower face stood 1.02 m above the field despite upslope contact.
            float placementCorrection = id == 209 ? 1.15f : 0;
            go.transform.localPosition = new Vector3(x,CoastalTerrain.Height(x,z) - height*.13f-placementCorrection,z);
            go.transform.localRotation = Quaternion.Euler(0,Lerp(-180,180,id,90),0);
        }

        static Mesh RockMesh(float width,float height,float depth,int id)
        {
            int sides = 7 + (int)(Hash(id,50)*4), rings = 5;
            var p = new Vector3[rings,sides];
            float[] y = { -.58f,-.16f,.14f,.49f,.64f };
            // Recessed upper shoulders and a smaller broken crown reduce the broad
            // table-top silhouette while preserving the same collider/render mesh.
            float[] radius = { .64f,1,.86f,.73f,.45f };
            float leanX = Lerp(-.19f,.19f,id,51), leanZ = Lerp(-.15f,.15f,id,52);
            for (int ring = 0; ring < rings; ring++)
            for (int side = 0; side < sides; side++)
            {
                float angle = side * (Mathf.PI*2/sides) + Lerp(-.11f,.11f,id*31+side,53);
                float sector = Lerp(.75f,1.16f,id*31+side,54);
                float fracture = Lerp(.91f,1.07f,id*137+ring*17+side,55);
                // The same sector runs through the layers, producing broad fractures instead of random spikes.
                p[ring,side] = new Vector3(
                    Mathf.Cos(angle)*width*radius[ring]*sector*fracture + leanX*y[ring]*width,
                    height*(y[ring]+Lerp(-.065f,.065f,id*71+side,56)) + width*.025f*Mathf.Sin(angle+id),
                    Mathf.Sin(angle)*depth*radius[ring]*sector*fracture + leanZ*y[ring]*depth);
            }
            var mesh = new MeshData();
            Color variation = new Color(Lerp(.81f,1.13f,id,61),Lerp(.86f,1.08f,id,62),Lerp(.91f,1.06f,id,63),1);
            for (int ring=0;ring<rings-1;ring++)
            for (int side=0;side<sides;side++)
            {
                int next=(side+1)%sides;
                mesh.Quad(p[ring,side],p[ring+1,side],p[ring+1,next],p[ring,next],variation);
            }
            Vector3 top=Vector3.zero,bottom=Vector3.zero;
            for(int side=0;side<sides;side++){top+=p[rings-1,side]/sides;bottom+=p[0,side]/sides;}
            top.y += height*.018f;
            for(int side=0;side<sides;side++)
            {
                int next=(side+1)%sides;
                mesh.Tri(top,p[rings-1,next],p[rings-1,side],variation);
                mesh.Tri(bottom,p[0,side],p[0,next],variation);
            }
            return mesh.ToMesh("Fractured layered block " + id);
        }

        static void AddRosette(MeshData mesh,Vector3 root,float scale,int id,bool flowers=true)
        {
            int count=14+(int)(Hash(id,73)*5);
            for(int leaf=0;leaf<count;leaf++)
            {
                float angle=leaf*2.39996323f+id;
                float age=leaf/(float)(count-1);
                float length=scale*Lerp(.65f,1.05f,id*29+leaf,74)*(1-age*.45f);
                Vector3 direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3 side=new Vector3(-direction.z,0,direction.x);
                float lift=Mathf.Lerp(.32f,1.1f,age);
                Color color=Color.Lerp(new Color(.075f,.30f,.28f),new Color(.28f,.59f,.54f),Hash(id*31+leaf,75));
                Vector3[] centers = {
                    root+Vector3.up*.08f*scale,
                    root+direction*length*.35f+Vector3.up*length*.35f,
                    root+direction*length*.72f+Vector3.up*length*lift*.75f,
                    root+direction*length+Vector3.up*length*lift
                };
                float[] widths={.035f,.14f,.095f,0};
                var sections=new Vector3[4,4];
                for(int s=0;s<4;s++)
                {
                    float w=widths[s]*scale;
                    sections[s,0]=centers[s]-side*w;
                    sections[s,1]=centers[s]+Vector3.up*w*.6f;
                    sections[s,2]=centers[s]+side*w;
                    sections[s,3]=centers[s]-Vector3.up*w*.28f;
                }
                for(int s=0;s<3;s++)for(int face=0;face<4;face++)
                {
                    int next=(face+1)%4;
                    if(s==2) mesh.Tri(sections[s,face],sections[s,next],sections[s+1,face],color);
                    else mesh.Quad(sections[s,face],sections[s,next],sections[s+1,next],sections[s+1,face],color);
                }
            }
            if(flowers&&id%3==0)
            {
                var stemTop=root+new Vector3(.09f,scale*1.17f,.02f);
                Stem(mesh,root,stemTop,.022f*scale,new Color(.27f,.40f,.27f));
                for(int i=0;i<4;i++)
                {
                    float angle=i*2.4f+id;
                    Vector3 offset=new Vector3(Mathf.Cos(angle)*.12f, i*.055f,Mathf.Sin(angle)*.12f)*scale;
                    Flower(mesh,stemTop+offset,.115f*scale,id+i);
                }
            }
        }

        static void AddColumnSucculent(MeshData mesh,Vector3 root,float scale,int id)
        {
            for(int column=0;column<3;column++)
            {
                Vector3 anchor=root+new Vector3((column-1)*.21f*scale,0,column==1?-.03f:.09f);
                float height=scale*(column==1?1.75f:1.1f), radius=scale*(column==1?.115f:.09f);
                const int sides=12, rings=7;
                var p=new Vector3[rings,sides];
                Color color=new Color(.10f,.38f,.34f);
                for(int ring=0;ring<rings;ring++)for(int side=0;side<sides;side++)
                {
                    float t=ring/(float)(rings-1),a=side*Mathf.PI*2/sides;
                    float r=radius*(side%2==0?1:.73f)*(ring==rings-1?.10f:Mathf.Sin((t*.79f+.12f)*Mathf.PI));
                    p[ring,side]=anchor+new Vector3(Mathf.Cos(a)*r+(column-1)*t*t*.19f*scale,t*height,Mathf.Sin(a)*r);
                }
                for(int ring=0;ring<rings-1;ring++)for(int side=0;side<sides;side++)
                {
                    int next=(side+1)%sides;
                    mesh.Quad(p[ring,side],p[ring+1,side],p[ring+1,next],p[ring,next],side%2==0?color:color*1.15f);
                }
                Vector3 tip=anchor+new Vector3((column-1)*.19f*scale,height,0);
                for(int side=0;side<sides;side++)mesh.Tri(tip,p[rings-1,(side+1)%sides],p[rings-1,side],color);
                if(column!=0)Flower(mesh,tip+Vector3.up*.025f,scale*.13f,id+column);
            }
        }

        static void Stem(MeshData mesh,Vector3 bottom,Vector3 top,float radius,Color color)
        {
            Vector3 axis=(top-bottom).normalized;
            Vector3 right=Vector3.Cross(axis,Vector3.forward).normalized;
            Vector3 forward=Vector3.Cross(right,axis);
            for(int i=0;i<5;i++)
            {
                float a=i*Mathf.PI*2/5,b=(i+1)*Mathf.PI*2/5;
                Vector3 p=(right*Mathf.Cos(a)+forward*Mathf.Sin(a))*radius;
                Vector3 q=(right*Mathf.Cos(b)+forward*Mathf.Sin(b))*radius;
                mesh.Quad(bottom+p,top+p,top+q,bottom+q,color);
            }
        }

        static void Flower(MeshData mesh,Vector3 center,float radius,int id)
        {
            Color pink=Color.Lerp(new Color(.68f,.09f,.36f),new Color(.58f,.19f,.66f),Hash(id,80));
            for(int petal=0;petal<5;petal++)
            {
                float a=petal*Mathf.PI*2/5+id;
                Vector3 direction=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                Vector3 side=new Vector3(-direction.z,0,direction.x);
                Vector3 left=center+direction*radius*.55f-side*radius*.36f+Vector3.up*radius*.16f;
                Vector3 tip=center+direction*radius+Vector3.up*radius*.35f;
                Vector3 right=center+direction*radius*.55f+side*radius*.36f+Vector3.up*radius*.16f;
                mesh.Quad(center,left,tip,right,pink);
                mesh.Quad(center,right,tip,left,pink*.78f);
            }
            mesh.Tri(center+new Vector3(-radius*.15f,.025f,0),center+new Vector3(0,.025f,radius*.15f),center+new Vector3(radius*.15f,.025f,0),new Color(.98f,.66f,.15f));
        }

        static GameObject MeshObject(string name,Mesh mesh,Material material,Transform parent,bool collider)
        {
            var go=new GameObject(name);
            go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            if(collider)go.AddComponent<MeshCollider>().sharedMesh=mesh;
            return go;
        }

        static float Hash(int id,int channel)
        {
            uint h=unchecked((uint)(id*374761393+channel*668265263+Seed));
            h=(h^(h>>13))*1274126177u;
            return ((h^(h>>16))&0x00ffffff)/16777216f;
        }
        static float Lerp(float low,float high,int id,int channel)=>Mathf.Lerp(low,high,Hash(id,channel));

        sealed class MeshData
        {
            readonly List<Vector3> positions=new List<Vector3>();
            readonly List<int> triangles=new List<int>();
            readonly List<Color> colors=new List<Color>();
            public void Tri(Vector3 a,Vector3 b,Vector3 c,Color color)
            {
                int start=positions.Count;
                positions.Add(a);positions.Add(b);positions.Add(c);
                colors.Add(color);colors.Add(color);colors.Add(color);
                triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
            {Tri(a,b,c,color);Tri(a,c,d,color);}
            public Mesh ToMesh(string name)
            {
                var mesh=new Mesh {name=name};
                if(positions.Count>65535)mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(positions);mesh.SetTriangles(triangles,0);mesh.SetColors(colors);
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
