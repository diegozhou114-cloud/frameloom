using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class UIPanelRebindTests : RuntimeTestBase
    {
        [UnityTest]
        public IEnumerator Panel_Reopen_ReusesHierarchyAndRebindsData()
        {
            if (UIManager.HasInstance)
                Object.DestroyImmediate(UIManager.Instance.gameObject);

            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            RebindProbePanel panel = manager.Open<RebindProbePanel>("first");
            manager.Close(panel);
            panel = manager.Open<RebindProbePanel>("second");
            RebindProbePanel sameOpenPanel = manager.Open<RebindProbePanel>("third");
            EventBus.Publish("RebindProbeEvent");

            Assert.That(sameOpenPanel, Is.SameAs(panel));
            Assert.That(panel.BuildCount, Is.EqualTo(1));
            Assert.That(panel.transform.childCount, Is.EqualTo(1));
            Assert.That(panel.BindCount, Is.EqualTo(3));
            Assert.That(panel.BoundValue, Is.EqualTo("third"));
            Assert.That(panel.CallbackCount, Is.EqualTo(1));
        }
    }
}
