using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 仿真同步桥 — 监听 SimWorld 事件，管理 Unity GameObject 生命周期。
    /// </summary>
    public class SimulationSyncBridge
    {
        private readonly SimWorld _world;
        private readonly ViewConfig _viewConfig;
        private readonly Dictionary<int, GameObject> _entityViews = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Vector3> _prevPositions = new Dictionary<int, Vector3>();

        /// <summary>事件：实体视图创建后（用于外部挂载组件）</summary>
        public event System.Action<SimEntity, GameObject> OnViewCreated;

        public SimulationSyncBridge(SimWorld world, ViewConfig viewConfig)
        {
            _world = world;
            _viewConfig = viewConfig;

            _world.OnEntityCreated += OnEntityCreated;
            _world.OnEntityDestroyed += OnEntityDestroyed;
        }

        private void OnEntityCreated(SimEntity entity)
        {
            // 从 SimTransformComponent 获取初始网格位置确定 ArchetypeId
            // 简化：用 GridX/GridY 推断类型。实际应由 SimWorld 维护 ArchetypeId 组件。
            // 这里先根据是否同时有 AttackComponent 来区分攻守方
            var prefab = _viewConfig.Entries.Length > 0 ? _viewConfig.Entries[0].Prefab : null;
            if (prefab == null) return;

            var go = Object.Instantiate(prefab);
            go.name = $"SimEntity_{entity.Id}_{entity.Generation}";

            _entityViews[entity.Id] = go;
            _prevPositions[entity.Id] = Vector3.zero;

            OnViewCreated?.Invoke(entity, go);
        }

        private void OnEntityDestroyed(SimEntity entity)
        {
            if (_entityViews.TryGetValue(entity.Id, out var go))
            {
                Object.Destroy(go);
                _entityViews.Remove(entity.Id);
                _prevPositions.Remove(entity.Id);
            }
        }

        /// <summary>同步所有实体位置（每渲染帧调用，带插值）</summary>
        public void SyncPositions(Interpolator interpolator)
        {
            float alpha = interpolator.Alpha;

            _world.ForEachComponent<SimTransformComponent>((entity, transform) =>
            {
                if (!_entityViews.TryGetValue(entity.Id, out var go)) return;

                float currentX = transform.PosX.ToFloat();
                float currentY = transform.PosY.ToFloat();
                Vector3 currentPos = new Vector3(currentX, 0f, currentY);

                if (_prevPositions.TryGetValue(entity.Id, out var prevPos))
                {
                    go.transform.position = Interpolator.LerpPosition(prevPos, currentPos, alpha);
                }
                else
                {
                    go.transform.position = currentPos;
                }

                _prevPositions[entity.Id] = currentPos;
            });
        }

        /// <summary>获取指定实体的视图</summary>
        public GameObject GetView(int entityId)
        {
            _entityViews.TryGetValue(entityId, out var go);
            return go;
        }

        /// <summary>清理所有视图</summary>
        public void Cleanup()
        {
            _world.OnEntityCreated -= OnEntityCreated;
            _world.OnEntityDestroyed -= OnEntityDestroyed;

            foreach (var go in _entityViews.Values)
                Object.Destroy(go);
            _entityViews.Clear();
            _prevPositions.Clear();
        }
    }
}
