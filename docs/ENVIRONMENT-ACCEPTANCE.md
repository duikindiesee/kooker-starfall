# Environment foundation: evidence and integration gates

The subsequent [supported-terrace visual revision and before/after comparison](ENVIRONMENT-TERRACE-COMPARISON.md) preserves the baseline results below and provides its own compiled-player checks and separate executable.

This workstream is an additive physical-environment foundation and a compiled Windows component-test player. It is not the panorama-scale world, an NPC brain, swimming implementation or a production deployment. Source is isolated on `codex/starfall-environment`, based on the preserved coastal checkpoint `30bdefb`.

## What is implemented

The [contract](ENVIRONMENT-CONTRACT.md) specifies units, gravity, seeded ticks, weather transitions, force limits, water boundaries, cold exposure, pause semantics and save validation. The [integration boundary](ENVIRONMENT-INTEGRATION.md) gives the coastal task an `IEnvironmentSurface` adapter and gives future inhabitants local weather/exposure data without copying or connecting their brain or memory code.

The fixture contains a continuous-collision rigid-body force component, buoyancy with mass-scaled damping, a swept grounded capsule, a shared wind-driven water shader, flexible vegetation proxies, precipitation, player cold signaling and a local capsule that seeks a marked shelter when cold. A shelter does not automatically make subzero air warm: the probe may remain cold while sheltered. These are explicit foundation behaviors and proxies, not completed world art or autonomous-inhabitant intelligence.

## Test layers

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Deterministic model and invalid-state checks | Passed 12,410 assertions in editor and compiled player | `EnvironmentChecks.Run`, local model/player reports | Cross-platform bitwise behavior not established |
| Existing island generator and save boundaries | Passed 2,841 required assertions | `CityLife.World.Editor.IslandValidation.Run`, local `validation.json` | Does not prove main-world generation |
| Gravity, mass, collisions, buoyancy, constraints and capsule traversal | 20 focused compiled-player checks passed in retained runs | `EnvironmentPhysicsChecks.Run`, detailed player JSON | Joint landscape traversal and actual input-device acceptance |
| Shared wind, weather transitions, cold response, pause and save | Instrumented compiled-player checks and rendered frames retained | Player weather observations, flags and four weather captures | NPC-brain navigation/memory integration |
| Performance | See measured final run record below | Per-frame CSV includes resolution; JSON includes percentiles and capacity | Long soak, other devices and main-world frame rate |
| Existing builds | 748 files unchanged in cosmic/display build roots | Before/after SHA-256 manifests retained locally | Other workstreams may legitimately create their own later builds |
| Repository safety | Local source checks retained | `check-project.py`, PowerShell parser, Gitleaks | No hosted CI, push, PR, merge or deployment performed |

## Retained failures and corrections

- First editor attempt ended during compilation without a completion report. Its log is retained; the cause is unverified. A subsequent editor run completed normally.
- `player-01`: failed buoyancy equilibrium (y=0.807 m after six seconds). Corrected drag to scale with mass and reused the same force function in runtime and test. Subsequent equilibrium measured approximately -0.000038 m with vertical speed 0.00032 m/s. Its missing-material probe was also corrected after inspecting the actual player capture.
- `player-03`: behaviors passed, but later screenshot inspection revealed a resolution change; its startup-only width field was insufficient. Do not interpret its frame data as a fixed-1280x720 benchmark.
- `player-04-stress`: the new per-frame guard correctly failed when resolution changed from 1280x720 to 2880x1800. Physics/weather flags passed. The fixture now disables inherited native-resolution/fullscreen-switch defaults and sets an explicit automated-run resolution; the exact cause of the earlier change is not established.

## Acceptance still open

The main-world adapter, coast/cliff/underwater traversal, landscape vegetation and water art, swimming, saved character/body transforms, real NPC integration, long-duration performance and direct keyboard/mouse acceptance remain unverified. The computer-use runtime required for native input automation was not exposed in this task; rendered-frame captures and scripted tests come from the actual Windows executable, not the Unity editor. No human gameplay approval is inferred.

Raw logs, synthetic save files, machine paths and preservation manifests remain in ignored `evidence/local`. Reviewed reports and rendered fixture frames may be retained under `evidence/verified` and `evidence/milestones`; they must preserve their component-test labeling.

## Final executable and measured runs

Local build: `Builds/Environment-20260913-154002/StarfallEnvironment.exe`, version `0.1.0-environment.1`, Unity 6000.6.0f1, Windows D3D11. The complete [build-file checksums](../evidence/verified/environment/build-files-sha256.json) cover the executable, managed code and runtime data; [source checksums](../evidence/verified/environment/source-sha256.json) record the compiled candidate. The [build report](../evidence/verified/environment/build.json) records success with zero errors.

| Final compiled run | Result | Frames / duration | p95 / p99 frame ms | Core update p95 ms | Load / resolution |
|---|---|---|---|---|---|
| [Stress 05](../evidence/verified/environment/player-05-stress.json) | PASS | 1,924 / 32.071 s | 16.716 / 16.920 | 0.0297 | 128 bodies, 512 precipitation capacity; every sample 1280x720 |
| [Repeat 06](../evidence/verified/environment/player-06-repeat.json) | PASS | 1,923 / 32.051 s | 16.684 / 16.930 | 0.0284 | Same executable/load; every sample 1280x720 |

Hardware reported by the executable: AMD Ryzen AI Max+ 395 and Radeon 8060S. [Frame data 05](../evidence/verified/environment/player-05-frame-times.csv) and [frame data 06](../evidence/verified/environment/player-06-frame-times.csv) retain individual measurements. [Repeatability comparison](../evidence/verified/environment/repeatability.json): all 20 physical measurements identical (maximum absolute difference 0); all four weather endpoint wind vectors identical. This is same-machine evidence, not a cross-platform PhysX guarantee. Eight additional acceptance flags passed in both runs: pause, disk save restoration, wind force, coastal grounding, coastal movement, cold/shelter response, wind visual consumers and weather transitions.

Actual frames from the compiled stress player: [clear](../evidence/milestones/environment-foundation/clear.png), [rain](../evidence/milestones/environment-foundation/rain.png), [cold](../evidence/milestones/environment-foundation/cold.png), [storm](../evidence/milestones/environment-foundation/storm.png). Their geometry is labeled test apparatus; proxy art is intentional. Water is an opaque directional-wave foundation shader, not the coastal task's final transparent underwater art. Unused inherited URP depth-of-field/panini shader warnings remain in raw player logs; those effects are not part of this fixture.

[Preservation check](../evidence/verified/environment/preservation.json): 748 previously inventoried cosmic/display build files unchanged. Build preparation settings were restored after each successful scripted build; the retained implementation changes are additive apart from milestone documentation.
