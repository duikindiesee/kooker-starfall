using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World.Editor
{
 // Runs the preserved generated scene in the installed Editor, never a blocked player.
 [InitializeOnLoad]
 public static class HunterPlayMode
 {
  static HunterPlayMode(){EditorApplication.playModeStateChanged+=StateChanged;}
  public static void Run()
  {
   if(Application.isBatchMode)throw new InvalidOperationException("Use visible Editor Play Mode for Game view evidence.");
   var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-hunterEditorScene");
   if(i<0||i+1>=args.Length)throw new ArgumentException("An exact generated hunter scene is required.");
   string scene=args[i+1];EditorSceneManager.OpenScene(scene);
   var grip=UnityEngine.Object.FindFirstObjectByType<HunterClubCarry>();
   if(!grip)throw new InvalidOperationException("Scene has no hunter grip.");
   var modelScale=grip.transform.localScale;grip.transform.localScale=Vector3.one;
   try{grip.AuthorGrip();}finally{grip.transform.localScale=modelScale;}
   grip.Club.GetComponent<MeshFilter>().sharedMesh=HunterOutfitAuthoring.CreateClubMesh();
   var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(Path.GetDirectoryName(scene).Replace('\\','/')+"/Pipeline.asset");
   if(!pipeline)throw new InvalidOperationException("Missing matching preview pipeline.");
   GraphicsSettings.defaultRenderPipeline=pipeline;
   int quality=QualitySettings.GetQualityLevel();
   for(int q=0;q<QualitySettings.names.Length;q++){QualitySettings.SetQualityLevel(q);QualitySettings.renderPipeline=pipeline;}
   QualitySettings.SetQualityLevel(quality);
   SessionState.SetBool("HunterPlayMode.Active",true);SessionState.SetInt("HunterPlayMode.ExitCode",0);
   var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.Show();view.Focus();view.maximized=true;
   EditorApplication.EnterPlaymode();
  }
  private static void StateChanged(PlayModeStateChange state)
  {
   if(!SessionState.GetBool("HunterPlayMode.Active",false)||state!=PlayModeStateChange.EnteredEditMode)return;
   SessionState.SetBool("HunterPlayMode.Active",false);
   EditorApplication.delayCall+=()=>EditorApplication.Exit(SessionState.GetInt("HunterPlayMode.ExitCode",0));
  }
 }
}
