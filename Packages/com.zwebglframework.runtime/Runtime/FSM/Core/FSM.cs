using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.FSM
{
    public readonly struct FSMTransition<T> where T : Enum
    {
        public bool HasOldState { get; }
        public T OldState { get; }
        public T NewState { get; }
        public object Data { get; }

        public FSMTransition(bool hasOldState, T oldState, T newState, object data)
        {
            HasOldState = hasOldState;
            OldState = oldState;
            NewState = newState;
            Data = data;
        }
    }

    /// <summary>
    /// 泛型有限状态机 — 管理状态的注册、切换和每帧更新。
    /// 状态切换时自动调用旧状态 Exit + 新状态 Enter。
    /// </summary>
    /// <typeparam name="T">状态标识枚举类型</typeparam>
    public class FSM<T> where T : Enum
    {
        private readonly Dictionary<T, FSMState<T>> _states = new Dictionary<T, FSMState<T>>();
        private readonly Queue<TransitionRequest> _pendingTransitions = new Queue<TransitionRequest>();
        private readonly Stack<FSMState<T>> _suspendedStates = new Stack<FSMState<T>>();
        private TransitionRequest? _deferredTransition;
        private FSMState<T> _currentState;
        private FSMState<T> _previousState;
        private bool _isTransitioning;
        private bool _isOverlayTransitioning;

        private readonly struct TransitionRequest
        {
            public FSMState<T> Source { get; }
            public T StateId { get; }
            public object Data { get; }

            public TransitionRequest(FSMState<T> source, T stateId, object data)
            {
                Source = source;
                StateId = stateId;
                Data = data;
            }
        }

        /// <summary>转换验证回调 — 返回 false 阻止切换。由 GameFSM 注入。</summary>
        public Func<T, T, bool> TransitionGuard { get; set; }

        /// <summary>覆盖层准入回调；未设置时沿用普通转换规则。</summary>
        public Func<T, T, bool> OverlayGuard { get; set; }

        /// <summary>当前激活状态</summary>
        public FSMState<T> CurrentState => _currentState;

        public bool HasCurrentState => _currentState != null;

        /// <summary>上一个状态</summary>
        public FSMState<T> PreviousState => _previousState;

        /// <summary>当前状态 ID（无状态时返回 default）</summary>
        public T CurrentStateId =>
            _currentState != null ? _currentState.StateId : default;

        /// <summary>状态变更事件（旧状态ID, 新状态ID, 传递数据）</summary>
        public event Action<FSMTransition<T>> OnStateChanged;

        // ==================== 状态管理 ====================

        /// <summary>注册状态</summary>
        public void AddState(FSMState<T> state)
        {
            if (state == null) return;

            if (_states.ContainsKey(state.StateId))
            {
                throw new InvalidOperationException(
                    $"FSM: 状态 {state.StateId} 已注册，不能重复添加。");
            }

            state.Owner = this;
            _states[state.StateId] = state;
        }

        /// <summary>批量注册状态</summary>
        public void AddStates(params FSMState<T>[] states)
        {
            foreach (var state in states)
            {
                AddState(state);
            }
        }

        /// <summary>移除状态</summary>
        public void RemoveState(T stateId)
        {
            if (_isOverlayTransitioning) return;

            if (_states.TryGetValue(stateId, out var state))
            {
                if (_suspendedStates.Count > 0 &&
                    (ReferenceEquals(_currentState, state) ||
                     ReferenceEquals(_suspendedStates.Peek(), state)))
                    return;

                if (_deferredTransition.HasValue &&
                    ReferenceEquals(_deferredTransition.Value.Source, state))
                    _deferredTransition = null;

                if (state == _currentState)
                {
                    state.OnExit();
                    _currentState = null;
                }

                state.Owner = null;
                _states.Remove(stateId);
            }
        }

        // ==================== 状态切换 ====================

        /// <summary>
        /// 转换验证钩子 — 先检查 TransitionGuard（外部注入），再检查子类重写。
        /// 返回 false 阻止切换。
        /// </summary>
        protected virtual bool CanTransitionTo(T fromStateId, T toStateId)
        {
            if (TransitionGuard != null && !TransitionGuard(fromStateId, toStateId))
                return false;
            return true;
        }

        /// <summary>切换到指定状态</summary>
        /// <param name="stateId">目标状态 ID</param>
        /// <param name="data">传递给新状态 OnEnter 的数据</param>
        /// <returns>true=切换成功, false=状态未注册或已是当前状态</returns>
        public bool ChangeState(T stateId, object data = null)
        {
            if (_isOverlayTransitioning || _suspendedStates.Count > 0) return false;
            return QueueTransition(new TransitionRequest(null, stateId, data));
        }

        internal bool ChangeStateFrom(FSMState<T> source, T stateId, object data = null)
        {
            if (_isOverlayTransitioning || source == null ||
                !ReferenceEquals(source.Owner, this))
                return false;

            if (ReferenceEquals(_currentState, source))
            {
                if (_suspendedStates.Count > 0) return false;
                return QueueTransition(new TransitionRequest(source, stateId, data));
            }

            if (_suspendedStates.Count != 1 ||
                !ReferenceEquals(_suspendedStates.Peek(), source))
                return false;
            if (!_states.TryGetValue(stateId, out FSMState<T> target))
            {
                Debug.LogError($"FSM: 状态 {stateId} 未注册。请先调用 AddState。");
                return false;
            }
            if (ReferenceEquals(target, source) ||
                !CanTransitionTo(source.StateId, stateId) ||
                _deferredTransition.HasValue)
                return false;

            _deferredTransition = new TransitionRequest(source, stateId, data);
            return true;
        }

        private bool QueueTransition(TransitionRequest request)
        {
            if (_isTransitioning)
            {
                _pendingTransitions.Enqueue(request);
                return true;
            }

            bool firstResult = false;
            bool first = true;
            _pendingTransitions.Enqueue(request);
            _isTransitioning = true;
            try
            {
                while (_pendingTransitions.Count > 0)
                {
                    bool result = ApplyTransition(_pendingTransitions.Dequeue());
                    if (first)
                    {
                        firstResult = result;
                        first = false;
                    }
                }
            }
            finally
            {
                _isTransitioning = false;
                _pendingTransitions.Clear();
            }

            return firstResult;
        }

        private bool ApplyTransition(TransitionRequest request)
        {
            if (_isOverlayTransitioning || _suspendedStates.Count > 0)
                return false;
            if (request.Source != null &&
                !ReferenceEquals(_currentState, request.Source))
                return false;
            if (!_states.TryGetValue(request.StateId, out FSMState<T> newState))
            {
                Debug.LogError($"FSM: 状态 {request.StateId} 未注册。请先调用 AddState。");
                return false;
            }

            // 不重复进入同一状态（静默拒绝 — 性能优先，调用方应自行预判避免）
            if (_currentState == newState) return false;

            // 转换规则验证（静默拒绝 — TransitionGuard 失败不抛异常，调用方应预验证）
            bool hasOldState = _currentState != null;
            if (hasOldState && !CanTransitionTo(_currentState.StateId, request.StateId))
            {
                return false;
            }

            // 退出旧状态
            var oldState = _currentState;
            var oldStateEnterData = oldState?.EnterData;
            if (oldState != null)
            {
                oldState.OnExit();
            }

            // 记录
            var previousState = _previousState;
            _previousState = oldState;
            var oldStateId = oldState != null ? oldState.StateId : default;

            // 进入新状态
            _currentState = newState;
            try
            {
                newState.OnEnter(request.Data);
            }
            catch
            {
                // OnEnter is part of the transition transaction. Clean up the
                // failed state and rebuild the previous state's full lifetime.
                try
                {
                    newState.OnExit();
                }
                finally
                {
                    _currentState = oldState;
                    _previousState = previousState;
                    if (oldState != null)
                        oldState.OnEnter(oldStateEnterData);
                }

                throw;
            }

            // 事件通知
            OnStateChanged?.Invoke(new FSMTransition<T>(
                hasOldState,
                oldStateId,
                request.StateId,
                request.Data));

            return true;
        }

        public bool PushState(T stateId, object data = null)
        {
            if (_isOverlayTransitioning || _suspendedStates.Count > 0 ||
                _currentState == null ||
                !_states.TryGetValue(stateId, out FSMState<T> overlay))
                return false;
            bool canPush = OverlayGuard != null
                ? OverlayGuard(_currentState.StateId, stateId)
                : CanTransitionTo(_currentState.StateId, stateId);
            if (!canPush)
                return false;

            // A successful Push starts a new lifecycle epoch. Older queued
            // transitions must not survive a complete re-entrant Push/Pop.
            _pendingTransitions.Clear();
            FSMState<T> suspended = _currentState;
            _isOverlayTransitioning = true;
            try
            {
                suspended.OnSuspend(data);
                _suspendedStates.Push(suspended);
                _previousState = suspended;
                _currentState = overlay;
                overlay.OnEnter(data);
                OnStateChanged?.Invoke(new FSMTransition<T>(
                    true,
                    suspended.StateId,
                    overlay.StateId,
                    data));
                return true;
            }
            finally
            {
                _isOverlayTransitioning = false;
            }
        }

        public bool PopState(object data = null)
        {
            if (_isOverlayTransitioning || _currentState == null ||
                _suspendedStates.Count == 0)
                return false;

            FSMState<T> overlay = _currentState;
            FSMState<T> resumed = _suspendedStates.Peek();
            TransitionRequest? deferred = _deferredTransition.HasValue &&
                ReferenceEquals(_deferredTransition.Value.Source, resumed)
                ? _deferredTransition
                : null;

            _isOverlayTransitioning = true;
            try
            {
                _suspendedStates.Pop();
                if (deferred.HasValue) _deferredTransition = null;
                overlay.OnExit();
                _previousState = overlay;
                _currentState = resumed;
                resumed.OnResume(data);
                OnStateChanged?.Invoke(new FSMTransition<T>(
                    true,
                    overlay.StateId,
                    resumed.StateId,
                    data));
            }
            finally
            {
                _isOverlayTransitioning = false;
            }

            if (deferred.HasValue) QueueTransition(deferred.Value);
            return true;
        }

        // ==================== 更新 ====================

        /// <summary>每帧更新当前状态</summary>
        public void Update()
        {
            _currentState?.OnUpdate();
        }

        // ==================== 查询 ====================

        /// <summary>获取指定状态实例</summary>
        public FSMState<T> GetState(T stateId)
        {
            _states.TryGetValue(stateId, out var state);
            return state;
        }

        /// <summary>检查指定状态是否已注册</summary>
        public bool HasState(T stateId)
        {
            return _states.ContainsKey(stateId);
        }

        // ==================== 清理 ====================

        /// <summary>退出当前状态并清空所有注册</summary>
        public void Clear()
        {
            _currentState?.OnExit();
            _currentState = null;
            while (_suspendedStates.Count > 0)
                _suspendedStates.Pop().OnExit();
            _previousState = null;
            _pendingTransitions.Clear();
            _deferredTransition = null;
            _isTransitioning = false;
            _isOverlayTransitioning = false;

            foreach (var kv in _states)
            {
                kv.Value.Owner = null;
            }
            _states.Clear();
        }
    }
}
