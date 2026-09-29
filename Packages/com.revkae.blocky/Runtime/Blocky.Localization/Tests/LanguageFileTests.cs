using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Blocky.Compiler;
using NUnit.Framework;

namespace Blocky.Localization.Tests
{
    /// <summary>The language files themselves: they read, and every one says everything English says.</summary>
    public class LanguageFileTests
    {
        private static readonly Regex Placeholder = new(@"\{(\d+)");

        private static LanguageFile English => LanguageFiles.Find(BlockyLanguages.SourceLanguage);

        private static IEnumerable<LanguageFile> Translations =>
            LanguageFiles.All.Where(f => !BlockyLanguages.IsSource(f.Code));

        [Test]
        public void Parse_ReadsLocaleNameAndStrings()
        {
            var file = LanguageFile.Parse("{ \"locale\": \"tr\", \"name\": \"Türkçe\", \"strings\": { \"run.go\": \"▶ Başlat\" } }", "ignored");

            Assert.AreEqual("tr", file.Code);
            Assert.AreEqual("Türkçe", file.Name);
            Assert.AreEqual("▶ Başlat", file.Strings["run.go"]);
        }

        [Test]
        public void MoreFilesForALanguage_AddToIt_OverTheFileNamedByItsCode()
        {
            LanguageFile File(string code, string name, params (string key, string text)[] strings) =>
                new(code, name, strings.ToDictionary(s => s.key, s => s.text));

            // Loaded in any order; "tr" is the base whatever comes first, and the project's own files lie over it by name.
            var files = LanguageFiles.Combine(new[]
            {
                ("tr.blocks", File("tr", "ignored", ("block.game.jump", "zıpla"), ("run.go", "Başla!"))),
                ("en", File("en", "English", ("run.go", "▶ Go"))),
                ("tr", File("tr", "Türkçe", ("run.go", "▶ Başlat"), ("run.stop", "Durdur")))
            });

            Assert.AreEqual(2, files.Count);
            Assert.AreEqual("en", files[0].Code, "the source language first");
            var turkish = files[1];
            Assert.AreEqual("Türkçe", turkish.Name, "the name comes from the base file");
            Assert.AreEqual("zıpla", turkish.Strings["block.game.jump"]);
            Assert.AreEqual("Durdur", turkish.Strings["run.stop"]);
            Assert.AreEqual("Başla!", turkish.Strings["run.go"], "a project's own file wins where it repeats a string");
        }

        [Test]
        public void Parse_WithoutALocale_UsesTheFileName_AndSkipsValuesThatArentText()
        {
            var file = LanguageFile.Parse("{ \"strings\": { \"a\": \"text\", \"b\": 3, \"c\": \"2024-01-01\" } }", "de");

            Assert.AreEqual("de", file.Code);
            Assert.AreEqual("de", file.Name);
            Assert.AreEqual("text", file.Strings["a"]);
            Assert.IsFalse(file.Strings.ContainsKey("b"));
            Assert.AreEqual("2024-01-01", file.Strings["c"], "a date-looking value stays the text it is");
        }

        [Test]
        public void EnglishAndTurkish_AreBothThere_EnglishFirst()
        {
            var codes = LanguageFiles.All.Select(f => f.Code).ToList();

            Assert.AreEqual(BlockyLanguages.SourceLanguage, codes[0]);
            CollectionAssert.Contains(codes, "tr");
        }

        [Test]
        public void EveryTranslation_SaysEverythingEnglishSays()
        {
            foreach (var file in Translations)
            {
                var missing = English.Strings.Keys.Where(k => !file.Strings.ContainsKey(k)).ToList();
                CollectionAssert.IsEmpty(missing, $"{file.Code}.json has no text for these keys (English is shown instead)");
            }
        }

        [Test]
        public void NoTranslation_HasKeysEnglishDoesNot()
        {
            foreach (var file in Translations)
            {
                var unknown = file.Strings.Keys.Where(k => !English.Strings.ContainsKey(k)).ToList();
                CollectionAssert.IsEmpty(unknown, $"{file.Code}.json has keys nothing asks for — renamed or misspelled?");
            }
        }

        [Test]
        public void EveryTranslation_KeepsThePlaceholders()
        {
            foreach (var file in Translations)
            foreach (var pair in English.Strings)
            {
                if (!file.Strings.TryGetValue(pair.Key, out var translated)) continue;
                CollectionAssert.AreEquivalent(Placeholders(pair.Value), Placeholders(translated), $"{file.Code}.json: \"{pair.Key}\"");
            }
        }

        [Test]
        public void EveryBlock_ItsInputs_AndTheirChoices_HaveEnglishNames()
        {
            var registry = BlockRegistry.LoadFromResources();
            var missing = new List<string>();
            for (var opcode = 0; opcode < registry.Count; opcode++)
            {
                var definition = registry.GetByOpcode(opcode);
                Need("block." + definition.blockType);
                foreach (var spec in definition.parameters)
                {
                    Need("param." + spec.key);
                    foreach (var choice in spec.choices) Need($"choice.{spec.key}.{choice.stableId}");
                }
            }

            CollectionAssert.IsEmpty(missing, "add these to en.json (and the other languages)");

            void Need(string key)
            {
                if (!English.Strings.ContainsKey(key) && !missing.Contains(key)) missing.Add(key);
            }
        }

        private static HashSet<string> Placeholders(string text) =>
            new(Placeholder.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value));
    }
}
