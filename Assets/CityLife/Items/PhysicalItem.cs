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

        public ItemModel BoundModel { get; private set; }
        public string BoundWorldId { get; private set; }
        public string BoundGenerationId { get; private set; }
        public bool IsBound => BoundModel != null;

        private void Awake()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            if (ItemCollider == null) ItemCollider = GetComponent<Collider>();
        }

        private void FixedUpdate()
        {
            SyncToModel();
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
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
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
        /// with trigger collider parented to actor hand.
        /// </summary>
        public void AttachToHand(Transform hand)
        {
            // Set velocities while dynamic
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;

            // Switch carried kinematic
            Body.isKinematic = true;
            Body.useGravity = false;

            // Configure trigger collider
            ItemCollider.isTrigger = true;

            // Parent to hand
            transform.SetParent(hand, false);
            transform.localPosition = new Vector3(0.06f, 0.04f, 0f);
            transform.localRotation = Quaternion.identity;
            IsCarried = true;
        }

        /// <summary>
        /// Unparents, restores dynamic physics before velocity operations on release,
        /// and configures compatible continuous dynamic collision detection and solid collider.
        /// </summary>
        public void ReleaseToPhysics(Vector3 releasePosition, Quaternion releaseRotation)
        {
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(releasePosition, releaseRotation);
            Physics.SyncTransforms();

            // Restore dynamic before velocity operations on release
            if (isAnchored)
            {
                Body.isKinematic = true;
                Body.useGravity = false;
            }
            else
            {
                Body.isKinematic = false;
                Body.useGravity = true;
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }

            ItemCollider.isTrigger = false;
            ItemCollider.enabled = true;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            IsCarried = false;
            Physics.SyncTransforms();
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
