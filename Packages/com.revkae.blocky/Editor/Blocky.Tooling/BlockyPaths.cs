using UnityEditor;

namespace Blocky.Tooling
{
    /// <summary>
    /// Where Blocky's own assets are, and where the tools put a project's. Blocky is a package, so its own assets are
    /// read through <c>Packages/com.revkae.blocky/…</c> — a path the AssetDatabase resolves however the package was
    /// installed (embedded, a local folder, or git) — and never written to: installed from git it is read-only.
    /// Everything the tools make for a project goes under <c>Assets</c>.
    /// </summary>
    internal static class BlockyPaths
    {
        /// <summary>The package's name in the Package Manager.</summary>
        public const string PackageName = "com.revkae.blocky";

        /// <summary>The package's root as an asset path.</summary>
        public const string PackageRoot = "Packages/" + PackageName;

        /// <summary>The workspace's stylesheets (<c>blocky-*.uss</c>).</summary>
        public const string StylesFolder = PackageRoot + "/Runtime/Blocky.Editor/Styles";

        /// <summary>The in-game workspace, wired and ready: <c>UIDocument</c>, panel settings, stylesheets, <c>BlockyInGamePanel</c>.</summary>
        public const string WorkspacePrefab = PackageRoot + "/Runtime/Prefabs/Blocky Workspace.prefab";

        /// <summary>Keeps the reflection-only block ops in stripped builds (<see cref="BlockyLinkXml"/>).</summary>
        public const string LinkXml = PackageRoot + "/Runtime/link.xml";

        /// <summary>Where the Program Editor window saves a program it makes for an object.</summary>
        public const string ProgramsFolder = "Assets/Blocky/Programs";

        /// <summary>Creates <paramref name="path"/> one folder at a time through the AssetDatabase, so each gets its .meta.</summary>
        public static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
