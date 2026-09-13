# Hybrid NPC requirement audit — 13 September 2026

**Subsequent manual-run evidence:** the separate preview.2 30-second diagnostic passed all 66 checks; one completion attempt timed out at 30003 ms with no completion response or raw reply, followed by one deterministic delivery. [Current diagnostic audit](HYBRID-DIAGNOSTIC-30S.md) and [reviewed runtime evidence](../evidence/milestones/hybrid-npc/manual-real-30s-20260913/README.md) supersede the blocked execution row below. Bounded fallback is verified; a valid real proposal/dialogue/reflection remains unverified. The following preview.1 audit is retained as its original historical snapshot.

Local build `KookerStarfallHybrid-0.0.5-preview.1-20260913-080358`; runtime source `9a3bf77dc219abf89dc6b0fd72cffa3f811a3ae7`. The **real-inference gate remains incomplete**. All implemented fake/offline player requirements below passed on the final exact build.

Evidence links: [final runtime with all 60 checks and proposal audit](../evidence/milestones/hybrid-npc/KookerStarfallHybrid-0.0.5-preview.1-20260913-080358/npc-runtime.json), [actual player gallery](../evidence/milestones/hybrid-npc/KookerStarfallHybrid-0.0.5-preview.1-20260913-080358/review.html), [43 focused checks](../evidence/verified/hybrid-proposal-validation.json), [26 deterministic action/perception checks](../evidence/verified/hybrid-deterministic-validation.json), [release/build/package evidence](../evidence/verified/hybrid-npc-release.json), [743-file preservation](../evidence/verified/hybrid-preservation.json).

| Claim / requirement | Status | Evidence | Remaining gap |
|---|---|---|---|
| Perception, navigation, physics and action authority stay deterministic | PASS | Existing 26 action/perception checks; final player delivery traces | Cross-device bit-identical physics not claimed |
| Provider interface and optional explicit adapter | PASS | `INpcProposalProvider`; endpoint-boundary focused tests; disabled-default actual player check | Only LM Studio local adapter implemented |
| Strict bounded JSON goals, plan, dialogue and reflection | PASS | 43 focused parser/validator/provider checks | Different schemas require separate validation |
| Model output cannot mutate the world directly | PASS | Actual `provider-does-not-mutate-world`; fresh-goal validator; existing sole action executor | No claim about arbitrary future provider plugins |
| High-level nonnearest goal changes behavior | PASS with labelled fake provider | Blue chosen instead of amber, then actual pickup/carry/delivery | Real model choice is unverified |
| Freshness, visibility, permission, ownership, capacity, cargo and cooldown | PASS | Focused stale/permission/cargo/cooldown checks plus actual denied proposal and action-failure recovery | Larger/shared world authority is separate |
| Disabled-by-default and explicit UI opt-in | PASS | Actual keyboard input opens Local thoughts off, enables it while paused, then resumes | Physical mouse click/native keyboard routing unverified |
| Concise HUD and inspectable text/audit | PASS | Source label, advisory plan, fictional dialogue, generated reflection and audit PNGs/JSON | No voice output or learning implemented |
| Timeout and unavailable-service fallback | PASS | Actual closed-loopback timeout, disabled optional requests and completed deterministic delivery | Normal local service latency not measured |
| Prevent repeated stalls after service failure | PASS | `unavailable-circuit-prevents-repeated-stalls`, then completed delivery | Retry is explicitly requested through the menu |
| Cancellation and discarded late reply | PASS | Broker tests plus actual pause cancellation and late-result rejection | No persistence across process restarts |
| Follow, free spectator and explicit same-body possession | PASS | Retained 42-control/baseline assertions within final 60-check run | Native input/performance remain separate |
| P/Escape menu and world freeze | PASS | Fixed tick, body/hand pose and context invariants in actual player | Other input devices untested |
| F11 and Options > Graphics toggles | PASS | Four actual display transitions; restored observed dimensions and identity/cargo/goal/log/position | Other displays/OS routing untested |
| Separate build and preserved accepted preview | PASS | Unique executable; 743 previous files unchanged by SHA256; accepted player remained responsive during work | No new normal foreground hybrid launch requested/performed |
| Build, package, provenance and secret checks | PASS | 2841 foundation assertions; 0 build errors/warnings; 188 package entries; Gitleaks 8.30.1 source/history/distribution pass; 19 asset bundle files reverified | Local only; no upload/push/merge |
| Read-only discovery of current local inference | PASS | Loopback inventory showed `google/gemma-4-26b-a4b-qat` loaded; no load/unload/settings requests | Inventory alone is not inference evidence |
| One real local model proposal and actual execution/fallback | BLOCKED / UNVERIFIED | [Explicit blocked-probe record](../evidence/milestones/hybrid-npc/KookerStarfallHybrid-0.0.5-preview.1-20260913-080358/real-local-probe.json): automatic approval review rejected launch, reason “blocked by policy”; zero real invocations/requests | A permitted real probe is required; provider reachability for inference, reply, parse/live validation, latency and real-goal execution are all unverified |
| Tree/blue giant/galaxy/living sea world direction | Retained design | User references and living-sea specification | Full landscape, fish/rays, aquatic growth and swimming are not implemented by this courtyard milestone |

The failed local runtime-01 and runtime-02 remain intact; runtime-03 is the 60-check repair baseline. Final runtime-04-offline independently passed the same 60 checks from the recorded source commit. The attempted real-probe launch did not execute. It was not retried through another tool.

The local ZIP is 44,072,501 bytes, SHA256 `d90fe1fd8d1135c55939aed13a8b29695fee7e50ae21795338220cc706800d71`. Asset library revision remains `fe306300ea3fede74b4a5c87afc86a28bb7999bc`. Recorded automated run duration is accelerated smoke time, not an FPS/performance benchmark.
