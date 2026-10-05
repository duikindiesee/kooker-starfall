# Round197: rejected surface-colour filtering experiment

Seven actual Unity Editor views rendered successfully at 1600 pixels wide.
This is not compiled-player acceptance. The render command was
`tools/render-coastal.ps1 -Round round-197 -Width 1600`.

The experiment expanded `sin(A)*cos(B)` glimmer into half the sum of
`sin(A+B)` and `sin(A-B)`, attenuating each term with
`1-smoothstep(.6,2.1,fwidth(phase))`. The shoreline pulse was similarly
filtered toward its midpoint. No terrain, saves, gameplay, depth transmission
or actual-reflection sampling changed.

Independent review of the offshore view (07) and matched shallow-water view (06)
still shows the conspicuous repeated bands. The experiment does not demonstrate
a useful correction of the reported defect. Its source change was reverted,
and all seven rendered views remain as negative evidence in
`evidence/milestones/coastal/round-197`.

Next diagnosis should isolate surface-normal/Fresnel/specular, reflection input
and coarse water geometry contributions before further tuning. This result
does not establish which of those contributions causes the dominant pattern.
The reference target and actual-player reflection/caustic gates remain open.
