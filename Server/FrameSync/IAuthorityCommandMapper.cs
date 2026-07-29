using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.FrameSync
{
    public interface IAuthorityCommandMapper
    {
        ISimCommand? ToSimulation(CommandEnvelope input);
    }
}
