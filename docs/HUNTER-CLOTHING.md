# Hunter clothing component

Compatibility base: `75fc4cb`, retained Quaternius Superhero male, Humanoid avatar and UAL1 clips. The revised hunter has full-length inner trousers beneath short split waist panels, a sleeveless vest, heavy cord belt, pouch and foot wraps. The copied body has a small local bulk/jaw adjustment and the model scales to (1.15, 1, 1.08). Source FBX files remain unchanged. Hood and short mantle are optional, off by default; weather activation is not wired. A passive left-hand wooden club leaves the right-hand pickup free. No hunting, combat, scavenging or tanning mechanic is added.

## Provenance

The fitted vest, trouser, foot-wrap and cold-layer shells derive from the retained CC0 body geometry and weights; waist panels, belt, stitches, pouch, wooden club and deterministic material grain are authored locally in `HunterOutfitAuthoring.cs`. No candidate clothing pack was acquired. Existing body, texture and animation files were checked against `CHARACTER-ASSET-REVISION.json`; all five SHA256 values matched. Retained `BASE-LICENSE.txt` and `ANIMATION-LICENSE.txt` state CC0 1.0. Keep these notices and the existing provenance manifest with distribution. Locally authored code and garment additions belong to this project; no new third-party clothing restriction is introduced. This does not relicense the repository as a whole.

## Attachment contract

Call `HunterOutfitAuthoring.Attach(model, outputAssetFolder)` once in an isolated editor build while the retained imported model is in its bind pose. Call AFTER the existing body/eye/brow material assignment loop, and before animation evaluation. The generator creates a visual child, uses the same bone transforms, exact source mesh bindposes and renderer coordinate space, and masks covered faces only on a cloned mesh. Then apply the model scale from `RunHunter`. It never modifies the source FBX, actor root, controller, hand socket or original avatar. It does not add an Animator, physics or network state. Assign layer 9 to the generated hierarchy as in `RunHunter`.

`HunterClubCarry` updates only the visual prop in LateUpdate, using the left palm and a downward/outward direction with clearance from the actor's ground datum. It adds no collider or combat behavior. On uneven terrain the main-world integration must supply/validate actual ground clearance; the current evidence is a flat courtyard. The right hand and its existing delivery logic are untouched.

The test controls are a separate `HunterPreview` component and MUST NOT be installed in the main-world actor. The preview adds crouch, sit and pickup test states to a new generated controller; the original controller states remain intact. The authoring function currently targets the inspected body proportions and must reject/review any body or bind-pose substitution.

## Separate preview

Run `tools/build-hunter.ps1` using pinned Unity 6000.6.0f1. It refuses competing Unity editors and creates a timestamped `Builds/KookerStarfallHunter-*` folder. `-Verify` runs the opt-in standalone capture sequence in a visible native window; hidden-window ScreenCapture produced black frames and is not valid evidence. F8 saves an in-player screenshot. V resumes manual walking; C/X select crouch/crouch walk; T sits/stands; P plays pickup; R runs the existing carry/delivery route; H toggles optional cold layers; 1/2/3 select front/side/back; right mouse orbits.

The preview is the isolated character courtyard, not the coastal main-world build. The main-world owner is preparing a new isolated coastal/environment/inhabitant candidate and has requested a passed clean clothing revision. Integration belongs to that owner; do not replace the current playable world with this courtyard. Import authoring/component code first, attach at the above seam, omit all preview controls/test stage, and repeat coverage tests in the actual integrated camera/terrain/actions.

## Accepted isolated checkpoint — 13 September 2026

Earlier panel-only builds failed pelvis coverage and remain preserved. The user accepted the revised exposed-shoulder silhouette and requested only functional closure. The final compiled source is `8659a92dcdaa8f657f62027a91e73211af3b9679`; the separate executable is `Builds/KookerStarfallHunter-0.0.3-preview.1-20260913-180511/KookerStarfallHunter.exe`. Keep its whole build folder. [Release record and file hashes](../evidence/verified/hunter-clothing-release.json).

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Continuous pelvis/seat coverage | Passed reviewed isolated sequences | [Seated front](../evidence/milestones/hunter-clothing/2026-09-13/seated-default-180-Sit-20.png), [sit-down side](../evidence/milestones/hunter-clothing/2026-09-13/seated-default-90-SitEnter-20.png), [stand-up rear quarter](../evidence/milestones/hunter-clothing/2026-09-13/seated-default-25-SitExit-20.png), [seated orbit](../evidence/milestones/hunter-clothing/2026-09-13/orbit-default-300.png) | Repeat on integrated terrain, seats, camera and changed animations |
| Walking, crouch, pickup/carry, sitting and both 360-degree seated camera orbits | Passed isolated motion/visual review | [Runtime report](../evidence/milestones/hunter-clothing/2026-09-13/runtime-report.json): 3,184 audited frames, 185 nonblack captures, carrying/delivered true, no runtime errors | Arbitrary poses and future actions unverified |
| Club clearance | Passed flat-ground test | Minimum rendered club bounds clearance 0.135669 m; left palm attachment, right-hand delivery retained | Terrain-aware clearance and future hand poses |
| Compiled preview | Passed | Unity 6000.6.0f1: zero build errors/warnings; 2,841 IslandValidation assertions; [manifest](../evidence/verified/hunter-clothing-release.json) | Courtyard is not the main world |
| Manual native shortcuts | Unverified | Native window observed; computer-use T keypresses did not produce observable transitions | Human/manual input acceptance |
| Optional cold layers | Motion reviewed, off by default | [Cold seated](../evidence/milestones/hunter-clothing/2026-09-13/seated-cold-180-Sit-20.png), [cold orbit](../evidence/milestones/hunter-clothing/2026-09-13/orbit-cold-120.png) | Weather/temperature integration not implemented |

Final counts: 8,414 base garment triangles (21,841 vertices), 10,766 with optional cold layers (28,897 vertices), and a 260-triangle club. Masking omits 4,520 covered body triangles on a clone: net base-plus-club increase is 4,154 triangles. Five base garment renderers, two optional cold renderers, one static prop renderer and five authored garment/prop materials. The earlier 5,000-triangle/two-material concept target is not met by expanded layers; sustained FPS and multi-inhabitant profiling are unverified.

[Cropped comparison sheet](../evidence/milestones/hunter-clothing/2026-09-13/contact-sheet.png) is a review aid; fourteen full original player captures and a hash catalogue are retained beside it. The complete 185-capture run remains in ignored `evidence/local/hunter/runtime-07`; its catalogue records every image hash. No generated concept image is presented as runtime evidence.

Acceptance requires walking, crouching, sitting transitions, pickup and actual carry/delivery, with front/back/side captures and scrutiny of panel overlap, neckline, underarms, hips and feet. Valid finite mesh bounds only detect catastrophic deformation, not clipping. No claims of cold/wet protection or main-world integration are made.
