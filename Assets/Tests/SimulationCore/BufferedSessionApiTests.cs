using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class BufferedSessionApiTests
    {
        private const string AssemblyName = "ZWebGLFrameWork.SimulationCore";

        [Test]
        public void BufferedTickCapability_ExposesApprovedPublicContract()
        {
            Type flags = RequiredType("SimTickOutputFields");
            Assert.That(flags.IsEnum, Is.True);
            Assert.That(flags.GetCustomAttribute<FlagsAttribute>(), Is.Not.Null);
            Assert.That(Enum.Parse(flags, "None"), Is.EqualTo(Enum.ToObject(flags, 0)));
            Assert.That(Enum.Parse(flags, "ExecutedCommands"), Is.EqualTo(Enum.ToObject(flags, 1)));
            Assert.That(Enum.Parse(flags, "CommandOutcomes"), Is.EqualTo(Enum.ToObject(flags, 2)));
            Assert.That(Enum.Parse(flags, "Events"), Is.EqualTo(Enum.ToObject(flags, 4)));
            Assert.That(Enum.Parse(flags, "Presentation"), Is.EqualTo(Enum.ToObject(flags, 8)));
            Assert.That(Enum.Parse(flags, "All"), Is.EqualTo(Enum.ToObject(flags, 15)));

            Type buffer = RequiredType("SimTickOutputBuffer");
            Type capability = RequiredType("IBufferedSimBattleSession");
            Assert.That(capability.IsInterface, Is.True);
            MethodInfo tickInto = capability.GetMethod("TickInto");
            Assert.That(tickInto, Is.Not.Null);
            Assert.That(tickInto.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(tickInto.GetParameters(), Has.Length.EqualTo(1));
            Assert.That(tickInto.GetParameters()[0].ParameterType, Is.EqualTo(buffer));
        }

        [Test]
        public void SimTickOutputBuffer_ExposesCapacityAndLifetimeContract()
        {
            Type flags = RequiredType("SimTickOutputFields");
            Type buffer = RequiredType("SimTickOutputBuffer");
            Type presentation = RequiredType("SimPresentationOutputBuffer");
            Assert.That(buffer.GetConstructor(new[] { flags, typeof(int), typeof(int), typeof(int) }),
                Is.Not.Null);
            Assert.That(typeof(IDisposable).IsAssignableFrom(buffer), Is.True);
            AssertMethod(buffer, "EnsureCapacity", typeof(void), typeof(int), typeof(int), typeof(int));
            AssertMethod(buffer, "Clear", typeof(void));
            AssertProperty(buffer, "Fields", flags);
            AssertProperty(buffer, "Code", typeof(TickCode));
            AssertProperty(buffer, "Lifecycle", typeof(SessionLifecycle));
            AssertProperty(buffer, "Phase", typeof(SimCombatPhase));
            AssertProperty(buffer, "Tick", typeof(int));
            AssertProperty(buffer, "Revision", typeof(long));
            AssertProperty(buffer, "Checksum", typeof(ulong));
            AssertProperty(buffer, "Result", typeof(SimBattleResult));
            AssertProperty(buffer, "ExecutedCommands", typeof(IReadOnlyList<ISimCommand>));
            AssertProperty(buffer, "CommandOutcomes", typeof(IReadOnlyList<CommandExecutionResult>));
            AssertProperty(buffer, "Events", typeof(IReadOnlyList<SimBattleEvent>));
            AssertProperty(buffer, "Presentation", presentation);
            AssertProperty(presentation, "Entities", typeof(IReadOnlyList<EntityPresentationState>));
            AssertProperty(presentation, "Created", typeof(IReadOnlyList<EntityPresentationState>));
            AssertProperty(presentation, "Updated", typeof(IReadOnlyList<EntityPresentationState>));
            AssertProperty(presentation, "Destroyed", typeof(IReadOnlyList<EntityPresentationKey>));
        }

        [Test]
        public void SimTickOutputBuffer_RejectsInvalidFlagsAndCapacities()
        {
            Type flags = RequiredType("SimTickOutputFields");
            Type buffer = RequiredType("SimTickOutputBuffer");
            ConstructorInfo constructor = buffer.GetConstructor(
                new[] { flags, typeof(int), typeof(int), typeof(int) });
            Assert.That(constructor, Is.Not.Null);

            AssertInvocationThrows<ArgumentOutOfRangeException>(() => constructor.Invoke(new[]
            {
                Enum.ToObject(flags, 16), 0, 0, 0,
            }));
            for (int index = 1; index < 4; index++)
            {
                object[] arguments = { Enum.ToObject(flags, 15), 0, 0, 0 };
                arguments[index] = -1;
                AssertInvocationThrows<ArgumentOutOfRangeException>(
                    () => constructor.Invoke(arguments));
            }

            object instance = constructor.Invoke(new[] { Enum.ToObject(flags, 15), 0, 0, 0 });
            MethodInfo ensureCapacity = buffer.GetMethod("EnsureCapacity");
            for (int index = 0; index < 3; index++)
            {
                object[] capacities = { 0, 0, 0 };
                capacities[index] = -1;
                AssertInvocationThrows<ArgumentOutOfRangeException>(
                    () => ensureCapacity.Invoke(instance, capacities));
            }
        }

        private static Type RequiredType(string name)
        {
            Type type = Type.GetType($"ZWebGLFrameWork.SimulationCore.{name}, {AssemblyName}");
            Assert.That(type, Is.Not.Null, $"Expected public type {name}.");
            return type;
        }

        private static void AssertMethod(Type type, string name, Type returnType,
            params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, parameters);
            Assert.That(method, Is.Not.Null, $"Expected {type.Name}.{name}.");
            Assert.That(method.ReturnType, Is.EqualTo(returnType));
        }

        private static void AssertProperty(Type type, string name, Type propertyType)
        {
            PropertyInfo property = type.GetProperty(name);
            Assert.That(property, Is.Not.Null, $"Expected {type.Name}.{name}.");
            Assert.That(property.PropertyType, Is.EqualTo(propertyType));
            Assert.That(property.CanWrite, Is.False);
        }

        private static void AssertInvocationThrows<T>(TestDelegate action) where T : Exception
        {
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(action);
            Assert.That(exception.InnerException, Is.TypeOf<T>());
        }
    }
}
