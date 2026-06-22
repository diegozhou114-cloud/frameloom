using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 热更新管理器 — 跨场景单例，负责资源热更与版本检查。
    /// 流程：检查版本 → 下载清单 → 下载差异文件 → 校验 → 应用
    ///
    /// 用法：
    ///   HotUpdateManager.Instance.Init();
    ///   var result = await HotUpdateManager.Instance.CheckVersionAsync();
    ///   var ok = await HotUpdateManager.Instance.DownloadUpdatesAsync();
    /// </summary>
    public class HotUpdateManager : SingletonDontDestroy<HotUpdateManager>
    {
        /// <summary>是否已初始化</summary>
        public bool IsReady { get; private set; }

        /// <summary>热更进度（0~1）</summary>
        public float Progress { get; private set; }

        /// <summary>当前阶段描述（用于 UI 显示）</summary>
        public string PhaseDescription { get; private set; }

        /// <summary>下载总大小（字节）</summary>
        public long TotalDownloadBytes { get; private set; }

        /// <summary>已下载大小（字节）</summary>
        public long DownloadedBytes { get; private set; }

        /// <summary>是否需要重启应用</summary>
        public bool NeedRestart { get; private set; }

        /// <summary>远程版本号（检查后有效）</summary>
        public string RemoteVersion { get; private set; }

        /// <summary>本地版本号</summary>
        public string LocalVersion => Application.version;

        // ==================== 初始化 ====================

        public void Init()
        {
            PhaseDescription = "等待检查";
            IsReady = true;
            Debug.Log($"[HotUpdateManager] ✅ 初始化完成 — 本地版本: {LocalVersion}");
        }

        // ==================== 版本检查 ====================

        /// <summary>
        /// 向服务器请求版本信息，返回更新策略。
        /// TODO: 实际 HTTP 请求远程版本清单 + 本地缓存验证。
        /// </summary>
        public async UniTask<VersionCheckResult> CheckVersionAsync()
        {
            PhaseDescription = "检查版本...";
            Progress = 0f;
            Debug.Log("[HotUpdateManager] 开始版本检查...");

            // TODO: 实际网络请求
            await UniTask.Delay(300); // 模拟网络延迟
            RemoteVersion = LocalVersion;
            Progress = 0.2f;

            var result = CompareVersion(LocalVersion, RemoteVersion);
            PhaseDescription = result switch
            {
                VersionCheckResult.UpToDate    => "已是最新版本",
                VersionCheckResult.NeedUpdate  => $"发现新版本: {RemoteVersion}",
                VersionCheckResult.ForceUpdate => $"需要强制更新: {RemoteVersion}",
                _                              => "",
            };

            Debug.Log($"[HotUpdateManager] 版本检查完成: {result}");
            return result;
        }

        // ==================== 下载更新 ====================

        /// <summary>
        /// 下载差异文件。
        /// TODO: 断点续传 + 文件校验。
        /// </summary>
        public async UniTask<bool> DownloadUpdatesAsync()
        {
            PhaseDescription = "下载更新...";
            Progress = 0.2f;
            Debug.Log("[HotUpdateManager] 开始下载更新...");

            // TODO: 实际下载逻辑
            TotalDownloadBytes = 10 * 1024 * 1024; // 模拟 10 MB
            for (int i = 0; i <= 10; i++)
            {
                await UniTask.Delay(100);
                DownloadedBytes = i * 1024 * 1024;
                Progress = 0.2f + 0.7f * (i / 10f);
                PhaseDescription = $"下载中... {DownloadedBytes / 1024 / 1024}/{TotalDownloadBytes / 1024 / 1024} MB";
            }

            PhaseDescription = "校验文件...";
            Progress = 0.95f;
            await UniTask.Delay(200);

            PhaseDescription = "应用更新...";
            await UniTask.Delay(100);

            NeedRestart = true;
            Progress = 1f;
            PhaseDescription = "更新完成，即将重启";
            Debug.Log("[HotUpdateManager] ✅ 更新下载完成，需要重启应用");
            return true;
        }

        // ==================== 内部 ====================

        private VersionCheckResult CompareVersion(string local, string remote)
        {
            if (string.IsNullOrEmpty(remote)) return VersionCheckResult.UpToDate;
            try
            {
                var localVer = new Version(local);
                var remoteVer = new Version(remote);
                if (remoteVer > localVer)
                    return VersionCheckResult.NeedUpdate;
                return VersionCheckResult.UpToDate;
            }
            catch
            {
                Debug.LogWarning($"[HotUpdateManager] 版本号解析失败: local={local}, remote={remote}");
                return VersionCheckResult.UpToDate;
            }
        }
    }

    /// <summary>版本检查结果</summary>
    public enum VersionCheckResult
    {
        UpToDate,
        NeedUpdate,
        ForceUpdate,
    }
}
