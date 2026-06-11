using System;
using System.Collections;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 协程管理器 — 非 MonoBehaviour 类也能启动协程。
    /// 跨场景单例，提供延时回调、逐帧回调等便捷方法。
    /// </summary>
    public class CoroutineManager : SingletonDontDestroy<CoroutineManager>
    {
        /// <summary>启动一个协程</summary>
        public new static Coroutine StartCoroutine(IEnumerator routine)
        {
            return (Instance as CoroutineManager)?.StartCoroutineInternal(routine);
        }

        /// <summary>停止一个协程</summary>
        public new static void StopCoroutine(Coroutine routine)
        {
            if (Instance != null && routine != null)
            {
                (Instance as CoroutineManager)?.StopCoroutineInternal(routine);
            }
        }

        /// <summary>停止所有协程</summary>
        public static void StopAll()
        {
            if (Instance != null)
            {
                (Instance as CoroutineManager)?.StopAllCoroutinesInternal();
            }
        }

        private Coroutine StartCoroutineInternal(IEnumerator routine)
        {
            return ((MonoBehaviour)this).StartCoroutine(routine);
        }

        private void StopCoroutineInternal(Coroutine routine)
        {
            ((MonoBehaviour)this).StopCoroutine(routine);
        }

        private void StopAllCoroutinesInternal()
        {
            ((MonoBehaviour)this).StopAllCoroutines();
        }

        // --- 便捷方法 ---

        /// <summary>延时 seconds 秒后执行回调</summary>
        public static Coroutine Delay(float seconds, Action callback)
        {
            return StartCoroutine(DelayRoutine(seconds, callback));
        }

        /// <summary>下一帧执行回调</summary>
        public static Coroutine NextFrame(Action callback)
        {
            return StartCoroutine(NextFrameRoutine(callback));
        }

        /// <summary>每帧执行回调，直到 predicate 返回 true</summary>
        public static Coroutine WaitUntil(Func<bool> predicate, Action onComplete = null)
        {
            return StartCoroutine(WaitUntilRoutine(predicate, onComplete));
        }

        // --- 内部协程 ---

        private static IEnumerator DelayRoutine(float seconds, Action callback)
        {
            yield return new WaitForSeconds(seconds);
            callback?.Invoke();
        }

        private static IEnumerator NextFrameRoutine(Action callback)
        {
            yield return null;
            callback?.Invoke();
        }

        private static IEnumerator WaitUntilRoutine(Func<bool> predicate, Action onComplete)
        {
            while (predicate != null && !predicate())
            {
                yield return null;
            }
            onComplete?.Invoke();
        }
    }
}
