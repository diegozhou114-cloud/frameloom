using System;
using System.Threading;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Network;
using ZWebGLFrameWork.Network.UnityHttp;
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
        private readonly OfflineSaveFlow _offlineSaveFlow =
            new OfflineSaveFlow(new DefaultOfflineSaveGateway());

        public LoginState() : base(GameState.Login) { }

        private CancellationTokenSource _loginCts;

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            _loginCts = new CancellationTokenSource();
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
            _loginCts?.Cancel();
            _loginCts?.Dispose();
            _loginCts = null;
            EventBus.Unsubscribe(LoginEvents.GuestLogin, OnGuestLogin);
            EventBus.Unsubscribe(LoginEvents.OfflineLogin, OnOfflineLogin);
            EventBus.Unsubscribe<int>(LoginEvents.CharacterSelected, OnCharacterSelected);
            UIManager.Instance.Close<LoginPanel>();
            UIManager.Instance.Close<CharacterSelectPanel>();
            base.OnExit();
        }

        // ==================== 事件回调 ====================

        private async void OnGuestLogin()
        {
            if (_loginCts == null)
                return;

            CancellationToken token = _loginCts.Token;
            UIManager.Instance.Close<LoginPanel>();
            try
            {
                Uri endpoint = new Uri(GameLauncher.Config.HttpsApiBaseUrl);
                GuestLoginResult result =
                    await NetworkManager.Instance.GuestLoginAsync(endpoint, token);
                if (token.IsCancellationRequested || !IsActive) return;
                if (!result.Success)
                {
                    PublishLoginFailure(result.Error);
                    return;
                }

                NetworkSession.Current =
                    new NetworkSession(result.PlayerId, result.Credentials);
                ChangeState(GameState.MainVillage);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                if (token.IsCancellationRequested || !IsActive) return;
                PublishLoginFailure(exception.Message);
            }
        }

        private void OnOfflineLogin()
        {
            Debug.Log("[LoginState] 用户选择离线模式");
            UIManager.Instance.Close<LoginPanel>();
            EnterOfflineMode();
        }

        private void EnterOfflineMode()
        {
            int slot = SaveManager.CurrentSlot;
            if (_offlineSaveFlow.TryPrepareSlot(slot))
            {
                ChangeState(GameState.MainVillage);
            }
            else
            {
                EventBus.Publish(LoginEvents.LoginError, "本地存档创建或加载失败，请选择其他存档位");
                EventBus.Subscribe<int>(LoginEvents.CharacterSelected, OnCharacterSelected);
                UIManager.Instance.Open<CharacterSelectPanel>();
            }
        }

        private void OnCharacterSelected(int slot)
        {
            EventBus.Unsubscribe<int>(LoginEvents.CharacterSelected, OnCharacterSelected);
            Debug.Log($"[LoginState] 角色已选择 slot={slot}");
            if (_offlineSaveFlow.TryPrepareSlot(slot))
            {
                ChangeState(GameState.MainVillage);
                return;
            }

            EventBus.Publish(LoginEvents.LoginError, "本地存档加载失败，请重试");
            EventBus.Subscribe<int>(LoginEvents.CharacterSelected, OnCharacterSelected);
        }

        private void PublishLoginFailure(string message)
        {
            Debug.LogError($"[LoginState] 登录失败: {message}");
            UIManager.Instance.Open<LoginPanel>();
            EventBus.Publish(LoginEvents.LoginError, message);
        }

        // ==================== 辅助 ====================

    }
}
