using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.FrameSync
{
    // TODO(PRODUCTION-CONTENT): Replace framework command IDs with approved project content.
    public sealed class FrameworkAuthorityCommandMapper : IAuthorityCommandMapper
    {
        public ISimCommand? ToSimulation(CommandEnvelope input)
        {
            if (input == null || input.Version != RealtimeProtocol.Version ||
                input.Tick < 0 || (input.PlayerId != 0 && input.PlayerId != 1) ||
                input.Sequence >= int.MaxValue ||
                !FrameSyncCommandPayload.TryDecode(input.MessageId, input.Payload,
                    out FrameSyncCommandPayload? payload))
                return null;

            return payload.Type switch
            {
                FrameSyncCommandType.Deploy => new DeployCmd
                {
                    Tick = input.Tick,
                    PlayerId = input.PlayerId,
                    Sequence = (int)input.Sequence,
                    UnitTypeId = payload.First,
                    GridX = payload.Second,
                    GridY = payload.Third
                },
                FrameSyncCommandType.Move => new MoveCmd
                {
                    Tick = input.Tick,
                    PlayerId = input.PlayerId,
                    Sequence = (int)input.Sequence,
                    EntityId = payload.First,
                    TargetX = payload.Second,
                    TargetY = payload.Third
                },
                FrameSyncCommandType.Skill => new SkillCmd
                {
                    Tick = input.Tick,
                    PlayerId = input.PlayerId,
                    Sequence = (int)input.Sequence,
                    CasterId = payload.First,
                    SkillId = payload.Second,
                    TargetX = payload.Third,
                    TargetY = payload.Fourth
                },
                FrameSyncCommandType.EndDeployment => new EndDeploymentCmd
                {
                    Tick = input.Tick,
                    PlayerId = input.PlayerId,
                    Sequence = (int)input.Sequence
                },
                _ => null
            };
        }
    }
}
