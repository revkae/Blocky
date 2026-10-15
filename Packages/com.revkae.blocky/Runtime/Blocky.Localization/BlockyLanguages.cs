using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Localization
{
    /// <summary>One language a player can pick: its locale code, the name it calls itself, and its culture.</summary>
    public sealed class BlockyLanguage
    {
        public string Code { get; }
        public string Name { get; }

        /// <summary>For anything that depends on the language, such as capital letters; invariant for a code .NET doesn't know.</summary>
        public CultureInfo Culture { get; }

        public BlockyLanguage(string code, string name)
        {
            Code = code;
            Name = string.IsNullOrEmpty(name) ? code : name;
            Culture = CultureFor(code);
        }

        private static CultureInfo CultureFor(string code)
        {
            try { return CultureInfo.GetCultureInfo(code); }
            catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
        }
    }

    /// <summary>
    /// Which languages Blocky speaks and which one is showing. Every file in <c>Resources/Languages</c> is a language
    /// (<see cref="LanguageFiles"/>); English is the source every other one falls back to, a key at a time.
    /// The first language shown is the one picked last time (saved in PlayerPrefs), else a <c>-language=tr</c>
    /// command-line argument, else the computer's own language, else English.
    /// <para>
    /// On Unity 6.7 and later, in a project with the built-in localization and Smart Strings modules, this runs on
    /// Unity's localization runtime (<see cref="UsesUnityLocalization"/>): translations can use Smart String plural
    /// forms, and a project's own Localization Settings get Blocky's languages and string table. On earlier versions
    /// Blocky reads the files itself and fills <c>{0}</c>-style placeholders with ordinary formatting (ADR-045).
    /// </para>
    /// </summary>
    public static class BlockyLanguages
    {
        public const string SourceLanguage = "en";
        public const string ResourcesFolder = "Languages";
        public const string SavedChoiceKey = "blocky.language";

        private static readonly Dictionary<string, BlockyLanguage> s_Known = new(StringComparer.OrdinalIgnoreCase);
        private static BlockyLanguage s_Override;
        private static bool s_Failed;
        private static bool s_Listening;

        /// <summary>
        /// Raised when the language on show changes, with the new one. Views reword themselves on it. An
        /// <see cref="Override"/> doesn't raise it.
        /// </summary>
        public static event Action<BlockyLanguage> Changed;

        /// <summary>True when Blocky's words go through Unity's localization runtime (Unity 6.7+ with its modules).</summary>
        public static bool UsesUnityLocalization => LanguageBackend.IsUnityLocalization;

        /// <summary>The language on show right now; null only if localization could not start.</summary>
        public static BlockyLanguage Current
        {
            get
            {
                if (s_Override != null) return s_Override;
                if (!EnsureReady()) return null;
                var code = LanguageBackend.SelectedCode;
                return string.IsNullOrEmpty(code) ? null : Language(code, null);
            }
        }

        /// <summary>The languages a player can pick, in menu order.</summary>
        public static IReadOnlyList<BlockyLanguage> Available
        {
            get
            {
                var languages = new List<BlockyLanguage>();
                if (!EnsureReady()) return languages;
                foreach (var (code, name) in LanguageBackend.Languages) languages.Add(Language(code, name));
                return languages;
            }
        }

        public static bool IsSource(string code) => string.Equals(code, SourceLanguage, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Switches every Blocky string to <paramref name="code"/> and remembers it for next time. Views redraw on
        /// <see cref="Changed"/>. False if there is no such language.
        /// </summary>
        public static bool Select(string code) => !string.IsNullOrEmpty(code) && EnsureReady() && LanguageBackend.Select(code);

        /// <summary>
        /// Shows every Blocky string in <paramref name="code"/> until disposed, without touching the player's
        /// choice. Tests use it so they read the same words on every machine, whatever its own language.
        /// </summary>
        public static IDisposable Override(string code)
        {
            var file = LanguageFiles.Find(code) ?? throw new ArgumentException($"Blocky has no '{code}' language file.", nameof(code));
            var previous = s_Override;
            s_Override = Language(file.Code, file.Name);
            return new Restore(() => s_Override = previous);
        }

        /// <summary>
        /// Makes sure the languages are loaded and one is picked. Cheap once done, so every lookup calls it. False
        /// (after one logged error) if localization could not start — callers then show their own fallback text.
        /// </summary>
        public static bool EnsureReady()
        {
            if (s_Failed) return false;

            try
            {
                if (!LanguageBackend.EnsureReady()) return false;
                if (!s_Listening)
                {
                    LanguageBackend.SelectionChanged += RaiseChanged;
                    s_Listening = true;
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

        /// <summary>The text for <paramref name="key"/> in the language on show, English where it has none; null if no file has it.</summary>
        internal static string Lookup(string key, object[] args)
        {
            if (string.IsNullOrEmpty(key) || !EnsureReady()) return null;
            var language = Current;
            return language == null ? null : LanguageBackend.Lookup(key, language, args);
        }

        /// <summary>One object per language, so a view can compare them and a name learned once is kept.</summary>
        private static BlockyLanguage Language(string code, string name)
        {
            if (s_Known.TryGetValue(code, out var known) && (name == null || known.Name == name)) return known;
            name ??= LanguageFiles.Find(code)?.Name;
            return s_Known[code] = new BlockyLanguage(code, name);
        }

        private static void RaiseChanged()
        {
            var language = Current;
            if (language != null) Changed?.Invoke(language);
        }

        /// <summary>
        /// Play mode runs without a domain reload (ADR-014), so statics outlive a session. Start each one from the
        /// language files as they are now.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            s_Override = null;
            s_Failed = false;
            s_Known.Clear();
            LanguageFiles.Forget();
            LanguageBackend.ResetForPlaySession();
        }

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
