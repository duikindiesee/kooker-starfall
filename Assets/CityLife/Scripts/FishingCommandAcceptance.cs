using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using CityLife.Items;

namespace CityLife.World
{
    // Opt-in compiled-player probe: submits via the real HUD and waits for normal
    // frame simulation. Does not teleport, seed perception, equip tools or force bites.
    public sealed class FishingCommandAcceptance : MonoBehaviour
    {
        [Serializable] public sealed class Sample
        {
            public float seconds;
            public string command, step, status, receipt, fishing, interestedFish;
            public string perception, nearbyFishDiagnostic;
            public Vector3 knownWater, selectedBank;
            public Vector3 actor, fish, bait;
            public bool swimming, baitEaten;
        }
        [Serializable] public sealed class Result
        {
            public string status = "RUNNING", version, buildGuid, scope = "Real HUD Input System submission and normal player simulation; isolated saves";
            public float automatedAudioVolume;
            public string consumedFishId, storedFishId, storageBasketId;
            public int nutritionAfterEating;
            public List<string> checks = new List<string>();
            public List<string> failures = new List<string>();
            public List<Sample> samples = new List<Sample>();
        }
        private readonly Result result = new Result();
        private string directory;
        private NpcAutonomy brain;
        private NpcPlayerControls controls;
        private Keyboard keyboard;
        private float started;
        private float previousAudioVolume;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            previousAudioVolume = AudioListener.volume;
            AudioListener.volume = 0f;
        }

