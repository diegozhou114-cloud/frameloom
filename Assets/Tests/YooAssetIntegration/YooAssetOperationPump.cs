using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using global::YooAsset;
using NUnit.Framework;
using UnityEditor;

namespace ZWebGLFrameWork.YooAsset.IntegrationTests
{
    /// <summary>在 EditMode 推进真实 OperationSystem，不构造或篡改操作完成状态。</summary>
    internal sealed class YooAssetOperationPump : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly Type _system = typeof(ResourcePackage).Assembly.GetType("YooAsset.OperationSystem", true);
        private readonly HashSet<AsyncOperationBase> _owned = new();
        private readonly string _packageName;
        private readonly object _watch;
        private readonly object _frameTime;

        public YooAssetOperationPump(string packageName)
        {
            Assert.That(YooAssets.Initialized, Is.False, "Run without an active YooAssets runtime.");
            Assert.That(GetQueue("_operations").Count, Is.Zero, "Other YooAsset operations are active.");
            Assert.That(GetQueue("_newList").Count, Is.Zero, "Other YooAsset operations are queued.");
            _packageName = packageName;
            _watch = _system.GetField("_watch", Flags).GetValue(null);
            _frameTime = _system.GetField("_frameTime", Flags).GetValue(null);
            if (_watch == null) _system.GetMethod("Initialize", Flags).Invoke(null, null);
        }

        public ResourcePackage CreatePackage() => (ResourcePackage)Activator.CreateInstance(
            typeof(ResourcePackage), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { _packageName }, null);

        public async UniTask Wait(AsyncOperationBase operation)
        {
            _owned.Add(operation);
            double deadline = EditorApplication.timeSinceStartup + 60;
            while (!operation.IsDone)
            {
                Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline),
                    "Timed out waiting for " + operation.GetType().Name);
                Tick();
                await UniTask.Yield();
            }
            Assert.That(operation.Status, Is.EqualTo(EOperationStatus.Succeed), operation.Error);
        }

        public async UniTask<T> Complete<T>(UniTask<T> task)
        {
            double deadline = EditorApplication.timeSinceStartup + 60;
            while (task.Status == UniTaskStatus.Pending)
            {
                Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline),
                    "Timed out waiting for real YooAsset loading.");
                Tick();
                await UniTask.Yield();
            }
            return await task;
        }

        private void Tick() => _system.GetMethod("Update", Flags).Invoke(null, null);
        private IList GetQueue(string field) => (IList)_system.GetField(field, Flags).GetValue(null);

        public void AbortPackageOperations()
        {
            _system.GetMethod("ClearPackageOperation", Flags).Invoke(null, new object[] { _packageName });
            Tick();
        }

        public void Dispose()
        {
            foreach (string field in new[] { "_operations", "_newList" })
            {
                var queue = GetQueue(field);
                for (int index = queue.Count - 1; index >= 0; index--)
                {
                    var operation = (AsyncOperationBase)queue[index];
                    if (operation.PackageName == _packageName || _owned.Contains(operation))
                        queue.RemoveAt(index);
                }
            }
            _system.GetField("_watch", Flags).SetValue(null, _watch);
            _system.GetField("_frameTime", Flags).SetValue(null, _frameTime);
        }
    }
}
