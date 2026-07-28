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
    }
}
