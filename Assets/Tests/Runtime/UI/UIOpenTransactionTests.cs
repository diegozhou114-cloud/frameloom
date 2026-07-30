using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class UIOpenTransactionTests : RuntimeTestBase
    {
        private static readonly FieldInfo StackField = typeof(UIManager).GetField(
            "_stack", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo DoOpenMethod = typeof(UIBase).GetMethod(
            "DoOpen", BindingFlags.Instance | BindingFlags.NonPublic);

        [UnityTest]
        public IEnumerator Open_WhenPanelClosesItself_KeepsItClosedAndUnowned()
        {
            UIManager manager = CreateManager();
            yield return null;

            SelfClosingPanel panel = manager.Open<SelfClosingPanel>();

            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(panel.gameObject.activeSelf, Is.False);
            Assert.That(manager.Get<SelfClosingPanel>(), Is.Null);
            Assert.That(manager.IsOpen<SelfClosingPanel>(), Is.False);
            Assert.That(manager.GetTop(UILayer.Normal), Is.Null);
            Assert.That(GetStack(manager).Count(UILayer.Normal), Is.Zero);
            Assert.That(Object.FindObjectsOfType<SelfClosingPanel>(true), Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Open_WhenSameTypeReenters_RebindsOneOwnedInstance()
        {
            UIManager manager = CreateManager();
            yield return null;

            SameTypeReentrantPanel panel = manager.Open<SameTypeReentrantPanel>("outer");

            Assert.That(panel.ReentrantResult, Is.SameAs(panel));
            Assert.That(panel.OpenCount, Is.EqualTo(1));
            Assert.That(panel.RebindCount, Is.EqualTo(1));
            Assert.That(panel.OpenData, Is.EqualTo("inner"));
            Assert.That(manager.Get<SameTypeReentrantPanel>(), Is.SameAs(panel));
            Assert.That(manager.GetTop(UILayer.Normal), Is.SameAs(panel));
            Assert.That(GetStack(manager).Count(UILayer.Normal), Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<SameTypeReentrantPanel>(true), Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Open_WhenOnOpenOpensAnotherPanel_KeepsNestedPanelOnTop()
        {
            UIManager manager = CreateManager();
            yield return null;

            NestedOpeningPanel panel = manager.Open<NestedOpeningPanel>();

            Assert.That(panel.OpenedPanel, Is.Not.Null);
            Assert.That(manager.GetTop(UILayer.Normal), Is.SameAs(panel.OpenedPanel));
            Assert.That(GetStack(manager).Count(UILayer.Normal), Is.EqualTo(2));
            Assert.That(panel.IsPaused, Is.True);
            Assert.That(panel.CanvasGroup.interactable, Is.False);
            Assert.That(panel.OpenedPanel.IsPaused, Is.False);
            Assert.That(panel.OpenedPanel.CanvasGroup.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator Open_WhenLayerIsAtCapacity_RejectsWithoutOpeningOrChangingState()
        {
            UIManager manager = CreateManager();
            yield return null;

            CapacityOccupantPanel occupant = manager.Open<CapacityOccupantPanel>();
            UIStack stack = GetStack(manager);
            CapacityFillerPanel filler = CreateOwnedObject("CapacityFiller")
                .AddComponent<CapacityFillerPanel>();
            for (int index = 1; index < 50; index++)
                stack.Push(filler);

            Assert.That(stack.Count(UILayer.Normal), Is.EqualTo(50));
            Assert.That(occupant.IsPaused, Is.True);
            UIBase topBefore = stack.Peek(UILayer.Normal);

            LogAssert.Expect(LogType.Error, new Regex(
                @"\[UIStack\].*Push 被拒绝: CapacityRejectedPanel"));
            CapacityRejectedPanel result = manager.Open<CapacityRejectedPanel>();

            CapacityRejectedPanel rejected = Object.FindObjectOfType<CapacityRejectedPanel>(true);
            Assert.That(result, Is.Null);
            Assert.That(rejected, Is.Not.Null);
            Assert.That(rejected.OpenCount, Is.Zero);
            Assert.That(rejected.IsOpen, Is.False);
            Assert.That(rejected.gameObject.activeSelf, Is.False);
            Assert.That(manager.Get<CapacityRejectedPanel>(), Is.Null);
            Assert.That(stack.Count(UILayer.Normal), Is.EqualTo(50));
            Assert.That(stack.Peek(UILayer.Normal), Is.SameAs(topBefore));
            Assert.That(occupant.IsPaused, Is.True);
        }

        [UnityTest]
        public IEnumerator Open_WhenOnOpenThrows_RollsBackAndResumesPreviousPanel()
        {
            UIManager manager = CreateManager();
            yield return null;

            StablePanel stable = manager.Open<StablePanel>();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => manager.Open<ThrowingPanel>());

            ThrowingPanel failed = Object.FindObjectOfType<ThrowingPanel>(true);
            Assert.That(exception.Message, Is.EqualTo("OnOpen failed"));
            Assert.That(failed, Is.Not.Null);
            Assert.That(failed.IsOpen, Is.False);
            Assert.That(failed.gameObject.activeSelf, Is.False);
            Assert.That(manager.Get<ThrowingPanel>(), Is.Null);
            Assert.That(manager.GetTop(UILayer.Normal), Is.SameAs(stable));
            Assert.That(GetStack(manager).Count(UILayer.Normal), Is.EqualTo(1));
            Assert.That(stable.IsOpen, Is.True);
            Assert.That(stable.IsPaused, Is.False);
            Assert.That(stable.CanvasGroup.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator Pop_WhenExactPanelIsNotOwned_DoesNothing()
        {
            UIManager manager = CreateManager();
            yield return null;

            StablePanel owned = manager.Open<StablePanel>();
            UnownedPanel unowned = CreateOwnedObject("UnownedPanel").AddComponent<UnownedPanel>();
            DoOpenMethod.Invoke(unowned, new object[] { null });
            UIStack stack = GetStack(manager);

            stack.Pop(unowned);

            Assert.That(unowned.IsOpen, Is.True);
            Assert.That(unowned.gameObject.activeSelf, Is.True);
            Assert.That(stack.Count(UILayer.Normal), Is.EqualTo(1));
            Assert.That(stack.Peek(UILayer.Normal), Is.SameAs(owned));
            Assert.That(owned.IsOpen, Is.True);
            Assert.That(owned.IsPaused, Is.False);
        }

        private UIManager CreateManager()
        {
            if (UIManager.HasInstance)
                Object.DestroyImmediate(UIManager.Instance.gameObject);
            return CreateOwnedObject("UIManager").AddComponent<UIManager>();
        }

        private static UIStack GetStack(UIManager manager) => (UIStack)StackField.GetValue(manager);

        private sealed class SelfClosingPanel : UIBase
        {
            protected override void OnOpen(object data) => Close();
        }

        private sealed class SameTypeReentrantPanel : UIBase
        {
            public int OpenCount { get; private set; }
            public int RebindCount { get; private set; }
            public SameTypeReentrantPanel ReentrantResult { get; private set; }

            protected override void OnOpen(object data)
            {
                OpenCount++;
                if (Equals(data, "outer"))
                    ReentrantResult = UIManager.Instance.Open<SameTypeReentrantPanel>("inner");
            }

            protected override void OnRebind(object data) => RebindCount++;
        }

        private sealed class NestedOpeningPanel : UIBase
        {
            public NestedOpenedPanel OpenedPanel { get; private set; }

            protected override void OnOpen(object data)
            {
                OpenedPanel = UIManager.Instance.Open<NestedOpenedPanel>();
            }
        }

        private sealed class NestedOpenedPanel : UIBase
        {
        }

        private sealed class CapacityOccupantPanel : UIBase
        {
        }

        private sealed class CapacityFillerPanel : UIBase
        {
        }

        private sealed class CapacityRejectedPanel : UIBase
        {
            public int OpenCount { get; private set; }

            protected override void OnOpen(object data) => OpenCount++;
        }

        private sealed class StablePanel : UIBase
        {
        }

        private sealed class ThrowingPanel : UIBase
        {
            protected override void OnOpen(object data)
            {
                throw new InvalidOperationException("OnOpen failed");
            }
        }

        private sealed class UnownedPanel : UIBase
        {
        }
    }
}
