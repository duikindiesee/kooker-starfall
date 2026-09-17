using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    public enum NpcActionKind { Pickup, Deliver, Drop, Store, Retrieve }
    [Serializable] public struct NpcActionResult
    { public bool success, duplicate; public string code; }

    // The only pickup/delivery/drop mutation boundary. Requests name registered objects, never code or arbitrary transforms.
    public sealed class NpcActionApi
    {
        private readonly string agentId, worldId;
        private readonly Transform actor, hand;
        private readonly Dictionary<string, NpcInteractable> objects = new Dictionary<string, NpcInteractable>(StringComparer.Ordinal);
        private readonly Dictionary<int, Receipt> receipts = new Dictionary<int, Receipt>();
        private sealed class Receipt { public string signature; public NpcActionResult result; }
        public NpcInteractable Held { get; private set; }
        public bool AllowPickup = true, AllowDelivery = true, AllowDrop = true;
        public int Deliveries { get; private set; }
        public ItemModel PhysicalModel { get; set; }
        public IItemActionAuthority PhysicalAuthority { get; set; }
        public int ClearanceMask = (1 << 0) | (1 << 8) | (1 << 10);
        public Transform HandTransform => hand;
        public Transform ActorTransform => actor;
        public string LastPhysicalDiagnostic { get; private set; }
        public string AgentId => agentId;
        public string WorldId => worldId;
        internal bool FailAfterPhysicalApplyForTesting;
#if UNITY_EDITOR
        public void SetFailAfterPhysicalApplyForTesting(bool value)
        {
            FailAfterPhysicalApplyForTesting = value;
        }
#endif

        public bool IsObjectRegistered(string stableId, NpcInteractable interactable)
        {
            return !string.IsNullOrEmpty(stableId) &&
                   interactable != null &&
                   objects.TryGetValue(stableId, out var reg) &&
                   reg == interactable;
        }

        public NpcActionApi(string agentId, string worldId, Transform actor, Transform hand, IEnumerable<NpcInteractable> registry)
        {
            this.agentId = agentId; this.worldId = worldId; this.actor = actor; this.hand = hand;
            foreach (var item in registry)
            {
                if (item == null || string.IsNullOrEmpty(item.StableId) || objects.ContainsKey(item.StableId))
                    throw new InvalidOperationException("A unique, nonempty id is required for every registered interactable.");
                objects.Add(item.StableId, item);
            }
        }

        public bool SyncFreeTransform(string itemId)
        {
            if (PhysicalModel == null || string.IsNullOrEmpty(itemId)) return false;
            if (!objects.TryGetValue(itemId, out var interactable) || interactable == null || !interactable.isActiveAndEnabled)
                return false;
            if (interactable.WorldId != worldId || interactable.gameObject.scene != actor.gameObject.scene)
                return false;
            var phys = interactable.GetComponent<PhysicalItem>();
            if (phys == null || !phys.IsBoundTo(PhysicalModel, worldId, PhysicalModel.GenerationId))
                return false;
            return phys.SyncToModel();
        }

        /// <summary>
        /// Trusted restore API for scoped persistence restoration.
        /// Restores authoritative hand ownership of a registered interactable when validated
        /// against the authoritative PhysicalModel, world scope, hand existence, and kinematic carry state.
        /// Does NOT bypass physical validation or permit arbitrary transform assignments.
        /// </summary>
        public bool RestoreHeld(NpcInteractable interactable)
        {
            if (interactable == null)
            {
                Held = null;
                return true;
            }

            if (interactable.Kind != NpcObjectKind.Item) return false;
            if (!objects.TryGetValue(interactable.StableId, out var reg) || reg != interactable) return false;
            if (interactable.WorldId != worldId || interactable.gameObject.scene != actor.gameObject.scene) return false;
            if (hand == null || hand.gameObject.scene != actor.gameObject.scene) return false;
            if (interactable.HeldBy != agentId) return false;

            var phys = interactable.GetComponent<PhysicalItem>();
            if (phys != null)
            {
                if (PhysicalModel == null) return false;
                if (!phys.IsBoundTo(PhysicalModel, worldId, PhysicalModel.GenerationId)) return false;
                if (!PhysicalModel.TryGetItem(interactable.StableId, out var snap)) return false;
                if (snap.location != ItemLocationKind.Carried || !string.Equals(snap.holderActorId, agentId, StringComparison.Ordinal)) return false;
                if (!phys.IsCarried || phys.CarriedHand != hand) return false;
            }
            else
            {
                if (interactable.transform.parent != hand) return false;
            }

            Held = interactable;
            return true;
        }

        private bool ValidatePhysicalMetadata(PhysicalItem phys, NpcInteractable target, out string denyCode)
        {
            denyCode = null;
            if (PhysicalModel == null)
            {
                denyCode = "physical-model-required";
                return false;
            }
            if (phys == null || !phys.IsValid())
            {
                denyCode = "physical-item-invalid";
                if (phys != null) LastPhysicalDiagnostic = phys.GetDiagnosticMeasurements();
                return false;
            }
            if (!phys.IsBoundTo(PhysicalModel, worldId, PhysicalModel.GenerationId))
            {
                denyCode = "physical-item-not-bound";
                return false;
            }
            if (!string.Equals(phys.itemId, target.StableId, StringComparison.Ordinal))
            {
                denyCode = "physical-identity-mismatch";
                return false;
            }
            if (!PhysicalModel.TryGetItem(target.StableId, out var itemState))
            {
                denyCode = "physical-item-not-registered";
                return false;
            }
            if (!PhysicalModel.TryGetDefinition(itemState.itemTypeId, out var def))
            {
                denyCode = "physical-definition-not-found";
                return false;
            }
            if (!string.Equals(phys.itemTypeId, def.itemTypeId, StringComparison.Ordinal) ||
                Mathf.Abs(phys.massKg - def.massKg) > 0.0001f ||
                Mathf.Abs(phys.dimensions.width - def.dimensions.width) > 0.0001f ||
                Mathf.Abs(phys.dimensions.height - def.dimensions.height) > 0.0001f ||
                Mathf.Abs(phys.dimensions.depth - def.dimensions.depth) > 0.0001f ||
                phys.isAnchored != def.isAnchored)
            {
                denyCode = "physical-metadata-mismatch";
                return false;
            }
            return true;
        }

        public NpcActionResult Execute(int requestId, NpcActionKind action, string targetId)
        {
            if (action == NpcActionKind.Store)
            {
                string heldId = Held != null ? Held.StableId : "";
                return Execute(requestId, action, heldId, targetId);
            }
            if (action == NpcActionKind.Retrieve)
            {
                string containerId = "";
                if (PhysicalModel != null && PhysicalModel.TryGetItem(targetId ?? "", out var snap) && snap.location == ItemLocationKind.Stored)
                {
                    containerId = snap.containerItemId;
                }
                return Execute(requestId, action, targetId, containerId);
            }

            string signature = action + ":" + (targetId ?? "");
            NpcActionResult Deny(string code) => new NpcActionResult { code = code, success = false };
            if (requestId <= 0) return Deny("invalid-request-id");
            if (action != NpcActionKind.Pickup && action != NpcActionKind.Deliver && action != NpcActionKind.Drop)
                return Deny("unsupported-action");
            if (receipts.TryGetValue(requestId, out var receipt))
            {
                if (receipt.signature != signature) return Deny("request-id-conflict");
                var repeat = receipt.result; repeat.duplicate = true; return repeat;
            }
            if (receipts.Count >= 256) return Deny("bounded-ledger-full");
            NpcActionResult Finish(NpcActionResult result)
            { receipts.Add(requestId, new Receipt { signature = signature, result = result }); return result; }

            if (action == NpcActionKind.Drop)
            {
                if (Held == null) return Finish(Deny("cargo-ownership-mismatch"));
                if (!Held.isActiveAndEnabled) return Finish(Deny("target-unavailable"));
                var phys = Held.GetComponent<PhysicalItem>();
                bool isCarriedByHand = phys != null ? (phys.CarriedHand == hand && phys.IsCarried) : (Held.transform.parent == hand);
                if (Held.HeldBy != agentId || !isCarriedByHand)
                    return Finish(Deny("cargo-ownership-mismatch"));
                if (!objects.TryGetValue(Held.StableId, out var regHeld) || regHeld != Held)
                    return Finish(Deny("cargo-ownership-mismatch"));
                if (!string.IsNullOrEmpty(targetId) && !string.Equals(targetId, Held.StableId, StringComparison.Ordinal))
                    return Finish(Deny("cargo-ownership-mismatch"));
                if (!Held.Permission || !AllowDrop)
                    return Finish(Deny("permission-denied"));
                if (hand == null)
                    return Finish(Deny("item-unavailable"));
                if (Held.WorldId != worldId || Held.gameObject.scene != actor.gameObject.scene || hand.gameObject.scene != actor.gameObject.scene)
                    return Finish(Deny("world-mismatch"));

                if (phys != null)
                {
                    phys.UpdateGripPose();
                    if (PhysicalModel == null)
                        return Finish(Deny("physical-model-required"));
                    if (!ValidatePhysicalMetadata(phys, Held, out string metaDeny))
                        return Finish(Deny(metaDeny));
                    if (!PhysicalModel.TryGetItem(Held.StableId, out var carriedSnap) ||
                        carriedSnap.location != ItemLocationKind.Carried ||
                        !string.Equals(carriedSnap.holderActorId, agentId, StringComparison.Ordinal))
                    {
                        return Finish(Deny("cargo-ownership-mismatch"));
                    }
                }

                Vector3 releasePos = hand.position + hand.forward * 0.25f;
                Quaternion releaseRot = hand.rotation;

                Physics.SyncTransforms();
                var scene = actor.gameObject.scene;
                var ps = scene.GetPhysicsScene();
                if (!ps.IsValid())
                    return Finish(Deny("invalid-physics-scene"));

                Collider heldCol = Held.GetComponent<Collider>();
                if (heldCol == null)
                    return Finish(Deny("physical-item-invalid"));

                Vector3 heldPos = Held.transform.position;
                bool clearanceBlocked = false;

                var sweepHits = new RaycastHit[32];
                var overlapHits = new Collider[32];

                if (heldCol is BoxCollider box)
                {
                    Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, Held.transform.lossyScale);
                    Vector3 heldCenter = heldPos + Held.transform.rotation * box.center;
                    Vector3 releaseCenter = releasePos + releaseRot * box.center;
                    Vector3 centerDelta = releaseCenter - heldCenter;
                    float centerDist = centerDelta.magnitude;
                    Vector3 centerDir = centerDist > 0.0001f ? centerDelta / centerDist : Vector3.forward;

                    // 1. Swept path check from held pose to release pose
                    if (centerDist > 0.001f)
                    {
                        int sweepCount = ps.BoxCast(heldCenter, halfExtents, centerDir, sweepHits, releaseRot, centerDist, ClearanceMask, QueryTriggerInteraction.Ignore);
                        if (sweepCount >= sweepHits.Length)
                            return Finish(Deny("clearance-check-overflow"));

                        for (int i = 0; i < sweepCount; i++)
                        {
                            var c = sweepHits[i].collider;
                            if (c == null) continue;
                            if (actor != null && (c.transform == actor || c.transform.IsChildOf(actor))) continue;
                            if (Held != null && (c.transform == Held.transform || c.transform.IsChildOf(Held.transform))) continue;
                            clearanceBlocked = true;
                            break;
                        }
                    }

                    // 2. Full shape overlap check at release pose
                    if (!clearanceBlocked)
                    {
                        int overlapCount = ps.OverlapBox(releaseCenter, halfExtents, overlapHits, releaseRot, ClearanceMask, QueryTriggerInteraction.Ignore);
                        if (overlapCount >= overlapHits.Length)
                            return Finish(Deny("clearance-check-overflow"));

                        for (int i = 0; i < overlapCount; i++)
                        {
                            var c = overlapHits[i];
                            if (c == null) continue;
                            if (actor != null && (c.transform == actor || c.transform.IsChildOf(actor))) continue;
                            if (Held != null && (c.transform == Held.transform || c.transform.IsChildOf(Held.transform))) continue;
                            clearanceBlocked = true;
                            break;
                        }
                    }
                }
                else if (heldCol is SphereCollider sphere)
                {
                    float radius = sphere.radius * Mathf.Max(Held.transform.lossyScale.x, Mathf.Max(Held.transform.lossyScale.y, Held.transform.lossyScale.z));
                    Vector3 heldCenter = heldPos + Held.transform.rotation * sphere.center;
                    Vector3 releaseCenter = releasePos + releaseRot * sphere.center;
                    Vector3 centerDelta = releaseCenter - heldCenter;
                    float centerDist = centerDelta.magnitude;
                    Vector3 centerDir = centerDist > 0.0001f ? centerDelta / centerDist : Vector3.forward;

                    if (centerDist > 0.001f)
                    {
                        int sweepCount = ps.SphereCast(heldCenter, radius, centerDir, sweepHits, centerDist, ClearanceMask, QueryTriggerInteraction.Ignore);
                        if (sweepCount >= sweepHits.Length)
                            return Finish(Deny("clearance-check-overflow"));

                        for (int i = 0; i < sweepCount; i++)
                        {
                            var c = sweepHits[i].collider;
                            if (c == null) continue;
                            if (actor != null && (c.transform == actor || c.transform.IsChildOf(actor))) continue;
                            if (Held != null && (c.transform == Held.transform || c.transform.IsChildOf(Held.transform))) continue;
                            clearanceBlocked = true;
                            break;
                        }
                    }

                    if (!clearanceBlocked)
                    {
                        int overlapCount = ps.OverlapSphere(releaseCenter, radius, overlapHits, ClearanceMask, QueryTriggerInteraction.Ignore);
                        if (overlapCount >= overlapHits.Length)
                            return Finish(Deny("clearance-check-overflow"));

                        for (int i = 0; i < overlapCount; i++)
                        {
                            var c = overlapHits[i];
                            if (c == null) continue;
                            if (actor != null && (c.transform == actor || c.transform.IsChildOf(actor))) continue;
                            if (Held != null && (c.transform == Held.transform || c.transform.IsChildOf(Held.transform))) continue;
                            clearanceBlocked = true;
                            break;
                        }
                    }
                }
                else
                {
                    return Finish(Deny("unsupported-physical-collider"));
                }

                if (clearanceBlocked)
                    return Finish(Deny("release-clearance-blocked"));

                if (phys != null)
                {
                    var modelReq = new ItemActionRequest
                    {
                        requestId = requestId,
                        action = ItemActionKind.Drop,
                        actorId = agentId,
                        itemId = Held.StableId,
                        position = releasePos,
                        rotation = releaseRot
                    };
                    var modelReceipt = PhysicalModel.Execute(worldId, PhysicalModel.GenerationId, modelReq, PhysicalAuthority);
                    if (!modelReceipt.success)
                        return Finish(Deny(modelReceipt.code));

                    phys.ReleaseToPhysics(releasePos, releaseRot);
                }
                else
                {
                    Held.transform.SetParent(null, true);
                    Held.transform.SetPositionAndRotation(releasePos, releaseRot);
                }

                var dropped = Held;
                dropped.HeldBy = "";
                Held = null;
                return Finish(new NpcActionResult { success = true, code = "dropped" });
            }

            // Pickup and Deliver common gates:
            if (!objects.TryGetValue(targetId ?? "", out var target) || target == null || !target.isActiveAndEnabled)
                return Finish(Deny("target-unavailable"));
            if (target.WorldId != worldId || target.gameObject.scene != actor.gameObject.scene)
                return Finish(Deny("world-mismatch"));
            if (!target.Permission || (action == NpcActionKind.Pickup ? !AllowPickup : !AllowDelivery))
                return Finish(Deny("permission-denied"));
            if (target.Approach == null || Vector3.Distance(actor.position, target.Approach.position) > .65f ||
                Vector3.Distance(actor.position + Vector3.up, target.SightPoint) > 1.7f)
                return Finish(Deny("out-of-reach"));
            Physics.SyncTransforms();
            Vector3 eye = actor.position + Vector3.up * 1.6f, delta = target.SightPoint - eye;
            var actorScene = actor.gameObject.scene;
            var actorPs = actorScene.GetPhysicsScene();
            RaycastHit losHit;
            bool hitSomething;
            if (actorPs.IsValid())
            {
                hitSomething = actorPs.Raycast(eye, delta.normalized, out losHit, delta.magnitude, (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore);
            }
            else
            {
                hitSomething = Physics.Raycast(eye, delta.normalized, out losHit, delta.magnitude, (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore);
            }
            bool losBlocked = false;
            if (hitSomething && losHit.collider != null)
            {
                Transform hitTransform = losHit.collider.transform;
                if (hitTransform != target.transform && !hitTransform.IsChildOf(target.transform) &&
                    hitTransform != actor && !hitTransform.IsChildOf(actor))
                {
                    losBlocked = true;
                }
            }
            if (losBlocked)
                return Finish(Deny("line-of-sight-blocked"));

            if (action == NpcActionKind.Pickup)
            {
                if (target.Kind != NpcObjectKind.Item) return Finish(Deny("wrong-target-kind"));
                if (Held != null) return Finish(Deny("hands-full"));
                if (!target.Available || hand == null) return Finish(Deny("item-unavailable"));

                var phys = target.GetComponent<PhysicalItem>();
                if (phys != null)
                {
                    if (PhysicalModel == null)
                        return Finish(Deny("physical-model-required"));
                    if (!ValidatePhysicalMetadata(phys, target, out string metaDeny))
                        return Finish(Deny(metaDeny));
                    if (phys.isAnchored)
                        return Finish(Deny("anchored-item-cannot-be-picked-up"));

                    var modelReq = new ItemActionRequest
                    {
                        requestId = requestId,
                        action = ItemActionKind.Pickup,
                        actorId = agentId,
                        itemId = target.StableId
                    };
                    var modelReceipt = PhysicalModel.Execute(worldId, PhysicalModel.GenerationId, modelReq, PhysicalAuthority);
                    if (!modelReceipt.success)
                        return Finish(Deny(modelReceipt.code));

                    phys.AttachToHand(hand);
                    target.HeldBy = agentId;
                    Held = target;
                    return Finish(new NpcActionResult { success = true, code = "picked-up" });
                }

                // Non-physical interactable legacy pickup
                target.transform.SetParent(hand, false); target.transform.localPosition = new Vector3(.06f, .04f, 0);
                target.transform.localRotation = Quaternion.identity;
                target.HeldBy = agentId; Held = target;
                return Finish(new NpcActionResult { success = true, code = "picked-up" });
            }

            if (target.Kind != NpcObjectKind.Destination) return Finish(Deny("wrong-target-kind"));
            var heldPhys = Held != null ? Held.GetComponent<PhysicalItem>() : null;
            bool isDeliveredByHand = heldPhys != null ? (heldPhys.CarriedHand == hand && heldPhys.IsCarried) : (Held != null && Held.transform.parent == hand);
            if (Held == null || Held.HeldBy != agentId || !isDeliveredByHand)
                return Finish(Deny("cargo-ownership-mismatch"));

            if (heldPhys != null)
                return Finish(Deny("physical-delivery-not-supported-in-slice"));

            if (!target.Available || target.Socket == null) return Finish(Deny("destination-full-or-invalid"));
            Held.transform.SetParent(target.Socket, false); Held.transform.localPosition = Vector3.zero;
            Held.transform.localRotation = Quaternion.identity;
            target.Occupant = Held.StableId; Held.DeliveredTo = target.StableId; Held.HeldBy = "";
            Held = null; Deliveries++;
            return Finish(new NpcActionResult { success = true, code = "delivered" });
        }

        private NpcInteractable GetAccessibleRootInteractable(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return null;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string currentId = targetId;
            while (!string.IsNullOrEmpty(currentId) && visited.Add(currentId))
            {
                if (!objects.TryGetValue(currentId, out var currentInteractable) || currentInteractable == null)
                    return null;

                var phys = currentInteractable.GetComponent<PhysicalItem>();
                if (phys != null && phys.IsStored && !string.IsNullOrEmpty(phys.BoundContainerItemId))
                {
                    currentId = phys.BoundContainerItemId;
                    continue;
                }

                if (PhysicalModel != null && PhysicalModel.TryGetItem(currentId, out var snap) &&
                    snap.location == ItemLocationKind.Stored && !string.IsNullOrEmpty(snap.containerItemId))
                {
                    currentId = snap.containerItemId;
                    continue;
                }

                return currentInteractable;
            }
            return null;
        }

        public NpcActionResult Execute(int requestId, NpcActionKind action, string itemId, string containerId)
        {
            if (action == NpcActionKind.Pickup || action == NpcActionKind.Deliver || action == NpcActionKind.Drop)
            {
                return Execute(requestId, action, itemId);
            }

            NpcActionResult Deny(string code) => new NpcActionResult { code = code, success = false, duplicate = false };
            if (requestId <= 0) return Deny("invalid-request-id");
            if (action != NpcActionKind.Store && action != NpcActionKind.Retrieve)
                return Deny("unsupported-action");

            if (PhysicalModel == null) return Deny("physical-model-required");
            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(containerId)) return Deny("invalid-scope-id");

            var modelReq = new ItemActionRequest
            {
                requestId = requestId,
                action = (action == NpcActionKind.Store ? ItemActionKind.Store : ItemActionKind.Retrieve),
                actorId = agentId,
                itemId = itemId,
                targetId = containerId
            };

            string canonicalSignature = ItemModel.BuildRequestSignature(modelReq, Quaternion.identity);
            if (PhysicalModel.TryGetReceipt(requestId, out string priorSig, out var priorReceipt))
            {
                if (string.Equals(priorSig, canonicalSignature, StringComparison.Ordinal))
                {
                    return new NpcActionResult
                    {
                        success = priorReceipt.success,
                        duplicate = true,
                        code = priorReceipt.code
                    };
                }
                return Deny("request-id-conflict");
            }

            NpcActionResult FailScoped(string code)
            {
                var receipt = PhysicalModel.RecordRejectionReceipt(worldId, PhysicalModel.GenerationId, modelReq, code);
                return new NpcActionResult
                {
                    success = receipt.success,
                    duplicate = receipt.duplicate,
                    code = receipt.code
                };
            }

            if (string.Equals(itemId, containerId, StringComparison.Ordinal)) return FailScoped("cannot-store-in-itself");

            if (action == NpcActionKind.Store)
            {
                if (Held == null) return FailScoped("cargo-ownership-mismatch");
                if (!string.Equals(Held.StableId, itemId, StringComparison.Ordinal) || Held.HeldBy != agentId)
                    return FailScoped("cargo-ownership-mismatch");
                if (!objects.TryGetValue(itemId, out var regHeld) || regHeld != Held)
                    return FailScoped("cargo-ownership-mismatch");
                var heldPhys = Held.GetComponent<PhysicalItem>();
                if (heldPhys == null || !heldPhys.IsCarried || heldPhys.CarriedHand != hand)
                    return FailScoped("cargo-ownership-mismatch");
                if (hand == null) return FailScoped("item-unavailable");
                if (hand.gameObject.scene != actor.gameObject.scene) return FailScoped("world-mismatch");
            }
            else // Retrieve
            {
                if (Held != null) return FailScoped("hands-full");
                if (hand == null) return FailScoped("item-unavailable");
                if (hand.gameObject.scene != actor.gameObject.scene) return FailScoped("world-mismatch");
            }

            NpcInteractable itemInteractable;
            NpcInteractable containerInteractable;

            if (action == NpcActionKind.Store)
            {
                itemInteractable = Held;
                if (!objects.TryGetValue(containerId, out containerInteractable) || containerInteractable == null || !containerInteractable.isActiveAndEnabled)
                    return FailScoped("target-unavailable");
            }
            else // Retrieve
            {
                if (!objects.TryGetValue(itemId, out itemInteractable) || itemInteractable == null || !itemInteractable.isActiveAndEnabled)
                    return FailScoped("target-unavailable");
                if (!objects.TryGetValue(containerId, out containerInteractable) || containerInteractable == null || !containerInteractable.isActiveAndEnabled)
                    return FailScoped("target-unavailable");
            }

            if (itemInteractable.WorldId != worldId || itemInteractable.gameObject.scene != actor.gameObject.scene ||
                containerInteractable.WorldId != worldId || containerInteractable.gameObject.scene != actor.gameObject.scene)
            {
                return FailScoped("world-mismatch");
            }

            var rootInteractable = GetAccessibleRootInteractable(containerId);
            if (rootInteractable == null || !rootInteractable.isActiveAndEnabled)
                return FailScoped("target-unavailable");
            if (rootInteractable.WorldId != worldId || rootInteractable.gameObject.scene != actor.gameObject.scene)
                return FailScoped("world-mismatch");
            if (!itemInteractable.Permission || !containerInteractable.Permission || !rootInteractable.Permission)
                return FailScoped("permission-denied");

            bool containerHeldBySelf = (rootInteractable == Held || string.Equals(rootInteractable.HeldBy, agentId, StringComparison.Ordinal));
            if (!containerHeldBySelf)
            {
                if (rootInteractable.Approach == null ||
                    Vector3.Distance(actor.position, rootInteractable.Approach.position) > .65f ||
                    Vector3.Distance(actor.position + Vector3.up, rootInteractable.SightPoint) > 1.7f)
                {
                    return FailScoped("out-of-reach");
                }

                Physics.SyncTransforms();
                Vector3 eye = actor.position + Vector3.up * 1.6f, delta = rootInteractable.SightPoint - eye;
                var actorScene = actor.gameObject.scene;
                var actorPs = actorScene.GetPhysicsScene();
                RaycastHit losHit;
                bool hitSomething;
                if (actorPs.IsValid())
                {
                    hitSomething = actorPs.Raycast(eye, delta.normalized, out losHit, delta.magnitude, (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore);
                }
                else
                {
                    hitSomething = Physics.Raycast(eye, delta.normalized, out losHit, delta.magnitude, (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore);
                }
                bool losBlocked = false;
                if (hitSomething && losHit.collider != null)
                {
                    Transform hitTransform = losHit.collider.transform;
                    if (hitTransform != rootInteractable.transform && !hitTransform.IsChildOf(rootInteractable.transform) &&
                        hitTransform != actor && !hitTransform.IsChildOf(actor))
                    {
                        losBlocked = true;
                    }
                }
                if (losBlocked)
                    return FailScoped("line-of-sight-blocked");
            }

            var itemPhys = itemInteractable.GetComponent<PhysicalItem>();
            string itemMetaDeny = null;
            if (itemPhys == null || !ValidatePhysicalMetadata(itemPhys, itemInteractable, out itemMetaDeny))
            {
                return FailScoped(itemMetaDeny ?? "physical-item-invalid");
            }

            var containerPhys = containerInteractable.GetComponent<PhysicalItem>();
            string containerMetaDeny = null;
            if (containerPhys == null || !ValidatePhysicalMetadata(containerPhys, containerInteractable, out containerMetaDeny))
            {
                return FailScoped(containerMetaDeny ?? "physical-item-invalid");
            }

            if (action == NpcActionKind.Retrieve)
            {
                if (!itemPhys.IsStored || !string.Equals(itemPhys.BoundContainerItemId, containerId, StringComparison.Ordinal))
                {
                    return FailScoped("target-unavailable");
                }
            }

            if (!PhysicalModel.TryPrepareTransition(worldId, PhysicalModel.GenerationId, modelReq, PhysicalAuthority, out var token, out string prepDeny))
            {
                return FailScoped(prepDeny);
            }

            var itemCapture = itemPhys.CaptureRuntimeState();
            var oldHeld = Held;
            var oldItemHeldBy = itemInteractable.HeldBy;

            bool physicalSucceeded = false;
            try
            {
                if (action == NpcActionKind.Store)
                {
                    itemPhys.ApplyStored(containerInteractable.transform, containerId);
                    itemInteractable.HeldBy = "";
                    Held = null;
                }
                else // Retrieve
                {
                    itemPhys.AttachToHand(hand);
                    itemInteractable.HeldBy = agentId;
                    Held = itemInteractable;
                }

                if (FailAfterPhysicalApplyForTesting)
                {
                    throw new InvalidOperationException("injected-test-failpoint-after-apply");
                }
                physicalSucceeded = true;
            }
            catch
            {
                physicalSucceeded = false;
            }

            if (!physicalSucceeded)
            {
                itemPhys.RestoreRuntimeState(itemCapture);
                Held = oldHeld;
                itemInteractable.HeldBy = oldItemHeldBy;
                PhysicalModel.CancelPreparedTransition(token);
                return FailScoped("physical-application-failed");
            }

            if (!PhysicalModel.TryCommitTransition(token, out var commitReceipt))
            {
                itemPhys.RestoreRuntimeState(itemCapture);
                Held = oldHeld;
                itemInteractable.HeldBy = oldItemHeldBy;
                return FailScoped("commit-failed");
            }

            return new NpcActionResult { success = true, duplicate = false, code = commitReceipt.code };
        }
    }
}
