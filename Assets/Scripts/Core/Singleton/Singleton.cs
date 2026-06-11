using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 普通单例基类 — 非跨场景，场景销毁时自动清理。
    /// 用法：public class MyManager : Singleton&lt;MyManager&gt; { }
    /// </summary>
    public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
    {
        private static T _instance;
        private static readonly object _lock = new object();

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = FindObjectOfType<T>();

                            if (_instance == null)
                            {
                                var go = new GameObject(typeof(T).Name);
                                _instance = go.AddComponent<T>();
                            }
                        }
                    }
                }

                return _instance;
            }
        }

        /// <summary>是否已实例化（不触发懒加载）</summary>
        public static bool HasInstance => _instance != null;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this as T;
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
