# Knowledge is progression

User-directed world rules, 13 September 2026. **Design and acceptance contract, not implemented gameplay.** The immediate critical path remains one real delivery, scoped persistence and one model reflection. None of the systems below may be presented as shipped because this document exists.

## Starting knowledge and provenance

A new inhabitant begins cognitively naive: minimal embodied movement, looking, grasping, sensing bodily distress and recognition of the immediately perceived starting refuge. It has no unexplained plant edibility, animal behavior, location, recipe or hazard knowledge. World generation data, hidden entity registries and model pretraining are not inhabitant knowledge sources. The engine filters perceptible information before it reaches any model. A familiar real-world plant name is not proof of a fictional plant's safety.

Knowledge develops through perception, cautious experiments, observed outcomes, communication and explicitly verified lessons. Record the world and inhabitant, source type and source ID, original observation, outcome evidence IDs, proposition/mechanic ID, confidence and its basis, discovery scope, simulation time, and append-only correction/supersession links. Keep observed facts, testimony, hypotheses, model interpretation and engine-verified mechanic lessons distinguishable. Confidence is not truth and is not a probability unless calibrated. Model inference never becomes a verified fact merely by being repeated, saved or confidently phrased.

Corrections append new evidence and supersede a belief without deleting its history. A claim learned in one world or by one inhabitant does not leak into another. Communication is itself a witnessed source with attribution and uncertainty; it does not silently copy an entire private memory. Unknown locations and unseen objects stay undisclosed. Saves bind knowledge to explicit world ID, inhabitant ID and schema version.

## Persistent world, death and return

Death does not regenerate or rewind terrain, weather time, ecology, structures, depleted resources, bags or shared history. The world clock and event ledger continue monotonically. Save/load restores the same world, not the start seed with progress re-applied approximately. Old saves remain unchanged; a future migration produces a separately identified copy only through an explicit versioned migration.

The stable inhabitant identity survives return. Each embodied life has a distinct life epoch; death closes one epoch and return creates the next. Old body handles, pending actions and model replies are cancelled/rejected. They cannot mutate the new body. Death and return IDs are idempotent: replay/reload cannot duplicate a return, bag, item or lesson.

Ordinary carried items move atomically into a recoverable bag keyed by death ID at the death location, or the last valid grounded location if the death position cannot support a bag. Record that deterministic placement decision in the death transaction. Remove those items from the dead body exactly once; the returning body receives none of them automatically. Bags persist across return and reload, with the usual reach, permission, capacity and pickup checks. A recovery hint may refer only to the inhabitant's already perceived route/location; it must not reveal a hidden map. Repeated deaths create separate bags with disjoint ownership; recovered items cannot remain in their old bag.

Return uses the established starting refuge and a defined safe baseline for health, energy, hydration and temperature exposure. The body baseline is explicit and versioned; it does not heal the world, restore depleted resources or rewind weather. If the refuge's return pad is obstructed or unsafe, use only an approved, deterministic refuge-local fallback or keep return pending with an understandable explanation. Do not regenerate terrain or silently reveal a new safe location. Any brief return protection needs a visible, finite rule and separate acceptance.

## Death evidence and one lesson

A future immutable death event must cite engine-verified cause, contributing conditions with measured values, relevant recent action/perception receipts, body/life epoch, inventory transfer and world tick. Attribution comes from deterministic survival/damage systems, never model narration. Multiple contributing conditions must not be flattened into an invented single cause.

At most one new lesson is granted per death. Select an existing mechanic the inhabitant has not learned, from a bounded engine-reviewed eligibility table. Tie it to the death evidence and any necessary prior discoveries. Starvation alone does not prove a particular berry is edible: a lesson about known-safe berries restoring energy requires a verified mechanic rule and an already discovered qualifying food. A seed-planting lesson must not reveal unseen seed sources or recipes not justified by the teaching rule. If no grounded undiscovered lesson is eligible, issue no lesson. Repeated deaths cannot mint new facts or repeatedly award the same lesson.

The optional model may express the selected lesson in concise words; it cannot select a new mechanic, change its evidence, assert locations, or promote its interpretation into fact. Persist the verified lesson independently of model availability. Display it as remembered insight with its source; display optional model reflection as interpretation. Later observed behavior must demonstrate use of the mechanic through ordinary Unity actions, not a decorative HUD badge.

## Staged roadmap after berries, refuge and living thought

1. **Unknown-food risk:** discoverable warnings and cautious, bounded testing; a clear separation between unfamiliar and verified safe food. Poisonous plants are future content, not assumed present today.
2. **Predator perception and escape:** bounded animal sensing/pursuit, readable threat cues, avoidance and retreat routes. Injuries/death require deterministic verified receipts, fair counterplay and repeatable scenarios.
3. **Shelter threat:** an understandable refuge safety model and ways to observe impending danger; no invisible unavoidable attack that teaches only through failure.
4. **Learned nonlethal deterrent:** discover and verify one mechanic such as fire, noise or a barrier, with costs, limits and animal responses. These examples are planned possibilities, not implemented defenses.
5. **Defensive crafting/combat:** only after the preceding stages pass, consider sharpened stakes or other defenses. Require discoverable materials/recipes, deterministic collision/damage, bounded behavior and fair counterplay. Do not grant expert crafting knowledge at spawn.

All stages retain Unity action authority, timeout/cancellation, fallback, memory isolation and separate evidence for source tests versus real-player behavior.

## Future death/return acceptance

| Test | Required observation | Current status |
|---|---|---|
| World continuity | Terrain/edit fingerprint, structure/resource/ecology state and weather/world time survive death without regeneration or rewind | Planned |
| Identity and life epoch | Same inhabitant ID, new body epoch; late replies and old action handles rejected | Planned |
| Inventory conservation | Every carried item appears in exactly one recoverable bag; none duplicated on body/return/replay | Planned |
| Safe body baseline | Return at refuge under explicit body thresholds, without changing world conditions | Planned |
| Occupied/unsafe refuge | Approved fallback or pending return; no hidden relocation or terrain repair | Planned |
| Verified cause | Death receipt agrees with deterministic survival/damage trace and contributing conditions | Planned |
| Lesson eligibility | Zero or one novel, mechanic-valid lesson; prerequisites and discoveries checked | Planned |
| Privacy | Other inhabitant/world queries and life-epoch reply substitution fail; undiscovered locations absent | Planned |
| Save/reload before/after death | Consistent world, bag, body epoch, event chain and lesson; old save bytes unchanged | Planned |
| Repeated death/idempotency | No repeated reward, duplicate bag, duplicate return or rewritten event | Planned |
| Recovery | Actual traversal and valid pickup restores items once; full capacity/denied reach handled safely | Planned |
| Remembered insight | HUD distinguishes verified lesson from model interpretation and cites its death evidence | Planned |
| Demonstrated learning | Later player-observed behavior uses the taught mechanic successfully through normal action checks | Planned |
| Post-return model reflection | One bounded request uses only relevant own verified memory; deadline, strict parser and current-life admission pass | Deferred until living-thought proof |

The present memory API v1 supports identity, pickup/delivery and sleep only. Death, life epochs, recovery, mechanic lessons and belief confidence/correction history need an explicit new schema/migration and service tests. Unknown event kinds remain rejected; this design does not widen API v1 permissions.
