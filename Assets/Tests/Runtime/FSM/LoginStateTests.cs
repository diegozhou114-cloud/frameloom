using System.Collections;
using System.Reflection;
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
    }
}
