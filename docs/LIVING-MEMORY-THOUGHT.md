# One living memory thought

**One complete living-memory thought passed in Unity Editor Play Mode on 14 September 2026.** Standalone acceptance remains blocked by Windows signing policy. This isolated slice does not add death/return, survival, ecology, saved-world migration or cross-world sharing.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Grounded event and scoped memory | Editor runtime PASS | Actual amber delivery at tick 588, three receipts, bounded own retrieval, SQLite restart; event `58150e0ef2c00827b7f68ee3b6ebaa504cd75a8345a6f79c6c286232447c527e` | Broader gameplay events |
| Complete model thought and live HUD | Editor runtime PASS | E4B generated "Delivered Amber"; strict parse/live admission at **991 ms / 1500 ms**, one attempt, zero reasoning tokens | Latency reliability and standalone acceptance |
| Isolation and preservation | PASS for this slice | Other inhabitant empty, foreign query rejected 400, 558 prior build hashes unchanged, Smart App Control On | Broader privacy testing |

Exact runtime source: `fda786a434b6618b8d405e731b7262c7f140a61d`. Evidence: `evidence/local/living-memory/run-20260914-editor-two-word/REPORT.md`, `runtime/living-memory.json`, and `runtime/51-living-memory-thought-or-fallback.png`. The screenshot explicitly labels Editor Play Mode and shows the model text, actor/world and delivery event. Model inventory took 21 ms, completion 968 ms, with 991 ms total measured through parsing. The linked E4B ran on the already-loaded Mac through laptop loopback. The seven focused runtime checks passed with no runtime errors; physical keyboard and standalone acceptance are separate. Earlier failed attempts below remain historical evidence.

The compiled courtyard performs its existing deterministic pickup and delivery. A trusted receipt hook exports identity and successful action receipts through StarfallMemoryExport. The isolated runner creates a new private capability configuration and SQLite database, starts the reviewed v1 HTTP service with outbound networking disabled, and passes an explicit client configuration path to this new player. The publisher and inhabitant reader capabilities are distinct and never enter model context or evidence output.

The player publishes exactly three receipts, checks each returned event hash against its own canonical export, and retrieves at most four own episodes. It rejects another world/inhabitant, nonconfirmed records, an unexpected summary or a mismatched event hash. Only the matching verified delivery episode enters inference. Raw private archives, unrelated episodes, beliefs and dreams are not queried.

Reflection-only protocol v2 is one JSON string containing a two-word thought. It cannot express a goal or action. This is separate from, and does not relax, the existing action-proposal schema. Unity retains version, request, event, world and inhabitant bindings in the trusted request closure instead of asking the model to echo them. The parser accepts only a complete nonempty printable ASCII JSON string up to 64 characters, with no trailing value, object or array. The bounded response has a 8-token cap; reasoning is disabled request-locally. Only the verified episode summary enters model context. The deadline is the existing gameplay value, **1500 ms**, including model-inventory validation. Inventory and completion timing are recorded separately. There is one attempt and no retry/warmup. Timeout, unavailable model, malformed text or interrupted control discards the thought and leaves deterministic behavior authoritative.

While inference runs, the diagnostic advances normal simulation ticks against wall-clock time. Before display it rechecks identity, control state, snapshot age and the actual delivered item/destination state. The reflection must mention the remembered item and completed delivery. Model text is displayed as interpretation of a separately cited verified event, never promoted to a fact or fed into an action API.

The runner queries a second inhabitant (which must have no episodes), rejects a foreign-world query, stops the service, reopens SQLite and verifies the persisted event chain and own episodes. These are real persistence checks; the player's traversal is scripted acceptance input, not physical keyboard acceptance. Failure is preserved, not relabeled a successful thought.

Run only when the coordinated Unity build slot is free:

```powershell
.\tools\build-npc.ps1 -Mode Hybrid
python tools/run-living-memory.py --exe Builds/EXACT_BUILD/KookerStarfallHybrid.exe --output evidence/local/living-memory/NEW_RUN
```

Earlier binaries and evidence remain separate. The runner refuses existing output and does not mutate model defaults or load a model. It uses the explicitly already-loaded E4B exposed through laptop loopback; device attribution must be recorded for each run because LM Link may execute on the Mac.

Death and knowledge progression are [future world rules and acceptance milestones](STARFALL-KNOWLEDGE-PROGRESSION.md), not part of this gate. API v1 remains unchanged and cannot ingest death/return events.

