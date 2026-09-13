using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Starfall.Food
{
    public enum FoodAction { Inspect, Gather, Eat, Drink, Plant, Harvest, Water, CampAid, Return, Recover }
    [Serializable] public sealed class FoodState
    {
        public string schema="starfall.food.v1", rules="food-eden.2", world, generation,actorId="inhabitant-1";
        public int seed, tick, subTick, satiety=6500, hydration=5500, fruitStock=2, regrowthProgress,
            carriedFruit, soilWater=100, seeds=1, plantedAt=-1, gardenStage, freshwaterMl=2000, lastRequest;
        public bool knowsBerry, knowsSpring, wetSeason=true;
        public string berryEvidence="", springEvidence="", lastMealEvidence="", lastSignature="", lastCode="";
        public bool lastSuccess;
        public Vector3 actorPosition;
        public int ripeAge,primaryStress,gardenGrowth,gardenStress,gardenRipeAge,temperatureC=22,nextSeed,ecologySequence,lastNaturalSeason=-1,aidUsed;
        public bool primaryDead,knowsPlanting,gardenEstablished;
        public string plantingEvidence="",causalMemory="";
        public List<EdenBush> bushes=new List<EdenBush>{new EdenBush{site=1}};
        public List<EdenSeed> drops=new List<EdenSeed>();
        public List<EdenEvent> ecologyEvents=new List<EdenEvent>();
        public FoodBody body=new FoodBody();public int incarnation=1;public bool seenBed,knowsMealBenefit;
        public List<FoodDeath> deaths=new List<FoodDeath>();public List<RecoveryBag> bags=new List<RecoveryBag>();
    }
    [Serializable] public sealed class FoodReceipt
    {
        public string world, generation, target, code; public int request, tick;
        public FoodAction action; public bool success, duplicate;
        public int foodDelta, waterDelta, inventoryDelta;
    }
    public struct FoodAccess
    {
        public bool visible, inReach, permitted, verifiedFreshwater;
    }
    // Trusted Unity adapter supplies access. Model/network proposals never supply these booleans.
    public interface IFoodAccess { FoodAccess Inspect(string target); }
    public sealed class FoodModel
    {
        public FoodState State {get;private set;}
        public FoodModel(string world,string generation,int seed)
        {
            if(!Id(world)||!Id(generation))throw new ArgumentException("Invalid world scope");
            State=new FoodState{world=world,generation=generation,seed=seed,fruitStock=2+(int)((uint)seed%2)};
        }
        public static bool Id(string s)=>s!=null&&System.Text.RegularExpressions.Regex.IsMatch(s,@"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}\z");
        public void FixedStep(bool paused)
        {
            if(paused)return;
            if(++State.subTick<50)return;State.subTick=0;State.tick++;
            bool wasDead=State.body.dead;FoodPhysiology.Step(State.body,ref State.satiety,ref State.hydration);
            if(!wasDead&&State.body.dead)RecordDeath();
            EdenEcology.Step(State);
        }
        void RecordDeath()
        {
            var s=State;string lesson="";
            if(s.body.cause=="prolonged-starvation"&&s.knowsBerry&&!s.knowsMealBenefit){lesson="Known-safe berries restore energy. Return to a previously observed bush.";s.knowsMealBenefit=true;}
            else if(s.body.cause=="prolonged-starvation"&&s.knowsBerry&&s.seenBed&&!s.knowsPlanting&&s.seeds>0){lesson="Saved berry seeds can grow in the moist bed you already observed.";s.knowsPlanting=true;s.plantingEvidence=s.generation+".death-lesson."+(s.deaths.Count+1);}
            var d=new FoodDeath{id=s.generation+".death."+(s.deaths.Count+1),world=s.world,generation=s.generation,actor=s.actorId,tick=s.tick,incarnation=s.incarnation,cause=s.body.cause,lesson=lesson,energy=s.satiety,hydration=s.hydration,fat=s.body.fat,health=s.body.health,deficitSeconds=s.body.deficitSeconds,drySeconds=s.body.drySeconds,previousHash=s.deaths.Count==0?"":s.deaths[s.deaths.Count-1].hash,hash=""};
            d.hash=Hash(JsonUtility.ToJson(d));s.deaths.Add(d);s.bags.Add(new RecoveryBag{death=s.deaths.Count,owner=s.actorId,berries=s.carriedFruit,seeds=s.seeds});s.carriedFruit=s.seeds=0;
            EdenEcology.Event(s,"death","refuge",d.cause+"; world continues. "+(lesson==""?"No new grounded lesson available.":lesson));
        }
        public FoodReceipt Execute(string world,string generation,int request,FoodAction action,string target,IFoodAccess access)
        {
            var s=State;var r=new FoodReceipt{world=s.world,generation=s.generation,request=request,tick=s.tick,action=action,target=target,code="invalid-request"};
            if(world!=s.world||generation!=s.generation){r.code="world-mismatch";return r;}
            if(request<=0||!Enum.IsDefined(typeof(FoodAction),action)||!Id(target)||access==null)return r;
            string signature=action+":"+target;
            if(request==s.lastRequest){r.code=s.lastSignature==signature?s.lastCode:"request-conflict";r.success=s.lastSignature==signature&&s.lastSuccess;r.duplicate=s.lastSignature==signature;return r;}
            if(request<s.lastRequest){r.code="stale-request";return r;}
            FoodReceipt End(string code,bool ok=false){r.code=code;r.success=ok;s.lastRequest=request;s.lastSignature=signature;s.lastCode=code;s.lastSuccess=ok;return r;}
            EdenBush other=null;foreach(var b in s.bushes)if(target=="berry"+(b.site+1))other=b;
            if(s.body.dead&&action!=FoodAction.Return)return End("return-required");
            if(target!="berry"&&other==null&&target!="spring"&&target!="sea"&&target!="bed"&&target!="refuge"&&target!="inventory")return End("unknown-target");
            var gate=access.Inspect(target);
            if(!gate.permitted)return End("permission-denied");
            if(target!="inventory"&&(!gate.visible||!gate.inReach))return End(!gate.visible?"not-visible":"out-of-reach");
            int beforeFood=s.satiety,beforeWater=s.hydration,beforeItems=s.carriedFruit;
            string code;
            switch(action)
            {
                case FoodAction.Inspect:
                    if(target=="berry"){s.knowsBerry=true;s.berryEvidence=s.generation+".lesson."+request;code="observed-signed-ripe-berry-lesson";}
                    else if(target=="bed"){s.knowsPlanting=true;s.plantingEvidence=s.generation+".garden-lesson."+request;code="observed-moist-soil-and-watering-lesson";}
                    else if(target=="spring"&&gate.verifiedFreshwater){s.knowsSpring=true;s.springEvidence=s.generation+".source."+request;code="verified-maintained-freshwater-source";}
                    else return End("no-safety-evidence");break;
                case FoodAction.Gather:
                    if(target!="berry"&&other==null)return End("wrong-target");
                    if(!s.knowsBerry)return End("unknown-food");
                    if(other==null?(s.primaryDead||s.fruitStock<=0):(other.dead||other.growth<EdenEcology.Mature||other.stock<=0))return End("depleted");
                    if(s.carriedFruit>=4)return End("inventory-full");
                    if(other==null)s.fruitStock--;else other.stock--;s.carriedFruit++;code="gathered-ripe-berry";break;
                case FoodAction.Eat:
                    if(target!="inventory")return End("wrong-target");
                    if(!s.knowsBerry)return End("unknown-food");
                    if(s.carriedFruit<1)return End("no-food");
                    if(s.body.stomach>8800)return End("comfortably-full");
                    s.body.stomach+=1200;s.body.protein=Math.Min(10000,s.body.protein+20);s.knowsMealBenefit=true;
                    s.carriedFruit--;s.satiety=Math.Min(10000,s.satiety+1200);s.hydration=Math.Min(10000,s.hydration+400);s.lastMealEvidence=s.generation+".ate."+request;if(s.seeds<4)s.seeds++;code="ate-ripe-berry-and-kept-visible-seed";break;
                case FoodAction.Drink:
                    if(target=="sea"||!gate.verifiedFreshwater)return End("unsafe-water");
                    if(target!="spring")return End("wrong-target");
                    if(!s.knowsSpring)return End("unverified-source");
                    if(s.freshwaterMl<250)return End("source-empty");
                    s.freshwaterMl-=250;s.hydration=Math.Min(10000,s.hydration+2000);code="drank-250ml-freshwater";break;
                case FoodAction.Plant:
                    if(target!="bed")return End("wrong-target");
                    if(!s.knowsPlanting)return End("unknown-cultivation");
                    if(s.seeds<1)return End("no-seed");
                    if(s.gardenStage>0&&s.gardenStage<4)return End("bed-occupied");
                    if(EdenEcology.Living(s)>=EdenEcology.PopulationCap)return End("population-cap");
                    if(!EdenEcology.Suitable(s))return End("unsuitable-soil-weather");
                    s.seeds--;s.plantedAt=s.tick;s.gardenStage=1;s.gardenGrowth=s.gardenStress=s.gardenRipeAge=0;s.gardenEstablished=false;code="planted-in-moist-bed";break;
                case FoodAction.Harvest:
                    if(target!="bed")return End("wrong-target");
                    if(!s.knowsBerry)return End("unknown-food");
                    if(s.gardenStage!=3)return End("not-mature");
                    if(s.carriedFruit>=4||s.seeds>=4)return End("inventory-full");
                    s.carriedFruit++;s.seeds++;s.plantedAt=s.tick;s.gardenStage=1;s.gardenGrowth=s.gardenRipeAge=0;code="harvested-fruit-and-seed";break;
                case FoodAction.Water:
                    if(target!="bed")return End("wrong-target");
                    if(!s.knowsPlanting)return End("unknown-cultivation");
                    if(s.freshwaterMl<250)return End("source-empty");
                    if(s.soilWater>80)return End("soil-already-moist");
                    s.freshwaterMl-=250;s.soilWater+=20;code="watered-soil-250ml";break;
                case FoodAction.CampAid:
                    if(target!="inventory")return End("wrong-target");
                    s.aidUsed++;s.satiety=Math.Max(s.satiety,5000);s.hydration=Math.Max(s.hydration,5000);s.freshwaterMl=Math.Max(s.freshwaterMl,500);s.seeds=Math.Max(s.seeds,1);code="explicit-camp-aid-assisted-run";break;
                case FoodAction.Return:
                    if(target!="inventory"||!s.body.dead)return End("not-awaiting-return");
                    s.body=new FoodBody();s.satiety=6500;s.hydration=5500;s.incarnation++;s.actorPosition=new Vector3(0,0,-2);code="returned-same-inhabitant-world-unchanged";break;
                case FoodAction.Recover:
                    if(target!="refuge")return End("wrong-target");
                    bool recovered=false;foreach(var bag in s.bags)if(bag.owner==s.actorId){int f=Math.Min(4-s.carriedFruit,bag.berries),seed=Math.Min(4-s.seeds,bag.seeds);s.carriedFruit+=f;s.seeds+=seed;bag.berries-=f;bag.seeds-=seed;recovered|=f+seed>0;}
                    if(!recovered)return End("nothing-recoverable-or-full");code="recovered-owned-refuge-bag";break;
                default:return End("unsupported");
            }
            r.foodDelta=s.satiety-beforeFood;r.waterDelta=s.hydration-beforeWater;r.inventoryDelta=s.carriedFruit-beforeItems;
            return End(code,true);
        }
        public string Json()=>JsonUtility.ToJson(State);
        public static string Hash(string data){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(data))).Replace("-","").ToLowerInvariant();}
        public static bool Valid(FoodState s,string world,string generation)
        {
            if(s==null||s.schema!="starfall.food.v1"||s.rules!="food-eden.2"||s.world!=world||s.generation!=generation||!Id(world)||!Id(generation)||!Id(s.actorId)||!FoodPhysiology.Valid(s.body)||s.incarnation<1||s.deaths==null||s.bags==null||s.deaths.Count>128||s.bags.Count!=s.deaths.Count)return false;
            string prev="";foreach(var d in s.deaths){if(d==null||d.world!=world||d.generation!=generation||d.actor!=s.actorId||d.previousHash!=prev||d.health!=0)return false;string hash=d.hash;var copy=JsonUtility.FromJson<FoodDeath>(JsonUtility.ToJson(d));copy.hash="";if(hash!=Hash(JsonUtility.ToJson(copy)))return false;prev=hash;}
            if(s.deaths.Count!=s.incarnation-(s.body.dead?0:1))return false;
            foreach(var b in s.bags)if(b==null||b.owner!=s.actorId||b.berries<0||b.berries>4||b.seeds<0||b.seeds>4)return false;
            int maxFruit=2+(int)((uint)s.seed%2);
            if(s.tick<0||s.tick>100000000||s.subTick<0||s.subTick>=50||s.satiety<0||s.satiety>10000||s.hydration<0||s.hydration>10000||s.fruitStock<0||s.fruitStock>maxFruit||s.carriedFruit<0||s.carriedFruit>4||s.seeds<0||s.seeds>4||s.freshwaterMl<0||s.freshwaterMl>2000||s.freshwaterMl%250!=0||s.lastRequest<0)return false;
            if(s.regrowthProgress<0||s.regrowthProgress>=EdenEcology.Regrow||s.soilWater<0||s.soilWater>100||s.fruitStock==maxFruit&&s.regrowthProgress!=0)return false;
            if(s.plantedAt< -1||s.plantedAt>s.tick||s.gardenStage<0||s.gardenStage>4||s.gardenGrowth<0||s.gardenGrowth>EdenEcology.Mature)return false;
            int stage=s.plantedAt<0?0:s.gardenGrowth>=EdenEcology.Mature?3:s.gardenGrowth>=EdenEcology.Mature/2?2:1;
            if(s.gardenStage!=4&&s.gardenStage!=stage||s.knowsBerry!=!string.IsNullOrEmpty(s.berryEvidence)||s.knowsSpring!=!string.IsNullOrEmpty(s.springEvidence))return false;
            if(s.bushes==null||s.drops==null||s.ecologyEvents==null||s.bushes.Count>3||s.drops.Count>8||s.ecologyEvents.Count>24||s.temperatureC< -30||s.temperatureC>60||s.nextSeed<0||s.ecologySequence<0||s.aidUsed<0||s.knowsPlanting!=!string.IsNullOrEmpty(s.plantingEvidence))return false;
            var sites=new HashSet<int>();foreach(var b in s.bushes)if(b==null||b.site<1||b.site>3||!sites.Add(b.site)||b.stock<0||b.stock>2||b.progress<0||b.progress>=EdenEcology.Regrow||b.growth<0||b.growth>EdenEcology.Mature||b.stress<0)return false;
            var seedsSeen=new HashSet<int>();foreach(var d in s.drops)if(d==null||d.id<=0||d.id>s.nextSeed||!seedsSeen.Add(d.id)||d.site<2||d.site>3||d.age<0||d.age>=EdenEcology.SeedLifetime||(d.outcome!="dropped"&&d.outcome!="germinated"&&d.outcome!="failed"))return false;
            if(EdenEcology.Living(s)>EdenEcology.PopulationCap)return false;
            if(s.knowsBerry&&!s.berryEvidence.StartsWith(generation+".lesson.",StringComparison.Ordinal)||s.knowsSpring&&!s.springEvidence.StartsWith(generation+".source.",StringComparison.Ordinal))return false;
            var p=s.actorPosition;return Finite(p.x)&&Finite(p.y)&&Finite(p.z)&&p.magnitude<10000;
        }
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        public bool Restore(string json,string world,string generation)
        {
            if(json==null||json.Length>128000)return false;
            try{var candidate=JsonUtility.FromJson<FoodState>(json);if(!Valid(candidate,world,generation)||candidate.seed!=State.seed||candidate.actorId!=State.actorId)return false;State=candidate;return true;}catch{return false;}
        }
        [Serializable] sealed class SaveEnvelope{public string schema="starfall.food-save.v1",payload,sha256;}
        [Serializable] sealed class SavePointer{public string schema="starfall.food-pointer.v1",snapshot;}
        public void Save(string path)
        {
            if(!Valid(State,State.world,State.generation))throw new InvalidOperationException("Invalid state");
            string payload=Json(),hash=Hash(payload);string json=JsonUtility.ToJson(new SaveEnvelope{payload=payload,sha256=hash},true);
            Directory.CreateDirectory(Path.GetDirectoryName(path));string snapshot="snap-"+hash+".json";string immutable=Path.Combine(Path.GetDirectoryName(path),snapshot);
            if(!File.Exists(immutable)){string candidate=immutable+".tmp-"+Guid.NewGuid().ToString("N");using(var f=new FileStream(candidate,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] b=Encoding.UTF8.GetBytes(json);f.Write(b,0,b.Length);f.Flush(true);}File.Move(candidate,immutable);}
            else if(File.ReadAllText(immutable)!=json)throw new InvalidOperationException("Immutable snapshot conflict");
            string pointer=JsonUtility.ToJson(new SavePointer{snapshot=snapshot});string tmp=path+".tmp";
            using(var f=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None)){byte[] b=Encoding.UTF8.GetBytes(pointer);f.Write(b,0,b.Length);f.Flush(true);}
            if(File.Exists(path))File.Replace(tmp,path,path+".bak");else File.Move(tmp,path);
        }
        public bool Load(string path,string world,string generation)
        {
            try
            {
                if(new FileInfo(path).Length>160000)return false;string text=File.ReadAllText(path);var pointer=JsonUtility.FromJson<SavePointer>(text);
                if(pointer!=null&&pointer.schema=="starfall.food-pointer.v1")
                {if(string.IsNullOrEmpty(pointer.snapshot)||!System.Text.RegularExpressions.Regex.IsMatch(pointer.snapshot,@"\Asnap-[0-9a-f]{64}\.json\z"))return false;string snapshot=Path.Combine(Path.GetDirectoryName(path),pointer.snapshot);if(new FileInfo(snapshot).Length>160000)return false;text=File.ReadAllText(snapshot);}
                var e=JsonUtility.FromJson<SaveEnvelope>(text);return e!=null&&e.schema=="starfall.food-save.v1"&&e.payload!=null&&e.sha256==Hash(e.payload)&&Restore(e.payload,world,generation);
            }catch{return false;}
        }
    }
}
