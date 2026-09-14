# First refuge v2 — separate Windows test player

Built source `88827d8ba38e541270250e9e4096f3b8046c8a08`, version **0.0.8-refuge.2**, on isolated branch `codex/starfall-first-refuge`. The task is separately pinned. No protected-main merge, deployment or external post was performed.

[Actual player interior](../evidence/milestones/refuge-v2/02-hearth-interior.png) · [Entrance](../evidence/milestones/refuge-v2/01-entrance.png) · [Sleep](../evidence/milestones/refuge-v2/04b-sleep.png) · [Regional context](../evidence/milestones/refuge-v2/06-regional-context.png) · [Clearly labelled concept](../evidence/concepts/refuge/refuge-concept-v1.png)

The authored rock overhang has a supported raised floor, broad entrance ramp, contained hearth, primitive mat and a modest six-log storage place. A collision-driven first-person test controller uses proximity-gated fire, fuel-transfer and rest actions. Rest settles into a visible sleep state after three seconds and can be interrupted. Soft pillow/weave are render-only; the mat and floor retain supporting collision.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Windows player | Built | [Build report](../evidence/verified/refuge-v2/build.json), 0 errors, 21 warnings | Warnings include inherited large-tree collider and optional postprocess stripping; no runtime exception observed |
| Entry/exit and collision | Scripted-player PASS | [40/40 checks](../evidence/verified/refuge-v2/player-report.json), repeated in two visible runs | Physical keyboard/mouse and integrated inhabitant navigation unverified |
| Dry floor and connected ingress | Limited regional PASS | Both measured y=1.8; fixed upper visual water bound -1.889; clearance 3.689m | No future tide, surge or river-flood guarantee |
| Fire and local weather | Scripted-player PASS | Ignition, fuel use, bounded visible light/heat, pause, storm extinguish; sampled wind 2 to .2m/s and rain 1 to .01 | Directional shelter; axial storm wind can enter and extinguish hearth. Not universally cold-safe |
| Rest/sleep and storage | Scripted-player PASS | Visible rest/sleep/wake and finite stock decrement through shared interaction gates | No NPC executor, memory/dream or world-save integration |
| Performance | Borderline | [Run context](../evidence/verified/refuge-v2/runtime-context.json): p95 33.665ms at 1280x720 in coordinated repeat; 96.437ms during shared build activity | Strict 33.3ms target not passed; long soak and other machines unverified |
| Earlier builds | See final preservation report | [SHA256 comparison](../evidence/verified/refuge-v2/preservation.json) | Newly created sibling builds are outside initial inventory |

The hearth consumes integer fuel ticks at 50 Hz, starts with 120 seconds of fuel and six stored 30-second logs, caps fuel at 360 seconds, rejects unsuitable ignition, and extinguishes on unsafe inputs or excessive local weather. Heat is an authored gameplay contribution bounded to 8C within 3m, not a physical combustion model. No spread mechanism exists. At the tested sample, the hearth added 5.333C. Runtime state is session-only.

Cave-zone source is retained from environment-zones `c75357f`. Per-position roof/wind rays drive attenuation. Floors/terrain/ramp use layer 10, walls and solid props layer 8, actor capsule layer 9. The frozen coastal tree geometry is retained. This is the finite coastal component, not the unimplemented panorama-scale canyon. Geometry revision is `terrain-r2-weathered-banks.refuge2`; it does not silently migrate saved worlds.

The first player and failed preflight remain preserved. [Failure record](../evidence/verified/refuge-v1/README.md) distinguishes the furniture-clearance rejection, fail-closed fire/zone state, unusable hidden-launch captures and the later visible player. The v2 primitive art is still a prototype: rock silhouette, bedding, storage and flame presentation remain much simpler than the concept painting.

Local player: `Builds/KookerStarfallRefuge-0.0.8-refuge.2-20260913-191013/StarfallRefuge.exe`. Portable ZIP: `Builds/Packages/StarfallRefuge-0.0.8-refuge.2-Windows.zip` (315043533 bytes), SHA256 `b14e052cab9b7ad698991ca40f480393aa19d183e8ffd7a353a298406dd41d48`. Extract the complete ZIP; keep its data folder and DLLs beside the executable. [Package manifest](../evidence/verified/refuge-v2/package-manifest.json) records included file hashes and CRC verification passed. Packaging is not separate runtime acceptance.

Controls: WASD move, hold RMB look, E toggle hearth within reach, T transfer a stored log, R rest/sleep/wake, Escape wake, P pause simulation. No flight or swimming. Build script `tools/refuge/build.ps1` requires a clean source commit and an explicitly free coordinated Unity slot. For scripted evidence, launch with `-refugeAcceptance -refugeEvidence <new-absolute-directory>` in a visible window.

[Integration handoff](REFUGE-HANDOFF.md) preserves the existing inhabitant authority and shared clock. [Knowledge progression](KNOWLEDGE-PROGRESSION.md) records naive knowledge, provenance, save-scoped beliefs and staged hazards; predator pressure and defenses remain future stages after the safe hearth/bedding milestone. [Discord draft](REFUGE-DISCORD-DRAFT.md) is prepared but not posted.
