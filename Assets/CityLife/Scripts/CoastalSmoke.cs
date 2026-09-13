using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace CityLife.World
{
    // Opt-in player-process checks. These do not establish native keyboard/mouse acceptance.
    public sealed class CoastalSmoke : MonoBehaviour
    {
        public static readonly bool Requested = Array.IndexOf(Environment.GetCommandLineArgs(), "-coastalSmoke") >= 0;
        [Serializable] sealed class Report
        {
            public string scope="Automated baked-player movement and collision checks; native input and visual acceptance pending", version;
            public bool passed, walking, flight, rockCollision;
            public float walkedMetres;
            public int rockColliders;
        }
        IEnumerator Start()
        {
            if(!Requested) yield break;
            var player=GetComponent<CoastalExplorer>();
            while(!player.SmokeReady) yield return null;
            string[] args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"-previewEvidence");
            if(index<0 || index+1>=args.Length || !Path.IsPathRooted(args[index+1])) { Application.Quit(4); yield break; }
            string directory=args[index+1];Directory.CreateDirectory(directory);
            var report=new Report{version=Application.version};
            Vector3 start=player.CameraPosition;
            player.SmokeAim(start+Vector3.left*20);
            for(int i=0;i<30;i++){player.SmokeStep(Vector3.forward,false,.05f);yield return null;}
            report.walkedMetres=Vector3.Distance(start,player.CameraPosition);
            report.walking=report.walkedMetres>2 && player.CurrentMode=="Walk";
            player.SmokeToggleMode();
            start=player.CameraPosition;
            for(int i=0;i<20;i++){player.SmokeStep(Vector3.up,false,.05f);yield return null;}
            report.flight=player.CurrentMode=="Fly" && player.CameraPosition.y>start.y+5;
            foreach(var collider in FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
            {
                if(collider.gameObject.layer==8 || collider.name=="Bark")continue;
                report.rockColliders++;
                // Approach the real rock from above with the same flight sphere sweep.
                if(report.rockCollision)continue;
                transform.position=new Vector3(collider.bounds.center.x,collider.bounds.max.y+2,collider.bounds.center.z);
                for(int i=0;i<15;i++)
                {
                    player.SmokeStep(Vector3.down,false,.05f);
                    if(player.SmokeLastSweepCollider==collider){report.rockCollision=true;break;}
                }
            }
            transform.position=new Vector3(21,4.6f,-25);player.SmokeAim(new Vector3(0,3.4f,14));
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,"coastal-player.png"));
            report.passed=report.walking && report.flight && report.rockCollision && report.rockColliders==84;
            File.WriteAllText(Path.Combine(directory,"coastal-runtime.json"),JsonUtility.ToJson(report,true));
            yield return new WaitForSeconds(2);
            Application.Quit(report.passed?0:3);
        }
    }
}
