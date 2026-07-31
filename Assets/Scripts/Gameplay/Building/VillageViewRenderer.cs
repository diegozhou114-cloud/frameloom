using System;
using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Gameplay
{
    public sealed class VillageViewRenderer : MonoBehaviour
    {
        private readonly Dictionary<string, GameObject> _views =
            new Dictionary<string, GameObject>();
        private VillageData _village;

        public int ViewCount => _views.Count;

        private void Start()
        {
            EventBus.Subscribe(DataManager.EventDataReady, OnDataReady);
            Bind(DataManager.Instance?.Village);
        }

        public void Bind(VillageData village)
        {
            Unbind();
            _village = village;
            if (_village == null) return;

            _village.OnBuildingAdded += AddView;
            _village.OnBuildingRemoved += RemoveView;
            foreach (BuildingData building in _village.Buildings)
                AddView(building);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe(DataManager.EventDataReady, OnDataReady);
            Unbind();
        }

        private void OnDataReady()
        {
            Bind(DataManager.Instance?.Village);
        }

        private void Unbind()
        {
            if (_village != null)
            {
                _village.OnBuildingAdded -= AddView;
                _village.OnBuildingRemoved -= RemoveView;
            }
            _village = null;

            foreach (GameObject view in _views.Values)
            {
                if (view != null) Destroy(view);
            }
            _views.Clear();
        }

        private void AddView(BuildingData building)
        {
            if (building == null || string.IsNullOrEmpty(building.Id) ||
                _views.ContainsKey(building.Id))
                return;

            BuildingConfig config = ConfigTableManager.HasInstance
                ? ConfigTableManager.Instance.GetBuilding(building.TypeId)
                : null;
            int width = Math.Max(1, config?.SizeX ?? 1);
            int height = Math.Max(1, config?.SizeY ?? 1);
            GridManager grid = GridManager.Instance;

            GameObject view = GameObject.CreatePrimitive(PrimitiveType.Cube);
            view.name = $"Building_{building.Id}";
            view.transform.SetParent(transform, false);
            view.transform.position = grid.GridToWorld(
                building.GridX.Value,
                building.GridY.Value,
                width * 0.5f / grid.CellSize,
                height * 0.5f / grid.CellSize) + Vector3.up * 0.5f;
            view.transform.localScale = new Vector3(
                width * grid.CellSize * 0.9f,
                1f,
                height * grid.CellSize * 0.9f);
            view.AddComponent<BuildingView>().BuildingId = building.Id;
            _views.Add(building.Id, view);
        }

        private void RemoveView(BuildingData building)
        {
            if (building == null || !_views.TryGetValue(building.Id, out GameObject view))
                return;
            _views.Remove(building.Id);
            if (view != null) Destroy(view);
        }
    }
}
