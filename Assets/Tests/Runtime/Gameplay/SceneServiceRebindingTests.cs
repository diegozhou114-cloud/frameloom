using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class SceneServiceRebindingTests : RuntimeTestBase
    {
        private sealed class ServiceSet
        {
            public Camera Camera;
            public CameraController CameraController;
            public GridRenderer GridRenderer;
            public BuildingPlacer Placer;
            public BuildingInteraction Interaction;
        }

        [UnityTest]
        public IEnumerator SceneChange_AllGameplayServices_RebindAndReleaseOldScene()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            CreateOwnedObject("GridManager").AddComponent<GridManager>();
            var registry = CreateOwnedObject("SceneServiceRegistry")
                .AddComponent<SceneServiceRegistry>();
            var input = CreateOwnedObject("InputManager").AddComponent<InputManager>();
            var selection = CreateOwnedObject("SelectionManager").AddComponent<SelectionManager>();
            var panel = CreateOwnedObject("BuildingInfoPanel").AddComponent<BuildingInfoPanel>();

            Scene firstScene = SceneManager.CreateScene("SceneBindings_First");
            Scene secondScene = SceneManager.CreateScene("SceneBindings_Second");
            ServiceSet first = CreateServices(firstScene, "First");
            ServiceSet second = CreateServices(secondScene, "Second");
            yield return null;

            registry.Rebind(firstScene);
            Assert.That(GetField<Camera>(input, "_mainCamera"), Is.SameAs(first.Camera));
            Assert.That(GetField<Camera>(selection, "_mainCamera"), Is.SameAs(first.Camera));
            Assert.That(GetField<Camera>(first.Placer, "_mainCamera"), Is.SameAs(first.Camera));
            Assert.That(GetField<BuildingPlacer>(first.Interaction, "_placer"), Is.SameAs(first.Placer));
            Assert.That(GetField<BuildingInteraction>(panel, "_interaction"), Is.SameAs(first.Interaction));

            registry.Rebind(secondScene);
            Assert.That(GetField<Camera>(input, "_mainCamera"), Is.SameAs(second.Camera));
            Assert.That(GetField<Camera>(selection, "_mainCamera"), Is.SameAs(second.Camera));
            Assert.That(GetField<Camera>(second.Placer, "_mainCamera"), Is.SameAs(second.Camera));
            Assert.That(GetField<BuildingPlacer>(second.Interaction, "_placer"), Is.SameAs(second.Placer));
            Assert.That(GetField<BuildingInteraction>(panel, "_interaction"), Is.SameAs(second.Interaction));
            Assert.That(GetField<Camera>(first.Placer, "_mainCamera"), Is.Null);
            Assert.That(GetField<BuildingPlacer>(first.Interaction, "_placer"), Is.Null);

            registry.Release(secondScene);
            Assert.That(GetField<Camera>(input, "_mainCamera"), Is.Null);
            Assert.That(GetField<Camera>(selection, "_mainCamera"), Is.Null);
            Assert.That(GetField<BuildingInteraction>(panel, "_interaction"), Is.Null);

            yield return SceneManager.UnloadSceneAsync(firstScene);
            yield return SceneManager.UnloadSceneAsync(secondScene);
        }

        private static ServiceSet CreateServices(Scene scene, string suffix)
        {
            var root = new GameObject($"Services_{suffix}");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.tag = "MainCamera";
            var camera = root.AddComponent<Camera>();
            var cameraController = root.AddComponent<CameraController>();
            var gridRenderer = root.AddComponent<GridRenderer>();
            var placer = root.AddComponent<BuildingPlacer>();
            var interaction = root.AddComponent<BuildingInteraction>();
            return new ServiceSet
            {
                Camera = camera,
                CameraController = cameraController,
                GridRenderer = gridRenderer,
                Placer = placer,
                Interaction = interaction,
            };
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            return field.GetValue(target) as T;
        }
    }
}
