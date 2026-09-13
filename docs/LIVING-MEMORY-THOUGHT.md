# One living memory thought

Isolated 0.0.6-memory-preview.1 implementation. **Runtime acceptance pending until an actual-player report passes.** This does not add death/return, survival, ecology, saved-world migration or cross-world sharing.

The compiled courtyard performs its existing deterministic pickup and delivery. A trusted receipt hook exports identity and successful action receipts through StarfallMemoryExport. The isolated runner creates a new private capability configuration and SQLite database, starts the reviewed v1 HTTP service with outbound networking disabled, and passes an explicit client configuration path to this new player. The publisher and inhabitant reader capabilities are distinct and never enter model context or evidence output.

The player publishes exactly three receipts, checks each returned event hash against its own canonical export, and retrieves at most four own episodes. It rejects another world/inhabitant, nonconfirmed records, an unexpected summary or a mismatched event hash. Only the matching verified delivery episode enters inference. Raw private archives, unrelated episodes, beliefs and dreams are not queried.

The new reflection-only protocol v1 uses exact fields v (version), r (request correlation), d (dialogue), f (reflection). It cannot express a goal or action. This is separate from, and does not relax, the existing action-proposal schema. Both texts must be nonempty printable ASCII within strict lengths. The bounded response has a 64-token cap; reasoning is disabled request-locally. The deadline is the existing gameplay value, **1500 ms**, including model-inventory validation. There is one attempt and no retry/warmup. Timeout, unavailable model, malformed text or interrupted control discards the thought and leaves deterministic behavior authoritative.

While inference runs, the diagnostic advances normal simulation ticks against wall-clock time. Before display it rechecks identity, control state, snapshot age and the actual delivered item/destination state. The reflection must mention the remembered item and completed delivery. Model text is displayed as interpretation of a separately cited verified event, never promoted to a fact or fed into an action API.

The runner queries a second inhabitant (which must have no episodes), rejects a foreign-world query, stops the service, reopens SQLite and verifies the persisted event chain and own episodes. These are real persistence checks; the player's traversal is scripted acceptance input, not physical keyboard acceptance. Failure is preserved, not relabeled a successful thought.

Run only when the coordinated Unity build slot is free:

```powershell
.\tools\build-npc.ps1 -Mode Hybrid
python tools/run-living-memory.py --exe Builds/EXACT_BUILD/KookerStarfallHybrid.exe --output evidence/local/living-memory/NEW_RUN
```

Earlier binaries and evidence remain separate. The runner refuses existing output and does not mutate model defaults or load a model. It uses the explicitly already-loaded E4B exposed through laptop loopback; device attribution must be recorded for each run because LM Link may execute on the Mac.

Death and knowledge progression are [future world rules and acceptance milestones](STARFALL-KNOWLEDGE-PROGRESSION.md), not part of this gate. API v1 remains unchanged and cannot ingest death/return events.
