# Starfall save slots, world identity and recovery gate

Requirement revision: `starfall.save.requirements.v1`, recorded 13 September 2026. **Next architectural gate; not implemented and not actual-player accepted.** Proving one genuine, perception-grounded local-model thought remains the sole implementation priority. This document authorizes no parallel save implementation, migration of existing data, service deployment or player replacement.

The [system architecture](SYSTEM-ARCHITECTURE.md) describes the current components. Existing [terrain edit persistence](WORLD-FOUNDATION.md) and [memory service v1](../services/starfall-memory/contracts/API-v1.md) are separate tested boundaries. Neither establishes a complete game save. In particular, the current memory ledger does not contain every terrain, physics, inventory or brain transition, so it cannot reconstruct the whole Unity world.

## Identity and player operations

| Identifier | Required meaning |
|---|---|
| `slot_id` | Unique local save container, with a display label and immutable checkpoint generations. A label or directory name is never identity. |
| `world_id` | Unique world-instance identity allocated when a new game is created. Separate from a world template/schema name, generator fingerprint or seed. |
| `seed` and generator descriptor | New games generate a fresh seed and store its exact type/encoding, algorithm/version, settings, asset/content dependencies and base fingerprint. An explicit repeated seed may recreate terrain, but still creates a different world identity. |
| `inhabitant_id` | Stable, never recycled within a world, including across save/load, rename, death or retirement. The identity key is `(world_id, inhabitant_id)`; names, list indices and model sessions do not identify an inhabitant. |
| `object_id` | Stable world-scoped identity for items, terrain edit subjects, structures and other persistent entities. Removed objects retain tombstones where references require them. |
| `checkpoint_id`, `parent_checkpoint_id`, `continuation_id` | Immutable save generation, its ancestry and the history continuation to which events/outbox records belong. These distinguish resuming an older checkpoint from continuing a later history. |
| `session_id` | Fresh runtime/publisher session after launch. It does not replace world or inhabitant identity or reuse an old source sequence. |

Naming bridge for Unity-facing designs: `saveSlotId`, `worldId` and `inhabitantId` refer to the concepts named `slot_id`, `world_id` and `inhabitant_id` above. The eventual wire schemas must pin one spelling explicitly; readers must not accept ambiguous aliases or reinterpret existing schema v1 fields.

Event identity must include its schema and explicit world/inhabitant provenance. The current service uses a hash of canonical event bytes and unique `(source.session_id, source.sequence)` within its configured world/publisher. The future continuation contract must retain those immutable event IDs, hash-chain ancestry and idempotent receipt semantics, and explicitly bind new sessions to the selected checkpoint/continuation. Exact retries remain the same event; changed-content retries fail. Saving, migration, reset and wiki rebuild must not rewrite prior events to make a join pass. Corrections are separately typed appended records under a supported version, not edits to the original evidence.

Production IDs and initial seeds may use independent creation-time entropy. Deterministic simulation and fixture identities must not depend on wall-clock time, render frames or a global random stream. No existing fixed courtyard/template ID is silently promoted into a production save identity.

| Player operation | Required behavior |
|---|---|
| **New game / New world** | Allocate a fresh slot, world identity and generated seed; create new inhabitant identities and empty world-specific history. Start from a pinned definition. Preserve all other slots. |
| **New character in this world** | Keep the selected world, seed, terrain, objects, weather/time and shared history. Allocate a new inhabitant identity and its own private history/capability; do not copy another character's episodes, beliefs or dreams. Character creation is an explicit future lifecycle event. |
| **Continue / Load** | Select and validate one complete checkpoint and restore its exact world and inhabitant identities. Display which slot/checkpoint is being loaded. |
| **Reset / Start over** | Default to a fresh slot. If the user explicitly chooses to archive the old slot, retain it with its history and create the replacement separately. Reset must not silently delete saves or clear the memory database. |
| **Delete** | A separate explicit destructive operation that identifies the affected slot and retained backups. It is never an implicit part of reset, new character, import, recovery or migration. |

Only one writable session may own a local world continuation. Loading an earlier checkpoint must retain later checkpoints and create a separately recorded continuation with its own storage view, event session and capability binding. Its historical ledger prefix is preserved byte-for-byte; events after the chosen checkpoint must not appear in the resumed world. No history merge or event rewrite is implicit. World-scoped inhabitant identity remains stable; continuation selects which history is visible.

Archiving marks a complete slot/history set inactive and preserves its manifests, identities and checkpoint ancestry. Active-world queries must not scan archived worlds or backups. An archive can return only through explicit restore and the same full validation as load. It grants no memory access to a newly created world or character. Explicit deletion must identify whether archives/backups are included; deleting one selected container does not silently remove other copies.

