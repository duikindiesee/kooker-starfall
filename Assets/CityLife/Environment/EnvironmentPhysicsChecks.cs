using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starfall.EnvironmentFoundation
{
    public static class EnvironmentPhysicsChecks
    {
        [Serializable] public sealed class Result {public string name;public bool passed; public float measured,expected,tolerance;}
        public static List<Result> Run()
        {
            var results=new List<Result>();
            void Check(string n,float m,float e,float t){results.Add(new Result{name=n,measured=m,expected=e,tolerance=t,passed=EnvironmentClock.Finite(m)&&Mathf.Abs(m-e)<=t});}
            var scene=SceneManager.CreateScene("Environment isolated physics tests",new CreateSceneParameters(LocalPhysicsMode.Physics3D));var physics=scene.GetPhysicsScene();
            var owned=new List<GameObject>();
            GameObject Box(string n,Vector3 p,Vector3 scale){var g=new GameObject(n);g.layer=8;SceneManager.MoveGameObjectToScene(g,scene);g.transform.position=p;g.transform.localScale=scale;g.AddComponent<BoxCollider>();owned.Add(g);return g;}
            Rigidbody Body(string n,Vector3 p,float mass){var g=Box(n,p,Vector3.one);g.layer=0;var b=g.AddComponent<Rigidbody>();b.mass=mass;b.useGravity=true;b.linearDamping=0;b.angularDamping=0;b.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;return b;}
            void Sim(int count){for(int i=0;i<count;i++)physics.Simulate(.02f);}
            try
            {
                Box("floor",new Vector3(0,-.5f,0),new Vector3(100,1,100));
                var light=Body("1 kg drop",new Vector3(-5,10,0),1);var heavy=Body("100 kg drop",new Vector3(5,10,0),100);
                Sim(25);float expected=10-9.81f*.02f*.02f*25*26/2;
                Check("gravity semi-implicit displacement 0.5s",light.position.y,expected,.025f);Check("mass independent gravity",heavy.position.y,light.position.y,.001f);
                Check("gravity velocity 0.5s",light.linearVelocity.y,-4.905f,.02f);
                Sim(150);Check("floor stops falling",light.position.y,.5f,.035f);Check("heavy floor contact",heavy.position.y,.5f,.035f);
                Box("wall",new Vector3(0,2,8),new Vector3(20,4,.2f));var projectile=Body("fast body",new Vector3(0,1,0),1);projectile.linearVelocity=Vector3.forward*30;Sim(30);Check("continuous wall collision",Mathf.Max(0,projectile.position.z-7.5f),0,.05f);
                var locked=Body("constraint",new Vector3(10,2,0),2);locked.constraints=RigidbodyConstraints.FreezePositionX|RigidbodyConstraints.FreezeRotation;for(int i=0;i<50;i++){locked.AddForce(new Vector3(100,0,0));physics.Simulate(.02f);}Check("position constraint under force",locked.position.x,10,.001f);
                var windLight=Body("wind light",new Vector3(-10,3,-5),1);var windHeavy=Body("wind heavy",new Vector3(10,3,-5),10);windLight.useGravity=windHeavy.useGravity=false;
                for(int i=0;i<25;i++){windLight.AddForce(Vector3.right*2);windHeavy.AddForce(Vector3.right*2);physics.Simulate(.02f);}
                Check("force mass acceleration light",windLight.linearVelocity.x,1,.005f);Check("force mass acceleration heavy",windHeavy.linearVelocity.x,.1f,.005f);
                var floating=Body("buoyancy test",new Vector3(20,-3,0),10);floating.useGravity=true;
                // Isolated buoyancy column below the floor is moved outside its footprint.
                floating.position=new Vector3(60,-3,0);
                for(int i=0;i<300;i++){floating.AddForce(EnvironmentForces.Water(0,floating.position.y,floating.linearVelocity,floating.mass,.02f));physics.Simulate(.02f);}
                Check("buoyant body approaches water equilibrium",floating.position.y,0,.3f);
                Check("buoyant body settles vertical speed",floating.linearVelocity.y,0,.1f);
                var slope=Box("30 degree slope",new Vector3(-20,1,0),new Vector3(6,.2f,6));slope.transform.rotation=Quaternion.Euler(0,0,30);Physics.SyncTransforms();
                bool hit=physics.Raycast(new Vector3(-20,8,0),Vector3.down,out RaycastHit ground,20);
                Check("slope collider normal 30 degrees",hit?Vector3.Angle(ground.normal,Vector3.up):-999,30,.1f);
                slope.transform.rotation=Quaternion.Euler(0,0,60);Physics.SyncTransforms();hit=physics.Raycast(new Vector3(-20,8,0),Vector3.down,out ground,20);
                Check("slope collider normal 60 degrees",hit?Vector3.Angle(ground.normal,Vector3.up):-999,60,.1f);
                var host=new GameObject("isolated controller environment");owned.Add(host);SceneManager.MoveGameObjectToScene(host,scene);
                var world=host.AddComponent<EnvironmentWorld>();world.enabled=false;world.Surface=new ProbeSurface(physics);
                var person=new GameObject("swept capsule test");owned.Add(person);SceneManager.MoveGameObjectToScene(person,scene);person.transform.position=new Vector3(30,2,-20);person.AddComponent<CharacterController>();var walker=person.AddComponent<EnvironmentWalker>();walker.enabled=false;walker.World=world;
                void Walk(int count,Vector3 direction){walker.InputDirection=direction;for(int i=0;i<count;i++){walker.Step(.02f);physics.Simulate(.02f);}}
                void Place(Vector3 point){var cc=person.GetComponent<CharacterController>();cc.enabled=false;person.transform.position=point;cc.enabled=true;Physics.SyncTransforms();Walk(5,Vector3.zero);}
                Walk(100,Vector3.zero);Check("capsule falls and grounds",person.transform.position.y,0,.08f);
                Box("capsule wall",new Vector3(30,2,-15),new Vector3(5,4,.2f));Physics.SyncTransforms();Walk(150,Vector3.forward);
                Check("capsule sweep blocks wall",Mathf.Max(0,person.transform.position.z+15.35f),0,.04f);
                var gentle=Box("walkable ramp",new Vector3(-30,2.5f,-20),new Vector3(10,.2f,6));gentle.transform.rotation=Quaternion.Euler(0,0,30);
                Place(new Vector3(-35,0,-20));Walk(125,Vector3.right);Check("capsule climbs gentle slope",person.transform.position.y>2?1:0,1,0);
                var steep=Box("steep ramp",new Vector3(-10,4.33f,-20),new Vector3(10,.2f,6));steep.transform.rotation=Quaternion.Euler(0,0,60);
                Place(new Vector3(-14,0,-20));Walk(125,Vector3.right);Check("capsule rejects steep slope",person.transform.position.x < -12?1:0,1,0);
                Place(new Vector3(30,0,18));Walk(100,Vector3.forward);Check("capsule dry shore limit",Mathf.Max(0,person.transform.position.z-20),0,.001f);
                Place(new Vector3(48,0,-30));Walk(100,Vector3.right);Check("capsule finite edge",Mathf.Max(0,person.transform.position.x-49),0,.001f);
                walker.SafePosition=new Vector3(30,0,-30);Place(new Vector3(100,-40,0));Walk(2,Vector3.zero);Check("capsule recovers out of bounds",Vector3.Distance(person.transform.position,walker.SafePosition),0,.08f);
            }
            finally {foreach(var g in owned)UnityEngine.Object.DestroyImmediate(g);SceneManager.UnloadSceneAsync(scene);}
            return results;
        }
        sealed class ProbeSurface:IEnvironmentSurface
        {
            readonly PhysicsScene physics;public ProbeSurface(PhysicsScene p){physics=p;}
            public string WorldId=>"physics.fixture";public string Revision=>"v1";
            public Bounds PhysicalBounds=>new Bounds(new Vector3(0,40,0),new Vector3(98,120,98));
            public bool Contains(Vector3 p)=>PhysicalBounds.Contains(p);
            public bool TryGround(Vector3 p,out float h,out Vector3 normal){bool found=physics.Raycast(new Vector3(p.x,90,p.z),Vector3.down,out var hit,200,1<<8);h=found?hit.point.y:0;normal=found?hit.normal:Vector3.up;return found;}
            public float WaterLevel(Vector3 p)=>2;public float WaterDepth(Vector3 p)=>p.z>20?2:0;public Vector3 Current(Vector3 p)=>Vector3.zero;
        }
    }
}
