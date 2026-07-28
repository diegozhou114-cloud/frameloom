using System;
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

        [Test]
        public void ContentManifest_ArchetypeCountExceedsPayload_IsRejectedBeforeAllocation()
        {
            byte[] payload =
            {
                0x5A, 0x53, 0x49, 0x4D,
                0x01, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00,
            };

            FormatException error = Assert.Throws<FormatException>(() =>
                DeterministicContentManifest.Parse(payload));

            Assert.That(error.Message, Is.EqualTo("Archetype count exceeds the manifest payload."));
        }
    }
}
