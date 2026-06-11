using System;
using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// A* 网格寻路 — 在村庄网格上寻找最短路径。
    /// 8 方向移动（上下左右+四角），对角线有阻挡检测（不穿墙角）。
    /// </summary>
    public static class AStarPathfinding
    {
        private static readonly Vector2Int[] _directions = {
            new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, 0),
            new Vector2Int(1, -1), new Vector2Int(0, -1), new Vector2Int(-1, -1),
            new Vector2Int(-1, 0), new Vector2Int(-1, 1),
        };

        // 直方向成本 = 10，对角 = 14（≈ 10 * √2）
        private static readonly int[] _costs = { 10, 14, 10, 14, 10, 14, 10, 14 };

        private static GridManager _grid;
        private static readonly Dictionary<Vector2Int, Node> _nodeCache =
            new Dictionary<Vector2Int, Node>();
        private static readonly List<Node> _openList = new List<Node>();

        /// <summary>
        /// 寻找从起点到终点的最短路径。
        /// </summary>
        /// <param name="start">起点网格坐标</param>
        /// <param name="end">终点网格坐标</param>
        /// <param name="excludeBuildingId">忽略的建筑ID（寻路者自身）</param>
        /// <returns>路径列表（不含起点），找不到返回空列表</returns>
        public static List<Vector2Int> FindPath(Vector2Int start, Vector2Int end,
            string excludeBuildingId = null)
        {
            if (_grid == null) _grid = GridManager.Instance;
            if (_grid == null) return new List<Vector2Int>();

            // 起点=终点
            if (start == end)
                return new List<Vector2Int> { end };

            // 终点不在边界内
            if (!_grid.IsInBounds(end))
                return new List<Vector2Int>();

            _nodeCache.Clear();
            _openList.Clear();

            var startNode = GetNode(start);
            startNode.G = 0;
            startNode.H = Heuristic(start, end);
            startNode.Parent = null;
            _openList.Add(startNode);

            while (_openList.Count > 0)
            {
                // 取 F 最小的节点
                int bestIdx = 0;
                for (int i = 1; i < _openList.Count; i++)
                {
                    if (_openList[i].F < _openList[bestIdx].F ||
                        (_openList[i].F == _openList[bestIdx].F && _openList[i].H < _openList[bestIdx].H))
                        bestIdx = i;
                }

                var current = _openList[bestIdx];

                // 到达终点
                if (current.Position == end)
                    return ReconstructPath(current);

                _openList.RemoveAt(bestIdx);
                current.Closed = true;

                // 探索邻居
                for (int i = 0; i < _directions.Length; i++)
                {
                    var neighborPos = current.Position + _directions[i];

                    // 边界检测
                    if (!_grid.IsInBounds(neighborPos)) continue;

                    // 对角线穿墙检测
                    if (i % 2 == 1) // 对角方向
                    {
                        var adj1 = current.Position + _directions[(i + 2) % 8]; // 上一个直方向
                        var adj2 = current.Position + _directions[(i + 4) % 8]; // 下一个直方向
                        if (IsBlocked(adj1, excludeBuildingId) && IsBlocked(adj2, excludeBuildingId))
                            continue;
                    }

                    // 障碍检测
                    if (IsBlocked(neighborPos, excludeBuildingId)) continue;

                    var neighbor = GetNode(neighborPos);
                    if (neighbor.Closed) continue;

                    int newG = current.G + _costs[i];
                    if (!_openList.Contains(neighbor) || newG < neighbor.G)
                    {
                        neighbor.G = newG;
                        neighbor.H = Heuristic(neighborPos, end);
                        neighbor.Parent = current;

                        if (!_openList.Contains(neighbor))
                            _openList.Add(neighbor);
                    }
                }
            }

            // 无路径
            return new List<Vector2Int>();
        }

        // ==================== 内部 ====================

        private static int Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            // Octile distance（8方向）
            return 10 * Mathf.Max(dx, dy) + 4 * Mathf.Min(dx, dy);
        }

        private static bool IsBlocked(Vector2Int pos, string excludeBuildingId)
        {
            var village = Data.DataManager.Instance?.Village;
            if (village == null) return false;

            var building = village.GetBuildingAt(pos.x, pos.y);
            if (building == null) return false;
            if (building.Id == excludeBuildingId) return false;

            return true;
        }

        private static Node GetNode(Vector2Int pos)
        {
            if (!_nodeCache.TryGetValue(pos, out var node))
            {
                node = new Node { Position = pos };
                _nodeCache[pos] = node;
            }
            return node;
        }

        private static List<Vector2Int> ReconstructPath(Node endNode)
        {
            var path = new List<Vector2Int>();
            var current = endNode;

            while (current != null)
            {
                path.Add(current.Position);
                current = current.Parent;
            }

            path.Reverse();
            path.RemoveAt(0); // 移除起点
            return path;
        }

        private class Node
        {
            public Vector2Int Position;
            public int G; // 起点到当前的代价
            public int H; // 当前到终点的启发代价
            public int F => G + H;
            public Node Parent;
            public bool Closed;
        }
    }
}
