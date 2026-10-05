# Kokerboom critique protocol

**Current user direction:** R06 is the approved visual reference. The numeric/full R19 review is complete and the experimental result is preserved; tree work is frozen. Remaining defects are deferred; the historical 9/10 rubric below no longer blocks world work. See [TREE-BASELINE.md](TREE-BASELINE.md).

Protocol version: `citylife.kokerboom-critique.v1`, fixed before the follow-up's first independent render review. See [KOKERBOOM-REFERENCE.md](KOKERBOOM-REFERENCE.md) for botanical sources, scale targets, and art provenance. Recorded scores come from independent image review; the geometry author and documentation author must not self-award acceptance.

The [interactive visual review](KOKERBOOM-VISUAL-REVIEW.html) presents the preserved evidence and score history. This document retains the exact review scope, arithmetic and outstanding defects.

## Historical required evidence set

Capture the same identified mature specimen in front, three-quarter, and rear views under neutral light. Add close-ups of at least two branch unions, lower-trunk bark and its transition to upper branches, and a terminal rosette with individual leaves readable. Include a fixed-seed juvenile-to-adult lineup with metre marks or a labelled human-scale reference. Framing must show the full crown and trunk where required.

Then capture **two actual 3D prototype-patch views at player framing**, including neutral and reference-inspired lighting. Show a readable nearby tree and the tree population with believable ground contact and scale. A populated prototype ground patch is sufficient; an isolated specimen against a studio plane is not population evidence. Neither a completed landscape nor water/underworld rendering is required for this first tree review. The original protocol required tree approval before the landscape rewrite. The later user-directed freeze supersedes that prerequisite; future world integration can assess the retained identity without another tree-polishing loop.

Retain original rendered image files and their dimensions. Label image IDs, seed, `age01`, LOD, geometry version, camera transform/FOV, lighting preset, exposure/post-processing, renderer, and build/commit or source digest. Record mesh hashes and validation report paths. A contact sheet may aid review, but it does not replace full-resolution close-ups. Do not use image generation, painting, or retouching as implementation evidence.

## Six fixed criteria

Each criterion receives a score from **0 to 10** only after the critic has inspected its relevant images. Use half-point increments if needed. A missing required view makes the affected criterion **unverified**, not zero or an inferred pass.

| # | Criterion | What the critic must inspect | Examples of major defects |
|---|---|---|---|
| 1 | Anatomy and silhouette | Stout trunk; repeated forks; broad rounded, three-dimensional crown; a recognizable kokerboom in front, three-quarter, and rear views | Palm/antler silhouette; planar or sparse crown; repeated identical fork angles; thin pole trunk |
| 2 | Joins and surface | Convincing continuous fork unions; branch taper; lower-trunk flakes; transition into smoother pale upper branches; coherent normals/shading | Floating or socketed branches; visible cylinder caps; abrupt diameter jumps; detached bark; severe faceting or repetitive grooves dominating the tree |
| 3 | Rosettes and leaves | Terminal placement; blue/grey-green succulent volume; pointed narrow blades; variation within a coherent rosette; adequate crown density | Needle balls, paper fans, leafless tips, giant leaves relative to metre scale, floating rosettes, obvious intersecting slabs |
| 4 | Age and size variation | Juvenile leaf arrangement and development; meaningful changes of proportion and fork structure; credible metric scale; variation across seeds | Identical adult tree simply scaled down; all trees identical; impossible dimensions; collapsed or disproportionately thin young trunks |
| 5 | Palette and light identity | Gold/copper against blue-green and navy; reference-appropriate illumination; foliage separation and controlled highlights. If a planetary sky is shown, a large blue gas giant is permitted and Earth is excluded. Water is not required. | Earth-like continental globe; clipped bloom swallowing form; muddy foliage; colour grading that conceals failed joins or turns the tree into neon plastic |
| 6 | In-world readability | Nearby and population views at player framing on an actual 3D prototype patch; visible form; stable scale and LOD; believable ground contact and distribution | Floating/sunken trees; unreadable silhouettes; abrupt LOD collapse; distracting repeated clones; acceptable isolated specimen that fails in the population views |

Use the same anchors for all six criteria:

| Score range | Meaning |
|---|---|
| 0–2 | Required structure or visual function is largely absent. |
| 3–4 | Some relevant cues exist, but the result reads incorrectly or has dominant defects. |
| 5–6 | Recognizable foundation with several conspicuous problems. |
| 7–8 | Coherent result with visible issues that still need a targeted revision. |
| 9 | Convincing in every required view; only minor polish remains and there is no major defect. |
| 10 | Exceptionally resolved across the evidence set, with no meaningful defect identified by the critic. This is not automatic from passing numerical tests. |

The critic must give a short image-specific reason for every score, including the strongest evidence and any remaining defect. State uncertainty when resolution or framing prevents a conclusion. Do not infer surface quality from a distant silhouette or botanical quality from attractive lighting.

## Historical critic acceptance calculation

- **Botanical mean** = `(criterion 1 + criterion 2 + criterion 3 + criterion 4) / 4`.
- **Art mean** = `(criterion 5 + criterion 6) / 2`.
- **Overall** = the lower of the botanical and art means.
- **Pass** requires both means to be at least **9.0**, all six criteria verified, the full evidence set present, and **no major defect**. Keep the actual arithmetic; do not round a value below 9.0 up to a pass.

A major defect blocks acceptance regardless of means. The critic explicitly labels major defects and names their image IDs. A missing or unverified criterion blocks a pass; leave the means pending instead of substituting numbers. The evidence and rule remain fixed across iterations. The earlier protocol called for implementation revisions and renewed independent review, retaining inconvenient evidence and fixed weights. That iterative requirement is retired by the current user-directed freeze.

## Round 01: rejected

**Independent critique, 10 September 2026: REJECTED.** The independent critic inspected all nine actual Unity images below. The coordinating task supplied the scores and findings recorded here; these are subjective visual judgements, not scores awarded by the geometry or documentation author. Numerical mesh checks do not change them.

