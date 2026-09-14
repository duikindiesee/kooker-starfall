# Blue delivery regression: bounded compiled-player repair

Independent coordinator inspection compared runtime-20 with runtime-21.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Failure reproduced | Confirmed | runtime-20/runtime/integrated-runtime.json: 2 deliveries, blue held, 18 movement failures, 6 events | Preserve failed run |
| Unintended plant colliders removed | Confirmed in scene and player | Generated scene 192240 had 12 Sourfig SphereColliders; scene 193301 had zero; runtime-21 forage-decoration check reports zero | Other future decorations require same guard |
| Complete delivery route | Passed this compiled run | runtime-21/runtime/integrated-runtime.json: 3 deliveries, 3 occupied destinations, held none, zero failures, 7 events | Repeat against final release candidate |

Evidence paths are relative to `evidence/local/combined/` unless a generated
scene timestamp is specified. Generated scenes reside under
`Assets/CityLife/GeneratedPreview-20260914-<timestamp>/CosmicPreview.unity`.

The inspected launch receipt identifies source
`1760b7922a7f27b321cfae25c512dae2c97f455c`, build
`KookerStarfallIntegrated-0.0.10-canyon.1-20260914-193301`, executable SHA256
`a4c71b1bfc3e42d02f2f129fe04adef3ebf2343a662884e4b99598e9dfc90e7d`.

Editor-time deferred collider destruction was replaced with immediate removal;
runtime construction disables the collider before deferred destruction.
Navigation checks and delivery thresholds were not relaxed. The runtime report
explicitly remains `PASS_AUTOMATED_NATIVE_AND_COVERAGE_REVIEW_PENDING`.
This repair does not accept visual quality, club grip, human controls, packaging
or release. Model provenance and restart persistence have separate receipts.

## Separate model and persistence scope

Independently inspected `runtime-21/runtime/living-memory.json`: the provider
returned `Delivered amber` with finish reason `stop`, one attempt, 433 ms total,
and live admission tied to the real amber delivery at tick 542. This is a bounded
two-word event acknowledgement, not evidence of general reasoning or dialogue.

`runtime-21/restart-persistence.json` retains three SQLite events: registration,
amber pickup and amber delivery. It does not prove that all seven full-cycle
events survived restart. Keep the three-delivery decision report and persisted
amber journey claims separate until a full-cycle storage audit proves more.
