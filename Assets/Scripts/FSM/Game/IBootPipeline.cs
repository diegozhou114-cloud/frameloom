using System.Threading;
using Cysharp.Threading.Tasks;

namespace ZWebGLFrameWork.FSM
{
    public interface IBootPipeline
    {
        UniTask<GameState?> RunAsync(CancellationToken token);
    }
}
