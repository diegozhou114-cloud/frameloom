using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Network;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 登录状态 — 离线优先 + 联网可选。
    /// 离线模式：确保本地存档存在，直接进角色选择/主城。
    /// 联网模式：显示 LoginPanel，通过 EventBus 监听按钮事件。
    /// </summary>
    public class LoginState : FSMState<GameState>
    {
        public LoginState() : base(GameState.Login) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入登录状态");
            EventBus.Publish("LoginStarted");

            var cfg = GameLauncher.Config;

            // 离线模式：完全跳过网络，直接使用本地存档
            if (cfg != null && cfg.EnableOfflineMode)
            {
                Debug.Log("[LoginState] 🔌 离线模式 — 使用本地存档");
                EnterOfflineMode();
            }
            else
            {
                // 联网模式：显示登录面板，订阅按钮事件
                Debug.Log("[LoginState] 🌐 联网模式");
                EventBus.Subscribe(LoginEvents.GuestLogin, OnGuestLogin);
                EventBus.Subscribe(LoginEvents.OfflineLogin, OnOfflineLogin);
                UIManager.Instance.Open<LoginPanel>();
            }
        }

        public override void OnExit()
        {
            EventBus.Unsubscribe(LoginEvents.GuestLogin, OnGuestLogin);
            EventBus.Unsubscribe(LoginEvents.OfflineLogin, OnOfflineLogin);
            EventBus.Unsubscribe<int>("CharacterSelected", OnCharacterSelected);
            UIManager.Instance.Close<LoginPanel>();
            UIManager.Instance.Close<CharacterSelectPanel>();
            base.OnExit();
        }

        // ==================== 事件回调 ====================

        private void OnGuestLogin()
        {
            UIManager.Instance.Close<LoginPanel>();

            var cfg = GameLauncher.Config;
            string host = cfg?.DefaultServerHost ?? "127.0.0.1";
            int port = cfg?.DefaultServerPort ?? 7777;

            EventBus.Subscribe<NetworkStateChangedEvent>(
                NetworkManager.EventNetworkStateChanged, OnNetworkStateChanged);

            NetworkManager.Instance.Connect(NetworkProtocol.TCP, host, port);
        }

        private void OnOfflineLogin()
        {
            Debug.Log("[LoginState] 用户选择离线模式");
            UIManager.Instance.Close<LoginPanel>();
            EnterOfflineMode();
        }

        private void EnterOfflineMode()
        {
            GameInitializer.EnsureSaveExists();

            if (SaveManager.Exists(SaveManager.CurrentSlot))
            {
                SaveManager.LoadToDataManager(SaveManager.CurrentSlot);
                ChangeState(GameState.MainVillage);
            }
            else
            {
                EventBus.Subscribe<int>("CharacterSelected", OnCharacterSelected);
                UIManager.Instance.Open<CharacterSelectPanel>();
            }
        }

        private void OnCharacterSelected(int slot)
        {
            EventBus.Unsubscribe<int>("CharacterSelected", OnCharacterSelected);
            Debug.Log($"[LoginState] 角色已选择 slot={slot}");
            SaveManager.LoadToDataManager(slot);
            ChangeState(GameState.MainVillage);
        }

        private void OnNetworkStateChanged(NetworkStateChangedEvent evt)
        {
            if (evt.NewState == ConnectionState.Connected)
            {
                EventBus.Unsubscribe<NetworkStateChangedEvent>(
                    NetworkManager.EventNetworkStateChanged, OnNetworkStateChanged);
                Debug.Log("[LoginState] ✅ 服务器连接成功 → MainVillage");
                ChangeState(GameState.MainVillage);
            }
            else if (evt.NewState == ConnectionState.Disconnected
                     && !string.IsNullOrEmpty(evt.Message))
            {
                EventBus.Unsubscribe<NetworkStateChangedEvent>(
                    NetworkManager.EventNetworkStateChanged, OnNetworkStateChanged);
                Debug.LogError($"[LoginState] ❌ 连接失败: {evt.Message}");
                // 重新打开登录面板并显示错误
                UIManager.Instance.Open<LoginPanel>();
                EventBus.Publish("LoginError", evt.Message);
            }
        }

        // ==================== 辅助 ====================

    }
}
