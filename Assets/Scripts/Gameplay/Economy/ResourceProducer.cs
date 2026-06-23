using UnityEngine;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Gameplay
{
    /// <summary>
    /// 资源产出系统 — 基于建筑等级持续产出金币/圣水。
    /// 由 MainVillageState.OnUpdate 每帧驱动，
    /// 产出量通过 Observable 自动通知 UI 更新。
    /// </summary>
    public class ResourceProducer
    {
        private readonly VillageData _village;
        private bool _isRunning;

        /// <summary>每秒金币产出</summary>
        public float GoldPerSecond { get; private set; }

        /// <summary>每秒圣水产出入</summary>
        public float ElixirPerSecond { get; private set; }

        // 累积小数部分（避免每帧 FloorToInt 丢失精度）
        private float _goldAccum;
        private float _elixirAccum;

        public ResourceProducer(VillageData village)
        {
            _village = village;
        }

        public void Start()
        {
            RecalculateRates();
            _isRunning = true;
            Debug.Log($"[ResourceProducer] 启动: 金币 +{GoldPerSecond:F1}/s, 圣水 +{ElixirPerSecond:F1}/s");
        }

        /// <summary>每帧调用，累积产出并按整数增量写入 PlayerData</summary>
        public void Tick(float deltaTime)
        {
            if (!_isRunning) return;

            var player = DataManager.Instance.Player;
            if (player == null) return;

            _goldAccum += GoldPerSecond * deltaTime;
            _elixirAccum += ElixirPerSecond * deltaTime;

            int goldAdd = Mathf.FloorToInt(_goldAccum);
            int elixirAdd = Mathf.FloorToInt(_elixirAccum);

            if (goldAdd > 0)
            {
                player.Gold.Value += goldAdd;
                _goldAccum -= goldAdd;
            }

            if (elixirAdd > 0)
            {
                player.Elixir.Value += elixirAdd;
                _elixirAccum -= elixirAdd;
            }
        }

        public void Stop()
        {
            _isRunning = false;
        }

        /// <summary>重新计算产出速率（建筑变更后调用）</summary>
        public void RecalculateRates()
        {
            float goldRate = 0f;
            float elixirRate = 0f;

            foreach (var building in _village.Buildings)
            {
                var config = ConfigTableManager.Instance.GetBuilding(building.TypeId);
                if (config == null || config.Category != "Resource") continue;

                float baseRate = building.Level.Value * 0.5f; // 每级 0.5/s

                if (config.Name.Contains("Gold") || config.Name.Contains("Mine"))
                    goldRate += baseRate;
                else if (config.Name.Contains("Elixir") || config.Name.Contains("Collector"))
                    elixirRate += baseRate;
                else if (config.Name.Contains("Dark"))
                    ; // 暗黑重油产出（后续扩展）
            }

            GoldPerSecond = goldRate;
            ElixirPerSecond = elixirRate;
        }
    }
}
