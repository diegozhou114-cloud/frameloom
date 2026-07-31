using System.Threading;
using System.Reflection;
using System.Text.RegularExpressions;
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

        private sealed class CompletingPipeline : IBootPipeline
        {
            private readonly UniTaskCompletionSource<GameState?> _completion = new();

            public async UniTask<GameState?> RunAsync(CancellationToken token)
            {
                return await _completion.Task.AttachExternalCancellation(token);
            }

            public void Complete(GameState target) => _completion.TrySetResult(target);
        }

        private sealed class VillageProbe : FSMState<GameState>
        {
            public VillageProbe() : base(GameState.MainVillage) { }
        }

        private sealed class LoginProbe : FSMState<GameState>
        {
            public LoginProbe() : base(GameState.Login) { }
        }

        private sealed class PauseProbe : FSMState<GameState>
        {
            public PauseProbe() : base(GameState.Pause) { }
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

        [UnityTest]
        public System.Collections.IEnumerator
            LaunchState_PipelineCompletionDuringPause_TransitionsAfterResume()
        {
            var pipeline = new CompletingPipeline();
            var fsm = new FSM<GameState>();
            fsm.TransitionGuard = (from, to) =>
                from == GameState.Launch && to == GameState.Login;
            fsm.OverlayGuard = GameFSM.CanPushOverlay;
            fsm.AddStates(new LaunchState(pipeline), new LoginProbe(), new PauseProbe());
            try
            {
                Assert.That(fsm.ChangeState(GameState.Launch), Is.True);
                Assert.That(fsm.PushState(GameState.Pause), Is.True);

                pipeline.Complete(GameState.Login);
                yield return null;

                Assert.That(fsm.CurrentStateId, Is.EqualTo(GameState.Pause));
                Assert.That(fsm.PopState(), Is.True);
                Assert.That(fsm.CurrentStateId, Is.EqualTo(GameState.Login));
            }
            finally
            {
                fsm.Clear();
            }
        }

        [TestCase(ResourceProviderType.AssetBundle)]
        [TestCase(ResourceProviderType.Addressables)]
        public void LaunchState_LegacyResourceProviderSelection_FallsBackToResources(
            ResourceProviderType configuredProvider)
        {
            MethodInfo warning = typeof(LaunchState).GetMethod("WarnOnUnsupportedResourceProvider",
                BindingFlags.Static | BindingFlags.NonPublic);

            LogAssert.Expect(UnityEngine.LogType.Warning,
                new Regex($"{configuredProvider} is reserved for TODO\\(YooAsset\\)"));
            warning.Invoke(null, new object[] { configuredProvider });

            Assert.That(warning, Is.Not.Null);
        }
    }
}
