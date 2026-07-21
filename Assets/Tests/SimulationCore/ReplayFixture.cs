namespace ZWebGLFrameWork.SimulationCore.Tests
{
    internal static class ReplayFixture
    {
        public static ReplayFile RecordTerminalBattle(byte[] canonicalManifestBytes)
        {
            BattleInit init = SessionTestDriver.OneHitBattleInit(canonicalManifestBytes);
            using ISimBattleSession session =
                new SimBattleSessionFactory().Create(canonicalManifestBytes);
            StartResult started = session.Start(init);
            if (!started.Accepted)
                throw new System.InvalidOperationException(started.Code.ToString());
            var recorder = new CommandRecorder(init);
            session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            });
            for (int count = 0; count < 20; count++)
            {
                TickResult tick = session.Tick();
                recorder.Record(tick);
                if (tick.Result != null)
                {
                    ReplayFile replay = recorder.Export();
                    if (replay.FinalResult == null)
                        throw new System.InvalidOperationException(
                            "Terminal result was not recorded.");
                    return replay;
                }
            }
            throw new System.InvalidOperationException(
                "Terminal replay fixture did not finish.");
        }
    }
}
