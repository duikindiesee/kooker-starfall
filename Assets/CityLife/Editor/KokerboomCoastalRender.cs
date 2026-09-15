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
        private static bool skySiteMode;
        private static bool filmSiteMode;
        private static bool seepSiteMode;
        private static Starfall.Food.IntegratedFoodRuntime seepStudy;
        public static void RenderCoastalSlice(){coastalMode=true;ph02FamilyMode=true;hybridMode=true;Run(false);}
        public static void RenderCoastalSkySites(){skySiteEvidence.Clear();coastalMode=true;skySiteMode=true;ph02FamilyMode=true;hybridMode=true;Run(false);}
        public static void RenderCoastalFilmSites(){filmSiteEvidence.Clear();coastalMode=true;filmSiteMode=true;ph02FamilyMode=true;hybridMode=true;Run(false);}
        public static void RenderCoastalSeepSite(){coastalMode=true;seepSiteMode=true;ph02FamilyMode=true;hybridMode=true;Run(false);}
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
                if(seepSiteMode)
                {
                    var adapter=new GameObject("Editor-only spring footprint study");adapter.transform.SetParent(coast.transform);
                    seepStudy=adapter.AddComponent<Starfall.Food.IntegratedFoodRuntime>();
                    seepStudy.Attach(adapter.transform,coast.transform,CoastalTerrain.DefinitionId);
                    VerifySeepFootprint(terrain,seepStudy);
                    subjects.Add(new Subject{Root=seepStudy.Spring.gameObject,Kind="editor-only-freshwater-seep-study"});
                }
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
            // The visual sea continues to z=2200; a shorter camera far plane exposes
            // the solid-color background as a false light-gray ocean horizon.
            var giant=GameObject.Find("Blue gas giant - procedural volumetric cloud bands");
            if(giant==null)throw new InvalidOperationException("Coastal giant missing.");
            IntegratedCelestial.PlaceDistantGiant(giant.transform,camera);
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

        private static void VerifySeepFootprint(GameObject terrain,Starfall.Food.IntegratedFoodRuntime food)
        {
            Physics.SyncTransforms();
            var collider=terrain.GetComponent<MeshCollider>();
            var renderer=food.SpringWaterRenderer;
            var water=renderer?.GetComponent<MeshFilter>()?.sharedMesh;
            var rim=food.Spring?.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            if(collider==null||water==null||rim==null)throw new InvalidOperationException("Editor seep footprint is missing exact terrain or visual mesh.");
            float Ground(Vector3 world)
            {
                if(!collider.Raycast(new Ray(new Vector3(world.x,1000,world.z),Vector3.down),out var hit,2000))
                    throw new InvalidOperationException("Seep footprint ray does not meet the terrain collider.");
                return hit.point.y;
            }
            var record=new SeepFootprintRecord{site=food.SpringPosition,waterVertices=water.vertexCount,waterTriangles=water.triangles.Length/3,rimVertices=rim.vertexCount};
            foreach(var vertex in water.vertices)
            {
                var world=renderer.transform.TransformPoint(vertex);
                float gap=world.y-Ground(world);
                record.minimumWaterVertexGap=Mathf.Min(record.minimumWaterVertexGap,gap);
                record.maximumWaterVertexGap=Mathf.Max(record.maximumWaterVertexGap,gap);
                if(gap<.012f||gap>.025f)throw new InvalidOperationException("Seep water vertex is not draped to actual collider: "+gap);
            }
            var triangles=water.triangles;
            var vertices=water.vertices;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var centre=renderer.transform.TransformPoint((vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3);
                float gap=centre.y-Ground(centre);
                record.minimumWaterTriangleCentroidGap=Mathf.Min(record.minimumWaterTriangleCentroidGap,gap);
                record.maximumWaterTriangleCentroidGap=Mathf.Max(record.maximumWaterTriangleCentroidGap,gap);
                if(gap<-.015f||gap>.055f)throw new InvalidOperationException("Seep water triangle bridges terrain: "+gap);
            }
            var rimFilter=food.Spring.GetComponentInChildren<MeshFilter>();
            for(int i=0;i<rim.vertexCount;i+=3)
            {
                var world=rimFilter.transform.TransformPoint(rim.vertices[i]);
                float embed=world.y-Ground(world);
                record.maximumOuterSkirtGap=Mathf.Max(record.maximumOuterSkirtGap,embed);
                if(embed>-.095f)throw new InvalidOperationException("Seep stone outer skirt is not embedded: "+embed);
            }
            record.status="PASS_AUTHORED_COLLIDER_FOOTPRINT_ONLY";
            File.WriteAllText(Path.Combine(outputDirectory,"freshwater-seep-grounding.json"),JsonUtility.ToJson(record,true));
        }
        [Serializable]private sealed class SeepFootprintRecord
        {
            public string status;
            public Vector3 site;
            public int waterVertices,waterTriangles,rimVertices;
            public float minimumWaterVertexGap=float.PositiveInfinity,maximumWaterVertexGap=float.NegativeInfinity;
            public float minimumWaterTriangleCentroidGap=float.PositiveInfinity,maximumWaterTriangleCentroidGap=float.NegativeInfinity;
            public float maximumOuterSkirtGap=float.NegativeInfinity;
            public string scope="Actual Editor solid terrain MeshCollider rays at every wet vertex and triangle centroid; ordinary actor-view and gameplay interaction require a compiled player.";
        }

        private static void CoastalCamera(Vector3 position,Vector3 aim)
        {
            Coastal();Perspective(position,aim,width*9/16);camera.fieldOfView=52;
        }
        private static void SeepSiteCamera(Vector3 offset)
        {
            Coastal();
            var site=seepStudy.SpringPosition;
            Perspective(site+offset,site+Vector3.up*.06f,width*9/16);
            camera.fieldOfView=52;
        }
        private static void SeepFollowPoseCamera(string label,Vector3 point,float yaw,float pitch)
        {
            Coastal();
            Vector3 pivot=point+Vector3.up*1.05f;
            Vector3 offset=Quaternion.Euler(pitch,yaw,0)*Vector3.back;
            bool occluded=Physics.SphereCast(pivot,.2f,offset,out var hit,4.8f,(1<<8)|(1<<10),QueryTriggerInteraction.Ignore);
            float distance=occluded?Mathf.Max(.3f,hit.distance-.08f):4.8f;
            Perspective(pivot+offset*distance,pivot,width*9/16);
            camera.fieldOfView=60;
            File.WriteAllText(Path.Combine(outputDirectory,"freshwater-seep-follow-pose-"+label+".json"),JsonUtility.ToJson(new SeepFollowRecord{
                approach=point,pivot=pivot,eye=camera.transform.position,actualDistance=distance,occluded=occluded,yaw=yaw,pitch=pitch
            },true));
        }
        [Serializable]private sealed class SeepFollowRecord
        {
            public Vector3 approach,pivot,eye;
            public float actualDistance,yaw,pitch;
            public bool occluded;
            public string scope="Editor pose computed with CharacterPreviewCamera.Follow equations (distance4.8,FOV60) and actual terrain/rock spherecast. Actor rig, HUD, input and ordinary compiled-camera acceptance absent.";
        }

        private static List<Shot> BuildCoastalShots()
        {
            if(seepSiteMode) return new List<Shot>{
                new Shot{Id="01-grounded-freshwater-seep-close",Purpose="Collider-draped shallow seep and stone rim close view; Editor geometry only, not ordinary actor camera or functional drink proof.",Height=width*9/16,Configure=()=>SeepSiteCamera(new Vector3(3,1.45f,-4.8f))},
                new Shot{Id="02-grounded-freshwater-seep-oblique",Purpose="Same authored site oblique footprint/terrain gap critique with no actor-model authority.",Height=width*9/16,Configure=()=>SeepSiteCamera(new Vector3(-3.2f,1.75f,-4.6f))},
                new Shot{Id="03-pose-matched-default-follow-spring",Purpose="Editor camera pose using unchanged follow defaults yaw155,pitch15 and actual spherecast at observed external spring approach; actor/HUD absent.",Height=width*9/16,Configure=()=>{Coastal();SeepFollowPoseCamera("default-spring",seepStudy.Spring.Approach.position,155,15);}},
                new Shot{Id="04-candidate-horizon-follow-spring",Purpose="Bounded proposed yaw0,pitch-5 follow framing at spring; actor/HUD absent, not gameplay setting or ordinary player proof.",Height=width*9/16,Configure=()=>{Coastal();SeepFollowPoseCamera("candidate-spring",seepStudy.Spring.Approach.position,0,-5);}},
                new Shot{Id="05-candidate-horizon-follow-berry",Purpose="Same proposed yaw0,pitch-5 framing at activity berry to test broader landscape composition before any gameplay camera setting.",Height=width*9/16,Configure=()=>{Coastal();SeepFollowPoseCamera("candidate-berry",seepStudy.Berry.Approach.position,0,-5);}}
            };
            if(skySiteMode) return BuildSkySiteShots();
            if(filmSiteMode) return BuildFilmSiteShots();
            return new List<Shot>{
            new Shot{Id="01-coastal-side-composition",Purpose="Fixed provisional side-view: frozen R19 tree on rocky bank, wrapping turquoise river toward deep sea, canyon mountains, blue gas giant and distant procedural galaxy. Actual Unity geometry; no artwork billboard.",Height=width*9/16,Configure=()=>CoastalCamera(new Vector3(21,4.6f,-25),new Vector3(0,3.4f,14))},
            new Shot{Id="02-water-to-sea",Purpose="Fixed surface material/depth comparison from the river toward open sea; no underwater-life or swimming claim.",Height=width*9/16,Configure=()=>CoastalCamera(new Vector3(10,-.1f,13),new Vector3(-2,-1,64))},
            new Shot{Id="03-rocky-tree-bank",Purpose="Fixed gameplay-distance rock bank, grounding and secondary succulent surface view; collision geometry exists but native input not yet verified.",Height=width*9/16,Configure=()=>CoastalCamera(new Vector3(13,1.9f,-12),new Vector3(2,-.4f,-3))},
            new Shot{Id="04-canyon-opening",Purpose="Fixed overview of layered canyon banks, river continuity and opening to sea. Element critique separate from combined scene.",Height=width*9/16,Configure=()=>CoastalCamera(new Vector3(-17,10,5),new Vector3(11,6,46))},
            new Shot{Id="05-shallow-bed-only-diagnostic",Purpose="Diagnostic actual opaque bed, shelves and planted geometry with only the water renderer disabled. Not a playable-water appearance or acceptance image.",Height=width*9/16,Configure=()=>{CoastalCamera(new Vector3(-11,3.2f,20),new Vector3(-2,-2.25f,34));coastalWater.SetActive(false);}},
            new Shot{Id="06-shallow-water-matched-diagnostic",Purpose="Exact camera matched to 05 with the authored water restored; establishes what the surface hides or transmits before further visual work.",Height=width*9/16,Configure=()=>{CoastalCamera(new Vector3(-11,3.2f,20),new Vector3(-2,-2.25f,34));coastalWater.SetActive(true);}},
            new Shot{Id="07-offshore-islands-sea-vista",Purpose="Traversable-boundary lookout toward three inaccessible render-only offshore landforms and the visual sea continuation. No boat or island navigation claim.",Height=width*9/16,Configure=()=>{CoastalCamera(new Vector3(0,32,520),new Vector3(-40,18,1250));camera.farClipPlane=2400;}}
            };
        }

        private static readonly List<string> skySiteEvidence = new List<string>();
        private static List<Shot> BuildSkySiteShots() => new List<Shot> {
            new Shot{Id="01-sky-dry-bank",Purpose="Diagnostic dry western bank at ordinary sea heading; posed Editor geometry, not autonomous traversal.",Height=width*9/16,Configure=()=>SkySiteCamera(-52,-55,"bank")},
            new Shot{Id="02-sky-forward-same-heading",Purpose="Same sea heading from separately dry forward site; parallax composition only, not route proof.",Height=width*9/16,Configure=()=>SkySiteCamera(-52,-5,"forward")},
            new Shot{Id="03-sky-dry-mouth",Purpose="Mouth site selected by actual collider dry freeboard, not the prior submerged bed.",Height=width*9/16,Configure=()=>SkySiteCamera(-38,5,"mouth")},
            new Shot{Id="04-sky-high-lookout",Purpose="Dry high lookout, same heading and 52-degree field of view.",Height=width*9/16,Configure=()=>SkySiteCamera(-90,110,"lookout")}
        };
        private static void SkySiteCamera(float proposedX,float z,string label)
        {
            Coastal();
            if(!CoastalSkyViewSites.TryDryEyeNear(proposedX,z,camera.nearClipPlane,out var eye,out var evidence))
                throw new InvalidOperationException("No matched dry sky site for "+label+": "+evidence);
            skySiteEvidence.Add(label+": "+evidence);
            File.WriteAllLines(Path.Combine(outputDirectory,"sky-site-selection.txt"),skySiteEvidence);
            CoastalCamera(eye,eye+new Vector3(.04f,.07f,1)*100f);
        }
        private static readonly List<string> filmSiteEvidence = new List<string>();
        private static List<Shot> BuildFilmSiteShots() => new List<Shot> {
            new Shot{Id="01-film-follow-yaw-0",Purpose="Diagnostic genuine follow-camera orbit at grounded actor proxy; not gameplay footage.",Height=width*9/16,Configure=()=>FilmSiteCamera(0,"yaw-0")},
            new Shot{Id="02-film-follow-yaw-minus-45",Purpose="Diagnostic genuine follow-camera orbit at grounded actor proxy; not gameplay footage.",Height=width*9/16,Configure=()=>FilmSiteCamera(-45,"yaw-minus-45")},
            new Shot{Id="03-film-follow-yaw-minus-80",Purpose="Diagnostic genuine follow-camera orbit at grounded actor proxy; not gameplay footage.",Height=width*9/16,Configure=()=>FilmSiteCamera(-80,"yaw-minus-80")}
        };
        private static void FilmSiteCamera(float yaw,string label)
        {
            CoastalCamera(new Vector3(128,7,-87),new Vector3(128,4,-80));
            Physics.SyncTransforms();
            const int solid=(1<<8)|(1<<10);
            if(!Physics.Raycast(new Vector3(128,300,-80),Vector3.down,out var floor,400,solid,QueryTriggerInteraction.Ignore) ||
                floor.point.y<=CoastalWater.Level+1f)throw new InvalidOperationException("Grounded follow proxy has no dry floor");
            var proxy=new GameObject("Editor grounded actor camera proxy; not rendered NPC");
            proxy.transform.position=floor.point+Vector3.up*.06f;
            var follow=camera.gameObject.AddComponent<CharacterPreviewCamera>();
            follow.Target=proxy.transform;follow.SuppressInput=true;follow.Yaw=yaw;follow.Pitch=12;follow.Distance=5.8f;
            follow.Follow();camera.fieldOfView=60;
            if(follow.ActualDistance<2.3f || Physics.CheckSphere(camera.transform.position,.2f,solid,QueryTriggerInteraction.Ignore) ||
               camera.transform.position.y<=CoastalWater.Level+1f)
            {
                filmSiteEvidence.Add(label+": rejected follow position="+camera.transform.position+" distance="+follow.ActualDistance+
                    " occluded="+follow.Occluded+" actorFloor="+floor.point);
                File.WriteAllLines(Path.Combine(outputDirectory,"film-site-selection.txt"),filmSiteEvidence);
                throw new InvalidOperationException("Follow film camera clipped or wet at "+label);
            }
            filmSiteEvidence.Add(label+": actualFollowCamera="+camera.transform.position+"; actorProxy="+proxy.transform.position+
                "; actorFloor="+floor.point+"; yaw="+yaw+"; pitch=12; distance="+follow.ActualDistance+
                "; occluded="+follow.Occluded+"; FOV=60; no ordinary actor or HUD in Editor shot");
            File.WriteAllLines(Path.Combine(outputDirectory,"film-site-selection.txt"),filmSiteEvidence);
        }

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
