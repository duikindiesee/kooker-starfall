# Verification record

The [save and world lifecycle gate](SAVE-GAME-CONTRACT.md#required-fixtures-and-actual-player-acceptance) records future deterministic/isolation fixtures and actual-player save, quit, relaunch, reload, new-character/new-world and reset/archive acceptance. These requirements are not completed results; the existing evidence below remains scoped to its original builds and tests.

This is a new island foundation preview. Native desktop control was paused by the user after the first visible image and Night-button check. Background scenario evidence is kept separate from native controls and window presentation. Full CityLife gameplay is outside this milestone.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Source implementation pinned | Verified | Fresh Git fetch: `b71307023aea600da27aa3eada6cdcce7ba758ed`; source file hashes in [SourceVectors.json](../Assets/CityLife/Resources/SourceVectors.json) | No authenticated production world inspected; this is a new world, not a converted browser save |
| Independent source oracle is reproducible | Passed | `node tools/reference-vectors.mjs ../citylife-source --check`; byte-identical output SHA256 `3d139f3703fb3f60dcd0ca2226e342e06ffc306e5596832dfdcc576b728a31f9` | Only documented source domains/settings are compared |
| Base generation and edit persistence | Passed | [Final Unity validation report](../evidence/verified/final-validation.json): 2,841 assertions; 210 matching-setting terrain vectors, 42 noise vectors and 96 RNG values; full regeneration, reverse traversal, atomic save/reload, invalid-data rejection | Other CPUs/platforms and future browser-save migration unverified |
| Physical extent | Measured | 4,096m square, 6.007008km² sampled dry land, 3.285632km² gentle land, highest ground 126.501556m | Land area does not imply usable plots or house capacity |
| Windows player builds | Passed | [Final build report](../evidence/verified/final-build-result.json): Unity 6000.6.0f1, zero build errors/warnings, 2026-09-09 20:34:05 UTC | Final archive scanning recorded separately |
| Real native window renders the island | Verified for Build 03 | [Visible image](../evidence/milestones/02-visible-night-player-2026-09-09.png), [framed snapshot](../evidence/milestones/02b-user-requested-island-with-frame-2026-09-09.png), [native events](../evidence/verified/native-observation-build03.json) | Later candidate fixes still need native presentation review |
| Night control responds | Verified for Build 03 | Native GUI event at `2026-09-09T20:04:15.9533509Z`; visible night image | Keyboard shortcuts, mouse look, manual walk/fly and screenshot shortcut remain unverified |
| Ocean edge and ripple improvement | Verified offscreen | [Arid overview](../evidence/milestones/04a-arid-overview-2026-09-09.png): former corner absent and distant moiré reduced | Native candidate presentation remains unverified |
| Dry ground and vegetation | Verified offscreen | [Walking after](../evidence/milestones/04c-arid-walking-2026-09-09.png) and [close-up](../evidence/milestones/04d-grass-closeup-2026-09-09.png): visible grass blades, textured soil, rocks and coloured plants | Richer blending/wind/density tuning and native presentation remain future work |
| Offscreen rendering and scripted travel | Passed | [Final route report](../evidence/verified/final-candidate-route.json): four 1600×900 images, flight/walking coverage, zero observed errors/ground penetration, exit 0 | Native input and HUD presentation remain unverified |
| Offscreen frame timings | Measured | 1,430 samples / 24.31s; mean 17.00ms, p95 16.97ms, worst 153.89ms; Radeon 8060S, D3D11, target 60fps | Includes capture/phase-transition hitches and other active workloads; does not establish native presented-frame performance or GPU headroom |
| Asset licence and provenance | Verified | Three CC0 records/hashes: one imported ground image and two candidate previews; [credits](ASSET-CREDITS.md) and portable notice | Kenney rock/grass models are not imported or tested |
| Repository and package safeguards | Passed locally | [Check evidence](../evidence/verified/repository-checks.json): source scan, 186-entry final ZIP integrity and distribution scan, project/art guards, oracle and syntax checks | Full history rerun after implementation commit; hosted checks are linked from the PR |
| Full CityLife gameplay/shared world | Deferred | [Source and scope](SOURCE-AND-SCOPE.md) | Roads, plots, house tools, Kooker HQ assets/interactions, authoritative service, household runtimes and multiplayer |

## Failure history and acceptance limits

The first visible player was black because URP initialized an unused template SSAO feature whose resources were stripped. Removing that feature corrected Build 03's actual visible render. The original black image remains in the visual catalogue. Build success alone was insufficient evidence.

A later isolated hidden-window run had no runtime errors and moved through its scripted route, but all framebuffer images were black. Hardened image checks rejected it with exit code 3. Those frame times are not published as rendering performance. An explicit offscreen camera route is used for background graphics checks; it cannot establish native presentation, HUD readability, focus or input behavior.

The native Night event is the only completed live input check. No successful F12 capture or WASD/mouse test is inferred from implementation. Desktop testing must resume only under a new permitted slot. Background builds/tests do not replace or restart the user's player window. The enlarged HTML asset catalogue's local URL was blocked by browser policy, so its browser layout remains unverified; embedded image bytes and CSS framing were checked.
