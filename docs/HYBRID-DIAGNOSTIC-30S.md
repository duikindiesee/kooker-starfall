# Local proposal diagnostic — 30-second opt-in

The manual 5-second run is preserved: [real probe record](../evidence/milestones/hybrid-npc/manual-real-5s-20260913/real-local-probe.json), [64-check actual-player report](../evidence/milestones/hybrid-npc/manual-real-5s-20260913/npc-runtime.json), and [actual fallback delivery](../evidence/milestones/hybrid-npc/manual-real-5s-20260913/41-real-probe-delivery-or-fallback.png). It reached the model inventory, attempted exactly one completion against the already-loaded model, received no completion response within 5000 ms and completed deterministic fallback delivery. It does not prove a valid real proposal or generated dialogue/reflection. The earlier automatic-launch rejection remains historical evidence; the manual run supersedes its zero-request status.

Version **0.0.5-preview.2** adds a diagnostic-only deadline. Normal gameplay remains **1500 ms**, off by default. The ordinary broker still rejects requests above its existing 5000 ms maximum. The separate diagnostic broker permits only 5000 or 30000 ms and uses the same schema, timeout/cancellation, audit and live validation code.

Only the combination **`-npcSmoke -npcRealProbe -npcDiagnostic30`** permits the 30000 ms budget. The extra flag alone changes no gameplay behavior. Provider configuration and world reset clear the diagnostic override. The existing 5-second smoke remains available without `-npcDiagnostic30`.

The harness first repeats the complete fake/offline acceptance, then takes one stable synthetic perception snapshot and makes at most one real completion attempt. If the explicit model is no longer loaded, inventory validation prevents that attempt. It never calls model load/unload/download/settings APIs. The model identifier and endpoint remain explicit arguments. A raw response is not accepted as an action: strict parsing and current eligibility precede deterministic navigation, pickup and delivery. Optional thoughts are disabled after the one attempt, so no second model call occurs at delivery.

Evidence includes the requested deadline and elapsed time, whether inventory/completion HTTP returned, whether raw proposal text arrived, parse/live-validation outcome, proposed goal/target, non-empty admitted dialogue/reflection, screenshots and actual deterministic delivery. Missing, invalid, slow or rejected replies must finish with bounded fallback and the exact failure code. The overall smoke watchdog remains finite. Accelerated smoke duration is not an FPS measurement; a single 30-second probe is not a gameplay-latency benchmark.

The manually launched diagnostic writes into a new evidence directory and refuses to overwrite non-empty earlier evidence. The exact new executable, source commit and one-line PowerShell command are recorded in the diagnostic build handoff after compilation. **Preparing that command does not execute a real model request.** Automatic real-probe launch remains blocked by approval review; the user's manual command is the pending execution step.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| 5-second real attempt and bounded fallback | PASS | Retained manual run: 64 checks, one completion attempted, timeout at 5000 ms, actual delivery | No real model proposal/text was received |
| Normal gameplay timeout remains 1500 ms | PASS | 48 focused checks, including default, normal limit and diagnostic flag guard | New actual-player regression still required |
| 30-second real proposal or fallback | Prepared for manual execution | New source-pinned build and manual launch command | Actual 30-second outcome remains unverified until that run finishes |
