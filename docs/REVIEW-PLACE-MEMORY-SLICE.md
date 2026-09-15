# Grounded place-memory slice — 15 September 2026

Source commit: `2de3ddbb917d17db3660d36938641a457d2dfc1a` on the isolated combined branch. This is a component checkpoint, not a playable-world or model-planning acceptance receipt.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Explored cells are grounded | Implemented and component-tested | `StarfallSurvivalAutonomy` records a three-metre cell only when the actor physically occupies a supported terrain/refuge-floor position; chosen or failed routes no longer pre-credit cells. | Actual-player travel and save/restart observation |
| Place history is scoped and revisable | Implemented and component-tested | `FoodState` stores world/generation/actor-bound, hash-chained first-seen/changed/revisit events; `LastSeen` derives the current belief while old events remain. Visual categories `fruiting-succulent` and `freshwater-seep` enter only through live LOS perception. | Compiled perception and model retrieval/decision proof |
| Old saves and new worlds remain isolated | Component-tested | Older `food.v1` payloads without lists restore with empty lists after validation; foreign world/inhabitant/generation and malformed locations are rejected; a fresh-world save leaves the old scoped save recoverable. | Unified full-world checkpoint and migration remain unimplemented |
| Bounded capacity is safe | Intermediate, not final | The maximum fixture (512 cells, 256 events) saved/reloaded as a 217,523-byte immutable snapshot. Exhaustion explicitly fails closed instead of silently dropping observations. | Timeline-preserving archival/compaction before sustained evolving-world acceptance |

The pinned Unity editor's checks-only method passed 114 deterministic tests at `evidence/local/editor-food-checks-20260915-170221/passed.txt` (SHA256 `f587d326b9b6105a85c3d6cef8775e2fbba73b278b557a0d91bb0ed2a14df94b`). The seven source files in the commit match that run's content; no integrated player was built or launched. Perception supplies same-tick, stable-ID-sorted observations every ten autonomy ticks; persisted food time is monotonic, while `Brain.Tick` is recorded as session-local metadata because it resets on process restart. A test reloads the saved ledger, observes again at a lower local brain tick and higher persisted food tick, and revises the belief without erasing history.

This slice deliberately does not expose undiscovered authored registry positions to the model. Need/day planning, Telegram for the same inhabitant, broader world save, actual-player memory use and any Hermes-versus-bridge choice are separate dependent gates.
