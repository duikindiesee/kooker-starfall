using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CityLife.World
{
    [Serializable]
    public sealed class IslandDefinition
    {
        public string schema, worldId, generator, sourceCommit;
        public int seed, cells, chunkCells, elevationOctaves, moistureOctaves;
        public double cellMetres, heightScale, seaLevel, elevationFrequency, mountainFrequency, moistureFrequency;
        public float Width => (float)(cells * cellMetres);
        public const string Generator = "citylife.desert-island.v1";
        public void Validate()
        {
            if (schema != "citylife.unity.world.v1" || generator != Generator)
                throw new InvalidOperationException("Unsupported world format or generator. Migrate explicitly; never regenerate a save with a different algorithm.");
            if (worldId == null || !Regex.IsMatch(worldId, "^[a-zA-Z0-9._-]{1,100}$") || sourceCommit == null || !Regex.IsMatch(sourceCommit, "^[a-f0-9]{40}$"))
                throw new InvalidOperationException("World identity must be a safe opaque ID and source provenance a full Git SHA.");
            if (cells < 64 || cells > 2048 || chunkCells != 64 || cells % chunkCells != 0)
                throw new InvalidOperationException("World cells must be a multiple of 64 in [64, 2048].");
            if (!Finite(cellMetres) || cellMetres < 1 || cellMetres > 16 || !Finite(heightScale) || heightScale <= 0 || heightScale > 1000 || !Finite(seaLevel) || seaLevel <= 0 || seaLevel >= 1)
                throw new InvalidOperationException("Invalid physical world dimensions.");
            foreach (double f in new[] { elevationFrequency, mountainFrequency, moistureFrequency })
                if (!Finite(f) || f <= 0 || f > 32) throw new InvalidOperationException("Invalid noise frequency.");
            if (elevationOctaves < 1 || elevationOctaves > 8 || moistureOctaves < 1 || moistureOctaves > 8)
                throw new InvalidOperationException("Invalid octave count.");
        }
        public string Fingerprint()
        {
            Validate(); var c = CultureInfo.InvariantCulture;
            string canonical = string.Join("|", schema, worldId, generator, sourceCommit, seed.ToString(c), cells.ToString(c),
                cellMetres.ToString("R", c), heightScale.ToString("R", c), seaLevel.ToString("R", c), chunkCells.ToString(c),
                elevationFrequency.ToString("R", c), mountainFrequency.ToString("R", c), moistureFrequency.ToString("R", c),
                elevationOctaves.ToString(c), moistureOctaves.ToString(c));
            return Hash(Encoding.UTF8.GetBytes(canonical));
        }
        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public IslandDefinition Snapshot() => (IslandDefinition)MemberwiseClone();
    }
}
