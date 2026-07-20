using System.Threading;
using System.Threading.Tasks;

namespace ZWebGLFrameWork.Server
{
    public interface IReplayMatchProvider
    {
        Task<AuthoritativeMatchRecord> LoadAsync(
            string path, CancellationToken cancellationToken);
    }
}
