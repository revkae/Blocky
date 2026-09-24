using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Localization
{
    /// <summary>One language: its locale code, the name it calls itself, and every string by key.</summary>
    public sealed class LanguageFile
    {
        public string Code { get; }
        public string Name { get; }
        public IReadOnlyDictionary<string, string> Strings { get; }

        public LanguageFile(string code, string name, IReadOnlyDictionary<string, string> strings)
        {
            Code = code;
            Name = name;
            Strings = strings;
        }

        /// <summary>
        /// Reads one language file:
        /// <c>{ "locale": "tr", "name": "Türkçe", "strings": { "run.go": "▶ Başlat", ... } }</c>.
        /// <paramref name="fallbackCode"/> (the file name) stands in for a missing <c>locale</c>. Null if it isn't one.
        /// </summary>
        public static LanguageFile Parse(string json, string fallbackCode)
        {
            // Dates off: a value that happens to look like one must stay the text it is.
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            if (JToken.ReadFrom(reader) is not JObject root) return null;

            var code = (string)root["locale"];
            if (string.IsNullOrEmpty(code)) code = fallbackCode;
            if (string.IsNullOrEmpty(code)) return null;

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root["strings"] is JObject entries)
                foreach (var entry in entries.Properties())
                    if (entry.Value.Type == JTokenType.String) strings[entry.Name] = (string)entry.Value;

            var name = (string)root["name"];
            return new LanguageFile(code, string.IsNullOrEmpty(name) ? code : name, strings);
        }
    }

    /// <summary>
    /// Every language file in <c>Resources/Languages</c>. A language is one file there, so adding one means
    /// adding a file: nothing else in the project lists them. More files for the same language add to it:
    /// a project keeps the words for its own blocks in, say, <c>en.blocks.json</c> in its own <c>Resources/Languages</c>
    /// folder, so updating Blocky never touches them. The file named just by its code (<c>en</c>) is the base; the
    /// others are laid over it in name order, and a string they repeat replaces the base's.
    /// </summary>
    public static class LanguageFiles
    {
        private static List<LanguageFile> s_All;

        public static IReadOnlyList<LanguageFile> All => s_All ??= Load();

        public static LanguageFile Find(string code)
        {
            foreach (var file in All)
                if (string.Equals(file.Code, code, StringComparison.OrdinalIgnoreCase)) return file;
            return null;
        }

        /// <summary>Reads the files again on next use — after a play session starts, so edited text shows up.</summary>
        public static void Forget() => s_All = null;

        private static List<LanguageFile> Load()
        {
            var parsed = new List<(string assetName, LanguageFile file)>();
            foreach (var asset in Resources.LoadAll<TextAsset>(BlockyLanguages.ResourcesFolder))
            {
                try
                {
                    var file = LanguageFile.Parse(asset.text, asset.name);
                    if (file == null) Debug.LogWarning($"Blocky: language file '{asset.name}' has no \"locale\" and was skipped.");
                    else parsed.Add((asset.name, file));
                }
                catch (JsonException e)
                {
                    Debug.LogWarning($"Blocky: language file '{asset.name}' isn't valid JSON and was skipped: {e.Message}");
                }
            }

            return Combine(parsed);
        }

        /// <summary>
        /// One <see cref="LanguageFile"/> per language from any number of files: for each language the file named by
        /// its code is the base (else the first by name), and the rest are laid over it in name order, their strings
        /// replacing the base's where they repeat one. The source language comes first, then the rest by code.
        /// </summary>
        public static List<LanguageFile> Combine(IEnumerable<(string assetName, LanguageFile file)> parsed)
        {
            var byCode = new Dictionary<string, List<(string assetName, LanguageFile file)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in parsed)
            {
                if (!byCode.TryGetValue(entry.file.Code, out var group)) byCode[entry.file.Code] = group = new List<(string, LanguageFile)>();
                group.Add(entry);
            }

            var files = new List<LanguageFile>();
            foreach (var group in byCode.Values)
            {
                group.Sort((a, b) =>
                {
                    var aBase = string.Equals(a.assetName, a.file.Code, StringComparison.OrdinalIgnoreCase);
                    var bBase = string.Equals(b.assetName, b.file.Code, StringComparison.OrdinalIgnoreCase);
                    return aBase != bBase ? (aBase ? -1 : 1) : string.CompareOrdinal(a.assetName, b.assetName);
                });

                if (group.Count == 1)
                {
                    files.Add(group[0].file);
                    continue;
                }

                var strings = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (_, file) in group)
                foreach (var pair in file.Strings)
                    strings[pair.Key] = pair.Value;
                files.Add(new LanguageFile(group[0].file.Code, group[0].file.Name, strings));
            }

            // The source language first, then the rest in a fixed order, so the language list never shuffles.
            files.Sort((a, b) =>
            {
                var aSource = BlockyLanguages.IsSource(a.Code);
                var bSource = BlockyLanguages.IsSource(b.Code);
                return aSource != bSource ? (aSource ? -1 : 1) : string.CompareOrdinal(a.Code, b.Code);
            });
            return files;
        }

    }
}
