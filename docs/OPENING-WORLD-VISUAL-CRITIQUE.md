# Panorama visual acceptance — minimum 7.3/10

User instruction, 2026-09-14: critique and improve the world against the attached
panorama until the review reaches at least 7.3/10. This is an additional visual
gate, not a replacement for the integrated runtime or user acceptance gates.

Reference: `evidence/references/opening-world-approved-panorama-20260914.png`.
Preserve the spacious canyon-to-sea gameplay layout; match the reference's visual
language rather than flattening the world into a non-playable backdrop.

## Initial review

Evidence inspected: `evidence/milestones/coastal/round-122/2026-09-14-01-coastal-side-composition.png`.
This is a retained rendered composition, not fresh eye-level player traversal.
Scores are subjective reviewer judgments against the reference, not measurements.

| Dimension | Weight | Score /10 | Observed gap |
|---|---:|---:|---|
| Composition and spacious layered depth | 20% | 5.0 | One central islet and huge nearby wall dominate; reference has broad shore, multiple depth layers and distant mesas |
| Cliff geology and rock materials | 20% | 3.5 | Smooth rounded wall with scribbled texture; reference has angular strata, ledges, erosion and strong shadow pockets |
| Water and shoreline | 20% | 4.0 | Turquoise hue is right, but surface is flat/opaque with little reflection, depth variation or bright ripple detail |
| Lighting, contrast and atmosphere | 15% | 4.0 | Flat diffuse scene lacks warm raking sunlight, dark rock contrast and distance separation |
| Vegetation and ground dressing | 15% | 5.0 | Recognizable Kookerboom, but pale trunk, clustered slab rocks and sparse tiny plants differ from warm trees and organic shore distribution |
| Giant, moons and sky | 10% | 6.0 | Giant establishes identity; banding, luminous edge and star-field contrast need refinement; moons not established by this view |

Weighted initial score: **4.45/10 — below target**. Do not reuse the earlier tree
score as a score for this world. Positive user feedback on the cave/environment
remains valid but does not imply this visual gate passed.

Independent second review of compiled `runtime-12` representative evidence:
composition 5.5, geology 3.8, water 3.8, lighting 3.8, vegetation 2.5, sky 5.2.
With the same weights, **4.10/10 — below target**. This is a different view set,
not a measured regression from 4.45. The reviewer identified largely bare slopes,
primitive activity props, flat cyan water/hard shore edges and rounded noisy
cliffs. The playable scale is useful progress but not visual resemblance. A
matched camera set remains required before comparing improvement between builds.

### First visual implementation pass: round 124

Inspected `evidence/milestones/coastal/round-124/2026-09-14-01-coastal-side-composition.png`
against the round-122 composition. Stepped cliff silhouette is more legible,
but the increased surface noise reads as scribbles, not convincing rock strata.
Water remains visually opaque cyan in this shot, with no recognizable submerged
bed or plants. Foothill decoration remains tiny/sparse, and the foreground rock
ring still reads as stacked slabs. This is a limited improvement, not a 7.3 pass;
no new aggregate score is assigned from this one editor-rendered view.

Before another broad polish pass, establish one clearly readable shallow-bed
view with submerged rock/plant detail and correctly masked caustics. Then refine
rock form and clustered dressing at visible gameplay scale. Preserve this failed
comparison and repeat the representative compiled-player set after rebuilding.

## Improvement order and stopping rule

### Clear shallow-water requirement

Detail references: `evidence/references/approved-water-shelves-20260914.png` and
`evidence/references/approved-underwater-light-20260914.png` (the user's repeated
underwater crops show the same visual direction). Judge bright animated ripple
highlights, bed-projected caustic light patterns, subtle shafts/attenuation,
submerged dark fractured shelves and sparse aquatic planting. These concept
cutaways guide appearance, not a requirement to render an unnatural sliced
surface from every gameplay camera. Actual above-water viewing must reveal the
bed convincingly; underwater views are supplemental, not a new swimming system.

User explicitly marks the crystalline light pattern on the surface and on the
shallow bed as critical. Distinguish surface reflection/highlights from refracted
caustics on submerged terrain and rocks. Both must be visible in moving player
evidence, with coherent scale, depth attenuation and no bright pattern projected
onto dry ground. An opaque surface pattern alone does not satisfy this gate.

