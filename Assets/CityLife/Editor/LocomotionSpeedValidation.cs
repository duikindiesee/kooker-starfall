using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityLife.World.Editor
{
    public static class LocomotionSpeedValidation
    {
        public static void RunBatch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var root = new GameObject("Movement validation actor");
            var actor = root.AddComponent<CharacterPreviewActor>();
            actor.Capsule = root.AddComponent<CharacterController>();
            actor.Capsule.height = 1.8f;
            actor.Capsule.center = Vector3.up * .9f;
            Vector3 start = new Vector3(25f, CoastalTerrain.Height(25f, -32f) + .05f, -32f);
            actor.Place(start);
            actor.Stamina = 50f;
            actor.Step(Vector3.forward, .1f);
            if (actor.IsSwimming || Mathf.Abs(actor.ActualSpeed - 3.0525f) > .02f || actor.Stamina <= 50f)
                throw new Exception($"Walk failed: speed={actor.ActualSpeed}, stamina={actor.Stamina}, swim={actor.IsSwimming}");
            actor.Place(start);
            actor.Stamina = 100f;
            actor.IsSprinting = true;
            actor.Step(Vector3.forward, .1f);
            if (Mathf.Abs(actor.ActualSpeed - 5f) > .02f || Mathf.Abs(actor.Stamina - 99.2f) > .01f)
                throw new Exception($"Run failed: speed={actor.ActualSpeed}, stamina={actor.Stamina}");
            actor.Place(start);
            actor.Stamina = 0;
            actor.Step(Vector3.forward, .1f);
            if (Mathf.Abs(actor.ActualSpeed - 3.0525f) > .02f || actor.Stamina <= 0)
                throw new Exception("Exhaustion must fall back to walking with stamina recovery.");
            Debug.Log("LOCOMOTION_SPEED_VALIDATION_PASSED: measured walk 3.0525 m/s, run 5 m/s, walk recovery, run drain 8/s, exhausted walk fallback.");
            UnityEngine.Object.DestroyImmediate(root);
            EditorApplication.Exit(0);
        }
    }
}
