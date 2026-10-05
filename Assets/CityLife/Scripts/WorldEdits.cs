using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace CityLife.World
{
    [Serializable] public sealed class HeightEdit { public int x, z; public float deltaMetres; }
    [Serializable] public sealed class WorldEdits
    {
        public string schema = "citylife.unity.edits.v1";
        public string worldId, baseFingerprint;
        public int revision;
        public HeightEdit[] terrain = Array.Empty<HeightEdit>();
        public static WorldEdits Empty(IslandDefinition d) => new WorldEdits { worldId = d.worldId, baseFingerprint = d.Fingerprint() };
        public void Apply(IslandField field)
        {
            field.AssertDefinitionUnchanged();
            var d = field.Definition;
            if (schema != "citylife.unity.edits.v1" || worldId != d.worldId || baseFingerprint != field.BaseFingerprint || revision < 0 || terrain == null || terrain.Length > 100000)
                throw new InvalidDataException("World edits do not match this base. Preserve the save and migrate explicitly.");
            var candidate = new Dictionary<int, float>();
            foreach (var e in terrain)
            {
                if (e == null || e.x < 0 || e.z < 0 || e.x > d.cells || e.z > d.cells || !IslandDefinition.Finite(e.deltaMetres) || Math.Abs(e.deltaMetres) > 100)
                    throw new InvalidDataException("Invalid terrain edit.");
                int i = e.z * field.Stride + e.x;
                if (candidate.ContainsKey(i)) throw new InvalidDataException("Duplicate terrain edit.");
                candidate.Add(i, e.deltaMetres);
            }
            field.Edits.Clear(); foreach (var pair in candidate) field.Edits.Add(pair.Key, pair.Value);
        }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(this, true));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
        }
    }
}
