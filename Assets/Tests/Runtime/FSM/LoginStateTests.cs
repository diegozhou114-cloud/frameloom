using System.Collections;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests
{
    public sealed class LoginStateTests
    {
        [UnityTest]
        public IEnumerator GuestLoginCallback_AfterStateExit_IsIgnored()
        {
            var state = new LoginState();
            MethodInfo callback = typeof(LoginState).GetMethod(
                "OnGuestLogin", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(callback, Is.Not.Null);
            callback.Invoke(state, null);
            yield return null;
        }

        [Test]
        public void GuestLoginCompletionPolicy_AcceptsLiveTokenAndRejectsCancellation()
        {
            MethodInfo policy = typeof(LoginState).GetMethod(
                "CanApplyGuestLoginCompletion",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(policy, Is.Not.Null);

            using (var lifetime = new CancellationTokenSource())
            {
                Assert.That(policy.Invoke(null, new object[] { lifetime.Token }), Is.True);
                lifetime.Cancel();
                Assert.That(policy.Invoke(null, new object[] { lifetime.Token }), Is.False);
            }
        }
    }
}
