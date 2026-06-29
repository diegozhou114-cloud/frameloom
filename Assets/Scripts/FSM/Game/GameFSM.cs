using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 游戏全局状态机 — 跨场景单例，管理 Launch/Login/MainVillage/Battle/Loading 五大状态。
    /// 状态变更通过 EventBus 广播，UI 层可订阅响应。
    /// </summary>
    public class GameFSM : SingletonDontDestroy<GameFSM>
    {
        /// <summary>EventBus 事件键：状态变更</summary>
        public const string EventStateChanged = "GameStateChanged";

        private FSM<GameState> _fsm;

        /// <summary>当前游戏状态</summary>
        public GameState CurrentState => _fsm.CurrentStateId;

        /// <summary>上一个游戏状态</summary>
        public GameState PreviousState =>
            _fsm.PreviousState != null ? _fsm.PreviousState.StateId : GameState.Launch;

        // 合法转换表：哪些状态可以跳转到哪些状态
        private static readonly Dictionary<GameState, HashSet<GameState>> _transitionRules =
            new Dictionary<GameState, HashSet<GameState>>
            {
                {
                    GameState.Launch, new HashSet<GameState>
                    {
                        GameState.Login,
                        GameState.Loading,
                        GameState.MainVillage,  // 离线模式直通
                    }
                },
                {
                    GameState.Login, new HashSet<GameState>
                    {
                        GameState.MainVillage,
                        GameState.Loading,
                    }
                },
                {
                    GameState.MainVillage, new HashSet<GameState>
                    {
                        GameState.Battle,
                        GameState.Loading,
                        GameState.Pause,
                    }
                },
                {
                    GameState.Battle, new HashSet<GameState>
                    {
                        GameState.MainVillage,
                        GameState.Loading,
                        GameState.Pause,
                    }
                },
                {
                    GameState.Pause, new HashSet<GameState>
                    {
                        GameState.MainVillage,
                        GameState.Battle,
                        GameState.Loading,
                    }
                },
                {
                    GameState.Loading, new HashSet<GameState>
                    {
                        GameState.Launch,
                        GameState.Login,
                        GameState.MainVillage,
                        GameState.Battle,
                    }
                },
            };

        private float _pauseStartTime;

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // 切后台 → 进入暂停
                if (CurrentState == GameState.MainVillage || CurrentState == GameState.Battle)
                {
                    _pauseStartTime = Time.realtimeSinceStartup;
                    ChangeState(GameState.Pause, GamePauseEvent.Background);
                }
            }
            else
            {
                // 回前台 → 恢复
                if (CurrentState == GameState.Pause)
                {
                    float duration = Time.realtimeSinceStartup - _pauseStartTime;
                    ChangeState(PreviousState, new GameResumeEvent { PausedDuration = duration });
                }
            }
        }

        private bool _initialized;

        protected override void Awake()
        {
            base.Awake();
            InitializeFSM();
        }

        private void Start()
        {
            // 延迟到 Start 广播初始状态，确保其他模块的 Awake 已完成订阅
            if (!_initialized && _fsm != null)
            {
                _initialized = true;
                _fsm.ChangeState(GameState.Launch);
            }
        }

        private void InitializeFSM()
        {
            _fsm = new FSM<GameState>();
            _fsm.TransitionGuard = CanTransition; // 状态内部 ChangeState 也受转换规则约束

            // 注册所有状态
            _fsm.AddStates(
                new LaunchState(),
                new LoginState(),
                new MainVillageState(),
                new BattleState(),
                new LoadingState(),
                new PauseState()
            );

            // 监听内部状态变更，转发到 EventBus
            _fsm.OnStateChanged += OnInternalStateChanged;
        }

        private void Update()
        {
            _fsm?.Update();
        }

        // ==================== 公开 API ====================

        /// <summary>切换游戏状态（受转换规则约束）</summary>
        /// <returns>true=切换成功</returns>
        public bool ChangeState(GameState targetState, object data = null)
        {
            if (!CanTransition(CurrentState, targetState))
            {
                Debug.LogWarning(
                    $"[GameFSM] 非法状态转换: {CurrentState} → {targetState}。已阻止。");
                return false;
            }

            return _fsm.ChangeState(targetState, data);
        }

        /// <summary>带 Loading 过渡的状态切换：先切到 Loading，完成后自动切到目标</summary>
        public void ChangeStateWithLoading(GameState targetState, object data = null)
        {
            if (!CanTransition(CurrentState, targetState))
            {
                Debug.LogWarning(
                    $"[GameFSM] 非法状态转换: {CurrentState} → {targetState}");
                return;
            }

            // 进入 Loading，携带目标状态信息
            _fsm.ChangeState(GameState.Loading, new LoadingPayload
            {
                TargetState = targetState,
                TargetData = data,
            });
        }

        /// <summary>检查转换是否合法</summary>
        public bool CanTransition(GameState from, GameState to)
        {
            if (_transitionRules.TryGetValue(from, out var allowedTargets))
            {
                return allowedTargets.Contains(to);
            }
            return false;
        }

        /// <summary>获取当前状态的实例</summary>
        public FSMState<GameState> GetStateInstance(GameState state)
        {
            return _fsm.GetState(state);
        }

        // ==================== 内部 ====================

        private void OnInternalStateChanged(GameState oldState, GameState newState, object data)
        {
            // 广播到 EventBus，UI 层可订阅
            var payload = new GameStateChangedEvent
            {
                OldState = oldState,
                NewState = newState,
                Data = data,
            };

            EventBus.Publish(EventStateChanged, payload);

            Debug.Log($"[GameFSM] {oldState} → {newState}");
        }

        protected override void OnDestroy()
        {
            _fsm?.Clear();
            base.OnDestroy();
        }
    }
}
