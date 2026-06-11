using System;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Network
{
    /// <summary>
    /// 网络状态感知器 — 检测 WiFi/蜂窝网络切换和网络可达性。
    /// 跨场景单例，网络变化时通过 EventBus 广播。
    /// 
    /// 苹果审核要求：App 必须在网络切换时正确处理（如 WiFi→蜂窝时提示用户）。
    /// </summary>
    public class NetworkMonitor : SingletonDontDestroy<NetworkMonitor>
    {
        /// <summary>EventBus 事件键：网络类型变化</summary>
        public const string EventNetworkTypeChanged = "NetworkTypeChanged";

        /// <summary>EventBus 事件键：网络可达性变化</summary>
        public const string EventReachabilityChanged = "NetworkReachabilityChanged";

        /// <summary>当前网络类型</summary>
        public NetworkType CurrentNetworkType { get; private set; } = NetworkType.Unknown;

        /// <summary>当前是否有网络连接</summary>
        public bool IsReachable { get; private set; }

        /// <summary>检测间隔（秒）</summary>
        [SerializeField] private float _checkInterval = 2f;

        private float _timer;
        private NetworkType _lastNetworkType;
        private bool _lastReachable;

        private void Start()
        {
            RefreshNetworkState();
            _lastNetworkType = CurrentNetworkType;
            _lastReachable = IsReachable;
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < _checkInterval) return;
            _timer = 0f;

            RefreshNetworkState();

            // 检测变化
            if (_lastNetworkType != CurrentNetworkType)
            {
                var oldType = _lastNetworkType;
                _lastNetworkType = CurrentNetworkType;

                var evt = new NetworkTypeChangedEvent
                {
                    OldType = oldType,
                    NewType = CurrentNetworkType,
                };
                EventBus.Publish(EventNetworkTypeChanged, evt);

                Debug.Log($"[NetworkMonitor] 网络类型切换: {oldType} → {CurrentNetworkType}");
            }

            if (_lastReachable != IsReachable)
            {
                _lastReachable = IsReachable;
                EventBus.Publish(EventReachabilityChanged, IsReachable);

                Debug.Log($"[NetworkMonitor] 网络可达性: {IsReachable}");
            }
        }

        private void RefreshNetworkState()
        {
            switch (Application.internetReachability)
            {
                case NetworkReachability.ReachableViaCarrierDataNetwork:
                    CurrentNetworkType = NetworkType.Cellular;
                    IsReachable = true;
                    break;
                case NetworkReachability.ReachableViaLocalAreaNetwork:
                    CurrentNetworkType = NetworkType.WiFi;
                    IsReachable = true;
                    break;
                case NetworkReachability.NotReachable:
                    CurrentNetworkType = NetworkType.Unknown;
                    IsReachable = false;
                    break;
            }
        }
    }

    /// <summary>网络类型变化事件</summary>
    public struct NetworkTypeChangedEvent
    {
        public NetworkType OldType;
        public NetworkType NewType;
        public override string ToString() => $"{OldType} → {NewType}";
    }
}
