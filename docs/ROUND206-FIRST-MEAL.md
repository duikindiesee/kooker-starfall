# Round206 first ordinary model-chosen meal

Candidate: `KookerStarfallIntegrated-0.0.11-survival.1-20260915-100433`.
Source: `86378ba054873accd0d2afe69b82cac8ef0695ce`.
Full build fingerprint: `8b58ddf239ae8edabc3050800dba1fe5ad3f4c6dfe1f6075d702724d1dc6ba90`.
This is an interim observation from run14, not a completed soak or release.

The normal process receipt identifies a real player with survival enabled and
smoke, probe and accelerated death modes disabled. At request sequence 6 the
inhabitant carried one fruit but had no verified meal benefit. The offered
actions were `eat fruit,explore west`. Local E4B returned `eat fruit`, completed
with `stop` in 3020 ms, and was admitted after live validation.

At tick 3268, food receipt 3 records energy +1200, hydration +400 and inventory
-1. Meal-benefit knowledge changes from false to true only after that outcome.
The issued request, admission and meal have matching request/response linkage.
Actual framebuffer `game-frames/frame-000510.png` visibly displays the chosen
meal and measured benefit beside the inhabitant and succulent.

The same candidate separately records the event reflection `Delivered amber`
in 454 ms through `google/gemma-4-e4b`. That reflection is distinct from the
`starfall-local-e4b` survival choice. Neither is full conversational intelligence.

Private evidence remains under `evidence/local/normal-survival/run-14`:
`process.json`, `survival-evidence/normal-survival.jsonl`,
`memory-evidence/normal-living-memory.json`, and the game-only frame capture.
The 120-second capture reports 875 frames, zero dropped readbacks and two busy
skips. It has not yet been encoded/replayed as a final narrated walkthrough.

Remaining: sustained ordinary behavior, actual drinking, restart retention,
separate death diagnostic, cave/weather integration, club and sky/water visuals,
protected review and final user acceptance. The smaller memory card is visibly
present; the large diagnostic decision panel remains unsuitable for a scenic
final walkthrough. This record grants no 7.3/10 visual score or promotion.
