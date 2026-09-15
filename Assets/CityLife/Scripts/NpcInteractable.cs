using UnityEngine;

namespace CityLife.World
{
    public enum NpcObjectKind { Item, Destination, Place }
    public sealed class NpcInteractable : MonoBehaviour
    {
        public string StableId;
        // A scoped visual category emitted only with a live LOS observation.
        // It is not a registry lookup or a nutrition/safety conclusion.
        public string ObservedType="";
        public string WorldId = "starfall.npc-courtyard.v1";
        public NpcObjectKind Kind;
        public bool Permission = true;
        public Transform Approach, Socket;
        public string HeldBy = "", DeliveredTo = "", Occupant = "";
        public Vector3 SightPoint => Kind == NpcObjectKind.Item ? transform.position : transform.position + Vector3.up * .65f;
        public bool Available => Kind == NpcObjectKind.Item ? HeldBy.Length == 0 && DeliveredTo.Length == 0 : Occupant.Length == 0;
        private Transform originalParent;
        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private bool originalPermission, remembered;
        public void RememberInitial()
        {
            if (remembered) return;
            remembered = true; originalParent = transform.parent; originalPosition = transform.position;
            originalRotation = transform.rotation; originalPermission = Permission;
        }
        public void RestoreInitial()
        {
            transform.SetParent(originalParent, true); transform.SetPositionAndRotation(originalPosition, originalRotation);
            Permission = originalPermission; HeldBy = DeliveredTo = Occupant = "";
        }
    }
}
