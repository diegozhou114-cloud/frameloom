using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZWebGLFrameWork.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class VillageRuntimeBootstrap : MonoBehaviour
    {
        public static VillageRuntimeBootstrap EnsureCreated()
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                VillageRuntimeBootstrap existing =
                    root.GetComponentInChildren<VillageRuntimeBootstrap>(true);
                if (existing != null) return existing;
            }

            var rootObject = new GameObject("VillageRuntime");
            SceneManager.MoveGameObjectToScene(rootObject, scene);
            return rootObject.AddComponent<VillageRuntimeBootstrap>();
        }

        private void Awake()
        {
            Camera mainCamera = FindMainCamera(gameObject.scene);
            if (mainCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                SceneManager.MoveGameObjectToScene(cameraObject, gameObject.scene);
                mainCamera = cameraObject.AddComponent<Camera>();
            }
            EnsureComponent<CameraController>(mainCamera.gameObject);

            _ = GridManager.Instance;
            _ = InputManager.Instance;
            _ = SelectionManager.Instance;
            EnsureComponent<GridRenderer>(gameObject);
            EnsureComponent<BuildingPlacer>(gameObject);
            EnsureComponent<BuildingInteraction>(gameObject);
            EnsureComponent<VillageViewRenderer>(gameObject);
        }

        private void Start()
        {
            SceneServiceRegistry.Instance.Rebind(gameObject.scene);
        }

        private static T EnsureComponent<T>(GameObject owner) where T : Component
        {
            T component = owner.GetComponent<T>();
            return component != null ? component : owner.AddComponent<T>();
        }

        private static Camera FindMainCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.CompareTag("MainCamera")) return camera;
                }
            }
            return null;
        }
    }
}
