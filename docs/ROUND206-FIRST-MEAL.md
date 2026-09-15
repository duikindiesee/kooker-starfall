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
skips. It is now encoded as local `starfall-round206-WIP-first-meal.mp4`:
119.67 seconds, full decode passed, SHA256
`76fe55227858b7f4b390ed3c923150315aeee521496f087a1dcc826f94c26bbf`.
Its companion capture receipt records measured timing and source-index hash.
This silent diagnostic clip is not the final narrated walkthrough.

The completed unchanged run records 174 issued requests, 150 admitted choices,
124 completed routes, seven meals, 22 timeouts, two stale rejections, five
live-terrain blocks and six unplannable routes. Final physiology tick is 1187.
No spring drink occurred. Fruit hydration is a valid survival outcome; absence
of a drink does not invalidate it or justify manufacturing thirst.

Run15 restores the scoped needs and learned meal benefit, and visibly recalls
the earlier delivery. However, its fresh delivery prerequisite blocks new
survival requests. Therefore gameplay continuation after restart FAILED even
though data reload succeeded. The next source correction records earned
survival authority separately; historical saves are preserved, not silently
treated as containing that marker. See SYSTEM-ARCHITECTURE.md.

Remaining: sustained ordinary behavior, actual drinking, restart retention,
separate death diagnostic, cave/weather integration, club and sky/water visuals,
protected review and final user acceptance. The smaller memory card is visibly
present; the large diagnostic decision panel remains unsuitable for a scenic
final walkthrough. This record grants no 7.3/10 visual score or promotion.
