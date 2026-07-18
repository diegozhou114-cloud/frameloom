using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using ZWebGLFrameWork.Editor;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class RepositoryIntegrityTests
    {
        [Test]
        public void RepositoryMetaAudit_HasNoMissingOrOrphanedMeta()
        {
            var errors = new List<string>();
            var assetsRoot = Path.GetFullPath("Assets");
            foreach (var path in Directory.EnumerateFileSystemEntries(
                assetsRoot, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path).StartsWith(".")) continue;
                if (path.EndsWith(".meta"))
                {
                    if (!File.Exists(path.Substring(0, path.Length - 5)) &&
                        !Directory.Exists(path.Substring(0, path.Length - 5)))
                        errors.Add($"orphan-meta {path}");
                    continue;
                }
                if (!File.Exists(path + ".meta"))
                    errors.Add($"missing-meta {path}");
            }
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void BuildScenes_LauncherIsEnabledSceneZero()
        {
            var enabled = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            Assert.That(enabled, Is.Not.Empty);
            Assert.That(enabled[0], Is.EqualTo("Assets/Scence/Launcher.unity"));
            Assert.That(BuildScript.GetEnabledScenePaths(), Is.EqualTo(enabled));
            Assert.That(File.Exists(enabled[0]), Is.True);
        }
    }
}
