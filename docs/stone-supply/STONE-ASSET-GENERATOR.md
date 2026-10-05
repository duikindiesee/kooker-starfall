# Stone Asset Generator & Metre-Scale Design Metadata

## 1. Overview & Scoped Pass Context
- **Task ID**: `86b2076d-1b15-41c6-b01c-8605b72c1d10`
- **Stream ID**: `da449324-f00d-4976-aceb-ff3eb3bbcfc0`
- **Branch**: `codex/starfall-agy-03-stone-supply`
- **Base Head**: `f6fc9101e829118636f3315718193892bd102b73`
- **Scope**: Asset-only source correction pass. Original procedural stone mesh generator, explicit metre-scale metadata, authored determinism validation, and source provenance.
- **Boundaries**:
  - Direct source edits only; no shell execution, Unity runtime, builds, player runs, git commands, or network calls.
  - No modifications to `Assets/CityLife/Items/**`, scenes, autonomy brains, or player saves.
  - No resource adapters, inventory pickup/store authority, fuel/fire transaction, or auto-spawning logic.
  - Existing diagnostic canyon-stone (`Starfall.Refuge.RefugeStone` / `canyon-stone`) remains unaltered.
  - No canonical item definition registration is claimed or asserted.
  - Direct source only; no rendered media, synthetic screenshots, or mockups fabricated.

---

## 2. Standalone Generator API

The generator lives in `Assets/CityLife/Stones/StoneMeshGenerator.cs` within the `CityLife.Stones` namespace. It provides a pure, self-contained procedural generation pipeline with zero global static state dependencies.

### Key Types & Signatures

```csharp
namespace CityLife.Stones
{
    public enum StoneShapeKind
    {
        RiverCobble = 0, // Water-smoothed, rounded pebble/cobble
        Fieldstone  = 1, // Sharp, angular, faceted quarry stone with cleavage planes
        FlatSlab    = 2, // Broad, tabular paver or hearth slab (low aspect ratio)
        Handstone   = 3  // Asymmetric, ergonomic tapered wedge tool stone
    }

    public struct StoneGenerationConfig
    {
        public const float MinSupportedScale = 0.25f; // ~4-7cm pebble
        public const float MaxSupportedScale = 3.00f; // ~50-84cm boulder/hearth slab

        public StoneShapeKind shape;
        public int seed;
        public int variantIndex;
        public float uniformScale;
        public bool flatShaded;

        public StoneGenerationConfig(
            StoneShapeKind shape,
            int seed = 0,
            int variantIndex = 0,
            float uniformScale = 1.0f,
            bool flatShaded = true);

        public static StoneGenerationConfig DefaultFor(StoneShapeKind shape, int seed = 0, int variantIndex = 0);

        public void Validate(); // Throws ArgumentOutOfRangeException on invalid scale or enum
    }

    public struct StoneMetadata
    {
        public StoneShapeKind shape;
        public int seed;
        public int variantIndex;
        public float uniformScale;
        public bool flatShaded;

        // Metre-scale bounding box dimensions
        public float widthMetres;
        public float heightMetres;
        public float depthMetres;
        public float boundingVolumeM3;

        // Volumetric design assumptions (design metadata, not measured physics simulation)
        public float formFactor;
        public float estimatedVolumeM3;
        public float assumedDensityKgPerM3;
        public float intendedMassKg;

        // Mesh metrics
        public int vertexCount;
        public int triangleCount;
        public Vector3 boundsMin;
        public Vector3 boundsMax;
        public Vector3 pivotOffset;

        public bool HasValidDimensions => ...; // Positive finite <= 20m
        public bool IsHandCarryable    => ...; // 0.5kg <= mass <= 25kg at 1.0 scale
    }

    public struct StoneGenerationResult
    {
        public Mesh mesh;
        public StoneMetadata metadata;
    }

    public static class StoneMeshGenerator
    {
        public const float StandardLithicDensityKgPerM3 = 2600.0f;

        public static StoneGenerationResult Generate(StoneGenerationConfig config);
        public static Mesh GenerateMesh(StoneShapeKind shape, int seed = 0, int variantIndex = 0, float uniformScale = 1.0f, bool flatShaded = true);
        public static StoneMetadata EstimateMetadata(StoneGenerationConfig config);
    }
}
```

