using System.Reflection;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class GameLauncherBackgroundThrottleTests
    {
        private static readonly MethodInfo FocusMethod = typeof(GameLauncher).GetMethod(
            "OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo StartMethod = typeof(GameLauncher).GetMethod(
            "Start", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo ConfigProperty = typeof(GameLauncher).GetProperty(
            "Config", BindingFlags.Static | BindingFlags.Public);
        private static readonly PropertyInfo ReadyProperty = typeof(GameLauncher).GetProperty(
            "IsReady", BindingFlags.Static | BindingFlags.Public);
        private static readonly FieldInfo InstanceField = typeof(SingletonDontDestroy<GameLauncher>).GetField(
            "_instance", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly object[] _unfocused = { false };
        private readonly object[] _focused = { true };

        private int _targetFrameRate;
        private GameObject _launcherObject;
        private AppConfig _config;

        [SetUp]
        public void SetUp()
        {
            _targetFrameRate = Application.targetFrameRate;
            Assert.That(FocusMethod, Is.Not.Null);
            Assert.That(StartMethod, Is.Not.Null);
            Assert.That(ConfigProperty, Is.Not.Null);
            Assert.That(ReadyProperty, Is.Not.Null);
            Assert.That(InstanceField, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = _targetFrameRate;
            SetConfig(null);
            InstanceField.SetValue(null, null);
            if (_launcherObject != null)
                Object.DestroyImmediate(_launcherObject);
            if (_config != null)
                Object.DestroyImmediate(_config);
        }

        [Test]
        public void OnApplicationFocus_WhenRuntimeOverrideDisablesThrottle_KeepsRuntimeFrameRate()
        {
            GameLauncher launcher = CreateLauncher(CreateConfig(autoDetect: true, sourceThrottle: true));
            Assert.That(_config.ThrottleInBackground, Is.True);

            Application.targetFrameRate = _config.RuntimeFrameRate;
            FocusMethod.Invoke(launcher, _unfocused);

            Assert.That(Application.targetFrameRate, Is.EqualTo(_config.RuntimeFrameRate));
        }

        [Test]
        public void OnApplicationFocus_WhenRuntimeThrottleEnabled_AppliesBackgroundThenRuntimeFrameRate()
        {
            GameLauncher launcher = CreateLauncher(CreateConfig(autoDetect: false, sourceThrottle: true));

            FocusMethod.Invoke(launcher, _unfocused);
            Assert.That(Application.targetFrameRate, Is.EqualTo(_config.BackgroundFrameRate));

            FocusMethod.Invoke(launcher, _focused);
            Assert.That(Application.targetFrameRate, Is.EqualTo(_config.RuntimeFrameRate));
        }

        [Test]
        public void OnDestroy_WhenCurrentLauncherIsDestroyed_ClearsStaticState()
        {
            GameLauncher launcher = CreateLauncher(CreateConfig(autoDetect: false, sourceThrottle: true));
            SetReady(true);

            Object.DestroyImmediate(launcher.gameObject);
            _launcherObject = null;

            Assert.That(InstanceField.GetValue(null), Is.Null);
            Assert.That(GameLauncher.Config, Is.Null);
            Assert.That(GameLauncher.IsReady, Is.False);
        }

        [Test]
        public void GameLauncher_UsesSharedDontDestroySingletonBase()
        {
            System.Type baseType = typeof(GameLauncher).BaseType;

            Assert.That(baseType, Is.Not.Null);
            Assert.That(baseType.IsGenericType, Is.True);
            Assert.That(baseType.GetGenericTypeDefinition(),
                Is.EqualTo(typeof(SingletonDontDestroy<>)));
            Assert.That(baseType.GetGenericArguments(), Is.EqualTo(new[] { typeof(GameLauncher) }));
        }

        [UnityTest]
        public IEnumerator DuplicateLauncher_DoesNotReplacePrimaryOrRunStartup()
        {
            GameLauncher primary = CreateLauncher(CreateConfig(autoDetect: false, sourceThrottle: true));
            SetReady(true);
            GameObject duplicateObject = new GameObject("DuplicateGameLauncher");
            GameLauncher duplicate = duplicateObject.AddComponent<GameLauncher>();

            StartMethod.Invoke(duplicate, null);

            Assert.That(Object.FindObjectsOfType<VillageRuntimeBootstrap>(), Is.Empty);
            Assert.That(InstanceField.GetValue(null), Is.SameAs(primary));
            Assert.That(GameLauncher.Config, Is.SameAs(_config));
            Assert.That(GameLauncher.IsReady, Is.True);

            primary.enabled = false;
            yield return null;

            Assert.That(duplicate == null, Is.True);
            Assert.That(InstanceField.GetValue(null), Is.SameAs(primary));
            Assert.That(GameLauncher.Config, Is.SameAs(_config));
        }

        private GameLauncher CreateLauncher(AppConfig config)
        {
            _config = config;
            _launcherObject = new GameObject("GameLauncherBackgroundThrottleTest");
            GameLauncher launcher = _launcherObject.AddComponent<GameLauncher>();
            SetConfig(_config);
            return launcher;
        }

        private static AppConfig CreateConfig(bool autoDetect, bool sourceThrottle)
        {
            AppConfig config = ScriptableObject.CreateInstance<AppConfig>();
            SetField(config, "_enablePlatformAutoDetect", autoDetect);
            SetField(config, "_targetFrameRate", 0);
            SetField(config, "_throttleInBackground", sourceThrottle);
            SetField(config, "_backgroundFrameRate", 11);
            config.Apply();
            return config;
        }

        private static void SetConfig(AppConfig config) =>
            ConfigProperty.GetSetMethod(nonPublic: true).Invoke(null, new object[] { config });

        private static void SetReady(bool ready) =>
            ReadyProperty.GetSetMethod(nonPublic: true).Invoke(null, new object[] { ready });

        private static void SetField(AppConfig config, string name, object value)
        {
            FieldInfo field = typeof(AppConfig).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing AppConfig field: {name}");
            field.SetValue(config, value);
        }
    }
}
