# Tinder / Dry-Brush Visual Asset Provenance

Recorded 16 September 2026.  
Status: **AUTHOR-COMPLETED SOURCE-ONLY**.  
Task Reference: `Task92025188-fee5-482e-bd98-0605bf18d4d4` (worker04).  
Checkout Base: `f6fc9101e829118636f3315718193892bd102b73`  
Branch: `codex/starfall-agy-04-tinder-fire`  
Verification Status: **COMPILATION, RUNTIME BOUNDS, AND RENDERING UNVERIFIED (NOT RUN)**.  
Future Unity execution, compilation, and visual acceptance remain subject to coordinator authorization.

---

## 1. Executive Summary & Boundaries

This deliverable contains an original visual source asset, standalone deterministic procedural geometry generator, project-compatible URP 17.6.0 vertex-color shader, material asset, and validation helper suite for a dry-brush / tinder bundle in Unity C#.

- **Scope Limitation**: Visual asset, shader/material, and procedural geometry generation only (`Assets/CityLife/Fire/**`, `docs/tinder-fire/**`, `evidence/tinder-fire/**`).
- **Core Ownership**: AG2 owns the active shared core. No shared Items, scenes, save files, adapters, consumption authority, warmth gameplay, or speculative interfaces are modified or introduced.
- **Resource Authority**: No canonical resource type ID has been approved. `stone.collectable.v1` remains provisional and must not be registered or treated as implemented. No fire, fuel, or tinder resource type is registered in the core runtime.
- **Gameplay Independence**: No parallel inventory, no fake fuel transactions, no warmth mechanics, no cold relief, and no fire recipes.
- **World Preservation Compliance**: Adheres strictly to `AGENTS.md` world-preservation rules:
  - Deterministic generation does NOT depend on clock time (`System.DateTime`), render frame count (`Time.frameCount`), or `UnityEngine.Random`.
  - World definitions and saved worlds are untouched and fully preserved.
  - Source visuals and documentation are not playable evidence.

---

## 2. Procedural Origin & Determinism Limits

The procedural dry-brush / tinder bundle is generated algorithmically as a self-contained 3D geometric cluster representing an arid kindling nest:

1. **Integer PRNG & Determinism Limits**:
   - Implemented via a private, platform-independent 32-bit Xorshift pseudorandom generator (`SeededRng`).
   - Driven entirely by an explicit integer seed (`Seed = 4217` default).
   - **Platform Determinism Limits**: The PRNG integer sequence is bit-identical across platforms. However, mesh geometry math relies on IEEE 754 single-precision floating point operations (`Mathf.Sin`, `Mathf.Cos`, `Mathf.Sqrt`, vector normalization). Hardware architectures, compiler optimizations, and SIMD/FMA variations may introduce minor least-significant-bit floating-point variances across different CPU architectures. Therefore, bit-identical coordinate output across different platforms is not claimed; local/same-build determinism and seed stability are guaranteed.

2. **Pre-Allocation Parameter & Workload Validation**:
   - `TinderGeometry.ValidateParameters(parameters)` validates inputs before any memory or object allocations:
     - `LodLevel` bounded to `[0, 2]`.
     - Twig and fiber counts bounded (`PrimaryTwigCount` $\le 100$, `SecondaryTwigCount` $\le 200$, `FineFiberCount` $\le 500$, total $> 0$).
     - Metre dimensions validated as finite (`!float.IsNaN`, `!float.IsInfinity`) and strictly positive (`BundleRadiusMetres`, `BundleHeightMetres` in `(0.001, 5.0]`).
     - Total calculated workload bounded to $\le 65534$ vertices.
     - Invalid inputs throw `ArgumentOutOfRangeException` or `ArgumentException` immediately without partial allocations.
   - Buffer capacities are pre-calculated via `TinderGeometry.CalculateWorkload` and allocated with exact capacity.

