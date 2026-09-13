using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Starfall.EnvironmentZones;

static class Program
{
    static int Main(string[] args)
    {
        var checks=new List<object>();int failures=0;
        void Check(string name,bool pass,object measured=null){checks.Add(new{name,passed=pass,measured});if(!pass)failures++;}
        var policy=new CaveZonePolicy("starfall.integrated-coastal.v1","terrain-r2-weathered-banks.integrated1","first-refuge");
        var outside=new OutdoorWeather{WindX=11,WindZ=0,AirC=-8,Rain01=1};
        var synthetic=new CaveProbe{WorldId=policy.WorldId,WorldRevision=policy.WorldRevision,ZoneId=policy.ZoneId,MetresInside=3,WindOcclusion01=1,RainOcclusion01=1,GeometryVerified=true,ThermalVerified=true,RockAirTargetC=12,WaterBoundKnown=true,FloorKnown=true,IngressKnown=true,LowestRefugeFloorY=3,LowestConnectedIngressY=2,MaximumDesignWaterY=1};
        var interior=CaveZoneEvaluator.Evaluate(policy,outside,synthetic);
        Check("synthetic interior wind attenuated >=85 percent",interior.WindMultiplier<=.15f,interior.WindMultiplier);
        Check("synthetic interior rain attenuated >=98 percent",interior.RainMultiplier<=.02f,interior.RainMultiplier);
        Check("cold interior warmer by >=6 C",interior.AirC-outside.AirC>=6,interior.AirC-outside.AirC);
        Check("validated synthetic refuge qualifies",interior.QualifiesAsDryRefuge,new{interior.AirC,interior.FreeboardMetres});
        var p=synthetic;p.MetresInside=-1;var exposed=CaveZoneEvaluator.Evaluate(policy,outside,p);
        Check("outside is unchanged",exposed.WindX==outside.WindX&&exposed.Rain01==outside.Rain01&&exposed.AirC==outside.AirC&&!exposed.QualifiesAsDryRefuge);
        p.MetresInside=0;var doorway=CaveZoneEvaluator.Evaluate(policy,outside,p);Check("doorway starts exposed",doorway.Blend01==0&&doorway.WindMultiplier==1);
        p.MetresInside=1.5f;var half=CaveZoneEvaluator.Evaluate(policy,outside,p);Check("midpoint blend",Math.Abs(half.WindMultiplier-.55f)<.00001f&&Math.Abs(half.AirC-2)<.00001f,new{half.WindMultiplier,half.AirC});
        float maxStep=0,prior=1;bool monotonic=true;
        for(int i=0;i<=300;i++){p=synthetic;p.MetresInside=i*.01f;var s=CaveZoneEvaluator.Evaluate(policy,outside,p);maxStep=Math.Max(maxStep,Math.Abs(s.WindMultiplier-prior));monotonic&=s.WindMultiplier<=prior+.000001f;prior=s.WindMultiplier;}
        Check("entrance transition monotonic and bounded",monotonic&&maxStep<.005f,maxStep);
        p=synthetic;p.GeometryVerified=false;var missing=CaveZoneEvaluator.Evaluate(policy,outside,p);Check("unverified cave gets no shelter credit",!missing.GeometryCredited&&!missing.QualifiesAsDryRefuge&&missing.RainMultiplier==1);
        p=synthetic;p.WindOcclusion01=0;Check("open wind path disqualifies refuge",!CaveZoneEvaluator.Evaluate(policy,outside,p).QualifiesAsDryRefuge);
        p=synthetic;p.RainOcclusion01=0;Check("open rain path disqualifies refuge",!CaveZoneEvaluator.Evaluate(policy,outside,p).QualifiesAsDryRefuge);
        p=synthetic;p.ThermalVerified=false;var noHeat=CaveZoneEvaluator.Evaluate(policy,outside,p);Check("no invented warming",noHeat.AirC==outside.AirC&&!noHeat.QualifiesAsDryRefuge);
        p=synthetic;p.WaterBoundKnown=false;var unknownWater=CaveZoneEvaluator.Evaluate(policy,outside,p);Check("unknown flood maximum fails closed",!unknownWater.WaterSafetyKnown&&!unknownWater.FloodSafe&&!unknownWater.QualifiesAsDryRefuge);
        p=synthetic;p.MaximumDesignWaterY=1.26f;Check("below required freeboard fails",!CaveZoneEvaluator.Evaluate(policy,outside,p).FloodSafe);
        p=synthetic;p.MaximumDesignWaterY=1.25f;Check("exact freeboard threshold passes",CaveZoneEvaluator.Evaluate(policy,outside,p).FloodSafe);
        p=synthetic;p.LowestConnectedIngressY=.5f;Check("low connected ingress fails despite high floor",!CaveZoneEvaluator.Evaluate(policy,outside,p).FloodSafe);
        p=synthetic;p.LowestRefugeFloorY=.5f;Check("low refuge floor fails despite high ingress",!CaveZoneEvaluator.Evaluate(policy,outside,p).FloodSafe);
        p=synthetic;p.FloorKnown=false;Check("unmeasured floor fails closed",!CaveZoneEvaluator.Evaluate(policy,outside,p).WaterSafetyKnown);
        p=synthetic;p.WorldRevision="other";Check("wrong revision gets no credit",!CaveZoneEvaluator.Evaluate(policy,outside,p).GeometryCredited);
        p=synthetic;p.ZoneId="other";Check("wrong zone gets no credit",!CaveZoneEvaluator.Evaluate(policy,outside,p).GeometryCredited);
        p=synthetic;p.MaximumDesignWaterY=float.NaN;Check("NaN flood bound fails closed",!CaveZoneEvaluator.Evaluate(policy,outside,p).WaterSafetyKnown);
        p=synthetic;p.LowestRefugeFloorY=float.MaxValue;p.LowestConnectedIngressY=float.MaxValue;p.MaximumDesignWaterY=-float.MaxValue;Check("overflowing flood clearance fails closed",!CaveZoneEvaluator.Evaluate(policy,outside,p).WaterSafetyKnown);
        var invalid=outside;invalid.WindX=float.PositiveInfinity;Check("invalid weather rejected",!CaveZoneEvaluator.Evaluate(policy,invalid,synthetic).Valid);
        invalid=outside;invalid.WindX=float.MaxValue;Check("finite overflowing wind rejected",!CaveZoneEvaluator.Evaluate(policy,invalid,synthetic).Valid);
        var wet=new ZoneExposure{Wetness01=1,Cold=true};var exposedWet=wet;
        for(int i=0;i<1500;i++){wet.Step(interior,false);exposedWet.Step(exposed,false);}
        Check("wet occupant dries within 30 seconds in synthetic refuge",wet.Wetness01<=.05f,wet.Wetness01);
        Check("cold occupant recovers in synthetic refuge",!wet.Cold&&wet.ApparentC>=7,wet.ApparentC);
        Check("exposed occupant stays cold and wet",exposedWet.Cold&&exposedWet.Wetness01>.9f,new{exposedWet.Wetness01,exposedWet.ApparentC});
        var before=wet;wet.Step(exposed,true);Check("pause preserves exposure",wet.Wetness01==before.Wetness01&&wet.ApparentC==before.ApparentC&&wet.Cold==before.Cold);
        var replay=new ZoneExposure{Wetness01=1,Cold=true};for(int i=0;i<1500;i++)replay.Step(interior,false);Check("identical ticks repeat exactly",replay.Wetness01==wet.Wetness01&&replay.ApparentC==wet.ApparentC&&replay.Cold==wet.Cold);
        var restored=JsonSerializer.Deserialize<ZoneExposure>(JsonSerializer.Serialize(replay,new JsonSerializerOptions{IncludeFields=true}),new JsonSerializerOptions{IncludeFields=true});for(int i=0;i<100;i++){replay.Step(exposed,false);restored.Step(exposed,false);}Check("exposure DTO round-trip continuation",restored.Wetness01==replay.Wetness01&&restored.ApparentC==replay.ApparentC&&restored.Cold==replay.Cold);
        var retained=restored;var corrupt=restored;corrupt.Wetness01=float.NaN;Check("invalid exposure restore rejected atomically",!restored.TryRestore(corrupt)&&restored.Wetness01==retained.Wetness01&&restored.ApparentC==retained.ApparentC);
        string output=args.Length>0?args[0]:"cave-zone-checks.json";Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output,JsonSerializer.Serialize(new{contract=CaveZonePolicy.Contract,status=failures==0?"PASS":"FAIL",layer="Compiled .NET 8 tests of shared C# contract; synthetic geometry/thermal/water inputs. Not Unity cave acceptance.",assertions=checks.Count,failures,checks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{checks.Count} cave-zone checks; {failures} failures; {output}");return failures==0?0:1;
    }
}