### Entrypoint Validation & Default Struct Behavior
- `Validate()` checks that `shape` is a defined enum value and `uniformScale` is a finite number in `[0.25, 3.00]`.
- All public entrypoints (`Generate`, `GenerateMesh`, `EstimateMetadata`) enforce `config.Validate()` prior to allocations.
- Because `StoneGenerationConfig` is a value struct, `default(StoneGenerationConfig)` initializes `uniformScale = 0.0f`. This is an invalid scale and is explicitly rejected with `ArgumentOutOfRangeException`. Callers must use the constructor or `StoneGenerationConfig.DefaultFor(shape)`.
- Pre-mesh geometry validation checks that vertex coordinates and bounding extents are finite and within `[0.001, 20.0]m`. Any malformed output throws `InvalidOperationException` and destroys any allocated Mesh.

---

## 3. Geometric Archetypes & Design Baseline Assumptions

All stone shapes originate from a subdivided icosahedron (320 triangles, 162 unique spherical directions), deformed by shape-specific procedural formulas and cleavage planes.

### Archetype Design Specifications (at Scale 1.0)

| Archetype | Nominal Envelope (W × H × D) | Form Factor | Est. Volume | Assumed Density | Intended Mass | Visual / Functional Intent |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **RiverCobble** | ~0.18m × 0.12m × 0.15m | 0.523 (ellipsoid) | ~0.0017 m³ | 2600 kg/m³ | ~4.4 kg | Water-smoothed pebble/cobble; rounded organic profile. |
| **Fieldstone** | ~0.22m × 0.18m × 0.20m | 0.580 (faceted block) | ~0.0046 m³ | 2600 kg/m³ | ~11.9 kg | Angular building stone; planar cleavage facets; masonry wall. |
| **FlatSlab** | ~0.28m × 0.07m × 0.24m | 0.680 (tabular prism) | ~0.0032 m³ | 2600 kg/m³ | ~8.3 kg | Flat paver or hearth capstone; low height-to-width aspect ratio (< 0.35). |
| **Handstone** | ~0.16m × 0.09m × 0.11m | 0.460 (tapered wedge) | ~0.0007 m³ | 2600 kg/m³ | ~1.9 kg | Asymmetric teardrop wedge; thick palm grip tapering to striking wedge. |

> [!IMPORTANT]
> **Design Baseline Assumptions Disclaimer**: The dimensions, volumes, and mass values in this table are **authored design baseline assumptions** calculated using bounding boxes, shape form factors, and standard lithic density ($2600\text{ kg/m}^3$). They do **not** represent measured physics simulation results, rigid-body contact tests, or canonical item registrations. Actual generated dimensions vary deterministically with seed and variant parameters.

### Supported Uniform Scale Envelope
- **Range**: `[0.25, 3.00]`.
- Dimensions scale linearly with `uniformScale`.
- Volume and intended mass scale with $(\text{uniformScale})^3$.
- Values $< 0.25$ are rejected to prevent degenerate sub-centimetre geometry.
- Values $> 3.00$ are rejected to keep assets within sensible hand-carryable and hearth-stone bounds.

---

## 4. Shading Modes, Spatial UVs, and Pivot Architecture

### Supported Shading Modes
1. **Flat-Shaded (`flatShaded = true`)**:
   - Each triangle has 3 independent vertices (320 triangles × 3 = 960 vertices).
   - Crisp facet normals point outward, providing clear edge definition under directional sunlight.
   - Recommended for angular quarry stone (`Fieldstone`) and low-poly art fidelity.
2. **Smooth-Shaded (`flatShaded = false`)**:
   - Vertices sharing identical 3D positions share area-weighted smooth normals.
   - Vertices are split only along the longitudinal UV texture seam where differing UV coordinates are required.
   - Smooth normals maintain shared-position smoothing across the seam split, preventing normal creases.
   - Produces ~171 vertices for smooth organic stones (`RiverCobble`).
   - Vertex counts match `EstimateMetadata` predictions exactly in both modes.

### Non-Degenerate Spatial UV Mapping & MikkTSpace Tangents
- UVs are generated via spatial spherical unwrap:
  $$u = \frac{\text{atan2}(z, x)}{2\pi} + 0.5, \quad v = \frac{\arcsin(\text{clamp}(y, -1, 1))}{\pi} + 0.5$$
- Triangles that cross the longitudinal seam ($\Delta u > 0.5$) shift low-longitude vertices ($u < 0.5$) by $+1.0$. This eliminates backward texture wrapping and maintains compact UV triangles.
- Every triangle produces a strictly non-zero UV determinant ($|\det(UV)| > 10^{-8}$), enabling valid MikkTSpace tangent calculation.
- Tangent vectors are verified to be finite, unit-length, orthogonal to normals ($|\mathbf{t} \cdot \mathbf{n}| < 0.02$), and have valid handedness ($w = \pm 1$).

