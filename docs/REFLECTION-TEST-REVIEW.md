# Reflection test attribution review

Source reviewed: `57bdb848c3958e4aa8f5a6bcb6f3ed9ee0dc94ec`.
Round 157 compiled successfully; compilation does not establish reflection quality.

The runtime test adds an important prerequisite: a completed probe render and
assigned texture. However, its off/on image difference is not yet a controlled
measurement of probe contribution. It captures different frames while the water
and caustics animate. `CoastalWater.shader` uses both `_Time.y` and
`_StarfallEnvironmentTime`; the earlier acceptance step explicitly measures
650 ms of that motion. A lower-frame mean difference greater than 0.02 can
therefore occur without a visible probe contribution.

Required correction: hold relevant animation, camera and lighting state constant
for matched off/on captures, or provide a temporal control that separates normal
frame changes from the probe effect. Freezing only the environment clock does
not freeze the shader's direct `_Time.y` ripple. Retain texture completion as a
separate prerequisite and inspect the actual reflected canyon/sky shapes.

No pixel-delta threshold alone establishes panorama-quality water. The view must
also demonstrate clear bed visibility, depth falloff and fine moving caustics
in ordinary play, with a separate motion test after the controlled comparison.
