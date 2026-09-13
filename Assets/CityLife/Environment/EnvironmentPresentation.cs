using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using CityLife.World;

namespace Starfall.EnvironmentFoundation
{
    // Render-only architecture. Test transforms, colliders, forces and clocks are never edited.
    public static class EnvironmentPresentation
    {
        static Mesh cube;
        static Material stone,metal,trim;
        [Serializable] sealed class Manifest
        {
            public string revision="starfall.environment-terrace.v1",beforeColliderFingerprint,afterColliderFingerprint;
            public bool collidersUnchanged;public int supportPiers=6;
            public string scope="Render-only supported test terrace; no new walkable/collision surfaces. All existing test positions and physics components retained.";
        }
        static string ColliderFingerprint()
        {
            var all=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.InstanceID);var text=new StringBuilder();
            foreach(var c in all)
            {
                if(!c.enabled||!c.gameObject.activeInHierarchy)continue;
                text.Append(c.name).Append('|').Append(c.GetType().Name).Append('|');
                var m=c.transform.localToWorldMatrix;for(int i=0;i<16;i++)text.Append(m[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                text.Append(c.isTrigger).Append(';');
            }
            using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","").ToLowerInvariant();
        }
        static GameObject VisualBox(string name,Vector3 position,Vector3 size,Material material,Transform parent)
        {
            var g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.position=position;g.transform.localScale=size;
            g.AddComponent<MeshFilter>().sharedMesh=cube;g.AddComponent<MeshRenderer>().sharedMaterial=material;return g;
        }
        static void Beam(string name,Vector3 a,Vector3 b,float width,Material material,Transform parent)
        {var g=VisualBox(name,(a+b)*.5f,new Vector3(width,width,(b-a).magnitude),material,parent);g.transform.rotation=Quaternion.LookRotation(b-a);}
        static void Pier(float x,float z,Transform parent)
        {
            float ground=CoastalTerrain.Height(x,z)-1.2f; const int sides=8;var vertices=new Vector3[sides*2];var triangles=new List<int>();
            for(int ring=0;ring<2;ring++)for(int i=0;i<sides;i++)
            {float a=i*Mathf.PI*2/sides;float r=ring==0?4.4f:2.1f;vertices[ring*sides+i]=new Vector3(x+Mathf.Cos(a)*r,ring==0?ground:38.8f,z+Mathf.Sin(a)*r);}
            for(int i=0;i<sides;i++){int next=(i+1)%sides;triangles.AddRange(new[]{i,i+sides,next,next,i+sides,next+sides});}
            for(int i=1;i<sides-1;i++)triangles.AddRange(new[]{sides,sides+i,sides+i+1});
            var mesh=new Mesh{name="Tapered sandstone pier - presentation only"};mesh.vertices=vertices;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
            var g=new GameObject("Bedrock-anchored sandstone support");g.transform.SetParent(parent,false);g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=stone;
            for(float y=ground+3;y<38;y+=5)VisualBox("Ochre pier binding",new Vector3(x,y,z),new Vector3(4.4f,.28f,4.4f),metal,parent);
            VisualBox("Pier capital",new Vector3(x,38.4f,z),new Vector3(5,1.2f,5),stone,parent);
        }
        public static void Apply(EnvironmentWorld world)
        {
            var before=ColliderFingerprint();var root=new GameObject("Starfall supported environment terrace - render only").transform;root.SetParent(world.transform,false);
            var template=GameObject.CreatePrimitive(PrimitiveType.Cube);template.GetComponent<Collider>().enabled=false;cube=template.GetComponent<MeshFilter>().sharedMesh;UnityEngine.Object.Destroy(template);
            stone=EnvironmentWorld.Material(new Color(.47f,.32f,.19f));metal=EnvironmentWorld.Material(new Color(.16f,.23f,.24f));trim=EnvironmentWorld.Material(new Color(.59f,.43f,.22f));
            stone.SetFloat("_Smoothness",.08f);metal.SetFloat("_Smoothness",.28f);
            var texture=new Texture2D(64,64,TextureFormat.RGB24,false);texture.name="Seeded sandstone grain";texture.wrapMode=TextureWrapMode.Repeat;
            for(int y=0;y<64;y++)for(int x=0;x<64;x++){uint h=unchecked((uint)(x*73856093^y*19349663^1904243));h^=h>>13;float n=(h%1000)/1000f;texture.SetPixel(x,y,new Color(.78f+n*.22f,.78f+n*.22f,.78f+n*.22f));}texture.Apply();stone.SetTexture("_BaseMap",texture);stone.SetTextureScale("_BaseMap",new Vector2(6,6));
            foreach(float x in new[]{-38f,-25f,-12f})foreach(float z in new[]{-10f,10f})Pier(x,z,root);
            foreach(float z in new[]{-10f,10f})for(int i=0;i<2;i++)
            {float x=-38+i*13;Beam("Dark cross-brace",new Vector3(x,24,z),new Vector3(x+13,37.5f,z),.5f,metal,root);Beam("Dark cross-brace",new Vector3(x,37.5f,z),new Vector3(x+13,24,z),.5f,metal,root);}
            VisualBox("Deep sandstone terrace fascia",new Vector3(-25,38.9f,0),new Vector3(34,1.2f,25),stone,root);
            foreach(float z in new[]{-12.45f,12.45f})VisualBox("Brass edge inlay",new Vector3(-25,40.007f,z),new Vector3(33.8f,.008f,.08f),trim,root);
            foreach(float x in new[]{-41.9f,-8.1f})VisualBox("Brass edge inlay",new Vector3(x,40.007f,0),new Vector3(.08f,.008f,24.8f),trim,root);
            foreach(var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if(r.name=="Test deck"||r.name=="30 degree slope")r.sharedMaterial=stone;
                else if(r.name=="Collision wall")r.sharedMaterial=trim;
                else if(r.name=="Shelter roof"||r.name=="Shelter post")r.sharedMaterial=metal;
            }
            // Delineate test stations on the existing surface without changing contact geometry.
            foreach(float x in new[]{-33f,-28f,-20.5f})VisualBox("Station separation inlay",new Vector3(x,40.008f,2),new Vector3(.055f,.008f,14),trim,root);
            for(int i=0;i<6;i++)VisualBox("Shelter roof rib",new Vector3(-40+i*1.2f,43.18f,-7),new Vector3(.09f,.07f,5.9f),trim,root);
            Shader sky=Shader.Find("Starfall/EnvironmentSky");if(sky==null||!sky.isSupported)throw new InvalidOperationException("Environment sky shader unavailable");
            RenderSettings.skybox=new Material(sky);world.View.clearFlags=CameraClearFlags.Skybox;
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.20f,.30f,.43f);RenderSettings.ambientEquatorColor=new Color(.30f,.27f,.24f);RenderSettings.ambientGroundColor=new Color(.15f,.12f,.10f);
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))if(light.name=="Environment sun"){light.color=new Color(1,.81f,.6f);light.intensity=1.7f;}
            string after=ColliderFingerprint();var manifest=new Manifest{beforeColliderFingerprint=before,afterColliderFingerprint=after,collidersUnchanged=before==after};
            var args=System.Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-environmentEvidence");if(at>=0&&at+1<args.Length&&Path.IsPathRooted(args[at+1])){Directory.CreateDirectory(args[at+1]);File.WriteAllText(Path.Combine(args[at+1],"presentation-manifest.json"),JsonUtility.ToJson(manifest,true));}
            if(!manifest.collidersUnchanged)throw new InvalidOperationException("Presentation changed test collision setup");
        }
    }
}