3. **Structural Composition & Envelope Clamping**:
   - **Primary Structural Twigs** (Arched Frame):
     - Arched quadratic Bezier curves forming an interlaced kindling cradle.
     - Extruded polygonal tubes (5 sides at LOD0, 4 sides at LOD1/LOD2) with tapered radial cross-sections from base ($r \approx 4.5\,\text{mm}$) to tip ($r \approx 1.8\,\text{mm}$). Closed end caps prevent open polygonal edges.
     - Start, mid, and end points are radially clamped to `BundleRadiusMetres - PrimaryTwigRadiusMetres`.
   - **Secondary Brittle Twigs** (Volumetric Kindling):
     - Shorter cross-cutting angular twigs weaving through the structure.
     - Extruded 4-sided prismatic tubes ($r \approx 2.2\,\text{mm}$ base tapering to $0.9\,\text{mm}$).
     - Radial and vertical coordinates are clamped to `BundleRadiusMetres - SecondaryTwigRadiusMetres` and `BundleHeightMetres`, enforcing the target bounding footprint.
   - **Fine Fibrous Curls / Grass Strands** (Combustible Core):
     - Double-sided arched ribbon geometry nestled in the interior bowl.
     - Segments: fixed at 4 segments per ribbon across all LODs where present (LOD0: 24 fibers, LOD1: 12 fibers, LOD2: 0 fibers).
     - Clamped within bundle radius and height envelope.

4. **Coherent Geometric Fit, Ground Alignment and Base-Center Pivot**:
   - Following procedural twig and ribbon generation, the generator executes `FitToTargetEnvelopeAndBaseCenter`:
     - **Prefit Measurement & Guards**: Validates that all generated vertex coordinates are finite (`IsFinite`) and bounding extent is strictly nondegenerate ($> 10^{-6}$m in X, Y, Z) before any mutation.
     - **Smallest Coherent Deterministic Fit**: Computes the positive uniform scale-down factor $s = \min(1.0, \min(target_x / size_x, target_y / size_y, target_z / size_z))$. Geometry is scaled down uniformly only when actual bounds exceed declared target envelope ($0.30 \times 0.15 \times 0.28\text{m}$); it is never upscaled unnecessarily ($s \le 1.0$).
     - **Normal Invariance**: Under positive uniform scaling ($M = s I$ with $s > 0$), the surface normal transformation is $(M^{-1})^T = \frac{1}{s} I$. Normalization yields $\frac{\frac{1}{s}\mathbf{n}}{\|\frac{1}{s}\mathbf{n}\|} = \mathbf{n}$. Therefore, normal directions are mathematically invariant, eliminating anisotropic inverse-transpose warping, distortion, or twig ovalization that would occur under non-uniform scaling.
     - **Base-Center Translation**: Computes the horizontal bounding box center $(c_x, c_z)$ and minimum height $y_{\min}$, translating all vertices by $-(c_x, y_{\min}, c_z)$. Residual correction guarantees horizontal center $(0.00, 0.00)$ and ground contact $Y = 0.00\,\text{m}$ well within $< 10^{-8}\,\text{m}$, strictly satisfying the $10^{-5}\,\text{m}$ pivot and ground tolerances.
     - **Topology & Attribute Preservation**: Preserves exact vertex counts (LOD0: 1026, LOD1: 454, LOD2: 218), triangle counts (LOD0: 1852, LOD1: 792, LOD2: 392), triangle index order, UVs, and vertex colors.

5. **Vertex Colors & Project-Compatible URP Shader**:
   - Baked vertex colors:
     - Dark weathered bark: `Color(0.42, 0.34, 0.24)`
     - Sun-bleached dry wood: `Color(0.62, 0.54, 0.42)`
     - Dried thatch / kindling straw: `Color(0.78, 0.70, 0.46)`
     - Aged straw wisp: `Color(0.65, 0.55, 0.35)`
   - **URP 17.6.0 Shader & Material**: Stock URP/Lit does not display vertex colors without custom shader binding. The deliverable includes `Assets/CityLife/Fire/TinderVertexColor.shader` and `Assets/CityLife/Fire/TinderVertexColor.mat`, multiplying vertex colors into albedo under URP ForwardLit with double-sided rendering (`Cull Off`), `ShadowCaster`, and `DepthOnly`.
   - Note: Visual shading remains unverified until an authorized in-editor or runtime rendering pass.

