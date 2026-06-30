using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 数据管理器 — 跨场景单例，持有所有游戏数据模型。
    /// 对外暴露 Player / Village 等数据入口。
    /// 注意：存档/读档由 SaveManager 负责（职责分离），本类仅为纯运行时状态持有者。
    /// </summary>
    public class DataManager : SingletonDontDestroy<DataManager>
    {
        /// <summary>EventBus 事件键：数据管理器就绪</summary>
        public const string EventDataReady = "DataReady";

        /// <summary>玩家数据</summary>
        public PlayerData Player { get; private set; }

        /// <summary>村庄数据</summary>
        public VillageData Village { get; private set; }

        /// <summary>数据是否已初始化</summary>
        public bool IsReady { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            Initialize();
        }

        private void Initialize()
        {
            Player = new PlayerData();
            Village = new VillageData();
            IsReady = true;

            EventBus.Publish(EventDataReady);
        }

        /// <summary>重置所有数据（新游戏）</summary>
        /// <remarks>
        /// Buildings is a plain <see cref="System.Collections.Generic.List{T}"/>,
        /// not an ObservableCollection, so <see cref="VillageData.Reset"/> does not
        /// clear it. We explicitly call <c>Clear()</c> here to ensure buildings are
        /// wiped on new game start.
        /// </remarks>
        public void ResetAll()
        {
            Player.Reset();
            Village.Reset();
            Village.Buildings.Clear();
        }

        protected override void OnDestroy()
        {
            Player = null;
            Village = null;
            IsReady = false;
            base.OnDestroy();
        }
    }
}
