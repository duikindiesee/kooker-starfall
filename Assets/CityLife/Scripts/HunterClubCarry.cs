using UnityEngine;
namespace CityLife.World
{
 // Cosmetic left-hand prop only: no collider, damage, targeting or action authority.
 // Keep the heavy end clear of the feet while the original right-hand pickup remains free.
 [DefaultExecutionOrder(150)]
 public sealed class HunterClubCarry : MonoBehaviour
 {
  public Animator Animator; public Transform Actor,Club; public float Length=.62f;
  public Transform[] GripBones; public Quaternion[] GripRotations;
  public Vector3 PalmAnchor, ShaftAxis; public Quaternion HandBasis;
  public Vector3[] FlexAxes; public Vector3 PalmAlong,PalmNormal;
  public Vector3 DiagnosticCurlAdjustment; public Vector2 DiagnosticAnchorAdjustment;
  public float ForearmSlope=.24f,ElbowOut=.22f;
  public float GroundClearance {get;private set;}
  public Vector3 GripCenter {get;private set;}
  // Author once in the imported bind pose, before the Animator evaluates.
  public void AuthorGrip() {
   var hand=Animator.GetBoneTransform(HumanBodyBones.LeftHand);
   var index=Animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
   var little=Animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
   var middle=Animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
   Vector3 across=(little.position-index.position).normalized;
   Vector3 along=(middle.position-hand.position).normalized;
   Vector3 palm=Vector3.Cross(along,across).normalized;
   PalmAnchor=hand.InverseTransformPoint(middle.position-along*.005f+palm*.028f);
   ShaftAxis=hand.InverseTransformDirection(across);
   PalmAlong=hand.InverseTransformDirection(along);PalmNormal=hand.InverseTransformDirection(palm);
   HandBasis=Quaternion.LookRotation(ShaftAxis,hand.InverseTransformDirection(along));
   var names=new[]{HumanBodyBones.LeftIndexProximal,HumanBodyBones.LeftIndexIntermediate,HumanBodyBones.LeftIndexDistal,
    HumanBodyBones.LeftMiddleProximal,HumanBodyBones.LeftMiddleIntermediate,HumanBodyBones.LeftMiddleDistal,
    HumanBodyBones.LeftRingProximal,HumanBodyBones.LeftRingIntermediate,HumanBodyBones.LeftRingDistal,
    HumanBodyBones.LeftLittleProximal,HumanBodyBones.LeftLittleIntermediate,HumanBodyBones.LeftLittleDistal};
   GripBones=new Transform[names.Length];GripRotations=new Quaternion[names.Length];FlexAxes=new Vector3[names.Length];
   for(int i=0;i<names.Length;i++) {
    var bone=Animator.GetBoneTransform(names[i]);GripBones[i]=bone;
    var localAxis=bone.InverseTransformDirection(across);
    FlexAxes[i]=localAxis;
    // Negative rotation about index-to-little bends toward the palm, not the back of the hand.
    float flex=i%3==0?new[]{50f,50f,45f,40f}[i/3]:i%3==1?70f:60f;
    GripRotations[i]=bone.localRotation*Quaternion.AngleAxis(-flex,localAxis);
   }
  }
  private void LateUpdate(){if(!Animator||!Club||GripBones==null)return;
   var upper=Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
   var forearm=Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
   var hand=Animator.GetBoneTransform(HumanBodyBones.LeftHand);
   // A relaxed bent elbow supports the weight. Preserve animated shoulder/root motion;
   // align the wrist with the forearm instead of twisting it to chase a world-space prop.
   Vector3 upperDirection=(Vector3.down-Actor.right*ElbowOut).normalized;
   upper.rotation=Quaternion.FromToRotation(forearm.position-upper.position,upperDirection)*upper.rotation;
   Vector3 forearmDirection=(Actor.forward+Vector3.up*ForearmSlope).normalized;
   forearm.rotation=Quaternion.FromToRotation(hand.position-forearm.position,forearmDirection)*forearm.rotation;
   Vector3 shaftDirection=Vector3.ProjectOnPlane(Vector3.down,forearmDirection).normalized;
   hand.rotation=Quaternion.LookRotation(shaftDirection,forearmDirection)*Quaternion.Inverse(HandBasis);
   for(int i=0;i<GripBones.Length;i++)GripBones[i].localRotation=GripRotations[i]*Quaternion.AngleAxis(-DiagnosticCurlAdjustment[i%3],FlexAxes[i]);
   GripCenter=hand.TransformPoint(PalmAnchor+PalmAlong*DiagnosticAnchorAdjustment.x+PalmNormal*DiagnosticAnchorAdjustment.y);Club.position=GripCenter;
   Club.rotation=Quaternion.FromToRotation(Vector3.down,hand.TransformDirection(ShaftAxis));
   GroundClearance=Club.GetComponent<Renderer>().bounds.min.y-Actor.position.y;
  }
 }
}
