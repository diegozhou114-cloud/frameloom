using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
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

        [Test]
        public void BuildScript_EmitsMachineReadableBuildReport()
        {
            string emitted = null;
            Application.LogCallback handler = (condition, _, __) => emitted = condition;
            Application.logMessageReceived += handler;
            try
            {
                BuildScript.EmitBuildReport(BuildTarget.WebGL, "Builds/WebGL",
                    BuildResult.Succeeded, 1234UL, 0,
                    System.TimeSpan.FromMilliseconds(1250));
            }
            finally
            {
                Application.logMessageReceived -= handler;
            }

            Assert.That(emitted, Does.StartWith(BuildScript.BuildReportPrefix));
            string json = emitted.Substring(BuildScript.BuildReportPrefix.Length);
            var report = JsonUtility.FromJson<BuildReportDto>(json);
            Assert.That(report.target, Is.EqualTo(BuildTarget.WebGL.ToString()));
            Assert.That(report.outputPath, Is.EqualTo("Builds/WebGL"));
            Assert.That(report.result, Is.EqualTo(BuildResult.Succeeded.ToString()));
            Assert.That(report.totalSizeBytes, Is.EqualTo(1234UL));
            Assert.That(report.totalErrors, Is.Zero);
            Assert.That(report.totalTimeSeconds, Is.EqualTo(1.25d));
        }

        [Test]
        public void RequiredRuntimeAssets_AllProductionKeysResolve()
        {
            var result = RequiredAssetManifestValidator.Validate(
                RequiredAssetManifestValidator.ManifestPath);

            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Errors));
            Assert.That(BuildValidator.ValidateBootPipeline(), Is.True);
        }

        [Test]
        public void FrameworkBattleDefaults_KeepProductionReplacementMarkers()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string combat = Path.Combine(root,
                "Packages/com.zwebglframework.runtime/Runtime/Combat");
            Assert.That(File.ReadAllText(Path.Combine(combat, "FrameworkBattleAuthority.cs")),
                Does.Contain("TODO(PRODUCTION-AUTHORITY)"));
            Assert.That(File.ReadAllText(Path.Combine(combat, "FrameworkBattleContent.cs")),
                Does.Contain("TODO(PRODUCTION-CONTENT)"));
        }

        [System.Serializable]
        private sealed class BuildReportDto
        {
            public string target;
            public string outputPath;
            public string result;
            public ulong totalSizeBytes;
            public int totalErrors;
            public double totalTimeSeconds;
        }

        [Test]
        public void RequiredAssetManifest_EmptyEntryArrayIsRejected()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path,
                    "{\"version\":1," +
                    "\"supportedModes\":[\"builder_base\",\"campaign\",\"clan_war\",\"village_attack\"]," +
                    "\"archetypeIds\":[1],\"unitIds\":[1],\"uiPanels\":[\"LoginPanel\"]," +
                    "\"entries\":[]}");

                var result = RequiredAssetManifestValidator.Validate(path);

                Assert.That(result.Errors,
                    Does.Contain("Manifest contains no required asset entries."));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void RequiredAssetManifest_NullAndEmptyEntriesAreRejected()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path,
                    "{\"version\":1," +
                    "\"supportedModes\":[\"builder_base\",\"campaign\",\"clan_war\",\"village_attack\"]," +
                    "\"archetypeIds\":[1],\"unitIds\":[1],\"uiPanels\":[\"LoginPanel\"]," +
                    "\"entries\":[null,{\"key\":\"\",\"assetPath\":\"\",\"expectedType\":\"\"}]}");

                var result = RequiredAssetManifestValidator.Validate(path);

                Assert.That(result.Errors,
                    Does.Contain("Manifest contains a null required asset entry."));
                Assert.That(result.Errors,
                    Does.Contain("Required asset entry has an empty key."));
                Assert.That(result.Errors,
                    Does.Contain("Required asset entry <empty> has an empty path."));
                Assert.That(result.Errors,
                    Does.Contain("Required asset entry <empty> has an empty expected type."));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
