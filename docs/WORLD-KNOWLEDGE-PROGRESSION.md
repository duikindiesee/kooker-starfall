# Knowledge and continuity: future world rules

Design recorded 13 September 2026. These rules do not expand the current integrated preview and are not implemented or runtime-proven by it. Existing saves remain untouched. See the [save contract](SAVE-GAME-CONTRACT.md) and [memory boundary](STARFALL-MEMORY.md).

## Knowledge is progression

A new inhabitant starts with minimal embodied abilities, without unjustified knowledge of local plants, animals, locations, recipes or hazards. Perception, cautious experiments, outcomes and communication create beliefs with evidence provenance, subject, world, inhabitant, time and confidence. A belief may be false; later contradictory evidence can correct it while preserving the original history. Communication records its source rather than automatically establishing truth. Private knowledge cannot leak between inhabitants or worlds.

Future poisonous plants and predators require readable warnings, avoidance and retreat, bounded animal behavior, deterministic Unity authority and fair counterplay. Fire, noise, barriers and sharpened stakes are possible learned defenses, not capabilities granted by a model's text. Food, injury, fire and combat remain authoritative mechanics with explicit preconditions and costs.

## Persistent world, returning inhabitant

Death never regenerates terrain or rewinds weather, ecology, structures, depletion or shared history. Preserve the stable inhabitant identity; give each returned body a new life identifier and link it to the prior life and immutable death event. Return to the starting refuge in a validated safe baseline body state. If that refuge is unsafe, recovery must use an explicitly tested fallback or remain pending; do not silently label it safe.

Proposed ordinary-inventory rule: deposit carried items in one persistent recovery container at the death location, or a validated nearby reachable recovery point when the location is physically inaccessible. Preserve each item identity and provenance; transfer ownership atomically. The returned body does not also retain these items. Essential baseline equipment, if any, must be explicitly enumerated by the world definition. Repeated deaths create distinct recoverable containers rather than overwriting earlier ones.

Unity records the verified fatal cause and contributing conditions with source tick and event identity. Unknown causes remain unknown. Derive at most one new grounded lesson from a death, tied to an existing mechanic and evidence available to that inhabitant. Starvation may reinforce that previously identified safe berries restore energy, or a previously observed planting mechanic. It must not identify an unknown plant, reveal an undiscovered location or invent a recipe. With no eligible lesson, produce none. Persist and visibly label a lesson as remembered insight; later behavior must demonstrate its use before claiming learning worked.

## Acceptance gates before implementation can be called complete

| Gate | Required actual-player and persistence evidence |
|---|---|
| Identity and body | Death event links old and new life IDs to the same inhabitant; return has documented body values and validated refuge clearance. |
| World continuity | Terrain edits, weather tick, depleted plants, structures and history survive death without rewind or regeneration. |
| Inventory recovery | Each item exists exactly once; recovery remains reachable under the declared rule; pickup and a second death cannot duplicate or erase it. |
| Grounded memory | One eligible lesson at most; no lesson from unknown cause; private records stay world/inhabitant scoped; repeated deaths do not farm duplicate knowledge. |
| Visible learning | Insight is visible with evidence provenance; subsequent authoritative behavior uses the learned mechanic with normal perception/reach/resource checks. |
| Save/reload | Save before death, after death, during recovery and after return; load preserves all linked world/body/item/memory states. Interrupted writes fail atomically; old saves remain intact. |
| Failure and fairness | Unsafe refuge, missing service, invalid evidence and inaccessible recovery have explicit tested outcomes; no hidden full reset or knowledge grant. |

## Staged roadmap

After the current integration, berries, refuge and living-thought gates: unknown-food risk; predator perception and escape; shelter threat; learned nonlethal deterrence; only then defensive crafting and combat. Each stage needs concept/reference, actual-player evidence and a truthful shareable draft. External publication requires its own authorization.

Later, an in-world transformation device may convert gathered resources into standardized physical parts, such as wood into planks. Its fiction is undecided. Before implementation, specify deterministic recipes and resource accounting, input/output identity and provenance, atomic consumption/creation, save persistence and physical construction constraints. Parts must be assembled through learned, validated actions; no free manifestation or duplication. This stays after current integration/refuge/food milestones.
