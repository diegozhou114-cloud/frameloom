using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.Gameplay;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Scenes
{
    public sealed class LauncherVillageCompositionTests : RuntimeTestBase
    {
        [UnityTest]
        public IEnumerator LauncherPlayMode_EntersInteractiveVillage()
        {
            Scene previous = SceneManager.GetActiveScene();
            int previousSlot = SaveManager.CurrentSlot;
            int testSlot = SaveManager.MaxSaveSlots - 1;
            Assert.That(SceneUtility.GetScenePathByBuildIndex(0),
                Is.EqualTo("Assets/Scence/Launcher.unity"));
            SaveManager.CurrentSlot = testSlot;
            SaveManager.Delete(testSlot);
            RegisterCleanup(() =>
            {
                SaveManager.Delete(testSlot);
                SaveManager.CurrentSlot = previousSlot;
            });

            AsyncOperation load = SceneManager.LoadSceneAsync(0, LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null);
            yield return load;
            Scene launcher = SceneManager.GetSceneByBuildIndex(0);
            Assert.That(launcher.IsValid() && launcher.isLoaded, Is.True);
            TrackLoadedScene(launcher);
            SceneManager.SetActiveScene(launcher);
            GameLauncher.EnsureCreatedForActiveScene();

            float deadline = Time.realtimeSinceStartup + 20f;
            while (GameFSM.Instance.CurrentState != GameState.MainVillage &&
                Time.realtimeSinceStartup < deadline)
            {
                if (GameFSM.Instance.CurrentState == GameState.Login)
                    EventBus.Publish(LoginEvents.OfflineLogin);
                yield return null;
            }
            yield return null;

            Assert.That(SceneManager.GetActiveScene().buildIndex, Is.Zero);
            Assert.That(Object.FindObjectOfType<GameLauncher>(), Is.Not.Null);
            Assert.That(SaveManager.Exists(testSlot), Is.True,
                "the real offline flow must create and load its persistent slot");
            Assert.That(GameFSM.Instance.CurrentState, Is.EqualTo(GameState.MainVillage));
            VillageRuntimeBootstrap bootstrap = FindInScene<VillageRuntimeBootstrap>(launcher);
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.gameObject.scene, Is.EqualTo(launcher));
            Camera camera = FindInScene<Camera>(launcher);
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.GetComponent<CameraController>(), Is.Not.Null);
            Assert.That(FindInScene<GridRenderer>(launcher), Is.Not.Null);
            Assert.That(FindInScene<BuildingPlacer>(launcher), Is.Not.Null);
            Assert.That(FindInScene<BuildingInteraction>(launcher), Is.Not.Null);
            Assert.That(FindInScene<VillageViewRenderer>(launcher), Is.Not.Null);
            BuildingView view = FindInScene<BuildingView>(launcher);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.GetComponent<Collider>(), Is.Not.Null);
            string buildingId = view.BuildingId;

            CameraController controller = camera.GetComponent<CameraController>();
            controller.enabled = false;
            camera.transform.position = new Vector3(0.5f, 8f, -8f);
            camera.transform.LookAt(view.transform.position);
            Physics.SyncTransforms();
            Vector3 screenPosition = camera.WorldToScreenPoint(view.transform.position);
            EventBus.Publish(InputManager.EventGesture, new GestureData
            {
                Type = GestureType.Tap,
                ScreenPosition = screenPosition,
            });

            Assert.That(SelectionManager.Instance.SelectedBuildingId, Is.EqualTo(buildingId));

            SceneManager.SetActiveScene(previous);
            yield return SceneManager.UnloadSceneAsync(launcher);
            SaveManager.Delete(testSlot);
            SaveManager.CurrentSlot = previousSlot;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null) return component;
            }
            return null;
        }
    }
}
