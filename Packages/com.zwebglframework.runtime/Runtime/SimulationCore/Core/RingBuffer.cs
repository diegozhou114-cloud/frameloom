using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 固定大小环形缓冲 — 用于 FrameSync 快照存储。
    /// 自动覆写最旧的条目，O(1) 索引访问。
    /// </summary>
    public class RingBuffer<T>
    {
        private readonly T[] _items;
        private int _head;  // 最早条目索引
        private int _count;

        public int Capacity { get; }
        public int Count => _count;

        public RingBuffer(int capacity)
        {
            Capacity = capacity;
            _items = new T[capacity];
        }

        /// <summary>添加条目，容量满时覆写最旧的</summary>
        public void Push(T item)
        {
            int index = (_head + _count) % Capacity;
            _items[index] = item;
            if (_count < Capacity)
                _count++;
            else
                _head = (_head + 1) % Capacity;
        }

        /// <summary>按逻辑索引访问（0 = 最旧, Count-1 = 最新）</summary>
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new IndexOutOfRangeException();
                return _items[(_head + index) % Capacity];
            }
        }

        /// <summary>获取最新的条目</summary>
        public T Latest => _count > 0 ? this[_count - 1] : default;

        /// <summary>获取最旧的条目</summary>
        public T Oldest => _count > 0 ? this[0] : default;

        /// <summary>清空所有条目</summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
            Array.Clear(_items, 0, _items.Length);
        }
    }
}
