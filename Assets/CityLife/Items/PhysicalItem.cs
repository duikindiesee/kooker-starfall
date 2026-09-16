using System;
using UnityEngine;

namespace CityLife.Items
{
    /// <summary>
    /// Unity runtime component for a physical item. Manages Rigidbody and Collider states,
    /// kinematic hand attachment during carry, restoration of dynamic physics on drop,
    /// and authoritative model synchronization.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalItem : MonoBehaviour
    {
        public string itemId;
        public string itemTypeId;
        public float massKg = 1.0f;
        public PhysicalDimensions dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f);
        public bool isAnchored = false;

        public Rigidbody Body;
        public Collider ItemCollider;

        public bool IsCarried { get; private set; }
        public Transform CarriedHand { get; private set; }
        public Vector3 GripLocalOffset = new Vector3(0.06f, 0.04f, 0f);
        public Quaternion GripLocalRotation = Quaternion.identity;

        public ItemModel BoundModel { get; private set; }
        public string BoundWorldId { get; private set; }
        public string BoundGenerationId { get; private set; }
        public bool IsBound => BoundModel != null;

        private void Awake()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            if (ItemCollider == null) ItemCollider = GetComponent<Collider>();
        }

        /// <summary>
        /// Explicit setup creating or configuring components prior to runtime actions.
        /// Does NOT silently clamp mass.
        /// </summary>
        public void ConfigureComponents()
        {
            if (Body == null)
            {
                Body = GetComponent<Rigidbody>();
                if (Body == null) Body = gameObject.AddComponent<Rigidbody>();
            }

            if (ItemCollider == null)
            {
                ItemCollider = GetComponent<Collider>();
                if (ItemCollider == null) ItemCollider = gameObject.AddComponent<BoxCollider>();
            }

            if (ItemCollider is BoxCollider box && dimensions.IsValid())
            {
                box.size = new Vector3(dimensions.width, dimensions.height, dimensions.depth);
                box.center = Vector3.zero;
            }

            if (Body != null)
            {
                Body.mass = massKg;
                if (isAnchored)
                {
                    Body.isKinematic = true;
                    Body.useGravity = false;
                }
                else
                {
                    Body.isKinematic = false;
                    Body.useGravity = true;
                }
                // Speculative continuous collision detection anticipates both linear and angular motion to mitigate contact tunneling on rotating items
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                Body.linearDamping = 0.05f;
                Body.angularDamping = 0.05f;
            }
        }

        /// <summary>
        /// Binds this physical item component to a specific authoritative ItemModel.
        /// </summary>
        public void Bind(ItemModel model, string worldId, string generationId)
        {
            BoundModel = model;
            BoundWorldId = worldId;
            BoundGenerationId = generationId;
        }

        public bool IsBoundTo(ItemModel model, string worldId, string generationId)
        {
            return BoundModel != null &&
                   BoundModel == model &&
                   string.Equals(BoundWorldId, worldId, StringComparison.Ordinal) &&
                   string.Equals(BoundGenerationId, generationId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Read-only validation. Fails closed without mutating mass, colliders, or scale.
        /// </summary>
        public bool IsValid()
        {
            if (string.IsNullOrEmpty(itemId) || !ItemModel.IsValidId(itemId)) return false;
            if (string.IsNullOrEmpty(itemTypeId) || !ItemModel.IsValidId(itemTypeId)) return false;
            if (!ItemDefinition.Finite(massKg) || massKg <= 0f || massKg > 10000f) return false;
            if (!dimensions.IsValid()) return false;

            if (Body == null || ItemCollider == null) return false;
            if (!ItemCollider.enabled) return false;

            // Reject non-unit scale
            Vector3 scale = transform.lossyScale;
            if (Mathf.Abs(scale.x - 1f) > 0.001f || Mathf.Abs(scale.y - 1f) > 0.001f || Mathf.Abs(scale.z - 1f) > 0.001f)
                return false;

            // Reject multiple/compound colliders on the same object in this slice
            var cols = GetComponents<Collider>();
            if (cols.Length != 1) return false;

            // Supported collider validation
            if (ItemCollider is BoxCollider box)
            {
                if (box.size.x <= 0f || box.size.y <= 0f || box.size.z <= 0f) return false;
            }
            else if (ItemCollider is SphereCollider sphere)
            {
                if (sphere.radius <= 0f) return false;
            }
            else if (ItemCollider is CapsuleCollider capsule)
            {
                if (capsule.radius <= 0f || capsule.height <= 0f) return false;
            }
            else if (ItemCollider is MeshCollider mesh)
            {
                if (!mesh.convex) return false;
            }
            else
            {
                return false;
            }

            // Body mass must match declared mass without silent clamping
            if (!ItemDefinition.Finite(Body.mass) || Body.mass <= 0f || Mathf.Abs(Body.mass - massKg) > 0.0001f)
                return false;

            if (isAnchored)
            {
                if (!Body.isKinematic || Body.useGravity) return false;
            }

            return true;
        }

        /// <summary>
        /// Sets velocities to zero while still dynamic, then switches to kinematic carry
        /// with trigger collider following actor hand via scale-neutral kinematic follower.
        /// Unparents to root level to avoid inheriting non-uniform scale or bone rotation shear
        /// from the animated avatar rig, preserving declared metre dimensions.
        /// </summary>
        public void AttachToHand(Transform hand)
        {
            CarriedHand = hand;

            // Set velocities while dynamic
            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.isKinematic = true;
                Body.useGravity = false;
            }

            // Configure trigger collider
            if (ItemCollider != null)
            {
                ItemCollider.isTrigger = true;
            }

            // Scale-neutral kinematic follower: unparent to root level to guarantee lossyScale == Vector3.one
            transform.SetParent(null, true);
            transform.localScale = Vector3.one;
            IsCarried = true;
            UpdateGripPose();
        }

        /// <summary>
        /// Updates the world transform of this carried item to match the hand's grip pose
        /// while strictly maintaining unit world scale (no scale inheritance or shear).
        /// </summary>
        public void UpdateGripPose()
        {
            if (!IsCarried || CarriedHand == null) return;
            transform.position = CarriedHand.TransformPoint(GripLocalOffset);
            transform.rotation = CarriedHand.rotation * GripLocalRotation;
            transform.localScale = Vector3.one;
        }

        private void LateUpdate()
        {
            if (IsCarried && CarriedHand != null)
            {
                UpdateGripPose();
            }
        }

        private void FixedUpdate()
        {
            if (IsCarried && CarriedHand != null)
            {
                UpdateGripPose();
            }
        }

        /// <summary>
        /// Unparents, restores dynamic physics before velocity operations on release,
        /// and configures compatible continuous dynamic collision detection and solid collider.
        /// </summary>
        public void ReleaseToPhysics(Vector3 releasePosition, Quaternion releaseRotation)
        {
            CarriedHand = null;
            transform.SetParent(null, true);
            transform.localScale = Vector3.one;
            transform.SetPositionAndRotation(releasePosition, releaseRotation);
            Physics.SyncTransforms();

            // Restore dynamic before velocity operations on release
            if (isAnchored)
            {
                if (Body != null)
                {
                    Body.isKinematic = true;
                    Body.useGravity = false;
                }
            }
            else
            {
                if (Body != null)
                {
                    Body.isKinematic = false;
                    Body.useGravity = true;
                    Body.linearVelocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                }
            }

            if (ItemCollider != null)
            {
                ItemCollider.isTrigger = false;
                ItemCollider.enabled = true;
            }
            if (Body != null)
            {
                // Speculative continuous collision detection anticipates both linear and angular motion to mitigate contact tunneling on rotating items
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            IsCarried = false;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Returns concise diagnostic measurements (lossyScale, bodyMass, colliderBounds).
        /// </summary>
        public string GetDiagnosticMeasurements()
        {
            Vector3 scale = transform.lossyScale;
            float mass = Body != null ? Body.mass : float.NaN;
            string boundsStr = "none";
            if (ItemCollider != null)
            {
                var b = ItemCollider.bounds;
                boundsStr = $"center=({b.center.x:F3},{b.center.y:F3},{b.center.z:F3}),size=({b.size.x:F3},{b.size.y:F3},{b.size.z:F3})";
            }
            return $"lossyScale=({scale.x:F4},{scale.y:F4},{scale.z:F4}), bodyMass={mass:F4}kg, colliderBounds=[{boundsStr}]";
        }


        /// <summary>
        /// Synchronizes this free, dynamic body's world transform to the authoritative ItemModel.
        /// </summary>
        public bool SyncToModel()
        {
            if (BoundModel == null) return false;
            if (IsCarried) return false;
            if (isAnchored) return false;
            if (Body == null || Body.isKinematic) return false;
            if (!gameObject.scene.IsValid() || !isActiveAndEnabled) return false;

            return BoundModel.SyncFreeTransform(
                BoundWorldId,
                BoundGenerationId,
                itemId,
                transform.position,
                transform.rotation);
        }
    }
}
