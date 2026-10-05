using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace CityLife.World
{
    public enum Biome : byte { Ocean, Shallows, Beach, Plains, Forest, Highland, Mountain, Peak, River }
    public sealed class IslandField
    {
        public readonly IslandDefinition Definition;
        public readonly string BaseFingerprint;
        private readonly SeededNoise elevation, moisture;
        public float[] Heights { get; private set; }
        public byte[] Biomes { get; private set; }
        public int Stride => Definition.cells + 1;
        public long GenerationMilliseconds { get; private set; }
        public float MaxHeight { get; private set; }
        public float LandAreaKm2 { get; private set; }
        public float FlatAreaKm2 { get; private set; }
        public Vector3 Landing { get; private set; }
        public Vector3 Summit { get; private set; }
        public Dictionary<int, float> Edits { get; } = new Dictionary<int, float>();
        public IslandField(IslandDefinition definition)
        {
            definition.Validate(); Definition = definition.Snapshot(); BaseFingerprint = Definition.Fingerprint();
            var rng = new SeededRandom(definition.seed);
            elevation = new SeededNoise(rng); moisture = new SeededNoise(rng);
        }
        public void SampleBase(int x, int z, out float height, out Biome biome)
        {
            var d = Definition;
            double u = x / (double)d.cells, v = z / (double)d.cells;
            double e = 0.5 + 0.5 * elevation.Fractal(u * d.elevationFrequency, v * d.elevationFrequency, d.elevationOctaves);
            double ridge = elevation.Fractal(u * d.mountainFrequency, v * d.mountainFrequency, 5, true);
            e = e * 0.78 + ridge * 0.42 * e;
            double dx = (u - 0.5) * 2, dz = (v - 0.5) * 2;
            e *= 1 - SmoothStep(0.55, 1.02, Math.Sqrt(dx * dx + dz * dz));
            // Source Float32Array rounding is part of the contract, before worldY/classification.
            float ef = (float)Math.Max(0, Math.Min(1, e));
            float mf = (float)(0.5 + 0.5 * moisture.Fractal(u * d.moistureFrequency, v * d.moistureFrequency, d.moistureOctaves));
            height = (float)((ef - d.seaLevel) * d.heightScale);
            double t = (ef - d.seaLevel) / (1 - d.seaLevel);
            biome = ef < d.seaLevel - 0.06 ? Biome.Ocean : ef < d.seaLevel ? Biome.Shallows : t < 0.035 ? Biome.Beach :
                t < 0.34 ? (mf > 0.52 ? Biome.Forest : Biome.Plains) : t < 0.6 ? Biome.Highland : t < 0.82 ? Biome.Mountain : Biome.Peak;
        }
        public void Generate(CancellationToken token = default)
        {
            AssertDefinitionUnchanged();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int n = Stride, land = 0, flat = 0;
            Heights = new float[n * n]; Biomes = new byte[n * n]; MaxHeight = float.MinValue;
            float landingScore = float.MaxValue;
            for (int z = 0; z < n; z++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    SampleBase(x, z, out float h, out Biome b);
                    Heights[i] = h; Biomes[i] = (byte)b;
                    if (h > 0 && x < n - 1 && z < n - 1) land++;
                    var pos = Position(x, z, h);
                    if (h > MaxHeight) { MaxHeight = h; Summit = pos; }
                    if (b == Biome.Plains || b == Biome.Forest)
                    {
                        float score = Math.Abs(pos.x + Definition.Width * 0.12f) + Math.Abs(pos.z + Definition.Width * 0.23f) + h * 4;
                        if (score < landingScore) { landingScore = score; Landing = pos; }
                    }
                }
            }
            for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
            {
                int i = z * n + x;
                if (Heights[i] > 0 && Biomes[i] != (byte)Biome.Beach && Math.Abs(Heights[i + 1] - Heights[i - 1]) / (2 * Definition.cellMetres) < 0.15 && Math.Abs(Heights[i + n] - Heights[i - n]) / (2 * Definition.cellMetres) < 0.15) flat++;
            }
            LandAreaKm2 = (float)(land * Definition.cellMetres * Definition.cellMetres / 1000000);
            FlatAreaKm2 = (float)(flat * Definition.cellMetres * Definition.cellMetres / 1000000);
            GenerationMilliseconds = watch.ElapsedMilliseconds;
        }
        public Vector3 Position(int x, int z, float height) => new Vector3((float)((x - Definition.cells / 2.0) * Definition.cellMetres), height, (float)((z - Definition.cells / 2.0) * Definition.cellMetres));
        public float Height(int x, int z)
        {
            x = Math.Max(0, Math.Min(Definition.cells, x)); z = Math.Max(0, Math.Min(Definition.cells, z));
            int i = z * Stride + x;
            float h;
            if (Heights == null) SampleBase(x, z, out h, out _); else h = Heights[i];
            return h + (Edits.TryGetValue(i, out float delta) ? delta : 0);
        }
        // Same triangles as the fine mesh, rather than bilinear interpolation through them.
        public float Ground(float wx, float wz)
        {
            double gx = Math.Max(0, Math.Min(Definition.cells - 0.0001, wx / Definition.cellMetres + Definition.cells / 2.0));
            double gz = Math.Max(0, Math.Min(Definition.cells - 0.0001, wz / Definition.cellMetres + Definition.cells / 2.0));
            int x = (int)gx, z = (int)gz; float u = (float)(gx - x), v = (float)(gz - z);
            float a = Height(x, z), b = Height(x + 1, z), c = Height(x, z + 1), e = Height(x + 1, z + 1);
            return u + v <= 1 ? a + u * (b - a) + v * (c - a) : e + (1 - u) * (c - e) + (1 - v) * (b - e);
        }
        public Vector3 Normal(int x, int z) => new Vector3(Height(x - 1, z) - Height(x + 1, z), (float)(2 * Definition.cellMetres), Height(x, z - 1) - Height(x, z + 1)).normalized;
        private static readonly Color[] Palette = { Hex(0x0e3f57), Hex(0x2f9fb5), Hex(0xe8d9b0), Hex(0xd9a86a), Hex(0x8f8752), Hex(0xc98b4e), Hex(0x8a5f4a), Hex(0xe8dcc6), Hex(0x39b6d8) };
        public Color ColorAt(int x, int z)
        {
            int i = z * Stride + x; var c = Palette[Biomes[i]];
            float shade = (float)(0.96 + SeededNoise.Scatter((uint)i) * 0.08);
            return new Color(c.r * shade, c.g * shade, c.b * shade, 1);
        }
        public static Color Hex(int h) => new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f, 1).linear;
        private static double SmoothStep(double a, double b, double x) { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
        public string BaseHash()
        {
            AssertDefinitionUnchanged();
            var bytes = new byte[Heights.Length * 4]; Buffer.BlockCopy(Heights, 0, bytes, 0, bytes.Length);
            return IslandDefinition.Hash(bytes);
        }
        public void AssertDefinitionUnchanged()
        {
            if (Definition.Fingerprint() != BaseFingerprint) throw new InvalidOperationException("A loaded world's generation settings cannot change. Create a new world or migrate explicitly.");
        }
    }
}
