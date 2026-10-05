# Fallen Wood Asset Slice (Starfall / CityLife)

**Status:** SOURCE ONLY (Unity visual/physics validation pending)  
**Owning Task:** `fallen-wood`  
**Target Checkout:** `tasks/fallen-wood/checkout`  

---

## 1. Overview & Architectural Boundaries

This slice delivers a standalone, bounded Unity C# procedural fallen-wood mesh generator (Geometry Version 2) authored specifically for the Starfall / CityLife ecosystem.

### Scoped Deliverables:
- **Procedural Mesh Generator (Geometry Version 2):** Procedural generation of fallen branches, logs, and kindling with outward winding, exact UV seam coincidence, rough cap rim alignment, and finite parameter validation.
- **Local Metre Units:** All vertices, dimensions, radii, and offsets are expressed strictly in local metres (1.0 unit = 1.0 metre).
- **Unit Transform Scale:** Transforms require identity scale `(1, 1, 1)`; spatial sizing is baked directly into the authored mesh vertices. Hierarchy validation fails closed if ancestors have non-unit, non-uniform, mirrored, or sheared scaling.
- **Deterministic Explicit Seed:** All morphology, natural bow curvature, bark roughness, and color variation use an explicit seed stream (`DeterministicRng` 32-bit XorShift), guaranteeing algorithmic PRNG-stream determinism on the same runtime without relying on `UnityEngine.Random`, frame rate, or clock time. (No cross-platform bit-for-bit floating point guarantee is claimed).
- **Bark & End-Grain Coloration in Code:** Authored bark weathering and end-grain (heartwood core and sapwood perimeter ring) coloration are baked into mesh vertex colors (`Color32`) and accessible via code-based procedural textures using standard Unity APIs.
- **Physical Asset Metadata Only:** Stated bounding dimensions (`width`, `height`, `depth` in metres), volume ($m^3$), and dry mass ($kg$) are authored estimates declared as asset metadata only; not measured botanical species data.

### Strict Non-Goals & Boundaries:
- **No Runtime Physics:** Rigidbody, Colliders, dynamic joint attachments, or physical impact handling are explicitly not claimed or wired.
- **No Inventory or Supply Adapters:** AG2 owns Items and runtime core; no basket interfaces, supply adapters, or inventory slots are invented. `FallenWoodAssetMetadata.ToPhysicalDimensions()` provides an explicit conversion bridge depending on `CityLife.Items.PhysicalDimensions` in `Assets/CityLife/Items/ItemDefinition.cs`.
- **No Spawning Authority or World Save Changes:** World definition, persistence, and chunk spawning authority are preserved and untouched.
- **No Fuel Consumption or Fire APIs:** AG4 owns fire systems; no burn rates, heat output, or fuel interfaces are defined.
- **No Chopping Required:** Fallen wood is accepted as the primary ground source.

---

## 2. Component Inventory

All C# source files reside strictly within `Assets/CityLife/Wood/`:

| File | Namespace | Role |
| :--- | :--- | :--- |
| `FallenWoodProfile.cs` | `CityLife.Wood` | Data configuration for morphology, dimensions, segments, curvature, roughness, density, and color palettes. Reconciled finite validation. |
| `FallenWoodAssetMetadata.cs` | `CityLife.Wood` | Mutable serialized asset metadata container holding bounding dimensions, volume, authored dry density/mass estimates, and `ToPhysicalDimensions()` bridge. |
| `FallenWoodGenerator.cs` | `CityLife.Wood` | Standalone generator producing procedural `Mesh` (Geometry Version 2: outward winding, coincident seams/caps), calculating volume & dry mass estimates, and providing guarded procedural textures. |
| `FallenWoodInstance.cs` | `CityLife.Wood` | Visual `MonoBehaviour` managing `MeshFilter`, tracking owned procedural meshes, atomically replacing on successful generation, cleaning up on destruction, and failing closed on non-unit/sheared hierarchies. |
| `FallenWoodChecks.cs` | `CityLife.Wood` | Authored in-memory verification suite validating determinism, outward winding, seam/cap coincidence, finite inputs, hierarchy fail-closed scale, and ownership lifecycle. (Authored, unrun in source-only pass). |

