# Coastal slice — first review checkpoint

> A separate bounded player now exists as a later engineering checkpoint. See [Coastal playable checkpoint](COASTAL-PLAYABLE-CHECKPOINT.md). The four-screen panorama is the main visual authority; the fixed close camera below remains a component-study view.

The first bounded Starfall coastal study is ready for user review. Three parallel workstreams built water/sea, canyon terrain, and the rocky tree bank with a small succulent set. The combined Unity scene retains the frozen R19 tree and adds the blue giant and a procedural distant galaxy. The supplied concepts are **provisional references**; an exact image/camera match is not claimed.

![Actual Unity second coastal side-view](../evidence/milestones/coastal/round-02/2026-09-10-01-coastal-side-composition.png)

This is an actual Unity1600×900 GPU capture. It is an offscreen scene study, not a new coastal executable, painted mockup or finished world. The separately released [R19 player](RELEASE-0.0.2-preview.1.md) is unchanged by this work.

## One baseline, one revision, then review

| Element | Baseline | Revised | Visible gain | Remaining gap |
|---|---:|---:|---|---|
| Water and sea | 4/10 | 5/10 (+1) | Vivid turquoise and broader blue sea horizon | Opaque foreground, triangular outlet seam, distant striping and uniform mint shoreline halo |
| Terrain/canyons | 4/10 | 5.5/10 (+1.5) | Less regular layers, muted ochre, broad gullies/buttresses | Smooth inflated cliff faces, weak strata and looping crack marks |
| Rocks | 5.5/10 | 6.5/10 (+1) | Dark stone separates from sand/water; one excessive overhang corrected | Repeated bevelled slabs, smooth faces, limited visible descent through the waterline |
| Succulents | 4/10 | 4/10 (0) | Small flowers and column forms remain visible | Many rosettes obscured; no second flora pass attempted |
| Combined scene | 4.5/10 | 5.5/10 (+1) | Clearer colour hierarchy, continuous sky and river-to-sea composition | Water realism and bank transition; provisional reference and camera |

Scores are subjective art-direction judgements, not objective benchmarks or user approval. Element owners reviewed their own actual views; the root agent made the combined retrospective comparison. The scores are not statistically independent. No nine-point target was applied.

Effort was three parallel initial implementations, one targeted water/terrain/rock revision, one sky-extent correction and four views per round. The rock pass also lowered one overhanging block by1.15m after measuring its lower face against the actual terrain triangles. No further visual iteration has been performed. Dollar costs were not measured. Capture timings measure rendering plus readback, not development effort or gameplay FPS.

| Comparable view | Baseline | Revised |
|---|---|---|
| Combined side-view | [R01](../evidence/milestones/coastal/round-01/2026-09-10-01-coastal-side-composition.png) | [R02](../evidence/milestones/coastal/round-02/2026-09-10-01-coastal-side-composition.png) |
| River opening to sea | [R01](../evidence/milestones/coastal/round-01/2026-09-10-02-water-to-sea.png) | [R02](../evidence/milestones/coastal/round-02/2026-09-10-02-water-to-sea.png) |
| Rocky tree bank | [R01](../evidence/milestones/coastal/round-01/2026-09-10-03-rocky-tree-bank.png) | [R02](../evidence/milestones/coastal/round-02/2026-09-10-03-rocky-tree-bank.png) |
| Canyon opening | [R01](../evidence/milestones/coastal/round-01/2026-09-10-04-canyon-opening.png) | [R02](../evidence/milestones/coastal/round-02/2026-09-10-04-canyon-opening.png) |

All four recorded camera fields, light fields and image dimensions compare exactly between the rounds. Shader appearance, local terrain banks, one rock placement and the distant ocean/sky extent intentionally changed. See the [machine-readable review](../evidence/verified/coastal-first-review.json).

## What is verified

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Actual composed Unity scene | Four captures passed, 0 capture errors / 2 warnings | [R02 metrics](../evidence/milestones/coastal/round-02/metrics.json) and images above | User visual acceptance; native coastal player |
| Frozen R19 tree | Main counts retained: 3,745,751 vertices / 6,550,207 triangles, PH02 tint1 | Same metrics | Tree review remains frozen; this does not award a new score |
| Terrain | Actual mesh64,829 vertices /128,640 triangles | Same metrics | Convincing geological detail and native travel |
| Rocks and succulents | Actual35,298 vertices /11,766 triangles across85 meshes | Same metrics | Flora visibility and more natural rock forms |
| Water | Actual16,045 vertices /31,320 triangles across2 meshes | Same metrics | Seam-free clear-water depth, underwater view and swimming |
| Rock/ground physics | PASS84/84 rock ray hits and8 terrain mesh-vertex rays | [Actual scene collision record](../evidence/milestones/coastal/round-02/coastal-collision.json) | Not a native movement, player-body or swimming test |
| Existing world and builds | Separate worktree/branch, source scene and identity | [Definition](../evidence/milestones/coastal/round-02/coastal-definition.json); no old island resource changes | No new saved-world contract or coastal distribution yet |

The water/terrain do not contain fish, rays, kelp or a functioning underwater ecosystem. Those remain the [living-sea direction](STARFALL-LIVING-SEA.md). Sustained FPS and native coastal controls have not been measured.

## Version and boundaries

The study uses `starfall.coastal-slice.v1`, terrain seed1904242 and tree/rock seed4242. The active terrain covers x[−90,90], z[−55,145] metres; water level is−2m. Revised terrain content is explicitly `terrain-r2-weathered-banks`, with source hashes recorded in the definition. It preserves the hero pad and river controls while changing surrounding banks. It neither loads nor overwrites an earlier saved world.

The distant ocean is an additional **visual-only** surface, x[−960,960], z[145,1015] metres. It has no additional terrain, collision, navigation, population or saved state. The procedural sky is separate from the user's artwork; no concept image is used as a game texture.

Baseline source: `0c40ab7`. Single revised source: `616ca85`. Earlier R01 water source was recovered from the reverse patch and checked byte-for-byte against its pre-change hashes before the baseline commit was made. Both complete image sets remain preserved.

Render another explicitly authorized comparison with the pinned editor:

```powershell
.\tools\render-coastal.ps1 -Round round-03
```

Use an unused round. This command does not launch a player or replace R06/R19. The current checkpoint is to show the two sets and choose the next meaningful change, not to start another unattended polishing loop.
