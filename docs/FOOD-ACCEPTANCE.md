# First Berry: isolated player evidence

This is a separate primitive Unity component fixture. It does not replace the combined Starfall island. The concept storyboard is illustrated intent, not captured gameplay. No Discord message has been posted.

## Preserved checkpoints

| Claim | Status | Evidence under this checkout | Remaining gap |
|---|---|---|---|
| v0.1.0 berry loop | Passed 45 model + 14 scripted player checks and exact separate-process reload | `evidence/local/visible-20260913-182902/runtime-report.json`, `relaunch-report.json` | Earlier overlapping labels and water placement failed visual review; preserved as history |
| Native input | Observed Meet needs and Save button actions in v0.1.0 | `evidence/local/visible-20260913-182902/native-input-check.json` | Human acceptance and later-version input remain separate |
| v0.1.1 loop | Passed 65 model + 14 scripted player checks and exact separate-process reload | `evidence/local/v011-loop-20260913-190548/` | Not a controlled performance benchmark; p95 59.08 ms with other work active |
| v0.1.1 natural lifecycle | Passed five checks using declared seed 25, with no injected seed outcome | `evidence/local/v011-eden-20260913-191125/eden-report.json` | Long ecology receipts still overlapped; final revision pending |
| v0.1.1 death and return | Passed six rendered scripted checks | `evidence/local/v011-mortality-20260913-190919/mortality-report.json` | Starts at a labelled synthetic final-starvation boundary, not hours of natural play |
| v0.1.2 world-switch isolation | Passed 79 loop/model checks, separate reload, six mortality and six Eden checks | `evidence/local/v012-loop-20260913-192342/`, `v012-mortality-20260913-192553/`, `v012-eden-20260913-192606/` | Rapid HUD state changes revealed framebuffer ghosting; these images fail final visual acceptance |
| v0.1.3 background camera | Six mortality checks passed; visual checkpoint rejected because world viewport became black | `evidence/local/v013-mortality-20260913-193524/` | Replaced in source by opaque GUI panel fills; needs v0.1.4 executable evidence |
| Combined-world integration | Not performed | [Adapter contract](FOOD-INTEGRATION.md) | Integrator must connect terrain sites, weather/water, actor identity, navigation and atomic save authority |

The mortality run preserves death hash, world tick and ecology through return, recovers the owned inventory once, and follows the remembered lesson with a verified energy-restoring meal. Model checks separately cover delayed dehydration/starvation, fat use, rest, activity, overeating, protein effects, repeated deaths with no fabricated lesson, immutable snapshot retention and world/inhabitant isolation.

Prior player preservation: `evidence/local/preservation/final-prior-builds-corrected.json` compares all 185 v0.1.0 and 186 v0.1.1 files by SHA-256 and reports no changes. The preceding `final-prior-builds.json` used the wrong baseline field name for v0.1.0; its failure is a comparison-script error, not a missing build. Keep it as diagnostic history. `evidence/local/final-secret-scan.log` records Gitleaks 8.30.1 passing 112 reachable commits and current publishable files; private fixture saves remain ignored.

## Launch and controls

Run `StarfallFood.exe` from a complete versioned folder under `Builds`; keep its adjacent data files. The window is 1280×720. Click **Learn berry (lesson)**, **Gather one**, **Eat berry**, **Verify / drink fresh**, then the cultivation button. Actions move the capsule to the target and check visibility/reach before changing state. **Meet needs** enables the bounded deterministic goal loop. It uses no local model.

**Save** explicitly writes an immutable snapshot and updates the active pointer. **Reload** restores the saved state with no offline growth. Quitting does not automatically save. **New world** creates a new world ID; **Clean reset** creates a new generation of the current world. Both preserve the selected seed and prior snapshots. Interactive saves use the dedicated `food-eden-2` folder under this player's Unity persistent data directory; older rules are not migrated.

**Wet / dry**, **Water bed**, **Rest / wake** and **Camp aid** are visible fixture controls. Aid is recorded assistance, not proof of a self-sustaining economy. **Return** appears after death; **Recover bag** visits the refuge inventory. This is one inhabitant, four plants maximum, fixed habitat sites, compressed growth, fictional body units, and at most 128 validated death records. General confidence/correction beliefs, body morphs, poisonous plants, predators, hunting and defenses remain roadmap work.

For reproducible scripted evidence, launch into a new evidence directory using `-foodTest -foodEvidence <directory>`, then use `-foodResume` with that same directory. `-foodEden -foodSeed 25` records the roughly three-minute natural lifecycle; `-foodMortality` runs the labelled boundary scenario. These scripted runs are distinct from native input and human acceptance.