        private void OnDestroy() => AudioListener.volume = previousAudioVolume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fishingCommandAcceptance") >= 0)
                new GameObject("Fishing Command Acceptance").AddComponent<FishingCommandAcceptance>();
        }

        private IEnumerator Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            directory = Argument(args, "-fishingEvidence");
            if (!Path.IsPathFullyQualified(directory ?? "") ||
                !Path.IsPathFullyQualified(Argument(args, "-npcSurvivalSave") ?? "") ||
                !Path.IsPathFullyQualified(Argument(args, "-physicalSave") ?? ""))
            {
                Debug.LogError("Fishing acceptance requires explicit evidence and isolated save paths.");
                Application.Quit(5);
                yield break;
            }
            Directory.CreateDirectory(directory);
            result.version = Application.version;
            result.buildGuid = Application.buildGUID;
            result.automatedAudioVolume = AudioListener.volume;
            started = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLog;
            for (int i = 0; i < 1800; i++)
            {
                brain = FindFirstObjectByType<NpcAutonomy>();
                controls = FindFirstObjectByType<NpcPlayerControls>();
                if (brain != null && brain.Ready && controls != null && controls.Hud != null &&
                    controls.Hud.CommandInputField != null) break;
                yield return null;
            }
            if (brain == null || !brain.Ready || controls?.Hud?.CommandInputField == null)
            {
                result.failures.Add("Player or command HUD did not initialize.");
                Finish(); yield break;
            }
            InputSystem.settings = Instantiate(InputSystem.settings);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard = InputSystem.AddDevice<Keyboard>("FishingCommandAcceptanceKeyboard");
            keyboard.MakeCurrent();
            controls.TestKeyboard = keyboard;
            controls.AllowUnfocusedTestInput = true;
            yield return new WaitForSeconds(1);
            var supplies = brain.GetComponent<FishingCampSupplies>();
            for (int i = 0; supplies != null && !supplies.Ready && i < 300; i++) yield return null;
            if (supplies != null && !supplies.Ready) { result.failures.Add(supplies.Receipt); Finish(); yield break; }

            string reloadExpected = Argument(args, "-fishingReloadExpected");
            if (!string.IsNullOrEmpty(reloadExpected))
            {
                CheckReload(reloadExpected, supplies);
                Capture("cold-reload");
                Finish(); yield break;
            }

            yield return RunCommand("go fish", 300);
            var first = HeldCatch();
            if (first == null) result.failures.Add("go fish did not land a physical fish in a hand.");
            else result.checks.Add("go fish landed " + first.itemId);
            if (result.failures.Count > 0) { Finish(); yield break; }

            string consumedId = first.itemId;
            result.consumedFishId = consumedId;
            int foodBefore = brain.Survival.Food.Model.State.satiety;
            yield return RunCommand("eat catch", 30);
            if (HeldCatch() != null || brain.Actions.PhysicalModel.TryGetItem(consumedId, out _) ||
                brain.Survival.Food.Model.State.satiety <= foodBefore)
                result.failures.Add("Eating did not conserve item removal and increased nutrition.");
            else result.checks.Add("eat catch removed exact fish and increased nutrition");
            result.nutritionAfterEating = brain.Survival.Food.Model.State.satiety;
            if (result.failures.Count > 0) { Finish(); yield break; }

            yield return RunCommand("go fish then store", 240);
            bool stored = false;
            foreach (var item in brain.Actions.PhysicalModel.GetAllItemSnapshots())
                if (item.location == ItemLocationKind.Stored &&
                    (item.itemTypeId == "food-river-fish" || item.itemTypeId == "food-river-carp"))
                { stored = true; result.storedFishId = item.itemId; result.storageBasketId = item.containerItemId; }
            if (!stored || HeldCatch() != null) result.failures.Add("Second catch was not transferred to basket storage.");
            else result.checks.Add("go fish then store transferred a real catch into basket");
            yield return new WaitForSeconds(2);
            Finish();
        }

        private void CheckReload(string expectedPath, FishingCampSupplies supplies)
        {
            if (!Path.IsPathFullyQualified(expectedPath) || !File.Exists(expectedPath))
            { result.failures.Add("Explicit prior runtime receipt required for reload."); return; }
            var expected = JsonUtility.FromJson<Result>(File.ReadAllText(expectedPath));
            if (expected.status != "PASS_HUD_COMMAND_RUNTIME" || string.IsNullOrEmpty(expected.consumedFishId) ||
                string.IsNullOrEmpty(expected.storedFishId) || string.IsNullOrEmpty(expected.storageBasketId))
            { result.failures.Add("Prior receipt did not prove complete catch/eat/store."); return; }
            var model = brain.Actions.PhysicalModel;
            if (model.TryGetItem(expected.consumedFishId, out _) || !model.TryGetTombstone(expected.consumedFishId, out _))
                result.failures.Add("Consumed fish resurrected or lost its consumption tombstone.");
            else result.checks.Add("Cold reload preserved exact consumed fish tombstone and absence");
            if (!model.TryGetItem(expected.storedFishId, out var stored) || stored.location != ItemLocationKind.Stored ||
                stored.containerItemId != expected.storageBasketId)
                result.failures.Add("Cold reload lost exact stored fish/container ownership.");
            else result.checks.Add("Cold reload preserved exact caught fish in original basket");
            var storedBindings = brain.PhysicalItems.Bindings.Where(x => x.itemId == expected.storedFishId).ToArray();
            if (storedBindings.Length != 1 || storedBindings[0].physicalItem == null ||
                !storedBindings[0].physicalItem.IsStored ||
                storedBindings[0].physicalItem.BoundContainerItemId != expected.storageBasketId)
                result.failures.Add("Cold reload lost stored fish runtime binding or container attachment.");
            else result.checks.Add("Cold reload restored the stored fish runtime object and container attachment");
            if (model.GetAllItemSnapshots().Count(x => x.itemId == expected.storageBasketId) != 1 ||
                brain.PhysicalItems.Bindings.Count(x => x.itemId == expected.storageBasketId) != 1 ||
                supplies == null || supplies.Receipt != "supplies-v1-already-applied")
                result.failures.Add("Basket migration duplicated or failed to restore existing basket.");
            else result.checks.Add("Cold reload restored one basket without repeating supply migration");
            if (Starfall.Food.ItemObservationMemory.Nearest(brain.Survival.Food.Model.State, "basket", brain.transform.position)?.id != expected.storageBasketId)
                result.failures.Add("Cold reload lost grounded basket memory.");
            else result.checks.Add("Cold reload preserved observed basket memory");
            var rod = FindObjectsByType<FishingRodItem>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.PhysicalItem != null && model.TryGetItem(x.PhysicalItem.itemId, out _));
            if (rod == null || rod.TipTransform == null || rod.Interactable == null)
                result.failures.Add("Cold reload lost usable fishing rod component or line tip.");
            else result.checks.Add("Cold reload retained usable fishing rod and line tip");
        }

        private IEnumerator RunCommand(string text, float timeout)
        {
            controls.Hud.FocusCommandInput();
            yield return null;
            controls.Hud.CommandInputField.text = text;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            float deadline = Time.realtimeSinceStartup + timeout;
            float nextSample = 0;
            string previousStep = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                var survival = brain.Survival;
                if (Time.realtimeSinceStartup >= nextSample || previousStep != survival.ActiveCommandCurrentStep)
                {
                    nextSample = Time.realtimeSinceStartup + 1;
                    var fishing = brain.GetComponent<FishingInteraction>();
                    var fish = fishing?.BaitResponse.InterestedFish;
                    result.samples.Add(new Sample { seconds = Time.realtimeSinceStartup - started,
                        command = text, step = survival.ActiveCommandCurrentStep, status = survival.ActiveCommandStatus,
                        receipt = survival.LastCommandReceipt, actor = brain.transform.position,
                        perception = brain.DescribePerception(), knownWater = survival.FindGroundedRiverWaterTarget(brain.transform.position),
                        selectedBank = survival.CommandCastingBank,
                        nearbyFishDiagnostic = NearbyFishDiagnostic(),
                        swimming = brain.Actor != null && brain.Actor.IsSwimming,
                        fishing = fishing != null ? fishing.State.ToString() : "missing",
                        baitEaten = fishing != null && fishing.BaitEaten,
                        bait = fishing != null ? fishing.BaitPosition : Vector3.zero,
                        fish = fish?.gameObject != null ? fish.gameObject.transform.position : Vector3.zero,
                        interestedFish = fish?.gameObject != null ? fish.gameObject.name : null });
                    if (previousStep != survival.ActiveCommandCurrentStep)
                    {
                        previousStep = survival.ActiveCommandCurrentStep;
                        Capture("stage-" + result.samples.Count);
                    }
                    Write();
                }
                if (!survival.HasActiveCommand)
                {
                    if (!survival.LastCommandReceipt.StartsWith("command-completed:"))
                        result.failures.Add(text + ": " + survival.LastCommandReceipt);
                    else result.checks.Add("HUD directive completed: " + text);
                    yield break;
                }
                yield return null;
            }
            result.failures.Add(text + ": runtime timeout at " + brain.Survival.ActiveCommandStatus);
            brain.Survival.CancelActiveCommand("acceptance-timeout");
        }

        private PhysicalItem HeldCatch()
        {
            foreach (var held in new[] { brain.Actions.HeldLeft, brain.Actions.HeldRight })
            {
                var item = held != null ? held.GetComponent<PhysicalItem>() : null;
                if (item != null && (item.itemTypeId == "food-river-fish" || item.itemTypeId == "food-river-carp")) return item;
            }
            return null;
        }
        private string NearbyFishDiagnostic()
        {
            var school = RiverFishSchool.Instance;
            if (school == null) return "missing school";
            return string.Join("; ", school.ActiveFish.Where(f => f?.gameObject != null)
                .OrderBy(f => Vector3.SqrMagnitude(f.gameObject.transform.position - brain.transform.position)).Take(4)
                .Select(f => $"{f.gameObject.name} pos={f.gameObject.transform.position} reserved={f.isReserved} world={f.interactable?.WorldId}"));
        }
        private void Capture(string name)
        {
            var camera = controls.View.GetComponent<Camera>();
            controls.Hud.Refresh(); Canvas.ForceUpdateCanvases();
            var texture = new RenderTexture(1280, 720, 24);
            var previous = camera.targetTexture; var active = RenderTexture.active;
            camera.targetTexture = texture; RenderTexture.active = texture; camera.Render();
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            camera.targetTexture = previous; RenderTexture.active = active;
            texture.Release(); Destroy(texture); Destroy(image);
        }
        private static string Argument(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) result.failures.Add(message);
        }
        private void Write() => File.WriteAllText(Path.Combine(directory, "fishing-command-runtime.json"), JsonUtility.ToJson(result, true));
        private void Finish()
        {
            result.status = result.failures.Count == 0 ? "PASS_HUD_COMMAND_RUNTIME" : "FAIL";
            Write(); Application.logMessageReceived -= OnLog;
#if UNITY_EDITOR
            if (Application.isBatchMode) { UnityEditor.EditorApplication.Exit(result.failures.Count == 0 ? 0 : 4); return; }
#endif
            Application.Quit(result.failures.Count == 0 ? 0 : 4);
        }
    }
}
