using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CityLife.World.Editor
{
    public static class NpcPreviewStage
    {
        public static void Configure(CharacterPreviewActor actor, Camera camera, string folder)
        {
            foreach (string name in new[] { "Source plinth", "Destination plinth", "Carryable crystal",
                "Source interaction stand", "Destination interaction stand", "Destination crystal socket" })
            { var o = GameObject.Find(name); if (o != null) Object.DestroyImmediate(o); }
            Object.DestroyImmediate(actor.Roamer); actor.Roamer = null;
            Object.DestroyImmediate(camera.GetComponent<CharacterPreviewSmoke>());
            actor.ExternalDrive = true; actor.View.Yaw = 140; actor.View.Pitch = 28; actor.View.Distance = 7;
            camera.fieldOfView = 60;
            var brain = actor.gameObject.AddComponent<NpcAutonomy>(); brain.Actor = actor;
            brain.Perception = actor.gameObject.AddComponent<NpcPerception>();
            brain.Log = actor.gameObject.AddComponent<NpcDecisionLog>();
            Material Material(string name, Color color)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", .2f); AssetDatabase.CreateAsset(m, folder + "/" + name + ".mat"); return m;
            }
            var stone = Material("NpcPlinths", new Color(.4f, .36f, .29f));
            var amber = Material("AmberCargo", new Color(.93f, .51f, .13f));
            var blue = Material("BlueCargo", new Color(.12f, .72f, .9f));
            var green = Material("GreenCargo", new Color(.3f, .85f, .53f));
            var denied = Material("DeniedCargo", new Color(.85f, .18f, .24f));
            Transform Marker(string name, Vector3 position) { var o = new GameObject(name); o.transform.position = position; return o.transform; }
            NpcInteractable Item(string id, Vector3 position, Material material, bool allowed = true)
            {
                var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder); plinth.name = id + " plinth"; plinth.layer = 8;
                plinth.transform.position = new Vector3(position.x, .43f, position.z); plinth.transform.localScale = new Vector3(.8f, .43f, .8f);
                plinth.GetComponent<Renderer>().sharedMaterial = stone;
                var cargo = GameObject.CreatePrimitive(PrimitiveType.Sphere); cargo.name = id; cargo.layer = 11;
                cargo.transform.position = new Vector3(position.x, 1.06f, position.z); cargo.transform.localScale = new Vector3(.24f, .38f, .24f);
                cargo.GetComponent<Renderer>().sharedMaterial = material; cargo.GetComponent<Collider>().isTrigger = true;
                var item = cargo.AddComponent<NpcInteractable>(); item.StableId = id; item.Permission = allowed;
                item.Approach = Marker(id + " approach", new Vector3(position.x, 0, position.z - 1));
                return item;
            }
            NpcInteractable Destination(string id, Vector3 position)
            {
                var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder); plinth.name = id; plinth.layer = 8;
                plinth.transform.position = new Vector3(position.x, .43f, position.z); plinth.transform.localScale = new Vector3(.85f, .43f, .85f);
                plinth.GetComponent<Renderer>().sharedMaterial = stone;
                var target = new GameObject(id + " sensor"); target.layer = 11; target.transform.position = plinth.transform.position;
                var sensor = target.AddComponent<SphereCollider>(); sensor.isTrigger = true; sensor.center = new Vector3(0, .65f, 0); sensor.radius = .35f;
                var item = target.AddComponent<NpcInteractable>(); item.StableId = id; item.Kind = NpcObjectKind.Destination;
                item.Approach = Marker(id + " approach", new Vector3(position.x, 0, position.z - 1));
                item.Socket = Marker(id + " socket", new Vector3(position.x, 1.06f, position.z));
                return item;
            }
            brain.Registry = new[] {
                Item("amber", new Vector3(-3, 0, -1), amber),
                Item("blue", new Vector3(4, 0, 1), blue),
                Item("hidden-green", new Vector3(0, 0, 3), green),
                Item("reserved-red", new Vector3(-1, 0, -3), denied, false),
                Destination("depot-west", new Vector3(-6, 0, 3)),
                Destination("depot-east", new Vector3(7, 0, 4)),
                Destination("depot-north", new Vector3(0, 0, 7))
            };
            var hud = camera.gameObject.AddComponent<NpcDecisionHud>(); hud.Brain = brain; hud.View = camera;
            var display = camera.gameObject.AddComponent<PreviewDisplayMode>(); display.MenuOnly = true;
            var controls = camera.gameObject.AddComponent<NpcPlayerControls>();
            controls.Brain = brain; controls.Hud = hud; controls.View = actor.View; controls.Display = display; hud.Controls = controls;
            var smoke = camera.gameObject.AddComponent<NpcPreviewSmoke>(); smoke.Brain = brain; smoke.Hud = hud; smoke.View = actor.View;
            smoke.Controls = controls;
            var light = GameObject.Find("Warm starlight").GetComponent<Light>(); light.shadowBias = .04f; light.shadowNormalBias = .1f;
            Time.fixedDeltaTime = NpcAutonomy.StepSeconds;
            Directory.CreateDirectory("evidence/local/npc");
        }
    }
}
