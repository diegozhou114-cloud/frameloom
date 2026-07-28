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
            cache[ResourceArrayCacheKey<BuildingConfig>("Config/Buildings")] =
                new BuildingConfig[] { null };
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
            cache[ResourceArrayCacheKey<BuildingConfig>("Config/Buildings")] = new[]
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

        [Test]
        public void PreloadAllAsync_NonPositiveId_ReturnsFalseWithoutPublishingTheRow()
        {
            var cache = (IDictionary)CacheField.GetValue(null);
            cache[ResourceArrayCacheKey<BuildingConfig>("Config/Buildings")] = new[]
            {
                new BuildingConfig { Id = 0 },
            };
            ConfigTableManager manager = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            bool result = true;

            LogAssert.Expect(LogType.Error,
                "[ConfigTableManager] BuildingConfig Id=0 must be positive");
            LogAssert.Expect(LogType.Error,
                "[ConfigTableManager] ❌ 建筑配置表加载失败或为空");
            Assert.DoesNotThrow(() => result = manager.PreloadAllAsync().GetAwaiter().GetResult());

            Assert.That(result, Is.False);
            Assert.That(manager.GetBuilding(0), Is.Null);
        }

        [Test]
        public void PreloadAllAsync_UnknownBuildingResourceType_ReturnsFalseWithoutPublishingTheRow()
        {
            var cache = (IDictionary)CacheField.GetValue(null);
            cache[ResourceArrayCacheKey<BuildingConfig>("Config/Buildings")] = new[]
            {
                new BuildingConfig
                {
                    Id = 1,
                    Category = "Resource",
                    ResourceType = (ResourceType)999,
                },
            };
            ConfigTableManager manager = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            bool result = true;

            LogAssert.Expect(LogType.Error,
                "[ConfigTableManager] BuildingConfig Id=1 has an invalid ResourceType");
            LogAssert.Expect(LogType.Error,
                "[ConfigTableManager] ❌ 建筑配置表加载失败或为空");
            Assert.DoesNotThrow(() => result = manager.PreloadAllAsync().GetAwaiter().GetResult());

            Assert.That(result, Is.False);
            Assert.That(manager.GetBuilding(1), Is.Null);
        }

        [Test]
        public void PreloadAllAsync_NonPositiveBuildingFootprint_ReturnsFalseWithoutPublishingTheRow()
        {
            var cache = (IDictionary)CacheField.GetValue(null);
            cache[ResourceArrayCacheKey<BuildingConfig>("Config/Buildings")] = new[]
            {
                new BuildingConfig { Id = 1, SizeX = 0, SizeY = 1 },
            };
            ConfigTableManager manager = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            bool result = true;

            LogAssert.Expect(LogType.Error,
                "[ConfigTableManager] BuildingConfig Id=1 has an invalid footprint");
            LogAssert.Expect(LogType.Error,
                "[ConfigTableManager] ❌ 建筑配置表加载失败或为空");
            Assert.DoesNotThrow(() => result = manager.PreloadAllAsync().GetAwaiter().GetResult());

            Assert.That(result, Is.False);
            Assert.That(manager.GetBuilding(1), Is.Null);
        }

        [Test]
        public void ConfigLoader_SameResourcePathWithDifferentTypes_DoesNotReuseIncompatibleCache()
        {
            BuildingConfig[] buildings = ConfigLoader.LoadJsonArrayFromResources<BuildingConfig>(
                "Config/Buildings");
            UnitConfig[] units = ConfigLoader.LoadJsonArrayFromResources<UnitConfig>(
                "Config/Buildings");

            Assert.That(buildings, Is.Not.Empty);
            Assert.That(units, Is.Not.Empty);
            Assert.That(units[0].Id, Is.EqualTo(buildings[0].Id));
        }

        [Test]
        public void ConfigLoader_RemoveCache_RemovesEveryTypeCachedAtThePath()
        {
            ConfigLoader.LoadJsonArrayFromResources<BuildingConfig>("Config/Buildings");
            ConfigLoader.LoadJsonArrayFromResources<UnitConfig>("Config/Buildings");
            Assert.That(ConfigLoader.CacheCount, Is.EqualTo(2));

            ConfigLoader.RemoveCache("Config/Buildings");

            Assert.That(ConfigLoader.CacheCount, Is.EqualTo(0));
        }

        private static string ResourceArrayCacheKey<T>(string path) =>
            $"res_arr:{typeof(T).AssemblyQualifiedName}:{path}";
    }
}
