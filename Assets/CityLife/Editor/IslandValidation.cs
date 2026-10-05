using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityLife.World.Editor
{
    public static class IslandValidation
    {
        [Serializable] private sealed class Oracle
        {
            public int schemaVersion;
            public string sourceCommit;
            public OracleSettings settings;
            public TerrainVector[] terrainVectors;
            public RngVector[] rngVectors;
            public NoiseVector[] noiseVectors;
        }
        [Serializable] private sealed class OracleSettings { public int cells; public double cellSize, heightScale, seaLevel; public OracleNoiseSettings noise; }
        [Serializable] private sealed class OracleNoiseSettings { public int elevOctaves, moistOctaves; public double elevFreq, mountainFreq, moistFreq; }
        [Serializable] private sealed class TerrainVector { public int seed, cells, x, z, biome; public double height, heightScale; }
        [Serializable] private sealed class RngVector { public int seed; public double[] values; }
        [Serializable] private sealed class NoiseVector { public int seed; public double x, z, value2, fbm, ridged, moistureFbm; }
        [Serializable] private sealed class Result
        {
            public string utc, status, baseHash, fingerprint;
            public int assertions, sourceTerrainVectors, sourceRngValues, sourceNoiseVectors;
            public double maxSourceHeightError;
            public float widthMetres, landKm2, gentleLandKm2, maxHeightMetres;
            public long generationMs;
            public string[] checks;
        }
        private static int assertions;
        private static void Check(bool okay, string message) { assertions++; if (!okay) throw new Exception("VALIDATION FAILED: " + message); }
        private static void Near(double a, double b, double tolerance, string message) => Check(Math.Abs(a - b) <= tolerance,
            message + " expected " + b.ToString("R", CultureInfo.InvariantCulture) + " got " + a.ToString("R", CultureInfo.InvariantCulture));
        private static IslandDefinition Clone(IslandDefinition d) => JsonUtility.FromJson<IslandDefinition>(JsonUtility.ToJson(d));
        private static void MustReject(Action action, string reason)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, reason);
        }
        [MenuItem("CityLife/Validate deterministic island")]
        public static void Run()
        {
            assertions = 0;
            var definitionAsset = Resources.Load<TextAsset>("IslandDefinition");
            var oracleAsset = Resources.Load<TextAsset>("SourceVectors");
            Check(definitionAsset != null && oracleAsset != null, "world definition and source oracle assets exist");
            var d = JsonUtility.FromJson<IslandDefinition>(definitionAsset.text);
            var oracle = JsonUtility.FromJson<Oracle>(oracleAsset.text);
            Check(d != null && oracle != null, "world definition and source oracle parse");
            d.Validate();
            Check(oracle.schemaVersion == 1 && oracle.sourceCommit == d.sourceCommit, "source oracle schema and pinned provenance");
            Check(oracle.settings != null && oracle.settings.noise != null, "source oracle settings are present");
            var os = oracle.settings;
            Check(os.cells == d.cells && os.cellSize == d.cellMetres && os.heightScale == d.heightScale && os.seaLevel == d.seaLevel,
                "source oracle physical settings match the world under test");
            Check(os.noise.elevFreq == d.elevationFrequency && os.noise.mountainFreq == d.mountainFrequency && os.noise.moistFreq == d.moistureFrequency &&
                os.noise.elevOctaves == d.elevationOctaves && os.noise.moistOctaves == d.moistureOctaves, "source oracle noise settings match the world under test");
            Check(oracle.terrainVectors != null && oracle.terrainVectors.Length == 420 && oracle.rngVectors != null && oracle.rngVectors.Length == 6 &&
                oracle.noiseVectors != null && oracle.noiseVectors.Length == 42, "complete independent source oracle coverage");
            int rngCount = 0;
            foreach (var vector in oracle.rngVectors)
            {
                Check(vector.values != null && vector.values.Length == 16, "source RNG sequence has expected coverage");
                var random = new SeededRandom(vector.seed);
                foreach (double expected in vector.values)
                {
                    // Mulberry32's result is an exact uint32 / 2^32 dyadic fraction. JsonUtility can
                    // parse its decimal JSON spelling one double ULP away; restore the source word
                    // instead of allowing any tolerance in the actual random generator output.
                    Check(expected >= 0 && expected < 1, "source RNG value lies in its unsigned fraction domain");
                    double expectedWord = Math.Round(expected * 4294967296.0);
                    Near(random.Next() * 4294967296.0, expectedWord, 0, "mulberry32 exact uint32 source parity");
                    rngCount++;
                }
            }
            foreach (var vector in oracle.noiseVectors)
            {
                var random = new SeededRandom(vector.seed); var noise = new SeededNoise(random); var moist = new SeededNoise(random);
                Near(noise.Value(vector.x, vector.z), vector.value2, 1e-12, "value noise");
                Near(noise.Fractal(vector.x, vector.z, 5), vector.fbm, 1e-12, "fractal noise");
                Near(noise.Fractal(vector.x, vector.z, 5, true), vector.ridged, 1e-12, "ridged noise");
                Near(moist.Fractal(vector.x, vector.z, 4), vector.moistureFbm, 1e-12, "moisture noise");
            }
            double maxError = 0;
            int terrainCount = 0;
            foreach (var vector in oracle.terrainVectors)
            {
                // The oracle also includes the browser's 608-cell/54-height-scale baseline.
                // Validate only the matching 1024-cell/240-height-scale world; no coordinates or heights are rescaled.
                if (vector.cells != d.cells) continue;
                Check(vector.heightScale == d.heightScale, "terrain vector physical scale");
                Check(vector.x >= 0 && vector.z >= 0 && vector.x < vector.cells && vector.z < vector.cells, "terrain vector lies inside the source domain");
                var settings = Clone(d); settings.seed = vector.seed;
                var field = new IslandField(settings);
                field.SampleBase(vector.x, vector.z, out float height, out Biome biome);
                maxError = Math.Max(maxError, Math.Abs(height - vector.height));
                Near(height, vector.height, 0.00002, "source terrain at " + vector.seed + "/" + vector.x + "/" + vector.z);
                Check((int)biome == vector.biome, "source biome classification");
                terrainCount++;
            }
            Check(terrainCount == 210 && rngCount == 96, "matching world source comparisons were actually executed");
            var full = new IslandField(d); full.Generate(); string baseHash = full.BaseHash();
            // Rebuild from independently parsed persisted settings; compare every sample, not a rehash of one array.
            var restored = new IslandField(Clone(d)); restored.Generate(); Check(restored.BaseHash() == baseHash, "full-world repeat generation");
            var aliasedInput = Clone(d); var snapshotted = new IslandField(aliasedInput);
            aliasedInput.seed++; aliasedInput.heightScale++;
            Check(snapshotted.BaseFingerprint == d.Fingerprint() && snapshotted.Definition.seed == d.seed && snapshotted.Definition.heightScale == d.heightScale,
                "caller mutation cannot change a loaded world's definition");
            snapshotted.SampleBase(512, 512, out float snapshotHeight, out _);
            Near(snapshotHeight, full.Height(512, 512), 0, "caller mutation cannot change terrain samples");
            snapshotted.Definition.heightScale++;
            MustReject(() => snapshotted.Generate(), "loaded definition mutation refuses regeneration");
            MustReject(() => WorldEdits.Empty(snapshotted.Definition).Apply(snapshotted), "loaded definition mutation cannot relabel edit identity");
            Check(full.Heights.Length == 1025 * 1025, "closed world grid");
            for (int i = 0; i <= d.cells; i++)
            {
                Check(full.Height(i, 0) < 0 && full.Height(i, d.cells) < 0 && full.Height(0, i) < 0 && full.Height(d.cells, i) < 0, "closed ocean boundary");
            }
            Check(full.LandAreaKm2 > 5 && full.LandAreaKm2 < 7, "meaningful dry land area");
            Check(full.FlatAreaKm2 > 2, "space for future neighbourhoods");
            Check(full.Ground(full.Landing.x, full.Landing.z) > 0, "dry player landing");
            var order = new List<int>(); for (int i = 0; i <= d.cells; i += 64) order.Add(i);
            order.Reverse();
            // Stateless sampling in reverse travel order, including chunk edges and the extra closed border.
            var shuffled = new IslandField(Clone(d));
            foreach (int x in order) foreach (int z in order)
            {
                shuffled.SampleBase(x, z, out float height, out _);
                Near(full.Height(x, z), height, 0, "travel-order independence and chunk seam");
                var p = full.Position(x, z, height); Near(full.Ground(p.x, p.z), height, 0.0001, "render/ground coordinates");
            }
            var different = Clone(d); different.seed++;
            var other = new IslandField(different); other.SampleBase(512, 512, out float differentHeight, out _);
            Check(differentHeight != full.Height(512, 512), "different seed changes island");
            var edits = WorldEdits.Empty(d); edits.revision = 1; edits.terrain = new[] { new HeightEdit { x = 512, z = 512, deltaMetres = 2.5f } };
            edits.Apply(full); Near(full.Height(512, 512), restored.Height(512, 512) + 2.5, 0.00001, "separate edit overlay applied");
            Check(full.BaseHash() == baseHash, "edit leaves procedural base immutable");
            int landingX = Mathf.RoundToInt(full.Landing.x / (float)d.cellMetres + d.cells / 2f);
            int landingZ = Mathf.RoundToInt(full.Landing.z / (float)d.cellMetres + d.cells / 2f);
            var unsafeLandingEdits = new List<HeightEdit>();
            for (int z = landingZ - 3; z <= landingZ + 3; z++)
            for (int x = landingX - 3; x <= landingX + 3; x++)
                unsafeLandingEdits.Add(new HeightEdit { x = x, z = z, deltaMetres = -100f });
            var landingField = new IslandField(Clone(d)); landingField.Generate();
            var changedLanding = WorldEdits.Empty(d); changedLanding.revision = 1; changedLanding.terrain = unsafeLandingEdits.ToArray(); changedLanding.Apply(landingField);
            Check(!IslandExplorer.IsSafeWalkPoint(landingField, landingField.Landing), "saved terrain edits can invalidate the procedural landing");
            Check(IslandExplorer.TryFindSafeWalkLanding(landingField, landingField.Landing, out Vector3 safeLanding) && IslandExplorer.IsSafeWalkPoint(landingField, safeLanding),
                "walking searches the edited terrain for a safe landing instead of trusting the procedural landing");
            var noSafeDefinition = Clone(d); noSafeDefinition.worldId += "-no-safe"; noSafeDefinition.cells = 64; noSafeDefinition.heightScale = 1;
            var noSafeField = new IslandField(noSafeDefinition); noSafeField.Generate();
            var noSafeEdits = new List<HeightEdit>();
            for (int z = 0; z <= noSafeDefinition.cells; z++)
            for (int x = 0; x <= noSafeDefinition.cells; x++)
                noSafeEdits.Add(new HeightEdit { x = x, z = z, deltaMetres = -100f });
            var noSafeOverlay = WorldEdits.Empty(noSafeDefinition); noSafeOverlay.revision = 1; noSafeOverlay.terrain = noSafeEdits.ToArray(); noSafeOverlay.Apply(noSafeField);
            Check(!IslandExplorer.TryFindSafeWalkLanding(noSafeField, noSafeField.Landing, out _), "walking reports no fallback when the edited world has no safe ground");
            var nonFiniteField = new IslandField(Clone(noSafeDefinition)); nonFiniteField.Generate();
            int nonFiniteX = Mathf.RoundToInt(nonFiniteField.Landing.x / (float)noSafeDefinition.cellMetres + noSafeDefinition.cells / 2f);
            int nonFiniteZ = Mathf.RoundToInt(nonFiniteField.Landing.z / (float)noSafeDefinition.cellMetres + noSafeDefinition.cells / 2f);
            nonFiniteField.Heights[nonFiniteZ * nonFiniteField.Stride + nonFiniteX] = float.NaN;
            Check(!IslandExplorer.IsSafeWalkPoint(nonFiniteField, nonFiniteField.Landing), "non-finite terrain can never be accepted as safe walking ground");
            bool successControlsRestored = false;
            bool evidenceSaved = IslandExplorer.TryWriteEvidence(() => { }, () => successControlsRestored = true, out string successError);
            Check(evidenceSaved && successControlsRestored && successError == "", "successful evidence write restores tour controls");
            bool failedControlsRestored = false;
            evidenceSaved = IslandExplorer.TryWriteEvidence(() => throw new IOException("synthetic failed write"), () => failedControlsRestored = true, out string evidenceError);
            Check(!evidenceSaved && failedControlsRestored && evidenceError.Contains("synthetic failed write"), "failed evidence write restores tour controls and reports the failure");
            string scratch = Path.Combine(Path.GetTempPath(), "CityLifeValidation", Guid.NewGuid().ToString("N"), "edits.json");
            edits.Save(scratch); edits.Save(scratch);
            var readBack = JsonUtility.FromJson<WorldEdits>(File.ReadAllText(scratch)); readBack.Apply(restored);
            Near(restored.Height(512, 512), full.Height(512, 512), 0, "atomic edit save/reload");
            Check(File.Exists(scratch + ".bak"), "edit backup retained");
            readBack.baseFingerprint = "mismatched"; MustReject(() => readBack.Apply(full), "mismatched base refused");
            Near(full.Height(512, 512), restored.Height(512, 512), 0, "invalid edit cannot partially mutate field");
            readBack.baseFingerprint = d.Fingerprint(); readBack.terrain = new[] { new HeightEdit { x = -1, z = 10, deltaMetres = 2 } };
            MustReject(() => readBack.Apply(full), "out-of-range edit refused");
            readBack.terrain = new[] { new HeightEdit { x = 5, z = 5, deltaMetres = float.NaN } };
            MustReject(() => readBack.Apply(full), "NaN edit refused");
            readBack.terrain = new[] { new HeightEdit { x = 5, z = 5, deltaMetres = 1 }, new HeightEdit { x = 5, z = 5, deltaMetres = 2 } };
            MustReject(() => readBack.Apply(full), "duplicate edit refused");
            Near(full.Height(512, 512), restored.Height(512, 512), 0, "invalid edit later in a batch preserves the prior overlay");
            var changed = Clone(d); changed.heightScale++;
            Check(changed.Fingerprint() != d.Fingerprint(), "generation configuration part of save identity");
            changed.generator = "unknown.v99"; MustReject(() => changed.Validate(), "unknown generator refused");
            var ambiguous = Clone(d); ambiguous.worldId += "|ambiguous";
            MustReject(() => ambiguous.Fingerprint(), "identity delimiter cannot alias canonical fields");
            ambiguous = Clone(d); ambiguous.sourceCommit = "unverified";
            MustReject(() => ambiguous.Fingerprint(), "source provenance must be a full Git commit");
            var result = new Result { status = "passed", utc = DateTime.UtcNow.ToString("O"), assertions = assertions,
                sourceTerrainVectors = terrainCount,
                sourceRngValues = rngCount, sourceNoiseVectors = oracle.noiseVectors.Length, maxSourceHeightError = maxError,
                baseHash = baseHash, fingerprint = d.Fingerprint(), widthMetres = d.Width, landKm2 = full.LandAreaKm2,
                gentleLandKm2 = full.FlatAreaKm2, maxHeightMetres = full.MaxHeight, generationMs = full.GenerationMilliseconds,
                checks = new[] { "source JavaScript arithmetic oracle", "full-grid regeneration", "different seed", "reverse chunk traversal", "closed coastline", "ground mesh mapping", "edited-terrain safe walking landing and no-safe fallback", "non-finite ground rejection", "successful and failed evidence writes restore tour controls", "immutable base and atomic edit save/reload", "mismatch/invalid data rejection" }
            };
            Directory.CreateDirectory("evidence/local"); File.WriteAllText("evidence/local/validation.json", JsonUtility.ToJson(result, true));
            Debug.Log("CITYLIFE_VALIDATION_PASSED " + JsonUtility.ToJson(result));
        }
    }
}
