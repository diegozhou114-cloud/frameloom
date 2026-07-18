using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class BuildingInfoPanelLifecycleTests : RuntimeTestBase
    {
        [UnityTest]
        public IEnumerator BuildingInfoPanel_Awake_InitializesUIBase()
        {
            var panelObject = CreateOwnedObject("BuildingInfoPanel");
            var panel = panelObject.AddComponent<BuildingInfoPanel>();
            yield return null;

            Assert.That(panel.Canvas, Is.Not.Null);
            Assert.That(panel.CanvasGroup, Is.Not.Null);
            Assert.That(panel.gameObject.activeSelf, Is.False);
        }
    }
}
