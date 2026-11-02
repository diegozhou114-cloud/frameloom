using System;
using NUnit.Framework;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Core.Tests
{
    public sealed class SubscriptionScopeRegistrationTests
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

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        public void FailedRegistration_RollsBackOnlyItsOwnListener(int kind, bool afterAdding)
        {
            int calls = 0;
            Action<SubscriptionScope> register = Registration(kind, () => calls++);
            using var existing = new SubscriptionScope();
            register(existing);
            var bus = new SubscriptionScopeTestBus(EventBus.Instance);
            var failure = new InvalidOperationException("registration failed");
            Action fail = () => throw failure;
            if (afterAdding) bus.AfterSubscribe = fail;
            else bus.BeforeSubscribe = fail;
            using var rejected = new SubscriptionScope(bus);

            Assert.That(Assert.Throws<InvalidOperationException>(() => register(rejected)),
                Is.SameAs(failure));
            Publish(kind);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(EventBus.TotalEventCount, Is.EqualTo(1));
            rejected.Dispose();
            Publish(kind);
            Assert.That(calls, Is.EqualTo(2));
            existing.Dispose();
            Assert.That(EventBus.TotalEventCount, Is.Zero);
        }

        [Test]
        public void FailedRollback_ReportsBothFailuresAndRetainsCleanupForDispose()
        {
            var bus = new SubscriptionScopeTestBus(EventBus.Instance);
            var registrationFailure = new ApplicationException("registration failed");
            bus.AfterSubscribe = () => throw registrationFailure;
            bus.ThrowOnRemove.Add("owned");
            using var scope = new SubscriptionScope(bus);

            AggregateException failure = Assert.Throws<AggregateException>(() =>
                scope.Subscribe("owned", () => { }));

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions[0], Is.SameAs(registrationFailure));
            Assert.That(EventBus.TotalEventCount, Is.EqualTo(1));
            bus.ThrowOnRemove.Clear();
            scope.Dispose();
            Assert.That(EventBus.TotalEventCount, Is.Zero);
            Assert.That(bus.RemovedKeys, Has.Count.EqualTo(2));
        }

        private static Action<SubscriptionScope> Registration(int kind, Action callback)
        {
            Action<int> one = _ => callback();
            Action<int, int> two = (_, __) => callback();
            return kind switch
            {
                0 => scope => scope.Subscribe("owned", callback),
                1 => scope => scope.Subscribe("owned", one),
                2 => scope => scope.Subscribe("owned", two),
                _ => scope => scope.Subscribe(new EventKey<int>("owned"), one),
            };
        }

        private static void Publish(int kind)
        {
            if (kind == 0) EventBus.Publish("owned");
            else if (kind == 2) EventBus.Publish("owned", 1, 2);
            else EventBus.Publish("owned", 1);
        }
    }
}
