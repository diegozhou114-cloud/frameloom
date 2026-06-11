using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 跨场景单例基类 — DontDestroyOnLoad，切换场景不销毁。
    /// 用法：public class AudioManager : SingletonDontDestroy&lt;AudioManager&gt; { }
    /// </summary>
    public abstract class SingletonDontDestroy<T> : MonoBehaviour where T : SingletonDontDestroy<T>
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
                                DontDestroyOnLoad(go);
                            }
                        }
                    }
                }

                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this as T;
            DontDestroyOnLoad(gameObject);
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
