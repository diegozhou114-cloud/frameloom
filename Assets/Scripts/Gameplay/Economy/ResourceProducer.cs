using System;
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
        private readonly PlayerData _player;
        private readonly Func<int, BuildingConfig> _getBuildingConfig;
        private bool _isRunning;

        /// <summary>每秒金币产出</summary>
        public float GoldPerSecond { get; private set; }

        /// <summary>每秒圣水产出入</summary>
        public float ElixirPerSecond { get; private set; }

        /// <summary>每秒暗黑重油产出</summary>
        public float DarkElixirPerSecond { get; private set; }

        // 累积小数部分（避免每帧 FloorToInt 丢失精度）
        private float _goldAccum;
        private float _elixirAccum;
        private float _darkElixirAccum;

        /// <summary>资源上限，防止溢出（可配置）</summary>
        private const int _maxResourceCap = int.MaxValue - 1;

        private static int SaturatingAdd(int current, int amount)
        {
            long sum = (long)current + amount;
            return (int)Math.Min(sum, _maxResourceCap);
        }

        public ResourceProducer(VillageData village)
            : this(village, DataManager.Instance.Player,
                typeId => ConfigTableManager.Instance.GetBuilding(typeId))
        {
        }

        public ResourceProducer(
            VillageData village,
            PlayerData player,
            Func<int, BuildingConfig> getBuildingConfig)
        {
            _village = village ?? throw new ArgumentNullException(nameof(village));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _getBuildingConfig = getBuildingConfig
                ?? throw new ArgumentNullException(nameof(getBuildingConfig));
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _village.OnBuildingAdded += OnBuildingAdded;
            _village.OnBuildingRemoved += OnBuildingRemoved;
            foreach (BuildingData building in _village.Buildings)
                building.Level.OnChanged += OnBuildingLevelChanged;
            RecalculateRates();
            Debug.Log($"[ResourceProducer] 启动: 金币 +{GoldPerSecond:F1}/s, 圣水 +{ElixirPerSecond:F1}/s, 暗黑重油 +{DarkElixirPerSecond:F1}/s");
        }

        /// <summary>每帧调用，累积产出并按整数增量写入 PlayerData</summary>
        public void Tick(float deltaTime)
        {
            if (!_isRunning) return;

            if (_player == null) return;

            _goldAccum += GoldPerSecond * deltaTime;
            _elixirAccum += ElixirPerSecond * deltaTime;
            _darkElixirAccum += DarkElixirPerSecond * deltaTime;

            int goldAdd = Mathf.FloorToInt(_goldAccum);
            int elixirAdd = Mathf.FloorToInt(_elixirAccum);
            int darkElixirAdd = Mathf.FloorToInt(_darkElixirAccum);

            if (goldAdd > 0)
            {
                _player.Gold.Value = SaturatingAdd(_player.Gold.Value, goldAdd);
                _goldAccum -= goldAdd;
            }

            if (elixirAdd > 0)
            {
                _player.Elixir.Value = SaturatingAdd(_player.Elixir.Value, elixirAdd);
                _elixirAccum -= elixirAdd;
            }

            if (darkElixirAdd > 0)
            {
                _player.DarkElixir.Value = SaturatingAdd(_player.DarkElixir.Value, darkElixirAdd);
                _darkElixirAccum -= darkElixirAdd;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _village.OnBuildingAdded -= OnBuildingAdded;
            _village.OnBuildingRemoved -= OnBuildingRemoved;
            foreach (BuildingData building in _village.Buildings)
                building.Level.OnChanged -= OnBuildingLevelChanged;
        }

        private void OnBuildingAdded(BuildingData building)
        {
            if (building == null) return;
            building.Level.OnChanged += OnBuildingLevelChanged;
            RecalculateRates();
        }

        private void OnBuildingRemoved(BuildingData building)
        {
            if (building == null) return;
            building.Level.OnChanged -= OnBuildingLevelChanged;
            RecalculateRates();
        }

        private void OnBuildingLevelChanged(int oldLevel, int newLevel)
        {
            RecalculateRates();
        }

        /// <summary>重新计算产出速率（建筑变更后调用）</summary>
        public void RecalculateRates()
        {
            float goldRate = 0f;
            float elixirRate = 0f;
            float darkElixirRate = 0f;

            foreach (var building in _village.Buildings)
            {
                var config = _getBuildingConfig(building.TypeId);
                if (config == null || config.Category != "Resource") continue;

                float baseRate = building.Level.Value * 0.5f; // 每级 0.5/s
                switch (config.ResourceType)
                {
                    case ResourceType.Gold:
                        goldRate += baseRate;
                        break;
                    case ResourceType.Elixir:
                        elixirRate += baseRate;
                        break;
                    case ResourceType.DarkElixir:
                        darkElixirRate += baseRate;
                        break;
                    case ResourceType.None:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(config.ResourceType), config.ResourceType,
                            "Unknown resource type");
                }
            }

            GoldPerSecond = goldRate;
            ElixirPerSecond = elixirRate;
            DarkElixirPerSecond = darkElixirRate;
        }
    }
}