---

## 3. Generating Model & Environment

- **Model**: `Gemini 3.8 Flash High`
- **Task ID**: `Task92025188-fee5-482e-bd98-0605bf18d4d4` (worker04)
- **Environment**: Antigravity agentic pairing environment on Windows.
- **Execution Mode**: Direct file edits only; no terminal execution, no Unity editor launch, no background tasks, no build pipeline runs, no git operations, no external network requests.

---

## 4. Geometry Parameters & LOD Profiles

The analytical geometry formulas are:
- Primary twig: $((segments + 1) \times sides + 2)$ vertices, $2 \times (segments + 1) \times sides$ triangles.
- Secondary twig: $((segments + 1) \times sides + 2)$ vertices, $2 \times (segments + 1) \times sides$ triangles.
- Fine fiber ribbon: $((segments + 1) \times 2)$ vertices, $segments \times 4$ triangles.

Calculated values from source loops:

| Parameter | LOD 0 (High) | LOD 1 (Medium) | LOD 2 (Low) | Unit |
| :--- | :--- | :--- | :--- | :--- |
| `Seed` | 4217 | 4217 | 4217 | integer |
| `PrimaryTwigCount` | 10 | 7 | 5 | count |
| `PrimaryTwigSides` | 5 | 4 | 4 | sides |
| `PrimaryTwigSegments` | 6 | 4 | 4 | segments |
| `SecondaryTwigCount` | 16 | 10 | 6 | count |
| `SecondaryTwigSides` | 4 | 4 | 4 | sides |
| `SecondaryTwigSegments` | 5 | 3 | 3 | segments |
| `FineFiberCount` | 24 | 12 | 0 | count |
| `FiberSegments` | 4 | 4 | N/A | segments |
| `BundleRadiusMetres` | 0.15 | 0.15 | 0.15 | metres |
| `BundleHeightMetres` | 0.14 | 0.14 | 0.14 | metres |
| `PrimaryTwigRadius` | 0.0045 (4.5 mm) | 0.0045 (4.5 mm) | 0.0045 (4.5 mm) | metres |
| `SecondaryTwigRadius` | 0.0022 (2.2 mm) | 0.0022 (2.2 mm) | 0.0022 (2.2 mm) | metres |
| `FiberWidth` | 0.0018 (1.8 mm) | 0.0018 (1.8 mm) | N/A | metres |
| **Exact Vertex Count** | **1026** | **454** | **218** | vertices |
| **Exact Triangle Count** | **1852** | **792** | **392** | triangles |

*Breakdown for LOD0*:
- Primary: $10 \times ((6 + 1) \times 5 + 2) = 370$ verts, $10 \times (2 \times 7 \times 5) = 700$ tris.
- Secondary: $16 \times ((5 + 1) \times 4 + 2) = 416$ verts, $16 \times (2 \times 6 \times 4) = 768$ tris.
- Fibers: $24 \times ((4 + 1) \times 2) = 240$ verts, $24 \times (4 \times 4) = 384$ tris.
- Total: $370 + 416 + 240 = 1026$ vertices, $700 + 768 + 384 = 1852$ triangles.

---

## 5. Physical Scale, Target Dimensions & Source-Only Metadata

The physical specifications are documented in `CityLife.Fire.TinderMetadata`:

- **Intended Mass**: `0.20 kg` (dry combustible fibrous brush weight).
  - Explicitly labelled: *Pending resource-owner approval and integration*.
- **Target Bounding Dimensions**: `0.30 m × 0.15 m × 0.28 m` (Width $\times$ Height $\times$ Depth).
  - **Status**: Design targets. Geometry source clamps endpoints to the radius envelope ($r \le 0.15\,\text{m}$), but runtime mesh bounds have not been measured in Unity. Intended dimensions must be treated as targets until validated in an authorized Unity session.
- **Pivot**: Base center `(0.00, 0.00, 0.00)` with ground plane contact at $Y = 0.00\,\text{m}$.
- **Asset Identifier**: `citylife.fire.tinder.dry-brush.v1-source`
- **Resource Classification**: `PROVISIONAL_UNAPPROVED`.

