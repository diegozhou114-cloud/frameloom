using NUnit.Framework;
using UnityEngine;

namespace ZWebGLFrameWork.Runtime.Tests.Support
{
    public sealed class RuntimeAssemblySmokeTests
    {
        [Test]
        public void RuntimePlayModeAssembly_IsDiscoverable()
        {
            Assert.That(Application.isPlaying, Is.True);
        }
    }
}
