# Kooker: Starfall — living sea plan

Later lifecycle requirement, recorded 13 September 2026: the [save and world lifecycle contract](SAVE-GAME-CONTRACT.md) governs persistent world/animal identities and any future authoritative food/ecology state. The original bounded visual-population scope below remains a historical design; it does not implement a saved ecosystem or food chain.

Design record: **10 September 2026**. **Every feature and acceptance check below is planned and unimplemented in the current Starfall preview.** This document prepares the user's request, **“Sea with life in it”**. It authorizes no world changes before the tree acceptance gate. It contains no visual score or implementation-pass claim.

The current preview remains the approximately 60 m tree and blue-giant study described in [STARFALL.md](STARFALL.md). This plan adds a later coastal slice to the new world direction; it does not reinterpret the earlier island as the requested landscape.

## Reference and intended experience

The user-supplied panoramas show warm desert and rocky shores above bright turquoise water, with a blue gas giant and a visible galaxy. Below the surface are sculpted rock shelves and arches, upright aquatic plants, coral-like clusters, small fish schools, larger fish and a prominent ray-like swimmer. Keep clear swimming space between populated ledges so that depth, scale and animal movement remain readable.

The primary composition reference is attachment **06-world-panorama-high-resolution**, 6400 × 2880, SHA256 `054709501737b038f26aad0d1ef5a846f3955f44efc7bdf3b5d2b2cc41a8e8ec`. Its local copy was visually inspected and the hash checked for this plan. Attachments 04/05 supply the same above/below-water direction at lower resolution; exact identifiers and the local manifest location are recorded in [KOKERBOOM-REFERENCE.md](KOKERBOOM-REFERENCE.md#six-additional-user-references-inspected-10-september-2026).

These attachments are user design references, not instructions embedded in an image, scientific evidence, game textures or licence grants. No artwork is imported or redistributed by this plan. Future asset imports require their own verified provenance and licences.

## First coastal slice — all planned

The first review should cover a continuous shore-to-seabed route, rather than a full region. Proposed initial dimensions are roughly 120 m along shore, a 60 m bay and shelves descending from ankle-deep water to approximately 15 m. These are adjustable design dimensions, not measurements from the artwork or the final world's extent. Record the chosen dimensions before implementation and capture.

| Feature | Planned requirement | Status |
|---|---|---|
| Shore and seabed | Warm sculpted rock continuing through a turquoise shallows zone into cooler submerged shelves. Include sand pockets and one navigable arch with visible thickness, underside and supports. | Planned / unimplemented |
| Water above and below | Small readable waves, depth-dependent colour and transmission, restrained reflection, refraction and light absorption. Render the surface correctly from both sides. | Planned / unimplemented |
| Aquatic growth | At least three distinguishable visual families: ribbon/kelp-like stands, branching or plate-like coral forms, and low anemone-like clusters. Anchor them to actual ledges; leave open sand and swimming corridors. These are art families, not a claim about species or their natural coexistence. | Planned / unimplemented |
| Schools and larger fish | Three small schools with different routes, spacing and size ranges, plus a few larger swimmers. An initial budget of 24–48 fish per school is a proposal, to be measured and adjusted openly. | Planned / unimplemented |
| Rays | At least one ray with a readable body, tail, underside and moving fins. A second may be added only if the population and performance budget support it. | Planned / unimplemented |
| Local motion | Plant sway rooted at the base; fish tail/body motion; ray fin motion; bounded movement around ledges. Avoid synchronized copies and immediate visible loop resets. | Planned / unimplemented |
| Population scope | A bounded animated population with obstacle avoidance and local routes. Breeding, feeding chains, persistent animal lives and ecosystem simulation are outside this first slice. | Planned / unimplemented |

Use actual metre dimensions in asset reports. Initial art targets are small fish around 0.10–0.25 m long, larger fish around 0.4–0.8 m, a ray around 1.5–2.5 m across and kelp stands around 1–3 m tall. These are review targets, not biological measurements. A temporary one-metre scale reference in inspection views must expose accidental scaling before final gameplay captures.

## Walk into the sea and return — planned flow

1. Start on dry ground at the normal 1.85 m walking eye height. The user can see a continuous path into the bay and a landmark on the return shore.
2. Walk into shallow water with the same WASD and right-mouse look controls. Ground collision and clearance continue through wading depth. The visible surface and depth calculation must use the same water-height function.
3. Enter swimming when the body becomes sufficiently immersed. Use a documented depth threshold with hysteresis so small waves do not alternate walk/swim every frame. Show the current mode briefly. There must be no teleport, camera snap or unexpected pointer capture.
4. Swim forward, turn, descend with Q and rise with E; Shift changes speed. Hold right mouse to look and release it or press Escape to release the pointer. Explicit inspection flight remains a separately labelled mode, rather than the proof of swimming.
5. Cross the waterline, descend along the shelf, pass through the arch and stop beside aquatic growth. A swept body volume must respect the floor, arch underside, walls and exposed rocks. The single-height terrain floor alone cannot constrain a swimmer inside an arch.
6. Watch a school and a ray pass, return through the bay, surface and walk onto the same shore. Leaving the water restores grounded walking without a fall through the seabed or sudden placement on a ledge above the player.

This complete path needs an authorized native-input session in the actual new player, with retained input/mode/camera records. An automated route may provide reproducible scene and performance evidence, but it cannot establish that the keyboard/mouse flow works for the user. No native-input session or launch is performed by this design task.

## Water, light and space — all planned

The surface should reveal shallow ground without making every depth equally transparent. The deeper view should retain nearby rock, growth and swimmers while distant forms fade through water. Refraction must respond to camera angle and surface motion without stretching foreground objects into bands. At the waterline, inspect cameras just above, crossing and just below the actual surface: no missing horizon strip, black frame, doubled shoreline or one-sided disappearance is acceptable.

Caustics should move coherently across submerged ground and nearby objects, weaken with depth or occlusion, and preserve their form. They must not illuminate dry cliffs or dominate every material. Inspect both light-facing and shaded ledges; increased fog, bloom or exposure must not be used to conceal missing geometry. Light shafts are optional after surface/depth readability works.

The reference's broad above/below-water composition guides the view, but an acceptance image must come from an actual 3D camera and water surface. A decorative cutaway or painted background does not prove waterline continuity, collision or the presence of underwater life.

On land, retain the accepted tree's scale, gold/ochre material hierarchy and cool foliage against navy sky. Integrate layered mesas and the coast into a continuous new landscape with its own outline. Keep the **large blue gas giant**, with readable curvature and cloud structure, and a galaxy band visible from the shore. **Earth is excluded.** Their light/reflections should support the warm land and turquoise sea without washing them out. Whether a visible sun is present remains a separate design decision; the gas giant is not automatically a light source. All landscape/sky integration described here is planned, not a claim about the current stage.

## Determinism and world separation — planned contract

Terrain shape, bathymetry, arch/rock geometry, static growth anchors and population spawn descriptors must belong to an explicit **new versioned world definition**. Pin a seed, generator/content versions, physical units and relevant settings; do not invent the final ID until those are chosen. Stable chunk/feature IDs and per-feature random streams must make generation independent of visit order and render timing.

A height field can describe shelves and bay depth. Overhangs and traversable arches require additional 3D geometry with matching collision. Include these features in the new world's version/fingerprint; they must not be an unrecorded decoration that changes the swimming route.

Separate reproducible spawn descriptors from local visual time. Plant phase, fish movement and ray fin cycles may advance locally without modifying terrain, spawn identity or a saved base. Evidence must record the animation time and local motion seed when reproducing a frame. Reloading or reversing chunk visit order should reproduce the static placement, not pretend that every animated animal occupies the same point at arbitrary wall-clock times.

The earlier island definition, namespace/source paths and saved edits remain intact. Do not overwrite its resource definition, silently regenerate an old world, apply old deltas to the new base or reuse its save identity. Any later persistent edits to Starfall need an explicit compatible contract; this first slice does not add such tools. See the preserved [world foundation](WORLD-FOUNDATION.md) for the existing separation rules.

## Culling, detail and performance — planned checks

Use spatial cells for static growth and bounded active areas for swimmers. Cull by view and distance with a margin around the camera; keep motion/spawn identity stable across activation. Lower detail should retain ray silhouette, school direction and kelp mass. Reduce fine fronds and small fish detail before whole populations disappear. Check the arch from both sides and underwater: surface terrain culling and edge skirts must not expose gaps below it.

Pool active swimmers and avoid allocation every frame. Use coarse obstacle volumes or another measured bounded avoidance method before considering individual rigid-body simulation. Share repeated geometry/materials where appropriate. Record actual active/resident counts and transparent overdraw; a low triangle count alone is insufficient for layered water and plants.

Proposed performance target: **60 frames/s at 1600 × 900 on a declared test machine and quality preset**, with a 16.7 ms frame budget. This is a planning target, not a delivered capability. Freeze the target before implementation review; report a miss rather than silently lowering it.

Measure at least 10 seconds of warmup followed by 30 seconds each at the shore, waterline, dense underwater area and moving return route. Report frame-time median, p95 and p99; CPU/GPU timings where available; resolution; render scale; hardware/API; quality; draw/triangle counts; live animals/plants; managed allocations; resident memory; and loading/culling hitches. If GPU timing is unavailable, say so. Compare a documented reduced-content baseline in the same scene/build. Screenshot readback time belongs in a separate capture measurement and must not be presented as normal gameplay FPS. Offscreen GPU route timing and native presentation timing remain separate evidence.

## Future acceptance evidence — all pending

Use dated, non-overwriting folders such as `evidence/milestones/living-sea/round-NN/`. The names below are proposed evidence IDs; no files are claimed to exist. Preserve full-resolution originals and a manifest with world/asset versions, seed, build/source hashes, camera pose/FOV, water height, animation time, light/exposure settings and content counts.

| ID | Required future view or run | Acceptance observation | Status |
|---|---|---|---|
| S01 shore approach | Actual 1.85 m walking view plus continuous approach recording | Accepted trees, mesas, galaxy, blue giant and reachable turquoise water share a readable landscape at metre scale. | Planned / pending |
| S02 waterline crossing | Same path captured just above, through and below the surface | Stable walk/wade/swim transitions, coherent refraction and shoreline; no black frame or surface disappearance. | Planned / pending |
| S03 submerged overview | Ordinary underwater swimming camera looking across shelves and up toward the surface | Depth colour, caustics, inhabited ledges and open space remain readable; actual geometry exists below the surface. | Planned / pending |
| S04 arch traversal | Approach, interior and exit views on one continuous route | Arch thickness, underside, collision and floor clearance work from both directions. | Planned / pending |
| S05 aquatic growth | Close and medium views with temporary one-metre scale marker | Three distinguishable forms, anchored bases, visible volume and motion that does not detach roots. | Planned / pending |
| S06 schools | Time-separated frames and a 15-second fixed-camera clip | Different school routes and scale; coherent movement, obstacle clearance and no obvious synchronized reset. | Planned / pending |
| S07 ray | Side, front/three-quarter and underside views plus a 15-second clip | Readable body/fin volume and scale, smooth swimming motion and clearance from ground/plants. | Planned / pending |
| S08 return and detail changes | Continuous return to dry shore, including near/far population views | User controls complete the round trip; no abrupt LOD disappearance, stranded swim state or ground penetration. | Planned / pending |
| S09 technical record | Deterministic regeneration, route log and timing JSON | Static hashes match across visit order; no render errors; body clearance, bounded populations and measured performance recorded. | Planned / pending |

Frame validity checks must reject empty/near-uniform captures and rendering exceptions. Native route evidence must identify actual input events; automatic input must be labelled. Missing views or unobserved behaviour remain unverified. The designated reviewer judges the result from the retained images and movement evidence; this document awards no score and does not replace the tree acceptance process.

## Implementation order and evidence boundary

The user-directed [R06 tree baseline and freeze](TREE-BASELINE.md) replaces the previous tree-score prerequisite. The following is later world direction, not further work in this closeout: prepare a new versioned coastal slice and collision, then waterline/swimming, anchored growth and moving populations, and the complete sky/land/sea composition and performance pass. Keep each stage independently inspectable. No current code, scene, build, world definition, save or running game was changed to prepare this document.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| User's living-sea direction is recorded | Verified reference review | Explicit request and the inspected 6400 × 2880 attachment/hash above | Asset choices and independent implementation review |
| Feature boundaries and acceptance route are specified | Design only | This document | Tree gate, implementation and all S01–S09 evidence |
| Sea, swimming and aquatic life work in Starfall | Unimplemented / unverified | No runtime evidence claimed | Actual scene, controls, deterministic checks and measured performance |
