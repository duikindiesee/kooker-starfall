# Play the berry-survival prototype

Current reviewable candidate: round211, source `f27ae2484709396ff6d90b8312c2d1287d9723da`.
Executable: `C:\bots\reflection\work\starfall-combined-today\Builds\KookerStarfallIntegrated-0.0.11-survival.1-20260915-115223\KookerStarfallIntegrated.exe`.
Full184-file fingerprint: `addc6532b7e665f36e6a349a5f1c75f1d4c16119e25eb327ad01c2236bfcebcc`.

## Ordinary launch

Use the tested `tools/play-starfall-with-memory.ps1` launcher, not the smoke or death diagnostic. LM Studio must expose `http://127.0.0.1:1234` with these already-loaded exact instances:

- `google/gemma-4-e4b`: linked Mac MLX, optional immediate delivery reflection,1500ms limit. First/cold requests can fall back.
- `starfall-local-e4b`: local Windows GGUF, asynchronous survival decisions,5000ms limit. Late/invalid answers do not execute.

The command below gives the user a dedicated persistent slot separate from all test stores. This new slot has not yet been launched; the same launcher/model settings were exercised in runs23/24. Keep the same storage path on later launches. A new session gets new evidence folders, not a reset save.

```powershell
$starfallRoot = 'C:\bots\reflection\work\starfall-combined-today'
$starfallSlot = Join-Path $env:LOCALAPPDATA 'Kooker\Starfall\BerryPrototype211'
$starfallSession = Join-Path $starfallSlot ('sessions\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
& "$starfallRoot\tools\play-starfall-with-memory.ps1" `
  -Player "$starfallRoot\Builds\KookerStarfallIntegrated-0.0.11-survival.1-20260915-115223\KookerStarfallIntegrated.exe" `
  -Storage $starfallSlot -Survival `
  -ModelEndpoint 'http://127.0.0.1:1234' `
  -Model 'google/gemma-4-e4b' -SurvivalModel 'starfall-local-e4b' `
  -Evidence (Join-Path $starfallSession 'memory') `
  -SurvivalEvidence (Join-Path $starfallSession 'survival')
```

Leave the launcher running until closing the game normally; it owns the private memory service. Do not delete an incompatible existing slot or silently reset it: the launcher rejects mismatched build/world identities. This is a manually launched Windows prototype, not a packaged one-click installer.

P: options; Tab: possess/release; F: spectator; R: autonomy; right mouse: look; L: decision display; F11: display mode. In a new slot the real three-delivery opening precedes survival. The inhabitant can then observe, gather and eat fruit, retaining scoped body/knowledge and inventory on close/relaunch.

## What is and is not proven

Runs23/24 prove real gathered fruit persisted and became a fresh model-chosen meal after restart: saved request6 -> executed request7, +1200energy/+400hydration/-1fruit. Run22 proves an explicitly accelerated death, grounded cave return and subsequent model action with the retained own-cause lesson in context. This does not establish natural mortality timing or improved survival policy caused by the lesson.

Freshwater discovery/drinking, the panorama-level giant/water/terrain visuals, final human play/club review, full-world saving, protected review, ZIP and final narrated walkthrough remain open. Detailed evidence is in `BERRY-SURVIVAL-MILESTONE-RECEIPT.md`.

Existing silent WIP gameplay video (older round206, not this build): `evidence/local/normal-survival/run-14/starfall-round206-WIP-first-meal.mp4`. Keep its version label; it is not the final narrated before/after.
