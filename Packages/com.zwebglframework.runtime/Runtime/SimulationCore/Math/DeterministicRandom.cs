using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 确定性随机数生成器 — SplitMix64 算法。
    /// 给定相同 seed，序列完全可复现，跨平台一致。
    /// </summary>
    public class DeterministicRandom
    {
        private ulong _state;

        /// <param name="seed">初始种子。相同 seed 产生相同序列。</param>
        public DeterministicRandom(int seed)
        {
            _state = (ulong)seed;
            // 避免 seed=0 导致的零序列问题：初始搅动一次
            NextULong();
        }

        /// <summary>返回 [0, int.MaxValue) 范围的随机整数</summary>
        public int Next()
        {
            return (int)(NextULong() & 0x7FFFFFFF);
        }

        /// <summary>返回 [0, max) 范围的随机整数</summary>
        public int Next(int max)
        {
            if (max <= 0) return 0;
            // NOTE: Modulo bias — when max does not divide 2^64 evenly, smaller values are slightly
            // over-represented. For uniform distribution, use rejection sampling: repeatedly draw
            // NextULong() until the result is below the largest multiple of max, then return % max.
            return (int)(NextULong() % (ulong)max);
        }

        /// <summary>返回 [min, max) 范围的随机整数</summary>
        public int Next(int min, int max)
        {
            if (min >= max) return min;
            ulong range = (ulong)((long)max - min);
            long offset = (long)(NextULong() % range);
            return (int)((long)min + offset);
        }

        /// <summary>返回 [0, 1) 范围的概率值（以 basis points 表示，0~9999）</summary>
        public int NextProbability()
        {
            return Next(10000);
        }

        /// <summary>概率判定：basisPoints 概率返回 true（如 3000 = 30%）</summary>
        public bool RollChance(int basisPoints)
        {
            return NextProbability() < basisPoints;
        }

        /// <summary>将内部状态重置为指定 seed</summary>
        public void Reset(int seed)
        {
            _state = (ulong)seed;
            NextULong(); // 初始搅动
        }

        // ==================== SplitMix64 核心 ====================

        private ulong NextULong()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
