# Evidence Manifest: Dry-Brush / Tinder Visual Source Asset

Date: 16 September 2026  
Status: **AUTHOR-COMPLETED SOURCE-ONLY**  
Task Reference: `Task92025188-fee5-482e-bd98-0605bf18d4d4` (worker04)  
Verification Status: **TESTS NOT RUN / COMPILATION & RENDERING UNVERIFIED**  
Checkout Base: `f6fc9101e829118636f3315718193892bd102b73`  
Target Branch: `codex/starfall-agy-04-tinder-fire`  

---

## 1. Asset Inventory & Status

| Path | Type | Status | Test Status | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Assets/CityLife/Fire/TinderGeometry.cs` | C# Source | **SOURCE-ONLY** | **NOT RUN** | Deterministic procedural mesh generator with parameter validation, prefit bounds measurement, coherent uniform scale-down fit to target envelope (0.30x0.15x0.28m), base-centre origin translation, workload bounding, and material helpers. |
| `Assets/CityLife/Fire/TinderGeometry.cs.meta` | Unity Meta | **SOURCE-ONLY** | **NOT RUN** | Asset importer metadata (preserved byte-for-byte). |
| `Assets/CityLife/Fire/TinderMetadata.cs` | C# Source | **SOURCE-ONLY** | **NOT RUN** | Source-only metadata descriptor (mass in kg, design target dimensions in metres, pivot; preserved byte-for-byte). |
| `Assets/CityLife/Fire/TinderMetadata.cs.meta` | Unity Meta | **SOURCE-ONLY** | **NOT RUN** | Asset importer metadata (preserved byte-for-byte). |
| `Assets/CityLife/Fire/TinderVertexColor.shader` | Shader | **SOURCE-ONLY** | **NOT RUN** | Project-compatible URP 17.6.0 double-sided vertex-color shader (preserved byte-for-byte). |
| `Assets/CityLife/Fire/TinderVertexColor.shader.meta` | Unity Meta | **SOURCE-ONLY** | **NOT RUN** | Asset importer metadata (preserved byte-for-byte). |
| `Assets/CityLife/Fire/TinderVertexColor.mat` | Material | **SOURCE-ONLY** | **NOT RUN** | Serialized material asset for TinderVertexColor shader (preserved byte-for-byte). |
| `Assets/CityLife/Fire/TinderVertexColor.mat.meta` | Unity Meta | **SOURCE-ONLY** | **NOT RUN** | Asset importer metadata (preserved byte-for-byte). |
| `Assets/CityLife/Fire/TinderValidation.cs` | C# Source | **SOURCE-ONLY** | **NOT RUN** | Repaired validation helper suite and aggregation self-tests (passedSelfTests field consistency, numerical roundoff tolerances 1e-5m replacing material 5mm/5cm allowances, diagnostic formatting :0.#####, exact target envelope checks, custom shader identity and isSupported check, SafeDestroy in finally, nonzero CLI exit; preserved byte-for-byte). |
| `Assets/CityLife/Fire/TinderValidation.cs.meta` | Unity Meta | **SOURCE-ONLY** | **NOT RUN** | Asset importer metadata (preserved byte-for-byte). |
| `docs/tinder-fire/ASSET-PROVENANCE.md` | Markdown Doc | **DOCUMENTATION** | Inspected | Procedural origin, model, parameters, LOD metrics, scale, Unity37 failed evidence analysis, geometric fit, and provenance records. |
| `evidence/tinder-fire/EVIDENCE-MANIFEST.md` | Markdown Doc | **MANIFEST** | Inspected | This evidence manifest. |

---

## 2. Test Execution & Evidence Boundaries

- **Direct File Edits Only**: No terminal commands, Unity editor processes, build executions, git commands, tracker interactions, or network requests were performed during this pass.
- **Prior Execution Evidence (Coordinator Unity37 Run)**:
  - Prior Unity37 run compiled cleanly, passed 7/7 aggregation self-tests, and passed 77/84 validation assertions before failing with exit code 1.
  - Passing categories in Unity37: parameter validation, determinism (identical vertices, normals, colors, triangles), custom shader identity (`CityLife/Fire/TinderVertexColor`) and `isSupported`, vertex colors populated and finite, calculated vs. actual counts (LOD0: 1026v/1852t, LOD1: 454v/792t, LOD2: 218v/392t), and ground alignment (min Y = 0.00m).
  - Honest failure measurements on unmodified generator:
    - LOD0: size `(0.291896194, 0.128437638, 0.290645003)`, XZ center `(-0.001385555, 0.001149811)`.
    - LOD1: size `(0.271612048, 0.128898740, 0.294382453)`, XZ center `(-0.000163041, -0.000718914)`.
    - LOD2: size `(0.282348782, 0.108320668, 0.293928683)`, XZ center `(0.005205318, -0.000945799)`.
    - Depth target 0.28m exceeded by all LODs; XZ centers failed strict 1e-5m tolerance.
  - **Unity37 is failed evidence, not a passing artifact**.
- **Authorized Geometry-Only Correction (`cave-tinder-geometry-authorization.json`)**:
  - Smallest coherent deterministic geometric fit via `FitToTargetEnvelopeAndBaseCenter`:
    - Prefit bounding box measurement with finite coordinate guards and nondegenerate extent guards (> 1e-6m).
    - Positive uniform scale-down bounded by declared envelope ($s = \min(1.0, \min(target_x / size_x, target_y / size_y, target_z / size_z))$). Never upscales unnecessarily ($s \le 1.0$).
    - Normal invariance: Under positive uniform scaling ($M = s I$, $s > 0$), $(M^{-1})^T = \frac{1}{s} I$, which normalizes identically to the original unit normal $\mathbf{n}$. Avoids anisotropic inverse-transpose warping, distortion, or twig ovalization.
    - Base-centre translation to horizontal origin $(0, 0)$ and ground contact $Y = 0.00$m with residual correction within $< 10^{-8}$m, strictly satisfying the $10^{-5}$m pivot and ground tolerances.
    - Preserves exact counts (LOD0: 1026v/1852t, LOD1: 454v/792t, LOD2: 218v/392t), topology, winding order, UVs, and vertex colors.
    - Aligns `CalculateTargetBounds` with `TinderMetadata.TargetDimensionsMetres` ($0.30 \times 0.15 \times 0.28$m at default scale).
- **Validation Helpers Preserved Byte-for-Byte**:
  - `TinderValidation.cs` and all its constants, thresholds, target dimensions, and self-tests are strictly preserved byte-for-byte.
  - Future coordinator execution will remeasure all LODs; all 84 assertions are expected to pass (84/84 PASSED, exit 0).
- **Rendering & Screen Captures**:
  - **NO FABRICATED SCREENSHOTS OR RENDERED PREVIEWS**. Visual rendering and real palette close-up/context remain pending an authorized in-editor or integrated build validation run.
- **Execution Status**: **TESTS NOT RUN** during this pass. Author-corrected source only.

---

## 3. Resource Authority & Scope Safeguards

- **Provisional Status**: No canonical resource type ID has been approved for fire, fuel, kindling, or tinder. `stone.collectable.v1` remains provisional and is not registered or treated as implemented.
- **Shared Core Ownership**: AG2 owns the active shared core. No shared core systems, items, inventory interfaces, scene files, or world save files were modified.
- **No Gameplay Claims**: There is strictly NO fire recipe, NO fuel consumption, NO warmth gameplay, NO cold relief, NO parallel inventory, and NO fake fuel transaction implemented or claimed.
- **World Preservation Compliance**: Deterministic geometry generation relies strictly on an explicit integer-seeded PRNG (`SeededRng`). It does not read clock time, frame count, or `UnityEngine.Random`. World definitions and save states remain untouched.
