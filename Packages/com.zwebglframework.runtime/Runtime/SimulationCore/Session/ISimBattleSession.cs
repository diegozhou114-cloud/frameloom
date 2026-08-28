using System;

namespace ZWebGLFrameWork.SimulationCore
{
    public interface ISimBattleSession : IDisposable
    {
        SimBattleState State { get; }
        StartResult Start(BattleInit init);
        CommandSubmitResult Submit(ISimCommand command);
        TickResult Tick();
        SimSnapshot CaptureSnapshot();
        SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot);
    }

    public interface ISimBattleSessionFactory
    {
        ISimBattleSession Create(byte[] canonicalManifestBytes, ISimLogger logger = null);
    }
}
