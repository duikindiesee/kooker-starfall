using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityLife.World.Editor
{
    public static class CharacterAssetImport
    {
        public const string Root = "Assets/CityLife/Characters/QuaterniusStandard/";
        public const string Body = Root + "Superhero_Male_FullBody.fbx";
        public const string Motions = Root + "UAL1_Standard.fbx";
        public static void Run()
        {
            foreach (string path in new[] { Body, Motions })
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = path == Motions;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.isReadable = true;
                if (path == Motions)
                {
                    var clips = importer.defaultClipAnimations;
                    foreach (var clip in clips)
                    {
                        clip.loopTime = clip.name.EndsWith("Loop", StringComparison.Ordinal);
                        clip.lockRootRotation = true; clip.keepOriginalOrientation = true;
                        clip.lockRootPositionXZ = true; clip.keepOriginalPositionXZ = true;
                        clip.lockRootHeightY = true; clip.keepOriginalPositionY = false;
                        clip.heightFromFeet = true; clip.heightOffset = 0;
                    }
                    importer.clipAnimations = clips;
                }
                importer.SaveAndReimport();
            }
            var normal = (TextureImporter)AssetImporter.GetAtPath(Root + "Skin_Normal.png");
            normal.textureType = TextureImporterType.NormalMap; normal.SaveAndReimport();
            var report = new System.Text.StringBuilder();
            foreach (string path in new[] { Body, Motions })
            {
                report.AppendLine("MODEL " + path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = UnityEngine.Object.Instantiate(prefab);
                var animator = instance.GetComponent<Animator>();
                report.AppendLine("avatar=" + animator?.avatar?.name + " valid=" + animator?.avatar?.isValid + " human=" + animator?.avatar?.isHuman);
                foreach (var r in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                    report.AppendLine("mesh=" + r.name + " vertices=" + r.sharedMesh.vertexCount + " materials=" + string.Join(",", r.sharedMaterials.Select(x=>x ? x.name : "null")) + " bounds=" + r.bounds);
                foreach (var t in instance.GetComponentsInChildren<Transform>()) report.AppendLine("bone " + t.name);
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(x=>!x.name.StartsWith("__")))
                    report.AppendLine("clip=" + clip.name + " length=" + clip.length + " fps=" + clip.frameRate + " humanoid=" + clip.humanMotion);
                UnityEngine.Object.DestroyImmediate(instance);
            }
            Directory.CreateDirectory("evidence/local/character");
            File.WriteAllText("evidence/local/character/import-inventory.txt", report.ToString());
            Debug.Log("CHARACTER_IMPORT_INVENTORY_COMPLETE");
        }
    }
}
