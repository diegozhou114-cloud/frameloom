using UnityEngine;
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

        /// <summary>面板恢复时调用（上层面板关闭）</summary>
        internal void DoResume()
        {
            if (!IsOpen || !IsPaused) return;

            IsPaused = false;
            CanvasGroup.interactable = true;
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
    }
}
