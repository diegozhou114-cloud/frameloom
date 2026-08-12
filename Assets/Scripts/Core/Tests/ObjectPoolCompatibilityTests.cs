using System;
using System.Reflection;
using NUnit.Framework;

namespace ZWebGLFrameWork.Core.Tests
{
    public sealed class ObjectPoolCompatibilityTests
    {
        private sealed class Probe { }

        [Test]
        public void LegacyFiveParameterConstructor_ExistsAndForwards()
        {
            ConstructorInfo constructor = typeof(ObjectPool<Probe>).GetConstructor(new[]
            {
                typeof(Func<Probe>),
                typeof(Action<Probe>),
                typeof(Action<Probe>),
                typeof(Action<Probe>),
                typeof(int)
            });

            Assert.That(constructor, Is.Not.Null, "missing legacy CLR constructor");
            var pool = (ObjectPool<Probe>)constructor.Invoke(new object[]
                { new Func<Probe>(() => new Probe()), null, null, null, 1 });
            Assert.That(pool.Count, Is.EqualTo(1));
            pool.Dispose();
        }
    }
}
