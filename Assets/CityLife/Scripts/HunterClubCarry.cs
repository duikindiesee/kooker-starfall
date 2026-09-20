using UnityEngine;
namespace CityLife.World
{
 // Cosmetic left-hand prop only: no collider, damage, targeting or action authority.
 // Keep the heavy end clear of the feet while the original right-hand pickup remains free.
 [DefaultExecutionOrder(150)]
 public sealed class HunterClubCarry : MonoBehaviour
 {
  public const float ClubLength=.56f;
  public Animator Animator; public Transform Actor,Club; public float Length=ClubLength;
  public Transform[] GripBones; public Quaternion[] GripRotations;
  public Transform[] GripLeaves; public Quaternion[] LeafRotations; public Vector3[] GripPositions,LeafPositions;
  public Vector3 PalmAnchor, ShaftAxis; public Quaternion HandBasis;
  public Vector3[] FlexAxes; public Vector3 PalmAlong,PalmNormal;
  public Vector3 DiagnosticCurlAdjustment,DiagnosticThumbAdjustment; public Vector2 DiagnosticAnchorAdjustment;
  public float ForearmSlope=-1.5f,ElbowOut=.6f,WristDeviation=30f;
  public float GroundClearance {get;private set;}
  public Vector3 GripCenter {get;private set;}
  public static float ClubRadius(float fraction){float t=Mathf.Clamp01((fraction-.22f)/.78f);return .010f+.026f*t*t*(3-2*t)+.043f*Mathf.Exp(-Mathf.Pow((fraction-.87f)/.17f,2));}
  public static float ClubCurve(float fraction){return fraction<=.22f?0:.012f*Mathf.Sin((fraction-.22f)/.78f*5);}
  // Author once in the imported bind pose, before the Animator evaluates.
  public void AuthorGrip() {
   var hand=Animator.GetBoneTransform(HumanBodyBones.LeftHand);
   var index=Animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
   var little=Animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
   var middle=Animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
   Vector3 along=(middle.position-hand.position).normalized;
   Vector3 across=Vector3.ProjectOnPlane(little.position-index.position,along).normalized;
   Vector3 palm=Vector3.Cross(along,across).normalized;
   PalmAnchor=hand.InverseTransformPoint(middle.position+along*.003f+palm*.028f);
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
    // Shorter outer fingers need their own curl; sharing the middle-finger pose
    // drives their first knuckles through the handle.
    float flex=new[]{30f,80f,55f,30f,65f,85f,25f,60f,85f,5f,45f,65f}[i];
    GripRotations[i]=bone.localRotation*Quaternion.AngleAxis(-flex,localAxis);
   }
   var thumb=new[]{Animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal),Animator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate),Animator.GetBoneTransform(HumanBodyBones.LeftThumbDistal)};
   var original=new[]{thumb[0].localRotation,thumb[1].localRotation,thumb[2].localRotation};
   var tip=thumb[2].GetChild(0);
   Vector3 target=middle.position+along*.002f+palm*.050f-across*.005f;
   for(int iteration=0;iteration<20;iteration++)for(int joint=2;joint>=0;joint--) {
    var bone=thumb[joint];var turn=Quaternion.FromToRotation(tip.position-bone.position,target-bone.position);
    bone.rotation=Quaternion.RotateTowards(Quaternion.identity,turn,12f)*bone.rotation;
   }
   for(int i=0;i<3;i++){GripBones[12+i]=thumb[i];GripRotations[12+i]=thumb[i].localRotation;FlexAxes[12+i]=thumb[i].InverseTransformDirection(across);}
   for(int i=0;i<3;i++)thumb[i].localRotation=original[i];
   GripPositions=new Vector3[GripBones.Length];for(int i=0;i<GripBones.Length;i++)GripPositions[i]=GripBones[i].localPosition;
   GripLeaves=new Transform[5];LeafRotations=new Quaternion[5];LeafPositions=new Vector3[5];
   for(int i=0;i<5;i++){var leaf=GripBones[i*3+2].GetChild(0);GripLeaves[i]=leaf;LeafRotations[i]=leaf.localRotation;LeafPositions[i]=leaf.localPosition;}
  }
  public bool Stowed { get; private set; }
  public void ToggleHolster() => SetStowed(!Stowed);
  public bool SetStowed(bool stowed, bool force = false)
  {
      if (!stowed && !force)
      {
          var brain = Actor != null ? (Actor.GetComponent<NpcAutonomy>() ?? Actor.GetComponentInChildren<NpcAutonomy>()) : null;
          if (brain != null && brain.Actions != null && brain.Actions.HeldLeft != null)
          {
              return false;
          }
      }
      Stowed = stowed;
      if (Club != null)
      {
          var rend = Club.GetComponent<Renderer>();
          if (rend != null) rend.enabled = true;
          if (stowed)
          {
              AttachToBack();
          }
      }
      return true;
  }
  public void AttachToBack()
  {
      if (!Animator || !Club) return;
      Transform backBone = Animator.GetBoneTransform(HumanBodyBones.Chest);
      if (backBone == null) backBone = Animator.GetBoneTransform(HumanBodyBones.Spine);
      if (backBone == null) backBone = Actor;
      if (Club.parent != backBone)
      {
          Club.SetParent(backBone, false);
          Club.localScale = Vector3.one;
      }
      // Holster diagonally across back from right shoulder blade to left waist
      Club.localPosition = new Vector3(-0.08f, 0.14f, 0.135f);
      Club.localRotation = Quaternion.Euler(-6f, 2f, 22f);
  }
  private void LateUpdate(){
      if(!Animator||!Club)return;
      if(Stowed)
      {
          AttachToBack();
          return;
      }
      var brain = Actor != null ? (Actor.GetComponent<NpcAutonomy>() ?? Actor.GetComponentInChildren<NpcAutonomy>()) : null;
      if (brain != null && brain.Actions != null && brain.Actions.HeldLeft != null)
      {
          AttachToBack();
          return;
      }
      if(GripBones==null)return;
   var upper=Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
   var forearm=Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
   var hand=Animator.GetBoneTransform(HumanBodyBones.LeftHand);
   // A relaxed bent elbow supports the weight. Preserve animated shoulder/root motion;
   // align the wrist with the forearm instead of twisting it to chase a world-space prop.
   float swing=Mathf.Clamp(Vector3.Dot((forearm.position-upper.position).normalized,Actor.forward),-.2f,.2f)*.65f;
   Vector3 upperDirection=(Vector3.down-Actor.right*ElbowOut+Actor.forward*swing).normalized;
   upper.rotation=Quaternion.FromToRotation(forearm.position-upper.position,upperDirection)*upper.rotation;
   float lowPose=Mathf.InverseLerp(1.25f,.85f,upper.position.y-Actor.position.y);
   float slope=Mathf.Lerp(ForearmSlope,.46f,lowPose);
   Vector3 forearmDirection=(Actor.forward+Vector3.up*slope).normalized;
   forearm.rotation=Quaternion.FromToRotation(hand.position-forearm.position,forearmDirection)*forearm.rotation;
   Vector3 handDirection=Quaternion.AngleAxis(-WristDeviation,Actor.right)*forearmDirection;
   Vector3 shaftDirection=Vector3.ProjectOnPlane(Vector3.down,handDirection).normalized;
   hand.rotation=Quaternion.LookRotation(shaftDirection,handDirection)*Quaternion.Inverse(HandBasis);
   for(int i=0;i<GripBones.Length;i++){GripBones[i].localPosition=GripPositions[i];GripBones[i].localRotation=GripRotations[i]*Quaternion.AngleAxis(-(i<12?DiagnosticCurlAdjustment[i%3]:DiagnosticThumbAdjustment[i%3]),FlexAxes[i]);}
   for(int i=0;i<GripLeaves.Length;i++){GripLeaves[i].localPosition=LeafPositions[i];GripLeaves[i].localRotation=LeafRotations[i];}
   // Share the hand's entire transform, including the retained model's nonuniform scale.
   // A world rotation alone loses that scale/shear and lets the shaft drift through fingers.
   if(Club.parent!=hand){Club.SetParent(hand,false);Club.localScale=Vector3.one;}
   Club.localPosition=PalmAnchor+PalmAlong*DiagnosticAnchorAdjustment.x+PalmNormal*DiagnosticAnchorAdjustment.y;
   Club.localRotation=Quaternion.FromToRotation(Vector3.down,ShaftAxis);GripCenter=Club.position;
   GroundClearance=Club.GetComponent<Renderer>().bounds.min.y-Actor.position.y;
  }
 }
}
