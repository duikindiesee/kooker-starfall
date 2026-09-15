using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace Starfall.Food
{
    public static class FoodChecks
    {
        sealed class Access:IFoodAccess
        {
            public bool visible=true,reach=true,permission=true;
            public FoodAccess Inspect(string t)=>new FoodAccess{visible=visible,inReach=reach,permitted=permission,verifiedFreshwater=t=="spring"};
        }
        public static List<string> Run(string folder)
        {
            Directory.CreateDirectory(folder);var passed=new List<string>();
            void Check(bool yes,string name){if(!yes)throw new Exception("FOOD CHECK FAILED: "+name);passed.Add(name);}
            var a=new Access();var m=new FoodModel("test-a","generation-a",4242);
            FoodReceipt Do(FoodAction action,string target)=>m.Execute(m.State.world,m.State.generation,m.State.lastRequest+1,action,target,a);
            void Advance(int seconds){for(int i=0;i<seconds*50;i++)m.FixedStep(false);}
            Check(!Do(FoodAction.Gather,"berry").success,"unknown berry not edible/gatherable");
            var sensedChoices=new List<string>{"explore north","inspect berry"};
            Check(CityLife.World.StarfallSurvivalThought.Parse("Inspect berry",sensedChoices,out string sensedAction)&&sensedAction=="inspect berry"&&
                !CityLife.World.StarfallSurvivalThought.Parse("gather berry",sensedChoices,out _)&&
                !CityLife.World.StarfallSurvivalThought.Parse("inspect berry now",sensedChoices,out _),
                "survival thought admits only an eligible exact two-word action");
            string scopedPrompt=CityLife.World.StarfallSurvivalThought.BuildRequest("local-e4b",6500,5500,sensedChoices);
            Check(scopedPrompt.Contains("inspect berry")&&!scopedPrompt.Contains("126")&&!scopedPrompt.Contains("spring"),
                "survival prompt contains eligible observed action but no authored berry coordinate or unseen spring");
            string heldPrompt=CityLife.World.StarfallSurvivalThought.BuildRequest("local-e4b",6500,5500,
                new List<string>{"eat fruit","explore south"},1,false);
            Check(heldPrompt.Contains("6500/10000 below replenish target")&&heldPrompt.Contains("5500/10000 below replenish target")&&
                heldPrompt.Contains("carried fruit=1")&&heldPrompt.Contains("not observed")&&
                !heldPrompt.Contains("previously helped")&&!heldPrompt.Contains("spring")&&!heldPrompt.Contains("126"),
                "survival prompt states measured need and carried fruit without inventing a meal outcome or resource");
            string learnedPrompt=CityLife.World.StarfallSurvivalThought.BuildRequest("local-e4b",7700,5900,
                new List<string>{"eat fruit","explore south"},1,true);
            Check(learnedPrompt.Contains("previously helped")&&!learnedPrompt.Contains("not observed"),
                "meal outcome enters model context only after verified eating");
            string explorationPrompt=CityLife.World.StarfallSurvivalThought.BuildRequest("local-e4b",6500,5500,
                new List<string>{"explore south","explore east"},1,false);
            Check(explorationPrompt.Contains("explore south")&&explorationPrompt.Contains("explore east")&&
                !explorationPrompt.Contains("Energy=")&&!explorationPrompt.Contains("fruit")&&
                !explorationPrompt.Contains("spring"),
                "pure exploration request omits irrelevant need and unknown-resource context");
            m.State.satiety=0;Check(!Do(FoodAction.Eat,"inventory").success,"starvation does not grant food knowledge");m.State.satiety=6500;
            Check(Do(FoodAction.Inspect,"berry").success&&m.State.berryEvidence.Contains("observed-fruit")&&!m.State.knowsMealBenefit,"observed fruit does not grant a meal outcome");
            a.visible=false;Check(!Do(FoodAction.Gather,"berry").success,"occlusion blocks action");a.visible=true;
            a.reach=false;Check(!Do(FoodAction.Gather,"berry").success,"reach blocks action");a.reach=true;
            a.permission=false;Check(!Do(FoodAction.Gather,"berry").success,"permission blocks action");a.permission=true;
            var r=Do(FoodAction.Gather,"berry");int stock=m.State.fruitStock,items=m.State.carriedFruit;
            var repeated=m.Execute("test-a","generation-a",r.request,FoodAction.Gather,"berry",a);
            Check(repeated.duplicate&&m.State.fruitStock==stock&&m.State.carriedFruit==items,"duplicate harvest consumes one fruit only");
            Check(!m.Execute("test-a","generation-a",r.request,FoodAction.Drink,"spring",a).success,"request-id conflict rejected");
            int food=m.State.satiety;Check(Do(FoodAction.Eat,"inventory").success&&m.State.satiety==food+1200,"eating consumes inventory and restores satiety");
            Check(!Do(FoodAction.Drink,"spring").success,"source needs evidence");int hydration=m.State.hydration;
            Check(!Do(FoodAction.Drink,"sea").success&&m.State.hydration==hydration,"seawater has no nutrition effect");
            Check(Do(FoodAction.Inspect,"spring").success&&Do(FoodAction.Drink,"spring").success&&m.State.freshwaterMl==1750,"verified finite freshwater transfer");
            Check(!Do(FoodAction.Plant,"bed").success,"planting requires grounded lesson");Do(FoodAction.Inspect,"bed");
            Check(m.State.seenBed&&!m.State.knowsPlanting&&!Do(FoodAction.Plant,"bed").success,"seeing moist soil does not teach seed cultivation");
            // This separate ecology regression supplies explicit prior germination
            // evidence; the ordinary player does not receive it from a glance.
            m.State.knowsPlanting=true;m.State.plantingEvidence="generation-a.prior-verified-germination";
            int seedCount=m.State.seeds;
            Check(Do(FoodAction.Plant,"bed").success&&m.State.seeds==seedCount-1,"plant consumes a seed");
            Check(!Do(FoodAction.Plant,"bed").success&&!Do(FoodAction.Harvest,"bed").success,"duplicate planting and premature harvest rejected");
            Do(FoodAction.Gather,"berry");Check(m.State.fruitStock==0&&!Do(FoodAction.Gather,"berry").success,"depletion rejects harvest");
            Advance(5);string midway=m.Json();string path=Path.Combine(folder,"a.json");m.Save(path);
            var reloaded=new FoodModel("test-a","generation-a",4242);Check(reloaded.Load(path,"test-a","generation-a")&&reloaded.Json()==midway,"atomic save reload exact state including regrowth");
            for(int i=0;i<55*50;i++){m.FixedStep(false);reloaded.FixedStep(false);}
            Check(m.Json()==reloaded.Json()&&m.State.fruitStock==2&&m.State.gardenStage==3,"regrowth and cultivation continue identically across reload");
            Check(Do(FoodAction.Harvest,"bed").success&&m.State.seeds==seedCount,"mature garden yields fruit and viable seed");
            string paused=m.Json();for(int i=0;i<500;i++)m.FixedStep(true);Check(paused==m.Json(),"pause freezes all food state");
            Do(FoodAction.Gather,"berry");m.State.wetSeason=false;int progress=m.State.regrowthProgress;Advance(30);Check(m.State.regrowthProgress==progress,"dry season blocks regrowth");
            m.State.wetSeason=true;m.State.soilWater=10;Advance(30);Check(m.State.regrowthProgress==progress,"low water blocks regrowth");
            var b=new FoodModel("test-b","generation-b",4242);Check(!b.Load(path,"test-b","generation-b")&&!b.State.knowsBerry&&b.State.carriedFruit==0&&b.State.seeds==1,"cross-save identity leakage rejected");
            var reset=new FoodModel("test-a","generation-reset",4242);Check(!reset.Load(path,"test-a","generation-reset")&&!reset.Execute("test-a","generation-a",999,FoodAction.Inspect,"berry",a).success,"reset rejects old generation save and command");
            File.WriteAllText(path,"corrupt");string unchanged=m.Json();Check(!m.Load(path,"test-a","generation-a")&&m.Json()==unchanged,"corrupt load retains live state");
            var invalid=JsonUtility.FromJson<FoodState>(m.Json());invalid.carriedFruit=-1;Check(!m.Restore(JsonUtility.ToJson(invalid),"test-a","generation-a"),"invalid counts rejected");
            for(int seed=0;seed<10;seed++)
            {
                var x=new FoodModel("replay","g",seed);var y=new FoodModel("replay","g",seed);
                for(int i=0;i<15000;i++){x.FixedStep(false);y.FixedStep(false);}
                Check(x.Json()==y.Json(),"fixed tick replay seed "+seed);
            }
            int viable=0;for(int i=1;i<=120;i++)if(EdenEcology.RandomKey(4242,i)%12==0)viable++;
            Check(viable>0&&viable<30,"rare seed viability: most fail");
            var eden=new FoodModel("eden","g",4242);int chosen=1;while(EdenEcology.RandomKey(4242,chosen)%12!=0)chosen++;
            eden.State.nextSeed=chosen;eden.State.drops.Add(new EdenSeed{id=chosen,site=2});string seedSnapshot=eden.Json();var replay=new FoodModel("eden","g",4242);Check(replay.Restore(seedSnapshot,"eden","g"),"dropped seed save validates");
            for(int i=0;i<30*50;i++){eden.FixedStep(false);replay.FixedStep(false);}
            Check(eden.Json()==replay.Json()&&eden.State.drops[0].outcome=="germinated"&&EdenEcology.Living(eden.State)==3,"rare natural germination save-stable and budgeted");
            for(int i=0;i<11*50;i++)eden.FixedStep(false);Check(eden.State.drops.Count==0,"dropped seeds expire within finite lifetime");
            eden.State.temperatureC=-5;for(int i=0;i<180*50;i++)eden.FixedStep(false);Check(EdenEcology.Living(eden.State)==0,"prolonged cold kills plants without respawn");
            eden.Execute("eden","g",1,FoodAction.CampAid,"inventory",a);Check(eden.State.aidUsed==1&&eden.State.seeds>=1&&eden.State.hydration>=5000,"explicit assisted recovery is logged and finite per request");
            var cap=new FoodModel("cap","g",4242);cap.State.bushes.Add(new EdenBush{site=2});cap.State.bushes.Add(new EdenBush{site=3});cap.State.knowsPlanting=true;cap.State.plantingEvidence="g.garden-lesson.1";
            Check(!cap.Execute("cap","g",1,FoodAction.Plant,"bed",a).success&&EdenEcology.Living(cap.State)==4,"population cap blocks deliberate planting too");
            var dry=new FoodModel("dry","g",4242);dry.State.wetSeason=false;dry.State.nextSeed=chosen;dry.State.drops.Add(new EdenSeed{id=chosen,site=2});
            for(int i=0;i<30*50;i++)dry.FixedStep(false);Check(dry.State.drops[0].outcome=="failed","viable seed fails unsuitable season");
            var rock=new FoodModel("rock","g",4242);rock.State.nextSeed=chosen;rock.State.drops.Add(new EdenSeed{id=chosen,site=3});for(int i=0;i<30*50;i++)rock.FixedStep(false);Check(rock.State.drops[0].outcome=="failed","viable seed fails rocky substrate");
            var drop=new FoodModel("drop","g",4242);for(int i=0;i<90*50;i++)drop.FixedStep(false);Check(drop.State.nextSeed>0&&drop.State.drops.Count>0,"unharvested ripe fruit drops seeds");
            passed.AddRange(FoodPhysiology.Checks());
            var full=new FoodModel("full","g",4242);full.State.knowsBerry=true;full.State.berryEvidence="g.lesson.1";full.State.carriedFruit=1;full.State.body.stomach=9500;
            Check(!full.Execute("full","g",1,FoodAction.Eat,"inventory",a).success&&full.State.carriedFruit==1,"overeating rejected before inventory mutation");
            var mortality=new FoodModel("mortality","g",4242);var ms=mortality.State;ms.knowsBerry=true;ms.berryEvidence="g.lesson.1";ms.seenBed=true;ms.carriedFruit=2;ms.seeds=1;ms.wetSeason=false;ms.fruitStock=0;
            void Starve(){ms.satiety=0;ms.hydration=10000;ms.body.fat=0;ms.body.health=1;ms.body.deficitSeconds=3600;for(int i=0;i<50;i++)mortality.FixedStep(false);}
            Starve();Check(ms.body.dead&&ms.deaths.Count==1&&ms.deaths[0].cause=="prolonged-starvation"&&ms.deaths[0].lesson.Contains("Energy and fat")&&!ms.knowsMealBenefit&&!ms.knowsPlanting,"verified death records measured cause without invented meal or planting knowledge");
            int deathTick=ms.tick;string deathHash=ms.deaths[0].hash;
            Check(ms.bags[0].berries==2&&ms.bags[0].seeds==1&&ms.carriedFruit==0,"ordinary inventory transferred to recoverable owned bag");
            Check(mortality.Execute("mortality","g",1,FoodAction.Return,"inventory",a).success&&ms.tick==deathTick&&ms.fruitStock==0&&!ms.wetSeason&&ms.incarnation==2,"return restores body without rewinding world");
            Check(ms.body.health==10000&&ms.satiety==6500&&ms.hydration==5500&&!ms.knowsMealBenefit&&ms.causalMemory==ms.deaths[0].lesson,"safe body defaults and cause-grounded memory persist");
            Check(mortality.Execute("mortality","g",2,FoodAction.Recover,"refuge",a).success&&ms.carriedFruit==2&&ms.bags[0].berries==0,"refuge inventory recovery exactly once");
            Starve();Check(ms.deaths.Count==2&&ms.deaths[0].hash==deathHash&&!ms.knowsPlanting&&!ms.knowsMealBenefit,"second death preserves immutable first event without fabricated mechanics");
            var dehydration=new FoodModel("dehydration","g",4242);var ds=dehydration.State;
            ds.satiety=10000;ds.hydration=0;ds.body.health=1;ds.body.drySeconds=901;
            for(int i=0;i<50;i++)dehydration.FixedStep(false);
            Check(ds.body.dead&&ds.deaths.Count==1&&ds.deaths[0].cause=="prolonged-dehydration"&&
                ds.deaths[0].lesson.Contains("Hydration remained depleted")&&!ds.knowsSpring&&!ds.knowsMealBenefit,
                "dehydration death records actual cause without inventing water or meal knowledge");
            string deathPath=Path.Combine(folder,"death.json");mortality.Save(deathPath);var mortalityReload=new FoodModel("mortality","g",4242);Check(mortalityReload.Load(deathPath,"mortality","g")&&mortalityReload.Json()==mortality.Json(),"death body bags and lesson survive reload");
            int oldSnapshots=Directory.GetFiles(folder,"snap-*.json").Length;
            mortality.Execute("mortality","g",3,FoodAction.Return,"inventory",a);Starve();Check(ms.deaths.Count==3&&ms.deaths[2].lesson==ms.deaths[0].lesson,"repeated death retains only the same measured cause lesson");
            mortality.Save(deathPath);Check(Directory.GetFiles(folder,"snap-*.json").Length==oldSnapshots+1,"new save retains immutable prior snapshot");
            var stranger=new FoodModel("mortality","g",4242);stranger.State.actorId="inhabitant-2";Check(!stranger.Load(deathPath,"mortality","g")&&!stranger.State.knowsBerry&&stranger.State.body.health==10000,"new inhabitant defaults and no cross-inhabitant memory leakage");
            return passed;
        }
    }
}
