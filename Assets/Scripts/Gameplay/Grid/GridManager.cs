using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Gameplay
{
    /// <summary>
    /// 网格管理器 — 管理村庄网格布局。
    /// 世界坐标 ↔ 网格坐标互转，占用检测，边界校验。
    /// 跨场景单例。
    /// </summary>
    public class GridManager : Singleton<GridManager>
    {
        [Header("网格尺寸")]
        [SerializeField] private int _rows = 44;           // 行数（Z 轴方向）
        [SerializeField] private int _columns = 44;        // 列数（X 轴方向）
        [SerializeField] private float _cellSize = 1f;     // 每格边长（世界单位）

        /// <summary>网格行数</summary>
        public int Rows => _rows;

        /// <summary>网格列数</summary>
        public int Columns => _columns;

        /// <summary>单元格边长</summary>
        public float CellSize => _cellSize;

        /// <summary>网格原点世界坐标</summary>
        public Vector3 GridOrigin => transform.position;

        /// <summary>网格总宽度</summary>
        public float WorldWidth => _columns * _cellSize;

        /// <summary>网格总高度</summary>
        public float WorldHeight => _rows * _cellSize;

        // ==================== 坐标转换 ====================

        /// <summary>世界坐标 → 网格坐标</summary>
        public Vector2Int WorldToGrid(Vector3 worldPos)
        {
            Vector3 local = worldPos - GridOrigin;
            int x = Mathf.FloorToInt(local.x / _cellSize);
            int y = Mathf.FloorToInt(local.z / _cellSize);
            return new Vector2Int(x, y);
        }

        /// <summary>网格坐标 → 世界坐标（单元格中心）</summary>
        public Vector3 GridToWorld(Vector2Int gridPos)
        {
            return GridOrigin + new Vector3(
                gridPos.x * _cellSize + _cellSize * 0.5f,
                0f,
                gridPos.y * _cellSize + _cellSize * 0.5f);
        }

        /// <summary>网格坐标 → 世界坐标（可指定偏移比例，0=左下角，0.5=中心）</summary>
        public Vector3 GridToWorld(int gridX, int gridY, float offsetX = 0.5f, float offsetY = 0.5f)
        {
            return GridOrigin + new Vector3(
                gridX * _cellSize + _cellSize * offsetX,
                0f,
                gridY * _cellSize + _cellSize * offsetY);
        }

        // ==================== 边界校验 ====================

        /// <summary>网格坐标是否在边界内</summary>
        public bool IsInBounds(Vector2Int gridPos)
        {
            return gridPos.x >= 0 && gridPos.x < _columns &&
                   gridPos.y >= 0 && gridPos.y < _rows;
        }

        /// <summary>建筑（占据多格）是否全部在边界内</summary>
        public bool IsInBounds(Vector2Int gridPos, int width, int height)
        {
            return gridPos.x >= 0 && gridPos.x + width <= _columns &&
                   gridPos.y >= 0 && gridPos.y + height <= _rows;
        }

        // ==================== 占用检测 ====================

        /// <summary>指定网格位置是否被占用</summary>
        public bool IsOccupied(Vector2Int gridPos)
        {
            var village = Data.DataManager.Instance?.Village;
            if (village == null) return false;

            return village.IsOccupied(gridPos.x, gridPos.y);
        }

        /// <summary>建筑区域（多格）是否有任何占用</summary>
        public bool IsAreaOccupied(Vector2Int origin, int width, int height,
            string excludeBuildingId = null)
        {
            var village = Data.DataManager.Instance?.Village;
            if (village == null) return false;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var building = village.GetBuildingAt(origin.x + x, origin.y + y);
                    if (building != null && building.Id != excludeBuildingId)
                        return true;
                }
            }
            return false;
        }

        // ==================== 最近网格吸附 ====================

        /// <summary>世界坐标吸附到最近有效网格</summary>
        public Vector2Int SnapToGrid(Vector3 worldPos, int buildingWidth, int buildingHeight)
        {
            Vector2Int raw = WorldToGrid(worldPos);

            // 限制在边界内
            raw.x = Mathf.Clamp(raw.x, 0, _columns - buildingWidth);
            raw.y = Mathf.Clamp(raw.y, 0, _rows - buildingHeight);

            return raw;
        }

        /// <summary>获取网格单元格的世界坐标边界（用于高亮绘制）</summary>
        public Bounds GetCellBounds(Vector2Int gridPos, int width = 1, int height = 1)
        {
            Vector3 min = GridToWorld(gridPos.x, gridPos.y, 0f, 0f);
            Vector3 max = GridToWorld(gridPos.x + width - 1, gridPos.y + height - 1, 1f, 1f);
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;
            size.y = 0.1f;
            return new Bounds(center, size);
        }
    }
}
