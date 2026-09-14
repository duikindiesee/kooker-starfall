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
        bool acceptanceAccess;
        void Awake() { EnsureModel(); }
        void EnsureModel()
        {
            if (Model == null) Model = new FoodModel(NpcTerrainNavigation.RegionId, Generation, 4242);
        }

        public void Attach(Transform actor, Transform worldRoot, string worldId)
        {
            Actor = actor; Model = new FoodModel(worldId, Generation, 4242);
            BerryPosition = new Vector3(8, CoastalTerrain.Height(8, -5) + .45f, -5);
            SpringPosition = new Vector3(-4, CoastalTerrain.Height(-4, 7) + .45f, 7);
            Berry = Target("Food / ripe berry bush", "berry-food", BerryPosition, worldRoot, new Color(.48f, .08f, .34f));
            Spring = Target("Food / maintained freshwater spring", "spring-food", SpringPosition, worldRoot, new Color(.05f, .72f, .86f));
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
