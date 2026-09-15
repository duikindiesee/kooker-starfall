using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Starfall.Food;

namespace CityLife.World
{
    // An explicit compiled-player diagnostic, never ordinary pacing. It waits
    // for a real model-driven meal, then accelerates measured need conditions
    // through the SAME FoodPhysiology.Step branch before testing safe return.
    public sealed class StarfallSurvivalDeathAcceptance : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public StarfallSurvivalAutonomy Survival;
        public IntegratedFoodRuntime Food;
        public Camera View;
        [Serializable] private sealed class Case
        {
            public string cause,lesson,deathHash,returnCode;
            public int incarnationBefore,incarnationAfter,worldTickBefore,worldTickAfter,bagCount;
            public bool realPhysiologyDeath,safeRefugeReturn,scopedReload,worldAndActorPreserved,noInventedKnowledge;
            public float returnX,returnY,returnZ;
        }
        [Serializable] private sealed class Report
        {
            public string status="FAIL",scope="Explicit accelerated-physiology diagnostic in compiled integrated player; NOT natural timeline death",world,actor,build;
            public bool actualModelMealBeforeProbe,geometryPreserved;
            public int modelFoodOutcomes,ordinaryActionDeliveries;
            public List<Case> cases=new List<Case>();
        }
        private IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"-npcSurvivalDeathAcceptance");
            if(flag<0)yield break;
            int at=Array.IndexOf(args,"-npcSurvivalDeathEvidence");
            if(at<0||at+1>=args.Length||!Path.IsPathFullyQualified(args[at+1]))yield break;
            string folder=args[at+1];
            if(Directory.Exists(folder)&&Directory.GetFileSystemEntries(folder).Length!=0)yield break;
            Directory.CreateDirectory(folder);
            var report=new Report{build=Application.version};
            float deadline=Time.realtimeSinceStartup+600;
            while((Brain==null||!Brain.Ready||Survival==null||!Survival.Enabled||
                Food==null||string.IsNullOrEmpty(Food.Model.State.lastMealEvidence))&&
                Time.realtimeSinceStartup<deadline)yield return null;
            if(Brain==null||Survival==null||Food==null||!Survival.Enabled||
                string.IsNullOrEmpty(Food.Model.State.lastMealEvidence))
            {
                File.WriteAllText(Path.Combine(folder,"death-diagnostic.json"),JsonUtility.ToJson(report,true));
                Application.Quit();yield break;
            }
            var s=Food.Model.State;report.world=s.world;report.actor=s.actorId;
            report.actualModelMealBeforeProbe=Survival.FoodOutcomes>0&&s.knowsMealBenefit&&
                s.lastMealEvidence.StartsWith(s.generation+".ate.",StringComparison.Ordinal);
            report.modelFoodOutcomes=Survival.FoodOutcomes;report.ordinaryActionDeliveries=Brain.Actions.Deliveries;
            Vector3 berry=Food.Berry.transform.position;
            string revision=Brain.TerrainNavigation.Revision;
            Brain.Pause();Survival.Cancel("explicit-death-diagnostic");
            yield return Capture(folder,"00-real-model-meal-before-accelerated-probe.png");
            // The probe sets measured precursor counters/need and one health
            // point. Only FixedStep -> FoodPhysiology.Step may set dead/cause.
            for(int variant=0;variant<2;variant++)
            {
                s=Food.Model.State;var result=new Case{incarnationBefore=s.incarnation,worldTickBefore=s.tick};
                s.body.health=1;
                if(variant==0)
                {
                    s.hydration=0;s.satiety=10000;s.body.drySeconds=901;s.body.deficitSeconds=0;
                    result.cause="prolonged-dehydration";
                }
                else
                {
                    s.hydration=10000;s.satiety=0;s.body.fat=0;s.body.drySeconds=0;s.body.deficitSeconds=3601;
                    result.cause="prolonged-starvation";
                }
                for(int tick=0;tick<50&&!s.body.dead;tick++)Food.Model.FixedStep(false);
                result.realPhysiologyDeath=s.body.dead&&s.body.cause==result.cause&&
                    s.deaths.Count==variant+1&&s.deaths[variant].cause==result.cause;
                if(result.realPhysiologyDeath)
                {
                    result.lesson=s.deaths[variant].lesson;result.deathHash=s.deaths[variant].hash;
                    result.bagCount=s.bags.Count;
                    Brain.Actor.DeadPose=true;
                    Brain.Actor.Step(Vector3.zero,NpcAutonomy.StepSeconds);
                    for(int settle=0;settle<12;settle++)yield return null;
                    yield return Capture(folder,variant==0?"01-measured-dehydration-death.png":"03-measured-starvation-death.png");
                    result.safeRefugeReturn=Survival.DiagnosticSafeReturn();
                    result.scopedReload=Survival.DiagnosticVerifyReload();
                    result.returnCode=result.safeRefugeReturn?"verified-safe-refuge-landing":"no-safe-return";
                    result.incarnationAfter=s.incarnation;result.worldTickAfter=s.tick;
                    result.returnX=Brain.transform.position.x;result.returnY=Brain.transform.position.y;result.returnZ=Brain.transform.position.z;
                    result.worldAndActorPreserved=s.world==report.world&&s.actorId==report.actor&&
                        Food.Berry.transform.position==berry&&Brain.TerrainNavigation.Revision==revision&&
                        s.deaths[variant].hash==result.deathHash&&s.tick>=result.worldTickBefore;
                    result.noInventedKnowledge=(!s.knowsPlanting)&&s.knowsMealBenefit&&
                        s.lastMealEvidence.StartsWith(s.generation+".ate.",StringComparison.Ordinal)&&
                        result.lesson==(variant==0?"Hydration remained depleted before fatal damage.":"Energy and fat were exhausted before fatal damage.");
                    yield return Capture(folder,variant==0?"02-safe-return-after-dehydration.png":"04-safe-return-after-starvation.png");
                }
                report.cases.Add(result);
                if(!result.realPhysiologyDeath||!result.safeRefugeReturn)break;
            }
            report.geometryPreserved=Food.Berry.transform.position==berry&&Brain.TerrainNavigation.Revision==revision;
            report.status=report.actualModelMealBeforeProbe&&report.ordinaryActionDeliveries>=3&&
                report.cases.Count==2&&report.geometryPreserved&&report.cases.TrueForAll(c=>
                    c.realPhysiologyDeath&&c.safeRefugeReturn&&c.scopedReload&&c.worldAndActorPreserved&&c.noInventedKnowledge)
                ?"PASS_COMPILED_ACCELERATED_CAUSE_AND_SAFE_RETURN_NOT_NATURAL_PACING":"FAIL";
            File.WriteAllText(Path.Combine(folder,"death-diagnostic.json"),JsonUtility.ToJson(report,true));
            Application.Quit();
        }
        private IEnumerator Capture(string folder,string name)
        {
            yield return new WaitForEndOfFrame();
            if(View==null)yield break;
            Canvas.ForceUpdateCanvases();
            var buffer=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);
            buffer.Create();var previous=View.targetTexture;var active=RenderTexture.active;
            var driver=View.GetComponent<CharacterPreviewCamera>();
            bool priorExternal=driver!=null&&driver.ExternalView;
            Vector3 priorPosition=View.transform.position;Quaternion priorRotation=View.transform.rotation;
            try
            {
                if(name.Contains("safe-return")&&driver!=null&&Survival.Refuge!=null)
                {
                    // The ordinary follow camera shortens to 0.3 m against the
                    // cave wall and produces a face-only image. For evidence,
                    // hold a fixed view near the real ingress looking into the
                    // same landed inhabitant; restore user camera afterward.
                    driver.ExternalView=true;
                    View.transform.position=Survival.Refuge.OriginOffset+new Vector3(-6.15f,3.0f,-.2f);
                    View.transform.LookAt(Brain.transform.position+Vector3.up*1.2f);
                }
                View.targetTexture=buffer;View.Render();RenderTexture.active=buffer;
                var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(folder,name),image.EncodeToPNG());Destroy(image);
            }
            finally
            {
                View.targetTexture=previous;RenderTexture.active=active;
                View.transform.SetPositionAndRotation(priorPosition,priorRotation);
                if(driver!=null)driver.ExternalView=priorExternal;
                buffer.Release();Destroy(buffer);
            }
        }
    }
}
