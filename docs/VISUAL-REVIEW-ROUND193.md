# Round193 water comparison — partial improvement, not acceptance

Root inspected matched actual Unity renders from rounds190, 192 and193. These
are Editor-rendered views, not compiled-player visual acceptance.

Round190's gray-green veil obscured the colour separation between dry land and
water. Round192 replaced a uniform bed/colour mix with view-angle-dependent
wavelength attenuation; its inverse-RGB scattering was too green. Round193
retains the ray-dependent bed transmission but uses independently scaled
scattering. The water is visibly more turquoise and less milky, while foreground
submerged rock shapes remain visible. This bounded improvement is retained for
the next compiled comparison; it does not establish reference fidelity.

Evidence:

- `evidence/milestones/coastal/round-190/2026-09-15-06-shallow-water-matched-diagnostic.png`
- `evidence/milestones/coastal/round-192/2026-09-15-06-shallow-water-matched-diagnostic.png`
- `evidence/milestones/coastal/round-193/2026-09-15-06-shallow-water-matched-diagnostic.png`
- `evidence/milestones/coastal/round-193/2026-09-15-02-water-to-sea.png`

Remaining: broad repetitive ripple bands, weak focused bed-light patterns,
blocky submerged rocks, sparse/unconvincing aquatic plants and a pale horizon
transition. Check distant-sea continuity after the changed scattering weight,
plus the existing actual-reflection and moving-caustic regression in the next
compiled player. No 7.3/10 score or user acceptance is awarded.
