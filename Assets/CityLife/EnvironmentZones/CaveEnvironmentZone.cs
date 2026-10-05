using System;

namespace Starfall.EnvironmentZones
{
    // Pure SI-unit contract. No Unity scene, clock, physics or accepted-baseline dependency.
    public sealed class CaveZonePolicy
    {
        public const string Contract="starfall.environment-zone.v1";
        public readonly string WorldId,WorldRevision,ZoneId;
        public readonly float BlendMetres,InteriorWindMultiplier,InteriorRainMultiplier,FreeboardMetres;
        public CaveZonePolicy(string world,string revision,string zone,float blendMetres=3,float windMultiplier=.1f,float rainMultiplier=.01f,float freeboardMetres=.75f)
        {
            if(string.IsNullOrWhiteSpace(world)||string.IsNullOrWhiteSpace(revision)||string.IsNullOrWhiteSpace(zone)||!Finite(blendMetres)||blendMetres<=0||!Finite(windMultiplier)||windMultiplier<0||windMultiplier>1||!Finite(rainMultiplier)||rainMultiplier<0||rainMultiplier>1||!Finite(freeboardMetres)||freeboardMetres<.5f)throw new ArgumentException("Invalid cave-zone policy; retain prior validated policy.");
            WorldId=world;WorldRevision=revision;ZoneId=zone;BlendMetres=blendMetres;InteriorWindMultiplier=windMultiplier;InteriorRainMultiplier=rainMultiplier;FreeboardMetres=freeboardMetres;
        }
        public static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
    }
    public struct OutdoorWeather
    {
        public float WindX,WindY,WindZ,AirC,Rain01;
    }
    public struct CaveProbe
    {
        public string WorldId,WorldRevision,ZoneId;
        // Signed distance along the validated entrance-to-interior path, not a generic box SDF.
        public float MetresInside,WindOcclusion01,RainOcclusion01;
        public bool GeometryVerified,ThermalVerified,WaterBoundKnown,FloorKnown,IngressKnown;
        public float RockAirTargetC,LowestRefugeFloorY,LowestConnectedIngressY,MaximumDesignWaterY;
    }
    public struct ZoneWeather
    {
        public bool Valid,GeometryCredited,ThermalCredited,WaterSafetyKnown,FloodSafe,QualifiesAsDryRefuge;
        public float Blend01,WindMultiplier,RainMultiplier,WindX,WindY,WindZ,AirC,Rain01,FreeboardMetres;
        public float WindSpeed=>(float)Math.Sqrt(WindX*WindX+WindY*WindY+WindZ*WindZ);
    }
    public static class CaveZoneEvaluator
    {
        static float Lerp(float a,float b,float t)=>a+(b-a)*t;
        static bool Unit(float f)=>CaveZonePolicy.Finite(f)&&f>=0&&f<=1;
        public static ZoneWeather Evaluate(CaveZonePolicy policy,OutdoorWeather outside,CaveProbe probe)
        {
            if(policy==null)throw new ArgumentNullException(nameof(policy));
            var value=new ZoneWeather{WindMultiplier=1,RainMultiplier=1};
            if(!CaveZonePolicy.Finite(outside.WindX)||!CaveZonePolicy.Finite(outside.WindY)||!CaveZonePolicy.Finite(outside.WindZ)||!CaveZonePolicy.Finite(outside.AirC)||outside.AirC < -80||outside.AirC>60||!Unit(outside.Rain01))return value;
            if((double)outside.WindX*outside.WindX+(double)outside.WindY*outside.WindY+(double)outside.WindZ*outside.WindZ>900)return value;
            value.Valid=true;value.WindX=outside.WindX;value.WindY=outside.WindY;value.WindZ=outside.WindZ;value.AirC=outside.AirC;value.Rain01=outside.Rain01;
            if(probe.WorldId!=policy.WorldId||probe.WorldRevision!=policy.WorldRevision||probe.ZoneId!=policy.ZoneId||!probe.GeometryVerified||!CaveZonePolicy.Finite(probe.MetresInside)||!Unit(probe.WindOcclusion01)||!Unit(probe.RainOcclusion01))return value;
            float t=Math.Max(0,Math.Min(1,probe.MetresInside/policy.BlendMetres));t=t*t*(3-2*t);value.Blend01=t;value.GeometryCredited=t>0;
            value.WindMultiplier=Lerp(1,policy.InteriorWindMultiplier,t*probe.WindOcclusion01);
            value.RainMultiplier=Lerp(1,policy.InteriorRainMultiplier,t*probe.RainOcclusion01);
            value.WindX*=value.WindMultiplier;value.WindY*=value.WindMultiplier;value.WindZ*=value.WindMultiplier;value.Rain01*=value.RainMultiplier;
            value.ThermalCredited=t>0&&probe.ThermalVerified&&CaveZonePolicy.Finite(probe.RockAirTargetC)&&probe.RockAirTargetC>=-30&&probe.RockAirTargetC<=30;
            if(value.ThermalCredited)value.AirC=Lerp(outside.AirC,probe.RockAirTargetC,t);
            value.WaterSafetyKnown=probe.WaterBoundKnown&&probe.FloorKnown&&probe.IngressKnown&&CaveZonePolicy.Finite(probe.LowestRefugeFloorY)&&CaveZonePolicy.Finite(probe.LowestConnectedIngressY)&&CaveZonePolicy.Finite(probe.MaximumDesignWaterY);
            if(value.WaterSafetyKnown){float margin=Math.Min(probe.LowestRefugeFloorY,probe.LowestConnectedIngressY)-probe.MaximumDesignWaterY;if(CaveZonePolicy.Finite(margin)){value.FreeboardMetres=margin;value.FloodSafe=margin>=policy.FreeboardMetres;}else value.WaterSafetyKnown=false;}
            value.QualifiesAsDryRefuge=t>=1&&value.WindMultiplier<=.15f&&value.RainMultiplier<=.02f&&value.ThermalCredited&&value.AirC>=7&&(outside.AirC>5||value.AirC-outside.AirC>=6)&&value.FloodSafe;
            return value;
        }
    }
    public struct ZoneExposure
    {
        public float Wetness01,ApparentC;public bool Cold;
        public bool TryRestore(ZoneExposure candidate)
        {
            if(!CaveZonePolicy.Finite(candidate.Wetness01)||candidate.Wetness01<0||candidate.Wetness01>1||!CaveZonePolicy.Finite(candidate.ApparentC)||candidate.ApparentC < -100||candidate.ApparentC>100)return false;
            this=candidate;return true;
        }
        // Caller invokes once per accepted 50 Hz world tick. No private wall-clock accumulator.
        public void Step(ZoneWeather local,bool paused)
        {
            if(paused||!local.Valid)return;
            float shelteredDrying=.04f*local.Blend01*(1-local.RainMultiplier);
            Wetness01=Math.Max(0,Math.Min(1,Wetness01+(.025f*local.Rain01-.008f-shelteredDrying)*.02f));
            ApparentC=local.AirC-.35f*local.WindSpeed-5*Wetness01;
            if(ApparentC<=5)Cold=true;else if(ApparentC>=7)Cold=false;
        }
    }
}
