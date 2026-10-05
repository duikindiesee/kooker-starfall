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

## Additional canyon and offshore review

Independently inspected `2026-09-15-04-canyon-opening.png` and
`2026-09-15-07-offshore-islands-sea-vista.png` in this same round. In the canyon
view, the right cliff and smaller sea-stack visibly occlude the giant: this
specific retained view does not show a planet rendered in front of those rocks.
Its low, strongly defined circular edge still makes it read close to the canyon
mouth. This is a composition concern even when the source sphere is distant.
The user's screenshot has a different apparent planet elevation; its build
identity is unverified. Do not dismiss it or claim current-player acceptance
from this older Editor view.

The offshore view exposes conspicuous parallel/checker-like wave repetition,
a pale flat horizon, and isolated rounded cliff blocks without convincing
shoreline breakup. It does not yet match the reference's richly layered rocky
coast. The canyon floor is largely bare and the underwater plants appear as
widely separated repeated rosettes rather than clustered reef growth.

Next visual iteration priority: current-player sky occlusion and angular scale
from three viewpoints; suppress repetitive water bands while retaining actual
bed visibility; add believable rock/shore transitions and planted clusters.
Do not replace traversal, depth or actual reflections with a reference billboard.
These are observed gaps, not an additional completed visual milestone.
