using System;
using System.Collections;
using System.Collections.Generic;
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
        private static readonly FieldInfo LayerPanelCountsField = typeof(UIManager).GetField(
            "_layerPanelCounts", BindingFlags.Instance | BindingFlags.NonPublic);
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
        public IEnumerator Open_WhenPreviousOnPauseReentersSameType_RebindsOneAdmission()
        {
            UIManager manager = CreateManager();
            yield return null;

            PauseSameTypePreviousPanel previous = manager.Open<PauseSameTypePreviousPanel>();
            PauseSameTypeOpeningPanel panel = manager.Open<PauseSameTypeOpeningPanel>("outer");

            Assert.That(previous.ReentrantResult, Is.SameAs(panel));
            Assert.That(panel.OpenCount, Is.EqualTo(1));
            Assert.That(panel.RebindCount, Is.EqualTo(1));
            Assert.That(panel.OpenData, Is.EqualTo("inner"));
            Assert.That(manager.GetTop(UILayer.Normal), Is.SameAs(panel));
            Assert.That(GetStack(manager).Count(UILayer.Normal), Is.EqualTo(2));
            Assert.That(CountOccurrences(GetStack(manager), panel), Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<PauseSameTypeOpeningPanel>(true), Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Open_WhenPreviousOnPauseOpensAnotherPanel_KeepsNestedPanelOnTop()
        {
            UIManager manager = CreateManager();
            yield return null;

            PauseNestedPreviousPanel previous = manager.Open<PauseNestedPreviousPanel>();
            PauseOuterPanel outer = manager.Open<PauseOuterPanel>();

            Assert.That(previous.OpenedPanel, Is.Not.Null);
            Assert.That(manager.GetTop(UILayer.Normal), Is.SameAs(previous.OpenedPanel));
            Assert.That(GetStack(manager).Count(UILayer.Normal), Is.EqualTo(3));
            Assert.That(outer.IsPaused, Is.True);
            Assert.That(outer.CanvasGroup.interactable, Is.False);
            Assert.That(previous.OpenedPanel.IsPaused, Is.False);
            Assert.That(previous.OpenedPanel.CanvasGroup.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator Open_WhenLayerIsAtCapacity_RejectsWithoutOpeningOrChangingState()
        {
            UIManager manager = CreateManager();
            yield return null;

            CapacityOccupantPanel occupant = manager.Open<CapacityOccupantPanel>();
            UIStack stack = GetStack(manager);
            var fillers = new List<CapacityFillerPanel>();
            for (int index = 1; index < 50; index++)
            {
                CapacityFillerPanel filler = CreateOwnedObject($"CapacityFiller_{index}")
                    .AddComponent<CapacityFillerPanel>();
                DoOpenMethod.Invoke(filler, new object[] { index });
                fillers.Add(filler);
                stack.Push(filler);
            }

            Assert.That(stack.Count(UILayer.Normal), Is.EqualTo(50));
            Assert.That(fillers, Has.Count.EqualTo(49));
            Assert.That(fillers, Is.All.Matches<CapacityFillerPanel>(
                filler => filler.IsOpen && filler.gameObject.activeSelf));
            Assert.That(occupant.IsPaused, Is.True);
            UIBase topBefore = stack.Peek(UILayer.Normal);
            Assert.That(topBefore, Is.SameAs(fillers[fillers.Count - 1]));
            Assert.That(topBefore.IsPaused, Is.False);
            Assert.That(topBefore.CanvasGroup.interactable, Is.True);

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
            Assert.That(topBefore.IsOpen, Is.True);
            Assert.That(topBefore.IsPaused, Is.False);
            Assert.That(topBefore.CanvasGroup.interactable, Is.True);
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
        public IEnumerator Open_WhenOnOpenAndRollbackOnCloseThrow_ClosesAndRestoresBeforeRethrow()
        {
            UIManager manager = CreateManager();
            yield return null;

            StablePanel stable = manager.Open<StablePanel>();

            LogAssert.Expect(LogType.Exception, new Regex("rollback close failed"));
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => manager.Open<ThrowingOpenAndClosePanel>());

            ThrowingOpenAndClosePanel failed = Object.FindObjectOfType<ThrowingOpenAndClosePanel>(true);
            Assert.That(exception, Is.SameAs(ThrowingOpenAndClosePanel.OpenException));
            Assert.That(failed, Is.Not.Null);
            Assert.That(failed.IsOpen, Is.False);
            Assert.That(failed.gameObject.activeSelf, Is.False);
            Assert.That(manager.Get<ThrowingOpenAndClosePanel>(), Is.Null);
            Assert.That(manager.GetTop(UILayer.Normal), Is.SameAs(stable));
            Assert.That(GetStack(manager).Count(UILayer.Normal), Is.EqualTo(1));
            Assert.That(stable.IsPaused, Is.False);
            Assert.That(stable.CanvasGroup.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator Open_WhenLatestSortingReservationFails_ReusesThatOrder()
        {
            UIManager manager = CreateManager();
            yield return null;

            manager.Open<StablePanel>();

            Assert.Throws<InvalidOperationException>(() => manager.Open<SortingFailurePanel>());
            SortingAfterFailurePanel next = manager.Open<SortingAfterFailurePanel>();

            Assert.That(next, Is.Not.Null);
            Assert.That(GetLayerPanelCount(manager, UILayer.Normal), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Open_WhenNestedPanelAllocatesAfterFailingPanel_DoesNotReclaimNestedOrder()
        {
            UIManager manager = CreateManager();
            yield return null;

            StablePanel stable = manager.Open<StablePanel>();
            Assert.Throws<InvalidOperationException>(() => manager.Open<NestedSortingFailurePanel>());
            NestedSortingSuccessPanel nested = manager.Get<NestedSortingSuccessPanel>();
            SortingAfterNestedPanel next = manager.Open<SortingAfterNestedPanel>();

            Assert.That(nested, Is.Not.Null);
            Assert.That(next, Is.Not.Null);
            Assert.That(stable.IsPaused, Is.True);
            Assert.That(stable.CanvasGroup.interactable, Is.False);
            Assert.That(GetLayerPanelCount(manager, UILayer.Normal), Is.EqualTo(4));
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

        private static int GetLayerPanelCount(UIManager manager, UILayer layer)
        {
            var counts = (Dictionary<UILayer, int>)LayerPanelCountsField.GetValue(manager);
            return counts[layer];
        }

        private static int CountOccurrences(UIStack stack, UIBase target)
        {
            int count = 0;
            foreach (UIBase panel in stack.GetLayerPanels(target.Layer))
            {
                if (ReferenceEquals(panel, target)) count++;
            }
            return count;
        }

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

        private sealed class PauseSameTypePreviousPanel : UIBase
        {
            public PauseSameTypeOpeningPanel ReentrantResult { get; private set; }

            protected override void OnPause()
            {
                ReentrantResult = UIManager.Instance.Open<PauseSameTypeOpeningPanel>("inner");
            }
        }

        private sealed class PauseSameTypeOpeningPanel : UIBase
        {
            public int OpenCount { get; private set; }
            public int RebindCount { get; private set; }

            protected override void OnOpen(object data) => OpenCount++;

            protected override void OnRebind(object data) => RebindCount++;
        }

        private sealed class PauseNestedPreviousPanel : UIBase
        {
            public PauseNestedOpenedPanel OpenedPanel { get; private set; }

            protected override void OnPause()
            {
                OpenedPanel = UIManager.Instance.Open<PauseNestedOpenedPanel>();
            }
        }

        private sealed class PauseOuterPanel : UIBase
        {
        }

        private sealed class PauseNestedOpenedPanel : UIBase
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

        private sealed class ThrowingOpenAndClosePanel : UIBase
        {
            public static readonly InvalidOperationException OpenException =
                new InvalidOperationException("transaction open failed");

            protected override void OnOpen(object data) => throw OpenException;

            protected override void OnClose()
            {
                throw new InvalidOperationException("rollback close failed");
            }
        }

        private sealed class SortingFailurePanel : UIBase
        {
            protected override void OnOpen(object data)
            {
                throw new InvalidOperationException("sorting open failed");
            }
        }

        private sealed class SortingAfterFailurePanel : UIBase
        {
        }

        private sealed class NestedSortingFailurePanel : UIBase
        {
            protected override void OnOpen(object data)
            {
                UIManager.Instance.Open<NestedSortingSuccessPanel>();
                throw new InvalidOperationException("nested sorting open failed");
            }
        }

        private sealed class NestedSortingSuccessPanel : UIBase
        {
        }

        private sealed class SortingAfterNestedPanel : UIBase
        {
        }

        private sealed class UnownedPanel : UIBase
        {
        }
    }
}
