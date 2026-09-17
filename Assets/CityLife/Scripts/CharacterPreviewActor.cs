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
        public bool TestControl;
        public bool ExternalDrive;
        public bool DeadPose;
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
            if (position.y < groundH + 0.01f)
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
            if (Capsule.isGrounded && fallingSpeed < 0) fallingSpeed = -2;
            fallingSpeed = Mathf.Max(-25, fallingSpeed - 18 * dt);
            LastCollision = Capsule.Move((direction * WalkSpeed + Vector3.up * fallingSpeed) * dt);
            if ((LastCollision & CollisionFlags.Above) != 0) fallingSpeed = Mathf.Min(0, fallingSpeed);
            if ((LastCollision & CollisionFlags.Sides) != 0) WallContacts++;
            Vector3 distance = transform.position - before; distance.y = 0;
            ActualSpeed = dt > 0 ? distance.magnitude / dt : 0;
            Travelled += distance.magnitude;
            if (direction.sqrMagnitude > .01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 360 * dt);
            if (Time.time >= gestureUntil) Animate(DeadPose ? "Crouch" : ActualSpeed > .12f ? "Walk" : "Idle");

            // Absolute terrain collision safety clamp:
            // Prevents the actor from ever falling through single-sided terrain mesh into the void
            float groundY = CoastalTerrain.Height(transform.position.x, transform.position.z);
            if (Physics.Raycast(new Vector3(transform.position.x, transform.position.y + 10f, transform.position.z), Vector3.down, out RaycastHit stepHit, 50f, 1 << 10, QueryTriggerInteraction.Ignore))
            {
                groundY = Mathf.Max(groundY, stepHit.point.y);
            }
            if (transform.position.y < groundY - 0.25f)
            {
                Capsule.enabled = false;
                transform.position = new Vector3(transform.position.x, groundY + 0.05f, transform.position.z);
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
