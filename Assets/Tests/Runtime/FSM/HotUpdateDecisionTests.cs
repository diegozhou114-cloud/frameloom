using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class HotUpdateDecisionTests
    {
        [Test]
        public void LaunchState_HotUpdateFailure_DoesNotContinue()
        {
            Assert.That(
                HotUpdateDecision.Evaluate(VersionCheckResult.NeedUpdate, false, false),
                Is.EqualTo(HotUpdateAction.Retry));
        }

        [Test]
        public void LaunchState_NeedRestart_DoesNotContinue()
        {
            Assert.That(
                HotUpdateDecision.Evaluate(VersionCheckResult.NeedUpdate, true, true),
                Is.EqualTo(HotUpdateAction.RestartRequired));
        }
    }
}
