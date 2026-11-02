using System;

namespace ZWebGLFrameWork.UI
{
    public abstract partial class UIBase
    {
        // ==================== 生命周期（由 UIManager 调用） ====================

        /// <summary>面板打开时调用</summary>
        internal void DoOpen(object data)
        {
            if (IsOpen) return;

            int openVersion = ++_lifecycleVersion;
            OpenData = data;
            gameObject.SetActive(true);
            IsOpen = true;
            IsPaused = false;
            CanvasGroup.interactable = true;

            if (_animation != null)
            {
                _animation.PlayOpen(() => CompleteOpen(openVersion, data));
            }
            else
            {
                CompleteOpen(openVersion, data);
            }
        }

        private void CompleteOpen(int openVersion, object data)
        {
            if (_lifecycleVersion != openVersion || !IsOpen) return;
            try { OnOpen(data); }
            catch
            {
                if (_lifecycleVersion == openVersion) ReleaseOpenSubscriptions();
                throw;
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
                DoRebind(data);
            OnResume();
        }

        /// <summary>构建面板的静态层级，最多执行一次。</summary>
        protected void BuildOnce(Action buildHierarchy)
        {
            if (_hierarchyBuilt) return;
            if (buildHierarchy == null)
                throw new ArgumentNullException(nameof(buildHierarchy));

            buildHierarchy();
            _hierarchyBuilt = true;
        }

        /// <summary>为已打开的面板绑定最新数据，不重建静态层级。</summary>
        internal void DoRebind(object data)
        {
            if (!IsOpen) return;
            OpenData = data;
            OnRebind(data);
        }

        /// <summary>面板关闭时调用</summary>
        internal void DoClose()
        {
            if (!IsOpen) return;

            int closeVersion = ++_lifecycleVersion;
            IsOpen = false;
            IsPaused = false;
            ReleaseOpenSubscriptions();

            if (_animation != null)
            {
                _animation.PlayClose(() =>
                    CompleteClose(closeVersion));
            }
            else
            {
                CompleteClose(closeVersion);
            }
        }

        /// <summary>立即完成关闭生命周期，用于终止性 UI 清理。</summary>
        internal void DoCloseImmediately()
        {
            if (!IsOpen) return;

            int closeVersion = ++_lifecycleVersion;
            IsOpen = false;
            IsPaused = false;
            ReleaseOpenSubscriptions();
            _animation?.Stop();
            CompleteClose(closeVersion);
        }

        private void CompleteClose(int closeVersion)
        {
            if (_lifecycleVersion != closeVersion) return;

            OnClose();
            if (_lifecycleVersion != closeVersion || IsOpen) return;

            gameObject.SetActive(false);
            OpenData = null;
        }

    }
}
