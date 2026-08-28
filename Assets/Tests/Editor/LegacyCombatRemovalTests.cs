using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class LegacyCombatRemovalTests
    {
        [Test]
        public void LegacyCombat_SourceAndToggleAreAbsent()
        {
            string root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string[] forbiddenPaths =
            {
                "Packages/com.zwebglframework.runtime/Runtime/Combat/AI",
                "Packages/com.zwebglframework.runtime/Runtime/Combat/Components",
                "Packages/com.zwebglframework.runtime/Runtime/Combat/Core",
                "Packages/com.zwebglframework.runtime/Runtime/Combat/Events",
                "Packages/com.zwebglframework.runtime/Runtime/Combat/Modes",
                "Packages/com.zwebglframework.runtime/Runtime/Combat/Spawner",
                "Packages/com.zwebglframework.runtime/Runtime/Combat/Systems",
                "Packages/com.zwebglframework.runtime/Runtime/Combat/SimInitializerImpl.cs",
                "Packages/com.zwebglframework.runtime/Runtime/Combat/SimulationSyncBridge.cs",
                "Packages/com.zwebglframework.runtime/Runtime/Combat/View/Interpolator.cs",
                "Assets/Tests/Combat/BattleE2ETests.cs"
            };
            Assert.That(forbiddenPaths.Where(path => Directory.Exists(Path.Combine(root, path)) ||
                File.Exists(Path.Combine(root, path))), Is.Empty);
            string appConfig = File.ReadAllText(Path.Combine(root,
                "Packages/com.zwebglframework.runtime/Runtime/Data/AppConfig.cs"));
            Assert.That(appConfig, Does.Not.Contain("UseSimulationCore"));
        }

        [Test]
        public void SerializedAssets_HaveNoMissingGuidReferences()
        {
            string[] assets = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/") &&
                    (path.EndsWith(".prefab") || path.EndsWith(".unity") || path.EndsWith(".asset")))
                .ToArray();
            var missing = new System.Collections.Generic.List<string>();
            foreach (string path in assets)
            {
                string text = File.ReadAllText(path);
                foreach (System.Text.RegularExpressions.Match match in
                    System.Text.RegularExpressions.Regex.Matches(text, "guid: ([0-9a-f]{32})"))
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value)))
                        missing.Add(path + ":" + match.Groups[1].Value);
            }
            Assert.That(missing, Is.Empty, string.Join("\n", missing));
        }
    }
}
