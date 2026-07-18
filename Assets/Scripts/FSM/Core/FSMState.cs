using System;
using System.Threading;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// FSM 状态基类 — 所有具体状态的父类。
    /// 用法：public class IdleState : FSMState&lt;MyEnum&gt; { }
    /// </summary>
    /// <typeparam name="T">状态标识枚举类型</typeparam>
    public abstract class FSMState<T> where T : Enum
    {
        /// <summary>状态唯一标识</summary>
        public T StateId { get; }

        /// <summary>所属状态机</summary>
        public FSM<T> Owner { get; internal set; }

        /// <summary>当前是否激活</summary>
        public bool IsActive { get; internal set; }

        /// <summary>进入状态时传入的数据</summary>
        public object EnterData { get; private set; }

        private CancellationTokenSource _lifetimeSource;
        protected CancellationToken LifetimeToken =>
            _lifetimeSource?.Token ?? CancellationToken.None;

        protected FSMState(T stateId)
        {
            StateId = stateId;
        }

        // ==================== 生命周期 ====================

        /// <summary>进入状态 — 首次初始化、注册事件</summary>
        public virtual void OnEnter(object data = null)
        {
            _lifetimeSource?.Dispose();
            _lifetimeSource = new CancellationTokenSource();
            IsActive = true;
            EnterData = data;
        }

        /// <summary>每帧更新 — 仅当前激活状态会被调用</summary>
        public virtual void OnUpdate() { }

        /// <summary>退出状态 — 清理资源、反注册事件</summary>
        public virtual void OnExit()
        {
            IsActive = false;
            EnterData = null;
            if (_lifetimeSource == null) return;
            _lifetimeSource.Cancel();
            _lifetimeSource.Dispose();
            _lifetimeSource = null;
        }

        // ==================== 便捷方法 ====================

        /// <summary>切换到目标状态</summary>
        protected void ChangeState(T targetState, object data = null)
        {
            Owner?.ChangeState(targetState, data);
        }

        public override string ToString()
        {
            return $"[{GetType().Name}] StateId={StateId}";
        }
    }
}
