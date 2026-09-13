using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace CityLife.World.Editor
{
    /// <summary>Bakes the inspected local stage, never an IslandBootstrap or user world/save.</summary>
    public static class CosmicPreviewBuild
    {
        public const string R19Version="0.0.2-preview.2";
        public const string FrozenTreeCommit="fc30b2857be419172e740f0d338d5913145d75fb";
        public const string FrozenComponentHash="3fee339d4fddf09846ff6c8f97f3bc89fa1c31faf8f7a1036879d9d90a72c1ca";
        public static void Build(Camera camera,GameObject ground,Dictionary<string,string> shaderSources,string evidenceDirectory,bool frozenR19=false,string sourceCommit="",string componentHash="")
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Use isolated preview batch.");
            if(frozenR19&&(!System.Text.RegularExpressions.Regex.IsMatch(sourceCommit,"^[0-9a-f]{40}$")||componentHash!=FrozenComponentHash))
                throw new InvalidOperationException("Frozen R19 build requires an exact source commit and unchanged component mesh hash.");
            IslandValidation.Run();
            string id=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string folder="Assets/CityLife/GeneratedPreview-"+id;
            string buildName=frozenR19?"KookerStarfallR19-"+R19Version+"-"+id:"KookerStarfall-"+id;
            string buildDirectory="Builds/"+buildName;
            if(Directory.Exists(folder)||Directory.Exists(buildDirectory))throw new IOException("Preview output already exists; existing builds are preserved.");
            Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var persisted=new Dictionary<Object,Object>();int assetIndex=0;
            Object Persist(Object source)
            {
                if(source==null)return null;
                if(persisted.TryGetValue(source,out Object cached))return cached;
                string existing=AssetDatabase.GetAssetPath(source);
                if(!string.IsNullOrEmpty(existing))return source;
                if(source is Shader shader)
                {
                    if(!shaderSources.TryGetValue(shader.name,out string code))return source;
                    string shaderPath=folder+"/Shader-"+(assetIndex++)+".shader";
                    File.WriteAllText(shaderPath,code);AssetDatabase.ImportAsset(shaderPath,ImportAssetOptions.ForceSynchronousImport);
                    var result=AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);persisted.Add(source,result);return result;
                }
                Object clone=Object.Instantiate(source);clone.hideFlags=HideFlags.None;
                persisted.Add(source,clone);
                if(clone is Material material)
                {
                    material.shader=(Shader)Persist(material.shader);
                    foreach(string property in material.GetTexturePropertyNames())
                        if(material.GetTexture(property)!=null)material.SetTexture(property,(Texture)Persist(material.GetTexture(property)));
                }
                AssetDatabase.CreateAsset(clone,folder+"/Asset-"+(assetIndex++)+".asset");return clone;
            }
            foreach(MeshFilter filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                filter.sharedMesh=(Mesh)Persist(filter.sharedMesh);
            foreach(Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                Material[] materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)materials[i]=(Material)Persist(materials[i]);
                renderer.sharedMaterials=materials;
            }
            MeshCollider collider=ground.GetComponent<MeshCollider>();if(collider==null)collider=ground.AddComponent<MeshCollider>();
            collider.sharedMesh=ground.GetComponent<MeshFilter>().sharedMesh;
            int woodColliders=0;
            foreach(MeshFilter filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if(filter.name=="Bark")
                {
                    var c=filter.gameObject.AddComponent<MeshCollider>();
                    // Use the legacy midphase for the dense frozen wood, without simplifying its geometry.
                    if(frozenR19&&filter.name=="Bark")c.cookingOptions&=~MeshColliderCookingOptions.UseFastMidphase;
                    c.sharedMesh=filter.sharedMesh;
                    woodColliders++;
                }
            camera.enabled=true;camera.tag="MainCamera";
            ground.layer=8;
            var explorer=camera.gameObject.AddComponent<CosmicPreviewExplorer>();explorer.Camera=camera;explorer.GroundMask=1<<8;
            if(frozenR19)
            {
                camera.gameObject.AddComponent<CosmicPreviewSmoke>();
                camera.gameObject.AddComponent<PreviewDisplayMode>();
            }
            if(camera.GetComponent<AudioListener>()==null)camera.gameObject.AddComponent<AudioListener>();

            var pipeline=Object.Instantiate((UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline);
            pipeline.hideFlags=HideFlags.None;
            var pipelineSettings=new SerializedObject(pipeline);var renderers=pipelineSettings.FindProperty("m_RendererDataList");
            for(int i=0;i<renderers.arraySize;i++)
            {
                var renderer=Object.Instantiate(renderers.GetArrayElementAtIndex(i).objectReferenceValue);renderer.hideFlags=HideFlags.None;
                AssetDatabase.CreateAsset(renderer,folder+"/Renderer-"+i+".asset");renderers.GetArrayElementAtIndex(i).objectReferenceValue=renderer;
            }
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.CreateAsset(pipeline,folder+"/Pipeline.asset");
            string oldCompany=PlayerSettings.companyName,oldProduct=PlayerSettings.productName,oldVersion=PlayerSettings.bundleVersion;
            int oldWidth=PlayerSettings.defaultScreenWidth,oldHeight=PlayerSettings.defaultScreenHeight,oldQuality=QualitySettings.GetQualityLevel();
            bool oldBackground=PlayerSettings.runInBackground,oldResize=PlayerSettings.resizableWindow,oldSwitch=PlayerSettings.allowFullscreenSwitch;
            FullScreenMode oldMode=PlayerSettings.fullScreenMode;
            RenderPipelineAsset oldGraphics=GraphicsSettings.defaultRenderPipeline;
            var oldPipelines=new RenderPipelineAsset[QualitySettings.names.Length];
            int capturedPipelines=0;
            try
            {
                for(int i=0;i<oldPipelines.Length;i++)
                {QualitySettings.SetQualityLevel(i);oldPipelines[i]=QualitySettings.renderPipeline;capturedPipelines=i+1;}
                QualitySettings.SetQualityLevel(oldQuality);
                PlayerSettings.companyName="LocalWorldStudy";PlayerSettings.productName=frozenR19?"Kooker Starfall R19 "+R19Version:"Kooker Starfall";PlayerSettings.bundleVersion=frozenR19?R19Version:"0.0.1-wip";
                PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.runInBackground=true;
                if(frozenR19){PlayerSettings.resizableWindow=true;PlayerSettings.allowFullscreenSwitch=false;}
                GraphicsSettings.defaultRenderPipeline=pipeline;
                for(int i=0;i<oldPipelines.Length;i++){QualitySettings.SetQualityLevel(i);QualitySettings.renderPipeline=pipeline;}
                QualitySettings.SetQualityLevel(oldQuality);
                string scene=folder+"/CosmicPreview.unity";EditorSceneManager.SaveScene(camera.gameObject.scene,scene);AssetDatabase.SaveAssets();
                string output=buildDirectory+(frozenR19?"/KookerStarfallR19.exe":"/KookerStarfall.exe");Directory.CreateDirectory(buildDirectory);
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},locationPathName=Path.GetFullPath(output),target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
                var evidence=new BuildEvidence{status=report.summary.result.ToString(),output=output,scene=scene,product=PlayerSettings.productName,bytes=(long)report.summary.totalSize,seconds=report.summary.totalTime.TotalSeconds,errors=(int)report.summary.totalErrors,warnings=(int)report.summary.totalWarnings,scope="Separate local WIP tree and blue-giant inspection stage. No IslandBootstrap, saved world, multiplayer or bot integrations. Visual gate not passed; source hybrid and runtime movement remain under review.",utc=DateTime.UtcNow.ToString("O")};
                evidence.buildId=buildName;evidence.version=PlayerSettings.bundleVersion;evidence.sourceCommit=sourceCommit;
                evidence.woodColliders=woodColliders;evidence.rockColliders=0;
                if(frozenR19)
                {
                    evidence.treeReview="R19";evidence.frozenTreeCommit=FrozenTreeCommit;evidence.componentMeshSha256=componentHash;
                    evidence.scope="Separate versioned local player using the frozen R19 PH02 tree and existing study stage. Preserves R06. No saved-world loading, multiplayer, bot integrations, living sea or wider landscape. Actual player startup/input/collision acceptance recorded separately.";
                }
                File.WriteAllText(Path.Combine(evidenceDirectory,"preview-build.json"),JsonUtility.ToJson(evidence,true));
                if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Cosmic WIP preview build failed.");
                Debug.Log("COSMIC_PREVIEW_BUILD_SUCCEEDED "+output);
            }
            finally
            {
                PlayerSettings.companyName=oldCompany;PlayerSettings.productName=oldProduct;PlayerSettings.bundleVersion=oldVersion;
                PlayerSettings.defaultScreenWidth=oldWidth;PlayerSettings.defaultScreenHeight=oldHeight;PlayerSettings.fullScreenMode=oldMode;PlayerSettings.runInBackground=oldBackground;
                PlayerSettings.resizableWindow=oldResize;PlayerSettings.allowFullscreenSwitch=oldSwitch;
                GraphicsSettings.defaultRenderPipeline=oldGraphics;
                for(int i=0;i<capturedPipelines;i++){QualitySettings.SetQualityLevel(i);QualitySettings.renderPipeline=oldPipelines[i];}
                QualitySettings.SetQualityLevel(oldQuality);AssetDatabase.SaveAssets();
            }
        }
        [Serializable]sealed class BuildEvidence{public string status,output,scene,product,scope,utc,buildId,version,sourceCommit,treeReview,frozenTreeCommit,componentMeshSha256;public long bytes;public double seconds;public int errors,warnings,woodColliders,rockColliders;}
    }
}
