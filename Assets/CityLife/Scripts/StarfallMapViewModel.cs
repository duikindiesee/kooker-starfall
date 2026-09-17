using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Starfall.Food;

namespace CityLife.World
{
    public readonly struct ExploredCellEntry
    {
        public readonly int X;
        public readonly int Z;
        public readonly int FirstFoodTick;
        public readonly int LastFoodTick;
        public readonly int Visits;

        public ExploredCellEntry(int x, int z, int firstFoodTick, int lastFoodTick, int visits)
        {
            X = x;
            Z = z;
            FirstFoodTick = firstFoodTick;
            LastFoodTick = lastFoodTick;
            Visits = visits;
        }
    }

    public readonly struct RememberedPlaceBelief
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
        public readonly string StatusText;

        public RememberedPlaceBelief(
            string id, string objectKind, string observedType, Vector3 position,
            int foodTick, int brainTick, string kind, bool available, bool permitted,
            string hash)
        {
            Id = id ?? "";
            ObjectKind = objectKind ?? "";
            ObservedType = observedType ?? "";
            Position = position;
            LastSeenFoodTick = foodTick;
            LastSeenBrainTick = brainTick;
            LastSeenKind = kind ?? "";
            WasAvailableWhenSeen = available;
            WasPermittedWhenSeen = permitted;
            ProvenanceHash = hash ?? "";

            string availStr = available ? "last seen available" : "last seen depleted/empty";
            StatusText = $"Belief (t={foodTick}): {ObservedType} [{availStr}]";
        }
    }

    public readonly struct ObservationEventEntry
    {
        public readonly int Sequence;
        public readonly string Kind; // "first-seen", "changed", "revisit"
        public readonly string Id;
        public readonly string ObservedType;
        public readonly Vector3 Position;
        public readonly int FoodTick;
        public readonly int BrainTick;
        public readonly bool Available;
        public readonly bool Permitted;
        public readonly string Hash;
        public readonly string Summary;

        public ObservationEventEntry(
            int sequence, string kind, string id, string observedType,
            Vector3 position, int foodTick, int brainTick, bool available,
            bool permitted, string hash)
        {
            Sequence = sequence;
            Kind = kind ?? "";
            Id = id ?? "";
            ObservedType = observedType ?? "";
            Position = position;
            FoodTick = foodTick;
            BrainTick = brainTick;
            Available = available;
            Permitted = permitted;
            Hash = hash ?? "";

            string detail = kind == "first-seen" ? "First observed" :
                            kind == "changed" ? "Condition changed" : "Revisited";
            string state = available ? "available" : "depleted";
            Summary = $"#{sequence} [t{foodTick}] {detail}: {id} ({observedType}) - {state}";
        }
    }

    // Read-only player map/history projection of existing FoodState PlaceLedger memory.
    // Consumes only scoped FoodState.exploredCells/observedPlaces and current inhabitant
    // position. Never consumes global resource registries, generated full terrain,
    // or secret coordinates. Fails closed on scope mismatch.
    // Employs revision-driven memory refresh and cached display to avoid frame-by-frame
    // garbage generation and revalidation overhead.
    public sealed class StarfallMapViewModel
    {
        public string ExpectedWorld { get; private set; }
        public string ExpectedGeneration { get; private set; }
        public string ExpectedActorId { get; private set; }
        public bool IsBound { get; private set; }
        public bool IsValid { get; private set; }
        public string StatusMessage { get; private set; } = "Unbound view model";

        public int Revision { get; private set; }
        public bool MemoryChanged { get; private set; }
        public bool DisplayChanged { get; private set; }

        public bool HasValidActorPosition { get; private set; }
        public Vector3 ActorPosition { get; private set; }
        public Vector2Int ActorCell { get; private set; }

        public int ExploredCellCount => exploredCells.Count;
        public int ObservedPlaceCount => rememberedBeliefs.Count;
        public int EventCount => observationEvents.Count;

        public int MinCellX { get; private set; }
        public int MaxCellX { get; private set; }
        public int MinCellZ { get; private set; }
        public int MaxCellZ { get; private set; }

        // Cached UI representations
        public string CachedAsciiGrid { get; private set; } = "";
        public string CachedStatusText { get; private set; } = "";
        public string CachedBeliefsText { get; private set; } = "";
        public string CachedEventsText { get; private set; } = "";

        private readonly List<ExploredCellEntry> exploredCells = new List<ExploredCellEntry>();
        private readonly Dictionary<string, ExploredCellEntry> cellLookup = new Dictionary<string, ExploredCellEntry>(StringComparer.Ordinal);
        private readonly List<RememberedPlaceBelief> rememberedBeliefs = new List<RememberedPlaceBelief>();
        private readonly Dictionary<string, RememberedPlaceBelief> beliefLookup = new Dictionary<string, RememberedPlaceBelief>(StringComparer.Ordinal);
        private readonly List<ObservationEventEntry> observationEvents = new List<ObservationEventEntry>();

        // Revision tracking
        private FoodState lastStateRef;
        private string lastValidatedWorld;
        private string lastValidatedGeneration;
        private string lastValidatedActorId;
        private int lastCellCount = -1;
        private int lastEventCount = -1;
        private int lastCellTick = -1;
        private string lastEventHash = "";
        private Vector2Int lastActorCell;
        private bool hasLastActorCell;

        public StarfallMapViewModel() { }

        public StarfallMapViewModel(string world, string generation, string actorId)
        {
            Bind(world, generation, actorId);
        }

        public void Invalidate()
        {
            InvalidateState("Invalidated");
        }

        private bool InvalidateState(string reason)
        {
            bool hadDisplayOrData = IsValid || exploredCells.Count > 0 || rememberedBeliefs.Count > 0 ||
                                    observationEvents.Count > 0 || !string.IsNullOrEmpty(CachedAsciiGrid) ||
                                    !string.IsNullOrEmpty(CachedStatusText);

            ClearData();
            IsValid = false;
            StatusMessage = reason ?? "Invalidated";

            if (hadDisplayOrData)
            {
                Revision++;
                MemoryChanged = true;
                DisplayChanged = true;
            }
            return false;
        }

        public bool Bind(string world, string generation, string actorId)
        {
            if (!FoodModel.Id(world) || !FoodModel.Id(generation) || !FoodModel.Id(actorId))
            {
                IsBound = false;
                InvalidateState("Invalid binding scope arguments");
                return false;
            }
            bool hadData = IsValid || exploredCells.Count > 0 || !string.IsNullOrEmpty(CachedAsciiGrid);
            ExpectedWorld = world;
            ExpectedGeneration = generation;
            ExpectedActorId = actorId;
            IsBound = true;
            IsValid = false;
            StatusMessage = "Bound; awaiting valid snapshot";
            ClearData();
            if (hadData)
            {
                Revision++;
                MemoryChanged = true;
                DisplayChanged = true;
            }
            return true;
        }

        public void Rebind(string world, string generation, string actorId)
        {
            Bind(world, generation, actorId);
        }

        public bool Update(FoodState state, Vector3 actorPosition, bool forceRevalidate = false)
        {
            MemoryChanged = false;
            DisplayChanged = false;

            if (!IsBound)
            {
                return InvalidateState("View model unbound");
            }

            if (state == null)
            {
                return InvalidateState("Null food state");
            }

            // Explicit world/generation/actor binding fails closed on mismatch
            if (state.world != ExpectedWorld || state.generation != ExpectedGeneration || state.actorId != ExpectedActorId)
            {
                return InvalidateState($"Scope mismatch: expected ({ExpectedWorld}/{ExpectedGeneration}/{ExpectedActorId}) but received ({state.world}/{state.generation}/{state.actorId})");
            }

            // Evaluate actor position safely
            bool posFinite = Finite(actorPosition.x) && Finite(actorPosition.y) && Finite(actorPosition.z) && actorPosition.magnitude < 10000f;
            Vector2Int newCell = posFinite ? new Vector2Int(Mathf.RoundToInt(actorPosition.x / 3f), Mathf.RoundToInt(actorPosition.z / 3f)) : Vector2Int.zero;
            bool actorCellMoved = !hasLastActorCell || hasLastActorCell != posFinite || (posFinite && newCell != lastActorCell);

            HasValidActorPosition = posFinite;
            ActorPosition = posFinite ? actorPosition : Vector3.zero;
            ActorCell = newCell;
            lastActorCell = newCell;
            hasLastActorCell = posFinite;

            // Revision tracking: detect whether place memory data has changed
            int cellCount = state.exploredCells != null ? state.exploredCells.Count : 0;
            int eventCount = state.observedPlaces != null ? state.observedPlaces.Count : 0;
            int cellTick = cellCount > 0 && state.exploredCells[cellCount - 1] != null ? state.exploredCells[cellCount - 1].lastFoodTick : -1;
            string eventHash = eventCount > 0 && state.observedPlaces[eventCount - 1] != null ? state.observedPlaces[eventCount - 1].hash : "";

            bool memoryNeedsRevalidation = forceRevalidate || !IsValid ||
                state != lastStateRef ||
                state.world != lastValidatedWorld ||
                state.generation != lastValidatedGeneration ||
                state.actorId != lastValidatedActorId ||
                cellCount != lastCellCount ||
                eventCount != lastEventCount ||
                cellTick != lastCellTick ||
                eventHash != lastEventHash;

            if (memoryNeedsRevalidation)
            {
                // Strict authoritative validation on incoming/revised state
                if (!FoodModel.Valid(state, ExpectedWorld, ExpectedGeneration))
                {
                    return InvalidateState("Authoritative state failed validation");
                }

                // Ingest state into defensive readonly struct copies
                exploredCells.Clear();
                cellLookup.Clear();
                int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
                if (state.exploredCells != null)
                {
                    for (int i = 0; i < state.exploredCells.Count; i++)
                    {
                        var cell = state.exploredCells[i];
                        if (cell == null) continue;
                        var entry = new ExploredCellEntry(cell.x, cell.z, cell.firstFoodTick, cell.lastFoodTick, cell.visits);
                        exploredCells.Add(entry);
                        cellLookup[cell.x + ":" + cell.z] = entry;
                        if (cell.x < minX) minX = cell.x;
                        if (cell.x > maxX) maxX = cell.x;
                        if (cell.z < minZ) minZ = cell.z;
                        if (cell.z > maxZ) maxZ = cell.z;
                    }
                }

                if (exploredCells.Count > 0)
                {
                    MinCellX = minX; MaxCellX = maxX; MinCellZ = minZ; MaxCellZ = maxZ;
                }
                else
                {
                    MinCellX = MaxCellX = MinCellZ = MaxCellZ = 0;
                }

                observationEvents.Clear();
                rememberedBeliefs.Clear();
                beliefLookup.Clear();

                if (state.observedPlaces != null)
                {
                    for (int i = 0; i < state.observedPlaces.Count; i++)
                    {
                        var ev = state.observedPlaces[i];
                        if (ev == null) continue;
                        var entry = new ObservationEventEntry(
                            ev.sequence, ev.kind, ev.id, ev.observedType,
                            ev.position, ev.foodTick, ev.brainTick,
                            ev.available, ev.permitted, ev.hash);
                        observationEvents.Add(entry);
                    }

                    // Build latest remembered beliefs
                    var latestPerId = new Dictionary<string, PlaceObservationEvent>(StringComparer.Ordinal);
                    for (int i = 0; i < state.observedPlaces.Count; i++)
                    {
                        var ev = state.observedPlaces[i];
                        if (ev == null || string.IsNullOrEmpty(ev.id)) continue;
                        latestPerId[ev.id] = ev;
                    }

                    foreach (var kvp in latestPerId)
                    {
                        var ev = kvp.Value;
                        var belief = new RememberedPlaceBelief(
                            ev.id, ev.objectKind, ev.observedType, ev.position,
                            ev.foodTick, ev.brainTick, ev.kind, ev.available,
                            ev.permitted, ev.hash);
                        rememberedBeliefs.Add(belief);
                        beliefLookup[ev.id] = belief;
                    }
                }

                lastStateRef = state;
                lastValidatedWorld = state.world;
                lastValidatedGeneration = state.generation;
                lastValidatedActorId = state.actorId;
                lastCellCount = cellCount;
                lastEventCount = eventCount;
                lastCellTick = cellTick;
                lastEventHash = eventHash;

                Revision++;
                MemoryChanged = true;
                DisplayChanged = true;
                IsValid = true;
                StatusMessage = $"Synchronized ({exploredCells.Count} cells, {rememberedBeliefs.Count} places, {observationEvents.Count} events)";

                RebuildCachedBeliefsText();
                RebuildCachedEventsText();
                RebuildCachedAsciiGridAndStatus();
                return true;
            }

            // No memory change: check if actor cell moved to update display
            if (actorCellMoved)
            {
                DisplayChanged = true;
                RebuildCachedAsciiGridAndStatus();
            }

            return true;
        }

        private void RebuildCachedBeliefsText()
        {
            if (rememberedBeliefs.Count == 0)
            {
                CachedBeliefsText = "REMEMBERED PLACES (LAST SEEN):\nNone observed yet";
                return;
            }

            var sb = new StringBuilder("REMEMBERED PLACES (LAST SEEN):\n");
            for (int i = 0; i < rememberedBeliefs.Count && i < 3; i++)
            {
                var b = rememberedBeliefs[i];
                string avail = b.WasAvailableWhenSeen ? "available" : "depleted/empty";
                sb.AppendLine($"• {b.Id} ({b.ObservedType}): t{b.LastSeenFoodTick} [{avail}]");
            }
            CachedBeliefsText = sb.ToString().TrimEnd();
        }

        private void RebuildCachedEventsText()
        {
            if (observationEvents.Count == 0)
            {
                CachedEventsText = "RECENT OBSERVATION EVENTS:\nNo events recorded";
                return;
            }

            var sb = new StringBuilder("RECENT OBSERVATION EVENTS:\n");
            int start = Math.Max(0, observationEvents.Count - 3);
            for (int i = observationEvents.Count - 1; i >= start; i--)
            {
                sb.AppendLine(observationEvents[i].Summary);
            }
            CachedEventsText = sb.ToString().TrimEnd();
        }

        private void RebuildCachedAsciiGridAndStatus()
        {
            CachedStatusText = $"Inhabitant Cell: ({ActorCell.x}, {ActorCell.y}) · Known: {ExploredCellCount} cells · {ObservedPlaceCount} places";
            CachedAsciiGrid = GenerateAsciiGrid(3);
        }

        private void ClearData()
        {
            exploredCells.Clear();
            cellLookup.Clear();
            rememberedBeliefs.Clear();
            beliefLookup.Clear();
            observationEvents.Clear();
            MinCellX = MaxCellX = MinCellZ = MaxCellZ = 0;
            HasValidActorPosition = false;
            ActorPosition = Vector3.zero;
            ActorCell = Vector2Int.zero;
            lastStateRef = null;
            lastValidatedWorld = null;
            lastValidatedGeneration = null;
            lastValidatedActorId = null;
            lastCellCount = -1;
            lastEventCount = -1;
            lastCellTick = -1;
            lastEventHash = "";
            hasLastActorCell = false;
            CachedAsciiGrid = "";
            CachedStatusText = "";
            CachedBeliefsText = "";
            CachedEventsText = "";
        }

        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        // Read-only queries and defensive copies
        public bool IsCellExplored(int x, int z) => cellLookup.ContainsKey(x + ":" + z);

        public bool TryGetCell(int x, int z, out ExploredCellEntry entry) =>
            cellLookup.TryGetValue(x + ":" + z, out entry);

        public RememberedPlaceBelief? GetRememberedPlace(string id)
        {
            if (id != null && beliefLookup.TryGetValue(id, out var belief))
                return belief;
            return null;
        }

        public ExploredCellEntry[] GetExploredCells() => exploredCells.ToArray();

        public RememberedPlaceBelief[] GetRememberedBeliefs() => rememberedBeliefs.ToArray();

        public ObservationEventEntry[] GetObservationEvents() => observationEvents.ToArray();

        // Local ASCII grid centered on actor cell or explored bounds center.
        // radius defines window half-size: (2*radius + 1) x (2*radius + 1) cells.
        public string GenerateAsciiGrid(int radius = 4)
        {
            if (!IsValid) return "[Map unavailable: " + StatusMessage + "]";
            if (radius < 1 || radius > 12) radius = 4;

            int centerX = HasValidActorPosition ? ActorCell.x : (MinCellX + MaxCellX) / 2;
            int centerZ = HasValidActorPosition ? ActorCell.y : (MinCellZ + MaxCellZ) / 2;

            var sb = new StringBuilder();

            // Pre-index remembered beliefs by cell coordinates
            var beliefsByCell = new Dictionary<string, RememberedPlaceBelief>(StringComparer.Ordinal);
            for (int i = 0; i < rememberedBeliefs.Count; i++)
            {
                var b = rememberedBeliefs[i];
                int bx = Mathf.RoundToInt(b.Position.x / 3f);
                int bz = Mathf.RoundToInt(b.Position.z / 3f);
                beliefsByCell[bx + ":" + bz] = b;
            }

            // North (high Z) to South (low Z)
            for (int z = centerZ + radius; z >= centerZ - radius; z--)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    bool isActor = HasValidActorPosition && x == ActorCell.x && z == ActorCell.y;
                    bool isExplored = IsCellExplored(x, z);
                    bool hasBelief = beliefsByCell.TryGetValue(x + ":" + z, out var belief);

                    if (isActor)
                    {
                        sb.Append('@');
                    }
                    else if (hasBelief)
                    {
                        if (belief.Id.StartsWith("berry", StringComparison.Ordinal) ||
                            belief.ObservedType.Contains("succulent") || belief.ObservedType.Contains("fruit"))
                            sb.Append('B');
                        else if (belief.Id.StartsWith("spring", StringComparison.Ordinal) ||
                            belief.ObservedType.Contains("seep") || belief.ObservedType.Contains("water"))
                            sb.Append('S');
                        else if (belief.Id.StartsWith("refuge", StringComparison.Ordinal) ||
                            belief.Id.Contains("refuge"))
                            sb.Append('R');
                        else
                            sb.Append('P');
                    }
                    else if (isExplored)
                    {
                        sb.Append('·');
                    }
                    else
                    {
                        sb.Append(' ');
                    }
                    sb.Append(' ');
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
