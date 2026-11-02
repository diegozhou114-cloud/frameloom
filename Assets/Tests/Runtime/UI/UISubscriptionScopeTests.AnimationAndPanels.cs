using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed partial class UISubscriptionScopeTests
    {
        [UnityTest]
        public IEnumerator DeferredOnOpenFailure_AlsoReleasesItsSubscriptions()
        {
            UIManager manager = CreateManager();
            yield return null;
            ScopeProbePanel panel = CreateOwnedObject("DeferredOpenSubscriptions")
                .AddComponent<ScopeProbePanel>();
            UIAnimation animation = panel.gameObject.AddComponent<UIAnimation>();
            Field(typeof(UIBase), "_animation").SetValue(panel, animation);
            Field(typeof(UIAnimation), "_openType").SetValue(animation, UIAnimationType.Fade);
            Field(typeof(UIAnimation), "_openDuration").SetValue(animation, 10f);
            Field(typeof(UIAnimation), "_closeType").SetValue(animation, UIAnimationType.None);
            var cache = (Dictionary<Type, UIBase>)Field(typeof(UIManager), "_panelCache")
                .GetValue(manager);
            cache[typeof(ScopeProbePanel)] = panel;
            manager.Open<ScopeProbePanel>("throw");
            int version = (int)Field(typeof(UIBase), "_lifecycleVersion").GetValue(panel);
            animation.Stop();

            TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() =>
                typeof(UIBase).GetMethod("CompleteOpen", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(panel, new object[] { version, "throw" }));

            Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
            EventBus.Publish("subscription-probe");
            Assert.That(panel.Calls, Is.Zero);
            Assert.That(EventBus.TotalEventCount, Is.Zero);
            manager.Close(panel);
        }

        [UnityTest]
        public IEnumerator AnimatedCloseThenReopen_ReleasesOldCycleBeforeAnimationCompletes()
        {
            UIManager manager = CreateManager();
            yield return null;
            ScopeProbePanel panel = CreateOwnedObject("AnimatedSubscriptions")
                .AddComponent<ScopeProbePanel>();
            UIAnimation animation = panel.gameObject.AddComponent<UIAnimation>();
            Field(typeof(UIBase), "_animation").SetValue(panel, animation);
            Field(typeof(UIAnimation), "_openType").SetValue(animation, UIAnimationType.None);
            Field(typeof(UIAnimation), "_closeType").SetValue(animation, UIAnimationType.Fade);
            Field(typeof(UIAnimation), "_closeDuration").SetValue(animation, 10f);
            var cache = (Dictionary<Type, UIBase>)Field(typeof(UIManager), "_panelCache")
                .GetValue(manager);
            cache[typeof(ScopeProbePanel)] = panel;
            manager.Open<ScopeProbePanel>();

            manager.Close(panel);
            int closedVersion = (int)Field(typeof(UIBase), "_lifecycleVersion").GetValue(panel);
            EventBus.Publish("subscription-probe");
            Assert.That(panel.Calls, Is.Zero);
            Assert.That(EventBus.TotalEventCount, Is.Zero);

            Assert.That(manager.Open<ScopeProbePanel>(), Is.SameAs(panel));
            typeof(UIBase).GetMethod("CompleteClose", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(panel, new object[] { closedVersion });
            yield return null;
            EventBus.Publish("subscription-probe");
            Assert.That(panel.IsOpen, Is.True);
            Assert.That(panel.gameObject.activeSelf, Is.True);
            Assert.That(panel.Calls, Is.EqualTo(1));
            Assert.That(EventBus.TotalEventCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PilotPanels_ReopenWithoutDuplicateSubscriptions_IgnoreEventsWhenClosed()
        {
            UIManager manager = CreateManager();
            yield return null;
            for (int index = 0; index < 100; index++)
            {
                LoadingPanel loading = manager.Open<LoadingPanel>();
                ErrorPanel error = manager.Open<ErrorPanel>();
                EventBus.Publish(BootProgressEvent.EventKey, new BootProgressEvent("phase", 0.5f));
                EventBus.Publish(BootErrorEvent.EventKey, new BootErrorEvent("module", "message"));
                Text phase = loading.transform.Find("PhaseText").GetComponent<Text>();
                Text message = error.transform.Find("DialogBox/Message").GetComponent<Text>();
                Assert.That(phase.text, Is.EqualTo("phase"));
                Assert.That(message.text, Is.EqualTo("[module]\nmessage"));
                Assert.That(EventBus.TotalEventCount, Is.EqualTo(2));

                manager.Close(error);
                manager.Close(loading);
                EventBus.Publish(BootProgressEvent.EventKey, new BootProgressEvent("closed", 1f));
                EventBus.Publish(BootErrorEvent.EventKey, new BootErrorEvent("closed", "closed"));
                Assert.That(phase.text, Is.EqualTo("phase"));
                Assert.That(message.text, Is.EqualTo("[module]\nmessage"));
                Assert.That(EventBus.TotalEventCount, Is.Zero);
            }
        }
    }
}
