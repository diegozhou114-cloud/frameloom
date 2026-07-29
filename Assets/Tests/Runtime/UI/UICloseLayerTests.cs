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

        [UnityTest]
        public IEnumerator UIManager_CloseLayer_ResumesLowerPanelPausedByClosedTopPanel()
        {
            if (UIManager.HasInstance)
                Object.DestroyImmediate(UIManager.Instance.gameObject);

            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            LowerLayerPanel lower = manager.Open<LowerLayerPanel>();
            manager.Open<TopLayerPanel>();
            Assert.That(lower.IsPaused, Is.True);

            manager.CloseLayer(UILayer.Top);

            Assert.That(lower.IsOpen, Is.True);
            Assert.That(lower.IsPaused, Is.False);
            Assert.That(lower.CanvasGroup.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator UIManager_CloseLayer_KeepsPanelsBelowRemainingModalPaused()
        {
            if (UIManager.HasInstance)
                Object.DestroyImmediate(UIManager.Instance.gameObject);

            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            LowerLayerPanel lower = manager.Open<LowerLayerPanel>();
            MiddleLayerPanel middle = manager.Open<MiddleLayerPanel>();
            manager.Open<TopLayerPanel>();
            Assert.That(lower.IsPaused, Is.True);
            Assert.That(middle.IsPaused, Is.True);

            manager.CloseLayer(UILayer.Top);

            Assert.That(middle.IsOpen, Is.True);
            Assert.That(middle.IsPaused, Is.False);
            Assert.That(middle.CanvasGroup.interactable, Is.True);
            Assert.That(lower.IsOpen, Is.True);
            Assert.That(lower.IsPaused, Is.True);
            Assert.That(lower.CanvasGroup.interactable, Is.False);
        }

        [UnityTest]
        public IEnumerator UIManager_CloseModalTop_ResumesLowerLayersWhenRevealedPanelIsNonModal()
        {
            if (UIManager.HasInstance)
                Object.DestroyImmediate(UIManager.Instance.gameObject);

            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            LowerLayerPanel lower = manager.Open<LowerLayerPanel>();
            NonModalPopupPanel nonModal = manager.Open<NonModalPopupPanel>();
            MiddleLayerPanel modal = manager.Open<MiddleLayerPanel>();
            Assert.That(lower.IsPaused, Is.True);
            Assert.That(nonModal.IsPaused, Is.True);

            manager.Close(modal);

            Assert.That(nonModal.IsOpen, Is.True);
            Assert.That(nonModal.IsPaused, Is.False);
            Assert.That(nonModal.CanvasGroup.interactable, Is.True);
            Assert.That(lower.IsOpen, Is.True);
            Assert.That(lower.IsPaused, Is.False);
            Assert.That(lower.CanvasGroup.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator UIManager_CloseModalUnderHigherModal_KeepsRevealedPanelsPaused()
        {
            if (UIManager.HasInstance)
                Object.DestroyImmediate(UIManager.Instance.gameObject);

            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            LowerLayerPanel lower = manager.Open<LowerLayerPanel>();
            NonModalPopupPanel nonModal = manager.Open<NonModalPopupPanel>();
            MiddleLayerPanel modal = manager.Open<MiddleLayerPanel>();
            manager.Open<TopLayerPanel>();

            manager.Close(modal);

            Assert.That(nonModal.IsOpen, Is.True);
            Assert.That(nonModal.IsPaused, Is.True);
            Assert.That(nonModal.CanvasGroup.interactable, Is.False);
            Assert.That(lower.IsOpen, Is.True);
            Assert.That(lower.IsPaused, Is.True);
            Assert.That(lower.CanvasGroup.interactable, Is.False);
        }

        private sealed class LowerLayerPanel : UIBase
        {
            protected override void Awake()
            {
                _layer = UILayer.Normal;
                _pauseLowerPanels = true;
                base.Awake();
            }
        }

        private sealed class TopLayerPanel : UIBase
        {
            protected override void Awake()
            {
                _layer = UILayer.Top;
                _pauseLowerPanels = true;
                base.Awake();
            }
        }

        private sealed class MiddleLayerPanel : UIBase
        {
            protected override void Awake()
            {
                _layer = UILayer.Popup;
                _pauseLowerPanels = true;
                base.Awake();
            }
        }

        private sealed class NonModalPopupPanel : UIBase
        {
            protected override void Awake()
            {
                _layer = UILayer.Popup;
                _pauseLowerPanels = false;
                base.Awake();
            }
        }
    }
}
