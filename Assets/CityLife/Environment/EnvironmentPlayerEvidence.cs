using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Starfall.EnvironmentFoundation
{
    public sealed class EnvironmentPlayerEvidence : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public string contract=EnvironmentClock.Contract,layer="compiled-player scripted harness",utc,unity,gpu,cpu,status;
            public int width,height,modelAssertions,frames;public float p95ms,p99ms,stepP95ms,elapsedSeconds;
            public int minWidth,maxWidth,minHeight,maxHeight,rigidBodies,precipitationCapacity;
            public bool pausePassed,saveDiskPassed,windForcePassed,controllerGroundPassed,controllerMovePassed;
            public bool coldResponsePassed,windVisualPassed,transitionPassed;
            public List<WeatherObservation> weather=new List<WeatherObservation>();
            public List<EnvironmentPhysicsChecks.Result> physics;public string[] captures;
        }
        [Serializable] sealed class WeatherObservation {public long tick;public Vector3 wind,vegetationLean,arrow,shaderWind,probe;public float air,apparent,playerApparent,precipitation;public bool cold,playerCold,sheltered;}
        EnvironmentWorld world;readonly List<float> frames=new List<float>(),steps=new List<float>();readonly List<int> widths=new List<int>(),heights=new List<int>();bool recording;
        void Update(){if(recording){frames.Add(Time.unscaledDeltaTime*1000);steps.Add(world.LastStepMs);widths.Add(Screen.width);heights.Add(Screen.height);}}
        IEnumerator Start()
        {
            world=GetComponent<EnvironmentWorld>();yield return null;
            string[] args=System.Environment.GetCommandLineArgs();string folder=null;
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-environmentEvidence")folder=args[i+1];
            if(string.IsNullOrEmpty(folder)||!Path.IsPathRooted(folder)){Debug.LogError("Explicit absolute -environmentEvidence required");Application.Quit(2);yield break;}
            Directory.CreateDirectory(folder);var report=new Report{utc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,cpu=SystemInfo.processorType,width=Screen.width,height=Screen.height};
            try{report.modelAssertions=EnvironmentChecks.Run().Count;report.physics=EnvironmentPhysicsChecks.Run();}
            catch(Exception e){File.WriteAllText(Path.Combine(folder,"failure.txt"),e.ToString());Application.Quit(3);yield break;}
            yield return new WaitForSecondsRealtime(2);
            report.controllerGroundPassed=Mathf.Abs(world.Walker.transform.position.y)<.15f;
            Vector3 before=world.Walker.transform.position;world.Walker.InputDirection=Vector3.right;yield return new WaitForSecondsRealtime(1);world.Walker.InputDirection=Vector3.zero;
            report.controllerMovePassed=world.Walker.transform.position.x-before.x>3.5f&&Mathf.Abs(world.Walker.transform.position.y)<.2f;
            world.SetPause(true);yield return new WaitForEndOfFrame();long tick=world.Clock.Tick;Vector3 p=world.Walker.transform.position,visual=world.VisualPositionSignature;
            yield return new WaitForSecondsRealtime(.5f);
            report.pausePassed=world.Clock.Tick==tick&&world.Walker.transform.position==p&&world.VisualPositionSignature==visual;world.SetPause(false);
            string save=Path.Combine(folder,"synthetic-environment-save.json");var state=world.CaptureSave();EnvironmentClock.WriteSave(save,state);EnvironmentClock.WriteSave(save,state);
            world.Clock.Tick+=100;world.PlayerExposure.Wetness=.9f;
            report.saveDiskPassed=world.TryRestore(JsonUtility.FromJson<EnvironmentSave>(File.ReadAllText(save)))&&world.Clock.Tick==state.tick&&world.PlayerExposure.Wetness==state.playerWetness&&world.Exposure.Cold==state.cold&&File.Exists(save+".bak");
            var forceProbe=EnvironmentWorld.Cube("Wind force acceptance probe",new Vector3(-22,43,-7),Vector3.one*.3f,Color.red);var body=forceProbe.AddComponent<Rigidbody>();body.useGravity=false;var force=forceProbe.AddComponent<EnvironmentBody>();force.World=world;force.DragArea=.2f;
            yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();report.windForcePassed=Vector3.Dot(force.LastWindForce,world.Clock.Sample.wind)>0&&body.linearVelocity.magnitude>0;Destroy(forceProbe);
            report.captures=new[]{"clear.png","rain.png","cold.png","storm.png"};
            float begin=Time.realtimeSinceStartup;recording=true;
            report.windVisualPassed=true;report.transitionPassed=true;
            for(int phase=0;phase<4;phase++)
            {
                world.Clock.Tick=phase*1500;
                Vector3 transitionStart=world.Clock.Sample.wind;
                yield return new WaitForSecondsRealtime(2.5f);
                Vector3 transitionMid=world.Clock.Sample.wind;
                yield return new WaitForSecondsRealtime(5.5f);
                var s=world.Clock.Sample;
                if(phase>0)report.transitionPassed &= (transitionMid-transitionStart).magnitude>.1f && (transitionMid-s.wind).magnitude>.1f;
                Vector3 shaderWind=Shader.GetGlobalVector("_StarfallWind");
                report.windVisualPassed &= Vector3.Dot(world.VegetationLean,s.wind)>0 && Vector3.Dot(world.ArrowDirection,s.wind.normalized)>.99f && (shaderWind-s.wind).magnitude<.01f;
                report.weather.Add(new WeatherObservation{tick=world.Clock.Tick,wind=s.wind,air=s.temperature,precipitation=s.precipitation,apparent=world.Exposure.ApparentTemperature,playerApparent=world.PlayerExposure.ApparentTemperature,cold=world.Exposure.Cold,playerCold=world.PlayerExposure.Cold,sheltered=world.ProbeSheltered,probe=world.ProbePosition,vegetationLean=world.VegetationLean,arrow=world.ArrowDirection,shaderWind=shaderWind});
                if(phase==2)report.coldResponsePassed=world.Exposure.Cold&&world.PlayerExposure.Cold&&world.ProbeSheltered;
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,report.captures[phase]));
            }
            recording=false;report.elapsedSeconds=Time.realtimeSinceStartup-begin;report.frames=frames.Count;
            var csv=new System.Text.StringBuilder("frame,frame_ms,environment_step_ms,width,height\n");
            for(int i=0;i<frames.Count;i++)csv.Append(i).Append(',').Append(frames[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(steps[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(widths[i]).Append(',').Append(heights[i]).Append('\n');
            File.WriteAllText(Path.Combine(folder,"frame-times.csv"),csv.ToString());
            widths.Sort();heights.Sort();report.minWidth=widths[0];report.maxWidth=widths[widths.Count-1];report.minHeight=heights[0];report.maxHeight=heights[heights.Count-1];report.rigidBodies=world.BodyCount;report.precipitationCapacity=world.ParticleCount;
            frames.Sort();steps.Sort();report.p95ms=Percentile(frames,.95f);report.p99ms=Percentile(frames,.99f);report.stepP95ms=Percentile(steps,.95f);
            bool passed=report.pausePassed&&report.saveDiskPassed&&report.windForcePassed&&report.controllerGroundPassed&&report.controllerMovePassed&&report.coldResponsePassed&&report.windVisualPassed&&report.transitionPassed&&report.physics.TrueForAll(x=>x.passed)&&report.p95ms<=33.3f&&report.p99ms<=50&&report.stepP95ms<=2&&report.minWidth==1280&&report.maxWidth==1280&&report.minHeight==720&&report.maxHeight==720;
            report.status=passed?"PASS":"FAIL";File.WriteAllText(Path.Combine(folder,"player-report.json"),JsonUtility.ToJson(report,true));
            yield return new WaitForSecondsRealtime(1);Application.Quit(passed?0:4);
        }
        static float Percentile(List<float> samples,float q)=>samples.Count==0?float.PositiveInfinity:samples[Mathf.Min(samples.Count-1,(int)(samples.Count*q))];
    }
}