---

## 3. Physical Parameters & Presets

Authored dry wood mass is derived from physical volume using a seasoned deadwood density estimate of approximately $680\,\text{kg/m}^3$ (typical staging estimate for arid deadwood; not empirical species measurements):

$$M_{\text{dry}} = V_{\text{frustums}} \times \rho_{\text{dry}}$$

### Built-in Presets:
1. **Fallen Branch (`CreateBranchPreset`)**:
   - Length: $0.65\,\text{m}$
   - Base Radius: $0.028\,\text{m}$ ($\approx 5.6\,\text{cm}$ diameter)
   - Tip Radius: $0.016\,\text{m}$ ($\approx 3.2\,\text{cm}$ diameter)
   - Expected Dry Mass: $\approx 0.45 - 0.65\,\text{kg}$
   - Features: Subtle bow curvature ($0.035\,\text{m}$), bark roughness, optional secondary twig fork.
2. **Fallen Log (`CreateLogProfile`)**:
   - Length: $1.15\,\text{m}$
   - Base Radius: $0.095\,\text{m}$ ($\approx 19\,\text{cm}$ diameter)
   - Tip Radius: $0.075\,\text{m}$ ($\approx 15\,\text{cm}$ diameter)
   - Expected Dry Mass: $\approx 15 - 20\,\text{kg}$
   - Features: Heavier trunk segment, bark roughness ($0.006\,\text{m}$), no side forks.
3. **Fallen Kindling (`CreateKindlingPreset`)**:
   - Length: $0.38\,\text{m}$
   - Base Radius: $0.014\,\text{m}$ ($\approx 2.8\,\text{cm}$ diameter)
   - Tip Radius: $0.008\,\text{m}$ ($\approx 1.6\,\text{cm}$ diameter)
   - Expected Dry Mass: $\approx 0.08 - 0.12\,\text{kg}$
   - Features: Lightweight branchlet, rapid collection candidate.

---

## 4. Usage Examples

### Procedural Generation via Code:
```csharp
using CityLife.Wood;
using UnityEngine;

// 1. Select or customize a profile
FallenWoodProfile profile = FallenWoodProfile.CreateBranchPreset();

// 2. Generate mesh with an explicit deterministic seed
uint seed = 0x5F1A94B2u;
Mesh woodMesh = FallenWoodGenerator.GenerateMesh(profile, seed, out FallenWoodAssetMetadata metadata);

// 3. Inspect physical metadata (asset metadata only)
Debug.Log($"Generated {metadata.profileName}: {metadata.width:F3}m x {metadata.height:F3}m x {metadata.depth:F3}m, mass={metadata.massKg:F2}kg");
```

### Verification Suite:
```csharp
using CityLife.Wood;

// Authored in-memory check suite (unrun in source-only pass; to be executed in active Unity player/test runner)
List<string> passedChecks = FallenWoodChecks.Run();
foreach (string check in passedChecks)
{
    Debug.Log($"Passed: {check}");
}
```

---

## 5. Known Validation Gaps (Honest Assessment)

Because this pass is strictly bounded to direct file edits with no shell, editor process, or player execution:
1. **Unity Editor Compilation:** The authored C# scripts have been written to standard Unity 2022+ C# specifications, but domain compilation in an active Unity Editor process has not been run.
2. **In-Engine Check Execution:** `FallenWoodChecks.Run()` is authored with extensive checks for outward winding, seam/cap coincidence, input validation, scale fail-closed, and mesh lifecycle, but has not been executed in an active Unity process.
3. **Visual & Shading Verification:** Vertex color shading and procedural texture preview have not been observed in the Unity Game/Scene view; actual Unity screenshots/media remain pending.
4. **Downstream Integration:** Integration with AG2 (Items model registration, carry attachments) and AG4 (fire combustion) remains open and must await their verified contract interfaces. `ToPhysicalDimensions()` provides an authored bridge depending on `CityLife.Items.PhysicalDimensions`.