### Pivot Alignment & Truthful Slope Limitations
- The generator shifts vertices so that `boundsMin.y == 0.0f` and the horizontal footprint center `(minX + maxX)/2, (minZ + maxZ)/2` is at `(0, 0)`.
- **Truthful Placement Limitation**: The bottom-center pivot guarantees resting contact on an idealized **horizontal flat surface** ($y = 0$). On sloped, uneven, rocky, or convex terrain, bottom-center pivot alone **cannot** guarantee terrain clearance or stable multi-point contact. Surface resting on non-flat terrain requires collision geometry, raycasts, and physics/placement resolution in a later integration pass.

---

## 5. Authored Validation Suite (`StoneValidation.cs`)

Located at `Assets/CityLife/Stones/Editor/StoneValidation.cs`. Authored for CI and Unity editor review; **not executed during this pass**.

Key checks include:
0. **Comparison Helper Regression Suite**: Dedicated regression assertions verifying that comparison helpers distinguish one-bit float differences and signed zero (+0.0f vs -0.0f) while matching identical scalar and vector values, and validating buffer length mismatch guards.
1. **Determinism & Buffer Reproducibility**: Repeat generation with identical config produces bitwise identical vertices (xyz), normals (xyz), UVs (xy), colors (rgba), tangents (xyzw), triangle indices, and all 18 public deterministic metadata fields across both flat and smooth shading modes using explicit scalar IEEE 754 bit comparisons (`BitConverter.ToInt32(BitConverter.GetBytes(...))`) without approximate operators or shared early-break shortcuts. Buffer lengths are verified prior to indexing.
2. **Distinct Seed & Variant Divergence**: Changing `seed` or `variantIndex` produces distinct vertex geometry (preserving `variantDiffPositions = true` on divergence distance > 0.001m).
3. **Resting Base Pivot**: Validates `boundsMin.y == 0.0f` and horizontal centering within $10^{-4}\text{ m}$.
4. **Metadata vs Mesh Coherence**: Validates exact vertex and triangle count equality between `EstimateMetadata`, `metadata`, and `mesh` in both flat and smooth shading modes, including UV seam splits. Validates bounding box extents match mesh bounds within $0.001\text{ m}$.
5. **Geometry & Invariant Checks**: Zero degenerate 3D triangles, strictly outward winding, non-zero per-triangle UV determinants, finite unit normals, finite unit tangents, tangent-normal orthogonality, and valid handedness ($\pm 1$).
6. **Input Validation & Struct Defaults**: Rejection of `default(StoneGenerationConfig)`, out-of-range scale ($< 0.25$, $> 3.00$, negative, zero, NaN, Inf), invalid shape enums, and post-initialization field mutation. Verification of scale boundaries ($0.25$ and $3.00$).
7. **Resource Leak Prevention**: Every created `Mesh` is tracked and destroyed via `UnityEngine.Object.DestroyImmediate` in `finally` blocks, including on check failure paths.

---

## 6. Required Later Visual Verifications (Unity Execution)

When an authorized Unity execution pass is scheduled, the following evidence captures must be gathered:

### Close-up Visual Inspection Captures
1. `RiverCobble`: Close-up inspection showing smooth silhouette, soft lighting transition, and buff sandstone tone.
2. `Fieldstone`: Close-up inspection displaying crisp planar cleavage facets and sharp shadow edges under directional light.
3. `FlatSlab`: Side and top elevation showing flat planar top/bottom and irregular fractured perimeter rim.
4. `Handstone`: Perspective view highlighting ergonomic palm bulb and striking wedge asymmetry.

### Inhabitant Scale & Placement Captures
1. **Grasp / In-Hand Scale**: Stone positioned in an inhabitant's hands to visually verify proportions relative to fingers and palms.
2. **Ground Resting**: Stone placed on varied terrain to confirm stable resting contact with collider geometry.
3. **Hearth Ring Assembly**: Array of stones arranged in a circle to evaluate aesthetic harmony as structural fireplace/hearth material.

---

## 7. Verification Status Manifest

| Verification Area | Status | Notes |
| :--- | :--- | :--- |
| **C# Source Structure** | Author-Complete | Strict validation at entrypoints; flat and smooth shading; spatial UVs. |
| **Metre-Scale Math** | Author-Complete | Supported scale $[0.25, 3.00]$, baseline density $2600\text{ kg/m}^3$. |
| **Unity Compile** | **UNVERIFIED** | Direct file pass; Unity editor was not invoked. |
| **Visual Appearance** | **UNVERIFIED** | No rendered media or synthetic screenshots fabricated. |
| **Inhabitant Context** | **UNVERIFIED** | Inhabitant grasp and scale alignment deferred to Unity runtime pass. |
| **Item Core Integration**| **DEFERRED** | ItemDefinition registration and inventory authority actively owned by AG2. |
