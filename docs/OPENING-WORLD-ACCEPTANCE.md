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

Run `python tools/check-visual-evidence.py <runtime-directory> --require 02-complete-autonomy-cycle.png --require 04-paused-options.png --require 07-refuge-entry.png` before using screenshots in a receipt. This detects missing or blank captures, not correct content. Inspect every claimed view manually and retain failed evidence instead of overwriting it.

Future hunting, construction, complete ecology, planetary geometry and infrastructure expansion are outside this milestone. They must not displace these required opening-world gates.
