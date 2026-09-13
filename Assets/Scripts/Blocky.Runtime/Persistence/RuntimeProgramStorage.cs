using System.IO;
using Blocky.Data;
using Blocky.Data.Serialization;
using UnityEngine;

namespace Blocky.Runtime.Persistence
{
    /// <summary>
    /// Player-facing save data for an in-game program editor — a real file under
    /// <see cref="Application.persistentDataPath"/>, not a Unity asset. Deliberately independent of
    /// <c>BlockProgramAsset</c>/<c>UnityEditor.AssetDatabase</c>: a player editing a program in a shipped build
    /// should never be able to (or need to) write into the project's source assets.
    /// </summary>
    public static class RuntimeProgramStorage
    {
        private static string DirectoryPath => Path.Combine(Application.persistentDataPath, "BlockyPrograms");

        private static string PathFor(string key) => Path.Combine(DirectoryPath, key + ".json");

        public static bool Exists(string key) => File.Exists(PathFor(key));

        public static ObjectProgram Load(string key) => ProgramSerializer.Deserialize(File.ReadAllText(PathFor(key)));

        public static void Save(string key, ObjectProgram program)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(PathFor(key), ProgramSerializer.Serialize(program));
        }
    }
}
