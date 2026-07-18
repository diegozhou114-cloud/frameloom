using System;
using System.Linq;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class ContentManifestTests
    {
        [Test]
        public void ComponentRegistry_PermutedRegistration_PreservesTypeMapping()
        {
            var registrations = SimComponentSchema.Registrations.Reverse().ToArray();
            var registry = new ComponentTypeRegistry(registrations);
            Assert.That(registry.GetId(typeof(SimTransformComponent)), Is.EqualTo(SimComponentTypeId.Transform));
            Assert.That(registry.GetId(typeof(SimTargetComponent)), Is.EqualTo(SimComponentTypeId.Target));
        }

        [Test]
        public void ContentManifest_UnityAndDotNet_ProduceSameCanonicalHash()
        {
            var manifest = new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 20, 10000, 20)
            });
            byte[] bytes = manifest.ToCanonicalBytes();
            Assert.That(bytes.Take(8).ToArray(),
                Is.EqualTo(new byte[] { 0x5A, 0x53, 0x49, 0x4D, 0x01, 0x00, 0x00, 0x00 }));
            Assert.That(manifest.ContentHash.Length, Is.EqualTo(32));
            Assert.That(DeterministicContentManifest.Parse(bytes).ContentHash,
                Is.EqualTo(manifest.ContentHash));
        }

        [Test]
        public void ComponentRegistry_UnknownOrMismatchedRegistration_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new ComponentTypeRegistry(new[]
            {
                new ComponentRegistration(typeof(string), 8)
            }));
            Assert.Throws<ArgumentException>(() => new ComponentTypeRegistry(new[]
            {
                new ComponentRegistration(typeof(SimTransformComponent), SimComponentTypeId.Health)
            }));
        }

        [Test]
        public void ContentManifest_UnsupportedSchema_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DeterministicContentManifest(2, Array.Empty<SimArchetypeDefinition>()));
        }
    }
}
