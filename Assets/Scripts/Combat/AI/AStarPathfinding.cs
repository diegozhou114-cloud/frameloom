using System;
using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// A* 网格寻路 — 实例类，每场战斗独立实例，无跨战斗污染。
    /// 8 方向移动，对角线穿墙检测。
    /// </summary>
    public class AStarPathfinding
    {
        private static readonly Vector2Int[] _directions = {
            new(0,1), new(1,1), new(1,0), new(1,-1), new(0,-1), new(-1,-1), new(-1,0), new(-1,1)
        };
        private static readonly int[] _costs = { 10, 14, 10, 14, 10, 14, 10, 14 };

        private readonly GridManager _grid;
        private readonly Dictionary<Vector2Int, Node> _nodeCache = new();
        private readonly List<Node> _openList = new();

        public AStarPathfinding(GridManager grid) { _grid = grid; }

        public List<Vector2Int> FindPath(Vector2Int start, Vector2Int end,
            string excludeBuildingId = null)
        {
            if (_grid == null) return new List<Vector2Int>();
            if (start == end) return new List<Vector2Int> { end };
            if (!_grid.IsInBounds(end)) return new List<Vector2Int>();

            _nodeCache.Clear();
            _openList.Clear();

            var startNode = GetNode(start);
            startNode.G = 0;
            startNode.H = Heuristic(start, end);
            startNode.Parent = null;
            _openList.Add(startNode);

            while (_openList.Count > 0)
            {
                int bestIdx = 0;
                for (int i = 1; i < _openList.Count; i++)
                    if (_openList[i].F < _openList[bestIdx].F ||
                        (_openList[i].F == _openList[bestIdx].F && _openList[i].H < _openList[bestIdx].H))
                        bestIdx = i;

                var current = _openList[bestIdx];
                if (current.Position == end) return ReconstructPath(current);

                _openList.RemoveAt(bestIdx);
                current.Closed = true;

                for (int i = 0; i < _directions.Length; i++)
                {
                    var neighborPos = current.Position + _directions[i];
                    if (!_grid.IsInBounds(neighborPos)) continue;

                    if (i % 2 == 1)
                    {
                        var adj1 = current.Position + _directions[(i + 2) % 8];
                        var adj2 = current.Position + _directions[(i + 4) % 8];
                        if (IsBlocked(adj1, excludeBuildingId) && IsBlocked(adj2, excludeBuildingId))
                            continue;
                    }

                    if (IsBlocked(neighborPos, excludeBuildingId)) continue;

                    var neighbor = GetNode(neighborPos);
                    if (neighbor.Closed) continue;

                    int newG = current.G + _costs[i];
                    if (!_openList.Contains(neighbor) || newG < neighbor.G)
                    {
                        neighbor.G = newG;
                        neighbor.H = Heuristic(neighborPos, end);
                        neighbor.Parent = current;
                        if (!_openList.Contains(neighbor)) _openList.Add(neighbor);
                    }
                }
            }

            return new List<Vector2Int>();
        }

        private static int Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
            return 10 * Mathf.Max(dx, dy) + 4 * Mathf.Min(dx, dy);
        }

        private bool IsBlocked(Vector2Int pos, string excludeBuildingId)
        {
            var village = Data.DataManager.Instance?.Village;
            if (village == null) return false;
            var building = village.GetBuildingAt(pos.x, pos.y);
            return building != null && building.Id != excludeBuildingId;
        }

        private Node GetNode(Vector2Int pos)
        {
            if (!_nodeCache.TryGetValue(pos, out var node))
                _nodeCache[pos] = node = new Node { Position = pos };
            return node;
        }

        private static List<Vector2Int> ReconstructPath(Node endNode)
        {
            var path = new List<Vector2Int>();
            var current = endNode;
            while (current != null) { path.Add(current.Position); current = current.Parent; }
            path.Reverse();
            path.RemoveAt(0);
            return path;
        }

        private class Node
        {
            public Vector2Int Position;
            public int G, H, F => G + H;
            public Node Parent;
            public bool Closed;
        }
    }
}
