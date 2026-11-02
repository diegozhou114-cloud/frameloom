using System;
using NUnit.Framework;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Core.Tests
{
    public sealed class SubscriptionScopeTests
    {
        [SetUp]
        public void SetUp()
        {
            EventBus.ResetInstance();
            EventBus.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ResetInstance();
            EventBus.ClearAll();
        }

        [Test]
        public void Dispose_RemovesZeroOneTwoParameterAndTypedKeySubscriptions()
        {
            var scope = new SubscriptionScope();
            int calls = 0;
            var typedKey = new EventKey<int>("typed");
            scope.Subscribe("zero", () => calls++);
            scope.Subscribe<int>("one", value => calls += value);
            scope.Subscribe<int, int>("two", (first, second) => calls += first + second);
            scope.Subscribe(typedKey, value => calls += value);
            PublishAll();
            Assert.That(calls, Is.EqualTo(10));

            scope.Dispose();
            PublishAll();

            Assert.That(calls, Is.EqualTo(10));
            Assert.That(EventBus.TotalEventCount, Is.Zero);
        }

        [Test]
        public void Dispose_AfterGlobalBusReplacement_UnsubscribesOriginalBus()
        {
            var original = new SubscriptionScopeTestBus(EventBus.Instance);
            var replacement = new SubscriptionScopeTestBus(EventBus.Instance);
            EventBus.SetInstance(original);
            var scope = new SubscriptionScope();
            scope.Subscribe("owned", () => { });
            EventBus.SetInstance(replacement);

            scope.Dispose();

            Assert.That(original.RemovedKeys, Is.EqualTo(new[] { "owned" }));
            Assert.That(replacement.RemovedKeys, Is.Empty);
            Assert.That(EventBus.TotalEventCount, Is.Zero);
        }

        [Test]
        public void Dispose_OneOwnerDoesNotRemoveAnotherOwnersSameDelegate()
        {
            var first = new SubscriptionScope();
            var second = new SubscriptionScope();
            int calls = 0;
            Action handler = () => calls++;
            first.Subscribe("shared", handler);
            second.Subscribe("shared", handler);

            first.Dispose();
            EventBus.Publish("shared");
            Assert.That(calls, Is.EqualTo(1));
            second.Dispose();
            EventBus.Publish("shared");
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_WhenCleanupThrows_TriesEveryEntryThenBecomesIdempotent()
        {
            var bus = new SubscriptionScopeTestBus(EventBus.Instance);
            bus.ThrowOnRemove.UnionWith(new[] { "first", "last" });
            var scope = new SubscriptionScope(bus);
            int calls = 0;
            scope.Subscribe("first", () => { });
            scope.Subscribe("middle", () => calls++);
            scope.Subscribe("last", () => { });

            AggregateException failure = Assert.Throws<AggregateException>(() => scope.Dispose());

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(bus.RemovedKeys, Is.EqualTo(new[] { "last", "middle", "first" }));
            EventBus.Publish("middle");
            Assert.That(calls, Is.Zero);
            Assert.DoesNotThrow(() => scope.Dispose());
            Assert.That(bus.RemovedKeys, Has.Count.EqualTo(3));
        }

        [Test]
        public void Subscribe_AfterDisposeOrWithNullHandler_DoesNotRegister()
        {
            Assert.Throws<ArgumentNullException>(() => new SubscriptionScope(null));
            var scope = new SubscriptionScope();
            Assert.Throws<ArgumentNullException>(() => scope.Subscribe("null", (Action)null));
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() => scope.Subscribe("late", () => { }));
            Assert.That(EventBus.TotalEventCount, Is.Zero);
        }

        [Test]
        public void Subscribe_WhenCustomBusDisposesScopeDuringRegistration_UnsubscribesImmediately()
        {
            var bus = new SubscriptionScopeTestBus(EventBus.Instance);
            var scope = new SubscriptionScope(bus);
            bus.AfterSubscribe = scope.Dispose;

            Assert.Throws<ObjectDisposedException>(() => scope.Subscribe("reentrant", () => { }));

            Assert.That(bus.RemovedKeys, Is.EqualTo(new[] { "reentrant" }));
            Assert.That(EventBus.TotalEventCount, Is.Zero);
        }

        private static void PublishAll()
        {
            EventBus.Publish("zero");
            EventBus.Publish("one", 2);
            EventBus.Publish("two", 3, 1);
            EventBus.Publish(new EventKey<int>("typed"), 3);
        }
    }
}
