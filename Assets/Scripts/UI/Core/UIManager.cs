using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// UI 管理器 — 跨场景单例，统一管理所有 UI 面板的打开/关闭/获取。
    /// 内部使用 UIStack 管理层级堆栈，自动分配 Canvas sortingOrder。
    /// </summary>
    public class UIManager : SingletonDontDestroy<UIManager>
    {
        [Header("UI Camera")]
        [SerializeField] private Camera _uiCamera;

        /// <summary>UI 专用相机</summary>
        public Camera UICamera => _uiCamera;

        private UIStack _stack;
        private Transform _uiRoot;
        private readonly Dictionary<UILayer, Canvas> _layerCanvases = new Dictionary<UILayer, Canvas>();
        private readonly Dictionary<UILayer, int> _layerPanelCounts = new Dictionary<UILayer, int>();
        private readonly Dictionary<Type, UIBase> _panelCache = new Dictionary<Type, UIBase>();
        private GameObject _ownedEventSystem;

        protected override void OnSingletonAwake()
        {
            Initialize();
        }

        private void Initialize()
        {
            _stack = new UIStack();
            EnsureEventSystem();
            CreateUIRoot();
            CreateLayerCanvases();

            if (GetComponent<BuildingInfoController>() == null)
                gameObject.AddComponent<BuildingInfoController>();
        }

        private void EnsureEventSystem()
        {
            EventSystem[] systems = FindObjectsOfType<EventSystem>(true);
            EventSystem primary = EventSystem.current;
            if (primary == null && systems.Length > 0)
                primary = systems[0];

            if (primary == null)
            {
                var eventObject = new GameObject("EventSystem");
                _ownedEventSystem = eventObject;
                primary = eventObject.AddComponent<EventSystem>();
                eventObject.AddComponent<StandaloneInputModule>();
            }
            else if (primary.GetComponent<BaseInputModule>() == null)
            {
                primary.gameObject.AddComponent<StandaloneInputModule>();
            }

            primary.transform.SetParent(null);
            DontDestroyOnLoad(primary.gameObject);

            foreach (EventSystem candidate in systems)
            {
                if (candidate != null && candidate != primary)
                    Destroy(candidate.gameObject);
            }
        }

        // ==================== 初始化 ====================

        private void CreateUIRoot()
        {
            _uiRoot = transform;

            // 创建 UI Camera（如果未指定）
            if (_uiCamera == null)
            {
                var cameraGO = new GameObject("UI_Camera");
                cameraGO.transform.SetParent(_uiRoot);
                _uiCamera = cameraGO.AddComponent<Camera>();
                _uiCamera.clearFlags = CameraClearFlags.Depth;
                _uiCamera.cullingMask = LayerMask.GetMask("UI");
                _uiCamera.orthographic = true;
                _uiCamera.depth = 100;
            }
        }

        private void CreateLayerCanvases()
        {
            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                var go = new GameObject($"Layer_{layer}");
                go.transform.SetParent(_uiRoot);
                go.transform.localPosition = Vector3.zero;
                go.transform.localScale = Vector3.one;
                go.layer = LayerMask.NameToLayer("UI");

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = UIDefine.GetLayerSortingOrder(layer);

                // 可选：GraphicRaycaster
                go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

                _layerCanvases[layer] = canvas;
                _layerPanelCounts[layer] = 0;
            }
        }

        // ==================== 打开面板 ====================

        /// <summary>打开指定类型的面板</summary>
        /// <typeparam name="T">面板类型</typeparam>
        /// <param name="data">可选参数，传递给 OnOpen</param>
        /// <returns>面板实例</returns>
        public T Open<T>(object data = null) where T : UIBase
        {
            var type = typeof(T);

            // 已打开面板只重绑数据；暂停/恢复由 UIStack 统一管理
            if (_panelCache.TryGetValue(type, out var existing) && existing.IsOpen)
            {
                existing.DoRebind(data);
                return existing as T;
            }

            // 实例化
            var panel = GetOrCreatePanel<T>();
            var layer = panel.Layer;

            // 设置父节点
            panel.transform.SetParent(_layerCanvases[layer].transform, false);
            panel.transform.localPosition = Vector3.zero;
            panel.transform.localScale = Vector3.one;
            if (panel.transform is RectTransform panelRect)
            {
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;
            }

            // 在 OnOpen 之前建立缓存和栈所有权，确保同步重入复用同一实例。
            _panelCache[type] = panel;
            if (!_stack.TryPush(panel))
                return null;

            // 分配 sortingOrder
            int sortingIndex = _layerPanelCounts[layer];
            panel.SortingOrder = UIDefine.GetLayerSortingOrder(layer) + sortingIndex;
            _layerPanelCounts[layer] = sortingIndex + 1;

            try
            {
                panel.DoOpen(data);
                _stack.ApplyPushEffects(panel);
                return panel;
            }
            catch
            {
                try
                {
                    _stack.Rollback(panel);
                }
                catch (Exception rollbackException)
                {
                    Debug.LogException(rollbackException);
                }

                if (_layerPanelCounts[layer] == sortingIndex + 1)
                    _layerPanelCounts[layer] = sortingIndex;
                throw;
            }
        }

        // ==================== 关闭面板 ====================

        /// <summary>关闭指定面板实例</summary>
        public void Close(UIBase panel)
        {
            if (panel == null || !panel.IsOpen) return;

            _stack.Pop(panel);
            // 保留在缓存中以便复用，避免重复创建GameObject
        }

        /// <summary>关闭指定类型的面板</summary>
        public void Close<T>() where T : UIBase
        {
            var type = typeof(T);
            if (_panelCache.TryGetValue(type, out var panel))
            {
                Close(panel);
            }
        }

        /// <summary>关闭指定层级的所有面板</summary>
        public void CloseLayer(UILayer layer)
        {
            _stack.ClearFrom(layer);
        }

        /// <summary>显示 Toast 提示（Tip 层，自动消失）</summary>
        /// <param name="message">提示文本</param>
        /// <param name="duration">显示时长（秒），默认 2 秒</param>
        public void ShowToast(string message, float duration = 2f)
        {
            Open<ToastPanel>(System.Tuple.Create(message, duration));
        }

        /// <summary>关闭所有面板</summary>
        public void CloseAll()
        {
            // 先通过 UIStack 正常关闭（触发 DoClose 生命周期）
            foreach (var kv in _panelCache)
            {
                if (kv.Value != null && kv.Value.IsOpen)
                    _stack.Pop(kv.Value);
            }
            // 然后销毁所有缓存的面板 GameObject
            foreach (var kv in _panelCache)
            {
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            }
            _panelCache.Clear();
        }

        // ==================== 获取面板 ====================

        /// <summary>获取已打开的面板实例（不触发任何生命周期）</summary>
        public T Get<T>() where T : UIBase
        {
            var type = typeof(T);
            if (_panelCache.TryGetValue(type, out var panel) && panel.IsOpen)
            {
                return panel as T;
            }
            return null;
        }

        /// <summary>获取指定层级栈顶面板</summary>
        public UIBase GetTop(UILayer layer)
        {
            return _stack.Peek(layer);
        }

        /// <summary>面板是否已打开</summary>
        public bool IsOpen<T>() where T : UIBase
        {
            return Get<T>() != null;
        }

        // ==================== 内部 ====================

        private T GetOrCreatePanel<T>() where T : UIBase
        {
            var type = typeof(T);
            var path = UIDefine.UIPrefabPath + type.Name;

            T panel;

            // 尝试从缓存获取（关闭但未销毁的）
            if (_panelCache.TryGetValue(type, out var cached) && !cached.IsOpen)
            {
                panel = cached as T;
            }
            // 尝试从 Resources 加载 Prefab
            else
            {
                var prefab = Resources.Load<GameObject>(path);
                if (prefab != null)
                {
                    var go = Instantiate(prefab);
                    panel = go.GetComponent<T>();
                    if (panel == null)
                        panel = go.AddComponent<T>();
                }
                else
                {
                    // 无 Prefab 时动态创建
                    var go = new GameObject(type.Name);
                    panel = go.AddComponent<T>();
                }
            }

            return panel;
        }

        /// <summary>获取层级的 Canvas（供扩展使用）</summary>
        public Canvas GetLayerCanvas(UILayer layer)
        {
            _layerCanvases.TryGetValue(layer, out var canvas);
            return canvas;
        }

        protected override void OnDestroy()
        {
            if (_ownedEventSystem != null)
            {
                Destroy(_ownedEventSystem);
                _ownedEventSystem = null;
            }
            base.OnDestroy();
        }
    }
}
