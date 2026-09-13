using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityLife.World
{
    [DefaultExecutionOrder(-190)]
    public sealed class CoastalPreviewSmoke : MonoBehaviour
    {
        public static readonly bool Requested=Array.IndexOf(Environment.GetCommandLineArgs(),"-coastalSmoke")>=0;
        readonly List<string> checks=new List<string>();
        CosmicPreviewExplorer explorer; string directory;
        IEnumerator Start()
        {
            if(!Requested){enabled=false;yield break;}
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-coastalEvidence");
            if(at<0||at+1>=args.Length||!Path.IsPathFullyQualified(args[at+1]))throw new InvalidOperationException("Coastal smoke requires an absolute evidence directory.");
            directory=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(directory);
            explorer=GetComponent<CosmicPreviewExplorer>();
            while(explorer==null||!explorer.SmokeReady)yield return null;
            Physics.SyncTransforms();
            Check("world-id",GameObject.Find("Starfall coastal slice v1 - separate world study")!=null);
            Check("terrain-collider",GameObject.Find("Coastal terrain "+CoastalTerrain.DefinitionId+" seed "+CoastalTerrain.Seed)?.GetComponent<MeshCollider>()!=null);
            Check("rock-colliders",CountRocks()==84);
            Check("water",GameObject.Find("Coastal water - luminous river and sea")!=null);
            Check("hero-tree",GameObject.Find("Mature inspection tree")!=null);
            Check("blue-giant",NamedPrefix("Blue gas giant")!=null);
            Check("galaxy",GameObject.Find("Distant galaxy - procedural dust and stellar band")!=null);
            Vector3 before=explorer.CameraPosition;
            for(int i=0;i<12;i++){explorer.SmokeStep(Vector3.forward,false,.05f);yield return null;}
            Check("walk-input-route",Vector3.Distance(before,explorer.CameraPosition)>.25f);
            Check("inside-bounds",explorer.CameraPosition.x>=CoastalTerrain.MinX&&explorer.CameraPosition.x<=CoastalTerrain.MaxX&&explorer.CameraPosition.z>=CoastalTerrain.MinZ&&explorer.CameraPosition.z<=CoastalTerrain.MaxZ);
            string json="{\n  \"status\": \"PASS\",\n  \"scope\": \"Actual built coastal player scene and controller route; hidden smoke, not user-visible native input acceptance.\",\n  \"checks\": [\""+string.Join("\", \"",checks)+"\"]\n}";
            File.WriteAllText(Path.Combine(directory,"coastal-runtime-smoke.json"),json);
            Application.Quit(0);
        }
        int CountRocks(){int count=0;foreach(var c in FindObjectsByType<MeshCollider>())if(c.name.StartsWith("Stratified shore rock ",StringComparison.Ordinal))count++;return count;}
        GameObject NamedPrefix(string prefix){foreach(var t in FindObjectsByType<Transform>())if(t.name.StartsWith(prefix,StringComparison.Ordinal))return t.gameObject;return null;}
        void Check(string name,bool pass){if(!pass)throw new InvalidOperationException("Coastal smoke failed: "+name);checks.Add(name);}
    }
}
