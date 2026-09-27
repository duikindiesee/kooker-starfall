# Round322: corrected fishing-rod carry pose

The standalone player capture now shows the inhabitant holding the fishing rod upright at the cork grip while walking and casting. The line visibly runs from the rod tip to the bobber. This is a set of still screenshots, not a video recording.

The grip origin is solved from the rod's rotated handle point, and the carried rod pose is kept aligned with the wrist and character-facing direction through the generic walk cycle. Integrated validation passed 2,074 named checks, including checks for palm-to-cork alignment and the stable upward rod axis. Unity 6000.6.0f1 built the candidate with zero errors and 77 warnings. Hashes in `source-sha256.json` tie both source files and the compiled `Assembly-CSharp.dll` to the captured candidate.

The muted standalone HUD probe completed `go fish`: carp `river-carp-19` approached, ate bait, was reeled in, and was eaten. The second `go fish then store` run still ended with `command-failed-no-reachable-bank`; fish storage and cold-reload acceptance remain unproven. Probe audio volume was 0. The exact PR remains open with no review decision or hosted checks.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Rod and cork align at the wrist during walk/cast | Captured in standalone player; alignment checks pass | `rod-upright-approach.png`, `rod-tip-to-bobber-line.png`, `fishing-command-runtime.json` | User visual acceptance |
| Line begins at rod tip and reaches bobber | Visible during cast | `rod-tip-to-bobber-line.png` | User acceptance |
| First natural-language fishing loop, bait response, catch, and eating | Passed in muted isolated player | `fishing-command-runtime.json` | Repeatability and fish storage/cold reload |
| Catch and store | Failed on second loop with `command-failed-no-reachable-bank` | `fishing-command-runtime.json` | Fix bank routing and rerun storage |
| Build and integrated checks | Passed; 0 errors, 2,074 checks | `build-result.json`, `source-sha256.json` | Hosted PR checks/review |
| Video recording | Not available; only still captures were produced | `captures` in `build-result.json` | Record video if requested |
