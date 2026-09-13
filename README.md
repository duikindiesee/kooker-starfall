<div align="center">

# ✦ Kooker: Starfall

**Where the sky becomes a world.**

A world in the making · Unity 6 · Windows preview

[Explore the direction](docs/STARFALL.md) · [Visual progress](docs/KOKERBOOM-VISUAL-REVIEW.html) · [Build & run](#-build--run) · [Credits](docs/ASSET-CREDITS.md)

</div>

![R06 Unity study corresponding to the retained visual reference](evidence/milestones/kokerboom/round-06/2026-09-10-01-preview-eye-level.png)

*Actual R06 Unity study capture, 10 September 2026 · WIP. The running R06 tree is the user-approved visual reference. This image is an offscreen study capture, not a new native screenshot or a finished world.*

Beneath a blue giant and a river of stars, warm desert gives way to luminous seas. Explore, shape, and one day inhabit a world still becoming.

**That is the destination.** This branch preserves the [first coastal scene review](docs/COASTAL-SLICE.md) and now adds a separate [bounded coastal player checkpoint](docs/COASTAL-PLAYABLE-CHECKPOINT.md): rocky tree bank, turquoise river/sea, canyon terrain and procedural galaxy in an actual built player. It is not yet the full panorama-scale world or living sea. World shaping, swimming, houses, building tools, inhabitants, bots and shared-world connections are future work.

## 🌌 The world ahead

| Element | Direction | Current state |
|---|---|---|
| Trees | Gold and ochre kokerboom trunks, rounded crowns and cool blue-green rosettes | R06 approved reference; R19 experiment frozen |
| Land | Warm sculpted mesas, rocky shores and turquoise bays | Bounded coastal scene review; no coastal player yet |
| Sky | A large blue gas giant, moons and a distant galaxy | Giant/stars in R19; procedural galaxy in coastal study |
| Sea | Clear shallows, submerged arches, kelp-like growth, coral forms, fish schools and rays | Surface colour study; underwater life and swimming unimplemented |
| Life and building | Explore, shape and eventually inhabit the world | Future scope |

The [visual direction](docs/KOKERBOOM-REFERENCE.md) records the concept references separately from actual Unity evidence. The new landscape will have its own versioned world definition, preserving the earlier island and its saved edits.

## 🌳 Tree baseline and preserved experiments

[![Actual Unity R16 neutral tree inspection, preserved historical experiment](evidence/milestones/kokerboom/round-16/2026-09-10-02-neutral-three-quarter.png)](docs/KOKERBOOM-VISUAL-REVIEW.html)

*Actual Unity R16 neutral inspection · WIP. Open the local [visual review catalogue](docs/KOKERBOOM-VISUAL-REVIEW.html) in a browser to compare preserved rounds and camera views. GitHub displays the HTML source; the [Markdown milestone index](docs/VISUAL-MILESTONES.md) is readable there directly.*

**The user approved R06 as the visual reference; the final numeric/full R19 review is complete and tree polishing is frozen.** R19 is a separately preserved experimental candidate, not an automatic player replacement. Remaining defects are deferred; the historical 9/10 critic target no longer blocks world work. See the [tree baseline and closeout](docs/TREE-BASELINE.md).

R16's historical full review remains **5.75/10, rejected under that rubric**; its numeric buried-root failure is preserved. R17/R18 are unscored six-view pilots. The revised R18 experimental source [passed 536 numeric assertions](evidence/verified/kokerboom-round-18-family-validation.json); the closing [R19 set contains all 21 actual offscreen views](docs/VISUAL-MILESTONES.md#round-19-frozen-family-review), with an independent **7.375/10 (7.4)** result, rejected under the historical rubric and frozen with defects deferred. These results do not change the retained R06 executable or establish native performance.

## 🎮 Build & run

The separate **0.0.2-preview.2** build adds a visible fullscreen/windowed button. Both button transitions were verified in the actual player, restoring its window size and preserving scene/player state. See [display release evidence and remaining checks](docs/RELEASE-0.0.2-preview.2.md). The earlier builds remain available and unchanged.

Use **Unity 6000.6.0f1** with Windows build support. URP **17.6.0** and Input System **1.20.0** are pinned in [Packages/manifest.json](Packages/manifest.json).

The R06 reference is preserved. The separate **0.0.2-preview.1 R19 player** has now been built, packaged and observed in its native window. See the [release notes, version mapping and checksums](docs/RELEASE-0.0.2-preview.1.md). To create another distinct R19 build from committed source:

~~~powershell
.\tools\render-kokerboom.ps1 -Round round-100 -R19PlayablePreview
~~~

Choose an unused round number; the label only identifies evidence and does not affect world generation. The script renders two study views and bakes a Windows player into `Builds/KookerStarfallR19-0.0.2-preview.2-<UTC>/`. It preserves earlier captures, refuses to run alongside another Unity editor and does not launch the player. Open `KookerStarfallR19.exe` from the resulting folder; keep the data folder and DLLs beside it.

The new player uses the frozen R19 PH02 family and explicit tint1. Its automatic player checks cover actual rendering, walking, ground clearance, flight, stage bounds and trunk collision. Native startup and the versioned HUD were observed after an authorized launch; the user explored without agent movement input. Full native controls, sustained performance and other-device execution remain unverified. R06 remains unchanged and separately available. The legacy `-PlayablePreview` route selects a different PH01 hybrid; it does not reproduce R06 or this R19 release.

| Control | Action |
|---|---|
| WASD | Move |
| Hold right mouse button | Look |
| Shift | Move faster |
| F | Switch walk / inspection flight |
| Q / E | Lower / raise flight height |
| Escape | Release the pointer |
| Alt + Enter | Toggle window / fullscreen |
| Display button at top right | Enter fullscreen / return to the remembered window size |

Walking follows the ground at 1.85 m eye clearance and stays within the study. This is an inspection controller; rocks do not yet have complete collision. F12 capture requires an explicit absolute folder passed with `-previewEvidence`; the preview does not capture automatically.

The preview has no implemented multiplayer, bot connections or saved-world loading. Offline operation and engine telemetry have not been fully validated. Legacy island build commands and controls remain in the [earlier island guide](docs/LEGACY-ISLAND.md).

## Save and new-game contract — planned

These are recorded requirements; complete-game save/load is not implemented or runtime-proven. The next implementation priority remains one genuine local-model thought.

| Operation | Contract |
|---|---|
| Save / Load | Bind the authoritative Unity world, NPC brains and the exact private/shared history to one versioned, validated checkpoint. Restore the whole checkpoint after quit/relaunch; refuse incompatible partial joins. |
| New Character | Keep the current world and seed; create a stable world-scoped inhabitant identity with its own private memory. |
| New World / New Game | Create a fresh save slot, unique world identity and generated seed, with separate inhabitants and history. A deliberately repeated seed still creates a different world. |
| Reset | Default to a fresh slot and preserve the old one. Explicit archive keeps the old world recoverable; deletion is a separate deliberate operation. |

The authoritative [save and world lifecycle contract](docs/SAVE-GAME-CONTRACT.md) defines checkpointing, event identity, isolation, migration, backup/export/import, corruption recovery and evidence required from the actual player. The [architecture map](docs/SYSTEM-ARCHITECTURE.md) marks this work as planned.

## Evidence at a glance

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Separate Windows study | R19 0.0.2-preview.1 built and packaged; native window observed | [Release record](evidence/verified/starfall-r19-release.json) | Native controls, rock collision and sustained performance |
| Visual reference and tree closeout | **R06 user approved**; frozen after the completed R19 review | [Baseline record](docs/TREE-BASELINE.md) | Experimental R19 is separate; remaining defects deferred, no further 9/10 polishing gate |
| Revised experimental PH02 source | **R18 numeric PASS, 536 assertions** | [Exact report](evidence/verified/kokerboom-round-18-family-validation.json) | All 21 R19 captures complete; independent result 7.375/10, frozen; R16 failure preserved; separate R19 player now recorded below |
| Starfall branding | Versioned R19 title and HUD observed | [Native window](evidence/milestones/starfall-r19-player/2026-09-10-native-r19-window.png) | None for naming |
| Coastal world study | Two comparison sets plus isolated Windows player smoke | [Review and gaps](docs/COASTAL-SLICE.md), [playable checkpoint](docs/COASTAL-PLAYABLE-CHECKPOINT.md) | Native user-input review, panorama-scale uplift and living sea |

## 🧭 Project guide

| Start here | Reference |
|---|---|
| World direction and naming | [Starfall](docs/STARFALL.md) · [Living sea](docs/STARFALL-LIVING-SEA.md) |
| System architecture and installation | [Components, authority/privacy boundaries and current setup status](docs/SYSTEM-ARCHITECTURE.md) |
| Images and review | [Visual catalogue](docs/KOKERBOOM-VISUAL-REVIEW.html) · [Milestones](docs/VISUAL-MILESTONES.md) · [Tree critique](docs/KOKERBOOM-CRITIQUE.md) |
| Assets and provenance | [Asset catalogue](docs/ASSET-CATALOGUE.md) · [Credits](docs/ASSET-CREDITS.md) · [Portable notices](Assets/CityLife/Art/THIRD-PARTY-NOTICES.txt) |
| Determinism and saved edits | [World foundation](docs/WORLD-FOUNDATION.md) · [Legacy island](docs/LEGACY-ISLAND.md) |
| Next save/new-game architectural gate | [Versioned slots, world-scoped identities, atomic checkpoints and recovery](docs/SAVE-GAME-CONTRACT.md) · Requirements only, after the living-thought priority |
| Native memory service | [Starfall memory: local ledger, episodes, wiki and sleep-gated dreams](docs/STARFALL-MEMORY.md) |
| Verification and contribution | [Evidence record](docs/VERIFICATION.md) · [CI and safeguards](docs/CI.md) |

The repository is [duikindiesee/kooker-starfall](https://github.com/duikindiesee/kooker-starfall). Its history and draft review continue under the new name. Internal `CityLife.World` namespaces, `Assets/CityLife` paths, legacy product settings, world IDs and save contracts remain intact; branding does not migrate an existing world.

Contribute through branches and pull requests. Retain provenance, licences and earlier evidence. Keep credentials, private runtime profiles, player state, downloaded archives and raw machine logs out of the public repository.

## Isolated first refuge test player

This branch adds [First Refuge v2](docs/FIRST-REFUGE.md): a separate Windows player with rock overhang, contained hearth, mat/rest/sleep and finite fuel storage. Forty scripted checks passed twice. Concept art and actual-player images are labelled separately; performance is borderline and inhabitant/memory integration remains pending. The historical milestones below retain their original scope.
