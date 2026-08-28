using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace ZWebGLFrameWork.Core.Tests
{
    public sealed class ObjectPoolTests
    {
        private sealed class Probe
        {
            internal readonly int Id;
            internal Probe(int id = 0) => Id = id;
            public override bool Equals(object obj) => obj is Probe;
            public override int GetHashCode() => 1;
        }

        private static readonly Func<Probe> Factory = () => new Probe();

        [Test]
        public void Constructor_RejectsInvalidArgumentsAndCapacity()
        {
            Assert.Throws<ArgumentNullException>(() => new ObjectPool<Probe>(null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ObjectPool<Probe>(Factory, preload: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewPool(2, 1));
            ParameterInfo limit = Constructor.GetParameters()[5];
            Assert.That(limit.Name, Is.EqualTo("maxRetained"));
            Assert.That(limit.DefaultValue, Is.EqualTo(256));
        }

        [Test]
        public void Release_UsesReferenceIdentityAndRejectsInvalidReturns()
        {
            ObjectPool<Probe> pool = NewPool(0, 2);
            Probe first = pool.Get();
            Probe second = pool.Get();
            Assert.That(first, Is.EqualTo(second));
            Assert.That(ReferenceEquals(first, second), Is.False);
            pool.Release(first);
            pool.Release(second);
            Assert.That(pool.Count, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => pool.Release(first));
            Assert.Throws<ArgumentException>(() => pool.Release(new Probe()));
            Assert.Throws<ArgumentNullException>(() => pool.Release(null));
            ((IDisposable)pool).Dispose();
        }

        [Test]
        public void Release_WhenRetentionIsFullDestroysOverflow()
        {
            int destroyed = 0;
            ObjectPool<Probe> pool = NewPool(0, 1, onDestroy: _ => destroyed++);
            Probe retained = pool.Get();
            Probe overflow = pool.Get();
            pool.Release(retained);
            pool.Release(overflow);
            Assert.That(pool.Count, Is.EqualTo(1));
            Assert.That(destroyed, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => pool.Release(overflow));
            ((IDisposable)pool).Dispose();
        }

        [Test]
        public void Clear_DestroysAvailableButCheckedOutRemainsOwned()
        {
            int destroyed = 0;
            ObjectPool<Probe> pool = NewPool(2, 2, onDestroy: _ => destroyed++);
            Probe checkedOut = pool.Get();
            pool.Clear();
            Assert.That(pool.Count, Is.Zero);
            Assert.That(destroyed, Is.EqualTo(1));
            pool.Release(checkedOut);
            Assert.That(pool.Count, Is.EqualTo(1));
            ((IDisposable)pool).Dispose();
            Assert.That(destroyed, Is.EqualTo(2));
        }

        [Test]
        public void Dispose_DestroysAllOwnedAndRejectsFurtherUse()
        {
            int destroyed = 0;
            ObjectPool<Probe> pool = NewPool(2, 2, onDestroy: _ => destroyed++);
            Probe checkedOut = pool.Get();
            IDisposable disposable = pool as IDisposable;
            Assert.That(disposable, Is.Not.Null);
            disposable.Dispose();
            disposable.Dispose();
            Assert.That(destroyed, Is.EqualTo(2));
            Assert.That(pool.Count, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => pool.Get());
            Assert.Throws<ObjectDisposedException>(() => pool.Release(checkedOut));
        }

        [Test]
        public void CallbackFailures_PreserveOwnershipState()
        {
            bool failGet = true;
            bool failRelease = true;
            ObjectPool<Probe> pool = NewPool(1, 1,
                onGet: _ => { if (failGet) throw new InvalidOperationException("get"); },
                onRelease: _ => { if (failRelease) throw new InvalidOperationException("release"); });
            Assert.Throws<InvalidOperationException>(() => pool.Get());
            Assert.That(pool.Count, Is.EqualTo(1));
            failGet = false;
            Probe item = pool.Get();
            Assert.Throws<InvalidOperationException>(() => pool.Release(item));
            Assert.That(pool.Count, Is.Zero);
            failRelease = false;
            pool.Release(item);
            Assert.That(pool.Count, Is.EqualTo(1));
            ((IDisposable)pool).Dispose();
        }

        [Test]
        public void FactoryAndDestroyFailures_LeaveConsistentLedger()
        {
            ObjectPool<Probe> failedFactory = NewPool(0, 1,
                factory: () => throw new InvalidOperationException("factory"));
            Assert.Throws<InvalidOperationException>(() => failedFactory.Get());
            Assert.That(failedFactory.Count, Is.Zero);
            ObjectPool<Probe> nullFactory = NewPool(0, 1, factory: () => null);
            Assert.Throws<InvalidOperationException>(() => nullFactory.Get());
            Assert.That(nullFactory.Count, Is.Zero);

            int attempts = 0;
            ObjectPool<Probe> failedDestroy = NewPool(2, 2,
                onDestroy: _ => { attempts++; throw new InvalidOperationException("destroy"); });
            Assert.Throws<InvalidOperationException>(() => failedDestroy.Clear());
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(failedDestroy.Count, Is.Zero);
        }

        [Test]
        public void SteadyStateObjectPool_GetReleaseDoesNotAllocateAfterWarmup()
        {
            ObjectPool<Probe> pool = NewPool(1, 1);
            for (int index = 0; index < 100; index++) pool.Release(pool.Get());
            Recorder recorder = CreateValidatedRecorder();
            recorder.enabled = true;
            for (int index = 0; index < 600; index++) pool.Release(pool.Get());
            recorder.enabled = false;
            long events = recorder.sampleBlockCount;
            ((IDisposable)pool).Dispose();
            Assert.That(events, Is.Zero, "steady Get/Release allocated");
        }

        private static ConstructorInfo Constructor => typeof(ObjectPool<Probe>).GetConstructor(
            new[] { typeof(Func<Probe>), typeof(Action<Probe>), typeof(Action<Probe>),
                typeof(Action<Probe>), typeof(int), typeof(int) });

        private static ObjectPool<Probe> NewPool(int preload, int maxRetained,
            Func<Probe> factory = null, Action<Probe> onGet = null,
            Action<Probe> onRelease = null, Action<Probe> onDestroy = null)
        {
            Assert.That(Constructor, Is.Not.Null, "missing bounded constructor");
            try
            {
                return (ObjectPool<Probe>)Constructor.Invoke(new object[]
                    { factory ?? Factory, onGet, onRelease, onDestroy, preload, maxRetained });
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static Recorder CreateValidatedRecorder()
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            Assert.That(recorder, Is.Not.Null);
            Assert.That(recorder.isValid, Is.True);
            recorder.FilterToCurrentThread();
            recorder.enabled = false;
            recorder.enabled = true;
            AllocateCalibrationBuffer();
            recorder.enabled = false;
            Assert.That(recorder.sampleBlockCount, Is.GreaterThanOrEqualTo(1));
            return recorder;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AllocateCalibrationBuffer() =>
            GC.KeepAlive(new byte[4096]);
    }
}
