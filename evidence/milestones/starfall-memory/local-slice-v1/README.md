# Starfall memory — reviewed local proof

The first native memory slice passed the actual Unity-export-to-HTTP-service flow. Eight events became per-inhabitant episodes, confirmed wiki facts and sleep-gated dreams. Two inhabitants retained separate private memory namespaces. The service was restarted against the same isolated database and recovered the same ledger, episodes and stored dreams.

The Unity source checkpoint is `ad60138` (full source commit in the export report). The final tested Python source is pinned by SHA256 in the integration and service reports. No running game was connected or replaced, and no container or remote service was deployed.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Unity emits actual action evidence | PASS, 12 checks | [Unity report](unity-memory-validation.json), [exact eight-event export](unity-events.jsonl) | Isolated editor fixture; sleep transitions are explicit test signals |
| Existing world and action checks retained | PASS | [2841 foundation assertions](foundation-validation.json), [26 NPC checks](npc-validation.json) | No new native player/UI/performance acceptance |
| Separate service receives Unity events and derives memory/wiki/dream output | PASS, 50 integration checks | [Actual HTTP/CLI integration](integration-report.json) | Explicit file bridge; continuous live-player outbox is future work |
| Append-only authority, idempotency, isolation and failure boundaries | PASS, 27 service tests | [Named test results and source hashes](service-validation.json) | Trusted publisher/host remain the authority boundary; no administrator-proof storage claim |
| Events survive dreaming and restart | PASS | [Ledger checkpoint](ledger-checkpoint.json), integration restart and unchanged-chain checks | Backup/signing/large-world performance are future hardening |
| Own memories and private theories | PASS | [First inhabitant](memories.json), [second inhabitant](other-inhabitant-memories.json), cross-namespace rejection checks | A complete inhabitant lifecycle requires more event types |
| Wiki separates fact from belief/theory | PASS | [Derived wiki pages](wiki.json), [stored dream](dream.json) | Offline rules, not a language model or learning |
| Dedicated container/volume recipe | CONFIG VALIDATED | [Sanitized Compose evidence](container-config-validation.json) | Image/container, volume and secret permissions not exercised; not deployed |
| Isolated editor settings preserved | PASS | [Four exact source/settings hashes](isolated-settings-preservation.json) | This is settings preservation, not a new player runtime check |

## Actual derived depot page

The confirmed fact is: **inhabitant-01 delivered amber to depot-west at tick 20.** Its event ID is `339d930140c0947da960ccb3769be689fae5793b705ad70b152c03f0bfdb2e55`.

The separate dream theory says: **Perhaps depot-west could be useful to visit again.** It cites the delivery but grants no access and does not assert that the depot is currently available. The wiki retains two occurrences from two distinct sleep periods in its dream history. The second inhabitant's private memory contains no delivery event and therefore produces no delivery association.

The public artifact set contains only reviewed synthetic JSON/JSONL and this explanation. Databases, capability files, full editor/service logs and intermediate proof runs stay in ignored local evidence. The proof removed its temporary capability file after shutting down its own service process. [Architecture and scope](../../../../docs/STARFALL-MEMORY.md) describe the authority and deployment boundaries.
