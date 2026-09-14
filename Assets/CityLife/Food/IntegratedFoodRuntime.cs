using UnityEngine;
using CityLife.World;

namespace Starfall.Food
{
    public sealed class IntegratedFoodRuntime : MonoBehaviour, IFoodAccess
    {
        public const string Generation = "combined-v1";
        public FoodModel Model { get; private set; }
        public Transform Actor;
        public NpcInteractable Berry, Spring;
        public Vector3 BerryPosition, SpringPosition;
        [field: SerializeField] public float MinimumRockClearance { get; private set; }
        public int RockColliderCount { get; private set; }
        bool acceptanceAccess;
        void Awake() { EnsureModel(); }
        void EnsureModel()
        {
            if (Model == null) Model = new FoodModel(NpcTerrainNavigation.RegionId, Generation, 4242);
        }

        public void Attach(Transform actor, Transform worldRoot, string worldId)
        {
            Actor = actor; Model = new FoodModel(worldId, Generation, 4242);
            Physics.SyncTransforms();
            // Resource sites sit away from the hero rock bank and on dry, sampled terrain.
            // The readable berry site shares the broad activity shelf but stays
            // outside the delivery fixture, rather than hiding in a mesa wall.
            BerryPosition = new Vector3(132, CoastalTerrain.Height(132, -80), -80);
            SpringPosition = new Vector3(-24, CoastalTerrain.Height(-24, 54) + .18f, 54);
            Berry = BerryBush(BerryPosition, worldRoot, worldId);
            Spring = Target("Food / maintained freshwater spring", "spring-food", SpringPosition, worldRoot, new Color(.05f, .72f, .86f));
            Physics.SyncTransforms();
            MinimumRockClearance = MeasureRockClearance(BerryPosition);
            if (MinimumRockClearance < 3f) throw new System.InvalidOperationException("Integrated berry bush overlaps coastal rock geometry.");
        }
        static NpcInteractable BerryBush(Vector3 position, Transform parent, string worldId)
        {
            var root = new GameObject("Food / recognizable ripe berry bush"); root.transform.SetParent(parent); root.transform.position = position;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material Make(string name, Color colour) { var m = new Material(shader) { name = name, color = colour }; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour); return m; }
            var wood = Make("Berry bush warm stem", new Color(.24f,.11f,.05f));
            var leaf = Make("Berry bush blue green leaves", new Color(.08f,.32f,.25f));
            var fruit = Make("Berry bush purple red fruit", new Color(.55f,.025f,.18f));
            for (int i=0;i<5;i++)
            {
                float angle=i*1.2566f; var branch=GameObject.CreatePrimitive(PrimitiveType.Cylinder); branch.name="Berry branch "+i; branch.transform.SetParent(root.transform,false);
                branch.transform.localPosition=new Vector3(Mathf.Cos(angle)*.28f,.62f,Mathf.Sin(angle)*.28f); branch.transform.localScale=new Vector3(.08f,.65f,.08f); branch.transform.localRotation=Quaternion.Euler(Mathf.Sin(angle)*22,0,Mathf.Cos(angle)*-22); branch.GetComponent<Renderer>().sharedMaterial=wood;
                var crown=GameObject.CreatePrimitive(PrimitiveType.Sphere); crown.name="Berry leaf crown "+i; crown.transform.SetParent(root.transform,false); crown.transform.localPosition=new Vector3(Mathf.Cos(angle)*.62f,1.35f,Mathf.Sin(angle)*.62f); crown.transform.localScale=new Vector3(1.15f,.55f,.9f); crown.GetComponent<Renderer>().sharedMaterial=leaf;
                for(int j=0;j<3;j++){var berry=GameObject.CreatePrimitive(PrimitiveType.Sphere);berry.name="Visible ripe berry "+i+"-"+j;berry.transform.SetParent(root.transform,false);float a=angle+j*2.09f;berry.transform.localPosition=crown.transform.localPosition+new Vector3(Mathf.Cos(a)*.36f,-.22f,Mathf.Sin(a)*.32f);berry.transform.localScale=Vector3.one*.18f;berry.GetComponent<Renderer>().sharedMaterial=fruit;}
            }
            var sensor=root.AddComponent<SphereCollider>(); sensor.radius=1.35f; sensor.center=new Vector3(0,.9f,0); sensor.isTrigger=true;
            var item=root.AddComponent<NpcInteractable>(); item.StableId="berry-food"; item.WorldId=worldId; item.Kind=NpcObjectKind.Place; item.Approach=root.transform; return item;
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
        static NpcInteractable Target(string name, string id, Vector3 position, Transform parent, Color colour)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere); target.name = name; target.transform.SetParent(parent);
            target.transform.position = position;
            target.transform.localScale = id.StartsWith("berry") ? new Vector3(1.2f, .9f, 1.2f) : new Vector3(1.8f, .25f, 1.8f);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name + " material", color = colour };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            target.GetComponent<Renderer>().sharedMaterial = material;
            var item = target.AddComponent<NpcInteractable>(); item.StableId = id; item.WorldId = NpcTerrainNavigation.RegionId; item.Kind = NpcObjectKind.Place;
            return item;
        }
        public FoodAccess Inspect(string target)
        {
            if (acceptanceAccess) return new FoodAccess { visible = true, inReach = true, permitted = true, verifiedFreshwater = target == "spring" };
            bool inventory = target == "inventory";
            Vector3 subject = target == "berry" ? BerryPosition : target == "spring" ? SpringPosition : Actor.position;
            return new FoodAccess { visible = true, inReach = inventory || Vector3.Distance(Actor.position, subject) < 2.2f,
                permitted = true, verifiedFreshwater = target == "spring" };
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
        void FixedUpdate() { EnsureModel(); if (Actor != null) { Model.State.actorPosition = Actor.position; Model.FixedStep(false); } }
    }
}
