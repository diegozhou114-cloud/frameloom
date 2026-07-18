using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class UICloseLayerTests : RuntimeTestBase
    {
        [UnityTest]
        public IEnumerator UIManager_CloseLayer_DoesNotLeakOrDuplicateCachedPanel()
        {
            if (UIManager.HasInstance)
                Object.DestroyImmediate(UIManager.Instance.gameObject);

            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            RebindProbePanel first = manager.Open<RebindProbePanel>("first");
            manager.CloseLayer(UILayer.Normal);
            RebindProbePanel second = manager.Open<RebindProbePanel>("second");

            Assert.That(second, Is.SameAs(first));
            Assert.That(Object.FindObjectsOfType<RebindProbePanel>(true), Has.Length.EqualTo(1));
        }
    }
}
