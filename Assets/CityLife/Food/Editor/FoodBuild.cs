using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using CityLife.World.Editor;
namespace Starfall.Food.Editor
{
    public static class FoodBuild
    {
        [Serializable]sealed class Report{public string status,folder,version;public int errors,checks;public double seconds;}
        public static void Run()
        {
            Directory.CreateDirectory("evidence/local");string checkFolder="evidence/local/editor-food-checks-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");var checks=FoodChecks.Run(checkFolder);File.WriteAllLines(checkFolder+"/passed.txt",checks);
            CityLifeBuild.Prepare();IslandValidation.Run();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Starfall food fixture").AddComponent<FoodWorld>();
            Directory.CreateDirectory("Assets/CityLife/Food/Scenes");string path="Assets/CityLife/Food/Scenes/FoodFixture.unity";EditorSceneManager.SaveScene(scene,path);
            PlayerSettings.companyName="StarfallComponentStudies";PlayerSettings.productName="Starfall Food - First Berry";PlayerSettings.bundleVersion="0.1.2-food.3";
            PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=false;PlayerSettings.defaultIsNativeResolution=false;PlayerSettings.allowFullscreenSwitch=false;
            string folder="Builds/Food-0.1.2-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(folder);
            var r=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{path},locationPathName=folder+"/StarfallFood.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("evidence/local/food-build.json",JsonUtility.ToJson(new Report{status=r.summary.result.ToString(),folder=folder,version=PlayerSettings.bundleVersion,errors=(int)r.summary.totalErrors,seconds=r.summary.totalTime.TotalSeconds,checks=checks.Count},true));
            if(r.summary.result!=BuildResult.Succeeded)throw new Exception("Food build failed");
        }
    }
}
