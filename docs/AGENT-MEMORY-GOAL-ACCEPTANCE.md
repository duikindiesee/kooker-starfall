# Agent memory and planning: active goal implementation order

User-authorized scope, 15 September 2026. This is an acceptance map, not a
claim that the following capabilities are implemented or runtime accepted.

## Dependency order

1. **Observed spatial memory.** Persist explored cells and discovered places
   under world, inhabitant and world-revision identity. Record observations
   with simulation time, source and location. Unobserved locations remain
   unknown even when Unity can navigate through them.
2. **Place timelines and belief revision.** Keep previous observations when a
   later visit discovers a change. A current belief points to its evidence;
   it is not a continuously synchronized copy of the live world registry.
3. **Known-place navigation.** Resolve a requested known destination to an
   allowed route. Unknown destinations require discovery or attributed
   directions. Validate that the destination is still usable upon arrival.
4. **Bounded planning.** Retrieve relevant observations and propose a short
   needs/day plan. Execute its steps through deterministic game actions.
   Replan on meaningful thresholds, failure or relevant new evidence, with
   hysteresis, cooldown and measured request budgets. Urgent safety behavior
   must not wait on a model. Retain current response validation/deadlines.
5. **Conversation bridge.** Telegram uses the same actor/world identity and
   memory retrieval. Restrict access, protect credentials and validate queued
   requests. Offline discussion cannot claim fresh observations or actions.
6. **Productive baseline.** Once the preceding loop is stable, conserve inputs
   through fallen wood, planks and an approved bedding/shelter recipe.

Evaluate Hermes reuse versus an equivalent small service before selecting a
runtime dependency. Long-term memory, permitted tools and consistent identity
are requirements; a particular agent framework is not. The in-world computer
and generated asset/code pipeline remain future design, not unrestricted host
access or automatic publication into the playable world.

## Required evidence

| Claim | Current status | Required evidence | Remaining gap |
|---|---|---|---|
| Explored areas persist | Not accepted | Discover, save, restart, compare observed cells | Implement and prove in player |
| Place history is grounded | Not accepted | Two visits, distinct timestamped events and sources | Preserve prior event on update |
| Changed world revises belief | Not accepted | Controlled change unseen until revisit, then new belief | No live registry leakage |
| Actor can return to known place | Not accepted | User/plan request, route, actual arrival | Unknown-place rejection/directions |
| Needs planning reduces calls | Not accepted | Measured calls and sustained completed actions | Define and test trigger policy |
| Identity isolation | Not accepted | Wrong actor/world/revision cannot read another store | Tests plus restart demonstration |
| Telegram shares inhabitant | Not accepted | Grounded conversation and executed request receipt | Secure setup and live bridge |

Development resets must be explicit and retain the prior save separately.
Ordinary restarts and death do not reset the world. Archive incompatible map
revisions rather than silently attaching old knowledge to new geography.

All existing environment, spring, clothing/grip, controls, delivery, model
provenance, visual critique and protected-review gates remain required. A
successful new memory test does not clear them. Final evidence must identify
the actual candidate source/build; incompatible historical builds cannot be
combined into one passing release. Retain a verified fallback under the
repository build-retention policy while validating the replacement.
