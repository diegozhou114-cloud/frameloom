using System;
using Cysharp.Threading.Tasks;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 异步管理器 — UniTask 实现，提供 Unity 主线程安全的异步 API。
    /// 支持 PlayerLoopTiming 调度，无 GC 分配。
    /// </summary>
    public static class AsyncManager
    {
        /// <summary>延迟指定毫秒</summary>
        public static UniTask Delay(int milliseconds)
            => UniTask.Delay(milliseconds);

        /// <summary>延迟指定秒数</summary>
        public static UniTask DelaySeconds(float seconds)
            => UniTask.Delay(TimeSpan.FromSeconds(seconds));

        /// <summary>逐帧等待直到条件成立</summary>
        public static async UniTask WaitUntil(Func<bool> predicate,
            PlayerLoopTiming timing = PlayerLoopTiming.Update)
        {
            await UniTask.WaitUntil(predicate, timing);
        }

        /// <summary>等待下一帧</summary>
        public static async UniTask NextFrame(PlayerLoopTiming timing = PlayerLoopTiming.Update)
        {
            await UniTask.Yield(timing);
        }

        /// <summary>所有任务完成</summary>
        public static UniTask WhenAll(params UniTask[] tasks)
            => UniTask.WhenAll(tasks);

        /// <summary>任意任务完成（返回完成任务的索引）</summary>
        public static async UniTask<int> WhenAny(params UniTask[] tasks)
        {
            int index = await UniTask.WhenAny(tasks);
            return index;
        }

        /// <summary>延迟到指定帧结束</summary>
        public static async UniTask EndOfFrame()
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
        }

        /// <summary>切换到后台线程</summary>
        public static async UniTask SwitchToThreadPool()
        {
            await UniTask.SwitchToThreadPool();
        }

        /// <summary>切换回主线程</summary>
        public static async UniTask SwitchToMainThread()
        {
            await UniTask.SwitchToMainThread();
        }
    }
}