---

## 6. Exact Changed Files

The delivery is strictly bounded to the authorized directories:

1. `Assets/CityLife/Fire/TinderGeometry.cs` — Standalone deterministic procedural mesh generator updated with `FitToTargetEnvelopeAndBaseCenter`, `GetTargetDimensions`, and updated `CalculateTargetBounds` (smallest coherent uniform scale-down bounded by declared envelope, origin base-center translation, finite/nondegenerate guards).
2. `Assets/CityLife/Fire/TinderGeometry.cs.meta` — Unity asset importer metadata (preserved byte-for-byte).
3. `Assets/CityLife/Fire/TinderMetadata.cs` — Source-only physical metadata descriptor and contract notices with design targets distinguished (preserved byte-for-byte).
4. `Assets/CityLife/Fire/TinderMetadata.cs.meta` — Unity asset importer metadata (preserved byte-for-byte).
5. `Assets/CityLife/Fire/TinderVertexColor.shader` — URP 17.6.0 compatible vertex-color lit shader supporting baked mesh colors with double-sided rendering (preserved byte-for-byte).
6. `Assets/CityLife/Fire/TinderVertexColor.shader.meta` — Unity asset importer metadata (preserved byte-for-byte).
7. `Assets/CityLife/Fire/TinderVertexColor.mat` — Serialized material asset bound to TinderVertexColor shader (preserved byte-for-byte).
8. `Assets/CityLife/Fire/TinderVertexColor.mat.meta` — Unity asset importer metadata (preserved byte-for-byte).
9. `Assets/CityLife/Fire/TinderValidation.cs` — Validation helper suite and aggregation self-tests for coordinator execution (preserved byte-for-byte).
10. `Assets/CityLife/Fire/TinderValidation.cs.meta` — Unity asset importer metadata (preserved byte-for-byte).
11. `docs/tinder-fire/ASSET-PROVENANCE.md` — Procedural origin, model, parameters, scale, mass, and provenance records (this file).
12. `evidence/tinder-fire/EVIDENCE-MANIFEST.md` — Deliverable evidence manifest recording Unity37 failed evidence, geometric fit correction, and tests NOT RUN.

---

## 7. Outstanding Validation Status

As required by task constraints (direct file edits only; no terminal, no Unity, no builds, no git, no network):

| Stage | Status | Notes |
| :--- | :--- | :--- |
| C# Syntax / Code Review | Self-contained, inspected | Uses standard public Unity APIs; bounded parameters, pre-allocation checks, and repaired aggregation logic. |
| Unity Editor Compilation | **NOT RUN** in this pass | Prior coordinator Unity37 compiled cleanly; this pass is author-completed source-only. |
| Measured Unity Mesh Bounds | **CALCULATED FIT** | Corrected via deterministic uniform scale and base-center alignment; coordinator remeasurement pending. |
| In-Editor Close-up Rendering | **NOT RUN** | Shader and material provided; visual rendering and real palette close-up/context remain pending. |
| In-World Placement / Playmode | **NOT RUN** | Not placed in any scene; no runtime spawning. |
| Gameplay / Fuel / Warmth Integration | **OUT OF SCOPE** | No gameplay items, fuel consumption, fire recipe, or warmth systems exist or are claimed. |
| Overall Asset Classification | **AUTHOR-COMPLETED SOURCE-ONLY** | Author pass complete; execution and verification remain under coordinator control. |

---

## 8. Prior Execution Evidence: Coordinator Unity37 Run Analysis