| Criterion | Score / 10 | Critic's reported defects and relevant image IDs |
|---|---:|---|
| Anatomy and silhouette | 4.5 | Sparse antler-like bouquets with a central crown void; thin branches instead of a convincing full crown. 01–03, 08–09. |
| Joins and surface | 2.0 | Branch corrugation, triangular holes, and a disconnected appearance; regular rectangular bark tiles and a sharp skirt at the base. 04–05, also 01–03. |
| Rosettes and leaves | 4.0 | Dark cup/socket-like rosette bases and overly uniform upright leaves. 06, also the crown views. |
| Age and size variation | 4.5 | Juvenile leaf mass is too small; the lineup does not demonstrate mature variation between seeds at a fixed age. 07. |
| Palette and light identity | 5.0 | Reference lighting leaves the canopy too dark; colour direction has not established the required visual identity. 08–09. |
| In-world readability | 4.0 | Canopy reads black at walking distance and the sparse crown remains apparent in population views. 08–09. |

**Botanical mean: 3.75. Art mean: 4.50. Overall: 3.75/10**, displayed as **3.8/10** when using one decimal place. The decision uses the unrounded value. Major surface/join and crown/readability defects remain; the tree does not pass. The threshold is unchanged: **both means at least 9.0, with no major defect**.

The [Round 01 render manifest](../evidence/milestones/kokerboom/round-01/metrics.json) records the actual Unity 6000.6.0f1 URP GPU captures at `2026-09-10T17:46:31.3272614Z`, including camera, lighting, image and subject measurements. These are offscreen editor renders of a procedural specimen and populated prototype patch. They do not establish native input or desktop presentation. All nine original files remain at their original paths:

| ID | Preserved actual Unity image |
|---|---|
| 01 | [Neutral front](../evidence/milestones/kokerboom/round-01/2026-09-10-01-neutral-front.png) |
| 02 | [Neutral three-quarter](../evidence/milestones/kokerboom/round-01/2026-09-10-02-neutral-three-quarter.png) |
| 03 | [Neutral rear](../evidence/milestones/kokerboom/round-01/2026-09-10-03-neutral-rear.png) |
| 04 | [Branch-union close-up](../evidence/milestones/kokerboom/round-01/2026-09-10-04-branch-union-closeup.png) |
| 05 | [Trunk-bark close-up](../evidence/milestones/kokerboom/round-01/2026-09-10-05-trunk-bark-closeup.png) |
| 06 | [Terminal-rosette close-up](../evidence/milestones/kokerboom/round-01/2026-09-10-06-terminal-rosette-closeup.png) |
| 07 | [Age lineup with metre scale](../evidence/milestones/kokerboom/round-01/2026-09-10-07-age-lineup-metres.png) |
| 08 | [Cosmic prototype at eye level](../evidence/milestones/kokerboom/round-01/2026-09-10-08-cosmic-gameplay-eye-level.png) |
| 09 | [Cosmic prototype overlook](../evidence/milestones/kokerboom/round-01/2026-09-10-09-cosmic-gameplay-overlook.png) |

The first numerical **PASS** was recorded at `2026-09-10T17:45:41.5366081Z`: **325 assertions**, with `independentRegeneration: true`. The [preserved Round 01 validation report](../evidence/verified/kokerboom-round-01-validation.json) has SHA256 `c3179e9d6f298ef473ed4c7824457ea3752517b5a43124fb3246d9cf073a0c07`. That report checks the recorded Round 01 mesh data and reproducibility; it does not demonstrate visually continuous branches, convincing bark, botanical realism, or a score of 9. It does not validate subsequent geometry changes. Round 01's visual rejection stands independently of the numerical pass.

### Evidence requested after Round 01

The Round 01 review requested all nine views again after revising the actual geometry and material fields, plus an isolated rosette from above and from the side, a juvenile close-up, and within-age mature seed variation. Round 02 below supplies those views and an additional no-shadows diagnostic. The original Round 01 files remain unchanged.

The requested changes addressed continuous thin branches and fork surfaces, irregular trunk flakes without a sharp basal skirt, crown density and shape, rosette attachments and leaf-angle variety, juvenile leaf mass, mature seed variation, and canopy readability. These were revision targets, not claims that a repair had passed. Full terrain, water, and underworld work remain outside this first tree-acceptance gate.

## Round 02: not accepted

**Independent critique, 10 September 2026: NOT ACCEPTED.** The critic inspected all **14 actual Unity images** listed below. These scores and findings were relayed by the coordinating task; the implementation and documentation authors did not self-score. No penalty was applied for an unfinished ocean or galaxy during this tree-only phase.

| Criterion | Score / 10 | Critic's reported defects and relevant image IDs |
|---|---:|---|
| Anatomy and silhouette | 6.0 | A flat horizontal branch lattice needs more upward terminal growth and a domed crown. 01–03. |
| Joins and surface | 3.5 | **Major:** branch ribbing persists in 04–06 and in the no-shadows view 10. Bark in 05 reads as embossed paving cells. The juvenile in 13 has dark pockets and a stretched surface. |
| Rosettes and leaves | 5.5 | Nested cups/socket-like leaves remain visible from the side in 12. **Major:** the juvenile in 13 reads as a closed bud on a textured pole. 06, 11–13. |
| Age and size variation | 5.0 | The juvenile form remains unresolved, while the adult specimens retain near-identical trunk, fork and crown proportions across seeds. 07, 13–14. |
| Palette and light identity | 5.0 | Foliage remains dark and visually small beside dominant branches in the reference-lit population views. 08–09. |
| In-world readability | 4.5 | Dark, small foliage and branch-dominated silhouettes still weaken the population at player framing. 08–09. |

**Botanical mean: 5.00. Art mean: 4.75. Overall: 4.75/10**, displayed as **4.8/10**. This improves on Round 01's 3.75, but remains below acceptance. The unchanged rule requires **both means at least 9.0 and no major defect**.

Persistence of the ribbing without cast shadows rules out cast shadows as the sole explanation. These images do **not** isolate whether geometry, normals, shader behaviour, or a combination causes it. Repair and comparison work must preserve that uncertainty until a diagnostic distinguishes them; a passing index-topology check is not an explanation of the visible ribbing.

The [Round 02 render manifest](../evidence/milestones/kokerboom/round-02/metrics.json) records Unity 6000.6.0f1 URP GPU captures at `2026-09-10T18:11:24.2779683Z`. Twelve inspection views are 1600 × 1200; the two population views are 1600 × 900. They are offscreen editor renders, with no native input or presented-frame acceptance claim. All files remain at their original paths:

