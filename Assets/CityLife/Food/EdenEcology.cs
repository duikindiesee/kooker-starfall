using System;
using System.Collections.Generic;
namespace Starfall.Food
{
    [Serializable]public sealed class EdenBush
    {public int site,stock=2,progress,ripeAge,growth=60,stress;public bool dead;}
    [Serializable]public sealed class EdenSeed
    {public int id,site,age;public string outcome="dropped";}
    [Serializable]public sealed class EdenEvent
    {public int sequence,tick;public string kind,target,detail;}
    public static class EdenEcology
    {
        public const int Regrow=30,Mature=60,FruitLifetime=90,SeedLifetime=40,PopulationCap=4;
        public static uint RandomKey(int seed,int id){uint x=unchecked((uint)seed^(uint)id*747796405u);x^=x>>16;x*=2246822519u;x^=x>>13;return x;}
        public static int Living(FoodState s){int n=s.primaryDead?0:1;foreach(var b in s.bushes)if(!b.dead)n++;if(s.gardenStage>0&&s.gardenStage<4)n++;return n;}
        public static bool Suitable(FoodState s)=>s.wetSeason&&s.soilWater>=20&&s.temperatureC>=10&&s.temperatureC<=35;
        public static void Event(FoodState s,string kind,string target,string detail)
        {
            if(s.ecologyEvents.Count>=24)s.ecologyEvents.RemoveAt(0);
            s.ecologyEvents.Add(new EdenEvent{sequence=++s.ecologySequence,tick=s.tick,kind=kind,target=target,detail=detail});
        }
        public static void Drop(FoodState s,int site)
        {
            if(s.drops.Count>=8){Event(s,"decay","berry","Seed budget full; fallen fruit decayed.");return;}
            int id=++s.nextSeed;int destination=2+(int)((RandomKey(s.seed,id)>>8)%2);
            s.drops.Add(new EdenSeed{id=id,site=destination});Event(s,"seed-drop","berry",$"Fallen fruit released seed {id} near site {destination}; finite lifespan {SeedLifetime}s.");
        }
        static void GrowBush(FoodState s,string id,ref int stock,ref int progress,ref int age,ref int stress,ref bool dead,int capacity)
        {
            if(dead)return;
            bool suitable=Suitable(s);
            stress=s.temperatureC<0||s.soilWater<10?stress+1:0;
            if(stress>=180){dead=true;stock=0;progress=age=0;Event(s,"plant-death",id,"Prolonged severe cold/drought killed mature bush.");return;}
            if(stock>0&&++age>=FruitLifetime){stock--;age=0;Drop(s,0);Event(s,"fruit-expired",id,"One unharvested ripe berry fell; stock reduced.");}
            if(stock<capacity&&suitable&&++progress>=Regrow){stock++;progress=0;s.soilWater-=2;Event(s,"regrowth",id,"One berry completed bud → ripening → ripe; water consumed.");}
        }
        public static void Step(FoodState s)
        {
            GrowBush(s,"berry",ref s.fruitStock,ref s.regrowthProgress,ref s.ripeAge,ref s.primaryStress,ref s.primaryDead,2+(int)((uint)s.seed%2));
            foreach(var b in s.bushes)
            {
                if(b.dead)continue;
                string id="berry"+(b.site+1);
                if(b.growth<Mature)
                {
                    if(Suitable(s)){b.growth++;b.stress=0;if(b.growth==Mature)Event(s,"mature",id,"Observed seedling became mature bush; fruit must still grow.");}
                    else if(s.temperatureC<0||s.soilWater<10){if(++b.stress>=60){b.dead=true;Event(s,"plant-death",id,"Seedling failed under prolonged stress.");}}
                }
                else GrowBush(s,id,ref b.stock,ref b.progress,ref b.ripeAge,ref b.stress,ref b.dead,2);
            }
            if(s.gardenStage>0&&s.gardenStage<4)
            {
                if(Suitable(s)){s.gardenStress=0;if(s.gardenGrowth<Mature)s.gardenGrowth++;s.gardenStage=s.gardenGrowth>=Mature?3:s.gardenGrowth>=Mature/2?2:1;if(s.gardenStage==3)s.gardenEstablished=true;}
                else if(s.temperatureC<0||s.soilWater<10){if(++s.gardenStress>=60){s.gardenStage=4;Event(s,"plant-death","bed","Cultivated seedling died; replant or request camp aid.");}}
                if(s.gardenStage==3&&++s.gardenRipeAge>=FruitLifetime){Drop(s,0);s.gardenRipeAge=s.gardenGrowth=0;s.gardenStage=1;Event(s,"fruit-expired","bed","Unharvested garden fruit fell; established plant remains.");}
            }
            foreach(var d in s.drops)
            {
                d.age++;
                if(d.outcome=="dropped"&&d.age==30)
                {
                    bool occupied=s.bushes.Exists(x=>x.site==d.site);bool success=RandomKey(s.seed,d.id)%12==0&&d.site==2&&Suitable(s)&&s.soilWater>=40&&!occupied&&Living(s)<PopulationCap&&s.lastNaturalSeason!=s.tick/300;
                    if(success){s.bushes.Add(new EdenBush{site=d.site,stock=0,growth=0});s.soilWater-=20;s.lastNaturalSeason=s.tick/300;d.outcome="germinated";Event(s,"germination","berry"+(d.site+1),$"Seed {d.id} germinated on moist spaced soil; population and water budget consumed.");}
                    else{d.outcome="failed";Event(s,"seed-failed","berry","Seed failed viability, habitat, spacing or population gate.");}
                }
            }
            s.drops.RemoveAll(d=>d.age>=SeedLifetime);
        }
    }
}
