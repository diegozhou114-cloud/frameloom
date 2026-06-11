using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    /// <summary>村庄数据 — 管理所有建筑和网格布局</summary>
    public class VillageData : ObservableModel
    {
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
            if (Buildings.Count >= MaxBuildings.Value)
            {
                Debug.LogWarning($"[VillageData] 建筑已满: {Buildings.Count}/{MaxBuildings.Value}");
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

        /// <summary>根据 ID 查找建筑</summary>
        public BuildingData GetBuilding(string buildingId)
        {
            return Buildings.Find(b => b.Id == buildingId);
        }

        /// <summary>获取指定位置的建筑</summary>
        public BuildingData GetBuildingAt(int gridX, int gridY)
        {
            return Buildings.Find(b =>
                b.GridX.Value == gridX && b.GridY.Value == gridY);
        }

        /// <summary>是否已被占用</summary>
        public bool IsOccupied(int gridX, int gridY)
        {
            return GetBuildingAt(gridX, gridY) != null;
        }
    }
}
