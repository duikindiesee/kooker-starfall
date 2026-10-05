using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starfall.Food
{
    /// <summary>
    /// Immutable dated belief regarding an observed fruiting-succulent food source.
    /// Derived strictly from scoped PlaceLedger observation history.
    /// 
    /// INVARIANT: History is never ready-to-gather. A remembered available source
    /// ALWAYS requires live runtime revalidation upon arrival (via IFoodAccess / line-of-sight / reach).
    /// </summary>
    public readonly struct CaveFoodBelief
    {
        public readonly string Id;
        public readonly string ObjectKind;
        public readonly string ObservedType;
        public readonly Vector3 Position;
        public readonly int LastSeenFoodTick;
        public readonly int LastSeenBrainTick;
        public readonly string LastSeenKind; // "first-seen", "changed", "revisit"
        public readonly bool WasAvailableWhenSeen;
        public readonly bool WasPermittedWhenSeen;
        public readonly string ProvenanceHash;
        public readonly int Sequence;
        public readonly string Reason;
        public readonly string StatusText;

        /// <summary>
        /// A remembered source is NEVER ready to gather from history alone.
        /// Live runtime line-of-sight and reach revalidation via IFoodAccess is strictly mandatory.
        /// </summary>
        public bool ReadyToGather => false;

        /// <summary>
        /// Revalidation is always required before attempting interaction.
        /// </summary>
        public bool RequiresRuntimeRevalidation => true;

        /// <summary>
        /// Indicates whether this belief represents a permitted and available source when last seen,
        /// making it an eligible candidate for route choice (subject to revalidation upon arrival).
        /// </summary>
        public bool IsCandidateForRoute => WasPermittedWhenSeen && WasAvailableWhenSeen;

        public CaveFoodBelief(
            string id,
            string objectKind,
            string observedType,
            Vector3 position,
            int lastSeenFoodTick,
            int lastSeenBrainTick,
            string lastSeenKind,
            bool wasAvailableWhenSeen,
            bool wasPermittedWhenSeen,
            string provenanceHash,
            int sequence)
        {
            Id = id ?? string.Empty;
            ObjectKind = objectKind ?? string.Empty;
            ObservedType = observedType ?? string.Empty;
            Position = position;
            LastSeenFoodTick = lastSeenFoodTick;
            LastSeenBrainTick = lastSeenBrainTick;
            LastSeenKind = lastSeenKind ?? string.Empty;
            WasAvailableWhenSeen = wasAvailableWhenSeen;
            WasPermittedWhenSeen = wasPermittedWhenSeen;
            ProvenanceHash = provenanceHash ?? string.Empty;
            Sequence = sequence;

            if (!wasPermittedWhenSeen)
            {
                Reason = $"Fruiting succulent '{Id}' at {Position} (tick {lastSeenFoodTick}) was not permitted when last observed; route choice disqualified unless permission status changes. Current runtime revalidation required.";
            }
            else if (!wasAvailableWhenSeen)
            {
                Reason = $"Fruiting succulent '{Id}' at {Position} (tick {lastSeenFoodTick}) was depleted/unavailable when last observed; candidate route deprioritized until re-observed. Current runtime revalidation required.";
            }
            else
            {
                Reason = $"Fruiting succulent '{Id}' at {Position} (tick {lastSeenFoodTick}) was observed available and permitted; candidate for route choice. Never ready to gather from history alone; current runtime line-of-sight and reach revalidation mandatory on arrival.";
            }

            string availStr = wasAvailableWhenSeen ? "available" : "depleted";
            string permStr = wasPermittedWhenSeen ? "permitted" : "unpermitted";
            StatusText = $"Belief(t={lastSeenFoodTick}, brain={lastSeenBrainTick}, kind={lastSeenKind}): {Id} ({ObservedType}) - {availStr}, {permStr}";
        }
    }

    /// <summary>
    /// Narrow read-only projection over scoped FoodState / PlaceLedger memory.
    /// Exposes dated beliefs about observed fruiting-succulent food sources.
    /// 
    /// Strict architectural boundaries:
    /// - Pure projection: does not mutate FoodState or PlaceLedger.
    /// - Scope-enforced: requires matching world, generation, and actorId.
    /// - Ledger-validated: rejects tampered or broken hash chains (PlaceLedger.Valid).
    /// - Bounded knowledge: legacy/empty history yields unknown, never invented coordinates.
    /// - Superseded status: newest observation event overrides older availability.
    /// - Readiness invariant: historical beliefs are NEVER ready-to-gather without runtime revalidation.
    /// - Source-only: no claim of live wiring or autonomous execution.
    /// </summary>
    public static class CaveFoodMemory
    {
        public const string FruitingSucculentType = "fruiting-succulent";
        public const string PlaceObjectKind = "Place";

        /// <summary>
        /// Attempts to project fruiting-succulent beliefs from the supplied FoodState.
        /// Fails closed if scope or ledger validation fails.
        /// </summary>
        public static bool TryProject(
            FoodState state,
            string expectedWorld,
            string expectedGeneration,
            string expectedActor,
            out List<CaveFoodBelief> beliefs,
            out string rejectionReason)
        {
            beliefs = new List<CaveFoodBelief>();
            rejectionReason = null;

            if (state == null)
            {
                rejectionReason = "FoodState is null.";
                return false;
            }

            if (!FoodModel.Id(expectedWorld) || !FoodModel.Id(expectedGeneration) || !FoodModel.Id(expectedActor))
            {
                rejectionReason = "Invalid scope identifier format.";
                return false;
            }

            if (state.world != expectedWorld || state.generation != expectedGeneration || state.actorId != expectedActor)
            {
                rejectionReason = $"Scope mismatch: expected ({expectedWorld}, {expectedGeneration}, {expectedActor}) but state contains ({state.world}, {state.generation}, {state.actorId}).";
                return false;
            }

            if (!PlaceLedger.Valid(state))
            {
                rejectionReason = "PlaceLedger validation failed (tampered, corrupted, or inconsistent event chain).";
                return false;
            }

            // Empty or null legacy history yields unknown food (empty list, not discovered food).
            if (state.observedPlaces == null || state.observedPlaces.Count == 0)
            {
                return true;
            }

            // Collect only the LATEST observation event for each place ID where the latest observedType is "fruiting-succulent".
            // Iterate backwards through observedPlaces (newest first).
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = state.observedPlaces.Count - 1; i >= 0; i--)
            {
                var e = state.observedPlaces[i];
                if (e == null || !FoodModel.Id(e.id)) continue;

                if (!seenIds.Add(e.id))
                {
                    // An earlier event for this id was already seen (since we iterate in reverse).
                    continue;
                }

                // Only include if the latest observation of this place is a fruiting-succulent Place.
                if (e.objectKind == PlaceObjectKind && e.observedType == FruitingSucculentType)
                {
                    beliefs.Add(new CaveFoodBelief(
                        e.id,
                        e.objectKind,
                        e.observedType,
                        e.position,
                        e.foodTick,
                        e.brainTick,
                        e.kind,
                        e.available,
                        e.permitted,
                        e.hash,
                        e.sequence));
                }
            }

            // Ensure deterministic order by ID.
            beliefs.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return true;
        }

        /// <summary>
        /// Projects fruiting-succulent beliefs from the supplied FoodState.
        /// Throws ArgumentException if scope or ledger validation fails.
        /// </summary>
        public static List<CaveFoodBelief> Project(
            FoodState state,
            string expectedWorld,
            string expectedGeneration,
            string expectedActor)
        {
            if (!TryProject(state, expectedWorld, expectedGeneration, expectedActor, out var beliefs, out string reason))
            {
                throw new ArgumentException("CaveFoodMemory projection rejected: " + reason);
            }
            return beliefs;
        }

        /// <summary>
        /// Tries to get the latest belief for a specific food place ID (e.g. "berry-food").
        /// Returns false if unobserved, non-fruiting-succulent, or if projection fails closed.
        /// </summary>
        public static bool TryGetLatestBelief(
            FoodState state,
            string expectedWorld,
            string expectedGeneration,
            string expectedActor,
            string id,
            out CaveFoodBelief belief,
            out string rejectionReason)
        {
            belief = default;
            if (!TryProject(state, expectedWorld, expectedGeneration, expectedActor, out var beliefs, out rejectionReason))
            {
                return false;
            }

            foreach (var b in beliefs)
            {
                if (b.Id == id)
                {
                    belief = b;
                    return true;
                }
            }

            return false;
        }
    }
}
