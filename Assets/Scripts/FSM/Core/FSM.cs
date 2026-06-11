using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 泛型有限状态机 — 管理状态的注册、切换和每帧更新。
    /// 状态切换时自动调用旧状态 Exit + 新状态 Enter。
    /// </summary>
    /// <typeparam name="T">状态标识枚举类型</typeparam>
    public class FSM<T> where T : Enum
    {
        private readonly Dictionary<T, FSMState<T>> _states = new Dictionary<T, FSMState<T>>();
        private FSMState<T> _currentState;
        private FSMState<T> _previousState;

        /// <summary>当前激活状态</summary>
        public FSMState<T> CurrentState => _currentState;

        /// <summary>上一个状态</summary>
        public FSMState<T> PreviousState => _previousState;

        /// <summary>当前状态 ID（无状态时返回 default）</summary>
        public T CurrentStateId =>
            _currentState != null ? _currentState.StateId : default;

        /// <summary>状态变更事件（旧状态ID, 新状态ID, 传递数据）</summary>
        public event Action<T, T, object> OnStateChanged;

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
            if (_states.TryGetValue(stateId, out var state))
            {
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

        /// <summary>切换到指定状态</summary>
        /// <param name="stateId">目标状态 ID</param>
        /// <param name="data">传递给新状态 OnEnter 的数据</param>
        /// <returns>true=切换成功, false=状态未注册或已是当前状态</returns>
        public bool ChangeState(T stateId, object data = null)
        {
            if (!_states.TryGetValue(stateId, out var newState))
            {
                throw new InvalidOperationException(
                    $"FSM: 状态 {stateId} 未注册。请先调用 AddState。");
            }

            // 不重复进入同一状态
            if (_currentState == newState) return false;

            // 退出旧状态
            var oldState = _currentState;
            if (oldState != null)
            {
                oldState.OnExit();
            }

            // 记录
            _previousState = oldState;
            var oldStateId = oldState != null ? oldState.StateId : default;

            // 进入新状态
            _currentState = newState;
            newState.OnEnter(data);

            // 事件通知
            OnStateChanged?.Invoke(oldStateId, stateId, data);

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
            if (_currentState != null)
            {
                _currentState.OnExit();
                _currentState = null;
            }

            _previousState = null;

            foreach (var kv in _states)
            {
                kv.Value.Owner = null;
            }
            _states.Clear();
        }
    }
}
