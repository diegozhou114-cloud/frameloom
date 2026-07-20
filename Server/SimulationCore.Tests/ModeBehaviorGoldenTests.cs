using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore.Tests;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class ModeBehaviorGoldenTests
    {
        [Test]
        public void SharedUnityFixture_ProducesCanonicalDotNetVector()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = ModeTestFixture.Manifest();
            SimBattleResult result = ModeTestFixture.EvaluateVillageAttackVector();

            Assert.That(manifest.ToCanonicalBytes(), Is.EqualTo(bytes));
            Assert.That(result.ResultId, Is.EqualTo("mode-vector:120:00000000000A11CE"));
            Assert.That(result.FinalChecksum, Is.EqualTo(0xA11CEUL));
        }
    }
}
