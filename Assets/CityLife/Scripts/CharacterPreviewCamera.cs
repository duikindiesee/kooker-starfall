using UnityEngine;
using UnityEngine.InputSystem;

namespace CityLife.World
{
    public sealed class CharacterPreviewCamera : MonoBehaviour
    {
        public Transform Target;
        public float Yaw = 155, Pitch = 15, Distance = 4.8f;
        public bool SuppressInput;
        public bool ExternalView;
        public float ActualDistance { get; private set; }
        public bool Occluded { get; private set; }
        private bool looking;
        public void ReleasePointer()
        {
            if (!looking) return;
            looking = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        private void OnDisable() => ReleasePointer();
        private void Update()
        {
            if (CharacterPreviewSmoke.Requested || SuppressInput || ExternalView) return;
            if (!Application.isFocused) { ReleasePointer(); return; }
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.rightButton.wasReleasedThisFrame) ReleasePointer();
            if (mouse.rightButton.wasPressedThisFrame)
            { looking = true; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (looking)
            {
                Vector2 delta = mouse.delta.ReadValue();
                Yaw += delta.x * .12f; Pitch = Mathf.Clamp(Pitch - delta.y * .12f, -8, 65);
            }
            Distance = Mathf.Clamp(Distance - mouse.scroll.ReadValue().y * .002f, 2.3f, 8);
        }
        private void LateUpdate() { if (!ExternalView) Follow(); }
        public void Follow()
        {
            if (Target == null) return;
            Vector3 pivot = Target.position + Vector3.up * 1.05f;
            Vector3 offset = Quaternion.Euler(Pitch, Yaw, 0) * Vector3.back;
            Occluded = Physics.SphereCast(pivot, .2f, offset, out RaycastHit hit, Distance, 1 << 8, QueryTriggerInteraction.Ignore);
            ActualDistance = Occluded ? Mathf.Max(.3f, hit.distance - .08f) : Distance;
            transform.position = pivot + offset * ActualDistance;
            transform.LookAt(pivot);
        }
    }
}
