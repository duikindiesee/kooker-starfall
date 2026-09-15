# Play the berry-survival prototype

Sole retained verified playable fallback: round211, source `f27ae2484709396ff6d90b8312c2d1287d9723da`. This is not final user acceptance.
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

Round211 run25 now proves ordinary freshwater discovery, inspection and drinking as well as meals over357.904seconds. The strict technical survival verifier passed against its scoped receipt and the separately accelerated run22 diagnostic. Repeated spring approaches while already hydrated remain a behavior defect, not complete survival-policy acceptance.

Newer rounds212/215/216/221 failed broad regression and their temporary binaries were removed; keep using round211 above. Round216 run26 separately proved seven meals, one spring drink and eight full-hydration menus without spring actions. Round221 run27 proved five meals and53 completed routes, but18 model timeouts and no spring discovery. Do not combine these different builds into one acceptance claim.

The panorama-level giant/water/terrain visuals, final human play/club review, full-world saving, protected review, ZIP and final watchable narrated walkthrough remain open. Detailed evidence is in `BERRY-SURVIVAL-MILESTONE-RECEIPT.md`.

Latest silent historical WIP video: `evidence/local/normal-survival/run-27/starfall-round221-WIP-ordinary-survival.mp4`,120seconds, SHA256 `472932d7cd83257f7ce2bcf9dd2ef615f69d40809724b41fea0195ff82b1988c`. Encoding/full-decode passed; source frames show the actor and berry/exploration activity, mostly against dark slopes. Round221 later failed its immediate-thought gate at1503ms and was deleted. The video remains valid historical evidence, not a current accepted player or final narrated showcase.

Remaining: one same-build combined survival/restart/grounded death-lesson demonstration with viewable gameplay; panorama visual/user acceptance; reliable immediate1500ms model response (separate from5000ms survival choices); full-world saving and final deliverables. Camera improvements remain unaccepted. Authoritative Task API epic/workstream IDs have not been verified, so linkage remains unresolved rather than invented.
