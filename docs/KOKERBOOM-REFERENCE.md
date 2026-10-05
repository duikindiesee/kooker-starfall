# Kokerboom reference and procedural contract

**Current user direction:** R06 is the approved visual reference. The numeric/full R19 review is complete and the experimental result is preserved; tree work is frozen. Remaining defects are deferred; the historical 9/10 rubric below no longer blocks world work. See [TREE-BASELINE.md](TREE-BASELINE.md).

Reference review: 10 September 2026. This document guides the isolated `codex/kokerboom-cosmic-desert` follow-up. It is not evidence that a finished mesh or player view meets the brief. The independent acceptance process is in [KOKERBOOM-CRITIQUE.md](KOKERBOOM-CRITIQUE.md).

## Botanical basis

The subject is **Aloidendron dichotomum**, also called quiver tree or kokerboom; the older name is *Aloe dichotoma*. It is a succulent tree from arid southern Africa, including Namibia and the Northern Cape. [SANBI PlantZAfrica](https://pza.sanbi.org/aloidendron-dichotomum), [Kew Plants of the World Online](https://powo.science.kew.org/taxon/urn:lsid:ipni.org:names:77125490-1/general-information).

| Observed description in the sources | Consequence for the model |
|---|---|
| SANBI describes repeated branch forks producing a dense, rounded crown. | Build a readable trunk-to-crown branching hierarchy, with uneven orientations and crown depth visible from the rear as well as the front. |
| SANBI distinguishes golden-brown, sharp-edged trunk scales from smooth upper branches with a whitish reflective coating. | Give the lower trunk irregular flaky relief; transition to smoother, lighter branches instead of applying the same grooves everywhere. |
| SANBI describes blue-green terminal leaf rosettes and vertical leaf rows on young plants. | Attach crowns to branch tips. A juvenile must have a different growth form, not only a smaller adult mesh. |
| Kew describes a stout trunk, pale flaking bark, a rounded crown, and greyish succulent narrow leaves about **30 cm** long with small marginal teeth. | Use thick, tapered, pointed leaf blades with volume. Avoid hair-thin needles, broad flat palm fans, or large saw teeth. |
| SANBI gives approximately **7 m** height; Kew gives a maximum of **9 m**. | Use those as real-world scale references. They do not establish a height-by-age curve or a mandatory height for every specimen. |

Sources for the botanical descriptions in this table: [SANBI description](https://pza.sanbi.org/aloidendron-dichotomum), [Kew general information](https://powo.science.kew.org/taxon/urn:lsid:ipni.org:names:77125490-1/general-information). No exact branch angle, trunk diameter, crown ratio, leaf count, or growth rate is claimed by this document to have been measured from those sources.

## Chosen procedural targets

These are proposed engineering/art bounds, **not botanical measurements**. Record the actual implemented parameters and measured geometry in the validation report. An intentional revision must be documented before judging the resulting images; do not change the rubric to accommodate a weak render.

| Parameter | Proposed target and purpose |
|---|---|
| Units and origin | One mesh unit = one metre. Ground plane at local `y = 0`; positive Y is up. A small buried root base is acceptable and must be included in whole-tree bounds. Include the rosettes in those bounds too. |
| `age01` | Dimensionless development parameter in `[0, 1]`, not years. Validate invalid input explicitly. Sample `0.05`, `0.3`, `0.6`, and `0.9` at the same seed for the scale lineup; the first sample must expose the juvenile stage. |
| Height | Juvenile/young/adult stages should differ in branching and proportions as well as size. Ordinary mature design targets of roughly 3–7 m fit the sources; do not exceed the cited 9 m scale without an explicit stylization decision. No precise juvenile height is sourced here. |
| Mature crown | A broad, rounded crown built from repeated forks. Choose a mature crown-width/tree-height ratio around `0.6–1.2` as a starting range; this is an art target, not a taxonomic rule. Measure X and Z widths so one camera cannot conceal a flat crown. |
| Leaves | An initial adult leaf-length envelope of `0.20–0.40 m`, centred near the cited `0.30 m`, allows deliberate variation. Width, thickness, curvature, orientation, and rosette leaf count are chosen parameters that must remain visually succulent. |
| Branch joins | Daughter branches flow into a shared woody volume; no visible socket lip, disconnected cylinder, exposed internal end cap, or sudden pinching at a fork. Mere mesh overlap is not proof of a convincing union. |
| Bark | Flakes belong to the trunk surface. Avoid floating chips, evenly spaced zebra rings, repetitive vertical fins, and high-contrast grooves continuing to every twig. |
| LOD | Retain the main trunk, major fork hierarchy, crown envelope, and rosette masses. Reduce small bark/leaf details first. State a triangle budget per LOD from actual counts rather than claiming an unmeasured performance result. |

The seed must control variation reproducibly without `UnityEngine.Random`, wall-clock time, global call order, or player travel. Geometry changes need their own version in evidence; they must not change the existing terrain definition or its saved-edit fingerprint.

## Art direction and reference provenance

The user-provided generated image **`kooker-nexus-original.png`** is the earlier composition/palette reference. Its SHA256 is `6390fb0d5e33bd11e1808767b18daebb43d50340f007c73b55256ab64eb88113`. Its warm gold/copper land and trunks contrast with blue-green and deep navy tones. Treat this as stylized visual direction, not botanical evidence or a source of sampled tree geometry. The brief rejects **Earth**; do not recreate recognizable terrestrial continents. The latest user instruction explicitly requests the whole-world look and feel, including a huge blue gas giant, a galaxy, and a sea with life. This supersedes the earlier optional-water scope for the final landscape. It does not make a completed sea or landscape a prerequisite for the current tree-only prototype review.

The final landscape is now requested as a **much larger continuous region**, with turquoise bays/creeks and underwater continuity. Its outline should not reproduce the old CityLife island. Preserve gameplay code and saved data, and place the new landscape under an explicit new versioned world identity. Do not silently rewrite the earlier world's definition or apply its edit overlays to an incompatible base. No fixed extent has been chosen and no infinite-world capability is claimed. The earlier island milestone remains a truthful, preserved record of that earlier world; its measurements do not define the new landscape's size.

Lighting remains configurable. The user's discussion of a visible sun versus no visible sun is exploratory and has not fixed either as a requirement. The blue gas giant remains a planet, not a replacement name for the sun. Current neutral and cosmic light presets support inspection; they do not establish the final world's lighting arrangement.

### Six additional user references, inspected 10 September 2026

The latest attachments are **user-supplied conceptual artwork**, not botanical photographs or Unity implementation evidence. Each was visually inspected. The following is an art reading of their visible content; it does not replace the botanical facts and metre-scale bounds above.

| Reference | Visible content and art direction |
|---|---|
| 01 — desert, water and sky | Open amber desert, dark foreground rocks, distant layered ridges, winding bright cyan water, sparse trees and a large field of navy stars. The scene has substantial open space between individual trees. |
| 02 — tree and galaxy | The close tree has a broad ochre/gold trunk with a flared grounded base. Its forks taper continuously into slimmer gold branches beneath a shallow, rounded dome of pointed blue-green rosettes. The leaf clusters have depth and overlapping blades, with warm edge highlights. A diagonal blue/violet galaxy band crosses the navy sky. |
| 03 — tree and gas giant | A second close tree repeats the substantial trunk, flowing fork junctions and flattened crown envelope. The huge blue gas giant has curved cloud bands and pale swirls, dominating the background above rust-coloured mesas. Warm trunk and desert light remain distinct from the cold blue planet and foliage. |
| 04 and 05 — complete panorama | These two supplied files are byte-identical. Trees and ochre land sit above bright turquoise water. Below the waterline are a ray-like swimmer, fish schools and larger fish, upright aquatic plants, coral-like clusters, rock arches and terraces, light shafts and a patterned seabed. The planet and galaxy remain visible above. |
| 06 — high-resolution panorama | The 6400 × 2880 attachment shows the complete land/sky/sea composition at the highest supplied resolution. A warm, sparsely vegetated shore, the blue gas giant and distant galaxy share the scene with visible underwater life. It is the primary whole-world composition reference in this set. |

For the tree revision, carry forward the broad gold/ochre trunk, continuous taper through repeated forks, and shallow domed crown of blue-green pointed rosettes. Preserve rounded crown depth when viewed from different directions. The artwork supplies a silhouette and material hierarchy; it does not establish exact leaf lengths, fork counts, branch angles, or a biological growth model. Use the sourced botanical description for those decisions and retain independent neutral-light scrutiny of joins and surfaces.

For the later world pass, the explicit user direction is the world look and feel, blue gas giant, galaxy in the distance, and **sea with life in it**. The images support amber desert against navy/cyan space and water, with underwater plants and animals visibly present. This documents the requested appearance; it is not a claim that aquatic content, simulation, or final world rendering already exists. The current tree-only prototype can still be judged before that landscape work.

Exact local copies and a manifest are retained under the Git-ignored `evidence/local/kokerboom-user-references/2026-09-10/` directory. The manifest records attachment order, image dimensions, byte sizes, SHA256, visual reading and the user's direction without full desktop paths. The publicly reviewable identifiers are:

| ID | Pixels | SHA256 |
|---|---|---|
| 01-desert-water-sky | 1920 × 1080 | `b39e1a2a4795ab927bf8c387aa8d94507d14d79a5edb7c6b4e621f11a0be358d` |
| 02-tree-galaxy | 2560 × 1080 | `f734aa1edd4b18473da05b59be98eb2fbf8508996a565823911b33fa4d6511b6` |
| 03-tree-gas-giant | 1920 × 1080 | `e5c195bc0557a06193e57bdafce67d8ba0186db33744eb4a36b20370a2f583eb` |
| 04-world-panorama | 3404 × 1546 | `40453811f2f1050ad9f85e6112697bb9c6253948e3293777ed835813ef9973b0` |
| 05-world-panorama | 3404 × 1546 | `40453811f2f1050ad9f85e6112697bb9c6253948e3293777ed835813ef9973b0` |
| 06-world-panorama-high-resolution | 6400 × 2880 | `054709501737b038f26aad0d1ef5a846f3955f44efc7bdf3b5d2b2cc41a8e8ec` |

All artwork remains local reference material. This document does not assert third-party licensing for it or import it as a game asset. SANBI and Kew are linked as factual references; their photographs have not been copied or embedded, and no permission to redistribute those photographs is assumed. Procedural meshes/materials created in this repository need no downloaded plant photograph. Any future imported media must pass the existing asset provenance and licence checks.

Use neutral light to expose shape and material defects before assessing the cosmic palette. Gold/copper trunk highlights against cooler blue-green foliage and navy surroundings should reinforce the tree silhouette. Bloom, fog, darkness, and tiny framing must not conceal branch joins or sparse crowns. That independent-acceptance prerequisite was part of the earlier protocol; the user-directed R06 baseline and closing R19 review supersede it. Remaining tree defects are deferred, with no further polishing gate on world work. A 3D prototype ground patch with a tree population, believable ground contact, and player-scale framing can establish the first in-world review. An isolated studio tree alone cannot. Rerun real gameplay population views after terrain integration to check that the accepted identity survives placement and lighting changes; the landscape is not a prerequisite for the first tree decision.

## Reproducible implementation checks

The current API is `KokerboomGeometry.Create(int seed, float age01, int lod = 0)`, returning a `GameObject` with separate bark and succulent meshes, and `Describe(int seed, float age01)`, returning `KokerboomDescriptor`. Its dimensions currently describe skeleton estimates; validation records actual mesh bounds separately. An editor-only `ClearCacheForValidation()` allows independent regeneration after all created instances have been destroyed. The following checks describe desired external behaviour, not a second implementation of the generator:

1. **Determinism:** hash a canonical sequence of mesh vertices, triangle indices, normals, UVs, colours, and submesh membership for repeat calls. Generate another seed between repeat calls. The original result must stay identical. Record the Unity version and platform; cross-platform bit identity remains unverified until tested there.
2. **Seed diversity:** fixed seeds `0`, `314`, `4242`, `-1`, `int.MinValue`, and `int.MaxValue` must produce valid meshes. Several ordinary seeds must produce distinct geometry; a seed merely recorded in metadata does not prove variation.
3. **Mesh validity:** finite attributes/bounds, indices within range, nonzero triangle areas above a stated numerical threshold, matching attribute counts, and no unintended empty submeshes. Inspect outward surfaces and normals separately; a valid index buffer does not prove correct winding.
4. **Development:** measured height and trunk/crown structure for the fixed age lineup must support development, including a juvenile form. Compare the same seed. Do not require every vertex or leaf count to increase monotonically; pruning or LOD can reduce those counts.
5. **Branch support:** metadata describes an acyclic rooted woody hierarchy, with positive radii and continuous parent/child attachment. Internal fork records identify both daughters. Terminal rosette centres attach to supported tips, within a documented tolerance. Check the actual generated bounds as well as the description.
6. **LOD preservation:** compare the same seed and age across supported LODs. Report measured height, crown width, terminal placement, and triangle counts. A proposed `5%` envelope tolerance is an engineering target; any deliberate simplification beyond it needs visual review. No LOD may remove the recognizable major fork structure.
7. **World separation:** generation must leave the world definition and base terrain fingerprint unchanged. Existing deterministic-world and saved-edit validation must continue to pass.

Useful `Describe` metadata: geometry version; seed, age and LOD; declared units; measured bounds and height; vertex/triangle counts per submesh; trunk radius; branch IDs and parent IDs with endpoints/radii; terminal positions; rosette count and centres; leaf-length range. Include measurement definitions and the mesh hash so that a report cannot silently refer to another specimen. Counts and declared radii alone do not establish believable anatomy or a high visual score.

`CityLife.World.Editor.KokerboomValidation.Run` implements a bounded numeric check, with a report at `evidence/local/kokerboom-validation.json`. It generates meshes for seeds `4242` and `314`, all three LODs for a mature specimen, and a four-stage age lineup; extreme/zero seeds receive descriptor-only probes. It hashes complete mesh attributes and independently regenerates after clearing the cache. Wood-only checks inspect connected components, boundary/nonmanifold edges, winding and vertex fans. The report distinguishes estimated descriptor dimensions from actual mesh bounds. Botanical branch hierarchy and per-leaf dimensions are not independently validated yet. Run this in an isolated editor validation session, before constructing a render scene whose meshes must remain live. The [preserved Round 04 run](../evidence/verified/kokerboom-round-04-validation.json) passed 349 assertions with independent regeneration and closed wood topology; that numeric evidence does not override Round 04's independent visual rejection.

## Current evidence boundary

The final [R19 full-family review](KOKERBOOM-CRITIQUE.md#round-19-frozen-family-review) records **7.375 overall (7.4 displayed), botanical 7.375 and art 7.5**, from all 21 images. It is rejected under the historical 9/10 rubric and frozen as an experiment. The approved R06 visual reference remains separate. All remaining R19 defects are deferred; no further tree-polishing or world-work gate is prescribed.

The historical R16 full-family review was **rejected at overall 5.75/10: botanical 6.75 and art 5.75**. All 21 actual views were independently inspected. The critic retains fuller PH02 rosettes, while support/wood continuity, bend shading, bark peeling, juvenile/mature ground contact and blue-green canopy readability remain unresolved. [Exact scores and image-specific findings](KOKERBOOM-CRITIQUE.md#round-16-ph02-fitted-crown-family-rejected) follow the unchanged six-criterion protocol; the historical pass target was both means at least 9.0 with no major defect. It is no longer the current world-work prerequisite.

R16 is a changed `citylife.aloidendron-dichotomum.v2-preview` family using actual imported PH02 crown tuples, fitted supports and modified procedural branches with PH01 mapped bark. [Component checks](../evidence/milestones/kokerboom/round-16/ph02-family-component-checks.json) verify source preservation and actual mesh readback; they do not establish full-family topology, appearance or runtime suitability. Source-file downloads remain unchanged while generated geometry, placement and materials are derivatives. The species is not certified by the publisher's quiver-tree label. The controlled foliage tint is zero in R16; the desired blue-green identity is still a visual requirement, not a passed result.

R09's separate 380-assertion hybrid result applies only to its recorded PH01 source state. Earlier 349-assertion procedural passes also do not apply to R16. The fresh [PH02 family numeric run failed at assertion 522](../evidence/verified/kokerboom-round-16-ph02-validation-failed.json): the full-age root extends below the unchanged −1 m floor. R16 render bounds already show minimum Y = −1.0133981704711914 m. Ten inspected wood meshes are single closed indexed manifolds; nine specimens are fully recorded and independent regeneration passed. PH02 cleanup completed, with all ten individual input hashes unchanged in `finally`; the aggregate end-of-run source gate was not reached, so its false field is not a completed source-integrity pass. That failure remains historical. The revised R18 experimental source subsequently [passed 536 assertions](../evidence/verified/kokerboom-round-18-family-validation.json), with ten recorded specimens/wood topology samples, regeneration, eleven unchanged sources and PH02 cleanup. R17/R18 remain unscored pilots; [R19 has completed all 21 offscreen captures](KOKERBOOM-CRITIQUE.md#round-19-frozen-family-review), with independent result 7.375/10, frozen; it is the final experimental review under the user-directed freeze. R06 remains the already-built small native WIP preview, now packaged without changing its runtime bytes; later R16 offscreen imagery is not a screenshot of that executable. [Capture and package distinction](VISUAL-MILESTONES.md#existing-r06-player-package-separate-from-r16-source) preserves these boundaries.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Botanical reference reviewed | Verified | SANBI and Kew pages linked above, reviewed 10 September 2026 | No direct specimen measurement or photographic redistribution rights asserted |
| User art identified | Verified | Earlier image plus all six new attachments inspected; dimensions and SHA256 above; exact local copies verified | No imported art asset or redistribution/licensing claim |
| Final world direction | Explicitly requested; implementation unverified | Latest user instruction and inspected panoramas: gas giant, galaxy and sea with life | Later landscape and aquatic implementation/evidence; not a prerequisite for tree-only review |
| Revised PH02 experimental source | **R18 PASS, 536 assertions**; R16 failure preserved | [R18 report](../evidence/verified/kokerboom-round-18-family-validation.json), [historical R16 failure](../evidence/verified/kokerboom-round-16-ph02-validation-failed.json) | R19 full review recorded at 7.375/10; defects deferred; no automatic R06 promotion or native performance claim |
| Final experimental visual review | R19: overall 7.375, historical rubric rejected; frozen | [Full critique](KOKERBOOM-CRITIQUE.md#round-19-frozen-family-review); all 21 views | Defects deferred; R06 user-approved reference retained |
| Earlier PH01 hybrid | R09 captured and rejected; numeric checks passed separately | [Derivative-use record](ASSET-CREDITS.md#r05-generated-derivatives-of-ph01) | R10 imported-tangent comparison found no meaningful visual gain; historical results do not validate PH02 |
