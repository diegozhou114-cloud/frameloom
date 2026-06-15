using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Core.Tests
{
    // ==================== Observable<T> Tests ====================

    [TestFixture]
    public class ObservableTests
    {
        // --- Value change triggers OnChanged ---

        [Test]
        public void ValueChange_TriggersOnChanged_WithCorrectOldAndNewValues()
        {
            var obs = new Observable<int>(10);
            int oldReceived = 0;
            int newReceived = 0;

            obs.OnChanged += (oldVal, newVal) =>
            {
                oldReceived = oldVal;
                newReceived = newVal;
            };

            obs.Value = 42;

            Assert.AreEqual(10, oldReceived, "Old value should be the previous value.");
            Assert.AreEqual(42, newReceived, "New value should be the newly set value.");
        }

        [Test]
        public void ValueChange_StringType_TriggersOnChanged()
        {
            var obs = new Observable<string>("initial");
            string oldReceived = null;
            string newReceived = null;

            obs.OnChanged += (o, n) => { oldReceived = o; newReceived = n; };

            obs.Value = "updated";

            Assert.AreEqual("initial", oldReceived);
            Assert.AreEqual("updated", newReceived);
        }

        // --- Same value does NOT trigger OnChanged (zero-change skip) ---

        [Test]
        public void SameValue_DoesNotTriggerOnChanged()
        {
            var obs = new Observable<int>(5);
            int fireCount = 0;
            obs.OnChanged += (_, _) => fireCount++;

            obs.Value = 5; // same value

            Assert.AreEqual(0, fireCount,
                "Setting the same value should not trigger OnChanged (zero-change skip).");
        }

        [Test]
        public void SameValue_TriggersOnlyOnce_WhenValueActuallyChanges()
        {
            var obs = new Observable<int>(100);
            int fireCount = 0;
            obs.OnChanged += (_, _) => fireCount++;

            obs.Value = 100; // no-op
            obs.Value = 200; // real change
            obs.Value = 200; // no-op again

            Assert.AreEqual(1, fireCount,
                "OnChanged should fire exactly once — only when value actually changes.");
        }

        // --- Implicit conversion ---

        [Test]
        public void ImplicitConversion_ToT_ReturnsUnderlyingValue()
        {
            Observable<int> obs = new Observable<int>(5);
            int x = obs; // implicit operator T

            Assert.AreEqual(5, x, "Implicit conversion should return the underlying value.");
        }

        [Test]
        public void ImplicitConversion_StringType_ReturnsCorrectValue()
        {
            Observable<string> obs = new Observable<string>("hello");
            string s = obs;

            Assert.AreEqual("hello", s);
        }

        // --- SetSilent ---

        [Test]
        public void SetSilent_ChangesValue_WithoutTriggeringOnChanged()
        {
            var obs = new Observable<int>(10);
            int fireCount = 0;
            obs.OnChanged += (_, _) => fireCount++;

            obs.SetSilent(99);

            Assert.AreEqual(0, fireCount, "SetSilent should NOT trigger OnChanged.");
            Assert.AreEqual(99, (int)obs, "Value should still be updated.");
        }

        // --- ClearListeners ---

        [Test]
        public void ClearListeners_HandlerNotCalledAfterClear()
        {
            var obs = new Observable<int>(1);
            int fireCount = 0;
            obs.OnChanged += (_, _) => fireCount++;

            obs.ClearListeners();
            obs.Value = 2;

            Assert.AreEqual(0, fireCount, "No handler should fire after ClearListeners.");
        }
    }

    // ==================== ObservableModel Tests ====================

    [TestFixture]
    public class ObservableModelTests
    {
        /// <summary>
        /// Simple test model with one observable field for testing ObservableModel.
        /// </summary>
        private class TestModel : ObservableModel
        {
            public Observable<int> Score = new Observable<int>(0);
        }

        [Test]
        public void FieldChange_TriggersOnAnyChanged_WithCorrectFieldName()
        {
            var model = new TestModel();
            string receivedFieldName = null;
            object receivedOld = null;
            object receivedNew = null;

            model.OnAnyChanged += (fieldName, oldVal, newVal) =>
            {
                receivedFieldName = fieldName;
                receivedOld = oldVal;
                receivedNew = newVal;
            };

            model.Score.Value = 100;

            Assert.AreEqual("Score", receivedFieldName,
                "OnAnyChanged should report the field name.");
            Assert.AreEqual(0, receivedOld, "Old value should be 0.");
            Assert.AreEqual(100, receivedNew, "New value should be 100.");
        }

        [Test]
        public void FieldChange_SameValue_DoesNotTriggerOnAnyChanged()
        {
            var model = new TestModel();
            int fireCount = 0;
            model.OnAnyChanged += (_, _, _) => fireCount++;

            model.Score.Value = 0; // same as initial

            Assert.AreEqual(0, fireCount,
                "Setting same value should not trigger OnAnyChanged (zero-change skip).");
        }

        [Test]
        public void GetField_ReturnsCorrectObservable()
        {
            var model = new TestModel();
            var field = model.GetField<int>("Score");

            Assert.IsNotNull(field);
            Assert.AreEqual(0, (int)field);
        }

        [Test]
        public void GetFieldNames_ContainsExpectedFields()
        {
            var model = new TestModel();
            var names = model.GetFieldNames();

            Assert.Contains("Score", (System.Collections.IList)names,
                "GetFieldNames should include 'Score'.");
        }

        [Test]
        public void ModelName_ReturnsClassName()
        {
            var model = new TestModel();
            Assert.AreEqual("TestModel", model.ModelName);
        }
    }
}
