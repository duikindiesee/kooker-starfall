# Round203: reflection blend isolation, diagnostic only

Seven actual Unity Editor views were produced with only the reflection blend
weight temporarily set to zero in `CoastalWater.shader`. Wave normals, glint,
glimmer, shore edge, fog and base transmission remained enabled. This was not
a playable build or an acceptable replacement water material.

Independent comparison of offshore view07 against round202 shows the strong
bright parallel/checker pattern substantially disappears when reflection
blending is removed. Faint dark surface variation remains. The resulting sea
is excessively dark and loses the required reflected scene; disabling reflection
is not a fix and must not be retained as one.

The experiment narrows the dominant bright artifact to the reflection branch:
the environment samples and/or their Fresnel-weight variation. It does not yet
distinguish the planar texture, probe fallback, projection or wave-normal-driven
Fresnel as the specific cause. The coarse dark coastline/depth transition also
remains and is a separate defect.

The exact temporary shader edit was reverted after capture. Evidence:
`evidence/milestones/coastal/round-203`, comparison with round202 view07.
Next controlled test should separate a constant reflection weight from the
existing variable Fresnel weight, retaining actual reflection sampling.
No reference-quality score, player acceptance or release is granted.
