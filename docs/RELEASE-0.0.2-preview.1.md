# Starfall 0.0.2-preview.1 — separate R19 player

This Windows preview bakes the frozen R19 PH02 tree into the existing study stage. It has a distinct executable, product title, version and ZIP. The R06 reference and its running player remain preserved. This is integration and packaging, not another tree-polishing round or a wider-world release.

| Identity | Preserved R06 | New R19 preview |
|---|---|---|
| Release label | R06 WIP; historical builder configured `0.0.1-wip` | `0.0.2-preview.1` |
| Build ID | `CosmicPreview-20260910-185028` | `KookerStarfallR19-0.0.2-preview.1-20260910-223113` |
| Executable | `CosmicWorldPreview.exe` | `KookerStarfallR19.exe` |
| ZIP | `Kooker-Starfall-WIP-R06-Windows.zip` | `KookerStarfallR19-0.0.2-preview.1-20260910-223113-Windows.zip` |
| ZIP size | 161.42 MiB | 293.69 MiB |
| Source mapping | Historical source snapshot and baked scene in the [freeze record](../evidence/verified/starfall-tree-baseline.json); no exact clean Git build commit was recorded | `a8946a069a3ab3ae47f6b5c54ce1c6a7fd6c421c` |
| Visual status | User-approved reference | Frozen R19 experiment, critic result 7.375/10; defects deferred |

R19's frozen tree basis is `fc30b2857be419172e740f0d338d5913145d75fb`. Its geometry, component and appearance shader inputs were checked unchanged before baking. The seven retained frozen input hashes and actual component mesh hash protect that boundary. The build route selects PH02, seed4242 and foliage tint1 explicitly; the old `-PlayablePreview` route remains a different PH01 path.

SHA256 of the new ZIP:

```text
8a133417d788bc2c43287e947bccb030390c453f11c45609efe7650926df4804
```

SHA256 of the preserved R06 ZIP:

```text
f24e31dc973ef8eb3addf8a9b457e142b736ad03ba4845841fd9dfe12c471bf9
```

Artifacts are local under `Builds/Download`; they are not committed to Git or uploaded by this task. The [release record](../evidence/verified/starfall-r19-release.json) contains executable/manifest hashes and exact evidence links. The user's earlier Discord distribution of R06 is user-reported, not independently verified here.

## Validation and limits

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Pinned Windows build | Passed, 0 build errors / 1 warning | [Build report](../evidence/milestones/kokerboom/round-20/preview-build.json), Unity6000.6.0f1 | Other-device execution |
| Existing island definition and edit contract | Passed, 2,841 assertions | Build's required `IslandValidation.Run`, fingerprint unchanged in [release record](../evidence/verified/starfall-r19-release.json) | No new world/save migration is provided |
| Actual new executable | Passed automatic checks, 146 checks and 3 captures, 0 observed runtime errors | [Player report](../evidence/milestones/starfall-r19-player/preview-smoke-20260910-223430-378.json) | Automatic method calls do not prove real keyboard/mouse acceptance |
| Frozen tree integration | Exact main counts: 3,745,751 vertices / 6,550,207 triangles; PH02 tint1 | Same player report and [startup image](../evidence/milestones/starfall-r19-player/preview-smoke-20260910-223430-378-01-startup.png) | No renewed visual score or performance certification |
| Walking, flight and trunk collision | Passed automatic shared-controller checks, ground clearance, all four bounds and blocked trunk approach | Player path samples and [contact image](../evidence/milestones/starfall-r19-player/preview-smoke-20260910-223430-378-03-trunk-contact.png) | Rock collision is deferred to the next milestone |
| Native window and HUD | Observed after explicitly authorized launch | [Native window](../evidence/milestones/starfall-r19-player/2026-09-10-native-r19-window.png) | User explored without agent movement input; full native controls and sustained FPS remain unverified |
| Archive and secrets | Passed, 187 entries / 183 runtime files unchanged; Gitleaks8.30.1 full-history/current-file/distribution scans | [Release record](../evidence/verified/starfall-r19-release.json), per-entry local ZIP manifest | Packaging is not another-device acceptance |
| R06 preservation | 194 files rechecked unchanged, original process retained | Release preservation record | No replacement or automatic promotion is implied |

The three wood colliders use the actual static mesh, with the legacy midphase selected for this dense geometry. Existing scenery rocks retain their earlier collision gap. No sea, galaxy, larger terrain, bots, multiplayer, saved-world loading or live integrations were added to this release. The separate coastal worktree owns the next world slice.

## Run and reproduce

Extract the complete ZIP into its own folder, then open `KookerStarfallR19.exe`. Keep its data directory and DLLs together. WASD moves; hold right mouse to look; Shift moves faster; F switches walk/fly; Q/E moves down/up in flight; Escape releases the pointer. F12 capture requires an explicit absolute `-previewEvidence` folder at launch.

Create a separate build from a clean committed source checkout:

```powershell
.\tools\render-kokerboom.ps1 -Round round-100 -R19PlayablePreview
```

Choose an unused evidence number. `round-20` is the preserved integration verification for this release, not a new tree critique. The launcher rejects altered frozen inputs or uncommitted build source and never launches the player. Build IDs include UTC timestamps; identical source does not promise byte-identical archives.

The opt-in `-previewSmoke -previewEvidence <absolute-folder>` route renders the built scene offscreen, bypasses native input/focus/pointer/HUD operations and exits its own process. It records scenario placement separately from movement. Normal launches remain interactive and do not run that route.
