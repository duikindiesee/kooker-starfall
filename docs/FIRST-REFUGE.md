# First refuge v1 — isolated regional candidate

Workstream: codex/starfall-first-refuge, based on integrated committed checkpoint 2cbb7f0. Earlier branches/builds are preserved. Current authored world is the finite coastal component, not the planned panorama-scale canyon.

The additive `RefugeBuild.Attach` creates a solid raised floor, broad entrance ramp, natural rock enclosure, designated contained hearth, grass mat and small fuel storage. `RefugeRuntime` supplies collision-driven first-person exploration, proximity-gated hearth/refuel/rest actions and a visible lowered rest view. It does not replace or integrate the NPC action executor. Full inhabitant animation and memory/dream integration remain planned.

The hearth uses integer fuel ticks at 50 Hz, a finite six-log reserve, ignition weather limits, explicit extinguish, bounded light/heat and weather/failure extinguishing. No spread API exists. Fuel transfer is atomic and capacity bounded. Runtime state is session-only; world save integration is not implemented.

Water safety is limited to the current fixed regional datum: y=-2 plus the shader sine amplitudes .045+.034+.032, with saturate on wave strength. Upper visual bound is -1.889. Runtime validates water mesh transforms and material strength, actual floor/entrance rays and clearance. No future tide, surge or river flood guarantee is implied. A changed water model invalidates this bound.

Cave zone source is retained from environment-zones c75357f. Per-position roof/wind rays feed attenuation; unknown geometry/water withholds refuge status. Thermal credit comes only from burning hearth proximity. The design does not promise a cold-safe interior in every storm or wind direction. Exterior rain is sampled separately, and a 128-drop visual pool is blocked by roof geometry.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Fire model | Synthetic PASS | evidence/verified/refuge/contract-checks.json; 10013 assertions including 10000 replay ticks | Actual player pending |
| Cave and contents | Authored | Assets/CityLife/Refuge/Editor/RefugeBuild.cs | Unity compilation, collision and visuals pending |
| Compiled regional acceptance | Pending | tools/refuge/build.ps1 and runtime scripted probe | Build slot follows combined candidate |
| Actual inhabitant / dream integration | Unimplemented | No executor or memory edits | Integrate through existing authority after review |

Build with `tools/refuge/build.ps1` when the coordinated Unity slot is free. Source must be committed. A unique versioned Builds folder is created; older output is never replaced. Run the new executable with `-refugeAcceptance -refugeEvidence <new-absolute-directory>` for scripted player evidence; omit flags for manual exploration.

Integration queue: accepted coastal/environment sources -> combined-world candidate -> separately reviewed refuge exact head -> NPC action binding and joint runtime review. The refuge's world/revision is explicit; adopting it must not silently modify saved worlds. No protected-main merge, deployment or MoJoJo acceptance is inferred.
