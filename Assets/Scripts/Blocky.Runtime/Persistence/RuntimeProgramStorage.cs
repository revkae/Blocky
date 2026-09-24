using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Blocky.Data;
using Blocky.Data.Serialization;
using UnityEngine;
using UnityEngine.SceneManagement;

// RootOverride is a test seam: the tests that set it put it back, and every Play session starts it over (ADR-014).
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Runtime.Persistence
{
    /// <summary>What <see cref="RuntimeProgramStorage.TryLoad"/> found under a key.</summary>
    public enum SavedProgramStatus
    {
        /// <summary>Nothing is saved: the object runs the program it was given in the scene.</summary>
        None,

        /// <summary>A saved program was read.</summary>
        Loaded,

        /// <summary>
        /// A file was there but couldn't be used — damaged, or written by a newer Blocky. It has been moved aside
        /// (never overwritten), and the object runs the program it was given in the scene.
        /// </summary>
        Unreadable
    }

    /// <summary>
    /// Player-facing save data for an in-game program editor — a real file under
    /// <see cref="Application.persistentDataPath"/>, not a Unity asset. Deliberately independent of
    /// <c>BlockProgramAsset</c>/<c>UnityEditor.AssetDatabase</c>: a player editing a program in a shipped build
    /// should never be able to (or need to) write into the project's source assets.
    ///
    /// One file per programmed object, at <c>BlockyPrograms/&lt;scene&gt;/&lt;parents&gt;/&lt;object&gt;.json</c>
    /// (ADR-031): the scene keeps same-named objects in different scenes apart, the folders keep an object apart from
    /// a same-named one elsewhere in the hierarchy, and a number in brackets tells same-named siblings apart
    /// (<c>Cube</c>, <c>Cube[1]</c>). Every name is made safe for every file system on the way (<see cref="SafeName"/>),
    /// so an object called "a/b", "Why?" or "CON" still saves. A file that can't be read is moved aside, never
    /// overwritten, and saving never throws.
    /// </summary>
    public static class RuntimeProgramStorage
    {
        public const string FolderName = "BlockyPrograms";

        private const string Extension = ".json";
        private const string EscapedChars = "<>:\"/\\|?*%[]"; // not allowed in a file name somewhere, or used by the key itself
        private const int MaxNameLength = 80; // per folder or file name; longer ones are cut and made unique with a hash
        private const string UntitledScene = "Untitled";

        /// <summary>Test seam: while set, saves go under this folder instead of <see cref="Application.persistentDataPath"/>.</summary>
        public static string RootOverride { get; set; }

        /// <summary>The folder every save is under.</summary>
        public static string Root => RootOverride ?? Path.Combine(Application.persistentDataPath, FolderName);

        /// <summary>How the key compares names: case-insensitively, so two names never share a file on a Windows or macOS disk.</summary>
        public static StringComparer NameComparer => StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// The key <paramref name="go"/>'s program is saved under: <c>scene/parent/…/object</c>, each part made safe
        /// by <see cref="SafeName"/>, and <c>[n]</c> after the name of an object with n earlier siblings of the same
        /// name (<see cref="NameComparer"/>). Renaming or moving an object in the Unity Editor gives it a new key, so it
        /// starts again from its scene program.
        /// </summary>
        public static string KeyFor(GameObject go)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));

            var parts = new List<string>();
            for (var t = go.transform; t != null; t = t.parent)
                parts.Add(Part(t.name, SameNameIndex(t)));
            parts.Add(SceneKey(go.scene));
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>The first part of every key in <paramref name="scene"/> — also the name of that scene's folder.</summary>
        public static string SceneKey(Scene scene) =>
            SafeName(scene.IsValid() && !string.IsNullOrEmpty(scene.name) ? scene.name : UntitledScene);

        /// <summary>
        /// The key of a child of <paramref name="parentKey"/> (a scene key or an object's key) named
        /// <paramref name="name"/>, that is the <paramref name="sameNameIndex"/>-th of its siblings with that name.
        /// </summary>
        public static string ChildKey(string parentKey, string name, int sameNameIndex) => parentKey + "/" + Part(name, sameNameIndex);

        /// <summary>
        /// <paramref name="name"/> as a folder or file name that works on Windows, macOS, Linux and WebGL, and can't be
        /// mistaken for another name: characters a file system refuses (and <c>%</c>, <c>[</c>, <c>]</c>) become
        /// <c>%XX</c>, a trailing dot or space is escaped, Windows device names (<c>CON</c>, <c>COM1</c>…) get their
        /// first letter escaped, an empty name is <c>%</c>, and a very long name is cut and ended with a short hash.
        /// </summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "%";

            var safe = new StringBuilder(name.Length + 8);
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                var last = i == name.Length - 1;
                if (c < ' ' || c == '\u007f' || EscapedChars.IndexOf(c) >= 0 || (last && (c == '.' || c == ' '))) AppendEscaped(safe, c);
                else safe.Append(c);
            }

            if (IsWindowsDeviceName(safe.ToString()))
            {
                var first = safe[0];
                safe.Remove(0, 1);
                safe.Insert(0, Escape(first));
            }

            if (safe.Length <= MaxNameLength) return safe.ToString();

            var cut = MaxNameLength - 9; // room for "~" and 8 hex digits
            if (char.IsHighSurrogate(safe[cut - 1])) cut--; // never split a character in two
            return safe.ToString(0, cut) + "~" + StableHash(name);
        }

        /// <summary>The file a key's program is saved in. The key must come from <see cref="KeyFor"/> or be built from <see cref="SafeName"/> parts.</summary>
        public static string PathFor(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A save key can't be empty.", nameof(key));

            var parts = key.Split('/');
            foreach (var part in parts)
                if (part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new ArgumentException($"'{key}' is not a save key. Make one with KeyFor, or from SafeName parts.", nameof(key));

            return Path.Combine(Root, Path.Combine(parts)) + Extension;
        }

        /// <summary>Whether anything is saved under <paramref name="key"/>.</summary>
        public static bool Exists(string key) => File.Exists(PathFor(key));

        /// <summary>Whether anything at all is saved for <paramref name="scene"/> — a cheap check before looking for a particular object.</summary>
        public static bool HasSavesFor(Scene scene) => Directory.Exists(Path.Combine(Root, SceneKey(scene)));

        /// <summary>
        /// Reads the program saved under <paramref name="key"/>. A file that can't be used — damaged, not a program,
        /// or from a newer Blocky (TDD §5.2: never half-load a future file) — is renamed to
        /// <c>&lt;file&gt;.&lt;time&gt;.unreadable</c> so the next save can't overwrite it, and a warning says where it went.
        /// </summary>
        public static SavedProgramStatus TryLoad(string key, out ObjectProgram program)
        {
            program = null;
            var path = PathFor(key);
            if (!File.Exists(path)) return SavedProgramStatus.None;

            try
            {
                program = ProgramSerializer.Deserialize(File.ReadAllText(path));
                return SavedProgramStatus.Loaded;
            }
            catch (Exception e) // the file is outside our control: anything wrong with it means "can't use it"
            {
                program = null;
                var keptAs = SetAside(path);
                Debug.LogWarning(keptAs != null
                    ? $"Blocky: the saved program '{key}' couldn't be read ({e.Message}). It was kept as '{keptAs}', and the object runs its scene program instead."
                    : $"Blocky: the saved program '{key}' couldn't be read ({e.Message}), nor moved aside. The object runs its scene program instead.");
                return SavedProgramStatus.Unreadable;
            }
        }

        /// <summary>
        /// Saves <paramref name="program"/> under <paramref name="key"/>. Never throws: a save that fails (a full disk,
        /// a read-only folder) must not undo the edit that caused it. False, with the reason, when nothing was written.
        /// </summary>
        public static bool TrySave(string key, ObjectProgram program, out string problem)
        {
            try
            {
                var path = PathFor(key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, ProgramSerializer.Serialize(program));
                problem = null;
                return true;
            }
            catch (Exception e)
            {
                problem = e.Message;
                Debug.LogWarning($"Blocky: couldn't save the program '{key}': {e.Message}");
                return false;
            }
        }

        /// <summary>Test hooks and redirected folders never outlive a Play session: domain reload is off (ADR-014).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession() => RootOverride = null;

        private static string Part(string name, int sameNameIndex) =>
            sameNameIndex == 0 ? SafeName(name) : SafeName(name) + "[" + sameNameIndex.ToString(CultureInfo.InvariantCulture) + "]";

        /// <summary>How many earlier siblings (in hierarchy order) share <paramref name="t"/>'s name.</summary>
        private static int SameNameIndex(Transform t)
        {
            var name = t.name;
            var position = t.GetSiblingIndex();
            var index = 0;

            if (t.parent != null)
            {
                for (var i = 0; i < position; i++)
                    if (SameName(t.parent.GetChild(i).name, name)) index++;
                return index;
            }

            var scene = t.gameObject.scene;
            if (!scene.IsValid()) return 0;
            foreach (var root in scene.GetRootGameObjects())
                if (root.transform != t && root.transform.GetSiblingIndex() < position && SameName(root.name, name)) index++;
            return index;
        }

        private static bool SameName(string a, string b) => NameComparer.Equals(a, b);

        private static void AppendEscaped(StringBuilder safe, char c) => safe.Append(Escape(c));

        private static string Escape(char c) => "%" + ((int)c).ToString("X2", CultureInfo.InvariantCulture);

        /// <summary>CON, PRN, AUX, NUL, COM0–9 and LPT0–9 name devices on Windows, with or without an extension.</summary>
        private static bool IsWindowsDeviceName(string name)
        {
            var dot = name.IndexOf('.');
            var stem = (dot >= 0 ? name.Substring(0, dot) : name).TrimEnd(' ').ToUpperInvariant();
            switch (stem)
            {
                case "CON":
                case "PRN":
                case "AUX":
                case "NUL":
                    return true;
            }
            return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3]);
        }

        /// <summary>FNV-1a: the same on every run and every platform, unlike <c>string.GetHashCode</c>.</summary>
        private static string StableHash(string s)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Renames an unreadable file out of the way (or copies it, if it can't be moved). Null if neither worked.</summary>
        private static string SetAside(string path)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var target = $"{path}.{stamp}.unreadable";
            for (var n = 2; File.Exists(target); n++) target = $"{path}.{stamp}-{n}.unreadable";

            try
            {
                File.Move(path, target);
                return target;
            }
            catch (Exception moveFailed) when (moveFailed is IOException || moveFailed is UnauthorizedAccessException)
            {
                try
                {
                    File.Copy(path, target);
                    return target;
                }
                catch (Exception copyFailed) when (copyFailed is IOException || copyFailed is UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }
    }
}
