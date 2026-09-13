# Starfall memory service

Separate local event ledger, episodic memory, derived wiki and sleep-gated dreaming. Standard-library Python service; no Reflection/Archive Keeper dependency or personal data integration.

- [Architecture, local setup and proof](../../docs/STARFALL-MEMORY.md)
- [Starfall system architecture and installation state](../../docs/SYSTEM-ARCHITECTURE.md)
- [Versioned API and record contracts](contracts/API-v1.md)
- [Event JSON Schema](contracts/event-v1.schema.json)
- [Container recipe](compose.yaml) (not deployed)

Run `python -m unittest -v test_memory` in this directory. Normal local startup uses explicit `--config` and `--data-dir`; there is no implicit search for another application's configuration. See the architecture guide for credential initialization and the loopback-only CLI.
