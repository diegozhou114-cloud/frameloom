using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Server;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class BufferedReplayValidatorTests
    {
        [Test]
        public async Task ValidateAsync_UsesTickIntoWithoutDirtyStateMaterialization()
        {
            (ReplayFile replay, AuthoritativeMatchRecord match) =
                BufferedServerConsumerTestSupport.AuthorityCase();
            var factory = new BufferedSpyFactory();
            var validator = new ReplayValidator(factory,
                ReplayValidationLimits.Approved,
                new UnlimitedReplayValidationBudget());

            ReplayValidationResult result = await validator.ValidateAsync(
                replay, match, BufferedServerConsumerTestSupport.Submitter,
                512, CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo(ReplayValidationCode.Valid));
            Assert.That(factory.Last.TickCalls, Is.Zero);
            Assert.That(factory.Last.TickIntoCalls, Is.EqualTo(replay.Records.Count));
            Assert.That(factory.Last.StateCalls, Is.Zero);
            Assert.That(factory.Last.LastFields,
                Is.EqualTo(SimTickOutputFields.CommandOutcomes));
        }

        [Test]
        public void LegacyValidate_UsesTickIntoWithoutDirtyStateMaterialization()
        {
            byte[] manifest = BufferedServerConsumerTestSupport.ManifestBytes();
            ReplayFile replay = BufferedServerConsumerTestSupport.LegacyReplay(manifest);
            var factory = new BufferedSpyFactory();
            var validator = new ReplayValidator(factory, manifest);

            ReplayResult result = validator.Validate(replay);

            Assert.That(result.IsValid, Is.True);
            Assert.That(factory.Last.TickCalls, Is.Zero);
            Assert.That(factory.Last.TickIntoCalls, Is.EqualTo(replay.Records.Count));
            Assert.That(factory.Last.StateCalls, Is.Zero);
            Assert.That(factory.Last.LastFields, Is.EqualTo(SimTickOutputFields.None));
        }
    }
}
