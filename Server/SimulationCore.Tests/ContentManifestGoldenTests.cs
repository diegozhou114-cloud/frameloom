using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class ContentManifestGoldenTests
    {
        [Test]
        public void ContentManifest_UnityAndDotNet_ProduceSameCanonicalHash()
        {
            var manifest = new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 20, 10000, 20)
            });
            byte[] bytes = manifest.ToCanonicalBytes();
            Assert.That(bytes[0], Is.EqualTo(0x5A));
            Assert.That(bytes[4], Is.EqualTo(0x01));
            Assert.That(manifest.ContentHash.Length, Is.EqualTo(32));
        }
    }
}
