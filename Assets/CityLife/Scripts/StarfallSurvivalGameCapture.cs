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
            public string sourceCamera, readbackStatus;
        }
        [Serializable] sealed class FinalRow
        {
            public string schema="starfall.game-frames.v1", status, build, sourceCamera, boundary;
            public int frameCount, droppedCount, skippedSamples, width, height, requestedSeconds;
            public long firstElapsedMs, lastElapsedMs, captureEndMs;
        }
        public NpcAutonomy Brain;
        public Camera View;
        const long IntervalMs=125;
        string directory, framesPath, finalPath, build;
        int seconds, width, height, frameCount, dropped, skipped;
        long firstMs=-1, lastMs=-1, filmedEndMs;
        bool inFlight, finalized, failed;
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
            directory=Path.GetFullPath(target);
            if(!Directory.Exists(directory)||Directory.GetFileSystemEntries(directory).Length!=0)yield break;
            framesPath=Path.Combine(directory,"frames.jsonl");finalPath=Path.Combine(directory,"capture.json");
            build=Flag("-npcMemoryBuild")??Application.version;
            width=Screen.width;height=Screen.height;
            if(width<320||height<240||(width&1)!=0||(height&1)!=0||
                !SystemInfo.supportsAsyncGPUReadback)yield break;
            clock=Stopwatch.StartNew();
            long nextSampleMs=0;
            while(!failed)
            {
                yield return new WaitForEndOfFrame();
                if(Screen.width!=width||Screen.height!=height){failed=true;break;}
                long elapsed=clock.ElapsedMilliseconds;
                if(elapsed>=seconds*1000L)break;
                if(elapsed<nextSampleMs)continue;
                nextSampleMs=elapsed+IntervalMs;
                if(inFlight){skipped++;continue;}
                int capturedTick=Brain.Tick;
                var texture=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32);
                texture.Create();inFlight=true;
                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(texture);
                    AsyncGPUReadback.Request(texture,0,TextureFormat.RGBA32,
                        request=>Completed(request,texture,elapsed,capturedTick));
                }
                catch(Exception)
                {
                    Destroy(texture);inFlight=false;dropped++;failed=true;
                }
            }
            filmedEndMs=clock.ElapsedMilliseconds;
            long waitFrom=clock.ElapsedMilliseconds;
            while(inFlight&&clock.ElapsedMilliseconds-waitFrom<2000)
                yield return null;
            if(inFlight){dropped++;failed=true;}
            Finish(failed?"FAILED_OR_INCOMPLETE":"CAPTURED_REAL_GAME_FRAMES");
        }
        void Completed(AsyncGPUReadbackRequest request,RenderTexture source,long elapsed,int tick)
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
                        width=width,height=height,sourceCamera=View.name,readbackStatus="ok"};
                    File.AppendAllText(framesPath,JsonUtility.ToJson(row)+"\n");
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
                requestedSeconds=seconds,firstElapsedMs=firstMs,lastElapsedMs=lastMs,
                captureEndMs=filmedEndMs>0?filmedEndMs:clock==null?0:clock.ElapsedMilliseconds,
                boundary="Actual game framebuffer and HUD only; real elapsed samples. Skipped are busy capture slots, dropped are readback/write errors. Gaps hold prior image; no desktop capture or simulated pacing."};
            try{File.WriteAllText(finalPath,JsonUtility.ToJson(row,true));}catch(Exception){}
        }
        void OnApplicationQuit(){Finish("PLAYER_EXITED_BEFORE_CAPTURE_COMPLETE");}
    }
}
