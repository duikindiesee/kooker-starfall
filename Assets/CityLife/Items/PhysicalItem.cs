using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.Items
{
    /// <summary>
    /// Unity runtime component for a physical item. Manages Rigidbody and Collider states,
    /// kinematic hand attachment during carry, restoration of dynamic physics on drop,
    /// and authoritative model synchronization.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
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
        public Vector3 GripLocalOffset = new Vector3(0.018f, 0.065f, 0.012f);
        public Quaternion GripLocalRotation = Quaternion.identity;

        public bool IsStored { get; private set; }
        public string BoundContainerItemId { get; private set; }

        public ItemModel BoundModel { get; private set; }
        public string BoundWorldId { get; private set; }
        public string BoundGenerationId { get; private set; }
        public bool IsBound => BoundModel != null;

        private readonly Dictionary<Renderer, bool> originalRendererStates = new Dictionary<Renderer, bool>();

        private void Awake()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            if (ItemCollider == null) ItemCollider = GetComponent<Collider>();
        }

        public void RecordInitialRendererStates()
        {
            if (IsStored) return;
            var list = new List<Renderer>();
            GetOwnedRenderers(list);
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r != null)
                {
                    originalRendererStates[r] = r.enabled;
                }
            }
        }

        public void GetOwnedRenderers(List<Renderer> result)
        {
            if (result == null) return;
            result.Clear();
            CollectOwnedRenderers(transform, result);
        }

        private void CollectOwnedRenderers(Transform current, List<Renderer> result)
        {
            if (current == null) return;
            var r = current.GetComponent<Renderer>();
            if (r != null)
            {
                result.Add(r);
            }
            for (int i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                if (child != null && child.GetComponent<PhysicalItem>() == null)
                {
                    CollectOwnedRenderers(child, result);
                }
            }
        }

        private void RestoreOwnedRenderers()
        {
            var renderers = new List<Renderer>();
            GetOwnedRenderers(renderers);
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r != null)
                {
                    if (originalRendererStates.TryGetValue(r, out bool orig))
                    {
                        r.enabled = orig;
                    }
                    else
                    {
                        r.enabled = true;
                    }
                }
            }
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
                else if (IsStored)
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

            if (!IsStored)
            {
                RecordInitialRendererStates();
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
        /// Accounts for Free, Carried, and Stored physics states without compromising strictness.
        /// </summary>
        public bool IsValid()
        {
            if (string.IsNullOrEmpty(itemId) || !ItemModel.IsValidId(itemId)) return false;
            if (string.IsNullOrEmpty(itemTypeId) || !ItemModel.IsValidId(itemTypeId)) return false;
            if (!ItemDefinition.Finite(massKg) || massKg <= 0f || massKg > 10000f) return false;
            if (!dimensions.IsValid()) return false;

            if (Body == null || ItemCollider == null) return false;
            if (Body.gameObject != gameObject || ItemCollider.gameObject != gameObject) return false;
            if (GetComponent<Rigidbody>() != Body) return false;

            // Stored items must have collider disabled; non-stored items must have collider enabled
            if (IsStored)
            {
                if (ItemCollider.enabled) return false;
            }
            else
            {
                if (!ItemCollider.enabled) return false;
            }

            // Reject non-unit scale and non-finite scale
            Vector3 scale = transform.lossyScale;
            if (!ItemDefinition.Finite(scale.x) || !ItemDefinition.Finite(scale.y) || !ItemDefinition.Finite(scale.z))
                return false;
            if (Mathf.Abs(scale.x - 1f) > 0.001f || Mathf.Abs(scale.y - 1f) > 0.001f || Mathf.Abs(scale.z - 1f) > 0.001f)
                return false;

            // Reject non-finite world transforms
            if (!ItemDefinition.Finite(transform.position.x) || !ItemDefinition.Finite(transform.position.y) || !ItemDefinition.Finite(transform.position.z))
                return false;
            if (!ItemDefinition.Finite(transform.rotation.x) || !ItemDefinition.Finite(transform.rotation.y) || !ItemDefinition.Finite(transform.rotation.z) || !ItemDefinition.Finite(transform.rotation.w))
                return false;

            // Reject multiple/compound colliders on the same object in this slice
            var cols = GetComponents<Collider>();
            if (cols.Length != 1 || cols[0] != ItemCollider) return false;

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
            else if (IsStored)
            {
                if (!Body.isKinematic || Body.useGravity) return false;
            }
            else if (IsCarried)
            {
                if (!Body.isKinematic || Body.useGravity || !ItemCollider.isTrigger) return false;
            }
            else
            {
                if (Body.isKinematic || !Body.useGravity || ItemCollider.isTrigger) return false;
            }

            return true;
        }

        /// <summary>
        /// Sets velocities to zero while still dynamic, then switches to kinematic carry
        /// with trigger collider following actor hand via scale-neutral kinematic follower.
        /// Unparents to root level to avoid inheriting non-uniform scale or bone rotation shear
        /// from the animated avatar rig, preserving declared metre dimensions.
        /// Restores renderers if transitioning out of Stored state.
        /// </summary>
        public bool IsLeftHand { get; set; }

        public static bool DetectLeftHand(Transform hand)
        {
            if (hand == null) return false;
            var anim = hand.GetComponentInParent<Animator>();
            if (anim != null && anim.isHuman)
            {
                Transform leftBone = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                if (leftBone != null && (hand == leftBone || hand.IsChildOf(leftBone)))
                    return true;
                Transform rightBone = anim.GetBoneTransform(HumanBodyBones.RightHand);
                if (rightBone != null && (hand == rightBone || hand.IsChildOf(rightBone)))
                    return false;
            }
            string n = hand.name;
            if (n.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.EndsWith("_l", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".l", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.Contains("hand_l") || n.Contains("Hand_L")) return true;
            return false;
        }

        /// <summary>
        /// Sets velocities to zero while still dynamic, then switches to kinematic carry
        /// with trigger collider following actor hand via scale-neutral kinematic follower.
        /// Unparents to root level to avoid inheriting non-uniform scale or bone rotation shear
        /// from the animated avatar rig, preserving declared metre dimensions.
        /// Restores renderers if transitioning out of Stored state.
        /// </summary>
        public void AttachToHand(Transform hand, bool? isLeft = null)
        {
            CarriedHand = hand;
            IsLeftHand = isLeft ?? DetectLeftHand(hand);
            bool wasStored = IsStored;
            IsStored = false;
            BoundContainerItemId = null;

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
                ItemCollider.enabled = true;
            }

            // Restore renderers when transitioning out of Stored
            if (wasStored)
            {
                RestoreOwnedRenderers();
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
            Vector3 offset = GripLocalOffset;
            if (IsLeftHand)
            {
                offset.x = -GripLocalOffset.x;
            }
            transform.position = CarriedHand.TransformPoint(offset);
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
        /// configures compatible continuous dynamic collision detection and solid collider,
        /// and restores renderers when transitioning out of Stored state.
        /// </summary>
        public void ReleaseToPhysics(Vector3 releasePosition, Quaternion releaseRotation)
        {
            CarriedHand = null;
            IsCarried = false;
            bool wasStored = IsStored;
            IsStored = false;
            BoundContainerItemId = null;

            transform.SetParent(null, true);
            transform.localScale = Vector3.one;
            if (ItemDefinition.Finite(releasePosition.x) && ItemDefinition.Finite(releasePosition.y) && ItemDefinition.Finite(releasePosition.z) &&
                ItemDefinition.Finite(releaseRotation.x) && ItemDefinition.Finite(releaseRotation.y) && ItemDefinition.Finite(releaseRotation.z) && ItemDefinition.Finite(releaseRotation.w))
            {
                transform.SetPositionAndRotation(releasePosition, releaseRotation);
            }
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

            // Restore renderers when transitioning out of Stored
            if (wasStored)
            {
                RestoreOwnedRenderers();
            }

            Physics.SyncTransforms();
        }

        /// <summary>
        /// Applies Stored state to this physical item: sets kinematic non-dynamic rigidbody,
        /// disables collider, disables renderers (hidden), and parents to container transform.
        /// Does NOT spawn duplicate bodies.
        /// </summary>
        public void ApplyStored(Transform containerTransform, string containerItemId)
        {
            CarriedHand = null;
            IsCarried = false;
            if (!IsStored)
            {
                RecordInitialRendererStates();
            }
            IsStored = true;
            BoundContainerItemId = containerItemId;

            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.isKinematic = true;
                Body.useGravity = false;
            }

            if (ItemCollider != null)
            {
                ItemCollider.enabled = false;
            }

            var renderers = new List<Renderer>();
            GetOwnedRenderers(renderers);
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] != null) renderers[i].enabled = false;
            }

            if (containerTransform != null)
            {
                transform.SetParent(containerTransform, true);
            }

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
            return $"lossyScale=({scale.x:F4},{scale.y:F4},{scale.z:F4}), bodyMass={mass:F4}kg, colliderBounds=[{boundsStr}], isStored={IsStored}";
        }


        /// <summary>
        /// Synchronizes this free, dynamic body's world transform to the authoritative ItemModel.
        /// </summary>
        public bool SyncToModel()
        {
            if (BoundModel == null) return false;
            if (IsCarried) return false;
            if (IsStored) return false;
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

        [Serializable]
        public struct PhysicalRuntimeStateCapture
        {
            public bool isCarried;
            public Transform carriedHand;
            public bool isStored;
            public string boundContainerItemId;
            public Transform parent;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public bool bodyKinematic;
            public bool bodyUseGravity;
            public Vector3 bodyLinearVelocity;
            public Vector3 bodyAngularVelocity;
            public CollisionDetectionMode bodyCollisionDetectionMode;
            public bool colliderEnabled;
            public bool colliderIsTrigger;
            public List<KeyValuePair<Renderer, bool>> rendererStates;
            public List<KeyValuePair<Renderer, bool>> originalRendererStates;
        }

        /// <summary>
        /// Captures exact runtime physical, transform, body, collider, and renderer state
        /// of this specific physical item for exact atomic transaction rollback.
        /// Justified narrow helper because IsCarried, IsStored, CarriedHand, BoundContainerItemId,
        /// and originalRendererStates have private setters and cannot be rolled back externally.
        /// </summary>
        public PhysicalRuntimeStateCapture CaptureRuntimeState()
        {
            var cap = new PhysicalRuntimeStateCapture
            {
                isCarried = IsCarried,
                carriedHand = CarriedHand,
                isStored = IsStored,
                boundContainerItemId = BoundContainerItemId,
                parent = transform.parent,
                localPosition = transform.localPosition,
                localRotation = transform.localRotation,
                localScale = transform.localScale,
                rendererStates = new List<KeyValuePair<Renderer, bool>>(),
                originalRendererStates = new List<KeyValuePair<Renderer, bool>>()
            };

            if (Body != null)
            {
                cap.bodyKinematic = Body.isKinematic;
                cap.bodyUseGravity = Body.useGravity;
                cap.bodyLinearVelocity = Body.linearVelocity;
                cap.bodyAngularVelocity = Body.angularVelocity;
                cap.bodyCollisionDetectionMode = Body.collisionDetectionMode;
            }

            if (ItemCollider != null)
            {
                cap.colliderEnabled = ItemCollider.enabled;
                cap.colliderIsTrigger = ItemCollider.isTrigger;
            }

            var renderers = new List<Renderer>();
            GetOwnedRenderers(renderers);
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r != null)
                {
                    cap.rendererStates.Add(new KeyValuePair<Renderer, bool>(r, r.enabled));
                }
            }

            foreach (var kvp in originalRendererStates)
            {
                cap.originalRendererStates.Add(kvp);
            }

            return cap;
        }

        /// <summary>
        /// Atomically restores exact captured runtime physical state, reversing any uncommitted transaction.
        /// </summary>
        public void RestoreRuntimeState(PhysicalRuntimeStateCapture cap)
        {
            IsCarried = cap.isCarried;
            CarriedHand = cap.carriedHand;
            IsStored = cap.isStored;
            BoundContainerItemId = cap.boundContainerItemId;

            transform.SetParent(cap.parent, false);
            transform.localPosition = cap.localPosition;
            transform.localRotation = cap.localRotation;
            transform.localScale = cap.localScale;

            if (Body != null)
            {
                Body.isKinematic = cap.bodyKinematic;
                Body.useGravity = cap.bodyUseGravity;
                Body.collisionDetectionMode = cap.bodyCollisionDetectionMode;
                if (!cap.bodyKinematic)
                {
                    Body.linearVelocity = cap.bodyLinearVelocity;
                    Body.angularVelocity = cap.bodyAngularVelocity;
                }
                else
                {
                    Body.linearVelocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                }
            }

            if (ItemCollider != null)
            {
                ItemCollider.enabled = cap.colliderEnabled;
                ItemCollider.isTrigger = cap.colliderIsTrigger;
            }

            if (cap.rendererStates != null)
            {
                for (int i = 0; i < cap.rendererStates.Count; i++)
                {
                    var kvp = cap.rendererStates[i];
                    if (kvp.Key != null)
                    {
                        kvp.Key.enabled = kvp.Value;
                    }
                }
            }

            originalRendererStates.Clear();
            if (cap.originalRendererStates != null)
            {
                for (int i = 0; i < cap.originalRendererStates.Count; i++)
                {
                    var kvp = cap.originalRendererStates[i];
                    if (kvp.Key != null)
                    {
                        originalRendererStates[kvp.Key] = kvp.Value;
                    }
                }
            }

            Physics.SyncTransforms();
        }
    }
}
