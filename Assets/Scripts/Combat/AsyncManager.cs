using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 异步管理器 — 提供 UniTask 兼容的异步 API。
    /// 纯 Task 实现，后续替换 UniTask 只需全局替换类型名。
    /// 
    /// API 对齐 UniTask：
    ///   UniTask.Delay(1000)        → AsyncManager.Delay(1000)
    ///   UniTask.WaitUntil(()=>..)  → AsyncManager.WaitUntil(()=>..)
    ///   UniTask.WhenAll(tasks)     → AsyncManager.WhenAll(tasks)
    /// </summary>
    public static class AsyncManager
    {
        /// <summary>延迟指定毫秒</summary>
        public static async Task Delay(int milliseconds)
        {
            await Task.Delay(milliseconds);
        }

        /// <summary>延迟指定秒数</summary>
        public static async Task DelaySeconds(float seconds)
        {
            await Task.Delay((int)(seconds * 1000));
        }

        /// <summary>等待条件为真</summary>
        public static async Task WaitUntil(Func<bool> predicate, int checkIntervalMs = 50)
        {
            while (!predicate())
            {
                await Task.Delay(checkIntervalMs);
            }
        }

        /// <summary>等待条件为真（每帧检查，主线程安全）</summary>
        public static async Task WaitUntilNextFrame(Func<bool> predicate)
        {
            while (!predicate())
            {
                await Task.Yield();
            }
        }

        /// <summary>等待一帧</summary>
        public static async Task NextFrame()
        {
            await Task.Yield();
        }

        /// <summary>并行等待所有任务</summary>
        public static async Task WhenAll(params Task[] tasks)
        {
            await Task.WhenAll(tasks);
        }

        /// <summary>并行等待任意一个完成</summary>
        public static async Task<Task> WhenAny(params Task[] tasks)
        {
            return await Task.WhenAny(tasks);
        }
    }
}
