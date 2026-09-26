# Stone Asset Provenance & Verification Evidence

## 1. Dispatch Provenance
- **Task ID**: `86b2076d-1b15-41c6-b01c-8605b72c1d10`
- **Stream ID**: `da449324-f00d-4976-aceb-ff3eb3bbcfc0`
- **Worker**: `starfall-agy-03`
- **Model**: `gemini-3.8-flash-high`
- **Base Head**: `f6fc9101e829118636f3315718193892bd102b73`
- **Scope**: Asset-only source correction pass for procedural stone meshes, shading modes, spatial UVs, and explicit metre-scale metadata.

---

## 2. Source Geometry & Authoring Integrity
- **Generator**: `CityLife.Stones.StoneMeshGenerator`
- **PRNG**: Custom deterministic `DeterministicPrng` using Murmur3 32-bit finalizer mix and Xorshift32.
  - Zero calls to `UnityEngine.Random` or `System.Random`.
  - Zero dependence on clock time, frame rate, or traversal order.
- **Entrypoint Validation**:
  - Validates `StoneGenerationConfig` on every public method (`Generate`, `GenerateMesh`, `EstimateMetadata`) and in constructor.
  - Rejects NaN, infinite, negative, zero, and out-of-range scale (supported scale: $[0.25, 3.00]$).
  - Rejects undefined shape enums.
  - Explicitly rejects `default(StoneGenerationConfig)` (`uniformScale = 0.0f`).
  - Pre-mesh validation ensures finite coordinates and valid metre-scale bounding extents before creating Mesh.
- **Topology & Shading Modes**:
  - Flat-shaded: 320 triangles, 960 independent vertices with crisp face normals for directional sunlit facet definition.
  - Smooth-shaded: Shared positions with area-weighted smooth normals (~171 vertices), splitting vertices only along the UV longitudinal seam.
  - Smooth normals maintain shared-position smoothing across seam-split duplicate vertices.
  - Metadata vertex count matches actual Mesh vertex count exactly across both modes.
- **Spatial UVs & Tangents**:
  - Nondegenerate spatial spherical unwrap based on direction vectors.
  - Longitudinal seam crossing triangles shift low-longitude vertices by $+1.0$ to prevent backward wrapping.
  - Guaranteed non-zero per-triangle UV determinant ($|\det(UV)| > 10^{-8}$).
  - MikkTSpace tangents verified for finite unit length, tangent-normal orthogonality ($|\mathbf{t} \cdot \mathbf{n}| < 0.02$), and valid handedness ($\pm 1$).
- **Usable Pivot & Ground Resting**:
  - Bottom-center pivot aligns lowest bounding vertex to $y = 0.0\text{ m}$ and centers horizontally in XZ on horizontal surfaces.
  - Truthful slope limitation: Bottom-center pivot alone cannot guarantee clearance or stable multi-point contact on sloped or uneven terrain; slope resting requires collision geometry and physics resolution.
- **Design Mass Assumptions**:
  - Documented mass values are authored design metadata based on standard lithic density ($2600\text{ kg/m}^3$) and shape form factors ($0.46 - 0.68$). They do not represent measured physics simulation evidence.
- **Determinism Validation Suite**:
  - Authored validation suite compares every component of every promised buffer (positions xyz, normals xyz, UVs xy, colors rgba, tangents xyzw, triangle indices) across both flat-shaded and smooth-shaded repeat runs using explicit scalar IEEE 754 bit comparisons via compatible BitConverter APIs (`BitConverter.ToInt32(BitConverter.GetBytes(...))`).
  - Pre-checks array lengths before indexing on all buffers; avoids shared early-break shortcuts that could skip subsequent buffers.
  - Verifies all 18 public deterministic StoneMetadata fields (shape, seed, variantIndex, uniformScale, flatShaded, width, height, depth, boundingVolume, formFactor, estimatedVolume, assumedDensity, intendedMass, vertexCount, triangleCount, boundsMin, boundsMax, pivotOffset).
  - Authored regression suite checks that comparison helpers distinguish one-bit float differences and signed zero (+0.0f vs -0.0f) while matching identical values and guarding against buffer length mismatches.
  - Preserves distinct seed and variant divergence assertions (`variantDiffPositions = true` on divergence distance > 0.001m).

---

## 3. Explicit Negative Assertions & Disclaimers
1. **No Fabricated Renders**: In accordance with project rules, no synthetic screenshots, mockup images, or unverified renders have been committed.
2. **No Alteration of Diagnostic Assets**: Existing diagnostic canyon-stone (`Starfall.Refuge.RefugeStone` / `canyon-stone`) has not been altered or replaced.
3. **No Shared Core Modification**: No files in `Assets/CityLife/Items/**`, scenes, brains, or save files were touched.
4. **No Premature Type Registration**: Canonical item type definitions are not registered in this pass; core runtime ownership remains with AG2.
5. **Authored Validation Unrun**: The validation suite in `Assets/CityLife/Stones/Editor/StoneValidation.cs` is authored for future CI/editor validation but was not executed during this pass. No compile, render, or runtime acceptance is claimed.
6. **No Leaked Mesh Resources**: Validation suite tracks and destroys every created `Mesh` via `DestroyImmediate` in `finally` blocks.

---

## 4. Outstanding Verifications Checklist

| Stage | Check Description | Current State | Target Pass |
| :--- | :--- | :--- | :--- |
| **Source** | Clean C# compilation in standalone environment | Author-Complete | Interactive CI / Build pass |
| **Unity** | Unity editor load, meta GUID resolution, and compilation | **UNVERIFIED** | Unity Editor Pass |
| **Validation**| Execution of `CityLife.Stones.Editor.StoneValidation.Run()` | **UNVERIFIED** | Unity Editor / Test Pass |
| **Visual** | Close-up inspect captures (facet shading, silhouette, textures) | **UNVERIFIED** | Interactive Render Pass |
| **Inhabitant**| Inhabitant hand-grasp and terrain placement captures | **UNVERIFIED** | Character / Scene Pass |
| **Runtime** | Registration into canonical `ItemDefinition` registry | **PENDING AG2** | AG2 Shared Runtime Pass |
