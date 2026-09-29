using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Settings are fixed, and migrations are registered once and are the same in every Play session, so there is nothing to reset.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Data.Serialization
{
    /// <summary>
    /// The only place programs are read from or written to disk/network. Refuses to load a file newer than
    /// <see cref="ProgramSchema.CurrentVersion"/>, and migrates forward step-by-step otherwise (TDD §5.2).
    /// </summary>
    public static class ProgramSerializer
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new BlockParamConverter(), new Vector2Converter() }
        };

        private static readonly List<ISchemaMigration> Migrations = new();

        public static void RegisterMigration(ISchemaMigration migration) => Migrations.Add(migration);

        public static string Serialize(ObjectProgram program) => JsonConvert.SerializeObject(program, Settings);

        public static ObjectProgram Deserialize(string json)
        {
            var root = JObject.Parse(json);
            var version = root["schemaVersion"]?.Value<int>() ?? 0;

            if (version == 0)
                throw new InvalidDataException("Program file has no schemaVersion; refusing to load an unversioned file.");
            if (version > ProgramSchema.CurrentVersion)
                throw new InvalidDataException(
                    $"Program file is schema version {version}, newer than this build supports ({ProgramSchema.CurrentVersion}). Refusing to load.");

            while (version < ProgramSchema.CurrentVersion)
            {
                var migration = Migrations.FirstOrDefault(m => m.FromVersion == version)
                    ?? throw new InvalidDataException($"No migration registered from schema version {version}.");
                root = migration.Migrate(root);
                version++;
                root["schemaVersion"] = version;
            }

            return root.ToObject<ObjectProgram>(JsonSerializer.Create(Settings));
        }
    }
}
