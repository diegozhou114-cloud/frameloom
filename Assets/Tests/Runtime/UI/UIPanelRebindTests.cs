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

        [UnityTest]
        public IEnumerator Panel_ReopenWhilePaused_RebindsWithoutBypassingStackOrder()
        {
            if (UIManager.HasInstance)
                Object.DestroyImmediate(UIManager.Instance.gameObject);

            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            RebindProbePanel panel = manager.Open<RebindProbePanel>("first");
            BlockingPanel blocker = manager.Open<BlockingPanel>();
            Assert.That(panel.IsPaused, Is.True);

            RebindProbePanel reopened = manager.Open<RebindProbePanel>("updated");

            Assert.That(reopened, Is.SameAs(panel));
            Assert.That(panel.BoundValue, Is.EqualTo("updated"));
            Assert.That(panel.IsPaused, Is.True);
            Assert.That(panel.CanvasGroup.interactable, Is.False);
            Assert.That(manager.GetTop(UILayer.Normal), Is.SameAs(blocker));
        }

        [UnityTest]
        public IEnumerator Panel_ReopenUnderPopupBlocker_PreservesLayerAndSortingOrder()
        {
            if (UIManager.HasInstance)
                Object.DestroyImmediate(UIManager.Instance.gameObject);

            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            RebindProbePanel panel = manager.Open<RebindProbePanel>("first");
            int sortingOrder = panel.SortingOrder;
            PopupBlockingPanel blocker = manager.Open<PopupBlockingPanel>();
            Assert.That(panel.IsPaused, Is.True);

            RebindProbePanel reopened = manager.Open<RebindProbePanel>("updated");

            Assert.That(reopened, Is.SameAs(panel));
            Assert.That(panel.BoundValue, Is.EqualTo("updated"));
            Assert.That(panel.IsPaused, Is.True);
            Assert.That(panel.CanvasGroup.interactable, Is.False);
            Assert.That(panel.SortingOrder, Is.EqualTo(sortingOrder));
            Assert.That(manager.GetTop(UILayer.Normal), Is.SameAs(panel));
            Assert.That(manager.GetTop(UILayer.Popup), Is.SameAs(blocker));
        }

        private sealed class BlockingPanel : UIBase
        {
        }

        private sealed class PopupBlockingPanel : UIBase
        {
            protected override void Awake()
            {
                _layer = UILayer.Popup;
                _pauseLowerPanels = true;
                base.Awake();
            }
        }
    }
}
