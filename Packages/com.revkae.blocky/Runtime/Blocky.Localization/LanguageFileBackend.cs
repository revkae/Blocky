#if !(BLOCKY_LOCALIZATION_RUNTIME && BLOCKY_SMART_STRINGS) // Unity before 6.7, or a project without its localization modules
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Localization
{
    /// <summary>
    /// <see cref="BlockyLanguages"/> straight from the language files (<see cref="LanguageFiles"/>), for Unity
    /// versions without the built-in localization runtime. A key the language on show hasn't translated reads the
    /// English one; <c>{0}</c>-style placeholders are filled with <see cref="string.Format(IFormatProvider, string, object[])"/>
    /// in the language's culture — what Smart Strings do for them too, so Blocky's own words read the same either way.
    /// The choice is saved under <see cref="BlockyLanguages.SavedChoiceKey"/>, as Unity's PlayerPrefs selector does.
    /// </summary>
    internal static class LanguageBackend
    {
        public const bool IsUnityLocalization = false;

        private const string CommandLinePrefix = "-language=";

        private static string s_Selected;

        public static event Action SelectionChanged;

        public static string SelectedCode => s_Selected;

        public static IEnumerable<(string code, string name)> Languages
        {
            get
            {
                foreach (var file in LanguageFiles.All) yield return (file.Code, file.Name);
            }
        }

        public static bool EnsureReady()
        {
            s_Selected ??= PickAtStartup();
            return s_Selected != null;
        }

        public static bool Select(string code)
        {
            var file = Match(code);
            if (file == null) return false;

            PlayerPrefs.SetString(BlockyLanguages.SavedChoiceKey, file.Code);
            if (string.Equals(s_Selected, file.Code, StringComparison.OrdinalIgnoreCase)) return true;

            s_Selected = file.Code;
            SelectionChanged?.Invoke();
            return true;
        }

        public static string Lookup(string key, BlockyLanguage language, object[] args)
        {
            var value = Find(language.Code, key);
            if (value == null && !BlockyLanguages.IsSource(language.Code)) value = Find(BlockyLanguages.SourceLanguage, key);
            if (value == null || args == null || args.Length == 0 || value.IndexOf('{') < 0) return value;

            try
            {
                return string.Format(language.Culture, value, args);
            }
            catch (FormatException)
            {
                Debug.LogWarning($"Blocky: the {language.Code} text for '{key}' has a placeholder that doesn't match its values: {value}");
                return value;
            }
        }

        public static void ResetForPlaySession() => s_Selected = null;

        private static string Find(string code, string key)
        {
            var file = LanguageFiles.Find(code);
            return file != null && file.Strings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
        }

        /// <summary>The one picked last time, else <c>-language=tr</c>, else the computer's own language, else English.</summary>
        private static string PickAtStartup()
        {
            var file = Match(PlayerPrefs.GetString(BlockyLanguages.SavedChoiceKey, null))
                       ?? Match(CommandLineChoice())
                       ?? Match(CultureInfo.CurrentUICulture.Name)
                       ?? LanguageFiles.Find(BlockyLanguages.SourceLanguage);
            if (file == null && LanguageFiles.All.Count > 0) file = LanguageFiles.All[0];
            return file?.Code;
        }

        private static string CommandLineChoice()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith(CommandLinePrefix, StringComparison.OrdinalIgnoreCase))
                    return argument.Substring(CommandLinePrefix.Length);
            return null;
        }

        /// <summary>The file for <paramref name="code"/>, or for its language alone: "tr-TR" finds <c>tr</c>.</summary>
        private static LanguageFile Match(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;

            var file = LanguageFiles.Find(code);
            var dash = code.IndexOf('-');
            return file ?? (dash > 0 ? LanguageFiles.Find(code.Substring(0, dash)) : null);
        }
    }
}
#endif