## Complete checkpoint boundary

The proposed manifest is `starfall.save.slot.v1`; this is a design identifier, not an accepted runtime format or a new version of the existing edit or memory schemas. Its required fields include slot/world/checkpoint/continuation identities, parent reference, exact seed and base descriptor, simulation tick, content/build compatibility, component schema versions, file byte lengths and cryptographic digests, and a consistent memory/outbox boundary. Human timestamps are descriptive metadata, never simulation inputs.

Each component declares its status as present or explicitly not used by this world definition. Missing required components are an error. Optional inference availability does not make persisted memory optional: a world that has private history must not load with that history quietly replaced by empty records.

| Component | State bound to the same checkpoint |
|---|---|
| World definition and terrain | Immutable generator/configuration/seed and base fingerprint, coordinate frame and units, edit schema/revision and full terrain delta set. Changed algorithms must not regenerate a saved base silently. |
| Persistent objects | Stable IDs, creation/removal/tombstones, placement, transforms, structural/interaction state, ownership and permissions. Include living sea entities or ecological state when those systems become authoritative; visual-only particles may regenerate. |
| Inventory and physical state | Exact item IDs/counts, holder/container relationships, capacity/occupied destinations, actor transform, velocity, posture, motion/swimming state and any implemented physical/vital state. Persist meaningful simulation state, not raw engine pointers or an assumed physics-engine cache. |
| Weather and time | Simulation tick/time, day/celestial phase, weather state and transition progress, temperature/wind/water state where authoritative, plus independent versioned random-stream states and scheduler deadlines. Offline wall-clock catch-up is off unless an explicit world policy defines it. |
| NPC brains | Identity/personality version, goals, plan progress, action phase, cooldowns, sleep state, deterministic planner state and admitted thought text/provenance. Preserve proposal timeout/circuit-breaker policy and resource limits. |
| Private history | Each world's inhabitant registrations, episodes, beliefs and dreams, evidence references, private-record boundaries and processor versions. Include immutable records that were appended independently of Unity events. |
| Shared history and wiki | Confirmed past-event ledger, ledger count/head hash and derived-record watermark/digest. Wiki caches include derivation version and source boundary; they do not become authority. |
| Delivery boundary | Durable publisher outbox, acknowledged source sequences, deduplication/receipt IDs and the ledger prefix corresponding to the Unity checkpoint. No acknowledged mutation may be replayed twice or lost between components. |

Perception is sampled again from restored Unity state before an action resumes. Rebuild navigation/collision caches against the validated world. Cancel in-flight inference and discard late replies on save/load; never serialize sockets, tasks, transport handles or a provider's private reasoning. Previously admitted text may be restored as historical text, with provenance; it is not a newly generated thought. Revalidate pending goals and actions against current reach, sight, permissions, capacity and ownership. Saving must reach a defined action boundary or explicitly serialize its transaction state; it must never capture half of a pickup/delivery.

## World and private-history isolation

One writable slot continuation binds one world state and one isolated memory storage view. All API capabilities, publisher sessions, caches, record queries, outbox entries and object/evidence references must agree on that binding. A world name, identical seed, matching inhabitant name or matching local inhabitant ID cannot authorize a join. A new character gets only its own capability, while the trusted coordinator retains publisher authority.

Validate the full cross-component join: identities, generator/base fingerprint, versions, checkpoint tick, entity references, inventory uniqueness, ledger prefix, private-record boundary, sleep state and delivery acknowledgements. Reject wrong-world/continuation records even if their individual schemas are valid. Reject incompatible or missing required parts before replacing the active session. An unavailable memory service must produce an explicit unavailable/load error or a fully validated local restore path, never an automatic connection to another database.

Secrets and operational capabilities live outside portable saves. They are reissued and rebound after validation. Exporting a full slot includes its fictional inhabitants' private histories, so the export action must make its contents clear. No personal Reflection archives, credentials or unrelated data are included. The current service's per-inhabitant capability checks remain the minimum boundary; continuation/checkpoint support requires an explicit future contract change.

## Atomic checkpoint and load protocol

The future coordinator owns a bounded protocol across Unity and memory; separate successful file writes are insufficient. There is no assumption of an atomic transaction spanning Unity files and SQLite.

