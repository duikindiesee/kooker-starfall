# Kooker: Starfall

*Where the sky becomes a world.*

Beneath a blue giant and a river of stars, warm desert gives way to luminous seas. Explore, shape, and one day inhabit a world still becoming.

[Project home](../README.md) · [Visual milestones](VISUAL-MILESTONES.md) · [Living sea plan](STARFALL-LIVING-SEA.md) · [Asset credits](ASSET-CREDITS.md)

![Actual R06 Unity offscreen study associated with the retained preview](../evidence/milestones/kokerboom/round-06/2026-09-10-01-preview-eye-level.png)

*Actual R06 Unity offscreen study, 10 September 2026. The running R06 tree is now the user-approved visual reference; this image is its offscreen study, not a newly captured native frame. See the [baseline and freeze](TREE-BASELINE.md).*

## Current scope

The preserved R06 standalone preview is an approximately 60 m baked tree and blue-giant study with a simple ground patch, rocks and walk/fly controls. Its tree is the user-approved visual reference; the later R19 offscreen experiment is separately frozen. The wider sculpted terrain, galaxy and living sea remain later direction; they are not completed features of this player or extra work in the tree closeout. World shaping, houses, building tools, bots, inhabitants and shared-world connections remain future integrations.

The preview is intended as a local study and does not generate the earlier island. The preview has no implemented multiplayer, bot connections or saved-world loading. Offline operation and engine telemetry have not been fully validated. The earlier foundation and its instructions remain documented in [LEGACY-ISLAND.md](LEGACY-ISLAND.md).

## The world ahead — planned

Warm sculpted mesas and rocky shores will frame turquoise bays in a larger continuous landscape. The new region needs its own outline, seed and versioned world definition; it will not overwrite the earlier island or reinterpret its saved edits. The final extent has not been chosen, and an infinite world is not promised.

Above it, a large **blue gas giant** and a visible galaxy will establish the sky. Earth is excluded. The intended contrast is warm gold/ochre land and trees against cool foliage, water and navy space; the final visible-sun arrangement remains a separate design choice. The current prototype giant and stars do not establish a finished sky.

Below the surface, the [living sea plan](STARFALL-LIVING-SEA.md) calls for readable shallows, sculpted seabed shelves and arches, kelp-like growth, coral/anemone-like forms, varied fish schools and rays. Its walk-to-water, swim and return route is planned and unimplemented. This first sea slice proposes a bounded animated population, not an ecosystem simulation. Houses, building/shaping tools, inhabitants, bots and shared-world connections also remain future work.

The [reference record](KOKERBOOM-REFERENCE.md) identifies the user's concept attachments separately from actual Unity images. Those references guide composition and colour; they are not evidence of working features or licence grants for imported assets.

## Current evidence

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Visual reference and closeout | **R06 user approved**; tree iteration frozen after R19 review | [Tree baseline](TREE-BASELINE.md) | R19 remains a separate experiment; defects deferred; no 9/10 world-work prerequisite |
| Revised experimental source | **R18 PASS, 536 assertions** | [R18 numeric report](../evidence/verified/kokerboom-round-18-family-validation.json), [historical R16 failure](../evidence/verified/kokerboom-round-16-ph02-validation-failed.json) | R19 full review recorded at 7.375/10; frozen; numeric checks do not establish visual or native-player acceptance |
| Separate Windows study | R06 build succeeded and local WIP was launched | [R06 build record](../evidence/milestones/kokerboom/round-06/preview-build.json) | Full native control, collision and performance acceptance |
| Wider world and living sea | Planned / unimplemented | [Living sea design and future acceptance route](STARFALL-LIVING-SEA.md) | Later direction outside this tree closeout; implementation and actual scene/input evidence remain future work |

The main R16 tree contains **6,591,379 triangles** and its cold editor creation took **517,901 ms** ([sanitized authoring record](../evidence/verified/kokerboom-round-16-authoring-cost.json)). This measures authoring cost, not FPS; stage attribution and sustained runtime suitability remain separate.