| ID | Preserved actual Unity image |
|---|---|
| 01 | [Neutral front](../evidence/milestones/kokerboom/round-02/2026-09-10-01-neutral-front.png) |
| 02 | [Neutral three-quarter](../evidence/milestones/kokerboom/round-02/2026-09-10-02-neutral-three-quarter.png) |
| 03 | [Neutral rear](../evidence/milestones/kokerboom/round-02/2026-09-10-03-neutral-rear.png) |
| 04 | [Branch-union close-up](../evidence/milestones/kokerboom/round-02/2026-09-10-04-branch-union-closeup.png) |
| 05 | [Trunk-bark close-up](../evidence/milestones/kokerboom/round-02/2026-09-10-05-trunk-bark-closeup.png) |
| 06 | [Terminal-rosette close-up](../evidence/milestones/kokerboom/round-02/2026-09-10-06-terminal-rosette-closeup.png) |
| 07 | [Age lineup with metre scale](../evidence/milestones/kokerboom/round-02/2026-09-10-07-age-lineup-metres.png) |
| 08 | [Cosmic prototype at eye level](../evidence/milestones/kokerboom/round-02/2026-09-10-08-cosmic-gameplay-eye-level.png) |
| 09 | [Cosmic prototype overlook](../evidence/milestones/kokerboom/round-02/2026-09-10-09-cosmic-gameplay-overlook.png) |
| 10 | [Neutral front without shadows](../evidence/milestones/kokerboom/round-02/2026-09-10-10-neutral-front-no-shadows.png) |
| 11 | [Isolated rosette from above](../evidence/milestones/kokerboom/round-02/2026-09-10-11-isolated-rosette-above.png) |
| 12 | [Isolated rosette from the side](../evidence/milestones/kokerboom/round-02/2026-09-10-12-isolated-rosette-side.png) |
| 13 | [Juvenile close-up](../evidence/milestones/kokerboom/round-02/2026-09-10-13-juvenile-closeup.png) |
| 14 | [Adult seed variation](../evidence/milestones/kokerboom/round-02/2026-09-10-14-adult-seed-variation.png) |

The [preserved Round 02 numerical report](../evidence/verified/kokerboom-round-02-validation.json), SHA256 `07e643d7763879281e23494c844be038216f437e4784af4d2fdc3a87a5aafd3d`, records **PASS, 349 assertions, independent regeneration** at `2026-09-10T18:07:37.5005888Z`. Its wood-only indexed topology checks and other mesh-data checks apply to the source captured in that report. They do not establish natural bark, well-shaped leaves, acceptable lighting, or a visual score of 9, and do not validate later changes.

The next work is targeted procedural surface/shape repair and actual Unity comparison with the separately credited Poly Haven quiver-tree candidates. Those are planned comparison paths, not accepted replacements. Further visual scores require another independent review of actual renders.

## Round 03: scoped imported-candidate review