1. Acquire the slot's writer lease. At a deterministic safe tick, quiesce world mutations and new publisher work. Establish a barrier covering private belief/dream writes as well as event ingestion. Capture a complete state only after action transactions reach their documented boundary.
2. Drain the durable outbox through that tick using idempotent receipts. Require acknowledgement of the matching ledger prefix and private-record boundary. If the deadline or storage operation fails, fail the save visibly and keep the last committed checkpoint. Do not advance the visible save timestamp or claim success.
3. Freeze an immutable Unity snapshot and a transaction-consistent memory snapshot under the barrier. Use the database's supported consistent backup/snapshot mechanism; copying an open SQLite file without its transactional state is not sufficient. Release gameplay only once later mutations cannot change those captured inputs.
4. Write a unique temporary generation on the same destination volume, including components, versions, hashes, byte lengths, parent relation and boundary metadata. Flush files according to the supported filesystem's durability contract. Verify all files and joins before publication.
5. Publish the immutable generation, then atomically replace the small committed-checkpoint pointer using the platform's supported mechanism. Retain the previous pointer/generation as recovery evidence. A crash may leave an orphan temporary generation, but must leave the reader with the previous or the new complete checkpoint, never a mixture. Prove behavior on the actual target filesystem; cross-volume moves are not the commit primitive.
6. Only acknowledge success after the committed manifest and referenced data can be read back and validated. Record exact build/schema/checkpoint evidence. A lock or temporary file left by a crash is recoverable metadata, not permission to delete a slot blindly.

Loading stages all required components into an inactive session/storage view, validates the complete graph, binds fresh capabilities and publisher session, and only then swaps the active world. Until that succeeds, the current session and selected checkpoint remain intact. Start simulation and optional inference after restoration and fresh live validation. The game clock resumes at the saved tick under its recorded offline policy.

A full-slot save may require the memory component to be reachable for this coordinated barrier. Bounded deterministic play during a service outage is separate from claiming a complete save; any future deferred-save mode requires its own reconciled outbox and acceptance contract.

## Migration, portability and recovery

| Operation | Required contract |
|---|---|
| Version validation | Explicit compatibility matrix for manifest, generator/content, edits, Unity state, brain, event/record/database and derivation versions. Unknown required versions, duplicate keys, invalid values and incompatible joins fail before mutation. No silent field dropping or partial load. |
| Migration | Explicit named source-to-target migration with prerequisites and dry-run validation. Write a new generation/storage copy, retain the original and a migration report, verify identities and referential integrity, and provide rollback. Preserve original event bytes/hashes; any representation mapping or correction must be explicit and traceable. Changing the terrain generator requires a supported migration or a new world, never covert regeneration. |
| Export | Export only a complete committed checkpoint as a versioned manifest plus required state/history and dependency references. Include file digests and versions, exclude credentials and caches that can safely rebuild, and refuse overwrite of an existing export by default. A raw live database copy is not an export. |
| Import | Validate paths, file types/sizes, hashes, schemas, dependencies and all joins in staging; do not execute archive content or allow path traversal. Allocate a fresh local slot while preserving the imported world/inhabitant identities for a restore. Detect an existing local writable copy and require explicit restore/archive selection; do not merge it. A different world requires an explicit supported fork/mapping operation or refusal. No global cross-device writer exclusivity or disconnected-copy merging is claimed. |
| Backup | Retain multiple versioned complete checkpoints and an explicit user-controlled backup location/retention policy. Record backup manifest/hash and restore-check result. A second file on the same failing disk is not the only recovery plan. No automatic upload or deletion of old slots is implied. |
| Crash/corruption recovery | Check manifest, component integrity, ledger chain and join invariants. Quarantine suspect generations without rewriting them. Offer the newest fully valid retained checkpoint and show any lost progress before recovery. Never pair an older Unity snapshot with a newer memory database or truncate history silently. If no complete checkpoint validates, refuse load and retain all evidence. |
| Wiki rebuild | Rebuild shared confirmed-fact pages deterministically from the immutable ledger prefix using a pinned processor. Rebuild private wiki sections from that inhabitant's preserved immutable belief/dream records and cited events; those private records cannot be recreated by inventing text from action events. Store a new derived generation with provenance; preserve previous generations. Rebuilding a wiki never changes Unity truth or promotes a belief/theory to a confirmed fact. |

## Required fixtures and actual-player acceptance

These are future acceptance criteria, not tests claimed to have run. Use synthetic data in retained repository evidence; real saves and private profiles remain outside the public repository.