Open the [visual review catalogue](KOKERBOOM-VISUAL-REVIEW.html) locally in a browser to compare retained rounds. GitHub displays that HTML as source; use the [Markdown milestone index](VISUAL-MILESTONES.md) for navigation on GitHub. Render success and numerical passes do not change the independent visual decision.

## Rename record — 2026-09-10

The coordinating task verified the authorized GitHub rename below. These are the identifiers and review heads at that rename, not a claim about future branch movement. This branding change performs no further repository mutation.

| Claim | Status at rename | Evidence | Remaining gap |
|---|---|---|---|
| Repository renamed from `duikindiesee/citylife-unity` to `duikindiesee/kooker-starfall` | Verified by coordinating task | [Repository](https://github.com/duikindiesee/kooker-starfall); unchanged numeric repository ID `1360600603` | None for rename |
| Existing pull request retained | Open draft PR #1 | [Draft PR #1](https://github.com/duikindiesee/kooker-starfall/pull/1); head `0d4291b`, base `0e6544e` at rename | New source work is not implied to be pushed, merged or accepted |
| Local remote points to renamed repository | Read directly during branding work | `git remote -v`: fetch/push `https://github.com/duikindiesee/kooker-starfall.git` | Existing filesystem checkout paths stay unchanged |
| Preview branding updated | Source only; unbuilt | [HUD source](../Assets/CityLife/Scripts/CosmicPreviewExplorer.cs), [preview build source](../Assets/CityLife/Editor/CosmicPreviewBuild.cs) | Next coordinated build and runtime verification |

The repository retains its history and review. Internal `CityLife.World` namespaces, `Assets/CityLife` paths, generator/world identifiers, legacy scene/build entry points, player-save contracts and local checkout directories are unchanged. The display rename is not a world migration. The legacy project company/product values are preserved; the separate preview build temporarily sets its own product and restores project settings in `finally`.

## Source branding and existing evidence

- Display name and HUD: **Kooker: Starfall**; the HUD continues to say **WIP local preview** and explicitly identifies terrain, galaxy and living sea as in development.
- Next-build Windows product: `Kooker Starfall`; executable: `Builds/KookerStarfall-<UTC>/KookerStarfall.exe`. The filesystem-safe product name omits the display colon.
- Internal class names, generated scene paths, command-line options, input logs and screenshot filename prefixes remain unchanged.
- The already-built [R06 preview](../evidence/milestones/kokerboom/round-06/preview-build.json) retains its earlier `Cosmic World Preview` product and `CosmicWorldPreview.exe`. Branding work did not restart, rebuild or alter that running player. The [checked R06 ZIP](../evidence/verified/starfall-r06-package-check.json) packages its 183 runtime files unchanged with three documentation files; it does not contain a newly built R16 family.
- R06 and all earlier inspection captures retain their original filenames and labels. The historical R16 PH02 inspection remains rejected under the critic rubric. [R19](VISUAL-MILESTONES.md#round-19-frozen-family-review) preserves 21 final experimental offscreen images, independently scored 7.375/10 and frozen with defects deferred; neither is a newly branded runtime or an automatic replacement for R06.

The previous README is retained in full below its historical notice in [LEGACY-ISLAND.md](LEGACY-ISLAND.md), with relative links adjusted for its new directory. Its island size, functionality and verification claims remain historical; they do not describe the small Starfall study.


## Knowledge progression and persistent death/return: design contract

The user-directed [knowledge progression rules](STARFALL-KNOWLEDGE-PROGRESSION.md) define naive starting knowledge, provenance/confidence/corrections, persistent-world death and return, recoverable inventory, at-most-one grounded lesson, privacy, and staged ecology/survival milestones. These are planned, not runtime-proven. Death lessons and post-return reflection remain after the current one-living-thought proof. Existing memory event schema v1 and old saves are unchanged.
