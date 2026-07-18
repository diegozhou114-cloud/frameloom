using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Runtime.Tests.Support
{
    public abstract class RuntimeTestBase
    {
        private readonly List<GameObject> _ownedObjects = new List<GameObject>();
        private readonly List<string> _tempDirectories = new List<string>();
        private readonly List<string> _loadedSceneNames = new List<string>();
        private readonly List<Action> _cleanupActions = new List<Action>();

        protected GameObject CreateOwnedObject(string name)
        {
            var instance = new GameObject($"RuntimeTest_{name}");
            _ownedObjects.Add(instance);
            return instance;
        }

        protected string CreateTempDirectory(string name)
        {
            string path = Path.Combine(
                Application.temporaryCachePath,
                $"zweb-runtime-{name}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            _tempDirectories.Add(path);
            return path;
        }

        protected void TrackLoadedScene(Scene scene)
        {
            if (scene.IsValid()) _loadedSceneNames.Add(scene.name);
        }

        protected void RegisterCleanup(Action cleanup)
        {
            _cleanupActions.Add(cleanup ?? throw new ArgumentNullException(nameof(cleanup)));
        }

        [SetUp]
        public void RuntimeSetUp()
        {
            EventBus.ResetInstance();
            EventBus.ClearAll();
            Time.timeScale = 1f;
        }

        [UnityTearDown]
        public IEnumerator RuntimeTearDown()
        {
            EventBus.ClearAll();
            Time.timeScale = 1f;

            foreach (GameObject instance in _ownedObjects)
            {
                if (instance != null) UnityEngine.Object.Destroy(instance);
            }
            _ownedObjects.Clear();

            foreach (EventSystem eventSystem in
                UnityEngine.Object.FindObjectsOfType<EventSystem>(true))
            {
                if (eventSystem != null) UnityEngine.Object.Destroy(eventSystem.gameObject);
            }
            yield return null;

            foreach (string sceneName in _loadedSceneNames)
            {
                Scene scene = SceneManager.GetSceneByName(sceneName);
                if (scene.IsValid() && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
            _loadedSceneNames.Clear();

            for (int index = _cleanupActions.Count - 1; index >= 0; index--)
            {
                try
                {
                    _cleanupActions[index]();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
            _cleanupActions.Clear();

            foreach (string path in _tempDirectories)
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            _tempDirectories.Clear();
        }
    }
}
