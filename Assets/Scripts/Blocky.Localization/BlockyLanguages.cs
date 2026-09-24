using System;
using System.Collections.Generic;
using Unity.Localization;
using UnityEngine;

namespace Blocky.Localization
{
    /// <summary>
    /// Which languages Blocky speaks and which one is showing, on Unity's built-in localization runtime
    /// (<see cref="LocalizationSettings"/>). Every file in <c>Resources/Languages</c> is a language
    /// (<see cref="LanguageFiles"/>); English is the source every other one falls back to, a key at a time.
    /// Needs no settings asset: when the project has none, Blocky makes its own at runtime. When the project has
    /// one (Project Settings > Localization), Blocky adds its languages and table to it instead.
    /// The first language shown is the one picked last time (saved in PlayerPrefs), else a <c>-language=tr</c>
    /// command-line argument, else the computer's own language, else English.
    /// </summary>
    public static class BlockyLanguages
    {
        public const string SourceLanguage = "en";
        public const string ResourcesFolder = "Languages";
        public const string SavedChoiceKey = "blocky.language";

        private static LocalizationSettings s_OwnSettings; // made here when the project has no settings asset
        private static LocalizationSettings s_Configured;  // the settings Blocky's languages were last added to
        private static Locale s_Override;
        private static bool s_Failed;

        /// <summary>The language on show right now; null only if localization could not start.</summary>
        public static Locale Current => s_Override ?? (EnsureReady() ? LocalizationSettings.SelectedLocale : null);

        /// <summary>The languages a player can pick, in menu order.</summary>
        public static IReadOnlyList<Locale> Available
        {
            get
            {
                var locales = new List<Locale>();
                if (!EnsureReady()) return locales;
                foreach (var locale in LocalizationSettings.Instance.AvailableLocales)
                    if (locale != null && locale.Enabled) locales.Add(locale);
                return locales;
            }
        }

        public static bool IsSource(string code) => string.Equals(code, SourceLanguage, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Switches every Blocky string to <paramref name="code"/> and remembers it for next time. Views redraw on
        /// <see cref="LocalizationSettings.SelectedLocaleChanged"/>. False if there is no such language.
        /// </summary>
        public static bool Select(string code)
        {
            var locale = Find(code);
            if (locale == null) return false;
            LocalizationSettings.SelectedLocale = locale;
            return true;
        }

        /// <summary>
        /// Shows every Blocky string in <paramref name="code"/> until disposed, without touching the player's
        /// choice. Tests use it so they read the same words on every machine, whatever its own language.
        /// </summary>
        public static IDisposable Override(string code)
        {
            var locale = Find(code) ?? throw new ArgumentException($"Blocky has no '{code}' language file.", nameof(code));
            var previous = s_Override;
            s_Override = locale;
            return new Restore(() => s_Override = previous);
        }

        /// <summary>
        /// Makes sure Unity Localization has settings holding Blocky's languages and string table. Cheap once done,
        /// so every lookup calls it. False (after one logged error) if the localization runtime could not start —
        /// callers then show their own fallback text.
        /// </summary>
        public static bool EnsureReady()
        {
            if (s_Failed) return false;

            try
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
            catch (Exception e)
            {
                s_Failed = true;
                Debug.LogException(e);
                return false;
            }
        }

        private static Locale Find(string code) =>
            string.IsNullOrEmpty(code) || !EnsureReady() ? null : LocalizationSettings.Instance.GetLocale(code);

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
                if (!IsSource(file.Code)) locale.FallbackCode = SourceLanguage;
                settings.AddLocale(locale);
            }

            if (settings.ProjectLocale == null) settings.ProjectLocale = settings.GetLocale(SourceLanguage);

            // Ahead of the command line and the system language: a language the player picked wins next time too.
            if (!settings.StartupSelectors.Exists(s => s is PlayerPrefLocaleSelector))
                settings.StartupSelectors.Insert(0, new PlayerPrefLocaleSelector { PlayerPreferenceKey = SavedChoiceKey });

            // Also lifts "Defer Initialization" if a project's settings ask for it: Blocky reads its words synchronously.
            _ = LocalizationSettings.InitializeAsync();
        }

        /// <summary>
        /// Play mode runs without a domain reload (ADR-014), so statics outlive a session. Start each one from the
        /// language files as they are now, against whichever settings are active.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            s_Configured = null;
            s_Override = null;
            s_Failed = false;
            LanguageFiles.Forget();
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
            if (s_OwnSettings == null) return;
            s_OwnSettings.Database.ReleaseAllAssets();
            if (LocalizationSettings.Instance == s_OwnSettings) LocalizationSettings.Instance = null;
            UnityEngine.Object.DestroyImmediate(s_OwnSettings);
            s_OwnSettings = null;
        };
#endif

        private sealed class Restore : IDisposable
        {
            private Action _undo;

            public Restore(Action undo) => _undo = undo;

            public void Dispose()
            {
                _undo?.Invoke();
                _undo = null;
            }
        }
    }
}
