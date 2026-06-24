using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 战斗初始布局描述 — 单个实体的生成参数。
    /// SimulationCore 不依赖 VillageData/BuildingData 等 Unity 侧类型，
    /// 由宿主侧实现此接口完成数据转换。
    /// </summary>
    [Serializable]
    public readonly struct SimEntitySpawnInfo
    {
        /// <summary>实体原型 ID（决定挂哪些逻辑组件和初始值）</summary>
        [JsonInclude] public readonly int ArchetypeId;
        /// <summary>初始网格 X 坐标</summary>
        [JsonInclude] public readonly int GridX;
        /// <summary>初始网格 Y 坐标</summary>
        [JsonInclude] public readonly int GridY;
        /// <summary>所属队伍（SimTeam.Attacker=1, SimTeam.Defender=2）</summary>
        [JsonInclude] public readonly int TeamId;

        [JsonConstructor]
        public SimEntitySpawnInfo(int archetypeId, int gridX, int gridY, int teamId)
        {
            ArchetypeId = archetypeId;
            GridX = gridX;
            GridY = gridY;
            TeamId = teamId;
        }
    }

    /// <summary>
    /// 战斗初始状态提供者 — 由 DataManager 侧实现。
    /// 返回战斗开始时的所有实体生成信息。
    /// </summary>
    public interface ISimInitializer
    {
        /// <summary>随机种子 — 确保客户端/服务器同 seed</summary>
        int Seed { get; }

        /// <summary>获取所有初始实体的生成信息</summary>
        IEnumerable<SimEntitySpawnInfo> GetInitialEntities();
    }
}
