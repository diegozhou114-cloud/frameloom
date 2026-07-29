using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class AppConfigTests
    {
        private static readonly FieldInfo EnableOfflineModeField = GetField("_enableOfflineMode");
        private static readonly FieldInfo HttpsApiBaseUrlField = GetField("_httpsApiBaseUrl");
        private static readonly FieldInfo WssRealtimeUrlField = GetField("_wssRealtimeUrl");
        private int _targetFrameRate;
        private int _vSyncCount;
        private bool _runInBackground;
        private int _qualityLevel;
        private LogLevel _minLogLevel;

        [SetUp]
        public void SetUp()
        {
            _targetFrameRate = Application.targetFrameRate;
            _vSyncCount = QualitySettings.vSyncCount;
            _runInBackground = Application.runInBackground;
            _qualityLevel = QualitySettings.GetQualityLevel();
            _minLogLevel = LogManager.MinLevel;
        }

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = _targetFrameRate;
            QualitySettings.vSyncCount = _vSyncCount;
            Application.runInBackground = _runInBackground;
            if (_qualityLevel >= 0 && _qualityLevel < QualitySettings.names.Length)
                QualitySettings.SetQualityLevel(_qualityLevel, applyExpensiveChanges: false);
            LogManager.MinLevel = _minLogLevel;
        }

        [Test]
        public void Apply_OfflineModeWithEmptyOnlineEndpoints_DoesNotThrow()
        {
            AppConfig config = ScriptableObject.CreateInstance<AppConfig>();
            try
            {
                Set(EnableOfflineModeField, config, true);
                Set(HttpsApiBaseUrlField, config, string.Empty);
                Set(WssRealtimeUrlField, config, string.Empty);

                Assert.DoesNotThrow(() => config.Apply());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Apply_OnlineModeWithEmptyOnlineEndpoints_Throws()
        {
            AppConfig config = ScriptableObject.CreateInstance<AppConfig>();
            try
            {
                Set(EnableOfflineModeField, config, false);
                Set(HttpsApiBaseUrlField, config, string.Empty);
                Set(WssRealtimeUrlField, config, string.Empty);

                Assert.Throws<InvalidOperationException>(() => config.Apply());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Apply_OnlineModeWithInvalidWssEndpoint_Throws()
        {
            AppConfig config = ScriptableObject.CreateInstance<AppConfig>();
            try
            {
                Set(EnableOfflineModeField, config, false);
                Set(HttpsApiBaseUrlField, config, "https://game.example");
                Set(WssRealtimeUrlField, config, string.Empty);

                Assert.Throws<InvalidOperationException>(() => config.Apply());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        private static FieldInfo GetField(string name)
        {
            FieldInfo field = typeof(AppConfig).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing AppConfig field: {name}");
            return field;
        }

        private static void Set(FieldInfo field, AppConfig config, object value) =>
            field.SetValue(config, value);
    }
}
