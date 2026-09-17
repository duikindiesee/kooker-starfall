# Physical items: expanded goal acceptance

User goal, 15 September 2026. Requirements, not implemented-feature claims.
Antigravity implements; Codex reviews; authoritative tracking belongs to the
dedicated Starfall writer. On 16 September 2026 the user approved the revised
Living, Physical World goal and coordination was resumed. The writer must
record normal evidence-backed setup review transitions before implementation
claims; approval does not waive gameplay acceptance or MoJoJo merge review.

## Contracts before assets

Every movable asset declares stable identity, dimensions in metres, mass in
kilograms, collider representation, carry limits and supported states. Fixed
terrain and furnishings are explicitly anchored. Do not add dynamic rigidbodies
to every decorative mesh. Document world gravity and test at the chosen value.

One state owner controls free, carried, stored and placed transitions. A held
item may be kinematic, but releasing it must restore appropriate collision and
gravity. Inventory and visible world instances cannot both own the same item.
Container payload contributes to carried mass without double-counting contents.
Recipes conserve declared quantities; offcuts/waste must be explicit outputs
where relevant, not silently lost or invented material.

## Required compiled-player evidence

| Requirement | Test | Required evidence |
|---|---|---|
| Gravity and contact | Drop representative wood, plank, basket and loose stone onto flat ground and slopes | Clip plus measured settling, penetration and velocity tolerances |
| Stable transitions | Repeated pickup, carry, drop, store, retrieve and supported placement | State receipts and no duplicate instances, hovering or collision explosions |
| Weight and capacity | Compare empty/full basket, limit boundary and overweight pickup | Declared masses, totals and visible accepted/rejected action |
| Construction | Consume exact recipe and place one supported improvement | Before/after inventory, collider and grounding observations |
| Persistence | Save/reload loose, stored and placed objects and reconcile in-flight actions | Same identity, quantities, ownership and grounded transforms; old save preserved |
| Performance | Test representative populated area, not only one object | Frame/physics measurements and explicit hardware/build identity |

Choose and record numeric tolerances before acceptance. Do not use a generous
post-hoc threshold to hide unstable physics. Include both small and large items;
test moving contacts and edge cases appropriate to their intended gameplay.

## Integration order

Physical schema and transition ownership precede movable resource/asset
integration. Then validate fallen wood, planks, storage and one bedding/shelter
improvement in the cave-to-resource loop. Spatial memory and needs planning use
the same authoritative inventory and action results, never model-invented mass
or successful crafting. Only one worker owns Unity build/test at a time.

## Visual scope

Assets must support the approved warm sandstone cave, textured ground, woven
bedding/storage, hearth and cool turquoise canyon vista. Giant planet and moons
remain behind landscape. The 7.3/10 critique requires actual normal-player views
of interior, entrance, resource route and water, not concept images or only a
posed panorama. Human play review and truthful recording remain separate gates.
