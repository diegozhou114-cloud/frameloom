using NUnit.Framework;
using UnityEngine;

namespace ZWebGLFrameWork.Combat.Tests
{
    public sealed class LegacyPoolPrewarmerTests
    {
        [Test]
        public void LegacyPoolPrewarmerTypeAndRoot_AreAbsent()
        {
            Assert.That(System.Type.GetType(
                "ZWebGLFrameWork.Data.PoolPrewarmer, ZWebGLFrameWork.Data"), Is.Null);
            Assert.That(GameObject.Find("[PoolPrewarmer_Root]"), Is.Null);
        }
    }
}
