using System;

namespace ZWebGLFrameWork.Data
{
    /// <summary>建筑状态</summary>
    public enum BuildingState
    {
        Normal,       // 正常（闲置）
        Upgrading,    // 升级中
        Constructing, // 建造中
        Disabled,     // 不可用（没资源等）
    }

    /// <summary>单个建筑数据</summary>
    [Serializable]
    public class BuildingData
    {
        /// <summary>唯一 ID</summary>
        public string Id;

        /// <summary>建筑类型 ID（配表引用）</summary>
        public int TypeId;

        /// <summary>当前等级</summary>
        public Observable<int> Level = new Observable<int>(1);

        /// <summary>网格 X 坐标</summary>
        public Observable<int> GridX = new Observable<int>(0);

        /// <summary>网格 Y 坐标</summary>
        public Observable<int> GridY = new Observable<int>(0);

        /// <summary>当前状态</summary>
        public Observable<BuildingState> State = new Observable<BuildingState>(BuildingState.Normal);

        /// <summary>升级完成时间（Unix 时间戳，0 表示不在升级）</summary>
        public Observable<long> UpgradeEndTime = new Observable<long>(0);

        /// <summary>是否正在升级</summary>
        public bool IsUpgrading =>
            State.Value == BuildingState.Upgrading && UpgradeEndTime.Value > 0;

        /// <summary>创建新建筑实例</summary>
        public static BuildingData Create(int typeId, int gridX, int gridY)
        {
            return new BuildingData
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                TypeId = typeId,
                GridX = { Value = gridX },
                GridY = { Value = gridY },
            };
        }
    }
}
