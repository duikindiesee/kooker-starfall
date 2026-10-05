using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starfall.EnvironmentFoundation
{
    public static class EnvironmentChecks
    {
        public static List<string> Run()
        {
            var checks = new List<string>();
            void Check(bool ok, string name) { if (!ok) throw new Exception("ENV_CHECK_FAILED " + name); checks.Add(name); }
            var a = new EnvironmentClock(1904243); var b = new EnvironmentClock(1904243);
            bool divergent = false;
            for (int i=0;i<12000;i++)
            {
                a.Step(); b.Step(); var x=a.Sample; var y=b.Sample;
                Check(x.wind == y.wind && x.temperature == y.temperature,"repeat:"+i);
                if(i%250==0) { var other=new EnvironmentClock(42); other.Tick=a.Tick; divergent |= other.Sample.wind != x.wind; }
                if (!EnvironmentClock.Finite(x.wind.x) || x.wind.magnitude>30.001f || x.precipitation<0 || x.precipitation>1)
                    throw new Exception("invalid sample at "+i);
            }
            Check(divergent,"different seed diverges");
            a.Paused=true; long tick=a.Tick; a.Step(); Check(a.Tick==tick,"pause freezes ticks"); a.Paused=false;
            var snapshot=a.Save("fixture", "v1", .6f); var restored=new EnvironmentClock(1);
            Check(restored.TryLoad(snapshot,"fixture","v1"),"load valid");
            for(int i=0;i<400;i++){a.Step();restored.Step();Check(a.Sample.wind==restored.Sample.wind,"continuation:"+i);}
            long retained=restored.Tick; snapshot.tick=-1;
            Check(!restored.TryLoad(snapshot,"fixture","v1") && restored.Tick==retained,"reject invalid atomically");
            snapshot=a.Save("other","v1",0);Check(!restored.TryLoad(snapshot,"fixture","v1"),"reject other world");
            float maxJump=0;
            for(int i=1;i<6500;i++){a.Tick=i-1;var before=a.Sample;a.Tick=i;maxJump=Mathf.Max(maxJump,(a.Sample.wind-before.wind).magnitude);}
            Check(maxJump<.3f,"wind transitions bounded per tick");
            var coast=new CoastalEnvironmentSurface();
            Check(coast.Contains(Vector3.zero) && !coast.Contains(new Vector3(9999,0,0)),"finite coastal bounds");
            Check(coast.WaterDepth(Vector3.zero)==0 && coast.WaterDepth(new Vector3(0,0,140))>0,"coastal dry pad and sea");
            var exposure=new ExposureState();for(int i=0;i<1000;i++)exposure.Step(-10,15,1,false);
            Check(exposure.Cold && exposure.Wetness>0,"cold wet signaling");
            for(int i=0;i<3000;i++)exposure.Step(18,0,0,true);
            Check(!exposure.Cold && exposure.Wetness<.01f,"shelter recovery");
            return checks;
        }
    }
}
