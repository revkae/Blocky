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
    /// adding a file: nothing else in the project lists them.
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
            var files = new List<LanguageFile>();
            foreach (var asset in Resources.LoadAll<TextAsset>(BlockyLanguages.ResourcesFolder))
            {
                try
                {
                    var file = LanguageFile.Parse(asset.text, asset.name);
                    if (file == null) Debug.LogWarning($"Blocky: language file '{asset.name}' has no \"locale\" and was skipped.");
                    else if (Find(files, file.Code) != null) Debug.LogWarning($"Blocky: two language files are for '{file.Code}'; '{asset.name}' was skipped.");
                    else files.Add(file);
                }
                catch (JsonException e)
                {
                    Debug.LogWarning($"Blocky: language file '{asset.name}' isn't valid JSON and was skipped: {e.Message}");
                }
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

        private static LanguageFile Find(List<LanguageFile> files, string code) =>
            files.Find(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase));
    }
}
