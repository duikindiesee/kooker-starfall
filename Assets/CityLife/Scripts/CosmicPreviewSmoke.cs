using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CityLife.World
{
    /// <summary>
    /// Opt-in verification of this process's baked player scene. No native input, focus, cursor,
    /// screenshots of the desktop, save data or settings are used. Inert without -previewSmoke.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class CosmicPreviewSmoke : MonoBehaviour
    {
        public static readonly bool Requested = Array.IndexOf(Environment.GetCommandLineArgs(), "-previewSmoke") >= 0;
        private static readonly List<string> runtimeErrors = new List<string>();
        private static int runtimeErrorCount;
        private static bool listening;
        private const float EyeHeight = 1.85f, Radius = .28f, StepSeconds = .05f;
        private const long ExpectedVertices = 3745751, ExpectedTriangles = 6550207;
        private CosmicPreviewExplorer explorer;
        private Camera view;
        private RenderTexture target;
        private UniversalRenderPipeline.SingleCameraRequest request;
        private Report report;
        private string directory, prefix;
        private bool finished, requestReady;
        private float began;
        private GameObject mainTree;
        private MeshCollider trunk;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ObserveStartupErrors()
        {
            if (!Requested || listening) return;
            runtimeErrors.Clear(); runtimeErrorCount = 0;
            Application.logMessageReceived += ObserveError;
            listening = true;
        }

        private static void ObserveError(string condition, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            runtimeErrorCount++;
            if (runtimeErrors.Count < 12) runtimeErrors.Add(type + ": " + condition);
        }

        private void Awake()
        {
            if (!Requested) { enabled = false; return; }
            ObserveStartupErrors();
            began = Time.realtimeSinceStartup;
            report = new Report { utc = DateTime.UtcNow.ToString("O"), version = Application.version,
                unityVersion = Application.unityVersion, scene = gameObject.scene.name };
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-previewEvidence");
                if (at < 0 || at + 1 >= args.Length || !Path.IsPathFullyQualified(args[at + 1]))
                    throw new InvalidOperationException("Smoke requires -previewEvidence followed by an absolute output directory.");
                directory = Path.GetFullPath(args[at + 1]);
                Directory.CreateDirectory(directory);
                prefix = "preview-smoke-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                explorer = GetComponent<CosmicPreviewExplorer>();
                view = GetComponent<Camera>();
                Require("camera-and-controller", view != null && explorer != null, "Smoke must be attached to the baked scene camera with its controller.");
                Require("camera-is-main", view.CompareTag("MainCamera"), "Actual baked main camera is used.");
                Require("camera-enabled", view.enabled && view.cullingMask != 0, "Baked camera is enabled with a nonempty culling mask.");
                report.initialCameraPosition = view.transform.position;
                report.initialCameraRotation = view.transform.eulerAngles;
                report.cameraFieldOfView = view.fieldOfView;
                report.cameraNearClip = view.nearClipPlane;
                report.cameraFarClip = view.farClipPlane;
                target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { name = "Starfall smoke offscreen target", antiAliasing = 1, useMipMap = false, autoGenerateMips = false };
                Require("render-target-created", target.Create(), "1600 x 900 ARGB32, depth 24.");
                // Initial normal camera frames initialize URP into this target, never the desktop.
                // Once URP accepts requests, only the explicit single-camera request renders.
                view.targetTexture = target;
                view.aspect = target.width / (float)target.height;
                request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            }
            catch (Exception error) { Finish(error); }
        }

        private IEnumerator Start()
        {
            if (!Requested || finished) yield break;
            IEnumerator routine = Verify();
            while (!finished)
            {
                object next = null; bool more = false; Exception failure = null;
                try
                {
                    if (runtimeErrorCount != 0) throw new InvalidOperationException("Runtime error/exception observed; see runtimeErrors.");
                    if (Time.realtimeSinceStartup - began > 180f) throw new TimeoutException("Bounded player smoke exceeded 180 seconds.");
                    more = routine.MoveNext();
                    if (more) next = routine.Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null) { Finish(failure); yield break; }
                if (!more) { Finish(null); yield break; }
                yield return next;
            }
        }

        private IEnumerator Verify()
        {
            while (!explorer.SmokeReady) yield return null;
            for (int frame = 0; frame < 60 && !RenderPipeline.SupportsRenderRequest(view, request); frame++) yield return null;
            Require("urp-single-camera-request", RenderPipeline.SupportsRenderRequest(view, request), "URP request support after offscreen warmup.");
            view.enabled = false;
            view.targetTexture = null;
            requestReady = true;
            Physics.SyncTransforms();
            InspectScene();
            Require("startup-walk", explorer.CurrentMode == "Walk", "Controller starts in walk mode over the ground collider.");
            CheckClearance("startup-clearance");
            Sample("startup", "loaded-scene", Vector3.zero, 0);
            Capture("01-startup");

            // Setup is recorded separately from movement. Find a level, unobstructed 2 m walk.
            Vector3 walkStart = Vector3.zero, walkEnd = Vector3.zero;
            bool walkFound = FindWalk(out walkStart, out walkEnd);
            Require("clear-walk-fixture", walkFound, "Ground and independent capsule casts establish a clear walking segment.");
            Place("walk", walkStart, walkEnd);
            Vector3 before = explorer.CameraPosition;
            for (int i = 0; i < 10; i++) { Drive("walk", Vector3.forward); yield return null; }
            float walked = Vector3.Distance(before, explorer.CameraPosition);
            Require("walk-movement", walked > 1.4f && walked < 2.05f, "Displacement metres=" + walked.ToString("R", CultureInfo.InvariantCulture));
            CheckClearance("walk-clearance");

            explorer.SmokeToggleMode(); Sample("flight", "same-F-transition", Vector3.zero, 0);
            Require("F-to-fly", explorer.CurrentMode == "Fly", "Uses the same ToggleMode as the real F input.");
            float initialY = explorer.CameraPosition.y;
            for (int i = 0; i < 8; i++) { Drive("flight-rise", Vector3.up); yield return null; }
            Require("fly-vertical-movement", explorer.CameraPosition.y - initialY > 3.5f, "Same fly Move/SweepSphere using E-equivalent direction.");
            explorer.SmokeAim(mainTree.transform.position + Vector3.up * 2.5f);
            Capture("02-flight");

            // All four actual stage limits are driven through Move and checked independently.
            Vector3[] starts = { new Vector3(26.5f, 30, 0), new Vector3(-26.5f, 30, 0),
                new Vector3(0, 30, 34.5f), new Vector3(0, 30, -18.5f) };
            Vector3[] directions = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            for (int side = 0; side < starts.Length; side++)
            {
                Place("bound-" + side, starts[side], starts[side] + directions[side]);
                for (int i = 0; i < 3; i++) { Drive("bound-" + side, Vector3.forward); yield return null; }
                Vector3 p = explorer.CameraPosition;
                float actual = side < 2 ? p.x : p.z;
                float wanted = side == 0 ? 27 : side == 1 ? -27 : side == 2 ? 35 : -19;
                Require("stage-bound-" + side, Mathf.Abs(actual - wanted) < .002f, "Actual=" + actual + "; expected=" + wanted);
            }
            Place("return-to-walk", walkStart + Vector3.up * 4, walkEnd + Vector3.up * 4);
            explorer.SmokeToggleMode(); Sample("return-to-walk", "same-F-transition", Vector3.zero, 0);
            Require("F-to-walk", explorer.CurrentMode == "Walk", "Same F transition snaps to measured ground.");
            CheckClearance("return-walk-clearance");

            Vector3 approach, aim; RaycastHit predicted;
            Require("trunk-approach-fixture", FindTrunkApproach(out approach, out aim, out predicted),
                "Starts with clear capsule; independent sweep must first hit the main tree Bark collider toward its actual bounds centre.");
            report.predictedContact = new Contact { collider = Hierarchy(predicted.collider.transform), distance = predicted.distance,
                point = predicted.point, normal = predicted.normal, start = approach, aim = aim };
            Place("trunk-approach", approach, aim);
            before = explorer.CameraPosition;
            int steps = Mathf.CeilToInt((predicted.distance + 1.5f) / (explorer.WalkSpeed * StepSeconds));
            bool hitTrunk = false; Vector3 tail = before;
            for (int i = 0; i < steps; i++)
            {
                if (i == steps - 4) tail = explorer.CameraPosition;
                Drive("trunk-approach", Vector3.forward);
                hitTrunk |= explorer.SmokeLastSweepCollider == trunk;
                yield return null;
            }
            float advanced = Vector3.Distance(before, explorer.CameraPosition);
            Require("main-trunk-collision", hitTrunk && advanced > .5f && Vector3.Distance(tail, explorer.CameraPosition) < .01f,
                "Actual main Bark sweep hit=" + hitTrunk + "; advanced metres=" + advanced + "; final four-step movement=" + Vector3.Distance(tail, explorer.CameraPosition));
            Require("trunk-no-capsule-overlap", BodyClear(explorer.CameraPosition, Radius - .01f), "Reduced-radius independent overlap query after blocked walk.");
            CheckClearance("trunk-ground-clearance");
            Capture("03-trunk-contact");
            Require("three-valid-captures", report.captures.Count == 3, "Actual built-scene offscreen camera captures; no desktop or HUD evidence.");
            yield return null;
        }

        private void InspectScene()
        {
            mainTree = GameObject.Find("Mature inspection tree");
            Require("R19-main-root", mainTree != null, "Mature inspection tree must exist in the baked scene.");
            foreach (MeshFilter filter in mainTree.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                long triangles = 0;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Require("main-mesh-triangles-" + filter.name + "-" + s, mesh.GetTopology(s) == MeshTopology.Triangles, "Main mesh topology.");
                    triangles += (long)mesh.GetIndexCount(s) / 3;
                }
                report.meshes.Add(new MeshRecord { path = Hierarchy(filter.transform), mesh = mesh.name, vertices = mesh.vertexCount,
                    triangles = triangles, subMeshes = mesh.subMeshCount, localBoundsCentre = mesh.bounds.center, localBoundsSize = mesh.bounds.size });
                report.mainVertices += mesh.vertexCount; report.mainTriangles += triangles;
                if (filter.name == "Bark") trunk = filter.GetComponent<MeshCollider>();
            }
            Require("R19-main-geometry", report.mainVertices == ExpectedVertices && report.mainTriangles == ExpectedTriangles,
                "Expected vertices/triangles=" + ExpectedVertices + "/" + ExpectedTriangles + "; actual=" + report.mainVertices + "/" + report.mainTriangles);
            Require("main-trunk-mesh-collider", trunk != null && trunk.enabled && !trunk.convex && trunk.sharedMesh != null,
                "Static non-convex collider on the actual main Bark mesh.");
            if (trunk != null) { report.trunkBoundsCentre = trunk.bounds.center; report.trunkBoundsSize = trunk.bounds.size; }
            bool ph02 = false;
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>())
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Require("material-present-" + renderer.name, material != null && material.shader != null, "Baked renderer material and shader.");
                    string shader = material.shader.name;
                    Require("shader-supported-" + renderer.name, material.shader.isSupported && shader != "Hidden/InternalErrorShader", shader);
                    if (!renderer.transform.IsChildOf(mainTree.transform)) continue;
                    bool tint = material.HasProperty("_FoliageTintStrength");
                    float strength = tint ? material.GetFloat("_FoliageTintStrength") : -1;
                    report.materials.Add(new MaterialRecord { renderer = Hierarchy(renderer.transform), material = material.name, shader = shader,
                        supported = material.shader.isSupported, hasFoliageTint = tint, foliageTint = strength });
                    if (shader == "CityLife/PH02FittedSupport")
                    {
                        ph02 = true;
                        Require("R19-PH02-tint", tint && Mathf.Abs(strength - 1) < .0001f, "Expected _FoliageTintStrength=1; actual=" + strength);
                    }
                }
            }
            Require("R19-PH02-material", ph02, "The actual main crown uses the PH02 fitted shader.");
        }

        private bool FindWalk(out Vector3 start, out Vector3 end)
        {
            Vector3[] candidates = { explorer.CameraPosition, new Vector3(-14, 2, -8), new Vector3(14, 2, -8),
                new Vector3(-18, 2, 15), new Vector3(18, 2, 15), new Vector3(0, 2, -14) };
            foreach (Vector3 candidate in candidates)
            {
                if (!Grounded(candidate, out Vector3 a) || !BodyClear(a)) continue;
                for (int direction = 0; direction < 8; direction++)
                {
                    Vector3 d = Quaternion.Euler(0, direction * 45, 0) * Vector3.forward;
                    if (!Grounded(a + d * 2, out Vector3 b) || !Inside(b) || !BodyClear(b) || Mathf.Abs(b.y - a.y) > .1f) continue;
                    bool level = true;
                    for (int s = 1; s <= 8; s++)
                        if (!Grounded(Vector3.Lerp(a, b, s / 8f), out Vector3 p) || Mathf.Abs(p.y - Mathf.Lerp(a.y, b.y, s / 8f)) > .08f) { level = false; break; }
                    if (!level || BodyCast(a, (b - a).normalized, Vector3.Distance(a, b) + .03f, out _)) continue;
                    start = a; end = b; return true;
                }
            }
            start = end = Vector3.zero; return false;
        }

        private bool FindTrunkApproach(out Vector3 start, out Vector3 aim, out RaycastHit hit)
        {
            Vector3 centre = trunk.bounds.center;
            float outer = Mathf.Max(trunk.bounds.extents.x, trunk.bounds.extents.z) + 1.6f;
            for (int side = 0; side < 16; side++)
            {
                Vector3 radial = Quaternion.Euler(0, side * 22.5f, 0) * Vector3.forward;
                if (!Grounded(centre + radial * outer, out Vector3 p) || !Inside(p) || !BodyClear(p)) continue;
                Vector3 targetPoint = new Vector3(centre.x, p.y, centre.z);
                Vector3 delta = targetPoint - p;
                if (!BodyCast(p, delta.normalized, delta.magnitude + .5f, out RaycastHit contact) || contact.collider != trunk || contact.distance < .8f) continue;
                bool walkable = true;
                float lastY = p.y;
                for (float distance = .2f; distance < contact.distance; distance += .2f)
                {
                    if (!Grounded(p + delta.normalized * distance, out Vector3 g) || Mathf.Abs(g.y - lastY) > .10f) { walkable = false; break; }
                    lastY = g.y;
                }
                if (!walkable) continue;
                start = p; aim = targetPoint; hit = contact; return true;
            }
            start = aim = Vector3.zero; hit = default; return false;
        }

        private bool Grounded(Vector3 point, out Vector3 result)
        {
            bool valid = explorer.SmokeGround(point, out RaycastHit hit) && hit.normal.y >= .64f;
            result = new Vector3(point.x, hit.point.y + EyeHeight, point.z);
            return valid;
        }
        private bool BodyClear(Vector3 eye, float radius = Radius) => !Physics.CheckCapsule(
            eye - Vector3.up * (EyeHeight - Radius - .055f), eye - Vector3.up * .25f, radius, explorer.CollisionMask, QueryTriggerInteraction.Ignore);
        private bool BodyCast(Vector3 eye, Vector3 direction, float distance, out RaycastHit hit) => Physics.CapsuleCast(
            eye - Vector3.up * (EyeHeight - Radius - .055f), eye - Vector3.up * .25f, Radius, direction,
            out hit, distance, explorer.CollisionMask, QueryTriggerInteraction.Ignore);
        private static bool Inside(Vector3 p) => p.x >= -27.002f && p.x <= 27.002f && p.z >= -19.002f && p.z <= 35.002f;

        private void Place(string phase, Vector3 position, Vector3 aim)
        {
            explorer.Camera.transform.position = position;
            explorer.SmokeAim(aim);
            Physics.SyncTransforms();
            Sample(phase, "explicit-scenario-setup-not-movement", Vector3.zero, 0);
        }
        private void Drive(string phase, Vector3 input)
        {
            explorer.SmokeStep(input, false, StepSeconds);
            Require("path-in-stage", Inside(explorer.CameraPosition), phase);
            Sample(phase, "same-controller-Move", input, StepSeconds);
        }
        private void Sample(string phase, string action, Vector3 input, float dt)
        {
            bool ground = explorer.SmokeGround(explorer.CameraPosition, out RaycastHit hit);
            report.path.Add(new PathSample { phase = phase, action = action, mode = explorer.CurrentMode,
                elapsedSeconds = Time.realtimeSinceStartup - began, inputSeconds = dt, position = explorer.CameraPosition,
                rotation = view.transform.eulerAngles, input = input, travelledMetres = explorer.TravelledMetres,
                groundFound = ground, groundClearance = ground ? explorer.CameraPosition.y - hit.point.y : -1,
                actualSweepCollider = action != "same-controller-Move" || explorer.SmokeLastSweepCollider == null ? "" : Hierarchy(explorer.SmokeLastSweepCollider.transform) });
        }
        private void CheckClearance(string name)
        {
            bool found = explorer.SmokeGround(explorer.CameraPosition, out RaycastHit hit);
            float clearance = explorer.CameraPosition.y - hit.point.y;
            Require(name, found && Mathf.Abs(clearance - EyeHeight) < .035f, "Ground clearance metres=" + clearance);
        }

        private void Capture(string label)
        {
            if (!requestReady) throw new InvalidOperationException("Offscreen request has not initialized.");
            RenderPipeline.SubmitRenderRequest(view, request);
            Texture2D pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture old = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                pixels.Apply(false, false);
                Color32[] data = pixels.GetPixels32();
                float min = 1, max = 0; int magenta = 0;
                foreach (Color32 c in data)
                {
                    float luminance = (.2126f * c.r + .7152f * c.g + .0722f * c.b) / 255;
                    min = Mathf.Min(min, luminance); max = Mathf.Max(max, luminance);
                    if (c.r > 210 && c.b > 210 && c.g < 45) magenta++;
                }
                string file = prefix + "-" + label + ".png";
                using (FileStream stream = new FileStream(Path.Combine(directory, file), FileMode.CreateNew, FileAccess.Write))
                { byte[] bytes = pixels.EncodeToPNG(); stream.Write(bytes, 0, bytes.Length); }
                bool valid = max > .06f && max - min > .035f && magenta / (float)data.Length < .005f;
                report.captures.Add(new CaptureRecord { file = file, width = target.width, height = target.height, position = view.transform.position,
                    rotation = view.transform.eulerAngles, valid = valid, minimumLuminance = min, maximumLuminance = max,
                    magentaPixelFraction = magenta / (float)data.Length });
                Require("capture-" + label, valid, "GPU readback is nonuniform, nonblack and not dominated by error-shader magenta; PNG retained even on failure.");
            }
            finally { RenderTexture.active = old; Destroy(pixels); }
        }

        private void Require(string name, bool pass, string detail)
        {
            report.checks.Add(new Check { name = name, pass = pass, detail = detail });
            if (!pass) throw new InvalidOperationException(name + ": " + detail);
        }
        private void Finish(Exception failure)
        {
            if (finished) return;
            finished = true;
            report.elapsedSeconds = Time.realtimeSinceStartup - began;
            report.runtimeErrorCount = runtimeErrorCount;
            report.runtimeErrors = runtimeErrors.ToArray();
            report.status = failure == null && runtimeErrorCount == 0 ? "PASS" : "FAIL";
            report.failure = failure == null ? "" : failure.GetType().Name + ": " + failure.Message;
            try
            {
                if (directory != null)
                {
                    string path = Path.Combine(directory, (prefix ?? "preview-smoke-failed") + ".json");
                    using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write))) writer.Write(JsonUtility.ToJson(report, true));
                }
            }
            catch (Exception error) { report.status = "FAIL"; report.failure += "; report write failed: " + error.Message; }
            Debug.Log("COSMIC_PREVIEW_SMOKE_" + report.status + " " + report.failure);
            Application.Quit(report.status == "PASS" ? 0 : 3);
        }
        private static string Hierarchy(Transform item) => item.parent == null ? item.name : Hierarchy(item.parent) + "/" + item.name;
        private void OnDestroy()
        {
            if (!Requested) return;
            if (view != null && view.targetTexture == target) view.targetTexture = null;
            if (target != null) { target.Release(); Destroy(target); }
            if (listening) { Application.logMessageReceived -= ObserveError; listening = false; }
        }

        [Serializable] private sealed class Report
        {
            public string status = "RUNNING", utc, version, unityVersion, scene, failure;
            public string scope = "R19 baked standalone player: automatic controller-method verification and actual URP camera RenderTexture captures. No real keyboard/mouse, native window, HUD, user acceptance, offline/telemetry or FPS verification.";
            public string renderPath = "urp-single-camera-request-offscreen";
            public string rockCollision = "SKIPPED: rock collision is unimplemented in this bounded R19 preview; rocks are the next milestone.";
            public float elapsedSeconds;
            public float cameraFieldOfView, cameraNearClip, cameraFarClip;
            public Vector3 initialCameraPosition, initialCameraRotation;
            public long mainVertices, mainTriangles;
            public int runtimeErrorCount;
            public string[] runtimeErrors;
            public Vector3 trunkBoundsCentre, trunkBoundsSize;
            public Contact predictedContact;
            public List<Check> checks = new List<Check>();
            public List<MeshRecord> meshes = new List<MeshRecord>();
            public List<MaterialRecord> materials = new List<MaterialRecord>();
            public List<PathSample> path = new List<PathSample>();
            public List<CaptureRecord> captures = new List<CaptureRecord>();
        }
        [Serializable] private sealed class Check { public string name, detail; public bool pass; }
        [Serializable] private sealed class MeshRecord { public string path, mesh; public long vertices, triangles; public int subMeshes; public Vector3 localBoundsCentre, localBoundsSize; }
        [Serializable] private sealed class MaterialRecord { public string renderer, material, shader; public bool supported, hasFoliageTint; public float foliageTint; }
        [Serializable] private sealed class PathSample
        {
            public string phase, action, mode, actualSweepCollider; public float elapsedSeconds, inputSeconds, groundClearance;
            public double travelledMetres; public bool groundFound; public Vector3 position, rotation, input;
        }
        [Serializable] private sealed class CaptureRecord
        {
            public string file; public int width, height; public bool valid;
            public float minimumLuminance, maximumLuminance, magentaPixelFraction; public Vector3 position, rotation;
        }
        [Serializable] private sealed class Contact { public string collider; public float distance; public Vector3 start, aim, point, normal; }
    }
}
