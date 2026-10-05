using System;
using System.IO;
using UnityEngine;
using CityLife.World;

namespace Starfall.EnvironmentFoundation
{
    public enum WeatherKind { Clear, Rain, Cold, Storm }
    public struct WeatherSample { public Vector3 wind; public float temperature, precipitation; public WeatherKind target; }
    [Serializable] public sealed class EnvironmentSave
    {
        public string contract, world, revision; public int seed; public long tick; public float wetness,apparentTemperature; public bool cold;
        public float playerWetness,playerApparentTemperature;public bool playerCold;
    }
    public sealed class EnvironmentClock
    {
        public const string Contract="starfall.environment.v1";
        public const float Dt=.02f;
        public long Tick; public int Seed {get;private set;} public bool Paused;
        public EnvironmentClock(int seed){Seed=seed;}
        public void Step(){if(!Paused && Tick<long.MaxValue-1)Tick++;}
        static uint Hash(uint x){x^=x>>16;x*=0x7feb352d;x^=x>>15;x*=0x846ca68b;return x^(x>>16);}
        WeatherSample Target(long segment)
        {
            int kind=(int)(segment%4); float angle=(Hash(unchecked((uint)(Seed+segment)))%360)*Mathf.Deg2Rad;
            float speed=kind==0?2:kind==1?7:kind==2?11:20;
            return new WeatherSample{wind=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*speed,
                temperature=kind==0?22:kind==1?12:kind==2?-8:3,precipitation=kind==0?0:kind==1?.7f:kind==2?.4f:1,target=(WeatherKind)kind};
        }
        public WeatherSample Sample
        {
            get
            {
                long segment=Tick/1500; var to=Target(segment);var from=Target(Math.Max(0,segment-1));
                float t=Mathf.SmoothStep(0,1,Mathf.Clamp01((Tick%1500)/250f));
                return new WeatherSample{wind=Vector3.Lerp(from.wind,to.wind,t),temperature=Mathf.Lerp(from.temperature,to.temperature,t),precipitation=Mathf.Lerp(from.precipitation,to.precipitation,t),target=to.target};
            }
        }
        public static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        public EnvironmentSave Save(string world,string revision,float wetness)=>new EnvironmentSave{contract=Contract,world=world,revision=revision,seed=Seed,tick=Tick,wetness=wetness};
        public bool TryLoad(EnvironmentSave s,string world,string revision)
        {
            if(s==null||s.contract!=Contract||s.world!=world||s.revision!=revision||s.tick<0||s.tick>1000000000000L||!Finite(s.wetness)||s.wetness<0||s.wetness>1||!Finite(s.apparentTemperature)||s.apparentTemperature < -100||s.apparentTemperature>100||!Finite(s.playerWetness)||s.playerWetness<0||s.playerWetness>1||!Finite(s.playerApparentTemperature)||s.playerApparentTemperature < -100||s.playerApparentTemperature>100)return false;
            Seed=s.seed;Tick=s.tick;return true;
        }
        public static void WriteSave(string path,EnvironmentSave state)
        {
            string tmp=path+".tmp";File.WriteAllText(tmp,JsonUtility.ToJson(state,true));
            if(File.Exists(path))File.Replace(tmp,path,path+".bak");else File.Move(tmp,path);
        }
    }
    [Serializable] public sealed class ExposureState
    {
        public float Wetness, ApparentTemperature=22; public bool Cold;
        public void Step(float air,float wind,float rain,bool sheltered)
        {
            Wetness=Mathf.Clamp01(Wetness+(sheltered?-.06f:rain*.025f-.008f)*EnvironmentClock.Dt);
            ApparentTemperature=air-(sheltered?0:wind*.35f)-Wetness*5+(sheltered?8:0);
            if(ApparentTemperature<=5)Cold=true;else if(ApparentTemperature>=7)Cold=false;
        }
    }
    public interface IEnvironmentSurface
    {
        string WorldId {get;} string Revision {get;}
        Bounds PhysicalBounds {get;} bool Contains(Vector3 p);
        bool TryGround(Vector3 p,out float height,out Vector3 normal);
        float WaterLevel(Vector3 p); float WaterDepth(Vector3 p); Vector3 Current(Vector3 p);
    }
    public sealed class CoastalEnvironmentSurface : IEnvironmentSurface
    {
        public string WorldId=>CoastalTerrain.DefinitionId; public string Revision=>CoastalTerrain.ContentRevision;
        public Bounds PhysicalBounds=>new Bounds(new Vector3(0,50,45),new Vector3(180,300,200));
        public bool Contains(Vector3 p)=>EnvironmentClock.Finite(p.x)&&EnvironmentClock.Finite(p.y)&&EnvironmentClock.Finite(p.z)&&PhysicalBounds.Contains(p);
        public bool TryGround(Vector3 p,out float height,out Vector3 normal)
        {
            height=0;normal=Vector3.up;if(!Contains(p))return false;
            height=CoastalTerrain.Height(p.x,p.z);const float e=.25f;
            normal=new Vector3(CoastalTerrain.Height(p.x-e,p.z)-CoastalTerrain.Height(p.x+e,p.z),2*e,CoastalTerrain.Height(p.x,p.z-e)-CoastalTerrain.Height(p.x,p.z+e)).normalized;return true;
        }
        public float WaterLevel(Vector3 p)=>CoastalWater.Level;
        public float WaterDepth(Vector3 p)=>TryGround(p,out float h,out _)?Mathf.Max(0,CoastalWater.Level-h):0;
        public Vector3 Current(Vector3 p)=>Vector3.zero; // No invented river flow field.
    }
}
