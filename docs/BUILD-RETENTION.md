# One current integrated build

User decision, 15 September 2026: integrated executables are ephemeral outputs,
not an accumulating archive. This supersedes earlier binary-preservation notes.
Keep one runtime-verified current player and its optional current ZIP. A temporary
candidate may coexist until validation; no source merge or release is implied.

## Storage reconciliation is not promotion

Explicit follow-up authorization: do not accumulate partial candidates while
waiting for freshwater, visual or other wider acceptance. Independently identify
the best runtime-verified fallback and the current testing candidate, bind both
to manifests and full-content hashes, and protect all active player paths.
Confirmed superseded binaries and redundant archives may then be removed without
declaring the candidate accepted. Never weaken `check-survival-promotion.ps1` or
feed synthetic passing evidence into promotion to enable cleanup.

Reconciliation must validate resolved direct build-child paths, reject reparse
points and user-state stores, preserve source/saves/compact evidence, and retain
an exact removal manifest with before/after inventory and drive free space.
Unknown paths remain untouched. A stopped failed test and a retained fallback
have different roles; recording these roles is mandatory. The existing promotion
helper's survival gate does not justify retaining every intermediate executable.

At 12:04 UTC on 15 September, `reconcile-integrated-builds.ps1` checked exact
manifests and full-content fingerprints, then removed 14 superseded direct
player folders (9,587,309,182 bytes). Only the round209 runtime-verified
fallback and round211 tested candidate remained. The deletion was permanent,
not Trash; source, manifests, runtime evidence and scoped stores remain for
rebuilding. The receipt is
`evidence/local/build-retention/reconcile-20260915-120450/reconciliation.json`.
No ZIP was removed because there was no matching redundant player ZIP.

New builds require an explicit verified fallback manifest and runtime receipt.
After a successful Unity build, `build-integrated.ps1` invokes separate
manifest/inventory-gated two-keep reconciliation: fallback plus an **unverified
temporary** new candidate, never a survival promotion. A failed build still
uses exact failed-output cleanup; a missing or ambiguous failure receipt leaves
the output for review. Both paths reject active players, reparse points,
user-state stores and unmanifested directories. The automatic success hook has
parsed and passed a two-keep dry run; first real new-build execution remains
to be tested. Keep this boundary distinct from release.

- `build-integrated.ps1` removes an explicitly failed candidate through
  `remove-failed-integrated-build.ps1`. Missing or ambiguous failure receipts fail
  closed and require inspection; successful but visually unaccepted is not a build failure.
- After runtime and content verification, `retain-current-integrated-build.ps1`
  promotes the specified verified candidate for local retention and removes older
  manifest-bound outputs. Without `-Execute` it is read-only. Packaging calls it
  only after ZIP entry verification succeeds.
- `run-living-memory.py --integrated` invokes retention after the standalone
  player, memory-isolation and restart checks, independently of packaging.
  An explicit runtime `FAIL` invokes failed-candidate cleanup after shutdown;
  any prior passing runtime receipt protects the current player. A crash without
  an attributable terminal receipt remains fail-closed for manual inspection.
  Newer timestamped candidates are not pruned by an older promotion request.
- Cleanup refuses active Unity operations, active target executables, linked
  output trees, out-of-scope paths and recognizable user-state stores. Unknown
  directories remain untouched and are reported for review.
- Keep source commits, failed/successful manifests, compact test logs, screenshots,
  package receipts and user saves. Before/after footage can use preserved evidence;
  removed binaries must not be described as still available.
- Local audit receipts live under `evidence/local/failed-build-cleanup` and
  `evidence/local/build-retention`. They record exact targets, byte totals and free
  space. Removal is permanent; reproducibility depends on retained source/assets/toolchain.

## Verified cleanup checkpoint

Retained: `KookerStarfallIntegrated-0.0.10-canyon.1-20260915-050306`, bound to
round-172 and runtime-33. Full content hash was checked before and after cleanup:
`fc7d08184f71273df2a9dc3bb334fb47d8cefe78d98ad53ecb6537f48ca78b24`.
One failed output (171,928,120 bytes), 51 superseded outputs and one old ZIP
(35,134,152,975 bytes combined) were removed. Free space after removal was
92,309,766,144 bytes. This is storage/runtime-identity evidence, not visual acceptance.

Task API / owning epic linkage remains pending authoritative discovery; the
local integration queue's Codex task IDs are not Task API identifiers.

Validation: the real failed output was removed; a successful build and a passing
runtime were both rejected as deletion authority; repeat retention reported zero
targets; the retained player's full content hash passed before and after cleanup.
The newly wired automatic lifecycle branches have syntax validation but still
require a fresh end-to-end candidate run; do not report that integration as tested.
# Survival promotion boundary (15 September follow-up)

Verifier regression: `python tools/test_survival_promotion.py` passed twelve
synthetic cases on 15 September: complete contract, missing drink, old process
schema, mismatched content, diagnostic disguised as ordinary play, short soak,
cross-actor events, unmatched food provenance, missing reload, wrong incarnation,
missing post-run death hash and evidence path escape. The fixture contains only
nonexecutable dummy bytes in a temporary directory, never invokes retention,
and is not gameplay or positive real-build promotion evidence.

A subsequent malformed-type review reproduced three false passes: string
`"false"` in normal/scoped-reload flags and a string duration. Strict JSON boolean
and finite-number checks now reject them; the expanded fifteen-case suite passes.

The legacy regression runner promoted round185 before survival was tested and
removed the previous 050306 and 060513 binary copies. Their source and evidence
remain, but those exact executable folders are no longer retained. This was a
gate mismatch, not survival acceptance.

Survival-version candidates now require `-SurvivalAcceptance` at retention and
packaging, or `--survival-acceptance` in the regression runner. Without it the
operation fails closed before deletion. Use `--no-retention` for regression-only
candidate testing. The separate `check-survival-promotion.ps1` checks a matching
full-content fingerprint, fresh v2 live process flags, at least five minutes of
ordinary play, model-provenance-linked meals and freshwater drinking, and the
same-build explicitly accelerated cause/return/scoped-reload diagnostic. Capture
both live processes with `capture-normal-process.ps1`; the death receipt path is
`acceleratedDeathDiagnostic.process` in the scoped survival receipt.

This is a technical retention boundary, not natural-timeline death, polished
animation, human visual acceptance or permission to release. Round190's older
process receipt is deliberately rejected. Round211's scoped receipt at
`evidence/local/normal-survival/run-25/scoped-today-receipt.json` now passes
the **technical** survival retention verifier: 357.904 seconds ordinary
process observation, model-linked fruit meals and freshwater drinking,
matching full-content post-exit hash, and an explicitly accelerated same-build
death/return diagnostic. This does not prove natural mortality timing, visual
acceptance, sustained high-quality policy or a releasable package.
