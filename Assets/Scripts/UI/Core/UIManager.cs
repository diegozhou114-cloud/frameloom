using System;
using System.Collections.Generic;
using UnityEngine;
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

        protected override void Awake()
        {
            base.Awake();
            Initialize();
        }

        private void Initialize()
        {
            _stack = new UIStack();
            CreateUIRoot();
            CreateLayerCanvases();

            if (GetComponent<BuildingInfoController>() == null)
                gameObject.AddComponent<BuildingInfoController>();
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
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _uiCamera;
                canvas.sortingOrder = UIDefine.GetLayerSortingOrder(layer);
                canvas.planeDistance = 10;

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

            // 如果已打开且在同一层，恢复并传递新数据
            if (_panelCache.TryGetValue(type, out var existing) && existing.IsOpen)
            {
                existing.DoResume(data);
                return existing as T;
            }

            // 实例化
            var panel = GetOrCreatePanel<T>();
            var layer = panel.Layer;

            // 设置父节点
            panel.transform.SetParent(_layerCanvases[layer].transform, false);
            panel.transform.localPosition = Vector3.zero;
            panel.transform.localScale = Vector3.one;

            // 分配 sortingOrder
            panel.SortingOrder = UIDefine.GetLayerSortingOrder(layer) + _layerPanelCounts[layer];
            _layerPanelCounts[layer]++;

            // 打开
            panel.DoOpen(data);
            _stack.Push(panel);
            _panelCache[type] = panel;

            return panel;
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
            // 清理缓存
            var toRemove = new List<Type>();
            foreach (var kv in _panelCache)
            {
                if (kv.Value.Layer >= layer)
                    toRemove.Add(kv.Key);
            }
            foreach (var key in toRemove)
            {
                _panelCache.Remove(key);
            }
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
            foreach (var kv in _panelCache)
            {
                if (kv.Value != null && kv.Value.IsOpen)
                    kv.Value.DoClose();
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            }
            _stack.Clear();
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
    }
}
