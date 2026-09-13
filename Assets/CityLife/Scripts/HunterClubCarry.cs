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
  public float ForearmSlope=-1.5f,ElbowOut=.22f,WristDeviation=30f;
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
   GripBones=new Transform[names.Length+3];GripRotations=new Quaternion[names.Length+3];FlexAxes=new Vector3[names.Length+3];
   for(int i=0;i<names.Length;i++) {
    var bone=Animator.GetBoneTransform(names[i]);GripBones[i]=bone;
    var localAxis=bone.InverseTransformDirection(across);
    FlexAxes[i]=localAxis;
    // Negative rotation about index-to-little bends toward the palm, not the back of the hand.
    float flex=i%3==0?new[]{40f,40f,35f,30f}[i/3]:i%3==1?50f:30f;
    GripRotations[i]=bone.localRotation*Quaternion.AngleAxis(-flex,localAxis);
   }
   var thumb=new[]{Animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal),Animator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate),Animator.GetBoneTransform(HumanBodyBones.LeftThumbDistal)};
   var original=new[]{thumb[0].localRotation,thumb[1].localRotation,thumb[2].localRotation};
   var tip=thumb[2].GetChild(0);
   Vector3 target=middle.position+along*.002f+palm*.050f-across*.025f;
   for(int iteration=0;iteration<20;iteration++)for(int joint=2;joint>=0;joint--) {
    var bone=thumb[joint];var turn=Quaternion.FromToRotation(tip.position-bone.position,target-bone.position);
    bone.rotation=Quaternion.RotateTowards(Quaternion.identity,turn,12f)*bone.rotation;
   }
   for(int i=0;i<3;i++){GripBones[12+i]=thumb[i];GripRotations[12+i]=thumb[i].localRotation;}
   for(int i=0;i<3;i++)thumb[i].localRotation=original[i];
  }
  private void LateUpdate(){if(!Animator||!Club||GripBones==null)return;
   var upper=Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
   var forearm=Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
   var hand=Animator.GetBoneTransform(HumanBodyBones.LeftHand);
   // A relaxed bent elbow supports the weight. Preserve animated shoulder/root motion;
   // align the wrist with the forearm instead of twisting it to chase a world-space prop.
   Vector3 upperDirection=(Vector3.down-Actor.right*ElbowOut).normalized;
   upper.rotation=Quaternion.FromToRotation(forearm.position-upper.position,upperDirection)*upper.rotation;
   float lowPose=Mathf.InverseLerp(1.25f,.85f,upper.position.y-Actor.position.y);
   float slope=Mathf.Lerp(ForearmSlope,.3f,lowPose);
   Vector3 forearmDirection=(Actor.forward+Vector3.up*slope).normalized;
   forearm.rotation=Quaternion.FromToRotation(hand.position-forearm.position,forearmDirection)*forearm.rotation;
   Vector3 handDirection=Quaternion.AngleAxis(-WristDeviation,Actor.right)*forearmDirection;
   Vector3 shaftDirection=Vector3.ProjectOnPlane(Vector3.down,handDirection).normalized;
   hand.rotation=Quaternion.LookRotation(shaftDirection,handDirection)*Quaternion.Inverse(HandBasis);
   for(int i=0;i<GripBones.Length;i++)GripBones[i].localRotation=i<12?GripRotations[i]*Quaternion.AngleAxis(-DiagnosticCurlAdjustment[i%3],FlexAxes[i]):GripRotations[i];
   GripCenter=hand.TransformPoint(PalmAnchor+PalmAlong*DiagnosticAnchorAdjustment.x+PalmNormal*DiagnosticAnchorAdjustment.y);Club.position=GripCenter;
   Club.rotation=Quaternion.FromToRotation(Vector3.down,hand.TransformDirection(ShaftAxis));
   GroundClearance=Club.GetComponent<Renderer>().bounds.min.y-Actor.position.y;
  }
 }
}
