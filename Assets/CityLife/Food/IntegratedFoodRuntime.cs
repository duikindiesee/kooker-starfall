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

        public void Attach(Transform actor, Transform worldRoot, string worldId)
        {
            Actor = actor; Model = new FoodModel(worldId, Generation, 4242);
            Berry = Target("Food / ripe berry bush", "berry-food", new Vector3(8, CoastalTerrain.Height(8, -5), -5), worldRoot, new Color(.48f, .08f, .34f));
            Spring = Target("Food / maintained freshwater spring", "spring-food", new Vector3(-4, CoastalTerrain.Height(-4, 7), 7), worldRoot, new Color(.05f, .72f, .86f));
        }
        static NpcInteractable Target(string name, string id, Vector3 position, Transform parent, Color colour)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere); target.name = name; target.transform.SetParent(parent);
            target.transform.position = position + Vector3.up * .45f;
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
            Transform subject = target == "berry" ? Berry.transform : target == "spring" ? Spring.transform : Actor;
            bool inventory = target == "inventory";
            return new FoodAccess { visible = true, inReach = inventory || Vector3.Distance(Actor.position, subject.position) < 2.2f,
                permitted = true, verifiedFreshwater = target == "spring" };
        }
        public bool RunAcceptanceSequence(out string evidence)
        {
            Vector3 original = Actor.position; int request = 1;
            Actor.position = Berry.transform.position; Model.State.actorPosition = Actor.position;
            var observeBerry = Model.Execute(Model.State.world, Generation, request++, FoodAction.Inspect, "berry", this);
            var gather = Model.Execute(Model.State.world, Generation, request++, FoodAction.Gather, "berry", this);
            var eat = Model.Execute(Model.State.world, Generation, request++, FoodAction.Eat, "inventory", this);
            Actor.position = Spring.transform.position; Model.State.actorPosition = Actor.position;
            var observeSpring = Model.Execute(Model.State.world, Generation, request++, FoodAction.Inspect, "spring", this);
            var drink = Model.Execute(Model.State.world, Generation, request++, FoodAction.Drink, "spring", this);
            Actor.position = original; Model.State.actorPosition = original;
            evidence = observeBerry.code + "; " + gather.code + "; " + eat.code + "; " + observeSpring.code + "; " + drink.code;
            return observeBerry.success && gather.success && eat.success && observeSpring.success && drink.success && FoodModel.Valid(Model.State, Model.State.world, Generation);
        }
        void FixedUpdate() { if (Model != null) { Model.State.actorPosition = Actor.position; Model.FixedStep(false); } }
    }
}
