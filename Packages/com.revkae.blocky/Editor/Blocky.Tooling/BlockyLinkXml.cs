using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace Blocky.Tooling
{
    /// <summary>
    /// Hands the package's <c>link.xml</c> to the linker for every build. Unity picks up <c>link.xml</c> files under
    /// <c>Assets</c> by itself; passing the package's own here means it is used however the package was installed. Without
    /// it an IL2CPP build above Minimal stripping loses every block op, because nothing names them —
    /// <c>OpTableBuilder</c> makes them by reflection — and the program data Newtonsoft fills by reflection.
    /// </summary>
    internal sealed class BlockyLinkXml : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            // Null when Blocky was copied into Assets rather than installed as a package — Unity reads its link.xml there anyway.
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BlockyLinkXml).Assembly);
            return package == null ? null : Path.Combine(package.resolvedPath, "Runtime", "link.xml");
        }
    }
}
