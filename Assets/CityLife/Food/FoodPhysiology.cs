using System;
using System.Collections.Generic;
using UnityEngine;
namespace Starfall.Food
{
    [Serializable]public sealed class FoodBody
    {
        public int stomach=2500,fat=5000,protein=6000,health=10000,fatigue=1000,seconds,deficitSeconds,drySeconds,lowProteinSeconds,surplusSeconds,submergedSeconds;
        public bool active,resting,sheltered=true,dead,submerged;
        public string cause="";
    }
    [Serializable]public sealed class FoodDeath
    {
        public string id,world,generation,actor,cause,lesson,previousHash,hash;
        public int tick,incarnation,energy,hydration,fat,health,deficitSeconds,drySeconds;
    }
    [Serializable]public sealed class RecoveryBag
    {public int death,berries,seeds;public string owner;}
    public static class FoodPhysiology
    {
        // Fictional game units and deliberately generous pacing, not clinical nutrition.
        public static void Step(FoodBody b,ref int energy,ref int hydration)
        {
            if(b.dead)return;b.seconds++;b.stomach=Math.Max(0,b.stomach-4);
            int expenditure=2+(b.active?3:0);int used=Math.Min(energy,expenditure);energy-=used;
            if(used<expenditure)b.fat=Math.Max(0,b.fat-(expenditure-used));
            hydration=Math.Max(0,hydration-(b.active?6:4));
            if(energy>8000){b.surplusSeconds++;if(b.surplusSeconds>=60&&b.fat<10000){int gain=Math.Min(50,10000-b.fat);energy-=gain;b.fat+=gain;b.surplusSeconds=0;}}else b.surplusSeconds=0;
            if(b.seconds%60==0)b.protein=Math.Max(0,b.protein-1);
            b.lowProteinSeconds=b.protein<2000?b.lowProteinSeconds+1:0;
            b.fatigue=Math.Clamp(b.fatigue+(b.resting?(b.sheltered?-4:-2):b.active?2:0),0,10000);
            b.deficitSeconds=energy==0&&b.fat==0?b.deficitSeconds+1:0;b.drySeconds=hydration==0?b.drySeconds+1:0;
            b.submergedSeconds=b.submerged?b.submergedSeconds+1:Math.Max(0,b.submergedSeconds-3);
            if(b.submergedSeconds>15){b.health=Math.Max(0,b.health-200);b.cause="drowning";}
            else if(b.drySeconds>900){b.health=Math.Max(0,b.health-2);b.cause="prolonged-dehydration";}
            else if(b.deficitSeconds>3600){b.health=Math.Max(0,b.health-1);b.cause="prolonged-starvation";}
            else if(b.resting&&b.sheltered&&energy>2000&&hydration>2000&&b.lowProteinSeconds<3600&&b.seconds%10==0)b.health=Math.Min(10000,b.health+1);
            if(b.health==0)b.dead=true;
        }
        public static float MovementFactor(FoodBody b,int energy)=>b.dead?0:b.deficitSeconds>600?.55f:b.fatigue>8000?.75f:energy<1000&&b.fat<1000?.85f:1;
        public static bool Valid(FoodBody b)=>b!=null&&b.stomach>=0&&b.stomach<=10000&&b.fat>=0&&b.fat<=10000&&b.protein>=0&&b.protein<=10000&&b.health>=0&&b.health<=10000&&b.fatigue>=0&&b.fatigue<=10000&&b.seconds>=0&&b.deficitSeconds>=0&&b.drySeconds>=0&&b.lowProteinSeconds>=0&&b.surplusSeconds>=0&&b.submergedSeconds>=0&&b.dead==(b.health==0);
        public static List<string> Checks()
        {
            var checks=new List<string>();void C(bool v,string n){if(!v)throw new Exception("PHYSIOLOGY: "+n);checks.Add(n);}
            var b=new FoodBody();int e=6500,h=5500;for(int i=0;i<600;i++)Step(b,ref e,ref h);C(!b.dead&&b.health==10000,"one missed meal is not fatal");
            var idle=new FoodBody();var active=new FoodBody{active=true};int ei=6500,ea=6500,hi=10000,ha=10000;for(int i=0;i<60;i++){Step(idle,ref ei,ref hi);Step(active,ref ea,ref ha);}C(ea<ei&&ha<hi,"activity spends energy and water");
            b=new FoodBody();e=1;h=10000;Step(b,ref e,ref h);C(b.fat==4999&&b.health==10000,"fat funds deficit before health loss");
            b=new FoodBody();e=9500;h=10000;for(int i=0;i<60;i++)Step(b,ref e,ref h);C(b.fat>5000,"sustained surplus slowly stores fat");
            b=new FoodBody{resting=true,health=9000,fatigue=4000};e=h=10000;for(int i=0;i<60;i++)Step(b,ref e,ref h);C(b.health>9000&&b.fatigue<4000,"sheltered rest restores health and fatigue");
            b=new FoodBody{protein=0};e=h=10000;for(int i=0;i<600;i++)Step(b,ref e,ref h);C(b.health==10000&&!b.dead,"low protein is not an instant death meter");
            b=new FoodBody{fat=0};e=0;h=10000;for(int i=0;i<15000;i++){h=10000;Step(b,ref e,ref h);}C(b.dead&&b.cause=="prolonged-starvation","eventual prolonged starvation");
            b=new FoodBody();e=10000;h=0;for(int i=0;i<7000;i++){e=10000;Step(b,ref e,ref h);}C(b.dead&&b.cause=="prolonged-dehydration","eventual prolonged dehydration");
            var drown=new FoodBody{submerged=true};int ed=10000,hd=10000;
            for(int i=0;i<15;i++)Step(drown,ref ed,ref hd);
            C(!drown.dead&&drown.health==10000,"15 seconds underwater breath does not immediately damage health");
            for(int i=0;i<50;i++)Step(drown,ref ed,ref hd);
            C(drown.dead&&drown.cause=="drowning","eventual drowning when submerged without air");
            var surfaced=new FoodBody{submerged=true};
            for(int i=0;i<10;i++)Step(surfaced,ref ed,ref hd);
            surfaced.submerged=false;
            for(int i=0;i<4;i++)Step(surfaced,ref ed,ref hd);
            C(surfaced.submergedSeconds==0,"surfacing rapidly recovers breath capacity");
            return checks;
        }
    }
}
