using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class SceneLoadingTests : RuntimeTestBase
    {
        private sealed class ImmediateLoader : ISceneLoadService
        {
            private readonly SceneLoadResult _result;

            public ImmediateLoader(SceneLoadStatus status) =>
                _result = new SceneLoadResult(status, status.ToString());

            public UniTask<SceneLoadResult> LoadSceneAsync(
                string sceneName,
                UnityEngine.SceneManagement.LoadSceneMode mode,
                CancellationToken token) => UniTask.FromResult(_result);
        }

        private sealed class BlockingLoader : ISceneLoadService
        {
            public bool Cancelled { get; private set; }

            public async UniTask<SceneLoadResult> LoadSceneAsync(
                string sceneName,
                UnityEngine.SceneManagement.LoadSceneMode mode,
                CancellationToken token)
            {
                try
                {
                    await UniTask.Delay(60000, cancellationToken: token);
                    return new SceneLoadResult(SceneLoadStatus.Succeeded);
                }
                catch (System.OperationCanceledException)
                {
                    Cancelled = true;
                    return new SceneLoadResult(SceneLoadStatus.Cancelled);
                }
            }
        }

        private sealed class ControlledOperation : ISceneLoadOperation
        {
            public float Progress { get; set; }
            public bool IsDone { get; set; }
            public bool AllowSceneActivation { get; set; }
        }

        [UnityTest]
        public System.Collections.IEnumerator LoadingState_MissingScene_PublishesFailureOnly()
        {
            yield return AssertFailureOnly(new ImmediateLoader(SceneLoadStatus.NotFound));
        }

        [UnityTest]
        public System.Collections.IEnumerator LoadingState_SceneTimeout_PublishesFailureOnly()
        {
            yield return AssertFailureOnly(new ImmediateLoader(SceneLoadStatus.Timeout));
        }

        [UnityTest]
        public System.Collections.IEnumerator LoadingState_DoesNotCompleteBeforeActivation()
        {
            var operation = new ControlledOperation { Progress = 0.9f, IsDone = false };
            UniTask<SceneLoadResult> task = SceneLoadRunner.RunAsync(
                operation, 2f, CancellationToken.None, () => 0.1f);
            yield return null;
            Assert.That(operation.AllowSceneActivation, Is.True);
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending));
            operation.IsDone = true;
            yield return task.ToCoroutine(result =>
                Assert.That(result.Status, Is.EqualTo(SceneLoadStatus.Succeeded)));
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneLoadRunner_CancelBeforeActivation_ReleasesActivation()
        {
            var operation = new ControlledOperation { Progress = 0.9f, IsDone = false };
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            UniTask<SceneLoadResult> task = SceneLoadRunner.RunAsync(
                operation, 2f, cancelled.Token, () => 0.1f);

            yield return task.ToCoroutine(result =>
                Assert.That(result.Status, Is.EqualTo(SceneLoadStatus.Cancelled)));
            Assert.That(operation.AllowSceneActivation, Is.True);
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneLoadRunner_CancelBeforeLoadingCompletes_ReleasesActivation()
        {
            var operation = new ControlledOperation { Progress = 0.5f, IsDone = false };
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            UniTask<SceneLoadResult> task = SceneLoadRunner.RunAsync(
                operation, 2f, cancelled.Token, () => 0.1f);

            yield return task.ToCoroutine(result =>
                Assert.That(result.Status, Is.EqualTo(SceneLoadStatus.Cancelled)));
            Assert.That(operation.AllowSceneActivation, Is.True);
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneLoadRunner_TimeoutBeforeActivation_ReleasesActivation()
        {
            var operation = new ControlledOperation { Progress = 0.5f, IsDone = false };
            using var cleanup = new CancellationTokenSource();
            UniTask<SceneLoadResult> task = SceneLoadRunner.RunAsync(
                operation, 1f, cleanup.Token, () => 0.6f);

            yield return null;
            yield return null;
            yield return task.ToCoroutine(result =>
                Assert.That(result.Status, Is.EqualTo(SceneLoadStatus.Timeout)));
            Assert.That(operation.AllowSceneActivation, Is.True);
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneLoadRunner_ActivationPhaseTimesOut()
        {
            var operation = new ControlledOperation { Progress = 0.9f, IsDone = false };
            using var cleanup = new CancellationTokenSource();
            UniTask<SceneLoadResult> task = SceneLoadRunner.RunAsync(
                operation, 1f, cleanup.Token, () => 0.6f);
            yield return null;
            yield return null;
            yield return null;
            bool completedBeforeCleanup = task.Status != UniTaskStatus.Pending;
            cleanup.Cancel();
            yield return task.ToCoroutine(result =>
                Assert.That(result.Status, Is.EqualTo(SceneLoadStatus.Timeout)));
            Assert.That(completedBeforeCleanup, Is.True,
                "activation phase must use the same timeout budget");
        }

        [UnityTest]
        public System.Collections.IEnumerator LoadingState_Exit_CancelsUnderlyingSceneLoad()
        {
            var loader = new BlockingLoader();
            var fsm = CreateFsm(loader);
            Assert.That(fsm.ChangeState(GameState.Loading, Payload()), Is.True);
            Assert.That(fsm.ChangeState(GameState.MainVillage), Is.True);
            yield return null;
            Assert.That(loader.Cancelled, Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(GameState.MainVillage));
        }

        private System.Collections.IEnumerator AssertFailureOnly(ISceneLoadService loader)
        {
            int failed = 0;
            int completed = 0;
            EventBus.Subscribe<SceneLoadStatus>("LoadingFailed", _ => failed++);
            EventBus.Subscribe("LoadingComplete", () => completed++);
            var fsm = CreateFsm(loader);
            Assert.That(fsm.ChangeState(GameState.Loading, Payload()), Is.True);
            yield return null;
            Assert.That(failed, Is.EqualTo(1));
            Assert.That(completed, Is.Zero);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(GameState.MainVillage));
        }

        private static FSM<GameState> CreateFsm(ISceneLoadService loader)
        {
            var fsm = new FSM<GameState> { TransitionGuard = (_, _) => true };
            fsm.AddStates(new LoadingState(loader),
                new ProbeState(GameState.MainVillage), new ProbeState(GameState.Battle));
            return fsm;
        }

        private static LoadingPayload Payload() => new LoadingPayload
        {
            TargetScene = "test-scene",
            TargetState = GameState.Battle,
        };

        private sealed class ProbeState : FSMState<GameState>
        {
            public ProbeState(GameState id) : base(id) { }
        }
    }
}