During prior execution pass Unity37, coordinator compiled and executed `TinderValidation.RunValidation()` and `TinderValidation.RunAggregationSelfTests()`.
This constitutes failed evidence, not a passing artifact:
- **Result**: Failed 77/84 assertions, exit code 1.
- **Aggregation Self-Tests**: Passed 7/7 (verified failure paths: no-throw, wrong-throw, missing shader, bad pivot, bad count, bad bounds, deliberate failure).
- **Passing Categories**: Parameter validation passed; calculated vs. actual counts passed across all LODs (LOD0: 1026v/1852t, LOD1: 454v/792t, LOD2: 218v/392t); determinism passed; custom shader identity and `isSupported` passed; vertex colors populated and finite passed.
- **Measured Sizes & Pivots on Unmodified Generator**:
  - LOD0: size `(0.291896194, 0.128437638, 0.290645003)`, XZ center `(-0.001385555, 0.001149811)`.
  - LOD1: size `(0.271612048, 0.128898740, 0.294382453)`, XZ center `(-0.000163041, -0.000718914)`.
  - LOD2: size `(0.282348782, 0.108320668, 0.293928683)`, XZ center `(0.005205318, -0.000945799)`.
- **Root Cause of Failure**:
  - Target envelope depth $Z = 0.28\text{m}$ was exceeded by all LODs (~$0.291$m to $0.294$m) because the generator produced a circular bundle ($r = 0.15\text{m}$, diameter $0.30\text{m}$) without anisotropic target accommodation.
  - Horizontal center offsets ($10^{-4}$ to $5 \times 10^{-3}\text{m}$) exceeded the strict $10^{-5}\text{m}$ pivot tolerance because previous alignment only adjusted vertical ground contact ($Y$).

---

## 9. Authorized Geometry-Only Correction & Expected Acceptance

Under authorization `cave-tinder-geometry-authorization.json`:

1. **Smallest Coherent Deterministic Geometric Fit**:
   - Implemented `FitToTargetEnvelopeAndBaseCenter` in `Assets/CityLife/Fire/TinderGeometry.cs`.
   - Bounded by the declared envelope ($0.30 \times 0.15 \times 0.28\text{m}$):
     - Prefit bounding box is measured across all vertices with finite coordinate guards and nondegenerate extent guards ($> 10^{-6}\text{m}$).
     - The uniform scale factor $s = \min(1.0, \min(target_x / size_x, target_y / size_y, target_z / size_z))$ is computed. For default parameters, $s \approx 0.951$ to $0.963$ (~$3.7\%$ to $4.9\%$ reduction).
     - Geometry is only scaled down when exceeding target; it is never upscaled unnecessarily ($s \le 1.0$).
2. **Mathematical Invariance of Surface Normals**:
   - Positive uniform scaling ($M = s I$ with scalar $s > 0$) preserves normal directions identically:
     $$\mathbf{n}' = \frac{(M^{-1})^T \mathbf{n}}{\|(M^{-1})^T \mathbf{n}\|} = \frac{\frac{1}{s}\mathbf{n}}{\|\frac{1}{s}\mathbf{n}\|} = \frac{\mathbf{n}}{\|\mathbf{n}\|} = \mathbf{n}$$
   - Because $s$ is scalar, normals do not rotate, avoiding anisotropic distortion or twig ovalization. If non-uniform scaling were used, normals would require $(M^{-1})^T = \operatorname{diag}(1/s_x, 1/s_y, 1/s_z)\mathbf{n}$ with per-vertex renormalization, and circular twig cross-sections would be warped into ellipses.
3. **Base-Center Pivot Alignment**:
   - The horizontal bounding box center $(c_x, c_z)$ and lowest vertical position $y_{\min}$ are calculated from the scaled vertices.
   - All vertices are translated by $-(c_x, y_{\min}, c_z)$.
   - Residual checks ensure horizontal center at $(0, 0)$ and ground contact at $Y = 0.00\text{m}$ within $< 10^{-8}\text{m}$, strictly satisfying the $10^{-5}\text{m}$ tolerances.
4. **Target Mapping Consistency**:
   - `CalculateTargetBounds(parameters)` maps consistently to `TinderMetadata.TargetDimensionsMetres` ($0.30 \times 0.15 \times 0.28\text{m}$ at default scale), aligning generator and metadata contracts without inventing gameplay.
5. **Expected Acceptance**:
   - All 84 assertions in `TinderValidation.RunValidation()` are expected to pass (84/84 PASSED) upon coordinator execution, yielding exit code 0.
   - Preserves truthful failure paths if unsupported degenerate inputs are provided.
   - No terminal, Unity, test runner, git, or network operations were performed during this pass.
