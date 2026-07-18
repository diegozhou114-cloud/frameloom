using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    public static class SceneLoadRunner
    {
        public static async UniTask<SceneLoadResult> RunAsync(
            ISceneLoadOperation operation,
            float timeoutSeconds,
            CancellationToken token,
            Func<float> deltaTime = null,
            Action<float> reportProgress = null)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            Func<float> elapsedStep = deltaTime ?? (() => Time.unscaledDeltaTime);
            float elapsed = 0f;
            while (true)
            {
                if (token.IsCancellationRequested)
                    return new SceneLoadResult(SceneLoadStatus.Cancelled);
                elapsed += elapsedStep();
                if (elapsed > timeoutSeconds)
                {
                    return new SceneLoadResult(
                        SceneLoadStatus.Timeout,
                        "Scene load timed out");
                }

                if (operation.Progress < 0.9f)
                {
                    reportProgress?.Invoke(Mathf.Clamp01(operation.Progress / 0.9f));
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    continue;
                }

                if (token.IsCancellationRequested)
                    return new SceneLoadResult(SceneLoadStatus.Cancelled);
                if (!operation.AllowSceneActivation)
                    operation.AllowSceneActivation = true;
                if (operation.IsDone)
                    break;
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            reportProgress?.Invoke(1f);
            return new SceneLoadResult(SceneLoadStatus.Succeeded);
        }
    }
}
