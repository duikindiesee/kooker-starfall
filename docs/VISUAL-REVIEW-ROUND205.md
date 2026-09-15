# Round205: flat environment reflection direction alone is insufficient

Seven Editor views were rendered with `reflectionDirection` calculated from
`(0,1,0)` instead of the wave normal. Other wave-normal uses, including Fresnel
and glint, remained unchanged. Offshore view07 still contains strong parallel
and checker bands. The temporary edit was reverted exactly afterward.

This rules out that single input change as a sufficient remedy. It does not
rule out multiple contributing terms or a planar reflection texture, which
does not use this direction variable. Earlier round204 fixed the blend weight
but retained sampling variation; independent negative tests must not be
combined into an unjustified claim that both paths are innocent.

Evidence: `evidence/milestones/coastal/round-205`. These are Editor views, not
proof the live planar camera executed. Next capture should expose reflection
availability and sampled colour directly, before further aesthetic tuning.
No gameplay build, visual acceptance or shader correction is claimed.
