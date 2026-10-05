# Round204: constant reflection strength does not remove banding

Seven Unity Editor views rendered successfully with the reflection blend weight
temporarily fixed to `0.3 * _ReflectionStrength`. Reflection sampling, wave
normals, glint, transmission and fog were retained. Independent offshore view07
inspection shows the same strong parallel/checker pattern as round202, at
reduced brightness. Varying Fresnel blend strength is therefore not necessary
for the dominant artifact in this render path.

Together with round203 (zero reflection contribution), this points next to the
sampled environment contribution, including reflection-direction/normal input,
rather than the final blend coefficient alone. This does not identify a specific
texture or prove a planar-camera defect.

Important scope: these are Editor study renders. `CoastalPlanarReflection` has
ordinary Awake/LateUpdate callbacks and no ExecuteAlways attribute; these
captures do not independently establish that its live planar camera ran. The
fallback environment path must be distinguished from compiled-player planar
rendering before claiming a runtime cause or fix.

The exact temporary shader edit was reverted. Evidence remains under
`evidence/milestones/coastal/round-204`. No executable, package, accepted visual
score or release was produced. Next isolate the sampled environment from its
normal-dependent direction, retaining a comparison with real player rendering.
