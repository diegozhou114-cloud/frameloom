using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 确定性 A* 网格寻路 — 零 Unity 依赖。
    /// 通过回调函数获取单元格是否可通行，通过多级比较键确保确定性。
    /// </summary>
    public class IntAStarPathfinding
    {
        private static readonly (int dx, int dy)[] _directions = {
            (0,1), (1,1), (1,0), (1,-1), (0,-1), (-1,-1), (-1,0), (-1,1)
        };
        private static readonly int[] _costs = { 10, 14, 10, 14, 10, 14, 10, 14 };

        private readonly int _gridWidth, _gridHeight;
        private readonly Func<int, int, bool> _isWalkable;
        private readonly Dictionary<ulong, Node> _nodeCache = new();
        private readonly BinaryHeap _openHeap = new();

        /// <param name="gridWidth">网格宽度</param>
        /// <param name="gridHeight">网格高度</param>
        /// <param name="isWalkable">回调：(gridX, gridY) => 是否可通行</param>
        public IntAStarPathfinding(int gridWidth, int gridHeight, Func<int, int, bool> isWalkable)
        {
            _gridWidth = gridWidth;
            _gridHeight = gridHeight;
            _isWalkable = isWalkable;
        }

        /// <summary>寻路，返回网格坐标列表（不含起点）</summary>
        public List<(int x, int y)> FindPath(int startX, int startY, int endX, int endY)
        {
            if (!InBounds(endX, endY)) return new List<(int, int)>();
            if (startX == endX && startY == endY) return new List<(int, int)> { (endX, endY) };

            _nodeCache.Clear();
            _openHeap.Clear();

            var startNode = GetNode(startX, startY);
            startNode.G = 0;
            startNode.H = Heuristic(startX, startY, endX, endY);
            startNode.Parent = null;
            _openHeap.Push(startNode);

            while (_openHeap.Count > 0)
            {
                var current = _openHeap.Pop();
                if (current.X == endX && current.Y == endY)
                    return ReconstructPath(current);

                current.Closed = true;

                for (int i = 0; i < _directions.Length; i++)
                {
                    (int dx, int dy) = _directions[i];
                    int nx = current.X + dx;
                    int ny = current.Y + dy;

                    if (!InBounds(nx, ny)) continue;

                    // 对角线穿墙检测
                    if (i % 2 == 1)
                    {
                        int adj1Idx = (i + 7) % 8;
                        int adj2Idx = (i + 1) % 8;
                        int adj1x = current.X + _directions[adj1Idx].dx;
                        int adj1y = current.Y + _directions[adj1Idx].dy;
                        int adj2x = current.X + _directions[adj2Idx].dx;
                        int adj2y = current.Y + _directions[adj2Idx].dy;
                        if (!_isWalkable(adj1x, adj1y) || !_isWalkable(adj2x, adj2y))
                            continue;
                    }

                    if (!_isWalkable(nx, ny)) continue;

                    var neighbor = GetNode(nx, ny);
                    if (neighbor.Closed) continue;

                    int newG = current.G + _costs[i];
                    if (!neighbor.InOpen || newG < neighbor.G)
                    {
                        neighbor.G = newG;
                        neighbor.H = Heuristic(nx, ny, endX, endY);
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

            return new List<(int, int)>();
        }

        private bool InBounds(int x, int y) => x >= 0 && x < _gridWidth && y >= 0 && y < _gridHeight;

        private static int Heuristic(int ax, int ay, int bx, int by)
        {
            int dx = Math.Abs(ax - bx);
            int dy = Math.Abs(ay - by);
            return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy);
        }

        private Node GetNode(int x, int y)
        {
            ulong key = ((ulong)(uint)x << 32) | (uint)y;
            if (!_nodeCache.TryGetValue(key, out var node))
                _nodeCache[key] = node = new Node { X = x, Y = y };
            return node;
        }

        private static List<(int, int)> ReconstructPath(Node endNode)
        {
            var path = new List<(int, int)>();
            var current = endNode;
            while (current != null)
            {
                path.Add((current.X, current.Y));
                current = current.Parent;
            }
            path.Reverse();
            path.RemoveAt(0); // 移除起点
            return path;
        }

        private class Node
        {
            public int X, Y;
            public int G, H;
            public int F => G + H;
            public Node Parent;
            public bool Closed;
            public bool InOpen;
            public int HeapIndex = -1;
        }

        /// <summary>最小二叉堆 — 多级比较键确保确定性（F→H→X→Y）</summary>
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
                result.InOpen = false;
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

            public void Update(Node node) => SiftUp(node.HeapIndex);

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
                    int left = idx * 2 + 1, right = idx * 2 + 2, smallest = idx;
                    if (left < _items.Count && Compare(_items[left], _items[smallest]) < 0) smallest = left;
                    if (right < _items.Count && Compare(_items[right], _items[smallest]) < 0) smallest = right;
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

            /// <summary>多级比较：F → H → X → Y，确保完全确定性</summary>
            private static int Compare(Node a, Node b)
            {
                int cmp = a.F.CompareTo(b.F);
                if (cmp != 0) return cmp;
                cmp = a.H.CompareTo(b.H);
                if (cmp != 0) return cmp;
                cmp = a.X.CompareTo(b.X);
                if (cmp != 0) return cmp;
                return a.Y.CompareTo(b.Y);
            }
        }
    }
}
