using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityLife.World.Editor
{
    public static class RiverbankPilotAuthoring
    {
        public static void ConfigureAssets()
        {
            // 1. Texture Importers
            ConfigureNormalMap("Assets/CityLife/Art/RiverbankPilot/Boulder/Textures/Boulder_Normal.png");
            ConfigureNormalMap("Assets/CityLife/Art/RiverbankPilot/Sedge/Textures/Sedge_Normal.png");
            ConfigureNormalMap("Assets/CityLife/Art/RiverbankPilot/Log/Textures/Log_Normal.png");

            ConfigureDefaultTexture("Assets/CityLife/Art/RiverbankPilot/Boulder/Textures/Boulder_BaseColor.png");
            ConfigureDefaultTexture("Assets/CityLife/Art/RiverbankPilot/Boulder/Textures/Boulder_MetallicGloss.png");
            ConfigureDefaultTexture("Assets/CityLife/Art/RiverbankPilot/Sedge/Textures/Sedge_BaseColor.png");
            ConfigureDefaultTexture("Assets/CityLife/Art/RiverbankPilot/Sedge/Textures/Sedge_MetallicGloss.png");
            ConfigureDefaultTexture("Assets/CityLife/Art/RiverbankPilot/Log/Textures/Log_BaseColor.png");
            ConfigureDefaultTexture("Assets/CityLife/Art/RiverbankPilot/Log/Textures/Log_MetallicGloss.png");

            // 2. Materials
            ConfigureMaterial(
                "Assets/CityLife/Art/RiverbankPilot/Boulder/Boulder_Material.mat",
                "Assets/CityLife/Art/RiverbankPilot/Boulder/Textures/Boulder_BaseColor.png",
                "Assets/CityLife/Art/RiverbankPilot/Boulder/Textures/Boulder_Normal.png",
                "Assets/CityLife/Art/RiverbankPilot/Boulder/Textures/Boulder_MetallicGloss.png",
                cullOff: false,
                metallic: 0.0f,
                smoothness: 0.18f
            );

            ConfigureMaterial(
                "Assets/CityLife/Art/RiverbankPilot/Sedge/Sedge_Material.mat",
                "Assets/CityLife/Art/RiverbankPilot/Sedge/Textures/Sedge_BaseColor.png",
                "Assets/CityLife/Art/RiverbankPilot/Sedge/Textures/Sedge_Normal.png",
                "Assets/CityLife/Art/RiverbankPilot/Sedge/Textures/Sedge_MetallicGloss.png",
                cullOff: true,
                metallic: 0.0f,
                smoothness: 0.22f
            );

            ConfigureMaterial(
                "Assets/CityLife/Art/RiverbankPilot/Log/Log_Material.mat",
                "Assets/CityLife/Art/RiverbankPilot/Log/Textures/Log_BaseColor.png",
                "Assets/CityLife/Art/RiverbankPilot/Log/Textures/Log_Normal.png",
                "Assets/CityLife/Art/RiverbankPilot/Log/Textures/Log_MetallicGloss.png",
                cullOff: true,
                metallic: 0.0f,
                smoothness: 0.15f
            );

            AssetDatabase.SaveAssets();
            Debug.Log("[RiverbankPilotAuthoring] Riverbank pilot textures and URP Lit materials successfully configured.");
        }

        private static void ConfigureNormalMap(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureDefaultTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureMaterial(string matPath, string baseTexPath, string normTexPath, string mgTexPath, bool cullOff, float metallic = 0.0f, float smoothness = 0.20f)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(matPath) };
                AssetDatabase.CreateAsset(mat, matPath);
            }

            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(baseTexPath);
            var normTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normTexPath);
            var mgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(mgTexPath);

            if (baseTex != null)
            {
                mat.SetTexture("_BaseMap", baseTex);
                mat.SetTexture("_MainTex", baseTex);
            }
            if (normTex != null)
            {
                mat.SetTexture("_BumpMap", normTex);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (mgTex != null)
            {
                mat.SetTexture("_MetallicGlossMap", mgTex);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }

            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);

            if (cullOff)
            {
                mat.SetFloat("_Cull", 0f); // RenderFace = Both
                mat.doubleSidedGI = true;
            }

            EditorUtility.SetDirty(mat);
        }
    }
}
