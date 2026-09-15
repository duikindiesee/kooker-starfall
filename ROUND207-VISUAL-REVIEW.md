# Round 207 visual review: water-to-sea

Evidence inspected: `evidence/milestones/coastal/round-207/2026-09-15-02-water-to-sea.png`.
This is an Editor-generated still, not compiled-player acceptance. Reviewed against the user's supplied panorama and explicit distant-gas-giant correction.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Spacious canyon silhouette | Partial | Tall canyon walls frame an open sky and winding water | Broad bare banks and smooth slopes lack the reference's broken foothill geology and clustered vegetation |
| Giant angular scale | Present | Giant fills a substantial portion of the sky | Lower arc meets the valley horizon; a still cannot establish distant depth or lack of local parallax. Require compiled mouth, bank and lookout views plus continuous walking |
| Transparent shallows | Partial | Submerged rocks and plants can be seen | Bed looks washed out; broad straight/radial light bands replace the reference's fine moving caustic network |
| Natural rock detail | Not accepted | Submerged and bank rocks are visibly coarse flat polygon clusters | Need layered, irregular rocky shelves with readable submerged relief |
| Plant placement and density | Not accepted | A few isolated pale succulents and flowers appear on banks and underwater | Need grounded clustered growth around rocks, differentiated aquatic plants, and intentional open areas rather than evenly sparse decoration |
| Overall reference threshold | Not passed | The still has the principal colors and large forms | No defensible 7.3/10 approval; compiled visual review and user acceptance remain outstanding |

Next diagnostic: use existing water debug modes 5-7 in the actual player to distinguish fallback reflection, planar reflection and planar coverage before changing more water parameters. Preserve normal rendering as the comparison. Do not replace the real restart/action test with visual work or call an Editor still a runtime pass.

## Compiled core run follow-up

Inspected `evidence/local/combined/runtime-207-core/runtime/integrated-runtime.json`, SHA256 `5eec798c2d9864b7d73689452e40cdf36c135b92bcfbe6e4d6b1d421f2014df9`. Overall **FAIL**, not a release candidate pass. This was scripted compiled coverage without living-memory inference.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Delivery cycle | Passed in scripted run | Three deliveries, seven memory events | Ordinary sustained and restart survival remain separate |
| Physical cave entry | Passed | 591 controller steps; floor gap 0.020 m | Human navigation review remains |
| Regional shelter | Passed | Interior rain multiplier 0.010 versus exterior 1.000; removed roof restores 1.000 | Hearth safety is not established by shelter credit |
| Paused burning hearth | Failed | Fire tick 3035 and weather tick 4901 both remain unchanged | Ignition/burning precondition failed; do not claim the complete check passed because clocks froze |
| Freshwater approach | Limited pass | 28-waypoint authored route to terrain-fitted seep | Does not prove discovery, line of sight, model choice or actual drinking |

Viewed actual player `01e-distant-giant-canyon-mouth.png`: the giant is large, smaller moons are visible, and the right cliff occludes part of its edge. Nevertheless the nearly full centered disk terminates at the valley floor and retains the nearby-ball impression reported by the user. Numerical distance or this posed still alone cannot establish the requested art direction. Consider higher/off-center framing with more lower-disk geological occlusion while preserving angular size; verify normal walking cameras, not only a posed diagnostic.

Admission audit: `StarfallSurvivalThought` checks exact response model identity, but this compiled source's `StarfallMemoryThought` and legacy `NpcLocalProposalProvider` do not. Their requested model labels alone must not be treated as returned-model identity evidence. Follow-up checks/tests are assigned to the gameplay owner; no prior receipts are silently upgraded.
