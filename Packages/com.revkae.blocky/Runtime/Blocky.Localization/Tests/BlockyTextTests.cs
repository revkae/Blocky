using System.Linq;
using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Localization.Tests
{
    /// <summary>Looking words up, in English and in Turkish — through Unity Localization on 6.7+, from the files before it.</summary>
    public class BlockyTextTests
    {
        [Test]
        public void English_ReadsTheEnglishFile()
        {
            using (BlockyLanguages.Override("en"))
            {
                Assert.AreEqual("▶ Go", BlockyText.Get("run.go"));
                Assert.AreEqual("Repeat", BlockyText.Get("block.control.repeat"));
            }
        }

        [Test]
        public void Turkish_ReadsTheTurkishFile()
        {
            using (BlockyLanguages.Override("tr"))
            {
                Assert.AreEqual("▶ Başlat", BlockyText.Get("run.go"));
                Assert.AreEqual("Tekrarla", BlockyText.Get("block.control.repeat"));
            }
        }

        [Test]
        public void Placeholders_AreFilled_WhereTheLanguagePutsThem()
        {
            using (BlockyLanguages.Override("en"))
                Assert.AreEqual("“A” in “B” must be from 0 to 9, so this script won't run.",
                    BlockyText.Format("advice.out_of_range", "A", "B", "0", "9"));

            using (BlockyLanguages.Override("tr"))
                Assert.AreEqual("“B” bloğundaki “A”, 0 ile 9 arasında olmalı; yoksa bu betik çalışmaz.",
                    BlockyText.Format("advice.out_of_range", "A", "B", "0", "9"));
        }

        [Test]
        public void AKeyNoLanguageHas_ShowsTheKey_OrTheFallback()
        {
            Assert.AreEqual("no.such.key", BlockyText.Get("no.such.key"));
            Assert.AreEqual("fallback", BlockyText.Get("no.such.key", "fallback"));
        }

        [Test]
        public void EveryTranslation_FallsBackToEnglish()
        {
            var languages = BlockyLanguages.Available;
            var english = LanguageFiles.Find("en");

            Assert.AreEqual("en", languages[0].Code);
            foreach (var language in languages.Skip(1))
            using (BlockyLanguages.Override(language.Code))
                foreach (var key in english.Strings.Keys.Where(k => english.Strings[k].IndexOf('{') < 0))
                    Assert.AreNotEqual(key, BlockyText.Get(key), $"{language.Code} has no text for {key}, and English didn't stand in");
        }

        [Test]
        public void Select_SwitchesTheLanguage_AndSaysSo()
        {
            var saved = PlayerPrefs.GetString(BlockyLanguages.SavedChoiceKey, null);
            var before = BlockyLanguages.Current.Code;
            var other = before == "tr" ? "en" : "tr";
            BlockyLanguage heard = null;
            void Hear(BlockyLanguage language) => heard = language;

            BlockyLanguages.Changed += Hear;
            try
            {
                Assert.IsTrue(BlockyLanguages.Select(other));
                Assert.AreEqual(other, BlockyLanguages.Current.Code);
                Assert.AreEqual(other, heard?.Code);
                Assert.IsFalse(BlockyLanguages.Select("xx"), "a language with no file");
            }
            finally
            {
                BlockyLanguages.Changed -= Hear;
                BlockyLanguages.Select(before);
                if (saved == null) PlayerPrefs.DeleteKey(BlockyLanguages.SavedChoiceKey);
                else PlayerPrefs.SetString(BlockyLanguages.SavedChoiceKey, saved);
            }
        }

        [Test]
        public void ALanguage_KnowsItsNameAndCulture()
        {
            var turkish = BlockyLanguages.Available.First(l => l.Code == "tr");

            Assert.AreEqual("Türkçe", turkish.Name);
            Assert.AreEqual("tr", turkish.Culture.TwoLetterISOLanguageName);
            Assert.AreSame(turkish, BlockyLanguages.Available.First(l => l.Code == "tr"), "one object per language");
        }

        [Test]
        public void Capitals_FollowTheLanguage()
        {
            using (BlockyLanguages.Override("tr"))
                Assert.AreEqual("DEĞİŞKENLER", BlockyText.ToUpper("Değişkenler")); // dotted İ, not the invariant I

            using (BlockyLanguages.Override("en"))
                Assert.AreEqual("VARIABLES", BlockyText.ToUpper("Variables"));
        }

        [Test]
        public void BlockNames_InputLabels_AndChoices_AreTranslated_ButIdsAreNot()
        {
            var registry = BlockRegistry.LoadFromResources();
            var turn = registry.Find("motion.turn_direction");
            var direction = turn.parameters.First(p => p.key == "direction");

            using (BlockyLanguages.Override("tr"))
            {
                Assert.AreEqual("Dön", turn.DisplayName);
                Assert.AreEqual("yön", direction.DisplayName);
                Assert.AreEqual("sol", direction.ChoiceDisplayName("left"));
                Assert.AreEqual("Hareket", BlockCategory.Motion.DisplayName());
            }

            Assert.AreEqual("motion.turn_direction", turn.blockType);
            Assert.AreEqual("left", direction.choices[0].stableId);
        }

        [Test]
        public void ABlockNoFileNames_ShowsItsOwnEnglishName()
        {
            var definition = ScriptableObject.CreateInstance<BlockDefinition>();
            definition.blockType = "test.brand_new";
            definition.displayNameKey = "Brand New";

            using (BlockyLanguages.Override("tr"))
                Assert.AreEqual("Brand New", definition.DisplayName);

            Object.DestroyImmediate(definition);
        }

        [Test]
        public void Advice_IsInThePlayersLanguage()
        {
            var registry = BlockRegistry.LoadFromResources();
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack { id = "stk_1", triggerBlockType = "event.when_go_clicked", sequence = new BlockNode[0] }
                }
            };

            using (BlockyLanguages.Override("tr"))
            {
                var advice = ProgramAdvice.Collect(program, registry);
                Assert.AreEqual(1, advice.Count);
                Assert.AreEqual("“Başlat'a tıklandığında” bloğunun altında henüz bir şey yok, bu yüzden hiçbir şey olmaz. Altına blok tak.",
                    advice[0].Message);
            }
        }
    }
}