| Gate | Required observation |
|---|---|
| Deterministic fixture | Fixed IDs, seeds, versions and inputs regenerate the same base and canonical save state across supported runs. Separate two worlds with the same seed and local inhabitant labels; create at least two inhabitants per world. Record exact comparison rules and numeric physics tolerances. Rendering order/FPS must not drive state. |
| Complete round trip | Mutate terrain and objects, transfer uniquely identified inventory, move/swim, advance weather/time, progress an NPC action and sleep, append episodes/private beliefs/dreams/shared facts, then compare the restored authoritative state and history against the committed checkpoint. Every implemented required component must participate. |
| Isolation and lifecycle | Alternate worlds and inhabitants; prove no private or shared cross-world leakage, no inherited private history on new character, no changed world on new character, and a new world/seed/slot on default new game. Prove reset preserves the old slot and continuation rollback hides later history. |
| Failed joins | Substitute a valid component from another world, continuation, tick or ledger boundary; remove a required part; introduce incompatible schema/content, duplicate ownership or missing evidence references. All loads fail without changing the current world or files. |
| Failure injection | Interrupt each checkpoint publication step and exercise process death, disk full, permission failure, cancelled inference, service outage and duplicate outbox retry. Restart selects a complete old/new checkpoint, never a mixed or falsely acknowledged save. |
| Migration and recovery | Migrate supported old fixtures into a new generation, preserve originals, roll back, export/import into an empty installation, detect duplicate imports, restore a backup, recover from damaged newest data, and fail safely when every retained generation is corrupt. |
| Derivation | Remove only rebuildable wiki caches and rebuild from the pinned history boundary; compare confirmed facts and private section scope/provenance without modifying ledger bytes or promoting theories. |
| **Actual playable flow** | In the real versioned player UI: create a new world and character; make observable world, inventory and memory changes; save and see an accurate completion indicator; quit the process; relaunch; select the slot; load; inspect restored state and private/shared history; perform the next action successfully with Unity validation. Repeat for a second world and a new character, and exercise an actual failed-load/recovery flow. |

Retain executable/source hashes, manifest and component hashes, sanitized readbacks, process exit/relaunch observations, screenshots or video showing the save/load UI and restored state, and exact expected/observed comparisons. Separate source/unit/fixture results from this actual-player result. A save file, screenshot alone, HTTP success, editor fixture or process launch does not complete the gate.

## Related component contracts

| Component | Existing reference and required future connection |
|---|---|
| World foundation | [Definition, determinism and terrain edits](WORLD-FOUNDATION.md). Preserve the current base/edit boundary and add full-slot orchestration around it through explicit compatible contracts. |
| Private memory and wiki | [Memory guide](STARFALL-MEMORY.md) and [event/API v1](../services/starfall-memory/contracts/API-v1.md). Coordinate event and independent private-record boundaries; shared wiki remains scoped to the selected world. |
| NPCs and optional thoughts | [Hybrid NPC contract](HYBRID-NPC.md). Preserve deterministic action authority, validation, timeouts and offline fallback; reload historical accepted text without presenting it as a new live reply. |
| Food and ecology | [Living sea scope](STARFALL-LIVING-SEA.md) currently defines a planned bounded visual population, not a persistent food chain. No dedicated food/ecology runtime contract exists in this documentation checkpoint. When authoritative food stocks, consumption, hunger, growth, reproduction or animal lives are introduced, version and persist their identities, quantities, timers and random streams within the same world checkpoint; derivations must not replenish food or invent animals. |
| Weather and physical environment | [Foundation rendering/time scope](WORLD-FOUNDATION.md) and [physical sandbox direction](PHYSICAL-SANDBOX-NEXT.md) distinguish previews from future simulation. No dedicated authoritative weather save contract exists in this checkpoint. Future weather/current/temperature systems must bind their state and schedules to the saved simulation tick; loading must not use the local wall clock to silently advance them. |
| Testing and delivery | [Verification record](VERIFICATION.md), [CI and evidence boundaries](CI.md), and [the gates above](#required-fixtures-and-actual-player-acceptance). Add exact results only when the corresponding supported schema, fixture and actual-player flow exist. |

Integrations developed in other branches must cite their reviewed contract versions when connected. These links do not claim their runtime is installed or tested in this documentation branch.

## Current status

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Save/new-game requirements | Recorded, revision v1 | This contract and the linked architecture map | Implementation starts only after the living-thought priority; detailed schemas/migrations still need review. |
| Existing terrain and memory boundaries | Separately implemented/tested within their recorded scope | [World foundation](WORLD-FOUNDATION.md), [memory slice evidence](../evidence/milestones/starfall-memory/local-slice-v1/README.md) | A coordinated full-world checkpoint, production lifecycle identities and continuation binding. |
| Save/quit/relaunch/load in the actual player | Unimplemented and unverified | No actual-player save/load result is asserted by this document | All fixture and actual-player gates above. |

Future design only: [knowledge progression, persistent death/return and resource transformation](WORLD-KNOWLEDGE-PROGRESSION.md) defines provenance, private-memory boundaries, inventory recovery and save/reload acceptance. These mechanics are not implemented by the current integrated preview.
