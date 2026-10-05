# Round321 exact-PR rod capture

Round321 was built from the PR worktree at source commit `060d1b0d2019ee568bdefc34494ff65180dcc84a`, with the updated rod source files in the build tree. The player capture is an actual standalone HUD/gameplay frame at the normal third-person camera distance. The thick mahogany pole is held diagonally across the inhabitant, with a light cork grip and a teal crosswise reel visible beyond the palm.

Unity 6000.6.0f1 completed the player build with zero build errors. The integrated suite passed 2,072 named checks. The two updated rod source files match the build's 258-file source manifest; their hashes and the compiled assembly hash are recorded in `build-result.json` and `source-sha256.json`.

The muted visual probe captured the rod at HUD stage 5 and was stopped before the fishing route finished. The raw receipt records `RUNNING` because it was intentionally stopped during the route. AudioListener volume was 0. This packet proves rod visibility in the compiled PR candidate; it does not prove the `go fish` route, fish capture, eating, storage, cold reload, or user acceptance.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Rod visible in the normal game camera | Captured in the compiled PR candidate | `rod-equipped-gameplay.png` | User acceptance in their session |
| Build and integrated checks | Passed; 0 errors and 2,072 checks | `build-result.json` | Hosted exact-head checks still absent |
| Fishing route and catch/eat/store | Not established by this visual-only probe | `fishing-command-runtime.json` | Complete the isolated player flow and cold reload |
| Review and merge | Pending | PR #5 | Exact-head independent review and required checks |
