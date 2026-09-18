using UnityEngine;
using UnityEngine.InputSystem;

namespace CityLife.World
{
    // Separate test-stage actor. Never reads or writes a saved world.
    public sealed class CharacterPreviewActor : MonoBehaviour
    {
        public Animator Animator;
        public CharacterController Capsule;
        public CharacterPreviewCamera View;
        public CharacterPreviewRoamer Roamer;
        public float WalkSpeed = 1.65f;
        public float SwimSpeed = 2.0f;
        public bool IsSprinting;
        public float Stamina = 100f;
        public float MaxStamina = 100f;
        public bool TestControl;
        public bool ExternalDrive;
        public bool DeadPose;
        public bool IsSwimming { get; private set; }
        public bool IsWading { get; private set; }
        public bool IsSubmerged { get; private set; }
        public float WaterDepth { get; private set; }
        public Vector3 TestDirection;
        public float ActualSpeed { get; private set; }
        public float Travelled { get; private set; }
        public CollisionFlags LastCollision { get; private set; }
        public int WallContacts { get; private set; }
        public bool Grounded => Capsule.isGrounded;
        private float fallingSpeed, gestureUntil;
        private string state = "";

        public void Gesture()
        {
            gestureUntil = Time.time + 2f;
            Animate("Interact");
        }
        public void CancelGesture() { gestureUntil = 0; Animate("Idle"); }
        public void RefreshAnimation() { state = ""; gestureUntil = 0; }
        public void Place(Vector3 position)
        {
            float groundH = CoastalTerrain.Height(position.x, position.z);
            if (Physics.Raycast(new Vector3(position.x, position.y + 10f, position.z), Vector3.down, out RaycastHit hit, 50f, 1 << 10, QueryTriggerInteraction.Ignore))
            {
                groundH = Mathf.Max(groundH, hit.point.y);
            }
            if (position.y < groundH + 0.02f)
            {
                position.y = groundH + 0.05f;
            }
            Capsule.enabled = false; transform.position = position; Capsule.enabled = true;
            fallingSpeed = 0; gestureUntil = 0; Physics.SyncTransforms();
        }
        private void Update()
        {
            if (ExternalDrive) return;
            Vector3 direction = Vector3.zero;
            if (TestControl) direction = TestDirection;
            else if (Roamer != null && Roamer.Running) direction = Roamer.Direction;
            if (!CharacterPreviewSmoke.Requested && Application.isFocused)
            {
                var key = Keyboard.current;
                if (key != null)
                {
                    if (key.rKey.wasPressedThisFrame) Roamer.Toggle();
                    if (key.escapeKey.wasPressedThisFrame) { Roamer.Pause(); View.ReleasePointer(); }
                    Vector2 move = new Vector2((key.dKey.isPressed ? 1 : 0) - (key.aKey.isPressed ? 1 : 0),
                        (key.wKey.isPressed ? 1 : 0) - (key.sKey.isPressed ? 1 : 0));
                    if (move.sqrMagnitude > 0)
                    {
                        Roamer.Pause();
                        direction = Quaternion.Euler(0, View.Yaw, 0) * new Vector3(move.x, 0, move.y);
                    }
                }
            }
            float dt = Mathf.Min(Time.deltaTime, .05f);
            Step(direction, dt);
        }
        public void Step(Vector3 direction, float dt)
        {
            if (Time.time < gestureUntil) direction = Vector3.zero;
            Vector3 before = transform.position;
            direction.y = 0; direction = Vector3.ClampMagnitude(direction, 1);

            // Anti-stuck obstacle tangent sliding:
            // When walking towards a rock or boulder, glide smoothly along the obstacle normal
            // rather than dead-stopping or wedging in crevices.
            if (direction.sqrMagnitude > 0.01f)
            {
                Vector3 rayOrigin = transform.position + Vector3.up * 0.9f;
                if (Physics.SphereCast(rayOrigin, 0.28f, direction, out RaycastHit obsHit, 0.45f, 1 << 8, QueryTriggerInteraction.Ignore))
                {
                    if (obsHit.normal.y < 0.65f)
                    {
                        Vector3 slideDir = Vector3.ProjectOnPlane(direction, obsHit.normal).normalized;
                        if (slideDir.sqrMagnitude > 0.01f) direction = slideDir;
                    }
                }
            }

            // Water awareness, surface buoyancy, and submersion detection
            float groundY = CoastalTerrain.Height(transform.position.x, transform.position.z);
            if (Physics.Raycast(new Vector3(transform.position.x, transform.position.y + 10f, transform.position.z), Vector3.down, out RaycastHit hitBelow, 50f, 1 << 10, QueryTriggerInteraction.Ignore))
            {
                groundY = Mathf.Max(groundY, hitBelow.point.y);
            }
            float waterSurface = CoastalWater.Level;
            WaterDepth = Mathf.Max(0f, waterSurface - groundY);
            IsSwimming = WaterDepth > 1.0f && transform.position.y <= waterSurface + 0.15f;
            IsWading = WaterDepth > 0.05f && !IsSwimming && transform.position.y <= waterSurface + 0.15f;
            IsSubmerged = transform.position.y + 1.65f < waterSurface;

            bool sprinting = IsSprinting && Stamina > 0f && direction.sqrMagnitude > 0.01f && !IsSwimming;
            float currentSpeed = IsSwimming ? SwimSpeed : (sprinting ? WalkSpeed * 1.85f : WalkSpeed);
            if (sprinting)
            {
                Stamina = Mathf.Max(0f, Stamina - 22f * dt);
                if (Stamina <= 0f) IsSprinting = false;
            }
            else
            {
                Stamina = Mathf.Min(MaxStamina, Stamina + 18f * dt);
            }

            if (IsSwimming)
            {
                // Surface flotation: upper chest aligns with surface, head safely above water
                float targetFlotationY = Mathf.Max(groundY + 0.05f, waterSurface - 1.15f);
                float buoyantVelocity = (targetFlotationY - transform.position.y) * 5.0f;
                fallingSpeed = Mathf.Clamp(buoyantVelocity, -3.5f, 5.0f);
            }
            else
            {
                if (Capsule.isGrounded && fallingSpeed < 0) fallingSpeed = -2;
                fallingSpeed = Mathf.Max(-25, fallingSpeed - 18 * dt);
            }

            // Edge drop safety: dampen movement toward steep vertical drops to prevent stepping into thin air
            if (direction.sqrMagnitude > 0.01f && !IsSwimming)
            {
                Vector3 probe = transform.position + direction * 0.55f;
                float probeH = CoastalTerrain.Height(probe.x, probe.z);
                if (transform.position.y - probeH > 1.8f && WaterDepth < 0.2f)
                {
                    direction *= 0.35f;
                }
            }

            LastCollision = Capsule.Move((direction * currentSpeed + Vector3.up * fallingSpeed) * dt);
            if ((LastCollision & CollisionFlags.Above) != 0) fallingSpeed = Mathf.Min(0, fallingSpeed);
            if ((LastCollision & CollisionFlags.Sides) != 0) WallContacts++;
            Vector3 distance = transform.position - before; distance.y = 0;
            ActualSpeed = dt > 0 ? distance.magnitude / dt : 0;
            Travelled += distance.magnitude;
            if (direction.sqrMagnitude > .01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 360 * dt);
            if (Time.time >= gestureUntil)
            {
                if (DeadPose) Animate("Crouch");
                else if (IsSwimming) Animate(ActualSpeed > 0.12f ? "Swim" : "SwimIdle");
                else Animate(ActualSpeed > 0.12f ? "Walk" : "Idle");
                if (Animator != null)
                {
                    Animator.speed = (sprinting && ActualSpeed > 0.12f) ? 1.55f : 1.0f;
                }
            }

            // Dynamic Terrain Slope Normal Alignment & Ground Contact:
            // Eliminates "walking in the air" on steep banks by tilting the visual model
            // smoothly to match the ground surface normal and adjusting vertical root offset
            // so both feet remain solidly planted on slopes.
            if (Animator != null && Animator.transform != transform)
            {
                if (IsSwimming || !Capsule.isGrounded)
                {
                    Animator.transform.localRotation = Quaternion.Slerp(Animator.transform.localRotation, Quaternion.Euler(0, 180f, 0), 10f * dt);
                    Animator.transform.localPosition = Vector3.Lerp(Animator.transform.localPosition, new Vector3(0, 0.12f, 0), 10f * dt);
                }
                else
                {
                    Vector3 pos = transform.position;
                    Vector3 fwd = transform.forward;
                    Vector3 rgt = transform.right;

                    float hF = CoastalTerrain.Height(pos.x + fwd.x * 0.40f, pos.z + fwd.z * 0.40f);
                    float hB = CoastalTerrain.Height(pos.x - fwd.x * 0.40f, pos.z - fwd.z * 0.40f);
                    float hR = CoastalTerrain.Height(pos.x + rgt.x * 0.30f, pos.z + rgt.z * 0.30f);
                    float hL = CoastalTerrain.Height(pos.x - rgt.x * 0.30f, pos.z - rgt.z * 0.30f);

                    Vector3 tanZ = (fwd * 0.80f + Vector3.up * (hF - hB)).normalized;
                    Vector3 tanX = (rgt * 0.60f + Vector3.up * (hR - hL)).normalized;
                    Vector3 groundNormal = Vector3.Cross(tanZ, tanX).normalized;
                    if (groundNormal.y < 0) groundNormal = -groundNormal;

                    Vector3 clampedNormal = Vector3.RotateTowards(Vector3.up, groundNormal, 32f * Mathf.Deg2Rad, 0f);
                    Vector3 localNormal = transform.InverseTransformDirection(clampedNormal);
                    Quaternion slopeTilt = Quaternion.FromToRotation(Vector3.up, localNormal);
                    Quaternion targetModelRot = slopeTilt * Quaternion.Euler(0, 180f, 0);

                    Animator.transform.localRotation = Quaternion.Slerp(Animator.transform.localRotation, targetModelRot, 14f * dt);

                    float slopeDrop = Mathf.Min(0f, Mathf.Min(hF, hB) - pos.y);
                    float targetY = Mathf.Clamp(0.12f + slopeDrop * 0.45f, -0.05f, 0.12f);
                    Animator.transform.localPosition = Vector3.Lerp(Animator.transform.localPosition, new Vector3(0, targetY, 0), 10f * dt);
                }
            }

            // Absolute terrain collision safety clamp:
            // Prevents the actor from sinking into uneven terrain or falling through single-sided mesh
            if (transform.position.y < groundY + 0.01f)
            {
                Capsule.enabled = false;
                transform.position = new Vector3(transform.position.x, groundY + 0.02f, transform.position.z);
                Capsule.enabled = true;
                fallingSpeed = 0;
                Physics.SyncTransforms();
            }
        }
        private void Animate(string wanted)
        {
            if (state == wanted) return;
            state = wanted;
            if (Animator != null) Animator.CrossFadeInFixedTime(wanted, .16f);
        }
    }
}
