using System;
using System.Threading.Tasks;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 异步管理器 — 提供 UniTask 兼容的异步 API（纯 Task 实现）。
    /// 提升到 Core 命名空间，全局可用。
    /// 后续替换为 UniTask：全局替换 Task → UniTask 即可。
    /// </summary>
    public static class AsyncManager
    {
        public static async Task Delay(int milliseconds) => await Task.Delay(milliseconds);
        public static async Task DelaySeconds(float seconds) => await Task.Delay((int)(seconds * 1000));

        public static async Task WaitUntil(Func<bool> predicate, int checkIntervalMs = 50)
        {
            while (!predicate()) await Task.Delay(checkIntervalMs);
        }

        public static async Task WaitUntilNextFrame(Func<bool> predicate)
        {
            while (!predicate()) await Task.Yield();
        }

        public static async Task NextFrame() => await Task.Yield();
        public static async Task WhenAll(params Task[] tasks) => await Task.WhenAll(tasks);
        public static async Task<Task> WhenAny(params Task[] tasks) => await Task.WhenAny(tasks);
    }
}
