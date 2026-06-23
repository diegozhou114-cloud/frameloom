using System.Collections.Generic;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// ISimInitializer 实现 — 将 DataManager 的 VillageData/BuildingData 转换为 SimEntitySpawnInfo。
    /// </summary>
    public class SimInitializerImpl : ISimInitializer
    {
        private readonly VillageData _village;
        private readonly int _seed;

        public int Seed => _seed;

        public SimInitializerImpl(VillageData village, int seed)
        {
            _village = village;
            _seed = seed;
        }

        public IEnumerable<SimEntitySpawnInfo> GetInitialEntities()
        {
            // 防守方建筑
            foreach (var building in _village.Buildings)
            {
                yield return new SimEntitySpawnInfo(
                    archetypeId: building.TypeId,
                    gridX: building.GridX.Value,
                    gridY: building.GridY.Value,
                    teamId: 1 // Defender = 1 (matches SimEntitySpawnInfo convention: 0=Attacker, 1=Defender)
                );
            }
        }
    }
}
