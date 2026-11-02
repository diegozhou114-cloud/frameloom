using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed partial class UISubscriptionScopeTests : RuntimeTestBase
    {
        [UnityTest]
        public IEnumerator OpenCloseRepeatedly_RegistersExactlyOneCallbackPerCycle()
        {
            UIManager manager = CreateManager();
            yield return null;
            ScopeProbePanel panel = null;
            for (int index = 0; index < 100; index++)
            {
                panel = manager.Open<ScopeProbePanel>();
                EventBus.Publish("subscription-probe");
                Assert.That(panel.Calls, Is.EqualTo(index + 1));
                manager.Close(panel);
                EventBus.Publish("subscription-probe");
                Assert.That(panel.Calls, Is.EqualTo(index + 1));
                Assert.That(EventBus.TotalEventCount, Is.Zero);
            }
            Assert.Throws<InvalidOperationException>(() => panel.SubscribeWhileClosed());
        }

        [UnityTest]
        public IEnumerator OnOpenFailure_RemovesSubscriptionsBeforeRollbackCompletes()
        {
            UIManager manager = CreateManager();
            yield return null;

            Assert.Throws<InvalidOperationException>(() => manager.Open<ScopeProbePanel>("throw"));

            ScopeProbePanel panel = Object.FindObjectOfType<ScopeProbePanel>(true);
            EventBus.Publish("subscription-probe");
            Assert.That(panel.Calls, Is.Zero);
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(EventBus.TotalEventCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DestroyWithoutClose_RemovesOpenCycleSubscriptions()
        {
            UIManager manager = CreateManager();
            yield return null;
            ScopeProbePanel panel = manager.Open<ScopeProbePanel>();
            Action publish = () => EventBus.Publish("subscription-probe");

            Object.DestroyImmediate(panel.gameObject);

            Assert.That(panel.IsOpen, Is.False);
            Assert.That(panel.IsPaused, Is.False);
            Assert.Throws<InvalidOperationException>(() => panel.SubscribeWhileClosed());
            Assert.DoesNotThrow(() => publish());
            Assert.That(EventBus.TotalEventCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator OnCloseReopensPanel_OldCycleCleanupPreservesNewSubscriptions()
        {
            UIManager manager = CreateManager();
            yield return null;
            ScopeProbePanel panel = manager.Open<ScopeProbePanel>();
            panel.ReopenOnClose = true;

            manager.Close(panel);
            EventBus.Publish("subscription-probe");

            Assert.That(panel.IsOpen, Is.True);
            Assert.That(panel.Calls, Is.EqualTo(1));
            manager.Close(panel);
            EventBus.Publish("subscription-probe");
            Assert.That(panel.Calls, Is.EqualTo(1));
            Assert.That(EventBus.TotalEventCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator OnCloseThrows_SubscriptionsAreAlreadyReleased()
        {
            UIManager manager = CreateManager();
            yield return null;
            ScopeProbePanel panel = manager.Open<ScopeProbePanel>();
            panel.ThrowOnClose = true;

            Assert.Throws<InvalidOperationException>(() => manager.Close(panel));

            EventBus.Publish("subscription-probe");
            Assert.That(panel.Calls, Is.Zero);
            Assert.That(EventBus.TotalEventCount, Is.Zero);
        }

        private UIManager CreateManager()
        {
            if (UIManager.HasInstance) Object.DestroyImmediate(UIManager.Instance.gameObject);
            var manager = CreateOwnedObject("SubscriptionUIManager").AddComponent<UIManager>();
            // Count panel subscriptions only; remove selection routing before its Start subscribes.
            Object.DestroyImmediate(manager.GetComponent<BuildingInfoController>());
            return manager;
        }

        private static FieldInfo Field(Type type, string name) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class ScopeProbePanel : UIBase
        {
            public int Calls { get; private set; }
            public bool ReopenOnClose { get; set; }
            public bool ThrowOnClose { get; set; }

            protected override void OnOpen(object data)
            {
                OpenSubscriptions.Subscribe("subscription-probe", OnEvent);
                if (Equals(data, "throw")) throw new InvalidOperationException("open failed");
            }

            protected override void OnClose()
            {
                if (ThrowOnClose) throw new InvalidOperationException("close failed");
                if (!ReopenOnClose) return;
                ReopenOnClose = false;
                UIManager.Instance.Open<ScopeProbePanel>();
            }

            public void SubscribeWhileClosed() =>
                OpenSubscriptions.Subscribe("subscription-probe", OnEvent);

            private void OnEvent() => Calls++;
        }
    }
}
