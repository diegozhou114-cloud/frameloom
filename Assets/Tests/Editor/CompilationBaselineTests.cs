using NUnit.Framework;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Editor;
using ZWebGLFrameWork.Gameplay;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class CompilationBaselineTests
    {
        [Test]
        public void EditorAssembly_AutoFixProcessor_Compiles()
        {
            Assert.That(typeof(AutoFixProcessor).Assembly.GetName().Name,
                Is.EqualTo("ZWebGLFrameWork.Editor"));
        }

        [Test]
        public void SimulationSystems_RefDelegates_Compile()
        {
            Assert.That(typeof(SimMovementSystem).Assembly,
                Is.SameAs(typeof(SimAttackSystem).Assembly));
        }

        [Test]
        public void GameplayAssembly_ResourceProducer_Compiles()
        {
            Assert.That(typeof(ResourceProducer).Assembly.GetName().Name,
                Is.EqualTo("ZWebGLFrameWork.Gameplay"));
        }

        [Test]
        public void CombatAssembly_CombatBootstrap_Compiles()
        {
            Assert.That(typeof(CombatBootstrap).Assembly.GetName().Name,
                Is.EqualTo("ZWebGLFrameWork.Combat"));
        }
    }
}
