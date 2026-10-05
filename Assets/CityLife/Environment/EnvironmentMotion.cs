using UnityEngine;

namespace Starfall.EnvironmentFoundation
{
    public static class EnvironmentForces
    {
        public static Vector3 Wind(Vector3 wind,Vector3 velocity,float area)
        {var relative=wind-velocity;return Vector3.ClampMagnitude(.5f*1.225f*Mathf.Max(0,area)*relative.magnitude*relative,200);}
        public static Vector3 Water(float level,float y,Vector3 velocity,float mass,float volume)
        {float fraction=Mathf.Clamp01(level-y+.5f);return Vector3.up*Mathf.Min(2000,1000*9.81f*volume*fraction)-velocity*(fraction*mass*12);}
    }
    public sealed class EnvironmentBody : MonoBehaviour
    {
        public EnvironmentWorld World; public float DragArea=.2f, Volume; public Vector3 SafePosition;
        Rigidbody body;
        public Vector3 LastWindForce {get;private set;}
        void Awake(){body=GetComponent<Rigidbody>();SafePosition=transform.position;body.mass=Mathf.Clamp(body.mass,.1f,1000);body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;}
        void FixedUpdate()
        {
            if(World==null||World.Clock.Paused)return;
            if(!World.Surface.Contains(body.position)||!EnvironmentClock.Finite(body.linearVelocity.sqrMagnitude)) {body.position=SafePosition;body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;return;}
            LastWindForce=EnvironmentForces.Wind(World.Clock.Sample.wind,body.linearVelocity,DragArea);
            body.AddForce(LastWindForce);
            if(Volume>0&&World.Surface.WaterDepth(body.position)>0)
            {
                body.AddForce(EnvironmentForces.Water(World.Surface.WaterLevel(body.position),body.position.y,body.linearVelocity,body.mass,Volume));
            }
            body.linearVelocity=Vector3.ClampMagnitude(body.linearVelocity,30);
        }
    }
    [RequireComponent(typeof(CharacterController))]
    public sealed class EnvironmentWalker : MonoBehaviour
    {
        public EnvironmentWorld World;public Vector3 InputDirection;public bool Jump;
        public float VerticalSpeed {get;private set;} public Vector3 SafePosition; public string Boundary="";
        CharacterController controller;
        void Awake(){controller=GetComponent<CharacterController>();controller.height=1.8f;controller.radius=.35f;controller.center=new Vector3(0,.9f,0);controller.slopeLimit=45;controller.stepOffset=.25f;controller.skinWidth=.035f;SafePosition=transform.position;}
        public void Step(float dt)
        {
            if(World==null||World.Clock.Paused)return;
            Vector3 direction=Vector3.ClampMagnitude(new Vector3(InputDirection.x,0,InputDirection.z),1);
            Vector3 proposed=transform.position+direction*4*dt;Boundary="";
            if(!World.Surface.Contains(proposed)){direction=Vector3.zero;Boundary="Finite world edge";}
            else if(World.Surface.WaterDepth(proposed)>.3f){direction=Vector3.zero;Boundary="Deep water: swimming unavailable";}
            else if(World.Surface.TryGround(proposed,out float h,out Vector3 normal)&&h>transform.position.y+.05f&&normal.y<Mathf.Cos(45*Mathf.Deg2Rad)){direction=Vector3.zero;Boundary="Slope exceeds 45 degrees";}
            if(controller.isGrounded&&VerticalSpeed<0){VerticalSpeed=-2;if(World.Surface.WaterDepth(transform.position)<=.3f)SafePosition=transform.position;}
            if(Jump&&controller.isGrounded)VerticalSpeed=5;Jump=false;
            VerticalSpeed=Mathf.Max(-40,VerticalSpeed-9.81f*dt);
            var collision=controller.Move((direction*4+Vector3.up*VerticalSpeed)*dt);
            if((collision&CollisionFlags.Above)!=0&&VerticalSpeed>0)VerticalSpeed=0;
            if(!World.Surface.Contains(transform.position)||transform.position.y<-30||
                (World.Surface.WaterDepth(transform.position)>.3f&&transform.position.y<World.Surface.WaterLevel(transform.position)))
            {controller.enabled=false;transform.position=SafePosition;controller.enabled=true;VerticalSpeed=0;Boundary="Recovered to dry safe ground";}
        }
        void FixedUpdate(){Step(EnvironmentClock.Dt);}
    }
}
