# Food v0.1.5: Windows execution block

Recorded 14 September 2026. This is a blocker report, not a review-ready integration handoff.

Later development result: [the trusted Editor visual/input gate passed](FOOD-EDITOR-ACCEPTANCE.md). The standalone block described here remains unresolved and its evidence remains intact.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Opaque-HUD correction baked | PASS | `evidence/local/build-20260914-060559/food-build.json`; source `8f790831f21717533018f638c0bd0df0976c462c`; zero build errors | Actual rendering/input not observed |
| Native launch | BLOCKED before process start | `evidence/local/v015-native-20260914/launch-error.txt`; Windows reported Application Control policy blocked the file | Approved execution of the existing candidate under the host's policy |
| OS reason | Confirmed signing/policy rejection | `code-integrity-events.json` in that same folder: events 3077 and 3033 at 08:08:06 SAST | Policy owner must determine a permitted signing/approval path; no bypass attempted |
| Prior builds preserved | PASS, 929 files unchanged by SHA-256 | `evidence/local/preservation/resume-all-builds-before.json` and `resume-all-builds-after.json` | None for this preservation comparison |
| Prior gameplay evidence | Retained; full player behavior/reload suites not repeated | [Existing checkpoints](FOOD-ACCEPTANCE.md) | New visual/input gate remains open |
| Integration/review | Not submitted as review-ready | Isolated branch `codex/starfall-food` in `duikindiesee/kooker-starfall`; `gh pr list --head codex/starfall-food --state all` returned `[]` | Visual gate, exact-head review and authoritative Live Brief queue/receipt before integration |

Candidate: `Builds/Food-0.1.5-20260914-060628/StarfallFood.exe`, version `0.1.5-food.6`. Executable SHA-256: `A4C71B1BFC3E42D02F2F129FE04ADEF3EBF2343A662884E4B99598E9DFC90E7D`. Retain the complete build folder; the executable alone is not the game payload.

The normal build's mandatory preflight ran 65 food model checks and the island validation; the already-passed standalone behavior/reload suites were not rerun. The attempted native session created no player log or window because Windows rejected process creation. No food process or editor was left running; the editor and native surface were released to the shared queue.

Resume with the existing candidate when the host provides an approved execution path. Inspect the actual world viewport and changing HUD, then exercise the native lesson/gather/eat and save controls while retaining screenshots and receipts. Do not repeat passed behavior/reload suites, relabel a build as visual evidence, or submit a clean integration handoff before this gate passes. Do not rename, relocate, whitelist or change security policy merely to evade this block.
