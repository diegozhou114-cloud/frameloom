using System.Threading;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class LaunchCancellationTests
    {
        private sealed class BlockingPipeline : IBootPipeline
        {
            public bool Cancelled { get; private set; }

            public async UniTask<GameState?> RunAsync(CancellationToken token)
            {
                try
                {
                    await UniTask.Delay(60000, cancellationToken: token);
                    return GameState.Login;
                }
                catch (System.OperationCanceledException)
                {
                    Cancelled = true;
                    throw;
                }
            }
        }

        private sealed class VillageProbe : FSMState<GameState>
        {
            public VillageProbe() : base(GameState.MainVillage) { }
        }

        [UnityTest]
        public System.Collections.IEnumerator LaunchState_Exit_CancelsPipelineAndPreventsLateTransition()
        {
            var pipeline = new BlockingPipeline();
            var fsm = new FSM<GameState>();
            fsm.AddStates(new LaunchState(pipeline), new VillageProbe());
            fsm.TransitionGuard = (_, _) => true;
            Assert.That(fsm.ChangeState(GameState.Launch), Is.True);
            Assert.That(fsm.ChangeState(GameState.MainVillage), Is.True);
            yield return null;

            Assert.That(pipeline.Cancelled, Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(GameState.MainVillage));
        }

        [TestCase(ResourceProviderType.AssetBundle)]
        [TestCase(ResourceProviderType.Addressables)]
        public void LaunchState_LegacyResourceProviderSelection_FallsBackToResources(
            ResourceProviderType configuredProvider)
        {
            MethodInfo factory = typeof(LaunchState).GetMethod("CreateResourceProvider",
                BindingFlags.Static | BindingFlags.NonPublic);

            IResourceProvider provider = (IResourceProvider)factory.Invoke(
                null, new object[] { configuredProvider });

            Assert.That(provider, Is.TypeOf<ResourcesProvider>());
        }
    }
}
