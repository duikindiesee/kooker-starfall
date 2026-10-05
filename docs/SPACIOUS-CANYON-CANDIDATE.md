# Spacious canyon environment candidate

## Claim boundary

This source revision expands the finite Starfall coastal field from the earlier 180 x 200 metre study to a deterministic 1,200 x 1,600 metre region. It is inspired by the broad scale and layered walls of Fish River Canyon and by the retained Starfall panorama; it is not a surveyed reconstruction of Fish River Canyon.

The authored route follows one continuous turquoise water surface from a narrow southern canyon, around the established rocky kokerboom bank, through a widening northern river mouth and into a reachable seabed/coast region. Six asymmetric terrain masses target 112-156 metre canyon walls. The world remains finite and its camera bounds are enforced at the terrain edge.

## Integrated placements

- The accepted hero bank and frozen tree retain their established metre scale near the origin.
- First Refuge is translated as one authored assembly onto a sampled east-bank terrain shelf near `(-165, 118)`. Its fixture controller remains removed in the combined build.
- The primary food target is now a branched blue-green bush with fifteen individually visible purple-red berries, grounded at the exact terrain sample near `(34, -18)`.
- Build-time setup rejects the berry placement when its measured distance from an actual named shore-rock collider is below three metres.
- The maintained spring remains a separate turquoise source marker near `(-24, 54)`.

## Objective acceptance hooks

The combined-player acceptance report now records:

1. exact terrain span and sampled cliff height (minimum target: 1,200 x 1,600 metres and 80 metres);
2. berry terrain-height delta and nearest actual shore-rock clearance;
3. First Refuge source presence, non-pickup registry identity, and its centre above the sampled terrain;
4. a camera capture aimed from the relocated refuge entrance instead of the obsolete origin coordinates.

## Honest remaining gaps

This commit has not been opened or built in Unity. Runtime collision, visual quality, frame rate, the entire route's walkability, water appearance, refuge entrance clearance and human scale judgement remain unverified until the coordinating task performs the exclusive Unity build and player run. The river surface still has no swimming or fluid physics. Distant sea beyond the 1,200 x 1,600 metre active heightfield remains explicitly visual-only.