User explicitly requests looking through the water to see the ground and small
rocks on the bed. The compiled player's shallow river/pool views must show actual
submerged terrain and grounded stones through the surface, not a painted opaque
texture. Use depth-dependent colour/attenuation so deeper water progressively
obscures the bottom. Keep surface ripples and reflections balanced with this
visibility, avoid dry-looking water, and verify both above-water oblique and
downward views in motion. Do not equate clear water with drinkable water; gameplay
freshwater/sea identity remains explicit. Additional aquatic creatures are not
implied by the user's informal mention of small things.

User subsequently explicitly added reef-like formations with plant life beneath
the clear water. Include submerged rocky shelves with clustered aquatic plants
in the coastal shallows, visually distinct from the freshwater riverbed and its
vegetation. This authorizes environmental dressing, not fish, harvesting or a
new ecology simulation. Validate submerged placement, visible underwater relief,
no above-water clipping, depth readability and frame-time impact in the player.

### Rocky foothill planting reference

Additional user close-up:
`evidence/references/approved-tree-bank-detail-20260914.png`. It clarifies warm
golden bark and visible root contact, dark fractured rather than slab-like rocks,
blue-green succulent clusters, and restrained pink-purple flowering patches in
rock pockets along the bank. Preserve irregular sandy gaps and tree scale/depth
variation. Use this as dressing/material/light reference; it does not revoke the
accepted tree topology or authorize another unbounded tree-remodelling loop.

User supplied a detail reference at
`evidence/references/approved-rocky-foothill-planting-20260914.png` and explicitly
called out plants decorating the rocky mountain feet. Match the visible pattern:
blue-green fleshy/spiky succulent rosettes and occasional taller flowering forms
clustered in sheltered pockets between dark broken rocks; scattered Kookerbooms
on successive foothill terraces; larger readable foreground silhouettes and
sparser smaller plants with distance. Leave warm sandy gaps rather than making
a uniform carpet or evenly spaced rows. These are visual forms, not verified
real-world species. Underwater vegetation is distinct from terrestrial planting.

Validate grounded roots on supported terrain, rock/mesh clearance, no floating
or buried plants, and unobstructed cave/food/traversal routes. Decorative plants
must remain visually distinguishable from edible berry bushes. Review close-up,
eye-level and wide views so distant specks cannot substitute for readable assets.

1. Give cliffs real angular stepped silhouettes, strata and shadow-catching
   breaks; distribute near/mid/far formations without compromising navigation.
2. Improve water depth colour, reflections/ripples and shoreline blending;
   retain explicit freshwater/sea gameplay identity independent of colour.
3. Add warm directional lighting, stronger rock contrast and atmospheric depth.
4. Refine shore rocks and vegetation distribution, then sky details.

Retain before/after shots from identical camera positions, plus eye-level river,
cave-approach and ocean-opening views in the actual compiled player. Record exact
build/source, per-dimension scores, weighted total, score delta and effort per
pass. Review the weakest views, not only one flattering angle. A 7.3 score requires
the representative view set; no blank frames, buried plants, broken collisions or
unreachable refuge can be averaged away. Re-run relevant runtime regressions.

If a pass barely improves the result, reassess the technique and report the
plateau rather than endlessly polishing or lowering the bar. Do not declare
completion until the visual bar and the user's final play review both pass.
# Round 128 targeted comparison — 14 September 2026

Root inspected the matching `2026-09-14-03-rocky-tree-bank.png` in coastal
rounds 127 and 128. Removing duplicate alpha transmission visibly reduces the
gray-green cast. This is a narrow improvement, not an overall score or acceptance.
Water remains mostly flat teal with oversized light blobs; this view does not
prove a readable reef bed or fine moving caustics. Broad slab-like rocks, pale
trunk lighting and noisy cliff lines remain unlike the approved panorama.
Next evidence must include an eye-level shallow-bed view and moving light on
submerged surfaces, plus a matched wide composition. The 7.3/10 gate remains open.
# Runtime 17 direct image review

Independent inspection of `evidence/local/combined/runtime-17/01c-readable-berry-bush.png`
finds recognizable red fruit, but the leaves still read as glossy repeated
capsules rather than an organic sourfig-inspired succulent. The plant is small
in the frame and partly under an oversized flat rock's shadow. This is not a
visual pass: require tapered fleshy leaf forms, varied grounded growth and a
clear close-up plus ordinary walking-distance view. Preserve the food identity
and gathering behavior while changing the mesh/material.

`08-clothing-Sit-1.png` shows covered trousers from the front. This one view does
not clear the full movement/orbit gate or club grip. The cave furnishings and
walls still have conspicuously primitive geometry; automated refuge traversal
is separate from the panorama-quality requirement. No revised numeric score
is assigned from these two images alone.
