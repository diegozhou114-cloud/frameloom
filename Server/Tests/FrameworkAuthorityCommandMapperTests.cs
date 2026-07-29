using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class FrameworkAuthorityCommandMapperTests
    {
        [Test]
        public void Mapper_ConvertsFrameworkDeployPayloadWithoutChangingEnvelopeMetadata()
        {
            var mapper = new FrameworkAuthorityCommandMapper();
            var input = new CommandEnvelope
            {
                Version = RealtimeProtocol.Version,
                Tick = 7,
                PlayerId = 1,
                Sequence = 9,
                MessageId = (uint)FrameSyncCommandType.Deploy,
                Payload = new byte[]
                {
                    10, 0, 0, 0,
                    3, 0, 0, 0,
                    4, 0, 0, 0
                }
            };

            ISimCommand command = mapper.ToSimulation(input);

            Assert.That(command, Is.TypeOf<DeployCmd>());
            var deploy = (DeployCmd)command;
            Assert.That(deploy.Tick, Is.EqualTo(7));
            Assert.That(deploy.PlayerId, Is.EqualTo(1));
            Assert.That(deploy.Sequence, Is.EqualTo(9));
            Assert.That(deploy.UnitTypeId, Is.EqualTo(10));
            Assert.That(deploy.GridX, Is.EqualTo(3));
            Assert.That(deploy.GridY, Is.EqualTo(4));
        }

        [Test]
        public void Mapper_RejectsUnknownOrMalformedFrameworkPayload()
        {
            var mapper = new FrameworkAuthorityCommandMapper();
            var input = new CommandEnvelope
            {
                Version = RealtimeProtocol.Version,
                Tick = 0,
                PlayerId = 0,
                Sequence = 1,
                MessageId = 99,
                Payload = new byte[] { 1 }
            };

            Assert.That(mapper.ToSimulation(input), Is.Null);
            input.MessageId = (uint)FrameSyncCommandType.Skill;
            Assert.That(mapper.ToSimulation(input), Is.Null);
        }
    }
}
