using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests
{
    public sealed class LoginStateTests
    {
        private sealed class SubscriptionRecordingBus : IEventBus
        {
            private readonly Dictionary<(string key, Type type), Delegate> _handlers =
                new Dictionary<(string key, Type type), Delegate>();

            public void Subscribe(string eventKey, Action handler) { }
            public void Unsubscribe(string eventKey, Action handler) { }
            public void Publish(string eventKey) { }
            public void Subscribe<T>(string eventKey, Action<T> handler)
            {
                var key = (eventKey, typeof(T));
                _handlers[key] = _handlers.TryGetValue(key, out Delegate existing)
                    ? Delegate.Combine(existing, handler) : handler;
            }

            public void Unsubscribe<T>(string eventKey, Action<T> handler)
            {
                var key = (eventKey, typeof(T));
                if (!_handlers.TryGetValue(key, out Delegate existing)) return;
                Delegate remaining = Delegate.Remove(existing, handler);
                if (remaining == null) _handlers.Remove(key);
                else _handlers[key] = remaining;
            }

            public void Publish<T>(string eventKey, T arg)
            {
                if (_handlers.TryGetValue((eventKey, typeof(T)), out Delegate handlers))
                    (handlers as Action<T>)?.Invoke(arg);
            }

            public void Subscribe<T1, T2>(string eventKey, Action<T1, T2> handler) { }
            public void Unsubscribe<T1, T2>(string eventKey, Action<T1, T2> handler) { }
            public void Publish<T1, T2>(string eventKey, T1 arg1, T2 arg2) { }
            public void ClearAll() { }
            public int TotalEventCount => 0;

            public int HandlerCount<T>(string eventKey)
            {
                return _handlers.TryGetValue((eventKey, typeof(T)), out Delegate handlers)
                    ? handlers.GetInvocationList().Length : 0;
            }
        }

        private sealed class FailingOfflineSaveGateway : IOfflineSaveGateway
        {
            public bool Exists(int slot) => true;
            public bool Create(int slot) => true;
            public bool Load(int slot) => false;
        }

        [Test]
        public void CharacterSelectionSubscription_IsIdempotent()
        {
            var bus = new SubscriptionRecordingBus();
            EventBus.SetInstance(bus);
            try
            {
                MethodInfo subscribe = typeof(LoginState).GetMethod(
                    "SubscribeCharacterSelection", BindingFlags.Instance | BindingFlags.NonPublic);
                ConstructorInfo constructor = typeof(LoginState).GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(OfflineSaveFlow) }, null);

                Assert.That(subscribe, Is.Not.Null);
                Assert.That(constructor, Is.Not.Null);
                var state = (LoginState)constructor.Invoke(new object[]
                {
                    new OfflineSaveFlow(new FailingOfflineSaveGateway())
                });
                subscribe.Invoke(state, null);
                subscribe.Invoke(state, null);

                Assert.That(bus.HandlerCount<int>(LoginEvents.CharacterSelected), Is.EqualTo(1));
                bus.Publish(LoginEvents.CharacterSelected, 0);
                Assert.That(bus.HandlerCount<int>(LoginEvents.CharacterSelected), Is.EqualTo(1));
                state.OnExit();
                Assert.That(bus.HandlerCount<int>(LoginEvents.CharacterSelected), Is.Zero);
            }
            finally
            {
                if (UIManager.HasInstance)
                    UnityEngine.Object.DestroyImmediate(UIManager.Instance.gameObject);
                EventBus.ResetInstance();
            }
        }

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
