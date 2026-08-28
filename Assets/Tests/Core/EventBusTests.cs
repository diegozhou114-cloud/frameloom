using NUnit.Framework;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Core.Tests
{
    [TestFixture]
    public class EventBusTests
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
            EventBus.ClearAll();
        }

        // ==================== 0-Param Subscribe / Publish ====================

        [Test]
        public void SubscribeAndPublish_ZeroParams_HandlerIsCalled()
        {
            bool wasCalled = false;
            EventBus.Subscribe("test_zero", () => wasCalled = true);

            EventBus.Publish("test_zero");

            Assert.IsTrue(wasCalled, "Handler should have been invoked on publish.");
        }

        [Test]
        public void Publish_NoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EventBus.Publish("no_such_key"),
                "Publishing with zero subscribers should not throw.");
        }

        // ==================== 1-Param Subscribe / Publish ====================

        [Test]
        public void SubscribeAndPublish_OneParam_HandlerReceivesArg()
        {
            string received = null;
            EventBus.Subscribe<string>("test_one", s => received = s);

            EventBus.Publish("test_one", "hello");

            Assert.AreEqual("hello", received, "Handler should receive the published argument.");
        }

        [Test]
        public void SubscribeAndPublish_OneParamInt_HandlerReceivesCorrectValue()
        {
            int received = 0;
            EventBus.Subscribe<int>("test_int", i => received = i);

            EventBus.Publish("test_int", 42);

            Assert.AreEqual(42, received);
        }

        // ==================== Unsubscribe ====================

        [Test]
        public void Unsubscribe_HandlerNoLongerCalled()
        {
            int callCount = 0;
            System.Action handler = () => callCount++;

            EventBus.Subscribe("test_unsub", handler);
            EventBus.Publish("test_unsub");
            Assert.AreEqual(1, callCount, "Handler should be called before unsubscribe.");

            EventBus.Unsubscribe("test_unsub", handler);
            EventBus.Publish("test_unsub");

            Assert.AreEqual(1, callCount, "Handler should NOT be called after unsubscribe.");
        }

        [Test]
        public void Unsubscribe_OneParam_HandlerNoLongerCalled()
        {
            string received = null;
            System.Action<string> handler = s => received = s;

            EventBus.Subscribe<string>("test_unsub1", handler);
            EventBus.Unsubscribe<string>("test_unsub1", handler);
            EventBus.Publish("test_unsub1", "should not arrive");

            Assert.IsNull(received, "Handler should not receive after unsubscribe.");
        }

        // ==================== Multiple Subscribers ====================

        [Test]
        public void MultipleSubscribers_BothAreCalled()
        {
            int callCount = 0;
            System.Action handler1 = () => callCount++;
            System.Action handler2 = () => callCount++;

            EventBus.Subscribe("test_multi", handler1);
            EventBus.Subscribe("test_multi", handler2);
            EventBus.Publish("test_multi");

            Assert.AreEqual(2, callCount, "Both handlers should be invoked.");
        }

        [Test]
        public void MultipleSubscribers_OneParam_BothReceiveSameArg()
        {
            string received1 = null;
            string received2 = null;
            System.Action<string> h1 = s => received1 = s;
            System.Action<string> h2 = s => received2 = s;

            EventBus.Subscribe("test_multi1", h1);
            EventBus.Subscribe("test_multi1", h2);
            EventBus.Publish("test_multi1", "data");

            Assert.AreEqual("data", received1);
            Assert.AreEqual("data", received2);
        }

        [Test]
        public void OneParamSameKeyWithDifferentPayloadTypes_DoNotConflict()
        {
            int receivedInt = 0;
            string receivedString = null;
            EventBus.Subscribe<int>("test_typed_key", value => receivedInt = value);
            EventBus.Subscribe<string>("test_typed_key", value => receivedString = value);

            EventBus.Publish("test_typed_key", 42);
            EventBus.Publish("test_typed_key", "payload");

            Assert.AreEqual(42, receivedInt);
            Assert.AreEqual("payload", receivedString);
        }

        [Test]
        public void TwoParamSameKeyWithDifferentPayloadTypes_DoNotConflict()
        {
            int receivedInt = 0;
            string receivedString = null;
            EventBus.Subscribe<int, string>("test_typed_pair", (number, text) =>
                receivedInt = number + text.Length);
            EventBus.Subscribe<string, int>("test_typed_pair", (text, number) =>
                receivedString = text + number);

            EventBus.Publish("test_typed_pair", 7, "a");
            EventBus.Publish("test_typed_pair", "b", 8);

            Assert.AreEqual(8, receivedInt);
            Assert.AreEqual("b8", receivedString);
        }

        // ==================== ClearAll ====================

        [Test]
        public void ClearAll_NoHandlersFireAfterClear()
        {
            int callCount = 0;
            EventBus.Subscribe("test_clear", () => callCount++);
            EventBus.Subscribe<string>("test_clear_str", _ => callCount++);

            EventBus.ClearAll();

            EventBus.Publish("test_clear");
            EventBus.Publish("test_clear_str", "x");

            Assert.AreEqual(0, callCount, "No handlers should fire after ClearAll.");
        }

        [Test]
        public void ClearAll_TotalEventCountBecomesZero()
        {
            EventBus.Subscribe("a", () => { });
            EventBus.Subscribe<string>("b", _ => { });
            EventBus.Subscribe<int, string>("c", (_, _) => { });

            Assert.Greater(EventBus.TotalEventCount, 0, "Should have events before clear.");

            EventBus.ClearAll();

            Assert.AreEqual(0, EventBus.TotalEventCount, "TotalEventCount should be 0 after ClearAll.");
        }

        // ==================== EventKey Type-Safe API ====================

        [Test]
        public void EventKey_SubscribeAndPublish_HandlerCalled()
        {
            var key = new EventKey<string>("test_eventkey");
            string received = null;

            EventBus.Subscribe(key, s => received = s);
            EventBus.Publish(key, "payload");

            Assert.AreEqual("payload", received, "EventKey-based publish should deliver the payload.");
        }

        [Test]
        public void EventKey_Unsubscribe_HandlerNotCalled()
        {
            var key = new EventKey<string>("test_eventkey_unsub");
            int callCount = 0;
            System.Action<string> handler = _ => callCount++;

            EventBus.Subscribe(key, handler);
            EventBus.Unsubscribe(key, handler);
            EventBus.Publish(key, "x");

            Assert.AreEqual(0, callCount, "Handler should not be called after EventKey unsubscribe.");
        }

        [Test]
        public void EventKey_StringImplicitConversion_KeyUsedCorrectly()
        {
            var key = new EventKey<string>("implicit_key");
            // Verify implicit conversion works
            string rawKey = key;
            Assert.AreEqual("implicit_key", rawKey);

            // Subscribe via EventKey, publish via raw string key should still work
            bool called = false;
            EventBus.Subscribe(key, _ => called = true);
            EventBus.Publish(rawKey, "x");

            Assert.IsTrue(called, "Publishing via raw string key (same key) should trigger handler.");
        }
    }
}
