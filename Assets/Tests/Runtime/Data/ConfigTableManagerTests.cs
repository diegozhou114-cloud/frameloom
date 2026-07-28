using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class ConfigTableManagerTests : RuntimeTestBase
    {
        private static readonly FieldInfo CacheField = typeof(ConfigLoader).GetField(
            "_cache", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp() => ConfigLoader.ClearCache();

        [TearDown]
        public void TearDown() => ConfigLoader.ClearCache();

        [Test]
        public void PreloadAllAsync_NullRow_ReturnsFalseWithoutThrowing()
        {
            var cache = (IDictionary)CacheField.GetValue(null);
            cache["res_arr:Config/Buildings"] = new BuildingConfig[] { null };
            ConfigTableManager manager = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            bool result = true;

            LogAssert.Expect(LogType.Error,
                "[ConfigTableManager] BuildingConfig contains a null configuration item");
            LogAssert.Expect(LogType.Error,
                "[ConfigTableManager] ❌ 建筑配置表加载失败或为空");
            Assert.DoesNotThrow(() => result = manager.PreloadAllAsync().GetAwaiter().GetResult());

            Assert.That(result, Is.False);
            Assert.That(manager.GetBuilding(1), Is.Null);
        }

        [Test]
        public void PreloadAllAsync_DuplicateId_ReturnsFalseWithoutDiscardingAConfigRow()
        {
            var cache = (IDictionary)CacheField.GetValue(null);
            cache["res_arr:Config/Buildings"] = new[]
            {
                new BuildingConfig { Id = 1 },
                new BuildingConfig { Id = 1 },
            };
            ConfigTableManager manager = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            bool result = true;

            LogAssert.Expect(LogType.Error,
                "[ConfigTableManager] BuildingConfig Id=1 is duplicated");
            LogAssert.Expect(LogType.Error,
                "[ConfigTableManager] ❌ 建筑配置表加载失败或为空");
            Assert.DoesNotThrow(() => result = manager.PreloadAllAsync().GetAwaiter().GetResult());

            Assert.That(result, Is.False);
            Assert.That(manager.GetBuilding(1), Is.Null);
        }
    }
}
