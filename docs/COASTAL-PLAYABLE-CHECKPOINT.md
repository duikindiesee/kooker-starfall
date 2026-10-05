# Coastal playable checkpoint

This checkpoint turns the previously offscreen coastal component study into a separately built, bounded Windows player. It does not merge the coast into the hybrid NPC or Starfall memory branches and does not replace any earlier player.

## Visual authority and scale

The original four-screen Starfall panorama is the visual acceptance authority. The close coastal side-view is only a repeatable component/camera study. The current finite terrain spans 180 by 200 metres, from x -90 to 90 and z -55 to 145, with a 176 by 196 metre controller boundary. It contains a connected river-to-sea heightfield rather than a backdrop-only island. The distant ocean beyond z 145 remains visual-only.

Objective criteria for this checkpoint:

| Criterion | Evidence | Current status |
|---|---|---|
| Finite explorable land | Controller bounds and actual coastal terrain mesh/collider | Automated player route passes; native keyboard feel pending |
| Environmental-scale canyon frame | Terrain extents 180 by 200 m; mesas reach roughly 20-30 m above the shore | Present in actual Unity views; panorama-level spaciousness needs user review |
| Connected river and sea | One water surface over one continuous carved river/seabed heightfield | Present; swimming and underwater camera are absent |
| Rocky hero bank | 84 rendered rock meshes have matching static colliders | Automated collision inventory passes |
| Frozen pale Kookerboom | Frozen R19 tree identity and mesh hash retained | Present; one actual trunk collider |
| Starfall sky | Blue gas giant and procedural galaxy are baked into the player scene | Present; visual match needs user review |
| Later inhabitant integration | Stable world identity `starfall.coastal-slice.v1`, deterministic seeds and explicit finite bounds | Ready as an integration target; no inhabitant is included |

## Evidence boundary

The build script creates a new timestamped output and refuses to reuse populated evidence rounds. The smoke script launches only the selected player in hidden verification mode. It verifies the built scene identity, major visual subjects, collider inventory and real controller movement route, then writes a report and exits.

Passing this smoke is actual built-player evidence, but it is not native user-input acceptance, sustained performance evidence, swimming, underwater continuity, population, saved-world compatibility or NPC integration. Those remain separate gates.

## Reproduce

```powershell
.\tools\build-coastal.ps1 -Round round-06
.\tools\smoke-coastal.ps1 -Player '<new executable path from the build result>'
```

Use a fresh round name. Existing builds and evidence are intentionally preserved.
