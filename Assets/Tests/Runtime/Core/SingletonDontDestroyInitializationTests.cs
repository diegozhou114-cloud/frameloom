using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class SingletonDontDestroyInitializationTests : RuntimeTestBase
    {
        private sealed class InitializationProbe :
            SingletonDontDestroy<InitializationProbe>
        {
            public static int InitializationCount { get; private set; }
            public bool WasInitialized { get; private set; }

            public static void ResetForTests()
            {
                InitializationCount = 0;
            }

            protected override void Awake()
            {
                base.Awake();
                if (!IsPrimaryInstance) return;
                WasInitialized = true;
                InitializationCount++;
            }
        }

        [UnityTest]
        public IEnumerator DuplicateSingleton_OnlyPrimaryInitializes()
        {
            if (InitializationProbe.HasInstance)
            {
                UnityEngine.Object.DestroyImmediate(
                    InitializationProbe.Instance.gameObject);
            }
            InitializationProbe.ResetForTests();

            InitializationProbe primary = CreateOwnedObject("PrimaryProbe")
                .AddComponent<InitializationProbe>();
            InitializationProbe duplicate = CreateOwnedObject("DuplicateProbe")
                .AddComponent<InitializationProbe>();

            Assert.That(InitializationProbe.Instance, Is.SameAs(primary));
            Assert.That(primary.WasInitialized, Is.True);
            Assert.That(duplicate.WasInitialized, Is.False);
            Assert.That(InitializationProbe.InitializationCount, Is.EqualTo(1));

            yield return null;

            Assert.That(duplicate == null, Is.True);
            Assert.That(InitializationProbe.Instance, Is.SameAs(primary));
        }

        [Test]
        public void DuplicateDataManager_DoesNotInitializeOrPublishDataReady()
        {
            if (DataManager.HasInstance)
                UnityEngine.Object.DestroyImmediate(DataManager.Instance.gameObject);

            int publishCount = 0;
            EventBus.Subscribe(DataManager.EventDataReady, () => publishCount++);

            DataManager primary =
                CreateOwnedObject("PrimaryDataManager").AddComponent<DataManager>();
            DataManager duplicate =
                CreateOwnedObject("DuplicateDataManager").AddComponent<DataManager>();

            Assert.That(DataManager.Instance, Is.SameAs(primary));
            Assert.That(publishCount, Is.EqualTo(1));
            Assert.That(duplicate.IsReady, Is.False);
        }

        [Test]
        public void DuplicateUIManager_DoesNotCreateUIRuntimeObjects()
        {
            if (UIManager.HasInstance)
                UnityEngine.Object.DestroyImmediate(UIManager.Instance.gameObject);

            UIManager primary =
                CreateOwnedObject("PrimaryUIManager").AddComponent<UIManager>();
            UIManager duplicate =
                CreateOwnedObject("DuplicateUIManager").AddComponent<UIManager>();

            Assert.That(UIManager.Instance, Is.SameAs(primary));
            Assert.That(primary.GetComponent<BuildingInfoController>(), Is.Not.Null);
            Assert.That(primary.GetComponentsInChildren<Canvas>(true),
                Has.Length.EqualTo(Enum.GetValues(typeof(UILayer)).Length));
            Assert.That(duplicate.GetComponent<BuildingInfoController>(), Is.Null);
            Assert.That(duplicate.transform.childCount, Is.Zero);
        }

        [Test]
        public void DuplicateGameFSM_PauseCallbackBeforeDestroy_DoesNotThrow()
        {
            if (GameFSM.HasInstance)
                UnityEngine.Object.DestroyImmediate(GameFSM.Instance.gameObject);

            GameFSM primary =
                CreateOwnedObject("PrimaryGameFSM").AddComponent<GameFSM>();
            GameFSM duplicate =
                CreateOwnedObject("DuplicateGameFSM").AddComponent<GameFSM>();
            MethodInfo pauseCallback = typeof(GameFSM).GetMethod(
                "OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(GameFSM.Instance, Is.SameAs(primary));
            Assert.That(pauseCallback, Is.Not.Null);
            Assert.DoesNotThrow(() => pauseCallback.Invoke(duplicate, new object[] { true }));
        }

        [Test]
        public void SceneLoader_DestroyedBeforeInit_ReleasesSingletonReference()
        {
            if (SceneLoader.HasInstance)
                UnityEngine.Object.DestroyImmediate(SceneLoader.Instance.gameObject);

            SceneLoader loader = CreateOwnedObject("SceneLoader").AddComponent<SceneLoader>();
            FieldInfo instanceField = typeof(SingletonDontDestroy<SceneLoader>).GetField(
                "_instance", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(instanceField, Is.Not.Null);
            Assert.That(instanceField.GetValue(null), Is.SameAs(loader));

            UnityEngine.Object.DestroyImmediate(loader.gameObject);

            Assert.That(instanceField.GetValue(null), Is.Null);
        }
    }
}
