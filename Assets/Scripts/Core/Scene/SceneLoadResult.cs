using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ZWebGLFrameWork.Core
{
    public enum SceneLoadStatus
    {
        Succeeded,
        NotFound,
        Timeout,
        Cancelled,
        Failed,
    }

    public readonly struct SceneLoadResult
    {
        public SceneLoadStatus Status { get; }
        public string Error { get; }
        public bool Succeeded => Status == SceneLoadStatus.Succeeded;

        public SceneLoadResult(SceneLoadStatus status, string error = null)
        {
            Status = status;
            Error = error;
        }
    }

    public interface ISceneLoadOperation
    {
        float Progress { get; }
        bool IsDone { get; }
        bool AllowSceneActivation { get; set; }
    }

    public interface ISceneLoadService
    {
        UniTask<SceneLoadResult> LoadSceneAsync(
            string sceneName, LoadSceneMode mode, CancellationToken token);
    }
}
