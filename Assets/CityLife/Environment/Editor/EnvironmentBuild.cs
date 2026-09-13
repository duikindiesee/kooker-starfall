using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using CityLife.World.Editor;

namespace Starfall.EnvironmentFoundation.Editor
{
    public static class EnvironmentBuild
    {
        public static void Run()
        {
            Directory.CreateDirectory("evidence/local");
            var checks=EnvironmentChecks.Run();File.WriteAllText("evidence/local/environment-model-checks.json",JsonUtility.ToJson(new CheckReport{assertions=checks.Count,status="PASS"},true));
            CityLifeBuild.Prepare();IslandValidation.Run();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Environment foundation").AddComponent<EnvironmentWorld>();
            Directory.CreateDirectory("Assets/CityLife/Environment/Scenes");string scenePath="Assets/CityLife/Environment/Scenes/EnvironmentFixture.unity";EditorSceneManager.SaveScene(scene,scenePath);
            PlayerSettings.companyName="StarfallComponentStudies";PlayerSettings.productName="Starfall Environment Foundation";PlayerSettings.bundleVersion="0.1.0-environment.1";
            PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.resizableWindow=false;
            PlayerSettings.defaultIsNativeResolution=false;PlayerSettings.allowFullscreenSwitch=false;
            string folder="Builds/Environment-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(folder);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scenePath},locationPathName=folder+"/StarfallEnvironment.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("evidence/local/environment-build.json",JsonUtility.ToJson(new BuildReport{status=report.summary.result.ToString(),folder=folder,seconds=report.summary.totalTime.TotalSeconds,errors=(int)report.summary.totalErrors},true));
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Environment player build failed");
        }
        [Serializable]sealed class CheckReport{public string status;public int assertions;}
        [Serializable]sealed class BuildReport{public string status,folder;public double seconds;public int errors;}
    }
}
