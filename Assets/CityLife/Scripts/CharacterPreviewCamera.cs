using UnityEngine;
using UnityEngine.InputSystem;

namespace CityLife.World
{
    public enum CameraViewMode
    {
        OverTheShoulder = 0,
        ScenicCinematicOrbit = 1,
        WideVista = 2,
        FirstPerson = 3
    }

    public sealed class CharacterPreviewCamera : MonoBehaviour
    {
        public Transform Target;
        public float Yaw = 155, Pitch = 15, Distance = 4.8f;
        public bool SuppressInput;
        public bool ExternalView;
        public CameraViewMode ViewMode = CameraViewMode.OverTheShoulder;

        public float ActualDistance { get; private set; }
        public bool Occluded { get; private set; }

        private bool looking;
        private float smoothedDistance = 4.8f;
        private float lastUserLookTime = -10f;
        private float orbitTimer;

        public void ReleasePointer()
        {
            if (!looking) return;
            looking = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDisable() => ReleasePointer();

        public void CycleViewMode()
        {
            ViewMode = (CameraViewMode)(((int)ViewMode + 1) % 4);
            switch (ViewMode)
            {
                case CameraViewMode.OverTheShoulder:
                    Distance = 3.8f; Pitch = 14f;
                    break;
                case CameraViewMode.ScenicCinematicOrbit:
                    Distance = 5.2f; Pitch = 16f;
                    break;
                case CameraViewMode.WideVista:
                    Distance = 7.5f; Pitch = 24f;
                    break;
                case CameraViewMode.FirstPerson:
                    Distance = 0.2f; Pitch = 5f;
                    break;
            }
        }

        private void Update()
        {
            if (CharacterPreviewSmoke.Requested || SuppressInput || ExternalView) return;
            if (!Application.isFocused) { ReleasePointer(); return; }

            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.rightButton.wasReleasedThisFrame) ReleasePointer();
            if (mouse.rightButton.wasPressedThisFrame)
            {
                looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                lastUserLookTime = Time.time;
            }

            if (looking)
            {
                Vector2 delta = mouse.delta.ReadValue();
                Yaw += delta.x * .12f;
                Pitch = Mathf.Clamp(Pitch - delta.y * .12f, -12, 65);
                lastUserLookTime = Time.time;
            }

            // Scenic Cinematic Orbit auto-drift when user is not actively controlling view
            if (ViewMode == CameraViewMode.ScenicCinematicOrbit && !looking)
            {
                if (Time.time - lastUserLookTime > 1.8f)
                {
                    Yaw += 7.5f * Time.deltaTime;
                    if (Yaw > 360f) Yaw -= 360f;
                }
            }

            Distance = Mathf.Clamp(Distance - mouse.scroll.ReadValue().y * .002f, 0.8f, 10f);
        }

        private void LateUpdate()
        {
            if (!ExternalView) Follow();
        }

        public void Follow()
        {
            if (Target == null) return;

            // First-person survival view: camera mounted directly at eye level
            if (ViewMode == CameraViewMode.FirstPerson)
            {
                Vector3 eyes = Target.position + Vector3.up * 1.62f + Target.forward * 0.18f;
                transform.position = eyes;
                transform.rotation = Quaternion.Euler(Pitch, Yaw, 0);
                ActualDistance = 0.1f;
                Occluded = false;
                return;
            }

            // Pivot height tailored to mode
            float pivotHeight = (ViewMode == CameraViewMode.WideVista) ? 1.45f : 1.25f;
            Vector3 pivot = Target.position + Vector3.up * pivotHeight;

            // Over-the-shoulder lateral offset: frames character on the rule-of-thirds line
            // and avoids placing the camera directly behind the neck/collar
            Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0);
            float shoulderOffset = (ViewMode == CameraViewMode.OverTheShoulder) ? 0.35f :
                                   (ViewMode == CameraViewMode.ScenicCinematicOrbit) ? 0.25f : 0.0f;
            Vector3 lateral = rot * Vector3.right * shoulderOffset;
            Vector3 cameraPivot = pivot + lateral;

            // Scenic orbit subtle vertical breathing bob
            if (ViewMode == CameraViewMode.ScenicCinematicOrbit)
            {
                orbitTimer += Time.deltaTime;
                cameraPivot += Vector3.up * (Mathf.Sin(orbitTimer * 0.9f) * 0.09f);
            }

            Vector3 backDir = rot * Vector3.back;

            // SphereCast collision against world geometry (layer 8: static terrain/walls, layer 10: ground)
            Occluded = Physics.SphereCast(cameraPivot, 0.22f, backDir, out RaycastHit hit, Distance,
                (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore);

            // Standoff buffer: keeps camera at least 0.75m away so it NEVER shoves inside the neck
            float targetDistance = Occluded ? Mathf.Max(0.75f, hit.distance - 0.10f) : Distance;

            // Smooth spring damping on distance to eliminate jarring pops against walls
            if (CharacterPreviewSmoke.Requested)
            {
                smoothedDistance = targetDistance;
            }
            else
            {
                float smoothSpeed = (targetDistance < smoothedDistance) ? 18f : 8f;
                smoothedDistance = Mathf.Lerp(smoothedDistance, targetDistance, smoothSpeed * Time.deltaTime);
            }

            ActualDistance = smoothedDistance;
            transform.position = cameraPivot + backDir * ActualDistance;

            // Look slightly ahead of character pivot for cinematic third-person framing
            Vector3 lookTarget = pivot + (rot * Vector3.forward * 1.2f) + lateral * 0.2f;
            transform.LookAt(lookTarget);
        }
    }
}
