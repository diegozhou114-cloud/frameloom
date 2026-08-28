using System;

namespace ZWebGLFrameWork.Data
{
    public sealed class ValidatedSaveState
    {
        public PlayerData Player { get; }
        public VillageData Village { get; }

        public ValidatedSaveState(PlayerData player, VillageData village)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Village = village ?? throw new ArgumentNullException(nameof(village));
        }
    }
}
