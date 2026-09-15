# One current integrated build

User decision, 15 September 2026: integrated executables are ephemeral outputs,
not an accumulating archive. This supersedes earlier binary-preservation notes.
Keep one runtime-verified current player and its optional current ZIP. A temporary
candidate may coexist until validation; no source merge or release is implied.

- `build-integrated.ps1` removes an explicitly failed candidate through
  `remove-failed-integrated-build.ps1`. Missing or ambiguous failure receipts fail
  closed and require inspection; successful but visually unaccepted is not a build failure.
- After runtime and content verification, `retain-current-integrated-build.ps1`
  promotes the specified verified candidate for local retention and removes older
  manifest-bound outputs. Without `-Execute` it is read-only. Packaging calls it
  only after ZIP entry verification succeeds.
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
