using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// UI 面板基类 — 所有 UI 界面的父类。
    /// 提供完整生命周期：OnOpen → (OnPause ⇄ OnResume) → OnClose
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIBase : MonoBehaviour
    {
        [Header("面板设置")]
        [SerializeField] protected UILayer _layer = UILayer.Normal;
        [SerializeField] protected bool _pauseLowerPanels = true;

        /// <summary>面板名称（用于查找和关闭）</summary>
        public string PanelName => GetType().Name;

        /// <summary>所属层级</summary>
        public UILayer Layer => _layer;

        /// <summary>打开时是否暂停下层面板</summary>
        public bool PauseLowerPanels => _pauseLowerPanels;

        /// <summary>当前是否处于打开状态</summary>
        public bool IsOpen { get; private set; }

        /// <summary>是否被暂停（被上层面板遮挡）</summary>
        public bool IsPaused { get; private set; }

        /// <summary>关联的 Canvas</summary>
        public Canvas Canvas { get; private set; }

        /// <summary>关联的 CanvasGroup（用于动画和交互控制）</summary>
        public CanvasGroup CanvasGroup { get; private set; }

        private UIAnimation _animation;

        /// <summary>打开时传入的数据</summary>
        public object OpenData { get; private set; }

        /// <summary>面板的 sortingOrder（由 UIManager 分配）</summary>
        public int SortingOrder
        {
            get => Canvas != null ? Canvas.sortingOrder : 0;
            set
            {
                if (Canvas != null)
                    Canvas.sortingOrder = value;
            }
        }

        protected virtual void Awake()
        {
            Canvas = GetComponent<Canvas>();
            if (Canvas == null)
                Canvas = gameObject.AddComponent<Canvas>();

            CanvasGroup = GetComponent<CanvasGroup>();
            if (CanvasGroup == null)
                CanvasGroup = gameObject.AddComponent<CanvasGroup>();

            _animation = GetComponent<UIAnimation>();

            // 默认隐藏
            gameObject.SetActive(false);
        }

        // ==================== 生命周期（由 UIManager 调用） ====================

        /// <summary>面板打开时调用</summary>
        internal void DoOpen(object data)
        {
            if (IsOpen) return;

            OpenData = data;
            gameObject.SetActive(true);
            IsOpen = true;
            IsPaused = false;

            if (_animation != null)
            {
                _animation.PlayOpen(() => OnOpen(data));
            }
            else
            {
                OnOpen(data);
            }
        }

        /// <summary>面板暂停时调用（被上层面板遮挡）</summary>
        internal void DoPause()
        {
            if (!IsOpen || IsPaused) return;

            IsPaused = true;
            CanvasGroup.interactable = false;
            OnPause();
        }

        /// <summary>面板恢复时调用（上层面板关闭），可携带新数据</summary>
        internal void DoResume(object data = null)
        {
            if (!IsOpen || !IsPaused) return;

            IsPaused = false;
            CanvasGroup.interactable = true;
            if (data != null)
            {
                OpenData = data;
                OnOpen(data); // 传递新数据
            }
            OnResume();
        }

        /// <summary>面板关闭时调用</summary>
        internal void DoClose()
        {
            if (!IsOpen) return;

            IsOpen = false;
            IsPaused = false;

            if (_animation != null)
            {
                _animation.PlayClose(() =>
                {
                    OnClose();
                    gameObject.SetActive(false);
                    OpenData = null;
                });
            }
            else
            {
                OnClose();
                gameObject.SetActive(false);
                OpenData = null;
            }
        }

        // ==================== 子类重写 ====================

        /// <summary>面板打开 — 初始化 UI、注册事件</summary>
        protected virtual void OnOpen(object data) { }

        /// <summary>面板被暂停 — 停止刷新、取消事件监听</summary>
        protected virtual void OnPause() { }

        /// <summary>面板恢复 — 重新刷新数据</summary>
        protected virtual void OnResume() { }

        /// <summary>面板关闭 — 清理资源、反注册事件</summary>
        protected virtual void OnClose() { }

        // ==================== 公共方法 ====================

        /// <summary>关闭当前面板（委托给 UIManager）</summary>
        public void Close()
        {
            UIManager.Instance.Close(this);
        }

        /// <summary>通过 EventBus 发送 UI 事件（便捷方法）</summary>
        protected void SendUIEvent(string eventKey)
        {
            EventBus.Publish(eventKey);
        }

        protected void SendUIEvent<T>(string eventKey, T arg)
        {
            EventBus.Publish(eventKey, arg);
        }

        // ==================== UI 快捷构造（子类复用） ====================

        /// <summary>创建全屏半透明/纯色背景条</summary>
        protected GameObject MakeBar(string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            go.GetComponent<Image>().color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        /// <summary>创建 Text 控件（锚点定位）</summary>
        protected Text CreateText(string name, string text, int fontSize, Vector2 anchor,
            Transform parent, TextAnchor align, Color color, float w = 200, float h = 28)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = align;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            txt.rectTransform.sizeDelta = new Vector2(w, h);
            return txt;
        }

        /// <summary>创建按钮（锚点区域定位，anchorMin↔anchorMax）</summary>
        protected Button CreateButton(string name, string label, Vector2 anchorMin, Vector2 anchorMax,
            Transform parent, Color color, UnityEngine.Events.UnityAction onClick, int fontSize = 18)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lt = labelGo.AddComponent<Text>();
            lt.text = label;
            lt.fontSize = fontSize;
            lt.color = Color.white;
            lt.alignment = TextAnchor.MiddleCenter;
            lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lt.rectTransform.anchorMin = Vector2.zero;
            lt.rectTransform.anchorMax = Vector2.one;
            return go.GetComponent<Button>();
        }
    }
}
