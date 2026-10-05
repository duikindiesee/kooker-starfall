# Round319 fishing rod review packet

Round319 was built with Unity 6000.6.0f1 on 26 September 2026. The HUD shows the rod equipped in a real standalone-player capture. This is a camera render from the gameplay test, not a desktop screenshot or concept.

`build-result.json` identifies the candidate build, 2,072 named integrated checks, runtime build GUID, and published changed-source hashes. `source-sha256.json` lists the changed Assets/tools file hashes; all listed hashes matched the source copied into this PR review branch before commit. The complete Unity build manifest covers 350 source files.

`runtime-result.json` is a sanitized receipt from one isolated fresh-world standalone run. `go fish` landed a fish and `eat catch` removed it and raised nutrition. The following `go fish then store` failed with `command-failed-no-reachable-bank`; this packet intentionally records the failure. No original player saves or raw logs are included. The earlier round318 packet records four successful standalone runs for its earlier binary, but round319 needs the full loop and cold-reload proof again.

The new rod includes a thicker tapered blank, a separate cork grip, a teal reel spool and crank, and pale line guides. The capture shows the ordinary third-person camera distance. User acceptance of how visible and natural it looks remains open.

## Review gates

- Repair and rerun the repeat fishing/storage path, then test cold reload on this exact round319 source/build.
- Existing fish/carp/riverbank provenance entries lack complete public HTTPS source and publisher licence references. `tools/check-project.py --self-test` exits at the provenance validation; do not invent these records or weaken the check.
- The package guard's negative cases pass. Forty-six PowerShell scripts parse. The PR targets `dev`, but workflows currently run on `main`; the live PR currently has no checks.
- Request a current exact-head independent review. Merge remains blocked until the failures above are resolved and required checks pass.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Rod visual in compiled player | Capture retained | `rod-equipped-gameplay.png` | User acceptance |
| Catch and eat | Passed in this run | `runtime-result.json` | None for this subflow |
| Repeat catch and store | Failed | `runtime-result.json` | Repair reachable-bank routing |
| Build checks | Passed 2,072 named checks | `build-result.json` | Full gameplay/reload validation |
| Merge readiness | Blocked | Provenance guard and empty PR checks | Resolve, then exact-head review/checks |

The 	ools/test-player-smoke.ps1 final blank lines were normalized for diff hygiene after this build. Its hash comparison in source-sha256.json records that documentation-only whitespace difference; gameplay source hashes are unchanged.
