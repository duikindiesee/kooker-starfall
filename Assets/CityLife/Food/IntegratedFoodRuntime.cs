using UnityEngine;
using CityLife.World;

namespace Starfall.Food
{
    public sealed class IntegratedFoodRuntime : MonoBehaviour, IFoodAccess
    {
        public const string Generation = "combined-v1";
        public FoodModel Model { get; private set; }
        public Transform Actor;
        public NpcAutonomy Brain;
        public NpcInteractable Berry, Spring;
        public MeshRenderer SpringWaterRenderer;
        public Starfall.Refuge.RefugeRuntime Refuge;
        public Vector3 BerryPosition, SpringPosition;
        [field: SerializeField] public float MinimumRockClearance { get; private set; }
        public int RockColliderCount { get; private set; }
        int shownFruitStock=-1;
        bool acceptanceAccess;
        void Awake() { EnsureModel(); }
        void EnsureModel()
        {
            if (Model == null)
            {
                Model = new FoodModel(Brain != null ? Brain.InstanceWorldId : NpcTerrainNavigation.RegionId, Generation, 4242);
                if (Brain != null) Model.State.actorId = NpcAutonomy.AgentId;
            }
        }

        public void Attach(Transform actor, Transform worldRoot, string worldId)
        {
            Actor = actor; Model = new FoodModel(worldId, Generation, 4242);
            Model.State.actorId=NpcAutonomy.AgentId;
            Physics.SyncTransforms();
            // Resource sites sit away from the hero rock bank and on dry, sampled terrain.
            // The readable berry site shares the broad activity shelf but stays
            // outside the delivery fixture, rather than hiding in a mesa wall.
            BerryPosition = new Vector3(126, CoastalTerrain.Height(126, -80), -80);
            // A maintained freshwater seep occupies a separate dry rocky-foot
            // shelf north of the activity bank. It is outside initial 12 m
            // perception; no model prompt receives this authored coordinate.
            SpringPosition = FindDrySpringSite();
            Berry = BerryBush(BerryPosition, worldRoot, worldId);
            Spring = FreshwaterSeep(SpringPosition, worldRoot, worldId,out SpringWaterRenderer);
            Physics.SyncTransforms();
            MinimumRockClearance = MeasureRockClearance(BerryPosition);
            if (MinimumRockClearance < 3f) throw new System.InvalidOperationException("Integrated berry bush overlaps coastal rock geometry.");
            if (SpringPosition.y <= CoastalWater.Level + 1f || MeasureRockClearance(SpringPosition) < 2f)
                throw new System.InvalidOperationException("Freshwater seep requires a dry, clear discovered shelf.");
        }
        static Vector3 FindDrySpringSite()
        {
            // Choose a real, reproducible dry pocket near the authored rocky-foot
            // shelf. This is world generation, never model starting knowledge.
            // The old centre point's flattened sphere passed while its wider
            // visible rim crossed lower wet terrain; evaluate the full footprint.
            for(int shell=0;shell<=12;shell+=2)
            for(int dz=-shell;dz<=shell;dz+=2)
            for(int dx=-shell;dx<=shell;dx+=2)
            {
                if(Mathf.Max(Mathf.Abs(dx),Mathf.Abs(dz))!=shell)continue;
                float x=121+dx,z=-58+dz;
                if(new Vector2(x-126,z+80).magnitude<16f)continue;
                float centre=CoastalTerrain.Height(x,z),low=centre,high=centre;
                for(int i=0;i<16;i++)
                {
                    float a=i*Mathf.PI*2f/16f;
                    foreach(float radius in new[]{.66f,1.05f})
                    {
                        float y=CoastalTerrain.Height(x+Mathf.Cos(a)*radius,z+Mathf.Sin(a)*radius);
                        low=Mathf.Min(low,y);high=Mathf.Max(high,y);
                    }
                }
                if(low<=CoastalWater.Level+1f||high-low>.50f)continue;
                var site=new Vector3(x,centre,z);
                if(MeasureRockClearance(site)<2f)continue;
                return site;
            }
            throw new System.InvalidOperationException("No dry terrain-fitted freshwater seep pocket near rocky-foot shelf.");
        }
        static NpcInteractable BerryBush(Vector3 position, Transform parent, string worldId)
        {
            var root = new GameObject("Food / Starfall sourfig forage succulent"); root.transform.SetParent(parent); root.transform.position = position;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material Make(string name, Color colour) { var m = new Material(shader) { name = name, color = colour }; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour); return m; }
            var wood = Make("Sourfig warm woody base", new Color(.25f,.12f,.055f));
            var leaf = Make("Sourfig blue green matte fleshy leaves", new Color(.105f,.39f,.32f));
            var leafLight = Make("Sourfig sunlit matte fleshy leaves", new Color(.18f,.48f,.38f));
            foreach (var material in new[] { leaf, leafLight })
            {
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .16f);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
            }
            var fruit = Make("Sourfig purple red fruit", new Color(.62f,.035f,.19f));
            var flower = Make("Sourfig restrained pink flower", new Color(.92f,.20f,.48f));
            void Primitive(PrimitiveType type, string name, Vector3 localPosition, Vector3 scale, Quaternion rotation, Material material)
            {
                var part=GameObject.CreatePrimitive(type); part.name=name; part.transform.SetParent(root.transform,false);
                part.transform.localPosition=localPosition; part.transform.localScale=scale; part.transform.localRotation=rotation;
                part.GetComponent<Renderer>().sharedMaterial=material; var collider=part.GetComponent<Collider>();
                // This plant is authored while the Editor is saving the generated
                // player scene. Deferred Destroy can serialize hidden primitive
                // colliders and block the depot-east route beside the bush.
                if(collider!=null)
                {
                    collider.enabled=false;
                    if(Application.isPlaying) Destroy(collider);
                    else DestroyImmediate(collider);
                }
            }
            void Runner(string name, Vector3 from, Vector3 to)
            {
                Vector3 delta=to-from; Primitive(PrimitiveType.Cylinder,name,(from+to)*.5f,
                    new Vector3(.028f,delta.magnitude*.5f,.028f),Quaternion.FromToRotation(Vector3.up,delta),wood);
            }
            void FleshyLeaf(string name, Vector3 basePosition, float length, float width, float thickness,
                Quaternion rotation, Material material)
            {
                // An eight-sided, pointed sourfig blade replaces the glossy capsule placeholder.
                // It is rooted at the runner, broadens low, and tapers to a slightly lifted tip.
                var mesh = new Mesh { name = "Original tapered Starfall sourfig leaf" };
                var vertices = new[]
                {
                    new Vector3(0,0,-thickness*.30f),
                    new Vector3(-width*.72f,length*.30f,0), new Vector3(0,length*.27f,thickness), new Vector3(width*.72f,length*.30f,0),
                    new Vector3(-width,length*.62f,0), new Vector3(0,length*.58f,thickness*.82f), new Vector3(width,length*.62f,0),
                    new Vector3(0,length,thickness*.10f)
                };
                var triangles = new[]
                {
                    0,2,1, 0,3,2, 1,2,5, 1,5,4, 2,3,6, 2,6,5,
                    4,5,7, 5,6,7, 0,1,4, 0,4,7, 0,7,6, 0,6,3
                };
                mesh.vertices=vertices; mesh.triangles=triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var part=new GameObject(name); part.transform.SetParent(root.transform,false); part.transform.localPosition=basePosition;
                part.transform.localRotation=rotation; part.AddComponent<MeshFilter>().sharedMesh=mesh;
                part.AddComponent<MeshRenderer>().sharedMaterial=material;
            }
            for (int i=0;i<7;i++)
            {
                float angle=i*2.399963f+.22f, radius=.18f+(i%3)*.12f;
                Vector3 basePoint=new Vector3(Mathf.Cos(angle)*radius,.08f,Mathf.Sin(angle)*radius);
                // Sourfig spreads along the ground. Keep the woody runner connected and
                // mostly concealed rather than presenting a ring of upright brown sticks.
                Runner("Grounded concealed woody runner "+i,new Vector3(0,.055f,0),basePoint+Vector3.up*.035f);
                int leafCount=5+(i%2);
                for(int j=0;j<leafCount;j++)
                {
                    float spread=(j-(leafCount-1)*.5f)*.34f, leafAngle=angle+spread;
                    float reach=.36f+.07f*((i+j)%3), height=.31f+.10f*((i*2+j)%3);
                    Vector3 leafBase=basePoint+new Vector3(Mathf.Cos(leafAngle)*reach*.40f,height*.40f,Mathf.Sin(leafAngle)*reach*.40f);
                    Runner("Attached sourfig runner "+i+"-"+j,basePoint+Vector3.up*.20f,leafBase);
                    Quaternion leafRotation=Quaternion.LookRotation(new Vector3(Mathf.Cos(leafAngle),.22f,Mathf.Sin(leafAngle)),Vector3.up)*Quaternion.Euler(72,0,0);
                    FleshyLeaf("Tapered fleshy sourfig leaf "+i+"-"+j,leafBase,.42f+.07f*((i+j)%3),.115f,.055f,
                        leafRotation,(i+j)%4==0?leafLight:leaf);
                }
                if(i%2==0)
                {
                    Vector3 fruitPosition=basePoint+new Vector3(Mathf.Cos(angle)*.30f,.31f,Mathf.Sin(angle)*.30f);
                    Runner("Fruit-bearing stem "+i,basePoint+Vector3.up*.13f,fruitPosition-Vector3.up*.07f);
                    Primitive(PrimitiveType.Sphere,"Visible ripe sourfig fruit "+i,fruitPosition,new Vector3(.17f,.20f,.17f),Quaternion.identity,fruit);
                    Primitive(PrimitiveType.Sphere,"Sourfig fruit crown "+i,fruitPosition+Vector3.up*.105f,new Vector3(.11f,.045f,.11f),Quaternion.identity,leafLight);
                }
            }
            Vector3 flowerCenter=new Vector3(-.38f,.58f,.26f); Runner("Flower stem",new Vector3(-.20f,.20f,.12f),flowerCenter-Vector3.up*.04f);
            for(int petal=0;petal<7;petal++)
            {
                float a=petal*Mathf.PI*2/7; Primitive(PrimitiveType.Sphere,"Sourfig flower petal "+petal,
                    flowerCenter+new Vector3(Mathf.Cos(a)*.12f,0,Mathf.Sin(a)*.12f),new Vector3(.13f,.035f,.075f),Quaternion.Euler(0,-a*Mathf.Rad2Deg,0),flower);
            }
            Primitive(PrimitiveType.Sphere,"Sourfig flower centre",flowerCenter+Vector3.up*.018f,new Vector3(.08f,.035f,.08f),Quaternion.identity,fruit);
            root.layer=11;
            var sensor=root.AddComponent<SphereCollider>(); sensor.radius=1.30f; sensor.center=new Vector3(0,.55f,0); sensor.isTrigger=true;
            var item=root.AddComponent<NpcInteractable>(); item.StableId="berry-food"; item.WorldId=worldId; item.Kind=NpcObjectKind.Place;
            item.Approach=ApproachPoint(root.transform,position,1.55f);
            return item;
        }
        static float MeasureRockClearance(Vector3 position)
        {
            // Static non-convex mesh colliders support overlap queries but not
            // Collider.ClosestPoint. Expand a real physics sphere in 25 cm steps
            // and retain a conservative clearance immediately before first contact.
            for (float radius = .25f; radius <= 10f; radius += .25f)
            {
                foreach (var collider in Physics.OverlapSphere(position + Vector3.up, radius,
                    1 << 8, QueryTriggerInteraction.Ignore))
                    if (collider.name.StartsWith("Stratified shore rock")) return radius - .25f;
            }
            return 10f;
        }
        public float MeasureRuntimeRockClearance()
        {
            Physics.SyncTransforms(); RockColliderCount = 0;
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (collider.gameObject.layer == 8 && collider.name.StartsWith("Stratified shore rock")) RockColliderCount++;
            return MeasureRockClearance(BerryPosition);
        }
        public int CountDecorativePlantColliders()
        {
            if (Berry == null) return -1;
            int count = 0;
            foreach (var collider in Berry.GetComponentsInChildren<Collider>(true))
                if (collider.gameObject != Berry.gameObject) count++;
            return count;
        }
        public void SyncFruitVisual()
        {
            if(Berry==null||Model==null||shownFruitStock==Model.State.fruitStock)return;
            shownFruitStock=Model.State.fruitStock;
            // Four original fruit clusters depict two finite harvest units;
            // a depleted plant retains its foliage but no ripe fruit. This
            // visual follows the authoritative model, never predicts stock.
            for(int visual=0;visual<4;visual++)
            {
                int named=visual*2;
                bool ripe=visual<Model.State.fruitStock*2;
                var berry=Berry.transform.Find("Visible ripe sourfig fruit "+named);
                var crown=Berry.transform.Find("Sourfig fruit crown "+named);
                if(berry!=null)berry.gameObject.SetActive(ripe);
                if(crown!=null)crown.gameObject.SetActive(ripe);
            }
        }
        static NpcInteractable FreshwaterSeep(Vector3 position, Transform parent, string worldId,
            out MeshRenderer waterRenderer)
        {
            const int sectors=16;
            const float outerRadius=1.05f, waterRadius=.66f;
            var target=new GameObject("Food / terrain-fitted freshwater seep");
            target.transform.SetParent(parent); target.transform.position=position; target.layer=11;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material Make(string name,Color colour,float smoothness)
            {
                var m=new Material(shader){name=name,color=colour};
                if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",colour);
                if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",smoothness);
                if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",0);
                return m;
            }
            var stone=Make("Seep fractured dark stone",new Color(.17f,.19f,.18f),.11f);
            var water=Make("Seep clear shallow freshwater",new Color(.12f,.48f,.54f),.56f);
            // Each bottom vertex follows the actual terrain footprint. The rim is
            // a shallow fractured rock pocket, not a hovering flattened sphere.
            var bottom=new Vector3[sectors]; var rim=new Vector3[sectors];
            var wet=new Vector3[sectors];
            float lowest=0,highest=0;
            for(int i=0;i<sectors;i++)
            {
                float a=i*Mathf.PI*2f/sectors;
                float radius=outerRadius*(.91f+.07f*Mathf.Sin(i*3.71f));
                float x=Mathf.Cos(a)*radius,z=Mathf.Sin(a)*radius;
                float ground=CoastalTerrain.Height(position.x+x,position.z+z)-position.y;
                lowest=Mathf.Min(lowest,ground);highest=Mathf.Max(highest,ground);
                bottom[i]=new Vector3(x,ground-.045f,z);
                float innerGround=CoastalTerrain.Height(position.x+Mathf.Cos(a)*waterRadius,position.z+Mathf.Sin(a)*waterRadius)-position.y;
                highest=Mathf.Max(highest,innerGround);
                wet[i]=new Vector3(Mathf.Cos(a)*waterRadius,0,Mathf.Sin(a)*waterRadius);
            }
            if(highest-lowest>.62f || position.y+lowest<=CoastalWater.Level+1f)
                throw new System.InvalidOperationException("Freshwater seep footprint requires a dry walkable rock pocket.");
            // A level pool sits just above its highest sampled patch. The
            // fractured stone rim rises above it and its lower skirt embeds in
            // the real slope; a fitted dark basin hides any exposed terrain gap.
            float waterY=highest+.045f;
            for(int i=0;i<sectors;i++)
            {
                float a=i*Mathf.PI*2f/sectors;
                rim[i]=new Vector3(Mathf.Cos(a)*outerRadius*.83f,
                    Mathf.Max(bottom[i].y+.18f,waterY+.105f)+(i%4)*.025f,
                    Mathf.Sin(a)*outerRadius*.83f);
                wet[i].y=waterY;
            }
            var rockVertices=new Vector3[sectors*3+1];
            var rockTriangles=new int[sectors*12+sectors*3];
            rockVertices[sectors*3]=new Vector3(0,waterY-.075f,0);
            for(int i=0;i<sectors;i++)
            {
                int next=(i+1)%sectors;
                int b=i*3,n=next*3,t=i*12;
                rockVertices[b]=bottom[i];rockVertices[b+1]=rim[i];
                rockVertices[b+2]=new Vector3(wet[i].x,waterY-.075f,wet[i].z);
                rockTriangles[t]=b;rockTriangles[t+1]=n;rockTriangles[t+2]=b+1;
                rockTriangles[t+3]=b+1;rockTriangles[t+4]=n;rockTriangles[t+5]=n+1;
                rockTriangles[t+6]=b+1;rockTriangles[t+7]=n+1;rockTriangles[t+8]=b+2;
                rockTriangles[t+9]=b+2;rockTriangles[t+10]=n+1;rockTriangles[t+11]=n+2;
                int floor=sectors*12+i*3;
                rockTriangles[floor]=sectors*3;rockTriangles[floor+1]=n+2;rockTriangles[floor+2]=b+2;
            }
            MeshRenderer Visual(string name,Vector3[] vertices,int[] triangles,Material material)
            {
                var part=new GameObject(name);part.transform.SetParent(target.transform,false);
                var mesh=new Mesh{name=name};mesh.vertices=vertices;mesh.triangles=triangles;
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                part.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                return renderer;
            }
            Visual("Grounded fractured seep rock rim",rockVertices,rockTriangles,stone);
            var waterVertices=new Vector3[sectors+1];var waterTriangles=new int[sectors*3];
            waterVertices[0]=new Vector3(0,waterY,0);
            for(int i=0;i<sectors;i++)
            {
                waterVertices[i+1]=wet[i];
                int t=i*3;waterTriangles[t]=0;waterTriangles[t+1]=(i+1)%sectors+1;waterTriangles[t+2]=i+1;
            }
            waterRenderer=Visual("Terrain-following shallow seep water",waterVertices,waterTriangles,water);
            // The target's trigger is deliberately independent of visual meshes:
            // no hidden primitive collider can obstruct walking beside the seep.
            var trigger=target.AddComponent<SphereCollider>();trigger.radius=.94f;trigger.isTrigger=true;
            var item = target.AddComponent<NpcInteractable>(); item.StableId = "spring-food"; item.WorldId = worldId; item.Kind = NpcObjectKind.Place;
            item.Approach=ApproachPoint(target.transform,position,1.42f);
            return item;
        }
        static Transform ApproachPoint(Transform source,Vector3 position,float offset)
        {
            // A grounded stop outside the resource mesh prevents the body from
            // walking through fruit leaves or the spring's rock lip. It is only
            // exposed to the planner after a live scoped visual observation.
            var marker=new GameObject("Grounded observed-resource approach");
            marker.transform.SetParent(source,false);
            float x=position.x+offset,z=position.z;
            marker.transform.position=new Vector3(x,CoastalTerrain.Height(x,z),z);
            return marker.transform;
        }
        public FoodAccess Inspect(string target)
        {
            if (acceptanceAccess) return new FoodAccess { visible = true, inReach = true, permitted = true, verifiedFreshwater = target == "spring" };
            bool inventory = target == "inventory";
            if(Actor==null) return new FoodAccess();
            NpcInteractable item = target == "berry" ? Berry : target == "spring" ? Spring : null;
            if(!inventory && item==null) return new FoodAccess();
            bool visible=inventory;
            if(item!=null && item.isActiveAndEnabled && item.WorldId==Model.State.world && item.Permission)
            {
                Vector3 eye=Actor.position+Vector3.up*1.6f, ray=item.SightPoint-eye;
                visible=ray.magnitude<=12f && !Physics.Raycast(eye,ray.normalized,ray.magnitude,(1<<8)|(1<<10),QueryTriggerInteraction.Ignore);
            }
            return new FoodAccess { visible=visible,
                inReach=inventory || (visible && Vector3.Distance(Actor.position,item.transform.position)<2.2f),
                permitted=inventory || (item!=null && item.Permission),
                verifiedFreshwater=target=="spring" && visible && item==Spring };
        }
        public bool RunAcceptanceSequence(out string evidence)
        {
            EnsureModel();
            int request = 1; acceptanceAccess = true;
            try
            {
                var observeBerry = Model.Execute(Model.State.world, Generation, request++, FoodAction.Inspect, "berry", this);
                var gather = Model.Execute(Model.State.world, Generation, request++, FoodAction.Gather, "berry", this);
                var eat = Model.Execute(Model.State.world, Generation, request++, FoodAction.Eat, "inventory", this);
                var observeSpring = Model.Execute(Model.State.world, Generation, request++, FoodAction.Inspect, "spring", this);
                var drink = Model.Execute(Model.State.world, Generation, request++, FoodAction.Drink, "spring", this);
                evidence = observeBerry.code + "; " + gather.code + "; " + eat.code + "; " + observeSpring.code + "; " + drink.code;
                return observeBerry.success && gather.success && eat.success && observeSpring.success && drink.success && FoodModel.Valid(Model.State, Model.State.world, Generation);
            }
            finally { acceptanceAccess = false; }
        }
        void FixedUpdate() { EnsureModel(); if (Actor != null) { bool paused=Brain!=null&&Brain.MenuPaused;
            if(!paused)
            {
                Model.State.actorPosition=Actor.position;
                Model.State.body.active=Brain!=null && Brain.Actor!=null && Brain.Actor.ActualSpeed>.12f;
                Model.State.body.resting=Refuge!=null && Refuge.Resting && Refuge.Body!=null && Refuge.Body.transform==Actor;
                Model.State.body.sheltered=Refuge!=null && Refuge.GeometryVerified && Refuge.Sample(Actor.position+Vector3.up).RainMultiplier<.05f;
            }
            Model.FixedStep(paused);
            if(Brain!=null && Brain.Actor!=null)Brain.Actor.DeadPose=Model.State.body.dead;
            SyncFruitVisual(); } }
    }
}
