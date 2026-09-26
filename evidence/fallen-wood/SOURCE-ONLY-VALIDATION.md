# Source-Only Validation & Provenance Report

**Label:** SOURCE ONLY  
**Status:** Unity visual/physics validation pending  
**Date:** 2026-09-16  
**Workspace:** `tasks/fallen-wood/checkout`  
**Authorship / Repair Model:** Gemini 3.8 Flash (High)  
**Target Excerpt Reference:** `Assets/CityLife/Items/ItemDefinition.cs` (Items core contract)  

---

## 1. Compliance & Permitted Scope Verification

All file modifications in this pass strictly adhere to the permitted write boundaries:
- Writes were confined exclusively to:
  - `Assets/CityLife/Wood/**`
  - `docs/fallen-wood/**`
  - `evidence/fallen-wood/**`
- No parent directory metadata files were created (specifically, no `Assets/CityLife/Wood.meta` or other files outside the designated subtrees).
- No shell commands, Unity editor builds, player processes, network calls, git commands, tracker updates, scene edits, or modifications outside allowed write paths were executed.
- Direct file edits only.

---

## 2. Changed Paths Manifest

| Path | Category | Purpose |
| :--- | :--- | :--- |
| `Assets/CityLife/Wood/FallenWoodProfile.cs` | Source Code | Morphological profile; reconciled finite bounds, fork parameter validation, authored staging density estimates |
| `Assets/CityLife/Wood/FallenWoodAssetMetadata.cs` | Source Code | Mutable serialized asset metadata container; documents authored estimates and real dependency on `CityLife.Items.PhysicalDimensions` |
| `Assets/CityLife/Wood/FallenWoodGenerator.cs` | Source Code | Procedural generator (Geometry Version 2: outward winding, ring seam coincidence, rough cap rim alignment, finite validation, texture guards) |
| `Assets/CityLife/Wood/FallenWoodInstance.cs` | Source Code | Visual component with owned mesh tracking, atomic replacement, cleanup on destroy, and fail-closed hierarchy scale validation |
| `Assets/CityLife/Wood/FallenWoodChecks.cs` | Source Code | Authored in-memory test suite verifying winding, seam/cap coincidence, inputs, hierarchy scale, and lifecycle (authored, unrun) |
| `docs/fallen-wood/README.md` | Documentation | Usage documentation, physical presets, API guide, and accurate validation gaps |
| `evidence/fallen-wood/source-provenance.json` | Provenance | JSON schema manifest tracking repair model, relative paths, and unrun status |
| `evidence/fallen-wood/SOURCE-ONLY-VALIDATION.md` | Evidence | This validation report |

---

## 3. Authored Invariant Analysis (Geometry Version 2)

1. **Geometry Version 2 & Outward Winding:**
   - Main tube and branch fork tube quads are wound with outward-facing triangles `(v00, v01, v10)` and `(v01, v11, v10)`.
   - Cross-product face normals $(V_1 - V_0) \times (V_2 - V_0)$ on nondegenerate triangles point outward and align with smooth radial normals.
   - Base and tip end-grain cap fans are wound consistently outward along spine tangents (`-tangent` for base cap, `+tangent` for tip cap).
2. **Seam & Cap Coincidence:**
   - Main trunk rings and branch rings reuse ring vertex 0 positions for $j = radialSegments$, guaranteeing exact geometric coincidence while splitting UVs (`u = 1.0` vs `u = 0.0`).
   - Base and tip end-grain cap perimeter vertices copy the rough displaced vertex positions of the trunk rim rings directly, eliminating gaps between rough bark rings and end caps.
   - Deterministic RNG consumption is strictly explicit: roughness and tone are sampled exactly `radialSegments` times per ring without redundant duplicate calls.
3. **Finite & Bounded Input Validation:**
   - `FallenWoodProfile.IsValid()` validates finite non-NaN and non-infinite values for all dimensions, morphology, and densities.
   - Fork parameters are validated: zero-branch presets (`branchChance = 0f`) with zero fork parameters remain valid; enabled fork presets require valid finite parameters.
   - `FallenWoodGenerator.GenerateMesh()` validates the profile before allocating any vertex lists or Unity mesh resources.
   - Procedural bark and end-grain texture generators guard dimensions and array products against negative, zero, or excessive allocations.
4. **Transform Scale & Fail-Closed Hierarchy Policy:**
   - `FallenWoodInstance.IsSupportedHierarchy()` evaluates all ancestor transforms and local-to-world matrix columns.
   - Rejects non-unit uniform scaling, non-uniform scaling, negative/mirrored scaling, and shear before mutating transform scale or generating mesh.
   - On rejection, leaves existing displayed mesh and ancestor transforms completely untouched.
5. **Owned Mesh Lifecycle & Cleanup:**
   - `FallenWoodInstance` tracks locally owned procedural meshes via `ownedMesh`.
   - External/imported shared meshes assigned to `MeshFilter` are never destroyed.
   - Atomic replacement: newly generated mesh is assigned before prior owned mesh is safely destroyed.
   - Safe destruction in `OnDestroy()` using `Destroy` in play mode and `DestroyImmediate` in edit mode.
6. **Physical Dimensions & Authored Estimates:**
   - Volume is computed analytically as the sum of conical frustums.
   - Dry wood density (~680 kg/m^3) and dry mass are authored estimates for game staging, not empirical species measurements.
   - `FallenWoodAssetMetadata` is a mutable serialized data container.
   - `ToPhysicalDimensions()` depends directly on `CityLife.Items.PhysicalDimensions` in `Assets/CityLife/Items/ItemDefinition.cs`.

---

## 4. Honest Remaining Validation Gaps

As no Unity editor process, player, or shell command was executed in this pass:
1. **Unity Domain Compilation:** The C# scripts have been authored to standard Unity C# conventions, but compilation in an active Unity editor process has not been run.
2. **In-Engine Check Execution:** `FallenWoodChecks.Run()` is authored and self-contained, but execution within an active Unity test runner/play mode session is pending.
3. **Visual & Shading Inspection:** Real-time shading of vertex colors and procedural textures in Scene/Game view has not been observed; actual Unity media/screenshots remain pending.
4. **Downstream Integration Verification:** Connecting this asset generator to AG2 (physical item instantiation/carry) and AG4 (combustion/fuel) awaits future milestone contracts.
