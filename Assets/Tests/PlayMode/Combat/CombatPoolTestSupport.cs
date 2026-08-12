using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using ZWebGLFrameWork.SimulationCore;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Combat.PlayModeTests
{
    internal static class CombatPoolTestSupport
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.NonPublic;

        internal static ViewConfig LoadConfig()
        {
            ViewConfig config = Resources.Load<ViewConfig>("Config/ViewConfig");
            Assert.That(config, Is.Not.Null);
            return config;
        }

        internal static GameObject GameObjectOf(IEntityView view)
        {
            if (view is Component component) return component.gameObject;
            PropertyInfo property = view.GetType().GetProperty("GameObject");
            return property?.GetValue(view) as GameObject;
        }

        internal static UnityEntityViewFactory ProductionFactory(CombatManager manager)
        {
            object bridge = typeof(CombatManager).GetField(
                "_presentation", InstanceFlags).GetValue(manager);
            return (UnityEntityViewFactory)bridge.GetType().GetField(
                "_factory", InstanceFlags).GetValue(bridge);
        }

        internal static object Field(CombatManager manager, string name) =>
            typeof(CombatManager).GetField(name, InstanceFlags).GetValue(manager);

        internal static IEntityViewFactory PresentationFactory(CombatManager manager)
        {
            object bridge = Field(manager, "_presentation");
            return (IEntityViewFactory)bridge.GetType().GetField(
                "_factory", InstanceFlags).GetValue(bridge);
        }

        internal static void SetPresentation(CombatManager manager,
            SessionPresentationBridge bridge) => typeof(CombatManager).GetField(
                "_presentation", InstanceFlags).SetValue(manager, bridge);

        internal static HashSet<int> ActiveViewIds(CombatManager manager)
        {
            object bridge = typeof(CombatManager).GetField(
                "_presentation", InstanceFlags).GetValue(manager);
            var views = (System.Collections.IDictionary)bridge.GetType().GetField(
                "_views", InstanceFlags).GetValue(bridge);
            var ids = new HashSet<int>();
            foreach (object view in views.Values)
                ids.Add(GameObjectOf((IEntityView)view).GetInstanceID());
            return ids;
        }

        internal static Recorder ValidatedRecorder()
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            Assert.That(recorder, Is.Not.Null);
            Assert.That(recorder.isValid, Is.True);
            recorder.FilterToCurrentThread();
            recorder.enabled = false;
            recorder.enabled = true;
            AllocateCalibration();
            recorder.enabled = false;
            Assert.That(recorder.sampleBlockCount,
                Is.GreaterThanOrEqualTo(1));
            TestContext.WriteLine(
                $"GC.Alloc recorder calibration events={recorder.sampleBlockCount}");
            return recorder;
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void AllocateCalibration() => GC.KeepAlive(new byte[4096]);

        internal static void DisposeIfSupported(object target) =>
            (target as IDisposable)?.Dispose();

        internal static void CleanupScene()
        {
            foreach (CombatManager manager in Object.FindObjectsOfType<CombatManager>())
                if (manager != null) Object.DestroyImmediate(manager.gameObject);
            GameObject root = GameObject.Find("[CombatViewPool]");
            if (root != null) Object.DestroyImmediate(root);
            foreach (Transform item in Object.FindObjectsOfType<Transform>())
                if (item != null && item.name.StartsWith("SimEntity_",
                    StringComparison.Ordinal)) Object.DestroyImmediate(item.gameObject);
        }

        internal static CombatManager CreateManager(string name)
        {
            CleanupScene();
            return new GameObject(name).AddComponent<CombatManager>();
        }
    }

    internal sealed class DisposableEntityViewFactory : IEntityViewFactory, IDisposable
    {
        public int CreateCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public IEntityView Create(EntityPresentationKey key, int archetypeId)
        {
            CreateCalls++;
            return new View();
        }
        public void Destroy(IEntityView view) { }
        public void Dispose() => DisposeCalls++;

        private sealed class View : IEntityView
        {
            public void SetPosition(float x, float y) { }
        }
    }

    internal sealed class ThrowingDestroyEntityViewFactory : IEntityViewFactory
    {
        private readonly IEntityViewFactory _inner;
        private readonly Exception _failure;

        public int DestroyCalls { get; private set; }

        public ThrowingDestroyEntityViewFactory(IEntityViewFactory inner, Exception failure)
        {
            _inner = inner;
            _failure = failure;
        }

        public IEntityView Create(EntityPresentationKey key, int archetypeId) =>
            _inner.Create(key, archetypeId);

        public void Destroy(IEntityView view)
        {
            DestroyCalls++;
            throw _failure;
        }
    }
}
