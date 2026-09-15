# Round199: larger distant gas giant candidate

The user rejected the giant reading as a sphere floating inside the canyon.
This bounded comparison changes only celestial position/scale and camera far
clip: centre `(9000,16800,42000)`, diameter `34000`, far clip `80000`.
Previous values were `(4500,4200,21000)`, diameter `9800`, clip `40000`.
The shared sphere mesh has radius 0.5 before scaling. Approximate angular diameter
at world origin grows from 26 degrees to 43 degrees while centre distance grows
from about 22 km to 46 km. This is visual staging, not a physical orbital model.

Seven actual Unity Editor views rendered successfully using
`tools/render-coastal.ps1 -Round round-199 -Width 1600`. Independent review of
views04 (canyon) and01 (tree-bank composition) shows a markedly larger planet,
cropped above the frame and visibly occluded by cliffs/sea-stack. Retain this as
the next candidate's comparison direction, not as user or full visual acceptance.

Still required: actual-player near-bank/canyon-mouth/high-view traversal with no
obvious nearby-object parallax, sky/terrain ordering and far-plane regression,
smaller-moon composition, and the user's review. Water banding, sparse planting
and blocky rock forms remain visible and are not fixed by the sky change.

Evidence: `evidence/milestones/coastal/round-199`. No new executable or ZIP was
created by this render-only pass.
