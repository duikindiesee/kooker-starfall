using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World
{
    // Opt-in normal-player footage. Captures the rendered game view + HUD,
    // never the desktop/window compositor. Real elapsed timestamps are kept;
    // neither Time.captureFramerate nor simulation time is changed.
    public sealed class StarfallSurvivalGameCapture : MonoBehaviour
    {
        [Serializable] sealed class FrameRow
        {
            public int frame, unityTick, width, height;
            public long utcElapsedMs;
            public string sourceCamera, cameraMode, readbackStatus;
        }
        [Serializable] sealed class FinalRow
        {
            public string schema="starfall.game-frames.v1", status, build, sourceCamera, boundary, scenicSiteEvidence;
            public int frameCount, droppedCount, skippedSamples, width, height, requestedSeconds, requestedStartTick, actualStartTick;
            public long firstElapsedMs, lastElapsedMs, captureEndMs;
            public bool scenicRequested, scenicAvailable, scenicRendered;
        }
        public NpcAutonomy Brain;
        public Camera View;
        const long IntervalMs=125;
        string directory, framesPath, finalPath, build;
        int seconds, width, height, frameCount, dropped, skipped, requestedStartTick, actualStartTick;
        long firstMs=-1, lastMs=-1, filmedEndMs;
        bool inFlight, finalized, failed, scenicRequested, scenicAvailable, scenicActive, scenicRendered;
        Vector3 scenicEye;
        string scenicSiteEvidence;
        CharacterPreviewCamera follow;
        NpcPlayerControls controls;
        Stopwatch clock;

        static string Flag(string name)
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]==name)return args[i+1];
            return null;
        }
        static bool HasFlag(string name)
        {
            foreach(string arg in Environment.GetCommandLineArgs())if(arg==name)return true;
            return false;
        }
        IEnumerator Start()
        {
            string target=Flag("-npcSurvivalCaptureFrames");
            if(string.IsNullOrEmpty(target))yield break;
            if(Flag("-npcSurvivalModel")==null||HasFlag("-npcSurvivalDeathAcceptance")||
                Brain==null||View==null)yield break;
            if(!int.TryParse(Flag("-npcSurvivalCaptureSeconds"),out seconds)||seconds<10||seconds>120)
                yield break;
            string startTick=Flag("-npcSurvivalCaptureAfterTick");
            if(startTick!=null && (!int.TryParse(startTick,out requestedStartTick)||requestedStartTick<0||requestedStartTick>50000))
                yield break;
            directory=Path.GetFullPath(target);
            if(!Directory.Exists(directory)||Directory.GetFileSystemEntries(directory).Length!=0)yield break;
            framesPath=Path.Combine(directory,"frames.jsonl");finalPath=Path.Combine(directory,"capture.json");
            build=Flag("-npcMemoryBuild")??Application.version;
            width=Screen.width;height=Screen.height;
            if(width<320||height<240||(width&1)!=0||(height&1)!=0||
                !SystemInfo.supportsAsyncGPUReadback)yield break;
            // Delay only the optional film, not the simulation. Brain keeps
            // its normal compiled autonomous decisions; the receipt records
            // both requested and observed start ticks.
            while(Brain.Tick<requestedStartTick)yield return null;
            actualStartTick=Brain.Tick;
            scenicRequested=HasFlag("-npcSurvivalCaptureScenic");
            if(scenicRequested && seconds>=80)
            {
                follow=View.GetComponent<CharacterPreviewCamera>();
                controls=View.GetComponent<NpcPlayerControls>();
                scenicAvailable=follow!=null && controls!=null && !follow.ExternalView && !Brain.Possessed &&
                    CoastalSkyViewSites.TryDryEyeNear(-52,-55,View.nearClipPlane,out scenicEye,out scenicSiteEvidence);
                if(!scenicAvailable)scenicSiteEvidence="Scenic observer refused: missing follow/controls, possession/external view, or no dry clear PhysX bank site.";
            }
            clock=Stopwatch.StartNew();
            long nextSampleMs=0;
            while(!failed)
            {
                if(scenicAvailable)SetScenic(clock.ElapsedMilliseconds>=40000 && clock.ElapsedMilliseconds<60000);
                yield return new WaitForEndOfFrame();
                if(Screen.width!=width||Screen.height!=height){failed=true;break;}
                long elapsed=clock.ElapsedMilliseconds;
                if(elapsed>=seconds*1000L)break;
                if(elapsed<nextSampleMs)continue;
                nextSampleMs=elapsed+IntervalMs;
                if(inFlight){skipped++;continue;}
                int capturedTick=Brain.Tick;
                string capturedMode=controls!=null&&controls.FreeSpectator?"player-free-spectator":
                    scenicActive?"scripted-scenic-observer; actor-autonomous":"ordinary-actor-follow";
                var texture=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32);
                texture.Create();inFlight=true;
                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(texture);
                    AsyncGPUReadback.Request(texture,0,TextureFormat.RGBA32,
                        request=>Completed(request,texture,elapsed,capturedTick,capturedMode));
                }
                catch(Exception)
                {
                    Destroy(texture);inFlight=false;dropped++;failed=true;
                }
            }
            SetScenic(false);
            filmedEndMs=clock.ElapsedMilliseconds;
            long waitFrom=clock.ElapsedMilliseconds;
            while(inFlight&&clock.ElapsedMilliseconds-waitFrom<2000)
                yield return null;
            if(inFlight){dropped++;failed=true;}
            Finish(failed?"FAILED_OR_INCOMPLETE":"CAPTURED_REAL_GAME_FRAMES");
        }
        void Completed(AsyncGPUReadbackRequest request,RenderTexture source,long elapsed,int tick,string cameraMode)
        {
            try
            {
                if(finalized)return;
                if(request.hasError) {dropped++;failed=true;return;}
                var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
                try
                {
                    // Unity's GPU readback starts at the opposite vertical
                    // origin from the PNG image. The first compiled frame was
                    // visibly upside down; correct row order here, preserving
                    // the original framebuffer pixels and timestamp.
                    byte[] pixels=request.GetData<byte>().ToArray();
                    int stride=width*4;var swap=new byte[stride];
                    for(int y=0;y<height/2;y++)
                    {
                        int upper=y*stride,lower=(height-1-y)*stride;
                        Buffer.BlockCopy(pixels,upper,swap,0,stride);
                        Buffer.BlockCopy(pixels,lower,pixels,upper,stride);
                        Buffer.BlockCopy(swap,0,pixels,lower,stride);
                    }
                    image.LoadRawTextureData(pixels);
                    image.Apply(false,false);
                    int number=frameCount+1;
                    string path=Path.Combine(directory,"frame-"+number.ToString("D6")+".png");
                    File.WriteAllBytes(path,image.EncodeToPNG());
                    var row=new FrameRow{frame=number,utcElapsedMs=elapsed,unityTick=tick,
                        width=width,height=height,sourceCamera=View.name,cameraMode=cameraMode,readbackStatus="ok"};
                    File.AppendAllText(framesPath,JsonUtility.ToJson(row)+"\n");
                    if(cameraMode.StartsWith("scripted",StringComparison.Ordinal))scenicRendered=true;
                    frameCount=number;if(firstMs<0)firstMs=elapsed;lastMs=elapsed;
                }
                finally{Destroy(image);}
            }
            catch(Exception){dropped++;failed=true;}
            finally{Destroy(source);inFlight=false;}
        }
        void Finish(string status)
        {
            if(finalized||string.IsNullOrEmpty(finalPath))return;
            finalized=true;
            var row=new FinalRow{status=status,build=build,sourceCamera=View==null?"unknown":View.name,
                frameCount=frameCount,droppedCount=dropped,skippedSamples=skipped,width=width,height=height,
                scenicRequested=scenicRequested,scenicAvailable=scenicAvailable,scenicRendered=scenicRendered,
                scenicSiteEvidence=scenicSiteEvidence,
                requestedSeconds=seconds,requestedStartTick=requestedStartTick,actualStartTick=actualStartTick,
                firstElapsedMs=firstMs,lastElapsedMs=lastMs,
                captureEndMs=filmedEndMs>0?filmedEndMs:clock==null?0:clock.ElapsedMilliseconds,
                boundary="Actual game framebuffer and compact HUD only; optional start waits for ordinary autonomous tick without pausing simulation. Scenic interval 40-60s, if requested/available, is a SCRIPTED observer camera from a dry PhysX bank; the actor keeps autonomous decisions but is not seen in the scenic shot. Camera returns to follow unless the player selected free spectator. Real elapsed samples; skipped busy slots/dropped readback errors. No desktop capture or simulated pacing."};
            try{File.WriteAllText(finalPath,JsonUtility.ToJson(row,true));}catch(Exception){}
        }
        void OnApplicationQuit(){SetScenic(false);Finish("PLAYER_EXITED_BEFORE_CAPTURE_COMPLETE");}
        void OnDisable(){SetScenic(false);}
        void SetScenic(bool on)
        {
            if(!scenicAvailable||follow==null||controls==null||View==null||scenicActive==on)return;
            if(on && (follow.ExternalView||Brain.Possessed||controls.FreeSpectator))
            {
                scenicAvailable=false;
                scenicSiteEvidence="Scenic observer refused: player changed camera or possessed actor before the scripted interval.";
                return;
            }
            scenicActive=on;
            controls.ScriptedScenicCapture=on;
            if(!on && controls.FreeSpectator)return;
            follow.ExternalView=on;
            if(on)
            {
                follow.ReleasePointer();
                View.transform.position=scenicEye;
                View.transform.rotation=Quaternion.LookRotation(new Vector3(.04f,.07f,1).normalized);
            }
            else follow.Follow();
        }
    }
}
