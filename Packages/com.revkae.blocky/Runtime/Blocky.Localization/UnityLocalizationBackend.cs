#if BLOCKY_LOCALIZATION_RUNTIME && BLOCKY_SMART_STRINGS // set by Blocky.Localization.asmdef when the project has both Unity 6.7 modules
using System;
using System.Collections.Generic;
using Unity.Localization;
using UnityEngine;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Localization
{
    /// <summary>
    /// <see cref="BlockyLanguages"/> on Unity's built-in localization runtime (<see cref="LocalizationSettings"/>):
    /// each language file becomes a locale and a <see cref="BlockyText.Table"/> string table
    /// (<see cref="LanguageFileProvider"/>), so fallback to English, Smart String formatting and the saved choice are
    /// Unity Localization's own. Needs no settings asset: when the project has none, Blocky makes its own at runtime.
    /// When the project has one (Project Settings > Localization), Blocky adds its languages and table to it instead.
    /// </summary>
    internal static class LanguageBackend
    {
        public const bool IsUnityLocalization = true;

        private static LocalizationSettings s_OwnSettings; // made here when the project has no settings asset
        private static LocalizationSettings s_Configured;  // the settings Blocky's languages were last added to

        public static event Action SelectionChanged;

        public static string SelectedCode => LocalizationSettings.SelectedLocale?.Code;

        public static IEnumerable<(string code, string name)> Languages
        {
            get
            {
                foreach (var locale in LocalizationSettings.Instance.AvailableLocales)
                    if (locale != null && locale.Enabled) yield return (locale.Code, locale.LocaleName);
            }
        }

        public static bool EnsureReady()
        {
            var settings = LocalizationSettings.Instance;
            if (settings == null)
            {
                if (s_OwnSettings == null) s_OwnSettings = CreateSettings();
                LocalizationSettings.Instance = settings = s_OwnSettings;
            }

            if (!ReferenceEquals(settings, s_Configured))
            {
                s_Configured = settings; // first: starting localization picks a language, and its listeners read text
                Configure(settings);
            }
            return true;
        }

        public static bool Select(string code)
        {
            var locale = LocalizationSettings.Instance.GetLocale(code);
            if (locale == null) return false;
            LocalizationSettings.SelectedLocale = locale;
            return true;
        }

        public static string Lookup(string key, BlockyLanguage language, object[] args)
        {
            var locale = LocalizationSettings.Instance.GetLocale(language.Code);
            if (locale == null) return null;

            var value = LocalizationSettings.ResourceDatabase.GetLocalizedString(BlockyText.Table, key, locale, args);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static LocalizationSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            settings.name = "Blocky Localization Settings";
            settings.hideFlags = HideFlags.HideAndDontSave;
            return settings;
        }

        /// <summary>Adds what's missing and leaves the rest alone, so a project's own settings keep their choices.</summary>
        private static void Configure(LocalizationSettings settings)
        {
            var providers = settings.Database.AssetProvider;
            if (providers.GetProvider<LanguageFileProvider>() == null) providers.AddProvider(new LanguageFileProvider());

            foreach (var file in LanguageFiles.All)
            {
                if (settings.GetLocale(file.Code) != null) continue;

                // A missing or empty string shows the English one rather than nothing.
                var locale = new Locale(file.Code, file.Name);
                if (!BlockyLanguages.IsSource(file.Code)) locale.FallbackCode = BlockyLanguages.SourceLanguage;
                settings.AddLocale(locale);
            }

            if (settings.ProjectLocale == null) settings.ProjectLocale = settings.GetLocale(BlockyLanguages.SourceLanguage);

            // Ahead of the command line and the system language: a language the player picked wins next time too.
            if (!settings.StartupSelectors.Exists(s => s is PlayerPrefLocaleSelector))
                settings.StartupSelectors.Insert(0, new PlayerPrefLocaleSelector { PlayerPreferenceKey = BlockyLanguages.SavedChoiceKey });

            // A game that switches language through Unity Localization itself rewords Blocky too.
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;

            // Also lifts "Defer Initialization" if a project's settings ask for it: Blocky reads its words synchronously.
            _ = LocalizationSettings.InitializeAsync();
        }

        private static void OnSelectedLocaleChanged(Locale _) => SelectionChanged?.Invoke();

        public static void ResetForPlaySession()
        {
            s_Configured = null;
            if (s_OwnSettings != null) s_OwnSettings.Database.ReleaseAllAssets(); // its tables hold the old text
        }

#if UNITY_EDITOR
        /// <summary>
        /// A script reload keeps objects but loses the statics pointing at them: destroy Blocky's own settings and
        /// tables first, as Unity does with the tables it builds, rather than leave a set behind on every recompile.
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void ReleaseBeforeScriptReload() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () =>
        {
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            if (s_OwnSettings == null) return;
            s_OwnSettings.Database.ReleaseAllAssets();
            if (LocalizationSettings.Instance == s_OwnSettings) LocalizationSettings.Instance = null;
            UnityEngine.Object.DestroyImmediate(s_OwnSettings);
            s_OwnSettings = null;
        };
#endif
    }
}
#endif
