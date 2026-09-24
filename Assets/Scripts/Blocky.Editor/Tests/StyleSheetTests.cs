using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Blocky.Compiler;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Editor.Tests
{
    /// <summary>
    /// The stylesheets as text. A misspelled token in USS fails silently — the property just isn't applied — so these
    /// check what no one would see until a theme looked wrong: every token used is defined, every theme redefines
    /// only tokens that exist, and every block category has its colors, in the default look and in High contrast.
    /// </summary>
    public class StyleSheetTests
    {
        private static readonly Regex Rule = new(@"([^{}]+)\{([^{}]*)\}", RegexOptions.Compiled);
        private static readonly Regex Declaration = new(@"(--[\w-]+)\s*:", RegexOptions.Compiled);
        private static readonly Regex Use = new(@"var\((--[\w-]+)\)", RegexOptions.Compiled);
        private static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        private static List<(string selector, string body)> _rules;

        /// <summary>Every rule of every Blocky stylesheet, wherever the package sits under Assets.</summary>
        private static List<(string selector, string body)> Rules
        {
            get
            {
                if (_rules != null) return _rules;
                var sheets = Directory.GetFiles(Application.dataPath, "blocky-*.uss", SearchOption.AllDirectories);
                Assert.IsNotEmpty(sheets, "no blocky-*.uss under " + Application.dataPath);

                _rules = new List<(string, string)>();
                foreach (var sheet in sheets)
                foreach (Match match in Rule.Matches(Comment.Replace(File.ReadAllText(sheet), string.Empty)))
                    _rules.Add((match.Groups[1].Value.Trim(), match.Groups[2].Value));
                return _rules;
            }
        }

        private static HashSet<string> DeclaredIn(Func<string, bool> selector) =>
            new(Rules.Where(r => selector(r.selector)).SelectMany(r => Declaration.Matches(r.body).Cast<Match>().Select(m => m.Groups[1].Value)));

        private static string ClassName(BlockCategory category) => category.ToString().ToLowerInvariant();

        [Test]
        public void EveryTokenTheSheetsUse_IsDefinedAtTheRoot()
        {
            var defined = DeclaredIn(s => s == ":root");
            var used = Rules.SelectMany(r => Use.Matches(r.body).Cast<Match>().Select(m => m.Groups[1].Value)).Distinct();

            CollectionAssert.IsEmpty(used.Where(token => !defined.Contains(token)).ToList(), "used with var() but never defined in a :root rule");
        }

        [TestCase(".blocky-theme--dark")]
        [TestCase(".blocky-theme--contrast")]
        public void ATheme_RedefinesOnlyTokensThatExist(string theme)
        {
            var defined = DeclaredIn(s => s == ":root");
            var redefined = DeclaredIn(s => s == theme);

            Assert.IsNotEmpty(redefined, theme + " has no rule of its own");
            CollectionAssert.IsEmpty(redefined.Where(token => !defined.Contains(token)).ToList(), "a theme token nothing reads — misspelled?");
        }

        [Test]
        public void EveryCategory_HasAColorAndAFill()
        {
            var missing = new List<string>();
            var rootTokens = DeclaredIn(s => s == ":root");
            foreach (BlockCategory category in Enum.GetValues(typeof(BlockCategory)))
            {
                var name = ClassName(category);
                if (!rootTokens.Contains("--blocky-color-" + name)) missing.Add("--blocky-color-" + name);
                if (!DeclaredIn(s => s == ".blocky-block--category-" + name).Contains("--blocky-fill")) missing.Add(".blocky-block--category-" + name + " { --blocky-fill }");
            }

            CollectionAssert.IsEmpty(missing);
        }

        [Test]
        public void HighContrast_RecolorsEveryCategory()
        {
            var missing = new List<string>();
            var themeTokens = DeclaredIn(s => s == ".blocky-theme--contrast");
            foreach (BlockCategory category in Enum.GetValues(typeof(BlockCategory)))
            {
                var name = ClassName(category);
                if (!themeTokens.Contains("--blocky-color-" + name)) missing.Add("--blocky-color-" + name);
                if (!DeclaredIn(s => s == ".blocky-theme--contrast .blocky-block--category-" + name).Contains("--blocky-fill"))
                    missing.Add(".blocky-theme--contrast .blocky-block--category-" + name + " { --blocky-fill }");
            }

            CollectionAssert.IsEmpty(missing);
        }

        [TestCase("")]
        [TestCase(".blocky-theme--dark ")]
        [TestCase(".blocky-theme--contrast ")]
        public void EveryLook_GivesTheGridItsDotColor(string theme)
        {
            Assert.IsTrue(DeclaredIn(s => s == theme + ".blocky-ingame-canvas-viewport").Contains("--bk-grid-dot"));
        }
    }
}
