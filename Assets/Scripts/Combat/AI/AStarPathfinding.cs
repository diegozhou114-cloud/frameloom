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
        private readonly BinaryHeap _openHeap = new();

        public AStarPathfinding(GridManager grid) { _grid = grid; }

        public List<Vector2Int> FindPath(Vector2Int start, Vector2Int end,
            string excludeBuildingId = null)
        {
            if (_grid == null) return new List<Vector2Int>();
            if (start == end) return new List<Vector2Int> { end };
            if (!_grid.IsInBounds(end)) return new List<Vector2Int>();

            _nodeCache.Clear();
            _openHeap.Clear();

            var startNode = GetNode(start);
            startNode.G = 0;
            startNode.H = Heuristic(start, end);
            startNode.Parent = null;
            _openHeap.Push(startNode);

            while (_openHeap.Count > 0)
            {
                var current = _openHeap.Pop();
                if (current.Position == end) return ReconstructPath(current);

                current.Closed = true;

                for (int i = 0; i < _directions.Length; i++)
                {
                    var neighborPos = current.Position + _directions[i];
                    if (!_grid.IsInBounds(neighborPos)) continue;

                    // 对角线穿墙检测：检查两个相邻的 cardinal 方向
                    if (i % 2 == 1)
                    {
                        // 正确的 cardinal 邻居索引：方向 (i+7)%8 和 (i+1)%8 总是指向相邻的正方向
                        var adj1 = current.Position + _directions[(i + 7) % 8];
                        var adj2 = current.Position + _directions[(i + 1) % 8];
                        if (IsBlocked(adj1, excludeBuildingId) && IsBlocked(adj2, excludeBuildingId))
                            continue;
                    }

                    if (IsBlocked(neighborPos, excludeBuildingId)) continue;

                    var neighbor = GetNode(neighborPos);
                    if (neighbor.Closed) continue;

                    int newG = current.G + _costs[i];
                    if (!neighbor.InOpen || newG < neighbor.G)
                    {
                        neighbor.G = newG;
                        neighbor.H = Heuristic(neighborPos, end);
                        neighbor.Parent = current;
                        if (!neighbor.InOpen)
                        {
                            neighbor.InOpen = true;
                            _openHeap.Push(neighbor);
                        }
                        else
                        {
                            _openHeap.Update(neighbor);
                        }
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
            public int G;
            public int H;
            public int F => G + H;
            public Node Parent;
            public bool Closed;
            public bool InOpen;     // 是否在 open heap 中（替代 Contains 的 O(N) 查找）
            public int HeapIndex = -1; // 在堆中的位置（用于 Update）
        }

        /// <summary>最小二叉堆 — O(log N) 插入/弹出，支持更新优先级</summary>
        private class BinaryHeap
        {
            private readonly List<Node> _items = new();
            public int Count => _items.Count;

            public void Clear() => _items.Clear();

            public void Push(Node node)
            {
                node.HeapIndex = _items.Count;
                _items.Add(node);
                SiftUp(_items.Count - 1);
            }

            public Node Pop()
            {
                var result = _items[0];
                result.HeapIndex = -1;
                result.InOpen = false;  // 标记已出堆，防止被误认为仍在堆中

                int last = _items.Count - 1;
                if (last > 0)
                {
                    _items[0] = _items[last];
                    _items[0].HeapIndex = 0;
                    _items.RemoveAt(last);
                    SiftDown(0);
                }
                else
                {
                    _items.RemoveAt(last);
                }
                return result;
            }

            public void Update(Node node)
            {
                // 节点的 G 减小了，可能需要上浮
                SiftUp(node.HeapIndex);
            }

            private void SiftUp(int idx)
            {
                while (idx > 0)
                {
                    int parent = (idx - 1) / 2;
                    if (Compare(_items[idx], _items[parent]) >= 0) break;
                    Swap(idx, parent);
                    idx = parent;
                }
            }

            private void SiftDown(int idx)
            {
                while (true)
                {
                    int left = idx * 2 + 1;
                    int right = idx * 2 + 2;
                    int smallest = idx;

                    if (left < _items.Count && Compare(_items[left], _items[smallest]) < 0)
                        smallest = left;
                    if (right < _items.Count && Compare(_items[right], _items[smallest]) < 0)
                        smallest = right;

                    if (smallest == idx) break;
                    Swap(idx, smallest);
                    idx = smallest;
                }
            }

            private void Swap(int a, int b)
            {
                (_items[a], _items[b]) = (_items[b], _items[a]);
                _items[a].HeapIndex = a;
                _items[b].HeapIndex = b;
            }

            private static int Compare(Node a, Node b)
            {
                int cmp = a.F.CompareTo(b.F);
                if (cmp != 0) return cmp;
                return a.H.CompareTo(b.H); // F 相同按 H 排序
            }
        }
    }
}
