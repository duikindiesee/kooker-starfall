using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CityLife.World
{
    // Opt-in actual player verification, offscreen only. No injected desktop input.
    [DefaultExecutionOrder(-200)]
    public sealed class CharacterPreviewSmoke : MonoBehaviour
    {
        public static readonly bool Requested = Array.IndexOf(Environment.GetCommandLineArgs(), "-characterSmoke") >= 0;
        public CharacterPreviewActor Actor;
        public CharacterPreviewCamera View;
        public CharacterPreviewRoamer Roamer;
        private Camera cameraComponent;
        private RenderTexture target;
        private UniversalRenderPipeline.SingleCameraRequest request;
        private string directory;
        private Report report;
        private bool done, ready;
        private float started;
        private static readonly List<string> Errors = new List<string>();

        [Serializable] public sealed class Check { public string name, evidence; public bool passed; }
        [Serializable] private sealed class Report
        {
            public string status, utc, version, unityVersion, gpu, failure, acceptanceLimit;
            public float seconds, walkedMetres, footRotationChange, groundedY, maxDetourX;
            public float lowestIdleVertexY, lowestWalkVertexY = 100, highestWalkSoleY = -100, barrierPassX = 100;
            public int frames, wallContacts, plans, blockedCells;
            public List<Check> checks = new List<Check>();
            public List<string> captures = new List<string>(), errors = new List<string>();
            public List<CharacterPreviewRoamer.Observation> observations;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Observe()
        {
            if (!Requested) return;
            Errors.Clear();
            Application.logMessageReceived += (condition, stack, type) =>
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(condition); };
        }
        private void Awake()
        {
            if (!Requested) { enabled = false; return; }
            started = Time.realtimeSinceStartup;
            try
            {
                var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-characterEvidence");
                if (index < 0 || index + 1 >= args.Length || !Path.IsPathFullyQualified(args[index + 1]))
                    throw new InvalidOperationException("An absolute -characterEvidence output directory is required.");
                directory = Path.GetFullPath(args[index + 1]);
                if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length > 0)
                    throw new IOException("Evidence output must be empty; previous evidence is preserved.");
                Directory.CreateDirectory(directory);
                report = new Report { utc = DateTime.UtcNow.ToString("O"), version = Application.version,
                    unityVersion = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                    acceptanceLimit = "Automated actual standalone player and offscreen rendering. Native keyboard/mouse flow and sustained performance remain unverified. Scripted behavior, not learning." };
                Actor.TestControl = true; Roamer.Pause();
                Time.captureDeltaTime = 1f / 30; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
                cameraComponent = GetComponent<Camera>();
                target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Need("render-target-created", target.Create(), "1280 x 720 colour and depth target.");
                cameraComponent.aspect = 1280f / 720; cameraComponent.targetTexture = target;
                request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            }
            catch (Exception e) { Finish(e); }
        }
        private IEnumerator Start()
        {
            if (!Requested || done) yield break;
            var routine = Verify();
            while (!done)
            {
                bool more = false; Exception failure = null; object next = null;
                try
                {
                    if (Errors.Count > 0) throw new InvalidOperationException("Player runtime errors observed.");
                    if (Time.realtimeSinceStartup - started > 180) throw new TimeoutException("Character smoke exceeded 180 seconds.");
                    more = routine.MoveNext(); if (more) next = routine.Current;
                }
                catch (Exception e) { failure = e; }
                if (failure != null) { Finish(failure); yield break; }
                if (!more) { Finish(null); yield break; }
                yield return next;
            }
        }
        private void Need(string name, bool passed, string evidence)
        {
            report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
            if (!passed) throw new InvalidOperationException(name + ": " + evidence);
        }
        private IEnumerator Verify()
        {
            for (int i = 0; i < 45; i++) yield return null;
            Need("urp-render-request", RenderPipeline.SupportsRenderRequest(cameraComponent, request), "Actual player URP initialized.");
            cameraComponent.enabled = false; cameraComponent.targetTexture = null; ready = true;
            Need("human-avatar", Actor.Animator.avatar != null && Actor.Animator.avatar.isValid && Actor.Animator.avatar.isHuman, "Imported adult male avatar is valid and human.");
            foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
                Need("bone-" + bone, Actor.Animator.GetBoneTransform(bone) != null, "Mapped bone exists in the instantiated body.");
            Need("root-motion-disabled", !Actor.Animator.applyRootMotion, "Controller owns movement; imported no-root-motion walk owns the pose.");
            Need("idle-state", Actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Idle state after settling.");
            Need("initial-ground-contact", Actor.Grounded, "CharacterController below collision at stage floor.");
            report.lowestIdleVertexY = LowestBodyVertex();
            Need("visible-idle-grounding", report.lowestIdleVertexY > -.06f && report.lowestIdleVertexY < .10f,
                "Lowest deformed body vertex y=" + report.lowestIdleVertexY + " above the y=0 floor.");
            Capture("01-idle");
            Vector3 before = Actor.transform.position;
            Transform foot = Actor.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Quaternion previousFoot = foot.localRotation;
            Actor.TestDirection = Vector3.forward; View.Yaw = 145;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                report.footRotationChange = Mathf.Max(report.footRotationChange, Quaternion.Angle(previousFoot, foot.localRotation));
                float sole = LowestBodyVertex();
                report.lowestWalkVertexY = Mathf.Min(report.lowestWalkVertexY, sole);
                report.highestWalkSoleY = Mathf.Max(report.highestWalkSoleY, sole);
                if (i == 20) Capture("02-walking");
            }
            report.walkedMetres = Vector3.Distance(before, Actor.transform.position);
            Need("walk-state", Actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), "Walk state active while character moves.");
            Need("walk-distance", report.walkedMetres > 3 && report.walkedMetres < 3.6f, "Two simulated seconds: " + report.walkedMetres + " metres.");
            Need("retargeted-foot-pose-changes", report.footRotationChange > 10, "Actual rendered body's foot rotation changed " + report.footRotationChange + " degrees.");
            Need("visible-walk-grounding", report.lowestWalkVertexY > -.08f && report.highestWalkSoleY < .14f,
                "Lowest body vertex across walk frames: " + report.lowestWalkVertexY + " to " + report.highestWalkSoleY + " metres.");
            Actor.TestDirection = Vector3.zero;
            for (int i = 0; i < 15; i++) yield return null;
            Need("return-to-idle", Actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Stopped motion transitions to idle.");
            Actor.Place(new Vector3(0, .05f, -2)); Actor.TestDirection = Vector3.forward;
            for (int i = 0; i < 90; i++) yield return null;
            Actor.TestDirection = Vector3.zero;
            report.wallContacts = Actor.WallContacts;
            Need("wall-stops-capsule", Actor.transform.position.z < -.65f && Actor.transform.position.z > -.9f && report.wallContacts > 0,
                "Actor z=" + Actor.transform.position.z + "; actual side contacts=" + report.wallContacts + ".");
            Actor.Place(new Vector3(5, 3, -5));
            for (int i = 0; i < 90; i++) yield return null;
            report.groundedY = Actor.transform.position.y;
            Need("gravity-and-floor", Actor.Grounded && Mathf.Abs(report.groundedY) < .12f, "Fall from 3m ended grounded at y=" + report.groundedY + ".");
            Actor.Place(new Vector3(0, 0, -2)); View.Yaw = 180; View.Pitch = 0; View.Follow();
            Need("camera-obstacle-avoidance", View.Occluded && View.ActualDistance < 2,
                "Sphere cast shortened following distance to " + View.ActualDistance + " metres before the wall.");
            Actor.Place(new Vector3(0, .05f, -5)); Actor.transform.rotation = Quaternion.identity;
            View.Yaw = 145; View.Pitch = 18; View.Distance = 5.5f;
            Actor.TestControl = false; Roamer.Begin();
            Directory.CreateDirectory(Path.Combine(directory, "roam-frames")); int video = 0;
            for (int i = 0; i < 1500 && !Roamer.Complete; i++)
            {
                yield return null;
                report.maxDetourX = Mathf.Max(report.maxDetourX, Mathf.Abs(Actor.transform.position.x));
                if (Mathf.Abs(Actor.transform.position.z) < .65f)
                    report.barrierPassX = Mathf.Min(report.barrierPassX, Mathf.Abs(Actor.transform.position.x));
                if (i % 6 == 0) Capture("roam-frames/frame-" + (video++).ToString("D4"), false);
                if (Roamer.Carrying && !report.captures.Contains("03-carrying.png")) Capture("03-carrying");
                NeedFinitePosition();
                if (!Roamer.Running && !Roamer.Complete) throw new InvalidOperationException("Roamer stopped before completion.");
            }
            report.plans = Roamer.Plans; report.blockedCells = Roamer.BlockedCells;
            Need("roamer-completed", Roamer.Complete, "Bounded scripted collect-and-deliver sequence finished.");
            Need("route-avoided-obstacle", Roamer.Plans == 2 && Roamer.BlockedCells > 0 && report.barrierPassX >= 1.85f && report.barrierPassX < 10,
                "Two routes; " + Roamer.BlockedCells + " blocked cells, minimum x clearance while passing wall " + report.barrierPassX + ".");
            Need("crystal-delivered", Roamer.Crystal.parent == Roamer.DestinationSocket && !Roamer.Carrying,
                "Actual crystal reparented from source via right hand to destination socket.");
            Need("observable-actions", Roamer.Journal.Count >= 6, Roamer.Journal.Count + " observation/action/outcome entries.");
            for (int i = 0; i < 15; i++) yield return null;
            View.Yaw = 155; View.Follow(); Capture("04-delivered");
            Need("camera-follows-actor", Vector3.Distance(cameraComponent.transform.position, Actor.transform.position) < 7,
                "Camera remains close to moved actor after the full route.");
            Need("no-runtime-errors", Errors.Count == 0, "Actual player reported no error/assert/exception.");
        }
        private void NeedFinitePosition()
        {
            Vector3 p = Actor.transform.position;
            if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z) || Mathf.Abs(p.x) > 11 || Mathf.Abs(p.z) > 11 || p.y < -.2f)
                throw new InvalidOperationException("Actor escaped finite stage bounds.");
        }
        private float LowestBodyVertex()
        {
            float lowest = float.MaxValue;
            foreach (var renderer in Actor.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (renderer.name != "SuperHero_Male") continue;
                var mesh = new Mesh(); renderer.BakeMesh(mesh);
                foreach (var point in mesh.vertices) lowest = Mathf.Min(lowest, renderer.transform.TransformPoint(point).y);
                Destroy(mesh);
            }
            return lowest;
        }
        private void Capture(string name, bool listed = true)
        {
            if (!ready) return;
            View.Follow(); RenderPipeline.SubmitRenderRequest(cameraComponent, request);
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
                if (listed) report.captures.Add(name + ".png");
            }
            finally { RenderTexture.active = previous; Destroy(texture); }
        }
        private void Finish(Exception error)
        {
            if (done) return; done = true;
            if (report != null && directory != null)
            {
                report.status = error == null ? "PASS" : "FAIL"; report.failure = error?.Message;
                report.seconds = Time.realtimeSinceStartup - started; report.frames = Time.frameCount;
                report.errors = new List<string>(Errors); report.observations = Roamer != null ? Roamer.Journal : null;
                File.WriteAllText(Path.Combine(directory, "character-runtime.json"), JsonUtility.ToJson(report, true));
            }
            Time.captureDeltaTime = 0;
            if (target != null) target.Release();
            Application.Quit(error == null ? 0 : 3);
        }
    }
}
