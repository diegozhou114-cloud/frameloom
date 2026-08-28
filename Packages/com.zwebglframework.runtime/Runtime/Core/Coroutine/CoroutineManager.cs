using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 统一协程管理器 — 非MonoBehaviour代码可通过静态方法启动/停止协程。
    /// 挂载到跨场景单例，提供全局协程宿主。
    /// 与 AsyncManager 互补：AsyncManager 处理 UniTask，CoroutineManager 处理 IEnumerator。
    /// </summary>
    public class CoroutineManager : SingletonDontDestroy<CoroutineManager>
    {
        /// <summary>启动协程（可从任何上下文调用）</summary>
        public static new Coroutine StartCoroutine(IEnumerator routine)
        {
            return ((MonoBehaviour)Instance).StartCoroutine(routine);
        }

        /// <summary>停止协程</summary>
        public static new void StopCoroutine(Coroutine routine)
        {
            if (routine != null && Instance != null)
                ((MonoBehaviour)Instance).StopCoroutine(routine);
        }

        /// <summary>停止所有协程</summary>
        public static void StopAll()
        {
            if (HasInstance)
                Instance.StopAllCoroutines();
        }

        /// <summary>延迟执行（回调方式）</summary>
        public static Coroutine Delay(float seconds, Action onComplete)
        {
            return StartCoroutine(DelayRoutine(seconds, onComplete));
        }

        /// <summary>每帧执行直到 predicate 返回 true</summary>
        public static Coroutine WaitUntil(Func<bool> predicate, Action onComplete)
        {
            return StartCoroutine(WaitUntilRoutine(predicate, onComplete));
        }

        /// <summary>将 IEnumerator 转为 UniTask 等待</summary>
        public static async UniTask ToUniTask(IEnumerator routine)
        {
            await routine.ToUniTask(Instance);
        }

        // ==================== 内部例程 ====================

        private static IEnumerator DelayRoutine(float seconds, Action onComplete)
        {
            yield return new WaitForSeconds(seconds);
            onComplete?.Invoke();
        }

        private static IEnumerator WaitUntilRoutine(Func<bool> predicate, Action onComplete)
        {
            float elapsed = 0f;
            const float defaultTimeout = 30f;
            while (!predicate())
            {
                elapsed += Time.unscaledDeltaTime;
                if (elapsed > defaultTimeout)
                {
                    Debug.LogWarning($"[CoroutineManager] WaitUntil 超时 ({defaultTimeout}s)");
                    yield break;
                }
                yield return null;
            }
            onComplete?.Invoke();
        }
    }
}
