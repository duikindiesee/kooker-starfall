using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.Items
{
    /// <summary>
    /// Deterministic in-memory checks for physical item model, state transitions,
    /// aliasing protection, rotation ingress, replay identity, nested containers,
    /// container authorization, and placement authority.
    /// Pure C# check suite with zero file/network/native engine side effects.
    /// </summary>
    public static class ItemChecks
    {
        public static List<string> Run()
        {
            var passed = new List<string>();

            void Check(bool condition, string name)
            {
                if (!condition)
                    throw new InvalidOperationException("PHYSICAL ITEM CHECK FAILED: " + name);
                passed.Add(name);
            }

            const string world = "test-physical-world-01";
            const string gen = "gen-01";
            const string actor1 = "actor-starfall-01";
            const string actor2 = "actor-starfall-02";

            var authority = new BasicItemActionAuthority();

            // -------------------------------------------------------------
            // 1. Definition aliasing protection (ingress & egress)
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                var callerDef = new ItemDefinition
                {
                    itemTypeId = "test-wood-log",
                    dimensions = new PhysicalDimensions(0.2f, 0.2f, 1.0f),
                    massKg = 4.5f,
                    isAnchored = false
                };

                Check(model.RegisterDefinition(callerDef), "register-definition-succeeds");

                // Mutate caller's original object after registration
                callerDef.massKg = 9999f;
                callerDef.isAnchored = true;
                callerDef.dimensions.width = 50f;

                Check(model.TryGetDefinition("test-wood-log", out var internalDef1), "fetch-registered-def");
                Check(Mathf.Approximately(internalDef1.massKg, 4.5f), "def-ingress-copy-prevents-original-mutation-aliasing");
                Check(!internalDef1.isAnchored, "def-ingress-copy-preserves-anchored-flag");
                Check(Mathf.Approximately(internalDef1.dimensions.width, 0.2f), "def-ingress-copy-preserves-dimensions");

                // Mutate retrieved copy
                internalDef1.massKg = 8888f;
                internalDef1.isContainer = true;
                internalDef1.maxContainedSlots = 100;

                Check(model.TryGetDefinition("test-wood-log", out var internalDef2), "fetch-second-copy-of-def");
                Check(Mathf.Approximately(internalDef2.massKg, 4.5f), "def-egress-copy-prevents-caller-mutation-aliasing");
                Check(!internalDef2.isContainer, "def-egress-copy-preserves-container-flag");
            }

            // -------------------------------------------------------------
            // 2. Rotation ingress validation & canonicalization
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "log",
                    dimensions = new PhysicalDimensions(0.2f, 0.2f, 1.0f),
                    massKg = 3.0f
                });

                // Zero quaternion
                Check(!ItemDefinition.TryCanonicalizeRotation(new Quaternion(0f, 0f, 0f, 0f), out _),
                    "zero-quaternion-canonicalization-fails");
                Check(!model.RegisterItem("log-zero", "log", ItemLocationKind.Free, Vector3.zero, new Quaternion(0f, 0f, 0f, 0f)),
                    "registration-rejects-zero-quaternion");

                // Non-unit quaternion
                Check(!ItemDefinition.TryCanonicalizeRotation(new Quaternion(2f, 0f, 0f, 0f), out _),
                    "non-unit-quaternion-canonicalization-fails");
                Check(!model.RegisterItem("log-nonunit", "log", ItemLocationKind.Free, Vector3.zero, new Quaternion(2f, 0f, 0f, 0f)),
                    "registration-rejects-non-unit-quaternion");

                // NaN quaternion
                Check(!ItemDefinition.TryCanonicalizeRotation(new Quaternion(float.NaN, 0f, 0f, 1f), out _),
                    "nan-quaternion-canonicalization-fails");
                Check(!model.RegisterItem("log-nan", "log", ItemLocationKind.Free, Vector3.zero, new Quaternion(float.NaN, 0f, 0f, 1f)),
                    "registration-rejects-nan-quaternion");

                // Infinity quaternion
                Check(!ItemDefinition.TryCanonicalizeRotation(new Quaternion(float.PositiveInfinity, 0f, 0f, 0f), out _),
                    "infinity-quaternion-canonicalization-fails");
                Check(!model.RegisterItem("log-inf", "log", ItemLocationKind.Free, Vector3.zero, new Quaternion(float.PositiveInfinity, 0f, 0f, 0f)),
                    "registration-rejects-infinity-quaternion");

                // Valid unit quaternion
                var validRot = Quaternion.Euler(0f, 45f, 0f);
                Check(ItemDefinition.TryCanonicalizeRotation(validRot, out var canonicalRot),
                    "valid-unit-quaternion-canonicalizes");
                Check(Mathf.Approximately(canonicalRot.x * canonicalRot.x + canonicalRot.y * canonicalRot.y + canonicalRot.z * canonicalRot.z + canonicalRot.w * canonicalRot.w, 1.0f),
                    "canonical-rotation-is-exact-unit-length");
                Check(model.RegisterItem("log-valid", "log", ItemLocationKind.Free, Vector3.zero, validRot),
                    "registration-accepts-valid-unit-quaternion");

                // Drop command with non-unit quaternion fails
                model.Execute(world, gen, new ItemActionRequest
                {
                    requestId = 1,
                    action = ItemActionKind.Pickup,
                    actorId = actor1,
                    itemId = "log-valid"
                }, authority);

                var dropNonUnit = model.Execute(world, gen, new ItemActionRequest
                {
                    requestId = 2,
                    action = ItemActionKind.Drop,
                    actorId = actor1,
                    itemId = "log-valid",
                    position = new Vector3(1f, 0f, 1f),
                    rotation = new Quaternion(0f, 0f, 0f, 0f)
                }, authority);
                Check(!dropNonUnit.success && dropNonUnit.code == "invalid-rotation",
                    "drop-command-rejects-zero-rotation");
            }

            // -------------------------------------------------------------
            // 3. Replay identity & changed transform conflict
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "stone",
                    dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                    massKg = 2.0f
                });
                model.RegisterItem("stone-1", "stone", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

                model.Execute(world, gen, new ItemActionRequest
                {
                    requestId = 1,
                    action = ItemActionKind.Pickup,
                    actorId = actor1,
                    itemId = "stone-1"
                }, authority);

                var dropReq = new ItemActionRequest
                {
                    requestId = 2,
                    action = ItemActionKind.Drop,
                    actorId = actor1,
                    itemId = "stone-1",
                    position = new Vector3(1f, 2f, 3f),
                    rotation = Quaternion.identity
                };

                var dropResult = model.Execute(world, gen, dropReq, authority);
                Check(dropResult.success && dropResult.code == "dropped-intent-recorded",
                    "initial-drop-intent-succeeds");

                // Replay identical drop request
                var replayIdentical = model.Execute(world, gen, dropReq, authority);
                Check(replayIdentical.duplicate && replayIdentical.success,
                    "identical-drop-replay-returns-duplicate-receipt");

                // Replay same requestId with changed position
                var dropChangedPos = new ItemActionRequest
                {
                    requestId = 2,
                    action = ItemActionKind.Drop,
                    actorId = actor1,
                    itemId = "stone-1",
                    position = new Vector3(9f, 2f, 3f), // Changed!
                    rotation = Quaternion.identity
                };
                var conflictPos = model.Execute(world, gen, dropChangedPos, authority);
                Check(!conflictPos.success && conflictPos.code == "request-id-conflict",
                    "changed-position-under-same-request-id-conflicts");

                // Replay same requestId with changed rotation
                var dropChangedRot = new ItemActionRequest
                {
                    requestId = 2,
                    action = ItemActionKind.Drop,
                    actorId = actor1,
                    itemId = "stone-1",
                    position = new Vector3(1f, 2f, 3f),
                    rotation = Quaternion.Euler(0f, 90f, 0f) // Changed!
                };
                var conflictRot = model.Execute(world, gen, dropChangedRot, authority);
                Check(!conflictRot.success && conflictRot.code == "request-id-conflict",
                    "changed-rotation-under-same-request-id-conflicts");

                // Confirm item pose remained at the original (1, 2, 3)
                model.TryGetItem("stone-1", out var stateAfterConflicts);
                Check(stateAfterConflicts.position == new Vector3(1f, 2f, 3f),
                    "conflicting-replay-leaves-stored-transform-unchanged");
            }

            // -------------------------------------------------------------
            // 4. Nested containers: capacity checks and ancestor overload
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "outer-basket",
                    dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                    massKg = 1.0f,
                    isContainer = true,
                    maxContainedMassKg = 10.0f,
                    maxContainedVolumeM3 = 0.5f,
                    maxContainedSlots = 4
                });
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "inner-basket",
                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                    massKg = 0.5f,
                    isContainer = true,
                    maxContainedMassKg = 5.0f,
                    maxContainedVolumeM3 = 0.2f,
                    maxContainedSlots = 3
                });
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "dense-stone",
                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                    massKg = 4.0f
                });
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "medium-stone",
                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                    massKg = 2.0f
                });

                model.RegisterItem("outer-1", "outer-basket", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("inner-1", "inner-basket", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("stone-a", "dense-stone", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("stone-b", "medium-stone", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

                // Actor1 picks up inner-1, stores into outer-1
                model.Execute(world, gen, new ItemActionRequest { requestId = 1, action = ItemActionKind.Pickup, actorId = actor1, itemId = "inner-1" }, authority);
                model.Execute(world, gen, new ItemActionRequest { requestId = 2, action = ItemActionKind.Store, actorId = actor1, itemId = "inner-1", targetId = "outer-1" }, authority);

                // Actor1 picks up stone-a (4kg), stores into inner-1
                model.Execute(world, gen, new ItemActionRequest { requestId = 3, action = ItemActionKind.Pickup, actorId = actor1, itemId = "stone-a" }, authority);
                var storeStoneA = model.Execute(world, gen, new ItemActionRequest { requestId = 4, action = ItemActionKind.Store, actorId = actor1, itemId = "stone-a", targetId = "inner-1" }, authority);
                Check(storeStoneA.success && storeStoneA.code == "stored-in-container",
                    "store-into-nested-inner-basket-succeeds");

                // Total contained in inner-1 = 4.0kg. Inner limit = 5.0kg.
                // Total contained in outer-1 = inner-1 (0.5kg) + stone-a (4.0kg) = 4.5kg.
                Check(Mathf.Approximately(model.GetItemTotalMassKg("outer-1"), 5.5f),
                    "total-outer-container-mass-includes-all-nested-contents-exactly-once");

                // Now attempt to store stone-b (2kg) into inner-1:
                // Immediate inner-1 contained would be 4.0 + 2.0 = 6.0kg > 5.0kg!
                model.Execute(world, gen, new ItemActionRequest { requestId = 5, action = ItemActionKind.Pickup, actorId = actor1, itemId = "stone-b" }, authority);
                var storeStoneB = model.Execute(world, gen, new ItemActionRequest { requestId = 6, action = ItemActionKind.Store, actorId = actor1, itemId = "stone-b", targetId = "inner-1" }, authority);
                Check(!storeStoneB.success && storeStoneB.code == "container-mass-capacity-exceeded",
                    "nested-store-rejects-immediate-container-mass-overload");
                Check(model.GetActorCarriedItemIds(actor1).Contains("stone-b"),
                    "failed-store-rolls-back-holding-state");

                // Now test ancestor overload: outer basket maxContainedMass = 5.0kg, inner basket maxContainedMass = 8.0kg.
                var modelAncestor = new ItemModel(world, gen);
                modelAncestor.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "tight-outer",
                    dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                    massKg = 1.0f,
                    isContainer = true,
                    maxContainedMassKg = 5.0f,
                    maxContainedVolumeM3 = 0.5f,
                    maxContainedSlots = 4
                });
                modelAncestor.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "spacious-inner",
                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                    massKg = 0.5f,
                    isContainer = true,
                    maxContainedMassKg = 8.0f, // spacious inner
                    maxContainedVolumeM3 = 0.3f,
                    maxContainedSlots = 4
                });
                modelAncestor.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "stone-3kg",
                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                    massKg = 3.0f
                });
                modelAncestor.RegisterItem("tight-outer-1", "tight-outer", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                modelAncestor.RegisterItem("spacious-inner-1", "spacious-inner", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                modelAncestor.RegisterItem("stone-first", "stone-3kg", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                modelAncestor.RegisterItem("stone-second", "stone-3kg", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

                modelAncestor.Execute(world, gen, new ItemActionRequest { requestId = 1, action = ItemActionKind.Pickup, actorId = actor1, itemId = "spacious-inner-1" }, authority);
                modelAncestor.Execute(world, gen, new ItemActionRequest { requestId = 2, action = ItemActionKind.Store, actorId = actor1, itemId = "spacious-inner-1", targetId = "tight-outer-1" }, authority);
                modelAncestor.Execute(world, gen, new ItemActionRequest { requestId = 3, action = ItemActionKind.Pickup, actorId = actor1, itemId = "stone-first" }, authority);
                modelAncestor.Execute(world, gen, new ItemActionRequest { requestId = 4, action = ItemActionKind.Store, actorId = actor1, itemId = "stone-first", targetId = "spacious-inner-1" }, authority);

                // Now spacious-inner-1 contains 3.0kg (limit 8.0kg).
                // tight-outer-1 contains inner (0.5kg) + stone-first (3.0kg) = 3.5kg (limit 5.0kg).
                // Adding stone-second (3.0kg) to spacious-inner-1:
                // inner capacity: 3.0 + 3.0 = 6.0kg <= 8.0kg (OK for inner!).
                // BUT outer capacity: 3.5 + 3.0 = 6.5kg > 5.0kg (OVERLOAD FOR OUTER ANCESTOR!).
                modelAncestor.Execute(world, gen, new ItemActionRequest { requestId = 5, action = ItemActionKind.Pickup, actorId = actor1, itemId = "stone-second" }, authority);
                var ancestorOverload = modelAncestor.Execute(world, gen, new ItemActionRequest { requestId = 6, action = ItemActionKind.Store, actorId = actor1, itemId = "stone-second", targetId = "spacious-inner-1" }, authority);
                Check(!ancestorOverload.success && ancestorOverload.code == "ancestor-container-mass-capacity-exceeded",
                    "nested-store-rejects-ancestor-container-overload");
                Check(modelAncestor.GetActorCarriedItemIds(actor1).Contains("stone-second"),
                    "failed-ancestor-overload-preserves-carried-item");
            }

            // -------------------------------------------------------------
            // 5. Nested containers: retrieve net mass change (no double counting)
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "outer-basket",
                    dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                    massKg = 1.0f,
                    isContainer = true,
                    maxContainedMassKg = 10.0f,
                    maxContainedVolumeM3 = 0.5f,
                    maxContainedSlots = 4
                });
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "inner-basket",
                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                    massKg = 0.5f,
                    isContainer = true,
                    maxContainedMassKg = 5.0f,
                    maxContainedVolumeM3 = 0.2f,
                    maxContainedSlots = 3
                });
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "heavy-stone",
                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                    massKg = 4.0f
                });

                model.RegisterItem("outer-basket-1", "outer-basket", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("inner-basket-1", "inner-basket", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("heavy-stone-1", "heavy-stone", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

                // Put inner into outer, stone into inner
                model.Execute(world, gen, new ItemActionRequest { requestId = 1, action = ItemActionKind.Pickup, actorId = actor1, itemId = "inner-basket-1" }, authority);
                model.Execute(world, gen, new ItemActionRequest { requestId = 2, action = ItemActionKind.Store, actorId = actor1, itemId = "inner-basket-1", targetId = "outer-basket-1" }, authority);
                model.Execute(world, gen, new ItemActionRequest { requestId = 3, action = ItemActionKind.Pickup, actorId = actor1, itemId = "heavy-stone-1" }, authority);
                model.Execute(world, gen, new ItemActionRequest { requestId = 4, action = ItemActionKind.Store, actorId = actor1, itemId = "heavy-stone-1", targetId = "inner-basket-1" }, authority);

                // Set Actor1 capacity limit to 7.0 kg, and 2 item hands
                model.SetActorCarryLimits(actor1, new ActorCarryLimits(7.0f, 2));

                // Actor1 picks up outer-basket-1: total mass is 1.0 + 0.5 + 4.0 = 5.5 kg <= 7.0 kg
                var pickupOuter = model.Execute(world, gen, new ItemActionRequest { requestId = 5, action = ItemActionKind.Pickup, actorId = actor1, itemId = "outer-basket-1" }, authority);
                Check(pickupOuter.success, "actor-picks-up-outer-basket-holding-nested-contents");
                Check(Mathf.Approximately(model.GetActorCarriedMassKg(actor1), 5.5f),
                    "actor-carried-mass-matches-outer-and-nested-mass");

                // Now actor retrieves heavy-stone-1 directly from inner-basket-1 into second hand:
                // Because outer-basket is ALREADY carried by actor, the net mass change is ZERO!
                // If blind addition were used, 5.5 + 4.0 = 9.5 kg > 7.0 kg would fail!
                var retrieveNested = model.Execute(world, gen, new ItemActionRequest
                {
                    requestId = 6,
                    action = ItemActionKind.Retrieve,
                    actorId = actor1,
                    itemId = "heavy-stone-1",
                    targetId = "inner-basket-1"
                }, authority);

                Check(retrieveNested.success && retrieveNested.code == "retrieved-from-container",
                    "retrieving-from-own-carried-nested-container-computes-zero-net-mass-change");
                Check(Mathf.Approximately(model.GetActorCarriedMassKg(actor1), 5.5f),
                    "actor-total-carried-mass-does-not-double-count-retrieved-item");
                Check(model.GetActorCarriedItemIds(actor1).Count == 2,
                    "actor-now-holds-outer-basket-and-retrieved-stone-in-hands");
            }

            // -------------------------------------------------------------
            // 6. Container authorization: foreign access denied, own allowed
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "basket",
                    dimensions = new PhysicalDimensions(0.4f, 0.4f, 0.4f),
                    massKg = 1.0f,
                    isContainer = true,
                    maxContainedMassKg = 10.0f,
                    maxContainedVolumeM3 = 0.5f,
                    maxContainedSlots = 4
                });
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "gem",
                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                    massKg = 0.5f
                });

                model.RegisterItem("basket-a", "basket", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("gem-a", "gem", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("gem-b", "gem", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

                // Actor1 picks up basket-a
                model.Execute(world, gen, new ItemActionRequest { requestId = 1, action = ItemActionKind.Pickup, actorId = actor1, itemId = "basket-a" }, authority);
                // Actor2 picks up gem-b
                model.Execute(world, gen, new ItemActionRequest { requestId = 2, action = ItemActionKind.Pickup, actorId = actor2, itemId = "gem-b" }, authority);

                // Actor2 attempts to store gem-b into Actor1's carried basket-a WITHOUT authority (null)
                var noAuthResult = model.Execute(world, gen, new ItemActionRequest
                {
                    requestId = 3,
                    action = ItemActionKind.Store,
                    actorId = actor2,
                    itemId = "gem-b",
                    targetId = "basket-a"
                }, null);
                Check(!noAuthResult.success && noAuthResult.code == "container-authority-missing",
                    "store-without-authority-defaults-to-refusal");

                // Actor2 attempts to store into Actor1's basket with standard authority (which disallows foreign access)
                var foreignStore = model.Execute(world, gen, new ItemActionRequest
                {
                    requestId = 4,
                    action = ItemActionKind.Store,
                    actorId = actor2,
                    itemId = "gem-b",
                    targetId = "basket-a"
                }, authority);
                Check(!foreignStore.success && foreignStore.code == "container-access-denied",
                    "foreign-actor-denied-access-to-other-actor-carried-container");

                // Actor1 drops basket-a to ground, but Actor2 tries to retrieve from it with authority requiring actor1
                var strictAuth = new BasicItemActionAuthority { requiredActorId = actor1 };
                var deniedActor2 = model.Execute(world, gen, new ItemActionRequest
                {
                    requestId = 5,
                    action = ItemActionKind.Store,
                    actorId = actor2,
                    itemId = "gem-b",
                    targetId = "basket-a"
                }, strictAuth);
                Check(!deniedActor2.success && deniedActor2.code == "container-access-denied",
                    "strict-authority-rejects-unauthorized-actor");
            }

            // -------------------------------------------------------------
            // 7. Placement authority: missing & mismatched denied, valid allowed
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "hearth-plank",
                    dimensions = new PhysicalDimensions(0.2f, 0.05f, 1.0f),
                    massKg = 3.0f
                });
                model.RegisterItem("plank-1", "hearth-plank", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

                model.Execute(world, gen, new ItemActionRequest { requestId = 1, action = ItemActionKind.Pickup, actorId = actor1, itemId = "plank-1" }, authority);

                var placeReqMissing = new ItemActionRequest
                {
                    requestId = 2,
                    action = ItemActionKind.Place,
                    actorId = actor1,
                    itemId = "plank-1",
                    targetId = "bench-socket-01",
                    position = new Vector3(2f, 1f, 2f),
                    rotation = Quaternion.identity,
                    supportNormal = new Vector3(0f, 1f, 0f)
                };

                // Placement without authority (requestId = 2)
                var noAuthPlace = model.Execute(world, gen, placeReqMissing, null);
                Check(!noAuthPlace.success && noAuthPlace.code == "placement-authority-missing",
                    "placement-without-authority-defaults-to-refusal");

                // Explicit assertion: identical same-ID replay still returns the original rejection even if different authority is now supplied
                var sameIdReplayWithAuthority = model.Execute(world, gen, placeReqMissing, authority);
                Check(sameIdReplayWithAuthority.duplicate && !sameIdReplayWithAuthority.success && sameIdReplayWithAuthority.code == "placement-authority-missing",
                    "same-id-replay-returns-original-rejection-even-with-new-authority");
                Check(model.GetActorCarriedItemIds(actor1).Contains("plank-1"),
                    "same-id-replay-rejection-leaves-ownership-unchanged");

                // Placement with authority that denies placement (distinct fresh requestId = 3)
                var denyingAuth = new BasicItemActionAuthority { allowPlacement = false };
                var placeReqDenying = new ItemActionRequest
                {
                    requestId = 3,
                    action = ItemActionKind.Place,
                    actorId = actor1,
                    itemId = "plank-1",
                    targetId = "bench-socket-01",
                    position = new Vector3(2f, 1f, 2f),
                    rotation = Quaternion.identity,
                    supportNormal = new Vector3(0f, 1f, 0f)
                };
                var deniedPlace = model.Execute(world, gen, placeReqDenying, denyingAuth);
                Check(!deniedPlace.success && deniedPlace.code == "placement-authorization-denied",
                    "placement-rejected-by-adapter-authority-fails");
                Check(model.GetActorCarriedItemIds(actor1).Contains("plank-1"),
                    "denied-placement-leaves-item-held-by-actor");

                // Legitimate placement with valid authority (distinct fresh requestId = 4)
                var placeReqApproved = new ItemActionRequest
                {
                    requestId = 4,
                    action = ItemActionKind.Place,
                    actorId = actor1,
                    itemId = "plank-1",
                    targetId = "bench-socket-01",
                    position = new Vector3(2f, 1f, 2f),
                    rotation = Quaternion.identity,
                    supportNormal = new Vector3(0f, 1f, 0f)
                };
                var approvedPlace = model.Execute(world, gen, placeReqApproved, authority);
                Check(approvedPlace.success && approvedPlace.code == "placed-on-support",
                    "legitimate-placement-succeeds-with-placed-on-support");

                model.TryGetItem("plank-1", out var placedState);
                Check(placedState.location == ItemLocationKind.Placed && placedState.placedSupportId == "bench-socket-01",
                    "placed-item-records-support-id-without-inventing-physical-settlement");
            }

            // -------------------------------------------------------------
            // 8. Anchored scenery cannot be picked up
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "hearth-boulder",
                    dimensions = new PhysicalDimensions(2.0f, 1.5f, 2.0f),
                    massKg = 800.0f,
                    isAnchored = true
                });
                model.RegisterItem("hearth-boulder-1", "hearth-boulder", ItemLocationKind.Anchored, Vector3.zero, Quaternion.identity);

                var pickupAnchored = model.Execute(world, gen, new ItemActionRequest
                {
                    requestId = 1,
                    action = ItemActionKind.Pickup,
                    actorId = actor1,
                    itemId = "hearth-boulder-1"
                }, authority);

                Check(!pickupAnchored.success && pickupAnchored.code == "anchored-scenery-cannot-be-picked-up",
                    "anchored-scenery-cannot-be-picked-up");
                Check(model.GetActorCarriedItemIds(actor1).Count == 0,
                    "failed-anchored-pickup-leaves-actor-hands-empty");
            }

            // -------------------------------------------------------------
            // 9. Scope mismatch & invalid identifier syntax
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "wood-stick",
                    dimensions = new PhysicalDimensions(0.05f, 0.05f, 0.8f),
                    massKg = 0.5f
                });
                model.RegisterItem("wood-stick-1", "wood-stick", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

                var wrongWorld = model.Execute("other-world", gen, new ItemActionRequest
                {
                    requestId = 1,
                    action = ItemActionKind.Pickup,
                    actorId = actor1,
                    itemId = "wood-stick-1"
                }, authority);
                Check(!wrongWorld.success && wrongWorld.code == "world-mismatch",
                    "foreign-world-scope-rejected");

                var wrongGen = model.Execute(world, "other-gen", new ItemActionRequest
                {
                    requestId = 2,
                    action = ItemActionKind.Pickup,
                    actorId = actor1,
                    itemId = "wood-stick-1"
                }, authority);
                Check(!wrongGen.success && wrongGen.code == "world-mismatch",
                    "foreign-generation-scope-rejected");

                var badActor = model.Execute(world, gen, new ItemActionRequest
                {
                    requestId = 3,
                    action = ItemActionKind.Pickup,
                    actorId = "bad actor name with spaces",
                    itemId = "wood-stick-1"
                }, authority);
                Check(!badActor.success && badActor.code == "invalid-scope-id",
                    "invalid-actor-identifier-rejected");
            }

            // -------------------------------------------------------------
            // 10. Containment cycle detection
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "box",
                    dimensions = new PhysicalDimensions(0.4f, 0.4f, 0.4f),
                    massKg = 1.0f,
                    isContainer = true,
                    maxContainedMassKg = 10f,
                    maxContainedVolumeM3 = 0.5f,
                    maxContainedSlots = 4
                });

                model.RegisterItem("box-a", "box", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("box-b", "box", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("box-c", "box", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

                // Self store
                model.Execute(world, gen, new ItemActionRequest { requestId = 1, action = ItemActionKind.Pickup, actorId = actor1, itemId = "box-a" }, authority);
                var selfStore = model.Execute(world, gen, new ItemActionRequest { requestId = 2, action = ItemActionKind.Store, actorId = actor1, itemId = "box-a", targetId = "box-a" }, authority);
                Check(!selfStore.success && selfStore.code == "cannot-store-in-itself",
                    "storing-container-in-itself-rejected");

                // Put box-b into box-a
                model.Execute(world, gen, new ItemActionRequest { requestId = 3, action = ItemActionKind.Drop, actorId = actor1, itemId = "box-a", position = Vector3.zero, rotation = Quaternion.identity }, authority);
                model.Execute(world, gen, new ItemActionRequest { requestId = 4, action = ItemActionKind.Pickup, actorId = actor1, itemId = "box-b" }, authority);
                model.Execute(world, gen, new ItemActionRequest { requestId = 5, action = ItemActionKind.Store, actorId = actor1, itemId = "box-b", targetId = "box-a" }, authority);

                // Put box-c into box-b
                model.Execute(world, gen, new ItemActionRequest { requestId = 6, action = ItemActionKind.Pickup, actorId = actor1, itemId = "box-c" }, authority);
                model.Execute(world, gen, new ItemActionRequest { requestId = 7, action = ItemActionKind.Store, actorId = actor1, itemId = "box-c", targetId = "box-b" }, authority);

                // Now hierarchy: box-a contains box-b contains box-c.
                // Attempt to put box-a into box-c -> cycle!
                model.Execute(world, gen, new ItemActionRequest { requestId = 8, action = ItemActionKind.Pickup, actorId = actor1, itemId = "box-a" }, authority);
                var cycleStore = model.Execute(world, gen, new ItemActionRequest { requestId = 9, action = ItemActionKind.Store, actorId = actor1, itemId = "box-a", targetId = "box-c" }, authority);
                Check(!cycleStore.success && cycleStore.code == "containment-cycle-detected",
                    "multi-level-containment-cycle-rejected-atomically");
            }

            // -------------------------------------------------------------
            // 11. Immutable snapshot protection
            // -------------------------------------------------------------
            {
                var model = new ItemModel(world, gen);
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "cup",
                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                    massKg = 0.2f
                });
                model.RegisterItem("cup-1", "cup", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

                Check(model.TryGetItem("cup-1", out var snapshot1), "get-item-snapshot");
                snapshot1.location = ItemLocationKind.Carried;
                snapshot1.holderActorId = "malicious-actor";
                snapshot1.position = new Vector3(999f, 999f, 999f);

                Check(model.TryGetItem("cup-1", out var snapshot2), "get-second-item-snapshot");
                Check(snapshot2.location == ItemLocationKind.Free, "item-state-egress-clone-protects-location");
                Check(string.IsNullOrEmpty(snapshot2.holderActorId), "item-state-egress-clone-protects-holder");
                Check(snapshot2.position == Vector3.zero, "item-state-egress-clone-protects-position");
            }

            return passed;
        }
    }
}
