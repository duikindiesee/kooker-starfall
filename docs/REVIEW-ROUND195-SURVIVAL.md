# Round 195 independent survival review

Reviewed 15 September 2026. This is a bounded compiled-player review, not release acceptance.

Build `KookerStarfallIntegrated-0.0.11-survival.1-20260915-083015`, source
`0c041280a6930cc37430dc4dda00a4aff3a91e27`; complete build content SHA-256
`62f6f4252092409b0d84049c5518968afb920757b550bf30a0fde6f03cc6c721`.

| Claim | Status | Evidence | Remaining gap |
| --- | --- | --- | --- |
| Model-selected meal changes physiology | Passed in diagnostic run | Local run-09 raw survival trace: `eat fruit`, complete `stop` response in 3775 ms, request/response hashes, energy +1200, hydration +400, inventory -1 | Ordinary sustained food and water behavior |
| Death returns to preserved refuge | Accelerated diagnostic passed | Run-09 death diagnostic records both causes, scoped reload, preserved world/actor and incarnation increments | Natural timeline pacing is not proven |
| Return no longer visibly hovers over bed | Bounded visual improvement | Independently viewed run-09 `02-safe-return-after-dehydration.png` and `04-safe-return-after-starvation.png`; feet beside bed near floor, measured gap about 0.0296 m | Motion, camera-orbit and animation polish |
| Return HUD accurately describes provenance | Failed | Both screenshots say `Model chose: safe return`; return trace has no model or request/response hashes | Label deterministic recovery as system-controlled, not a model choice |
| Club is accepted | Unverified by this review | Club visible in static return views | Static captures do not establish correct grip through motion |

Run-10 was separately observed as a responsive ordinary player, with v2 process
receipt, survival enabled and death/smoke flags disabled. Its startup is not a
completed soak and does not establish drinking. Require terminal raw outcome
evidence and matching post-exit build hash before retention promotion.

## Visual scope still open

The user's latest canyon image makes the gas giant read as a nearby hovering
object. Existing distant-position source is insufficient proof of composition.
Check the exact current player from canyon mouth, river bank and higher ground:
huge angular scale, terrain occlusion, no nearby-object parallax or apparent
contact with cliffs/water. Establish whether the supplied image predates the
current sky adjustment before changing geometry. Water, terrain and vegetation
remain subject to the reference-led visual review; no 7.3/10 acceptance is
awarded here.

Raw local evidence remains local; this review records the observations without
publishing player saves, machine-specific profiles or private runtime logs.
