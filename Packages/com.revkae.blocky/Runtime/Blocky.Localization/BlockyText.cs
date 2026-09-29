using System.Globalization;
using Unity.Localization;

namespace Blocky.Localization
{
    /// <summary>
    /// Every word a player reads, in the language they picked (<see cref="BlockyLanguages"/>). Keys are stable,
    /// dotted ids (<c>run.go</c>, <c>block.control.repeat</c>); the text for each lives in the language files, never
    /// in code. A key a language hasn't translated shows the English text; one no file has shows the fallback.
    /// Text with <c>{0}</c>-style placeholders is a Unity Smart String, so a translation can reorder the values or
    /// use plural forms.
    /// </summary>
    public static class BlockyText
    {
        /// <summary>The string table (collection) every Blocky string is in.</summary>
        public const string Table = "Blocky";

        /// <summary>The text for <paramref name="key"/>; the key itself when no language has it, so a gap is visible.</summary>
        public static string Get(string key) => Lookup(key, null) ?? key;

        /// <summary>The text for <paramref name="key"/>, or <paramref name="fallback"/> when no language has it.</summary>
        public static string Get(string key, string fallback) => Lookup(key, null) ?? fallback;

        /// <summary>The text for <paramref name="key"/> with its <c>{0}</c>, <c>{1}</c>… filled from <paramref name="args"/>.</summary>
        public static string Format(string key, params object[] args) => Lookup(key, args) ?? key;

        /// <summary>The culture of the language on show — for anything that depends on it, such as capital letters.</summary>
        public static CultureInfo Culture => BlockyLanguages.Current?.CultureInfo ?? CultureInfo.InvariantCulture;

        /// <summary>
        /// Capitals the way the language on show writes them: Turkish "değişkenler" is "DEĞİŞKENLER", not the
        /// invariant "DEĞIŞKENLER". Display text only — ids are never cased this way (the Turkish-I rule, TDD §7).
        /// </summary>
        public static string ToUpper(string text) => text?.ToUpper(Culture);

        private static string Lookup(string key, object[] args)
        {
            if (string.IsNullOrEmpty(key) || !BlockyLanguages.EnsureReady()) return null;

            var value = LocalizationSettings.ResourceDatabase.GetLocalizedString(Table, key, BlockyLanguages.Current, args);
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
