# Agent runtime decision: conditional, not installed

15 September 2026. Scope: evaluate Hermes reuse versus a small dedicated
Starfall service without changing the active model, player or credentials.

## Evidence and choice

The [official Hermes repository](https://github.com/NousResearch/hermes-agent)
describes persistent agent memory, toolsets and a messaging gateway including
Telegram. These are relevant reusable capabilities. They do not establish
Starfall-specific world isolation, observed-place provenance or game-command
validation. No Hermes installation or live game integration is claimed here.

Prefer a framework-neutral game boundary first. Consider Hermes as a
conversation/planning host only after a bounded compatibility test. A small
dedicated bridge is the alternative if Hermes cannot restrict tools and memory
to that boundary. This is not a decision to reimplement every agent feature.

## Shared contract required by either runtime

- Bind every session to authenticated user, world, inhabitant and map revision.
- Retrieve only the inhabitant's recorded observations and attributed messages.
- Never treat conversation summaries as verified world observations.
- Submit only typed game requests with an idempotency key and expiry.
- Unity rechecks eligibility, known destination, current state and permissions.
- Return separate accepted, executing, completed and rejected receipts.
- A queued request is not a completed action; offline mode reports unavailable
  execution, never fictional movement or discovery.
- Keep message receipt time separate from simulation observation time.
- No generic terminal, filesystem or generated-code execution in the initial
  inhabitant tool surface. Later crafting generation has a separate review gate.

## Selection experiment

Use a synthetic isolated world and actor, not existing user saves or bot tokens.
Run identical tests against both candidate adapters: retrieve one known place;
reject another actor's place; preserve a historical observation after revision;
reject an unknown destination; deduplicate a repeated request; expire a stale
request; survive service restart; report offline truthfully. Then test the
selected adapter with the compiled game and authorized private Telegram bot.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Hermes offers relevant building blocks | Documentation verified | Official repository | Pinned version and compatibility experiment |
| Starfall can use Hermes safely | Unverified | No integration test yet | Contract/tool isolation and latency |
| Dedicated bridge is sufficient | Proposed alternative | Contract above | Implementation and same tests |
| Telegram controls this inhabitant | Not implemented here | No live exchange receipt | Secure setup, adapter and player test |
