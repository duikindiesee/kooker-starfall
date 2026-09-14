using System;
using Starfall.Refuge;
class Program {
static int count; static void Check(bool v,string n){if(!v)throw new Exception(n);count++;Console.WriteLine("PASS "+n);}
static void Main(){
var a=new HearthState();var b=new HearthState();Check(a.Ignite(0,2,true),"dry ignition");b.Ignite(0,2,true);
for(int i=0;i<10000;i++){a.Step(.01f,2,true);b.Step(.01f,2,true);Check(a.FuelTicks==b.FuelTicks&&a.Burning==b.Burning,"replay "+i);}
Check(!a.Burning&&a.FuelTicks==0,"fuel depletion bounded");Check(a.AddLog()&&a.Ignite(0,0,true),"refuel after depletion");int f=a.FuelTicks;long t=a.Tick;a.Step(0,0,true,true);Check(f==a.FuelTicks&&t==a.Tick,"pause");a.Step(1,0,true);Check(!a.Burning,"rain extinguishes");Check(!a.Ignite(.3f,0,true),"wet ignition rejected");Check(!a.Ignite(0,13,true),"wind ignition rejected");Check(!a.Ignite(0,0,false),"unsafe site rejected");a.Ignite(0,0,true);a.Step(0,float.NaN,true);Check(!a.Burning,"nonfinite fail safe");a.Ignite(0,0,true);a.Step(0,20,true);Check(!a.Burning,"storm extinguishes");a.Ignite(0,0,true);Check(a.HeatAt(0)==8&&a.HeatAt(3)==0&&a.HeatAt(float.NaN)==0,"heat bounded");a.Extinguish();Check(a.HeatAt(0)==0,"extinguish removes heat");for(int i=0;i<20;i++)a.AddLog();Check(a.ReserveLogs==0&&a.FuelTicks<=HearthState.MaximumFuel,"finite storage fuel");Console.WriteLine("TOTAL "+count);
}}
