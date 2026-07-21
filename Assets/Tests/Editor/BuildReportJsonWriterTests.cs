using System;
using System.IO;
using NUnit.Framework;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class BuildReportJsonWriterTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "zweb-build-report-tests",
                TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(Path.Combine(_root, "builds", "WebGL"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void BuildReportWriter_CollectsDeterministicArtifactHashes()
        {
            string wasm = Path.Combine(_root, "builds", "WebGL", "game.wasm");
            File.WriteAllBytes(wasm, new byte[] { 1, 2, 3, 4 });

            BuildArtifactRecord[] first = BuildReportJsonWriter.CollectArtifacts(
                Path.Combine(_root, "builds", "WebGL"), _root);
            BuildArtifactRecord[] second = BuildReportJsonWriter.CollectArtifacts(
                Path.Combine(_root, "builds", "WebGL"), _root);

            Assert.That(first, Has.Length.EqualTo(1));
            Assert.That(first[0].path, Is.EqualTo("builds/WebGL/game.wasm"));
            Assert.That(first[0].bytes, Is.EqualTo(4));
            Assert.That(first[0].sha256, Has.Length.EqualTo(64));
            Assert.That(second[0].sha256, Is.EqualTo(first[0].sha256));
        }

        [Test]
        public void BuildScript_UsesConfiguredExternalEvidenceRoot()
        {
            string previous = Environment.GetEnvironmentVariable("ZWEB_EVIDENCE_ROOT");
            try
            {
                Environment.SetEnvironmentVariable("ZWEB_EVIDENCE_ROOT", _root);
                Assert.That(BuildScript.GetEvidenceRoot(), Is.EqualTo(Path.GetFullPath(_root)));

                string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
                Environment.SetEnvironmentVariable("ZWEB_EVIDENCE_ROOT", projectRoot);
                Assert.Throws<UnityEditor.Build.BuildFailedException>(
                    () => BuildScript.GetEvidenceRoot());
            }
            finally
            {
                Environment.SetEnvironmentVariable("ZWEB_EVIDENCE_ROOT", previous);
            }
        }
    }
}
