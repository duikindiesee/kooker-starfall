using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CityLife.World.Editor
{
    public static partial class KokerboomRender
    {
        private static bool coastalMode;
        private static GameObject coast;
        private static GameObject coastalTerrain;
        private static GameObject coastalWater;
        public static void RenderCoastalSlice(){coastalMode=true;ph02FamilyMode=true;hybridMode=true;Run(false);}
        public static void BuildCoastalPlayableSlice(){coastalMode=true;playablePreviewMode=true;ph02FamilyMode=true;hybridMode=true;Run(false);}

        private static void Coastal()
        {
            Cosmic();StrongCoolFill();
            cosmicFloor.SetActive(false);companions.SetActive(false);
            if(coast==null)
            {
                coast=new GameObject("Starfall coastal slice v1 - separate world study");
                GameObject terrain=CoastalTerrain.Create(coast.transform);
                coastalTerrain=terrain;
                GameObject rocks=CoastalRocks.Create(coast.transform);
                GameObject water=CoastalWater.Create(coast.transform); coastalWater=water;
                subjects.Add(new Subject{Root=terrain,Kind="coastal-terrain"});
                subjects.Add(new Subject{Root=rocks,Kind="coastal-rocks-and-succulents"});
                subjects.Add(new Subject{Root=water,Kind="coastal-water-surface"});
                BuildCoastalGalaxy();
                File.WriteAllText(Path.Combine(outputDirectory,"coastal-definition.json"),JsonUtility.ToJson(new CoastalDefinition(),true));
                VerifyCoastalCollision(terrain,rocks);
            }
            coast.SetActive(true);
            if(coastalTerrain==null)coastalTerrain=GameObject.Find("Coastal terrain "+CoastalTerrain.DefinitionId+" seed "+CoastalTerrain.Seed);
            if(coastalWater==null)coastalWater=GameObject.Find("Coastal water - luminous river and sea");
            camera.GetComponent<UniversalAdditionalCameraData>().requiresDepthTexture=true;
            camera.GetComponent<UniversalAdditionalCameraData>().requiresColorTexture=true;
            camera.farClipPlane=1500;
        }

        [Serializable]private sealed class CoastalDefinition
        {
            public string worldId=CoastalTerrain.DefinitionId,terrainContentRevision=CoastalTerrain.ContentRevision;
            public int terrainSeed=CoastalTerrain.Seed,treeSeed=4242,rockSeed=CoastalRocks.Seed;
            public string terrainSourceSha256=IslandDefinition.Hash(File.ReadAllBytes("Assets/CityLife/Scripts/CoastalTerrain.cs"));
            public string rockSourceSha256=IslandDefinition.Hash(File.ReadAllBytes("Assets/CityLife/Scripts/CoastalRocks.cs"));
            public string waterSourceSha256=IslandDefinition.Hash(File.ReadAllBytes("Assets/CityLife/Scripts/CoastalWater.cs"));
            public string reference="evidence/references/opening-world-approved-panorama-20260914.png; approved-water-shelves-20260914.png; approved-underwater-light-20260914.png; approved-tree-bank-detail-20260914.png; approved-rocky-foothill-planting-20260914.png";
            public Vector3 minimum=new Vector3(CoastalTerrain.MinX,-18,CoastalTerrain.MinZ),maximum=new Vector3(CoastalTerrain.MaxX,170,CoastalTerrain.MaxZ);
            public float waterLevelMetres=-2;
            public Vector3 sideCameraPosition=new Vector3(21,4.6f,-25),sideCameraTarget=new Vector3(0,3.4f,14);
            public float fieldOfView=52;
            public string visualOcean="Additional surface x[-1800,1800],z[900,2200], visual only; outside active terrain/collision slice";
            public string acceptance="Actual Unity composition and scene collider probes; no exact reference match, swimming or native coastal player acceptance";
        }

        private static void VerifyCoastalCollision(GameObject terrain,GameObject rocks)
        {
            Physics.SyncTransforms();
            var result=new CoastalCollisionRecord();
            foreach(MeshCollider collider in rocks.GetComponentsInChildren<MeshCollider>())
            {
                result.rockColliders++;
                if(collider.sharedMesh!=collider.GetComponent<MeshFilter>().sharedMesh||collider.convex||!collider.enabled)throw new InvalidOperationException("Rock collider differs from rendered mesh.");
                var ray=new Ray(new Vector3(collider.bounds.center.x,collider.bounds.max.y+2,collider.bounds.center.z),Vector3.down);
                if(!collider.Raycast(ray,out RaycastHit hit,collider.bounds.size.y+4))throw new InvalidOperationException("Actual rock collider raycast failed: "+collider.name);
                result.rockRayHits++;
            }
            if(result.rockColliders!=84)throw new InvalidOperationException("Expected the 84 recorded coastal rock colliders.");
            MeshCollider ground=terrain.GetComponent<MeshCollider>();
            if(ground==null)ground=terrain.GetComponentInChildren<MeshCollider>();
            if(ground==null)throw new InvalidOperationException("Coastal terrain collider missing.");
            Vector3[] vertices=ground.sharedMesh.vertices;
            for(int i=1;i<=8;i++)
            {
                Vector3 expected=ground.transform.TransformPoint(vertices[i*(vertices.Length/9)]);
                if(!ground.Raycast(new Ray(expected+Vector3.up*100,Vector3.down),out RaycastHit hit,140)||Mathf.Abs(hit.point.y-expected.y)>.025f)throw new InvalidOperationException("Terrain collider does not match its actual mesh vertex.");
                result.terrainRayHits++;
            }
            result.status="PASS";
            File.WriteAllText(Path.Combine(outputDirectory,"coastal-collision.json"),JsonUtility.ToJson(result,true));
        }
        [Serializable]private sealed class CoastalCollisionRecord
        {
            public string status;
            public int rockColliders,rockRayHits,terrainRayHits;
            public string scope="Actual Unity scene PhysX rays against exact static rendered rock/terrain meshes. Not a native coastal player movement or swimming test.";
        }

        private static void CoastalCamera(Vector3 position,Vector3 aim)
        {
            Coastal();Perspective(position,aim,width*9/16);camera.fieldOfView=52;
        }

        private static List<Shot> BuildCoastalShots()=>new List<Shot>{
            new Shot{Id="01-coastal-side-composition",Purpose="Fixed provisional side-view: frozen R19 tree on rocky bank, wrapping turquoise river toward deep sea, canyon mountains, blue gas giant and distant procedural galaxy. Actual Unity geometry; no artwork billboard.",Height=width*9/16,Configure=()=>CoastalCamera(new Vector3(21,4.6f,-25),new Vector3(0,3.4f,14))},
            new Shot{Id="02-water-to-sea",Purpose="Fixed surface material/depth comparison from the river toward open sea; no underwater-life or swimming claim.",Height=width*9/16,Configure=()=>CoastalCamera(new Vector3(10,-.1f,13),new Vector3(-2,-1,64))},
            new Shot{Id="03-rocky-tree-bank",Purpose="Fixed gameplay-distance rock bank, grounding and secondary succulent surface view; collision geometry exists but native input not yet verified.",Height=width*9/16,Configure=()=>CoastalCamera(new Vector3(13,1.9f,-12),new Vector3(2,-.4f,-3))},
            new Shot{Id="04-canyon-opening",Purpose="Fixed overview of layered canyon banks, river continuity and opening to sea. Element critique separate from combined scene.",Height=width*9/16,Configure=()=>CoastalCamera(new Vector3(-17,10,5),new Vector3(11,6,46))},
            new Shot{Id="05-shallow-bed-only-diagnostic",Purpose="Diagnostic actual opaque bed, shelves and planted geometry with only the water renderer disabled. Not a playable-water appearance or acceptance image.",Height=width*9/16,Configure=()=>{CoastalCamera(new Vector3(-11,3.2f,20),new Vector3(-2,-2.25f,34));coastalWater.SetActive(false);}},
            new Shot{Id="06-shallow-water-matched-diagnostic",Purpose="Exact camera matched to 05 with the authored water restored; establishes what the surface hides or transmits before further visual work.",Height=width*9/16,Configure=()=>{CoastalCamera(new Vector3(-11,3.2f,20),new Vector3(-2,-2.25f,34));coastalWater.SetActive(true);}},
            new Shot{Id="07-offshore-islands-sea-vista",Purpose="Traversable-boundary lookout toward three inaccessible render-only offshore landforms and the visual sea continuation. No boat or island navigation claim.",Height=width*9/16,Configure=()=>{CoastalCamera(new Vector3(0,32,520),new Vector3(-40,18,1250));camera.farClipPlane=2400;}}
        };

        private static void BuildCoastalGalaxy()
        {
            var mesh=new Mesh{name="Procedural distant galaxy sky plane"};
            Vector3[] vertices={new Vector3(-2000,-400,600),new Vector3(2000,-400,600),new Vector3(2000,1200,600),new Vector3(-2000,1200,600)};
            mesh.vertices=vertices;
            var uv=new Vector2[4];for(int i=0;i<4;i++)uv[i]=new Vector2((vertices[i].x+650)/1300,(vertices[i].y+60)/480);
            mesh.uv=uv;
            mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateBounds();owned.Add(mesh);
            var sky=new GameObject("Distant galaxy - procedural dust and stellar band");sky.transform.SetParent(backdrop.transform,false);
            sky.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=sky.AddComponent<MeshRenderer>();renderer.sharedMaterial=TemporaryShaderMaterial("Coastal galaxy sky",CoastalGalaxyShader);renderer.shadowCastingMode=ShadowCastingMode.Off;
        }

        private const string CoastalGalaxyShader=@"
Shader ""Hidden/Starfall/CoastalGalaxy"" {
SubShader {Tags {""RenderPipeline""=""UniversalPipeline"" ""Queue""=""Background""}
Pass {Cull Off ZWrite Off
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
V Vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.uv=i.uv;return o;}
float h(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float n(float2 p){float2 q=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(h(q),h(q+float2(1,0)),f.x),lerp(h(q+float2(0,1)),h(q+1),f.x),f.y);}
half4 Frag(V i):SV_Target{
float2 uv=i.uv;float y=uv.y-(.65-.42*uv.x);float dust=n(uv*25)+.5*n(uv*65)+.25*n(uv*180);
float band=exp(-y*y*180)*saturate(dust*.8-.15);float r=(y+.013*(n(uv*37)-.5))*85;float rift=exp(-r*r)*.68;
float3 c=lerp(float3(.006,.016,.052),float3(.022,.058,.14),saturate(1-uv.y));
c+=band*(1-rift)*lerp(float3(.09,.13,.36),float3(.20,.36,.57),n(uv*39));
float star=pow(saturate(n(uv*2200)),85)*.9;c+=star*float3(.72,.85,1);
return half4(c,1);}
ENDHLSL
}}}";
    }
}
