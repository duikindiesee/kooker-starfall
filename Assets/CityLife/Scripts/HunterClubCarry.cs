using UnityEngine;
namespace CityLife.World
{
 // Cosmetic left-hand prop only: no collider, damage, targeting or action authority.
 // Keep the heavy end clear of the feet while the original right-hand pickup remains free.
 [DefaultExecutionOrder(150)]
 public sealed class HunterClubCarry : MonoBehaviour
 {
  public Animator Animator; public Transform Actor,Club; public float Length=.62f;
  public float GroundClearance {get;private set;}
  private void LateUpdate(){if(!Animator||!Club)return;var hand=Animator.GetBoneTransform(HumanBodyBones.LeftHand);
   var fingers=Animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
   Club.position=Vector3.Lerp(hand.position,fingers.position,.65f);
   float height=Club.position.y-Actor.position.y;
   float down=Mathf.Clamp((height-.20f)/Length,.05f,.92f);
   Vector3 direction=Vector3.down*down-Actor.right*Mathf.Sqrt(1-down*down);
   Club.rotation=Quaternion.FromToRotation(Vector3.down,direction);
   GroundClearance=Club.GetComponent<Renderer>().bounds.min.y-Actor.position.y;
  }
 }
}
