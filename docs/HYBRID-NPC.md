# Optional local thoughts — 0.0.5-preview.1

This separate courtyard extends the accepted deterministic NPC checkpoint, source `34752eb`. Optional local model proposals can suggest one high-level collect/deliver/wait goal, a short advisory plan, fictional dialogue and a brief generated reflection. The same inhabitant executes admitted goals through its existing deterministic navigation and action APIs. No learning, persistent model memory, saved-world migration or broad world integration is implemented.

The Starfall visual direction remains the warm kokerboom landscape, immense blue gas giant, distant galaxy and a clear living sea. [The living-sea specification](STARFALL-LIVING-SEA.md) retains fish schools, rays, coral forms, aquatic plants, rock arches and underwater light as future scene work. The courtyard is a behavior/control study and does not claim that environment is complete.

## Controls

P opens a true pause menu; P resumes from any page. Escape backs out of submenus, resumes from the root, or opens pause and releases the pointer when outside menus. Tab deliberately possesses/releases the same NPC. F selects follow/free spectator. WASD and arrows move the explicitly possessed NPC or spectator camera; Q/E moves a spectator vertically and Shift increases its speed. R pauses/resumes autonomy while observing. Hold right mouse to look. L reveals perceptions, decisions, audit source and the last accepted fictional dialogue/reflection.

**F11 toggles fullscreen/windowed directly.** Options > Graphics retains the button. The shortcut briefly freezes simulation and camera during the existing asynchronous display transition, then restores the previous pause state; it does not reset the NPC, inventory or route. F11 was chosen as an explicit application shortcut with repeatable Input System evidence, avoiding a second Alt+Enter path through native window handling.

## Local provider setup

Default launch makes no model requests. Configuration and opt-in are separate:

```powershell
.\KookerStarfallHybrid.exe -npcLocalEndpoint http://127.0.0.1:1234 -npcLocalModel YOUR_ALREADY_LOADED_MODEL_ID
```

Then open **P > Controls > Local thoughts** and choose **Turn local thoughts on**. No model name is hard-coded. Only a literal loopback HTTP origin is accepted; credentials, redirects, proxies and arbitrary remote URLs are excluded. The adapter reads LM Studio's model inventory and refuses a configured model unless it is already loaded. It never calls load, unload, download or settings endpoints. If inventory is unavailable or the model is absent, deterministic fallback applies. Concurrent changes by another application are outside this adapter's control.

The adapter uses LM Studio's documented [JSON-schema chat-completion format](https://lmstudio.ai/docs/developer/openai-compat/structured-output). A valid HTTP response is still untrusted: the strict parser rejects unknown/duplicate fields, wrong types/IDs/versions, extra text, malformed JSON, excessive size/depth, unlisted goals and unbounded text/plans. Proposals are at most 4096 UTF-8 bytes; plan length is 1–3 enumerated advisory words and dialogue/reflection are each at most 160 printable ASCII characters. Plan/text never executes.

## Deterministic boundary

At an idle decision boundary, the planner copies up to 16 eligible perceptions, current cargo and the last deterministic outcome. One async provider request can be pending; no provider receives transforms, physics handles, action APIs, credentials or general tools. A response can nominate only a target present in that request. Consumption re-senses the world and validates current visibility, permission, availability, cargo kind, failed-goal cooldown and snapshot age. The existing action API separately rechecks reach, ownership, line of sight, permissions and destination capacity at the moment of action.

The default deadline is 1500 ms; at most 12 requests are allowed per session with a 250-tick request cooldown. An accepted wait lasts at most 50 ticks. Pause, possession changes, display shortcuts, disabled thoughts and world reset cancel in-flight work. A timeout or unavailable provider turns thoughts off until explicit retry, so subsequent deterministic goals continue without repeated service stalls. Late responses have no world continuation. A 128-entry audit records request ID/tick/source, eligible IDs/cargo, latency, receipt/parse/live-validation result and admitted text. Raw arbitrary HTTP errors are not printed or persisted.

## Reproduce and assess

From committed source, with the Unity editor closed:

```powershell
.\tools\build-npc.ps1 -Mode Hybrid -Smoke
```

The wrapper runs the pinned Unity 6000.6.0f1, foundation checks, deterministic NPC checks and strict hybrid checks, then builds a unique player folder. Smoke uses actual per-process Input System events and actual offscreen Unity rendering. It sends no OS input to another preview. The wrapper preserves source project settings, earlier outputs and the visible player. Package an exact matching passing run using `tools/package-npc.py --build BUILD_FOLDER --runtime RUNTIME_FOLDER`.

The opt-in `-npcRealProbe` smoke extension requires explicit `-npcLocalEndpoint` and `-npcLocalModel`, and runs only after the complete fake/offline suite passes in that player. It performs one bounded diagnostic attempt with a 5000 ms deadline, records provider reachability/completion receipt/parse/validation and then completes a deterministic delivery or fallback. It does not establish that the normal 1500 ms budget will accept that model reliably. It never makes a second inference call at the delivery goal.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Strict proposal/provider boundary | 43 focused checks passed | `NpcHybridValidation.Run`; retained validation JSON | Future schemas require new validation |
| Actual default/fake/offline behavior and controls | 60 checks passed in runtime-03 | Retained runtime JSON, proposal audit and real player PNGs | Physical keyboard/mouse routing and sustained performance are separate |
| F11 and graphics button preserve state | Both directions passed | Actual Screen mode/dimension transitions plus NPC identity/cargo/goal/log assertions | Other displays/devices not tested |
| Real local inference | Pending final probe evidence | Inspect the final release's real-local-probe.json when present | No general latency/reliability or learning claim |
| Living sea / full world | Planned separately | Living-sea design and user concept references | Marine life and swimming environment are not delivered here |

Failed runtime-01 and runtime-02 are preserved locally. Runtime-02 exposed repeated provider waits after an offline timeout; the circuit-breaker repair in runtime-03 allowed a complete deterministic delivery. No earlier binary was replaced, and this work is local/unpublished.
