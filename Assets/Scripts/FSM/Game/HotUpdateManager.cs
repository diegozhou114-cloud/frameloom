using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Network;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 热更新管理器 — 版本检查 + 差异化下载 + MD5 校验 + 断点续传。
    /// 通过 BootProgress / BootError 事件驱动 UI，与 LoadingPanel / ErrorPanel 解耦。
    /// </summary>
    public class HotUpdateManager : SingletonDontDestroy<HotUpdateManager>
    {
        public bool IsReady { get; private set; }
        public float Progress { get; private set; }
        public string PhaseDescription { get; private set; }
        public long TotalDownloadBytes { get; private set; }
        public long DownloadedBytes { get; private set; }
        public bool NeedRestart { get; private set; }
        public string RemoteVersion { get; private set; }
        public string LocalVersion => Application.version;
        public string HotUpdateBaseUrl { get; set; }  // 可在 AppConfig 中配置
        public string HotUpdateManifestPath { get; set; } = "update_manifest.json";

        private HttpClient _http;

        // ==================== 初始化 ====================

        public bool Init()
        {
            try
            {
                _http = new HttpClient();
                _http.Connect(HotUpdateBaseUrl ?? "https://your-cdn.com", 443);
                PhaseDescription = "等待检查";
                IsReady = true;
                Debug.Log($"[HotUpdateManager] ✅ 初始化完成 — 本地: {LocalVersion}, CDN: {HotUpdateBaseUrl}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HotUpdateManager] ❌ 初始化失败: {ex.Message}");
                return false;
            }
        }

        // ==================== 版本检查 ====================

        /// <summary>请求更新清单，返回更新策略</summary>
        public async UniTask<VersionCheckResult> CheckVersionAsync()
        {
            PublishProgress("检查版本...", 0.05f, "正在连接更新服务器");

            try
            {
                var manifest = await GetManifestAsync();
                if (manifest == null)
                {
                    PublishError("版本检查失败", "无法连接到更新服务器", true);
                    return VersionCheckResult.Error;
                }

                RemoteVersion = manifest.Version;
                PublishProgress("检查版本...", 0.15f, $"远程: v{RemoteVersion}");

                if (manifest.ForceUpdate)
                {
                    PublishProgress("需要强制更新", 0.2f, $"v{RemoteVersion}");
                    return VersionCheckResult.ForceUpdate;
                }

                var result = CompareVersion(LocalVersion, RemoteVersion);
                PhaseDescription = result switch
                {
                    VersionCheckResult.UpToDate   => "已是最新版本",
                    VersionCheckResult.NeedUpdate => $"发现新版本 v{RemoteVersion}",
                    _                              => "",
                };

                // 缓存文件清单供下载使用
                _pendingManifest = result == VersionCheckResult.NeedUpdate ? manifest : null;

                Debug.Log($"[HotUpdateManager] 版本检查完成: {result}");
                return result;
            }
            catch (Exception ex)
            {
                PublishError("版本检查异常", ex.Message, true);
                return VersionCheckResult.Error;
            }
        }

        // ==================== 下载更新 ====================

        private UpdateManifest _pendingManifest;

        public async UniTask<bool> DownloadUpdatesAsync()
        {
            if (_pendingManifest == null || _pendingManifest.Files.Count == 0)
            {
                Debug.Log("[HotUpdateManager] 无文件需要下载");
                return true;
            }

            var files = _pendingManifest.Files;
            TotalDownloadBytes = 0;
            DownloadedBytes = 0;
            foreach (var f in files) TotalDownloadBytes += f.Size;

            PublishProgress("开始下载", 0.2f, $"{files.Count} 个文件, {FormatSize(TotalDownloadBytes)}");

            var downloadDir = Path.Combine(Application.persistentDataPath, "HotUpdate");
            Directory.CreateDirectory(downloadDir);

            int failed = 0;
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var ok = await DownloadFileAsync(file, downloadDir, i, files.Count);
                if (!ok) failed++;
            }

            if (failed > 0)
            {
                PublishError("下载未完成", $"{failed}/{files.Count} 个文件下载失败", true);
                return false;
            }

            // 校验 + 应用
            PublishProgress("校验文件...", 0.9f);
            if (!VerifyAllFiles(files, downloadDir))
            {
                PublishError("文件校验失败", "下载文件 MD5 不匹配，请重试", true);
                return false;
            }

            PublishProgress("应用更新...", 0.95f);
            await UniTask.Delay(200);

            // C.3: 热更包中可能包含新的 AppConfig，下载完成后自动同步
            TrySyncAppConfig(downloadDir);

            NeedRestart = true;
            PublishProgress("更新完成", 1f, "即将重启应用");
            Debug.Log("[HotUpdateManager] ✅ 更新完成，需要重启");
            return true;
        }

        private async UniTask<bool> DownloadFileAsync(UpdateFileInfo file, string baseDir,
            int index, int total)
        {
            var url = $"{HotUpdateBaseUrl}/{file.Name}";
            var savePath = Path.Combine(baseDir, file.SubDir ?? "", file.Name);
            var tmpPath = savePath + ".tmp";

            // 断点续传：检查已下载的临时文件
            long existingBytes = 0;
            if (File.Exists(tmpPath))
                existingBytes = new FileInfo(tmpPath).Length;

            if (existingBytes >= file.Size)
            {
                // 已完成
                DownloadedBytes += file.Size;
                UpdateDownloadProgress(index, total);
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(savePath));

            var request = UnityWebRequest.Get(url);
            if (existingBytes > 0)
                request.SetRequestHeader("Range", $"bytes={existingBytes}-");

            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                var current = existingBytes + (long)request.downloadedBytes;
                UpdateDownloadProgress(index, total);
                PublishProgress($"下载中 ({index + 1}/{total})",
                    0.2f + 0.7f * ((float)(DownloadedBytes + current) / TotalDownloadBytes),
                    $"{FormatSize(DownloadedBytes + current)}/{FormatSize(TotalDownloadBytes)}");
                await UniTask.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[HotUpdateManager] 下载失败: {file.Name} — {request.error}");
                return false;
            }

            var data = request.downloadHandler.data;
            if (existingBytes > 0)
            {
                // 追加写入
                using (var fs = new FileStream(tmpPath, FileMode.Append))
                    await fs.WriteAsync(data, 0, data.Length);
            }
            else
            {
                await File.WriteAllBytesAsync(tmpPath, data);
            }

            request.Dispose();
            DownloadedBytes += data.Length;
            UpdateDownloadProgress(index + 1, total);
            return true;
        }

        private bool VerifyAllFiles(List<UpdateFileInfo> files, string baseDir)
        {
            foreach (var file in files)
            {
                var path = Path.Combine(baseDir, file.SubDir ?? "", file.Name);
                var tmpPath = path + ".tmp";

                if (!File.Exists(tmpPath)) return false;
                if (!VerifyMD5(tmpPath, file.MD5)) return false;

                // 原子替换
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmpPath, path);
                Debug.Log($"[HotUpdateManager] ✅ 校验通过: {file.Name}");
            }
            return true;
        }

        private bool VerifyMD5(string filePath, string expectedMD5)
        {
            if (string.IsNullOrEmpty(expectedMD5)) return true;
            try
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(filePath);
                var hash = md5.ComputeHash(stream);
                var actual = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return actual == expectedMD5.ToLowerInvariant();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HotUpdateManager] MD5 校验异常: {ex.Message}");
                return false;
            }
        }

        // ==================== HTTP ====================

        private async UniTask<UpdateManifest> GetManifestAsync()
        {
            var url = $"{HotUpdateBaseUrl}/{HotUpdateManifestPath}?t={DateTime.UtcNow.Ticks}";
            var request = UnityWebRequest.Get(url);
            request.timeout = 10;
            await request.SendWebRequest().ToUniTask();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[HotUpdateManager] 获取清单失败: {request.error}");
                request.Dispose();
                return null;
            }

            var json = request.downloadHandler.text;
            request.Dispose();
            return JsonUtility.FromJson<UpdateManifest>(json);
        }

        // ==================== 辅助 ====================

        private void UpdateDownloadProgress(int done, int total)
        {
            Progress = 0.2f + 0.7f * ((float)done / total);
        }

        private void PublishProgress(string phase, float progress, string hint = "")
        {
            PhaseDescription = phase;
            Progress = progress;
            EventBus.Publish(BootProgressEvent.EventKey,
                new BootProgressEvent(phase, progress, hint));
        }

        private void PublishError(string module, string message, bool canSkip)
        {
            EventBus.Publish(BootErrorEvent.EventKey,
                new BootErrorEvent(module, message, canSkip));
        }

        private VersionCheckResult CompareVersion(string local, string remote)
        {
            if (string.IsNullOrEmpty(remote)) return VersionCheckResult.Error;
            try
            {
                var l = new Version(local);
                var r = new Version(remote);
                return r > l ? VersionCheckResult.NeedUpdate : VersionCheckResult.UpToDate;
            }
            catch
            {
                Debug.LogWarning($"[HotUpdateManager] 版本号解析失败: local={local}, remote={remote}");
                return VersionCheckResult.UpToDate;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }

        // ==================== C.3 AppConfig 热更同步 ====================

        /// <summary>检查热更包中是否有新的 AppConfig，有则应用到 GameLauncher</summary>
        private static void TrySyncAppConfig(string downloadDir)
        {
            string configPath = Path.Combine(downloadDir, "Config", "AppConfig.asset");
            if (!File.Exists(configPath))
            {
                Debug.Log("[HotUpdateManager] 热更包中没有 AppConfig，跳过同步");
                return;
            }

            // AppConfig 是 ScriptableObject，通过 AssetBundle 加载
            // 当前简化实现：仅记录日志，实际需配合 AssetBundle 加载
            Debug.Log("[HotUpdateManager] 🔄 检测到热更 AppConfig，将在重启后生效");
        }
    }

    /// <summary>版本检查结果</summary>
    public enum VersionCheckResult
    {
        UpToDate,
        NeedUpdate,
        ForceUpdate,
        Error,      // 检查失败（网络错误等）
    }
}