The independent critic reviewed all **12 actual Unity comparison captures** of the original-scale Poly Haven Quiver Tree 01 and 02 assets. [The preserved images and source provenance](VISUAL-MILESTONES.md#kokerboom-follow-up--round-03-imported-candidate-comparison--2026-09-10) identify the two models and their explicit source texture assignments. The coordinator relayed these scoped scores; neither the implementation nor documentation author self-scored.

| Scoped dimension | PH01 / 10 | PH02 / 10 |
|---|---:|---:|
| Joins | 8.5 | Unverified: unbranched specimen |
| Bark | 8.5 | 8.5 |
| Rosettes/leaves | 7.5 | 8.0 |
| Palette | 4.5 | 5.0 |
| World readability | 6.5 | 7.0 |

These are candidate-specific observations, not the six-criterion family rubric. No botanical, art, or overall mean is computed from this table. PH02's unverified join result is not a zero. The comparison does not establish procedural development, variation across seeds, population suitability, or an accepted replacement. **Round 03 did not replace Round 02's full-family overall of 4.75**; Round 04 is a separate full-family review below.

The reviewer recommends studying PH01's natural branch shoulders and surface transitions, PH02's substantial overlapping leaves with upright inner blades and drooping outer blades, and both candidates' varied bark, scars and peeling. Preserve that surface variation; a uniform gold tint is not the recommendation. The procedural 349-assertion validation does not apply to these imported meshes.

## Round 04: not accepted

The revised procedural family has **15 actual Unity captures**, including the untextured branch-union diagnostic, in the [preserved Round 04 catalogue](VISUAL-MILESTONES.md#kokerboom-follow-up--round-04-not-accepted--2026-09-10). The [render manifest](../evidence/milestones/kokerboom/round-04/metrics.json) records `2026-09-10T18:28:46.7582526Z`, technical capture checks passed, **0 errors and 1 warning**. This is actual offscreen URP output of the tree and prototype patch, not native input or desktop-presentation acceptance.

**Independent full-family verdict: NOT ACCEPTED.** The critic found the evidence sufficient for all six criteria. The coordinating task supplied these scores and findings; no self-score, imported-candidate score, or numerical assertion count enters the means.

| Criterion | Score / 10 | Independent finding and remaining work |
|---|---:|---|
| Anatomy and silhouette | 6.5 | Build a rounded canopy with greater depth and overlapping rosette mass; shorten exposed terminal branch runs. 01–03. |
| Joins and surface | 6.0 | Preserve the wood repair: bands are largely gone in 04 and 15. Replace cellular bark with ragged incomplete peeling boundaries, lifted edges, pale substrate and ochre variation. The white material in 15 is too bright for subtle shading assessment. 04–05, 15. |
| Rosettes and leaves | 6.0 | Improve natural leaf cross-sections and juvenile attachment; remove dark cups, angular bends and exposed terminal stubs. 06, 11–13. |
| Age and size variation | 6.0 | Include stockier seed variants, differing fork heights and crown depths, and asymmetric branching generations. 07, 13–14. |
| Palette and light identity | 5.5 | Show actual blue-green leaf areas in the reference-lit views, rather than relying on sparkling edges. 08–09. |
| In-world readability | 5.0 | Canopy mass and visible leaf area still need to read at player framing. 08–09. |

**Botanical mean: 6.125. Art mean: 5.25. Overall: 5.25/10.** R03 imported-candidate scores and the numeric pass are excluded from this arithmetic. The family improves on R02's 4.75 but is **not accepted** under the unchanged requirement: both means at least 9.0 and no major defect.

The [Round 04 numerical report](../evidence/verified/kokerboom-round-04-validation.json) records **PASS: 349 assertions, independent regeneration** at `2026-09-10T18:25:55.4966694Z`; SHA256 `a13a044b199ec2039e68028ce99907ee6d18c186298391f7a283cdd0dd995609`. All nine recorded wood topology inspections have one connected component, zero boundary edges, zero nonmanifold edges, and zero nonmanifold vertex fans. These are indexed-mesh and reproducibility checks for this recorded procedural source, not visual scores or validation of the imported candidates.

Retain the verified wood-topology repair while addressing the remaining shape, bark, leaf and lighting findings. Earlier failures, scores and image sets remain preserved. Further acceptance requires another independent review of the revised actual renders.

## Round 05: hybrid shader failure

R05 changed the procedural architecture and added extracted PH01 source-rosette derivatives. The [failure manifest](../evidence/milestones/kokerboom/round-05/metrics.json) records `2026-09-10T18:43:44.1108644Z`, technical capture checks **failed**, **3 errors and 1 warning**, and only one of the intended 21 images. The retained local compiler log identifies an undeclared `inversesqrt` in `CityLife/KokerboomSurface` on DX11. The [single front image](../evidence/milestones/kokerboom/round-05/2026-09-10-01-neutral-front.png) is failure evidence, not a completed family review. **No R05 score is assigned.** This failed round is preserved separately from the later retry.

The [derivative provenance](ASSET-CREDITS.md#r05-generated-derivatives-of-ph01) distinguishes unchanged downloads from five groups of 21 source leaves, generated placement/tint and interior-atlas bark mapping. Basal pieces were configured only for separate diagnostics 18–21, not hybrid trees. The failed run did not reach those diagnostics. Their identity and exact Latin species remain unverified. R04's numeric 349-assertion pass does not transfer to this changed hybrid.

## Round 06: scoped WIP preview review

Two actual stage captures were produced before the separate local WIP executable bake. The [R06 manifest](../evidence/milestones/kokerboom/round-06/metrics.json) records `2026-09-10T18:50:27.6914345Z`, **2 captures**, technical checks passed, **0 errors and 1 warning**. The independent scoped review supplied **palette 4.5/10 and readability 5.0/10 only**. No missing criterion is filled in, and no full-family mean or acceptance is inferred.

The [separate preview build](../evidence/milestones/kokerboom/round-06/preview-build.json) succeeded and the new-only local WIP player was launched. This is a small baked tree/blue-giant stage, not the larger final landscape or an accepted family. Native preview state and later branding are recorded in [STARFALL.md](STARFALL.md). The existing R06 images and executable retain their original names; no old island was substituted for this preview. Source-only/offscreen evidence does not establish all native controls or offline operation.

## Round 07: hybrid family not accepted

The independent critic reviewed **all 21 actual Unity captures** and found them sufficient for all six fixed criteria. The coordinating task supplied the following subjective scores; neither the geometry author nor documentation author self-scored them. The [preserved catalogue](VISUAL-MILESTONES.md#kokerboom-follow-up--round-07-hybrid-family-not-accepted--2026-09-10) links every image. Its [render manifest](../evidence/milestones/kokerboom/round-07/metrics.json) records `2026-09-10T19:01:40.8125014Z`, technical capture checks passed, **0 errors and 1 warning**.

| Criterion | Independent score | Recorded findings |
|---|---:|---|
| Anatomy and silhouette | 6.0 | The crown remains a narrow, peaked cage rather than the broad, rounded overlapping mass. |
| Joins and surface | 5.0 | Severe horizontal branch texture stretch and mirrored bark remain conspicuous. |
| Rosettes and leaves | 7.0 | Source leaves have better curvature, but coverage is sparse and exposed stubs remain. Separate basal pieces do not supply missing healthy foliage. |
| Age and size variation | 6.0 | Mature structures remain too similar; the juvenile still reads as a stump. |
| Palette and light identity | 4.5 | The requested palette and visible leaf mass remain unresolved. |
| In-world readability | 5.0 | Albedo-only and stronger-fill diagnostics expose a geometry-coverage problem; lighting alone does not restore the missing canopy mass. |

**Botanical mean: 6.00. Art mean: 4.75. Overall: 4.75/10. Decision: REJECTED.** Both means must still reach 9.0 with no major defect. The full-family result is separate from scoped R03/R06 observations and the historical R04 numeric pass. R04's 349 assertions do not validate this changed architecture/source-leaf hybrid. The original PH01 files remain unchanged; the hybrid is a derivative described in [asset credits](ASSET-CREDITS.md#r05-generated-derivatives-of-ph01).

The next work targets broader, deeper canopy coverage, structure variation, and branch surface mapping. A separate PH02 original/cut-crown comparison is prepared to inspect a different source-leaf basis, without treating that comparison as a full-family replacement. Future untextured branch diagnostic captures use mid-gray; earlier white-diagnostic images remain unchanged. PH01 imported-versus-extracted position/normal/UV identity measurement is staged for the next hybrid run; its result is not yet claimed here.

## Round 08: scoped PH02 crown review

The editor-only mode `RenderPH02CrownCandidate` produced eight views of the original-scale PH02 source, an explicit open horizontal cut at proposed Y=0.65 m, and a separate simple support attachment. The [R08 manifest](../evidence/milestones/kokerboom/round-08/metrics.json) records `2026-09-10T19:16:24.8950315Z`, technical capture checks passed, **0 errors and 1 warning**. [All eight views are preserved](VISUAL-MILESTONES.md#kokerboom-follow-up--round-08-ph02-crown-comparison--2026-09-10). The original/cut meshes preserve vertex-mapped normals and polygon-corner UVs. The open boundary is not capped or relabelled an accepted socket. [PH02 derivative provenance](ASSET-CREDITS.md#ph02-open-crown-candidate) records the boundary and limits.

The independent scoped critic calls PH02 the stronger candidate and assigns **natural rosette 8.0/10 only**, with **no full-family mean**. Broad leaf bases, upright inner leaves, drooping outer leaves, teeth and ageing are useful source cues. No obvious new blade loss is visible in these views; retain the approximately **0.87×0.88 m original footprint**. This observation is not a semantic guarantee for every leaf or an accepted attachment.

Underside 05 remains open. Views 07/08 expose rim/cross-section and material mismatch: the 0.04 m overlap is not a weld. Further work should fit the irregular cut boundary and blend normals/materials without a collar, then re-inspect the underside. Cosmic coloration remains olive/gray with yellow edges. The scoped rosette score does not validate those joins, final palette, procedural variation, population suitability or the family gate.

The subsequent R09 hybrid experiment retains PH01 while isolating architecture/projection changes; PH02 is not substituted into that family. Its [source-identity diagnostic](../evidence/milestones/kokerboom/round-09/ph01-source-identity.json) matches position, normal and UV for all **94,176 extracted corners**, with zero opposite-normal or flipped-V matches. Tangents differ: this is a targeted tangent-preservation investigation, not evidence for flipping normals or UVs. The diagnostic alone does not establish R09 capture completion or visual acceptance.

## Round 09: hybrid family not accepted

The independent critic reviewed **all 21 actual Unity captures** and found the evidence sufficient for the six fixed criteria. The [render manifest](../evidence/milestones/kokerboom/round-09/metrics.json) records `2026-09-10T19:22:35.3021782Z`, technical capture checks passed, **0 errors and 1 warning**. [All 21 originals remain preserved](VISUAL-MILESTONES.md#kokerboom-follow-up--round-09-hybrid-family-not-accepted--2026-09-10); the [interactive review](KOKERBOOM-VISUAL-REVIEW.html) compares selected views with prior rounds. This family still uses PH01 source leaves; the scoped PH02 score is not included.

| Fixed criterion | Independent score / 10 |
|---|---:|
| Anatomy and silhouette | 7.0 |
| Joins and surface | 6.0 |
| Rosettes and leaves | 7.0 |
| Age and size variation | 5.5 |
| Palette and light identity | 5.5 |
| In-world readability | 5.5 |

**Botanical mean: 6.375. Art mean: 5.50. Overall: 5.50/10. Decision: REJECTED.** Both means must still reach 9.0 with no major defect. The independent findings recognize a broader crown, shorter terminal wood and repaired mapping. Remaining defects include geometric trunk bands, blurred bark, a pale bell-like juvenile, mechanical primary limbs and weak teal leaf mass. These are the recorded image-review findings, not scores inferred from technical checks.

The separate [R09 hybrid numerical report](../evidence/verified/kokerboom-round-09-hybrid-validation.json) passed **380 assertions** at `2026-09-10T19:29:03.5351796Z`, with independent regeneration. All nine inspected wood samples have one connected component and zero boundary edges, nonmanifold edges/vertices or inconsistent winding. Source-input hashes remained unchanged and `hybridCleanupComplete` is true. Report SHA256: `c2c837071f299483d4d466bf3491cae9c1c2902efb01bbaf49e99aa448c8dc70`. This report applies to the recorded R09 hybrid source; it does not override visual rejection or validate a later geometry/tangent change. Open source leaves are not subjected to the closed-wood test, and per-leaf dimensions remain outside this check.

The source-identity report matches P/N/UV for all 94,176 extracted corners while identifying tangent-basis differences. A separate A/D rosette comparison is prepared with current versus copied imported tuples, normal maps on/off, and conditional original imported subsets. Its numeric mapping record and actual images must establish the result; no tangent repair, visual improvement or production substitution is claimed here. Authoring mesh-build time and offscreen render/readback time are not gameplay FPS.

## Round 10: scoped PH01 tangent comparison, no meaningful visible gain

The independent critic reviewed **all 12 actual Unity images** of source rosettes A/D: current extractor, copied imported tuples, and verified original imported triangle subsets, each with the source normal map on/off. The [manifest](../evidence/milestones/kokerboom/round-10/metrics.json) records `2026-09-10T19:41:27.9590145Z`, technical capture checks passed, zero errors and one warning. [All 12 originals are preserved](VISUAL-MILESTONES.md#kokerboom-follow-up--round-10-ph01-tangent-comparison--2026-09-10).

**Independent scoped finding: no meaningful visible gain from the imported tangents.** Map-on/off comparisons retain the sparse silhouette, open centres, dark inner faces and olive/yellow appearance; differences are subtle. A in 05/06 remains sparse with basal gaps. D in 11/12 is fuller but still open. This review assigns no new family mean and does not justify substituting the adapter into the family as a visual fix. **R09 remains rejected at overall 5.50/10.** The next comparison should hold branch architecture fixed while comparing PH01 with fitted PH02, followed by material tuning supported by actual images.

The [actual adapter report](../evidence/milestones/kokerboom/round-10/ph01-imported-tuple-checks.json) separately passed mapping/readback checks for all **94,176 corners**, with zero unmatched or ambiguous corners. Exact-tuple deduplication was disabled. Actual imported triangle subsets passed count/coverage checks and were included for A/D. This validates the controlled attribute-copy operation, not botanical or visual improvement.

The [independent PNG difference measurements](../evidence/verified/kokerboom-round-10-image-differences.json) compare 14 pairs after checking identical stored cameras, lights and ambient settings. Current versus imported tuples with normal maps on has full-frame mean absolute RGB differences of **0.002476 for A** and **0.007126 for D**, on the encoded 0–255 channel scale. Pixels changing by at least two channel codes are **0.009010% for A** and **0.028906% for D**. These are unmasked whole-image numeric differences, not linear-light, perceptual or acceptance scores; original PNGs remain unchanged.

## Round 11: scoped wood diagnostic, taper repair retained

The independent critic reviewed all **three actual Unity wood views**, using the main PH01 hybrid specimen at seed 4242, age 1, LOD 0 and unit scale. The [manifest](../evidence/milestones/kokerboom/round-11/metrics.json) records `2026-09-10T19:54:20.6756293Z`, technical capture checks passed, zero errors and one warning. [The three originals are preserved](VISUAL-MILESTONES.md#kokerboom-follow-up--round-11-scoped-wood-diagnostic--2026-09-10).

**Independent scoped finding: bands are visibly gone in 04/15; retain the successful taper repair.** The basal bell/abrupt shoulder persists in 05 and the lower parts of 04/15. Forks are smoothly continuous but slightly pinched, primary limbs remain straight/uniform, and the bark is soft with weak peeling. No full-family score was assigned; R09 remains rejected at overall 5.50.

The [camera comparison](../evidence/verified/kokerboom-round-11-camera-comparison.json) verifies identical position, rotation, orthographic extent, clip values, image dimensions, lights and ambient values against the R09 shots. The only stored camera-field difference is shot 15's field of view, 52 to 60 degrees, which is inactive for its orthographic projection. This comparison checks recorded setup equality; it does not award visual acceptance.

## Round 12: fitted-support readback failure before images

The [failed R12 manifest](../evidence/milestones/kokerboom/round-12/metrics.json) records `2026-09-10T19:55:03.9959135Z`: **zero images, one error, zero warnings**. [The actual Create/readback report](../evidence/milestones/kokerboom/round-12/ph02-fitted-support-checks.json) remains unchanged. Candidate creation passed its checks, retained an unchanged crown hash and reported zero top position/normal/UV/tangent gaps. The independent harness readback failed: 171 source tuples versus 137 support tuples, with 34 missing source tuples. There is no image review or score for this run.

The [managed CPU diagnosis](../evidence/verified/ph02-boundary-selection-diagnosis.json) verified the selection error using actual source clipping buffers: 172 corner indices touch the cut plane, but only 138 are endpoints of its 69 boundary edges. The other 34 belong to triangles with only one cut-plane corner, produced by triangulating clipped quads; each matches boundary position/normal/UV while remaining a separate triangle corner. The old harness incorrectly included their triangle-specific tangent tuples in the seam coverage requirement.

The harness repair selects geometric single-incidence cut edges and retains **exact** endpoint P/N/UV/tangent4 coverage. It additionally requires all 69 support edges to reverse their corresponding source edges, with zero mismatched attributes and zero rim blend weight. Excluded non-boundary corner counts remain in the report. Geometry, shader, crown data and equality tolerances are unchanged. A new R13 run is required for actual corrected readback and images; this diagnosis does not claim that result or erase the R12 failure.

## Round 13: scoped fitted attachment improved

All **12 actual Unity views** completed after the boundary-selector repair. The [manifest](../evidence/milestones/kokerboom/round-13/metrics.json) records `2026-09-10T20:01:30.0879097Z`, technical checks passed, zero errors and one warning. [All originals are preserved](VISUAL-MILESTONES.md#kokerboom-follow-up--round-13-fitted-attachment-improved--2026-09-10). The [actual Create and readback report](../evidence/milestones/kokerboom/round-13/ph02-fitted-support-checks.json) verifies 69 source and 69 support boundary edges, exact reversed endpoint P/N/UV/tangent4, zero missing/unmatched rim tuples and zero rim blend. The 34 non-boundary plane-coincident corners remain reported separately. Crown hashes are unchanged.

**Independent scoped outcome: accept this as an improved attachment candidate for further comparison, with no full-family score.** Broad natural foliage is retained and the upper join is continuous without a lip. The lower tube remains open and needs measured overlap into an actual branch. Source-to-pale detail loss and olive/dark cosmic leaves remain unresolved. This is not acceptance of a complete tree, lower branch join, final palette or runtime population; R09 remains the latest full-family review at rejected overall 5.50.

The raw clipped crown contains 213,621 corner vertices before the support. Repeating that mesh across a family needs measured vertex reduction and runtime evidence. An optional imported-tuple path preserves complete attributes with bitwise exact deduplication, fits an owned crown clone, and reuses the R13 cameras for a separate controlled run. Its actual importer mapping, expanded Mesh hash and boundary checks must pass; synthetic CPU fixtures alone do not establish the actual reduction or appearance.

## Round 14: scoped bark sampling revision

The [R14 manifest](../evidence/milestones/kokerboom/round-14/metrics.json) records three actual wood views at `2026-09-10T20:05:46.8004775Z`, technical checks passed, zero errors and one warning. [The three images are preserved](VISUAL-MILESTONES.md#kokerboom-follow-up--round-14-scoped-bark-sampling--2026-09-10). The independent critic retains the clearer bark sampling. Repeated hooked scars, weak raised peeling and smoothing on the pale upper wood remain unresolved. This is a scoped surface review with no new full-family score; R09 remains rejected at 5.50.

## Round 15: actual imported PH02 tuple reduction verified

The optional imported-tuple path completed all **12 actual Unity views**. The [manifest](../evidence/milestones/kokerboom/round-15/metrics.json) records `2026-09-10T20:14:32.3640308Z`, technical checks passed, zero errors and one warning. [All originals are preserved](VISUAL-MILESTONES.md#kokerboom-follow-up--round-15-imported-ph02-tuples--2026-09-10). This is a source-attribute/reduction comparison, not a new family review or an appearance-improvement claim.

The [actual adapter report](../evidence/milestones/kokerboom/round-15/ph02-imported-crown-checks.json) maps all **82,074 source triangles**, with zero unmatched/ambiguous triangles, and retains **71,207 clipped-crown triangles**. Exact deduplication reduces **213,621 expanded corners to 43,200 vertices: 79.777269% fewer vertices**. Expanded before/after hashes and actual Unity Mesh readback agree at `423dced590276ea61c471242b065180bfe928520bc5553b1fc604aa137dd8cf2`. This verifies the imported path's complete attribute preservation, not equality to the old recalculated tangent basis or a gameplay FPS improvement.

The [fitted-support report](../evidence/milestones/kokerboom/round-15/ph02-fitted-support-checks.json) verifies unchanged caller/clone hashes and all 69 reversed boundary edges with exact P/N/UV/tangent4. Imported tangents replace R13's recalculated per-triangle tangents: distinct rim tuples change from 137 to 70, and support vertices from 1,841 to 1,775. These differences explain why identical pixels are not expected. The [independent report comparison](../evidence/verified/kokerboom-round-15-comparison-checks.json) verifies retained R13 position/projection/light settings and records the small Euler-angle round-trip differences. Independent visual interpretation remains separate; no score is assigned here.

The next family setup uses a separately identified `citylife.aloidendron-dichotomum.v2-preview` profile. Its component checks do not validate the changed family. Full-family views and numeric/runtime checks must assess the actual lower joins, bases, age/seed variation, material treatment and population readability.

## Round 16: PH02 fitted-crown family rejected

**Independent full-family verdict: REJECTED.** The independent critic inspected all **21 actual Unity images** and supplied the scores below. The documentation and geometry authors do not self-score. The [render manifest](../evidence/milestones/kokerboom/round-16/metrics.json) records `2026-09-10T20:42:38.8895966Z`, Unity 6000.6.0f1 / URP / Direct3D11, all 21 expected captures, technical checks passed, **zero errors and one warning**. These remain offscreen specimen and populated-prototype views, not native-player or FPS evidence.

| Criterion | Score / 10 | Independent findings and image IDs |
|---|---:|---|
| Anatomy and silhouette | 7.5 | Retain the fuller PH02 rosettes and broader family silhouette. Pose/structural variation remains part of the next revision. 01–03, 07, 14. |
| Joins and surface | 5.0 | Contrasting support/wood bands and rims in 06/18/19; continuous forks but stepped bend shading in 04/15. Repeated hooked scars and weak peeling relief in 05/21. The dark bend patch persists in 01 versus shadow-disabled 10. |
| Rosettes and leaves | 8.0 | Fuller source-derived rosettes are the improvement to retain. Glaucous blue-green canopy identity remains absent under ordinary scene lighting. 06, 11–12, 16–17. |
| Age and size variation | 6.5 | Juvenile form improves in 13 but its support remains smooth. A cut edge/protruding nub remains in 20, and mature ground termination is abrupt in 21. 07/14 supply the age/seed evidence. |
| Palette and light identity | 5.5 | Albedo-only 16 is clearer but still olive. Stronger cool fill in 17 mainly lights wood; the required blue-green canopy remains unresolved. |
| In-world readability | 6.0 | Fuller foliage is visible in the populated views, but ordinary gameplay light does not establish the intended cool canopy and warm-trunk separation. 08–09, 16–17. |

**Botanical mean: 6.75. Art mean: 5.75. Overall: 5.75/10**, displayed as **5.8/10** at one decimal place. At the time, this replaced R09's 5.50 as the latest full-family result; the earlier review remains preserved. Both means remain below **9.0**, and major join/surface and ground-contact defects block acceptance. Numerical checks and scoped source studies are excluded from the visual arithmetic.

At the time of this review, the critic proposed a coherent revision retaining the fuller PH02 rosettes and addressing support/wood skin, bend shading, ground contact, blue-green foliage and varied bark/poses. R17/R18 record the subsequent scoped work. The latest user direction now defers remaining defects and ends polishing with the completed R19 review.

R16 uses `citylife.aloidendron-dichotomum.v2-preview`, with frozen geometry SHA256 `d051e8f9871fab0187fbd825af0dd98fe51d6e2f38b9c9c62ee3105c6fe1addf`. The [component/readback report](../evidence/milestones/kokerboom/round-16/ph02-family-component-checks.json) verifies the actual imported PH02 tuple mapping, owned-clone fit and rigid component frame: **44,975 vertices and 74,519 triangles**, native readback hash `8a84ee3756a74ff8848fb56853896321a70aadf3dffaede506da4e29d75388b3`, unchanged input hashes, and **foliage tint strength 0**. Crown/support indices remain separate; this is not a welded whole tree. [Derivative credits](ASSET-CREDITS.md#ph02-fitted-crown-family-r16) distinguish unchanged downloads from the generated family.

The mature seed 4242, age 1, LOD0 subject in the actual manifest has **61 crowns, 3,766,337 vertices and 6,591,379 triangles** across wood and foliage. The [sanitized authoring record](../evidence/verified/kokerboom-round-16-authoring-cost.json) records **517,901 ms (517.901 s)** for cold main-tree creation. This includes the Create wrapper's naming/position/registration work and is **authoring cost, not FPS**. Those counts and timings do not establish runtime suitability or isolate a bottleneck. The fresh [full-family numeric run failed at assertion 522](../evidence/verified/kokerboom-round-16-ph02-validation-failed.json): the full-age root violates the unchanged −1 m floor. R16 render bounds already show minimum Y = −1.0133981704711914 m. Ten wood topology inspections are single closed indexed manifolds, nine specimens are fully recorded, and independent regeneration passed. `ph02CleanupComplete` is true and all ten individual source hashes are unchanged in `finally`. `sourceInputsUnchanged` remains false because the aggregate end-of-run gate was not reached; the run is **FAIL**, not a partial pass. [The attempt summary](../evidence/verified/kokerboom-round-16-numeric-attempt.json) preserves these distinctions. **R09's 380 assertions and earlier 349-assertion passes do not validate this changed family.** The later timing-only instrumentation does not retroactively provide stage timings for R16.

All 21 originals are preserved, including actual lower crown-to-wood side/underside and juvenile/mature base diagnostics in 18–21. Those replace the PH01-only basal-component views in this new round; earlier files remain unchanged.

| ID | Preserved actual Unity image |
|---|---|
| 01 | [Neutral front](../evidence/milestones/kokerboom/round-16/2026-09-10-01-neutral-front.png) |
| 02 | [Neutral three quarter](../evidence/milestones/kokerboom/round-16/2026-09-10-02-neutral-three-quarter.png) |
| 03 | [Neutral rear](../evidence/milestones/kokerboom/round-16/2026-09-10-03-neutral-rear.png) |
| 04 | [Branch union closeup](../evidence/milestones/kokerboom/round-16/2026-09-10-04-branch-union-closeup.png) |
| 05 | [Trunk bark closeup](../evidence/milestones/kokerboom/round-16/2026-09-10-05-trunk-bark-closeup.png) |
| 06 | [Terminal rosette closeup](../evidence/milestones/kokerboom/round-16/2026-09-10-06-terminal-rosette-closeup.png) |
| 07 | [Age lineup metres](../evidence/milestones/kokerboom/round-16/2026-09-10-07-age-lineup-metres.png) |
| 08 | [Cosmic gameplay eye level](../evidence/milestones/kokerboom/round-16/2026-09-10-08-cosmic-gameplay-eye-level.png) |
| 09 | [Cosmic gameplay overlook](../evidence/milestones/kokerboom/round-16/2026-09-10-09-cosmic-gameplay-overlook.png) |
| 10 | [Neutral front no shadows](../evidence/milestones/kokerboom/round-16/2026-09-10-10-neutral-front-no-shadows.png) |
| 11 | [Isolated rosette above](../evidence/milestones/kokerboom/round-16/2026-09-10-11-isolated-rosette-above.png) |
| 12 | [Isolated rosette side](../evidence/milestones/kokerboom/round-16/2026-09-10-12-isolated-rosette-side.png) |
| 13 | [Juvenile closeup](../evidence/milestones/kokerboom/round-16/2026-09-10-13-juvenile-closeup.png) |
| 14 | [Adult seed variation](../evidence/milestones/kokerboom/round-16/2026-09-10-14-adult-seed-variation.png) |
| 15 | [Untextured branch union closeup](../evidence/milestones/kokerboom/round-16/2026-09-10-15-untextured-branch-union-closeup.png) |
| 16 | [Cosmic leaf albedo only](../evidence/milestones/kokerboom/round-16/2026-09-10-16-cosmic-leaf-albedo-only.png) |
| 17 | [Cosmic strong cool fill](../evidence/milestones/kokerboom/round-16/2026-09-10-17-cosmic-strong-cool-fill.png) |
| 18 | [Ph02 lower crown wood join side](../evidence/milestones/kokerboom/round-16/2026-09-10-18-ph02-lower-crown-wood-join-side.png) |
| 19 | [Ph02 lower crown wood join underside](../evidence/milestones/kokerboom/round-16/2026-09-10-19-ph02-lower-crown-wood-join-underside.png) |
| 20 | [Ph02 juvenile base](../evidence/milestones/kokerboom/round-16/2026-09-10-20-ph02-juvenile-base.png) |
| 21 | [Ph02 mature base second angle](../evidence/milestones/kokerboom/round-16/2026-09-10-21-ph02-mature-base-second-angle.png) |

## R17/R18 scoped pilots and current closeout

R17 and R18 each retained six actual views with technical capture checks passed, zero errors and one warning. **Neither has a full-family score.** R17 matches R16's recorded camera fields only in shot 18; all R18 fields differ by view. [Both six-view sets and their camera-delta reports](VISUAL-MILESTONES.md#scoped-tree-pilots--r17-and-r18--2026-09-10) are preserved. Matching view categories do not establish replayed cameras or matrix equivalence.

The [R18 numeric report](../evidence/verified/kokerboom-round-18-family-validation.json) passes 536 assertions with ten specimens/wood topology samples, independent regeneration, eleven unchanged source inputs and PH02 cleanup. This applies to the revised experimental source; it does not alter R16's visual score or erase its numeric failure. R19's final independent result is 7.375/10. Remaining defects are deferred under the [user-directed R06 baseline/freeze](TREE-BASELINE.md); no further corrective round is prescribed.

## Round 19 frozen family review

**Final independent result: 7.375/10 (7.4 at one decimal), rejected under the historical 9/10 rubric.** All 21 images were reviewed. Botanical mean **7.375**; art mean **7.5**. This frozen experimental candidate does not replace the user-approved R06 visual reference. Remaining defects are **deferred**; tree polishing is closed by user direction.

The [full capture manifest](../evidence/milestones/kokerboom/round-19/metrics.json) records `2026-09-10T21:38:24.4067079Z`, **21 actual Unity offscreen images**, technical capture checks passed, **zero errors and one warning**. This is the full PH02-family mode with foliage tint 1, not either six-view pilot. The [freeze record](../evidence/verified/starfall-tree-baseline.json) rechecks the nine frozen source files and ties this candidate to the separate [R18 numeric PASS, 536 assertions](../evidence/verified/kokerboom-round-18-family-validation.json). Numerical success does not award visual scores or validate a new player.

All [21 original images are catalogued](VISUAL-MILESTONES.md#round-19-frozen-family-review) and were inspected by the independent critic dispatched by the coordinating task. Scores below are that final review, not author self-scoring. The historical rubric is retained for evaluation, but the user has ended the polishing loop and removed it as a prerequisite for world work.

| Fixed criterion | Score / 10 | Independent finding and deferred defect |
|---|---|---|
| 1. Anatomy and silhouette | 7.5 | Recognizable broad crown; thick angular bends/internal openings and similar family branching remain |
| 2. Joins and surface | 7.0 | Alternating material bands removed; small rim-like junction at 18, stepped bend shading at 04/10/15 and repeated scars/weak peeling at 05/21 remain |
| 3. Rosettes and leaves | 8.0 | Fuller natural leaf form retained; repeated rosettes across juvenile/mature trees and uniform cyan weathering remain |
| 4. Age and size variation | 7.0 | Better juvenile stem and ground contact; no clear floating gap in 20/21, but family/rosette repetition remains |
| 5. Palette and light identity | 7.5 | Continuous blue-green foliage gained; uniform cyan weathering and bright wood remain |
| 6. In-world readability | 7.5 | Improved cool canopy identity; bright wood and foliage readability at distance remain imperfect |

Botanical mean `(7.5 + 7 + 8 + 7) / 4 = 7.375`; art mean `(7.5 + 7.5) / 2 = 7.5`; overall is the lower mean, **7.375**, displayed as **7.4**. This remains below the historical 9/10 bar. The prior R16 result of 5.75 remains preserved; numeric checks contribute no points.

**Disposition: frozen, defects deferred.** Retain the user-approved running R06 reference and its exact package. R19 remains the separately preserved source-only experiment. No new player, automatic promotion or further corrective round follows this review.

## Historical report template

Fill this only from an actual independent critique:

```text
Critique ID/date:
Critic identity or task:
Geometry version and source commit/digest:
Validation report and mesh hashes:
Evidence manifest and image IDs:

1. Anatomy and silhouette: UNVERIFIED / 10 — evidence, reason, remaining defect
2. Joins and surface: UNVERIFIED / 10 — evidence, reason, remaining defect
3. Rosettes and leaves: UNVERIFIED / 10 — evidence, reason, remaining defect
4. Age and size variation: UNVERIFIED / 10 — evidence, reason, remaining defect
5. Palette and light identity: UNVERIFIED / 10 — evidence, reason, remaining defect
6. In-world readability: UNVERIFIED / 10 — evidence, reason, remaining defect

Botanical mean: PENDING
Art mean: PENDING
Overall (lower mean): PENDING
Major defects: NOT YET ASSESSED
Required evidence complete: NOT YET VERIFIED
Decision: NOT YET REVIEWED
Remaining defects (deferred under the current freeze):
```

## Delivery boundary

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| R06 tree visual reference | **User approved; retained unchanged** | [Baseline and technical freeze](TREE-BASELINE.md) | Full native control, collision, performance and offline evidence remain separate |
| Fixed critic rubric | Historical evaluation retained | Full-family Rounds 01/02/04/07/09/16; scoped studies separate | No longer a world-work gate or requirement for further polishing |
| Revised PH02 numerical mesh checks | **R18 PASS, 536 assertions** | [Exact report](../evidence/verified/kokerboom-round-18-family-validation.json), [historical R16 failure](../evidence/verified/kokerboom-round-16-ph02-validation-failed.json) | Numerical scope only; no visual/native-runtime score |
| R19 experimental candidate | Full 21-view capture complete; **independent result 7.375/10, frozen** | [Closing review](#round-19-frozen-family-review) | Final judgement recorded; defects deferred; no automatic R06 promotion |
| Historical R16 full review | Rejected, overall 5.75 | Twenty-one images; botanical 6.75, art 5.75 | Preserved history; remaining defects deferred |
