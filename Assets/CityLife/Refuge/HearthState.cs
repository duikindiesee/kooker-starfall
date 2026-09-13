using System;
namespace Starfall.Refuge
{
    // Integer fuel and 50 Hz ticks make combustion independent of rendering.
    public sealed class HearthState
    {
        public const int MaximumFuel=18000; // six minutes at 50 Hz
        public int FuelTicks {get;private set;}=6000;
        public int ReserveLogs {get;private set;}=6;
        public bool Burning {get;private set;}
        public long Tick {get;private set;}
        public string Reason {get;private set;}="Cold hearth";
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        public bool Ignite(float rain,float wind,bool safe)
        {
            if(!Valid(rain,wind)||!safe||rain>.2f||wind>12||FuelTicks<=0){Burning=false;Reason="Ignition refused: unsafe, wet, windy or empty";return false;}
            Burning=true;Reason="Contained fire";return true;
        }
        static bool Valid(float rain,float wind)=>Finite(rain)&&Finite(wind)&&rain>=0&&rain<=1&&wind>=0&&wind<=30;
        public bool AddLog(){if(ReserveLogs<=0||FuelTicks>MaximumFuel-1500)return false;ReserveLogs--;FuelTicks+=1500;return true;}
        public void Extinguish(){Burning=false;Reason="Extinguished";}
        public void Step(float rain,float wind,bool safe,bool paused=false)
        {
            if(paused)return;
            Tick++;
            if(!Valid(rain,wind)||!safe){Burning=false;Reason="Fail-safe extinguish";return;}
            if(!Burning)return;
            if(rain>.35f||wind>18){Burning=false;Reason="Weather extinguished fire";return;}
            FuelTicks=Math.Max(0,FuelTicks-1-(wind>8?1:0));
            if(FuelTicks==0){Burning=false;Reason="Fuel exhausted";}
        }
        public float HeatAt(float distance)=>Burning&&Finite(distance)&&distance>=0?8f*Math.Max(0,1-distance/3f):0;
    }
}
