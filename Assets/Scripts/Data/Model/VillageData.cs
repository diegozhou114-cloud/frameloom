using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    /// <summary>村庄数据 — 管理所有建筑和网格布局</summary>
    public class VillageData : ObservableModel
    {
        private readonly Func<int, BuildingConfig> _getBuildingConfig;

        public VillageData()
            : this(typeId => ConfigTableManager.HasInstance
                ? ConfigTableManager.Instance.GetBuilding(typeId)
                : null)
        {
        }

        public VillageData(Func<int, BuildingConfig> getBuildingConfig)
        {
            _getBuildingConfig = getBuildingConfig
                ?? throw new ArgumentNullException(nameof(getBuildingConfig));
        }

        public override void Reset()
        {
            base.Reset();
            Name.SetSilent("My Village");
            MaxBuildings.SetSilent(50);
        }

        /// <summary>村庄名称</summary>
        public Observable<string> Name = new Observable<string>("My Village");

        /// <summary>所有建筑列表</summary>
        public List<BuildingData> Buildings { get; private set; } = new List<BuildingData>();

        /// <summary>建筑上限</summary>
        public Observable<int> MaxBuildings = new Observable<int>(50);

        /// <summary>建筑数量变化事件</summary>
        public event System.Action<BuildingData> OnBuildingAdded;
        public event System.Action<BuildingData> OnBuildingRemoved;

        /// <summary>添加建筑</summary>
        public bool AddBuilding(BuildingData building)
        {
            if (building == null || string.IsNullOrWhiteSpace(building.Id))
                return false;
            if (building.GridX.Value < 0 || building.GridY.Value < 0)
                return false;

            if (GetBuilding(building.Id) != null)
            {
                Debug.LogWarning($"[VillageData] 建筑 Id 已存在: {building.Id}");
                return false;
            }

            if (building.TypeId == 1 && Buildings.Exists(item => item.TypeId == 1))
            {
                Debug.LogWarning("[VillageData] 村庄只能包含一个大本营");
                return false;
            }

            if (Buildings.Count >= MaxBuildings.Value)
            {
                Debug.LogWarning($"[VillageData] 建筑已满: {Buildings.Count}/{MaxBuildings.Value}");
                return false;
            }

            BuildingConfig config = _getBuildingConfig(building.TypeId);
            int width = Math.Max(1, config?.SizeX ?? 1);
            int height = Math.Max(1, config?.SizeY ?? 1);
            long right = (long)building.GridX.Value + width;
            long top = (long)building.GridY.Value + height;
            foreach (BuildingData existing in Buildings)
            {
                BuildingConfig existingConfig = _getBuildingConfig(existing.TypeId);
                int existingWidth = Math.Max(1, existingConfig?.SizeX ?? 1);
                int existingHeight = Math.Max(1, existingConfig?.SizeY ?? 1);
                long existingRight = (long)existing.GridX.Value + existingWidth;
                long existingTop = (long)existing.GridY.Value + existingHeight;
                if (building.GridX.Value < existingRight && existing.GridX.Value < right &&
                    building.GridY.Value < existingTop && existing.GridY.Value < top)
                    return false;
            }

            Buildings.Add(building);
            OnBuildingAdded?.Invoke(building);
            return true;
        }

        /// <summary>移除建筑</summary>
        public bool RemoveBuilding(string buildingId)
        {
            var building = Buildings.Find(b => b.Id == buildingId);
            if (building == null) return false;

            Buildings.Remove(building);
            OnBuildingRemoved?.Invoke(building);
            return true;
        }

        /// <summary>移除所有建筑，并为每个已移除建筑通知订阅者。</summary>
        public void ClearBuildings()
        {
            while (Buildings.Count > 0)
            {
                BuildingData building = Buildings[Buildings.Count - 1];
                Buildings.RemoveAt(Buildings.Count - 1);
                OnBuildingRemoved?.Invoke(building);
            }
        }

        /// <summary>根据 ID 查找建筑</summary>
        public BuildingData GetBuilding(string buildingId)
        {
            return Buildings.Find(b => b.Id == buildingId);
        }

        /// <summary>获取指定位置的建筑</summary>
        public BuildingData GetBuildingAt(int gridX, int gridY)
        {
            foreach (BuildingData building in Buildings)
            {
                BuildingConfig config = _getBuildingConfig(building.TypeId);
                int width = Math.Max(1, config?.SizeX ?? 1);
                int height = Math.Max(1, config?.SizeY ?? 1);
                int originX = building.GridX.Value;
                int originY = building.GridY.Value;
                if (gridX >= originX && gridX < originX + width &&
                    gridY >= originY && gridY < originY + height)
                    return building;
            }

            return null;
        }

        /// <summary>是否已被占用</summary>
        public bool IsOccupied(int gridX, int gridY)
        {
            return GetBuildingAt(gridX, gridY) != null;
        }
    }
}
