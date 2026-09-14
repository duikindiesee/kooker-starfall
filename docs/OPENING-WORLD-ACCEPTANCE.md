# Opening-world acceptance contract

This is the completion checklist for the active opening-world goal, not a release certificate. Each final receipt must identify the exact executable, source commit, test run and inspectable evidence. An older or isolated component pass cannot certify the new combined player.

| Requirement | Required final evidence | Current acceptance boundary |
|---|---|---|
| Spacious canyon, turquoise river/pools to sea, convincing cliffs | Actual player eye-level traversal and panorama; user visual approval | Expanded runtime vista exists; not final visual approval |
| Kookerboom, giant, moons, distant islands and natural boundaries | Identifiable player views and traversal/boundary tests | Giant/moons visible in runtime-08; all composition requirements still need final review |
| Terrain-fitted cave refuge | Continuous walk from playable bank through entrance, collision and shelter/weather measurements | A placed registry entry or teleported interior screenshot is insufficient |
| Recognizable grounded berries and freshwater | Runtime geometry/clearance measurement, visible placement, actual gather/eat/drink state changes | Do not substitute a baked clearance value for a live measurement |
| Weather and physics | Same-player weather transitions, gravity/collisions, refuge exposure, sustained-run performance | Isolated physics tests remain supporting evidence only |
| Camera and controls | Actual camera motion; pause, possession/release, spectator, fullscreen/windowed with readable UI | Internal camera coordinates alone are insufficient; black screenshots fail evidence sanity |
| Three deliveries without stall | Uninterrupted three-object route, persisted ordered decisions and all delivery receipts | Runtime-08 and -09 record three; repeat against release candidate |
| Scoped memory and reload | Real journey events persisted and recovered after restart; foreign world/inhabitant rejected | Memory service tests alone are insufficient |
| Genuine local thought and safe fallback | Same compiled candidate admits a grounded model answer tied to event; timeout/unavailable fallback tested | Endpoint or Editor-only response is insufficient |
| Clothing and club | Full motion/camera coverage; corrected grip and error-free required validation | Club remains held until validation repaired or user explicitly defers |
| Integration governance | Dependency/source manifest, requested review fixes validated, protected review receipts | No inference of approval from old successful CI; no unauthorized merge |
| Versioned executable and ZIP | Exact build path, source identity, SHA256, archive/private-file checks and launch test | Preserve prior build folders |
| Architecture/install/status | Reproducible manual service steps, endpoint and memory ownership, honest limitations | Do not claim unified game saving from service-only reload |
| Narrated before/after MP4 | Actual footage, evidence-backed narration, replay with audio and intact ending | See OPENING-WORLD-WALKTHROUGH.md; plan alone is not delivery |
| Human acceptance | User tests identified final build and explicitly accepts visual/play result | Pending; cannot be supplied by automation |

## Evidence sanity

## Normal-play memory boundary

The opt-in normal-play adapter is implemented in `0b56f70` and attached by
`ede6add`. These commits are not runtime acceptance. Final review must launch
without smoke flags, forced resets, test inputs or automatic quitting and prove:

- A real autonomous delivery is durably recorded, recalled and visibly linked
  to the optional model thought in the normal HUD.
- A subsequent launch recalls the same scoped history; retaining database bytes
  without demonstrating recall is insufficient.
- A completed model response is rejected while paused, possessed or no longer
  autonomous, including completion on the same frame as the control transition.
- The service database, outbox and capability configuration all have appropriate
  private storage protection. No credentials enter CLI arguments or evidence.

Independent review raised final-state admission and normal-reload recall gaps;
these remain pending correction and compiled-player proof.

### Normal-play evidence observed after hardening

The above source gaps were addressed by `a76f9d2` and subsequent integration.
Root independently inspected these actual-player artifacts on 2026-09-14:

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Normal delivery produces visible model response | Observed in candidate ending `165313` | `evidence/local/normal-memory/run-03/normal-living-memory.json` and matching PNG: normalPlay, persisted and admitted true; Delivered Amber; 1041 ms | Retain launcher/raw response provenance and repeat on final release |
| Subsequent normal launch recalls prior journey | Visible scoped recall observed | `evidence/local/normal-memory/run-04/normal-prior-journey.json` and PNG: prior hidden-green delivery at tick 1082 displayed at new tick 15 | Independent foreign-scope/reload receipt audit; not full world save/load |
| HUD clearly distinguishes planner and memory reflection | Not yet accepted | Both screenshots still show Local thoughts off while memory is displayed | Clarify labels and retest final UI |

These observations replace the earlier assertion that no normal-play evidence
exists, but do not certify all release gates or the final visual requirement.

Independent read-only audit found normal run-03/04 **visually persuasive, not
audit-complete**. Their reports omit independently retained launch flags,
executable hash, raw model response/finish reason, event-ID and restart checkpoint
correlation, and foreign-world/actor rejection receipts. The recorded build also
predates the one-thought-per-session/fallback-evidence refinement. The next normal
run must retain these sanitized records with a hash catalogue, demonstrate reuse
of the same private store, and exercise unavailable-model fallback. Never publish
the private database or capability configuration to supply this evidence.

Run `python tools/check-visual-evidence.py <runtime-directory> --require 02-complete-autonomy-cycle.png --require 04-paused-options.png --require 07-refuge-entry.png` before using screenshots in a receipt. This detects missing or blank captures, not correct content. Inspect every claimed view manually and retain failed evidence instead of overwriting it.

Future hunting, construction, complete ecology, planetary geometry and infrastructure expansion are outside this milestone. They must not displace these required opening-world gates.

### Post-reboot normal-play audit — 14 September

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Ordinary play retains a scoped event ledger | Collector evidence inspected | `evidence/local/normal-memory/run-07-process/ledger-audit.json`: seven chained events; foreign-world and foreign-actor queries rejected with 400, unknown capability with 401 | Complete same-store restart prefix proof and repeat on final build |
| Unavailable provider does not fabricate a thought | Safe fallback recorded | `run-07-runtime/normal-living-memory.json`: one delivery persisted, admitted false, empty thought | Model success is a separate gate |
| Loaded smaller model meets gameplay deadline | Failed in run 09 | `run-09-runtime/normal-living-memory.json`: SAFE_FALLBACK at 1507 ms, persisted true, admitted false | Diagnose exact-request latency; do not count a direct endpoint answer as in-game success |

Earlier successful thought captures remain historical evidence, not proof that
the post-reboot configuration meets the same deadline. Preserve both outcomes.

### Build-warning audit — round 130

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Candidate compiles | Build report succeeded with 18 warnings, zero errors | `evidence/milestones/coastal/round-130/preview-build.json`; source `d7f6ea359a70ecce1d2b534863330c873708eef0` | This is not runtime or visual acceptance |
| Deprecated Unity discovery calls remain | Confirmed compiler warnings | `evidence/local/integrated/build-20260914-181557.log`: CS0618 in EnvironmentPresentation, IntegratedFoodRuntime and CoastalSmoke | Review ordering semantics before replacing discovery overloads |
| Some public runtime fields are not Unity-serialized | Confirmed compiler warnings, not a demonstrated persistence defect | Same log: UAC1001 for MemoryExport, environment Clock/Exposure/LocalWeather/ShelterPolicy and refuge Local | Explicitly document runtime ownership; verify intended persistence through the scoped store rather than assuming scene serialization |

Do not add serialization attributes to runtime services merely to silence these
warnings. The event-memory ledger and a complete world save are different
contracts; this build report proves neither. Keep warnings visible in the final
manifest and rerun this audit against the exact release candidate.
