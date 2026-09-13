# Starfall memory contracts v1

One database belongs to one configured world and publisher. Identifiers match `[a-zA-Z0-9][a-zA-Z0-9._-]{0,95}`. Every endpoint requires a bearer capability. The publisher may append Unity events; each inhabitant capability is bound to exactly one memory namespace. Request bodies never select that namespace. No cookies, browser origins, remote endpoints, redirects, CORS, archive connector, model adapter or game command endpoint are supported.

| Method / route | Capability | Contract |
|---|---|---|
| POST `/v1/events` | Publisher | [Event JSON Schema](event-v1.schema.json); return `inserted`, content-addressed `event_id`, `chain_hash` |
| GET `/v1/health` | Either | `starfall.health.v1`, world, event count and verified chain head |
| GET `/v1/identity` | Inhabitant | Own stable identity and registration evidence |
| GET `/v1/memories` | Inhabitant | Own episodic records, each citing immutable event IDs |
| GET `/v1/facts` | Inhabitant | Shared confirmed past-event facts in this world |
| GET `/v1/beliefs` | Inhabitant | Own beliefs/theories; separate from facts |
| POST `/v1/beliefs` | Inhabitant | `starfall.belief.v1`, `request_id`, `label` (`belief` or `theory`), printable `text` (1–500 characters), `evidence_ids` (1–16 unique own event IDs) |
| GET `/v1/dreams` | Inhabitant | Own immutable derived dream records |
| POST `/v1/dreams` | Inhabitant | Exactly `{"schema":"starfall.dream.request.v1"}`; require latest verified sleep state; one idempotent result per sleep-start event |
| GET `/v1/wiki` | Inhabitant | `starfall.wiki.v1`; subject pages with confirmed facts, own beliefs, and own dream theories in distinct fields |

Unknown fields, unsupported routes/methods, duplicate JSON keys and nonfinite numbers fail closed. HTTP bodies require a single explicit Content-Length and JSON content type, at most 16 KiB. Events additionally have an 8 KiB canonical limit. Up to eight HTTP workers; socket reads time out after five seconds. Queries accept only nonnegative `after` and `limit` from 1–100. Responses use `{items, has_more, next_after}` for pagination. The wiki shows the first 100 records per section and explicitly carries those pagination flags; the paginated endpoints retrieve the rest. The prototype uses synchronous SQLite transactions and bounded dream windows, not a high-throughput production server.

## Event authority and evidence

The trusted Unity publisher must use a separately configured capability and approved build ID. The exporter records successful, nonduplicate receipts from `NpcActionApi.Execute`; it never turns NPC narration into an event. The service checks schema, configured provenance, contiguous source sequence, monotonic ticks within a session, registered identity, cargo history, unique item ownership, destination occupancy and sleep transitions. The configured publisher is the trust boundary: the service does not rerun Unity physics or cryptographically prove a compromised engine truthful. A future live transport needs source attestation and a durable retry outbox; this slice proves the explicit file-export bridge.

The event ID is SHA256 of canonical JSON: sorted keys, ASCII escaping, no whitespace, finite values only. The chain hash is SHA256 of `previous_hash + "\n" + canonical_event`; the first previous hash is 64 zeros. `(source.session_id, source.sequence)` is unique. Same-content retries return `inserted:false`; conflicting reuse returns 409. Failed validation and failed derivation roll back the entire event transaction. Source sessions are scoped under the database's fixed world/publisher, and stable inhabitant identity persists across sessions.

SQLite `user_version=1` owns `metadata`, `events`, and `records`. UPDATE and DELETE triggers protect all three tables. Event bytes and the chain are checked on startup and health inspection. Derived records are append-only and cite events; confirmed facts are generated only from identity or completed-action events. The process has no event update/delete API. This is not protection against a filesystem owner or database administrator who deliberately removes triggers and rewrites the entire ledger; external signed checkpoints/backup are future hardening.

## Derived record shape

All stored records carry `schema:starfall.memory.record.v1`, `world_id`, `inhabitant_id`, `kind`, and nonempty `evidence_ids`. Read responses add a numeric cursor; clients must treat all text as content and escape it if rendering HTML.

- `identity`: `display_name`; exactly one registration per stable inhabitant ID.
- `episode`: `epistemic_status:confirmed_event`, `summary`, simulation `tick`.
- `wiki_fact`: `epistemic_status:confirmed_event`, `summary`, `subjects`, `temporal_scope:past_event_only`. A past delivery is not a claim that a depot remains available or that future access is permitted.
- `belief`: `epistemic_status:belief|theory`, `text`, `request_id`. Evidence must belong to that inhabitant. A theory can cite a real event and still be wrong.
- `dream`: `epistemic_status:derived_summary_and_theories`, `processor:starfall.dream.rules.v1`, `sleep_event_id`, `summary`, `associations`, `window_limit:32`, `authority:derived_only`. Each association has `epistemic_status:theory`, text and event evidence. Other inhabitants' episodes and private beliefs are excluded.

Dreaming currently uses deterministic templates and the last 32 own events at the sleep boundary. It is invoked explicitly through the API, requires verified sleep, and produces no action requests, permissions, event corrections, model calls or learning. New sleep starts create new derived records. Repeating the request in the same sleep returns the existing record. A waking event closes the window. An automatic in-game sleep scheduler and optional language-model summarizer are separate future integrations.

## Version and correction policy

Unknown event, request, configuration and database versions are rejected. No automatic migration, world regeneration, identity rename, event rewrite, reset, transfer, death or depot-emptying event exists in v1. Those need explicit new contracts and migration tests. Corrections must be appended under a future explicit correction schema that preserves the superseded evidence; neither a dream nor an administrator API can silently overwrite history in this version. Derived processors are named/versioned so future rebuilds can be written separately and traced to their source events.
