using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class LocalizationManagerTests : RuntimeTestBase
    {
        [Test]
        public void SwitchLanguage_MissingLanguage_PublishesLoadedFallbackLanguage()
        {
            LocalizationManager manager = CreateOwnedObject("LocalizationManager")
                .AddComponent<LocalizationManager>();
            string observedLanguage = null;
            manager.OnLanguageChanged += language => observedLanguage = language;

            manager.SwitchLanguage("missing-test-language");

            Assert.That(manager.CurrentLanguage, Is.EqualTo("en-US"));
            Assert.That(observedLanguage, Is.EqualTo(manager.CurrentLanguage));
        }

        [Test]
        public void Init_DiscoveredLanguage_RegistersItAsSupported()
        {
            LocalizationManager manager = CreateOwnedObject("LocalizationManager")
                .AddComponent<LocalizationManager>();

            Assert.That(manager.Init("fr-CA"), Is.True);

            Assert.That(manager.SupportedLanguages, Does.Contain("fr-CA"));
        }
    }
}
