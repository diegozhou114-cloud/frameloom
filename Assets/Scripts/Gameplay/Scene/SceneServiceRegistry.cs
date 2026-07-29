using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Gameplay
{
    public readonly struct SceneServices
    {
        public static SceneServices Empty => default;

        public Scene Scene { get; }
        public Camera MainCamera { get; }
        public CameraController CameraController { get; }
        public GridManager Grid { get; }
        public GridRenderer GridRenderer { get; }
        public BuildingPlacer Placer { get; }
        public BuildingInteraction Interaction { get; }

        public SceneServices(
            Scene scene,
            Camera mainCamera,
            CameraController cameraController,
            GridManager grid,
            GridRenderer gridRenderer,
            BuildingPlacer placer,
            BuildingInteraction interaction)
        {
            Scene = scene;
            MainCamera = mainCamera;
            CameraController = cameraController;
            Grid = grid;
            GridRenderer = gridRenderer;
            Placer = placer;
            Interaction = interaction;
        }
    }

    public sealed class SceneServiceRegistry : SingletonDontDestroy<SceneServiceRegistry>
    {
        public SceneServices Current { get; private set; }
        public event Action<SceneServices> Changed;

        protected override void Awake()
        {
            base.Awake();
            if (!IsPrimaryInstance) return;

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded)
                Rebind(active);
        }

        public void Rebind(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("Scene must be valid and loaded", nameof(scene));

            if (Current.Scene.IsValid() && Current.Scene != scene)
            {
                Current = SceneServices.Empty;
                Changed?.Invoke(Current);
            }

            Camera mainCamera = FindMainCamera(scene);
            Current = new SceneServices(
                scene,
                mainCamera,
                mainCamera != null ? mainCamera.GetComponent<CameraController>() : null,
                GridManager.HasInstance ? GridManager.Instance : null,
                FindInScene<GridRenderer>(scene),
                FindInScene<BuildingPlacer>(scene),
                FindInScene<BuildingInteraction>(scene));
            Changed?.Invoke(Current);
        }

        public void Release(Scene scene)
        {
            if (Current.Scene != scene) return;
            Current = SceneServices.Empty;
            Changed?.Invoke(Current);
        }

        protected override void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Changed = null;
            base.OnDestroy();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Rebind(scene);
        private void OnSceneUnloaded(Scene scene) => Release(scene);

        private static Camera FindMainCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.CompareTag("MainCamera"))
                        return camera;
                }
            }
            return null;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }
    }
}