## Actual compiled-player result — 13 September 2026

Source `447faad`, build `KookerStarfallHybrid-0.0.6-memory-preview.1-20260913-185950`, runner `4a1a4f1`. Local evidence: `evidence/local/living-memory/run-20260913-1902/`.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Real identity, pickup and delivery persisted | Passed | Three player receipts; amber delivered to depot-west at tick 588; SQLite reopened and chain verified | Death/return is deferred |
| Bounded own-memory retrieval and isolation | Passed | Delivery event `33ac001292f38bafa3478a053240577dfaaac824fe8bda0ddecb0711667eaf5e`; other inhabitant empty; foreign-world query rejected 400 | No broader privacy certification claimed |
| Genuine thought within 1500 ms | Failed | One E4B completion attempt timed out at 1504 ms; no complete answer; server recorded only three completion tokens and zero reasoning tokens | A complete valid response within the unchanged deadline |
| Actual HUD admission guard | Passed | Player screenshot `runtime/51-living-memory-thought-or-fallback.png` visibly identifies event, actor and timeout; no thought admitted | Physical keyboard acceptance is separate |

The complete living-memory thought is **not proven**. The compiled player exited with its explicit genuine-thought assertion failure. No timeout extension, partial-answer admission or endpoint-only substitution was used.

## Minimal wire attempt — 14 September 2026

Source `cae0ada3419a6bb739df6cf3196a90260c7b6a88`, build `KookerStarfallHybrid-0.0.6-memory-preview.2-20260914-060428`. The scalar response path compiled with 2841 world assertions, 26 NPC checks and 71 hybrid/memory checks passing. All 558 files in the three earlier builds retained their hashes.

Actual player launch was blocked before startup/inference by Windows Application Control (`WinError 4551`). Code Integrity events 3033/3077 identified this executable as failing Enterprise signing requirements or policy. No policy bypass or alternate launch was attempted. Local evidence: `evidence/local/living-memory/run-20260914-scalar/REPORT.md` and `application-control-events.json`. A policy-compliant signed/approved executable is required before this version can establish runtime acceptance. This does not improve the previous failed thought result.

## Authorized Editor runtime path

The dedicated `StarfallMemoryPlayMode.Run` harness opens the preserved preview.2 courtyard scene and its pipeline in the pinned installed Unity Editor, then enters Play Mode. It runs the same runtime thought and action code; it does not load or execute the blocked standalone binary. Evidence and the thought HUD explicitly say Editor Play Mode, and event producer build identity is `editor-` plus the clean source commit. The runner preserves/restores project settings, uses new private memory state, and retains the same deadline, cancellation, isolation and admission gates. This route proves Editor runtime only, never standalone acceptance or a release.

```powershell
python tools/run-living-memory.py --editor 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' --scene Assets/CityLife/GeneratedPreview-Character-KookerStarfallHybrid-0.0.6-memory-preview.2-20260914-060428/InhabitantDecisions.unity --output evidence/local/living-memory/NEW_EDITOR_RUN
```

Run only with an explicitly allocated exclusive Editor slot and no existing Editor/import worker. The blocked binary and Code Integrity evidence remain preserved. Smart App Control remains On. Future standalone acceptance requires a properly signed release artifact whose certificate and reputation satisfy the machine's trust policy, then a fresh policy-on launch and actual-player gate. A signature alone is not a launch receipt; renaming/copying the blocked executable or changing policy is not this workflow. Certificate acquisition or signing-service enrollment is separate authorized release work.

See the separately documented [standalone signing requirement](STANDALONE-SIGNING.md). The Editor harness waits for startup/import settling and runs only the focused living-memory gate; standalone injected-keyboard/control acceptance is explicitly excluded, while live control-state checks still govern thought admission.

The first focused Editor run, source `a65debd8095d4dbee69cbc4af67fe124f35b1c03`, proved real delivery at tick 588, three scoped receipts, matching retrieval and SQLite restart/isolation. Event `09bb859b8b9752f6b6fea581f1fcff26a9d98de48fc651f275b8686da2935a7b` is bound to the visible Editor HUD. Thought admission failed at 1511 ms (101 ms inventory). The server recorded a complete six-token thought at cancellation, but no complete answer reached Unity within the deadline and none was admitted. Evidence: `evidence/local/living-memory/run-20260914-editor-focused/`. The next two-word prompt is a latency candidate, not a successful thought claim.
