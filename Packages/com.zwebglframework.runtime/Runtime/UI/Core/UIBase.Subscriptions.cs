using System;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.UI
{
    public abstract partial class UIBase
    {
        private SubscriptionScope _openSubscriptions;

        /// <summary>
        /// 当前打开周期的事件订阅。关闭开始、打开失败或销毁时解除；暂停不结束此周期。
        /// 对象存活期或随数据源变化的订阅应由各自独立的 SubscriptionScope 管理。
        /// </summary>
        protected SubscriptionScope OpenSubscriptions
        {
            get
            {
                if (!IsOpen)
                    throw new InvalidOperationException("Panel is not open.");
                return _openSubscriptions ??= new SubscriptionScope();
            }
        }

        private void ReleaseOpenSubscriptions()
        {
            SubscriptionScope subscriptions = _openSubscriptions;
            _openSubscriptions = null;
            try { subscriptions?.Dispose(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        /// <summary>销毁时释放尚未关闭的打开周期；重写时应调用基类实现。</summary>
        protected virtual void OnDestroy()
        {
            ++_lifecycleVersion;
            IsOpen = false;
            IsPaused = false;
            OpenData = null;
            ReleaseOpenSubscriptions();
        }
    }
}
