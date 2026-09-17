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
            var placeModel=new FoodModel("place-a","place-gen",4242);
            var place=placeModel.State;
            Check(PlaceLedger.LastSeen(place,"spring-food")==null&&place.exploredCells.Count==0,
                "unknown places and cells begin unknown without authored registry coordinates");
            var springLabelModel=new FoodModel("label-world","label-gen",4242);
            springLabelModel.State.tick=1;
            Check(PlaceLedger.Observe(springLabelModel.State,"spring-food","Place","freshwater-seep",
                    new Vector3(9,4,12),true,true,1,5,false)&&
                PlaceLedger.LastSeen(springLabelModel.State,"spring-food").observedType=="freshwater-seep"&&
                PlaceLedger.LastSeen(place,"spring-food")==null,
                "observed spring carries visual resource category without granting it to another inhabitant");
            Check(PlaceLedger.Occupy(place,2,3,0)&&PlaceLedger.Cell(place,2,3).visits==1&&
                PlaceLedger.Cell(place,90,90)==null,
                "only an occupied cell enters explored memory");
            place.tick=1;
            Check(PlaceLedger.Observe(place,"berry-food","Place","fruiting-succulent",new Vector3(6,4,9),true,true,1,50,false)&&
                PlaceLedger.LastSeen(place,"berry-food").kind=="first-seen"&&
                PlaceLedger.LastSeen(place,"berry-food").observedType=="fruiting-succulent"&&
                PlaceLedger.LastSeen(place,"spring-food")==null,
                "only scoped sensed fruit enters observed-place history");
            place.tick=2;
            Check(PlaceLedger.Observe(place,"berry-food","Place","fruiting-succulent",new Vector3(6,4,9),false,true,2,100,true)&&
                place.observedPlaces.Count==2&&place.observedPlaces[0].available&&
                !PlaceLedger.LastSeen(place,"berry-food").available&&
                PlaceLedger.LastSeen(place,"berry-food").kind=="changed",
                "revisit change revises last-seen belief but retains first observation");
            Check(!PlaceLedger.Observe(place,"berry-food","Place","fruiting-succulent",new Vector3(6,4,9),true,true,1,90,true)&&
                PlaceLedger.LastSeen(place,"berry-food").foodTick==2,
                "older observation cannot regress latest belief time");
            Check(!PlaceLedger.Occupy(place,int.MinValue,3,2)&&
                !PlaceLedger.Observe(place,"spring-food","Place","freshwater-seep",new Vector3(float.NaN,4,9),true,true,2,101,true),
                "malformed coordinates fail closed without overflow or nonfinite location");
            string placePath=Path.Combine(folder,"place-ledger-save.json");
            Check(FoodModel.Valid(place,"place-a","place-gen"),"observed-place event chain and scoped cells validate");
            placeModel.Save(placePath);
            var reopenedPlace=new FoodModel("place-a","place-gen",4242);
            Check(reopenedPlace.Load(placePath,"place-a","place-gen")&&
                reopenedPlace.State.observedPlaces.Count==2&&
                PlaceLedger.LastSeen(reopenedPlace.State,"berry-food").available==false&&
                PlaceLedger.LastSeen(reopenedPlace.State,"spring-food")==null&&
                PlaceLedger.Cell(reopenedPlace.State,2,3).visits==1,
                "scoped explored cells and revisable observed belief survive save reload");
            reopenedPlace.State.tick=3;
            Check(PlaceLedger.Observe(reopenedPlace.State,"berry-food","Place","fruiting-succulent",new Vector3(6,4,9),
                true,true,3,2,true)&&reopenedPlace.State.observedPlaces.Count==3&&
                reopenedPlace.State.observedPlaces[2].brainTick==2&&
                PlaceLedger.LastSeen(reopenedPlace.State,"berry-food").available,
                "restarted local brain tick may reset while persisted food time advances and revises belief");
            var visibleAfterReload=new HashSet<string>(StringComparer.Ordinal);
            CityLife.World.StarfallSurvivalAutonomy.SeedVisibleForReload(visibleAfterReload,
                new List<CityLife.World.NpcObservation>{new CityLife.World.NpcObservation{
                    id="berry-food",kind=CityLife.World.NpcObjectKind.Place,seenAtTick=2}});
            Check(visibleAfterReload.Contains("berry-food")&&!visibleAfterReload.Contains("spring-food"),
                "same-position reload seeds current LOS so it does not fabricate an unchanged revisit");
            reopenedPlace.State.tick=4;
            Check(PlaceLedger.Observe(reopenedPlace.State,"berry-food","Place","fruiting-succulent",
                new Vector3(6,4,9),true,true,4,4,!visibleAfterReload.Contains("berry-food"))&&
                reopenedPlace.State.observedPlaces.Count==3,
                "still-visible unchanged place at reload adds no false revisit event");
            visibleAfterReload.Remove("berry-food");
            Check(PlaceLedger.Observe(reopenedPlace.State,"berry-food","Place","fruiting-succulent",new Vector3(6,4,9),
                true,true,4,5,!visibleAfterReload.Contains("berry-food"))&&reopenedPlace.State.observedPlaces.Count==4&&
                PlaceLedger.LastSeen(reopenedPlace.State,"berry-food").kind=="revisit"&&
                reopenedPlace.State.observedPlaces[0].kind=="first-seen"&&
                reopenedPlace.State.observedPlaces[1].kind=="changed",
                "unchanged return records a revisit while old and revised observations remain inspectable");
            var foreign=JsonUtility.FromJson<FoodState>(placeModel.Json());
            foreign.observedPlaces[0].world="other-world";
            Check(!FoodModel.Valid(foreign,"place-a","place-gen")&&
                !new FoodModel("other-world","place-gen",4242).Restore(placeModel.Json(),"other-world","place-gen"),
                "foreign event and foreign world cannot inherit observed places");
            var wrongActor=JsonUtility.FromJson<FoodState>(placeModel.Json());
            wrongActor.observedPlaces[0].actor="different-inhabitant";
            var wrongGeneration=JsonUtility.FromJson<FoodState>(placeModel.Json());
            wrongGeneration.exploredCells[0].generation="different-generation";
            Check(!FoodModel.Valid(wrongActor,"place-a","place-gen")&&
                !FoodModel.Valid(wrongGeneration,"place-a","place-gen")&&
                !new FoodModel("place-a","place-gen",4242).Restore(
                    JsonUtility.ToJson(new FoodState{world="place-a",generation="place-gen",seed=4242,
                        actorId="different-inhabitant"}),"place-a","place-gen"),
                "inhabitant and generation mismatches refuse place-memory joins");
            var resetWorld=new FoodModel("place-new-world","place-new-gen",4242);
            string resetPath=Path.Combine(folder,"place-ledger-reset-world.json");resetWorld.Save(resetPath);
            Check(File.Exists(placePath)&&File.Exists(resetPath)&&
                PlaceLedger.LastSeen(resetWorld.State,"berry-food")==null&&
                new FoodModel("place-a","place-gen",4242).Load(placePath,"place-a","place-gen"),
                "fresh world starts unknown while prior scoped save remains recoverable");
            var oldModel=new FoodModel("old-place","old-gen",4242);
            string oldPayload=oldModel.Json();
            oldPayload=System.Text.RegularExpressions.Regex.Replace(oldPayload,",?\"exploredCells\":\\[\\]","");
            oldPayload=System.Text.RegularExpressions.Regex.Replace(oldPayload,",?\"observedPlaces\":\\[\\]","");
            var oldReload=new FoodModel("old-place","old-gen",4242);
            Check(oldReload.Restore(oldPayload,"old-place","old-gen")&&
                oldReload.State.exploredCells!=null&&oldReload.State.observedPlaces!=null&&
                oldReload.State.exploredCells.Count==0&&oldReload.State.observedPlaces.Count==0,
                "additive place ledger restores old food snapshot without inventing knowledge");
            var capacity=new FoodModel("large-place","large-gen",4242);capacity.State.tick=512;
            for(int i=0;i<PlaceLedger.MaximumCells;i++)
                if(!PlaceLedger.Occupy(capacity.State,i,0,512))throw new Exception("capacity cell fixture failed");
            for(int i=0;i<PlaceLedger.MaximumEvents;i++)
                if(!PlaceLedger.Observe(capacity.State,"site-"+i,"Place","observed-site",new Vector3(i,4,0),true,true,i+1,i+1,false))
                    throw new Exception("capacity event fixture failed");
            string capacityPath=Path.Combine(folder,"place-ledger-capacity-save.json");
            capacity.Save(capacityPath);var capacityReload=new FoodModel("large-place","large-gen",4242);
            Check(capacityReload.Load(capacityPath,"large-place","large-gen")&&
                capacityReload.State.exploredCells.Count==PlaceLedger.MaximumCells&&
                capacityReload.State.observedPlaces.Count==PlaceLedger.MaximumEvents&&
                !PlaceLedger.Occupy(capacityReload.State,600,0,512),
                "maximum bounded place payload reloads and capacity surfaces refusal rather than dropping knowledge");
            Check(!Do(FoodAction.Gather,"berry").success,"unknown berry not edible/gatherable");
            var exactModel=new Dictionary<string,object>{{"model","starfall-local-e4b"}};
            Check(CityLife.World.NpcBoundedJson.ExactCompletionModel(exactModel,"starfall-local-e4b")&&
                !CityLife.World.NpcBoundedJson.ExactCompletionModel(exactModel,"google/gemma-4-e4b")&&
                !CityLife.World.NpcBoundedJson.ExactCompletionModel(
                    new Dictionary<string,object>{{"model",false}},"starfall-local-e4b")&&
                !CityLife.World.NpcBoundedJson.ExactCompletionModel(
                    new Dictionary<string,object>(),"starfall-local-e4b"),
                "completion response must carry the exact requested loaded model identity");
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
            string urgentNeed=CityLife.World.StarfallSurvivalThought.BuildRequest("local-e4b",1700,1800,
                new List<string>{"explore north","approach berry"});
            Check(urgentNeed.Contains("1700/10000 severe low energy")&&
                urgentNeed.Contains("1800/10000 severe low hydration")&&
                !heldPrompt.Contains("severe low"),
                "only measured severe depletion receives urgent physiological wording");
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
            string groundedDeath=CityLife.World.StarfallSurvivalThought.BuildRequest("local-e4b",6500,5500,
                new List<string>{"explore south","explore east"},0,false,null,"prolonged-dehydration");
            string falseDeath=CityLife.World.StarfallSurvivalThought.BuildRequest("local-e4b",6500,5500,
                new List<string>{"explore south","explore east"},0,false,null,"unverified-water-source-claim");
            Check(groundedDeath.Contains("Prior verified own death: Hydration remained depleted")&&
                !groundedDeath.Contains("spring")&&!falseDeath.Contains("Prior verified own death")&&
                !falseDeath.Contains("unverified-water-source-claim"),
                "only measured own death cause enters model request without inventing a spring location");
            string reachedFruit=CityLife.World.StarfallSurvivalThought.BuildRequest("local-e4b",6300,5200,
                new List<string>{"gather berry","explore west"},0,false,"approach berry reached");
            string untrustedFruit=CityLife.World.StarfallSurvivalThought.BuildRequest("local-e4b",6300,5200,
                new List<string>{"gather berry","explore west"},0,false,"approach berry claimed edible");
            Check(reachedFruit.Contains("approach berry reached observed fruit")&&
                reachedFruit.Contains("food benefit unknown")&&!reachedFruit.Contains("edible")&&
                !untrustedFruit.Contains("claimed edible")&&!untrustedFruit.Contains("Last verified outcome"),
                "only an executed route supplies short experiment context; unverified benefit is not invented");
            string gatheredFruit=CityLife.World.StarfallSurvivalThought.BuildRequest("local-e4b",6300,5200,
                new List<string>{"eat fruit","explore west"},1,false,"gather berry succeeded");
            Check(gatheredFruit.Contains("put one observed fruit in inventory")&&
                gatheredFruit.Contains("unknown meal effect")&&gatheredFruit.Contains("not observed")&&
                !gatheredFruit.Contains("previously helped")&&!gatheredFruit.Contains("edible"),
                "gathered fruit can motivate a test without fabricating nutritional proof");
            Check(scopedPrompt.Contains("stay alive and discover resources")&&
                scopedPrompt.Contains("Inspect unknown visible things before using them")&&
                !scopedPrompt.Contains("edible")&&!scopedPrompt.Contains("safe berry"),
                "model gets a survival and investigation goal, not pregranted food safety");
            var exhausted=new FoodState{fruitStock=0,knowsBerry=true,satiety=6500,hydration=5500};
            Check(!CityLife.World.StarfallSurvivalAutonomy.BerryRelevant(exhausted),
                "known depleted berry stock does not lure repeated approach");
            exhausted.fruitStock=1;exhausted.carriedFruit=4;
            Check(!CityLife.World.StarfallSurvivalAutonomy.BerryRelevant(exhausted),
                "full inventory does not lure repeated harvest");
            exhausted.carriedFruit=1;exhausted.knowsMealBenefit=false;
            Check(!CityLife.World.StarfallSurvivalAutonomy.BerryRelevant(exhausted),
                "one carried untested fruit prevents berry-hoarding before a meal outcome");
            exhausted.carriedFruit=0;exhausted.satiety=9900;exhausted.hydration=9900;
            Check(!CityLife.World.StarfallSurvivalAutonomy.BerryRelevant(exhausted),
                "known berry does not displace exploration when measured food and water targets are met");
            var source=new FoodState{knowsSpring=false,hydration=10000,freshwaterMl=2000};
            Check(CityLife.World.StarfallSurvivalAutonomy.SpringRelevant(source),
                "unknown visible spring remains available for live approach and inspection");
            source.knowsSpring=true;
            Check(!CityLife.World.StarfallSurvivalAutonomy.SpringRelevant(source),
                "known spring does not lure repeated approach when hydration is full");
            source.hydration=8400;
            Check(CityLife.World.StarfallSurvivalAutonomy.SpringRelevant(source),
                "known finite spring may be approached for measured thirst");
            source.freshwaterMl=0;
            Check(!CityLife.World.StarfallSurvivalAutonomy.SpringRelevant(source),
                "empty known spring cannot lure another approach");
            exhausted.hydration=5000;exhausted.knowsMealBenefit=true;exhausted.body.stomach=8801;
            Check(!CityLife.World.StarfallSurvivalAutonomy.BerryRelevant(exhausted),
                "full stomach does not promote a known berry as a hydration meal");
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
            int postLoadRequest=reloaded.State.lastRequest+1;
            var postLoadAction=reloaded.Execute("test-a","generation-a",postLoadRequest,FoodAction.Inspect,"berry",a);
            Check(postLoadAction.success&&reloaded.State.lastRequest==postLoadRequest&&
                reloaded.State.berryEvidence.EndsWith("."+postLoadRequest)&&
                reloaded.Execute("test-a","generation-a",postLoadRequest,FoodAction.Inspect,"berry",a).duplicate,
                "reloaded request sequence admits exactly one fresh mutation; duplicate remains idempotent");
            Check(CityLife.World.StarfallSurvivalAutonomy.TryNextFoodRequest(postLoadRequest,out int nextAction)&&
                nextAction==postLoadRequest+1&&
                CityLife.World.StarfallSurvivalAutonomy.TryNextFoodRequest(int.MaxValue-1,out int lastAction)&&
                lastAction==int.MaxValue&&
                !CityLife.World.StarfallSurvivalAutonomy.TryNextFoodRequest(int.MaxValue,out _)&&
                !CityLife.World.StarfallSurvivalAutonomy.TryNextFoodRequest(-1,out _),
                "autonomy allocates fresh post-restart request IDs and fails closed at sequence boundaries");
            Check(reloaded.Load(path,"test-a","generation-a")&&reloaded.Json()==m.Json(),
                "test restores exact scoped snapshot after request-sequence negative probe");
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
            var unearned=new FoodModel("authority","generation-a",4242);
            Check(!FoodModel.HasEarnedSurvivalAuthority(unearned.State),
                "pre-survival food/body save cannot bypass live delivery prerequisite");
            Check(!FoodModel.HasEarnedSurvivalAuthority(m.State),
                "scripted food experiments and a meal never mint survival dispatch authority");
            unearned.State.survivalAuthorityEvidence=FoodModel.SurvivalAuthorityEvidence(unearned.State);
            string earnedPath=Path.Combine(folder,"earned-authority.json");unearned.Save(earnedPath);
            var earnedReload=new FoodModel("authority","generation-a",4242);
            Check(earnedReload.Load(earnedPath,"authority","generation-a")&&
                FoodModel.HasEarnedSurvivalAuthority(earnedReload.State),
                "scoped earned authority survives exact world/actor/generation reload");
            var malformed=JsonUtility.FromJson<FoodState>(unearned.Json());
            malformed.survivalAuthorityEvidence="foreign/actor/generation/three-live-deliveries";
            Check(!FoodModel.Valid(malformed,"authority","generation-a")&&
                !FoodModel.HasEarnedSurvivalAuthority(malformed),
                "foreign or malformed survival authority marker rejected");
            var wrongWorld=new FoodModel("foreign","generation-a",4242);
            Check(!wrongWorld.Load(earnedPath,"foreign","generation-a")&&
                !FoodModel.HasEarnedSurvivalAuthority(wrongWorld.State),
                "earned continuation never crosses a world boundary");
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
            Check(CityLife.World.StarfallSurvivalAutonomy.VerifiedOwnDeathCause(ds)==null,
                "dead inhabitant cannot issue a new postreturn death lesson request");
            var returned=dehydration.Execute("dehydration","g",1,FoodAction.Return,"inventory",a);
            Check(returned.success&&CityLife.World.StarfallSurvivalAutonomy.VerifiedOwnDeathCause(ds)=="prolonged-dehydration",
                "only valid returned own-world hash-linked death cause supplies model context");
            ds.deaths[0].lesson="A spring was nearby";
            Check(CityLife.World.StarfallSurvivalAutonomy.VerifiedOwnDeathCause(ds)==null,
                "tampered death lesson cannot be supplied to the model");
            var drowning=new FoodModel("drowning","g",4242);var drs=drowning.State;
            drs.body.submerged=true;drs.body.health=200;drs.body.submergedSeconds=15;
            for(int i=0;i<50;i++)drowning.FixedStep(false);
            Check(drs.body.dead&&drs.deaths.Count==1&&drs.deaths[0].cause=="drowning"&&
                drs.deaths[0].lesson.Contains("Submerged underwater without air; drowned."),
                "drowning death records actual cause and causal memory without hallucination");
            Check(CityLife.World.StarfallSurvivalAutonomy.VerifiedOwnDeathCause(drs)==null,
                "dead submerged inhabitant cannot issue request before safe return");
            var retDrown=drowning.Execute("drowning","g",1,FoodAction.Return,"inventory",a);
            Check(retDrown.success&&CityLife.World.StarfallSurvivalAutonomy.VerifiedOwnDeathCause(drs)=="drowning",
                "verified drowning death cause available to survival model after return");
            string deathPath=Path.Combine(folder,"death.json");mortality.Save(deathPath);var mortalityReload=new FoodModel("mortality","g",4242);Check(mortalityReload.Load(deathPath,"mortality","g")&&mortalityReload.Json()==mortality.Json(),"death body bags and lesson survive reload");
            int oldSnapshots=Directory.GetFiles(folder,"snap-*.json").Length;
            mortality.Execute("mortality","g",3,FoodAction.Return,"inventory",a);Starve();Check(ms.deaths.Count==3&&ms.deaths[2].lesson==ms.deaths[0].lesson,"repeated death retains only the same measured cause lesson");
            mortality.Save(deathPath);Check(Directory.GetFiles(folder,"snap-*.json").Length==oldSnapshots+1,"new save retains immutable prior snapshot");
            var stranger=new FoodModel("mortality","g",4242);stranger.State.actorId="inhabitant-2";Check(!stranger.Load(deathPath,"mortality","g")&&!stranger.State.knowsBerry&&stranger.State.body.health==10000,"new inhabitant defaults and no cross-inhabitant memory leakage");
            return passed;
        }
    }
}
