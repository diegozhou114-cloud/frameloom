using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class UIEventSystemTests : RuntimeTestBase
    {
        private UIManager CreateFreshUIManager()
        {
            if (UIManager.HasInstance)
                Object.DestroyImmediate(UIManager.Instance.gameObject);

            return CreateOwnedObject("UIManager").AddComponent<UIManager>();
        }

        private sealed class RoutedPointerTestModule : BaseInputModule
        {
            private Vector2 _position;
            private bool _pendingClick;

            public void QueueClick(Vector2 position)
            {
                _position = position;
                _pendingClick = true;
            }

            public override bool ShouldActivateModule() => true;

            public override void Process()
            {
                if (!_pendingClick) return;
                _pendingClick = false;
                var pointer = new PointerEventData(eventSystem)
                {
                    button = PointerEventData.InputButton.Left,
                    position = _position,
                };
                var hits = new List<RaycastResult>();
                foreach (BaseRaycaster raycaster in RaycasterManager.GetRaycasters())
                {
                    if (raycaster != null && raycaster.isActiveAndEnabled)
                        raycaster.Raycast(pointer, hits);
                }
                Assert.That(hits, Is.Not.Empty,
                    "the real Launcher button must be raycastable");
                GameObject target = null;
                foreach (RaycastResult hit in hits)
                {
                    GameObject candidate = ExecuteEvents.GetEventHandler<IPointerClickHandler>(
                        hit.gameObject);
                    if (candidate != null && candidate.name == "BtnOffline")
                    {
                        target = candidate;
                        break;
                    }
                }
                Assert.That(target, Is.Not.Null,
                    "the offline button must be the click target at its screen position");
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
            }
        }

        [UnityTest]
        public IEnumerator UIManager_Initialize_CreatesSingleEventSystem()
        {
            CreateFreshUIManager();
            yield return null;

            EventSystem[] systems = Object.FindObjectsOfType<EventSystem>(true);
            Assert.That(systems, Has.Length.EqualTo(1));
            Assert.That(systems[0].GetComponent<BaseInputModule>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator UIManager_WithExistingEventSystem_DoesNotDuplicate()
        {
            var existing = CreateOwnedObject("ExistingEventSystem");
            var expected = existing.AddComponent<EventSystem>();
            existing.AddComponent<StandaloneInputModule>();

            CreateFreshUIManager();
            yield return null;

            EventSystem[] systems = Object.FindObjectsOfType<EventSystem>(true);
            Assert.That(systems, Has.Length.EqualTo(1));
            Assert.That(systems[0], Is.SameAs(expected));
        }

        [UnityTest]
        public IEnumerator LauncherButton_PointerClick_InvokesAction()
        {
            var manager = CreateFreshUIManager();
            yield return null;
            manager.Open<LoginPanel>();
            yield return null;

            LoginPanel launcherPanel = Object.FindObjectOfType<LoginPanel>(true);
            Assert.That(launcherPanel, Is.Not.Null);
            RectTransform offlineButton = launcherPanel.transform.Find("BtnOffline")
                as RectTransform;
            Assert.That(offlineButton, Is.Not.Null);
            Canvas.ForceUpdateCanvases();
            int routedActions = 0;
            EventBus.Subscribe(LoginEvents.OfflineLogin, () => routedActions++);
            foreach (BaseInputModule existing in
                EventSystem.current.GetComponents<BaseInputModule>())
                existing.enabled = false;
            var module = EventSystem.current.gameObject.AddComponent<RoutedPointerTestModule>();
            module.QueueClick(RectTransformUtility.WorldToScreenPoint(
                null, offlineButton.TransformPoint(offlineButton.rect.center)));
            module.Process();

            Assert.That(routedActions, Is.EqualTo(1));
        }
    }
}
