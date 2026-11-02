# FrameLoom YooAsset 适配包

可选包，为业务资源提供 YooAsset 2.3.17 + UniTask 异步加载。框架启动 UI 继续使用本地 Resources。适配器不初始化全局 YooAssets、不选择 CDN、不销毁资源包，也不加载热更新程序集。

项目根 manifest 将 YooAsset 固定到上游提交 `101236db8a58a327071ff1ab35d28bc1535ca1c1`（UPM 路径 `Assets/YooAsset`）；UniTask 固定版本为 2.5.11。脱离此仓库使用时，需要在消费项目 manifest 配置相同 Git 依赖及 FrameLoom 运行时包。

## 安装

以下依赖组合已在独立 Unity 消费项目中从 GitHub 解析并通过公开 API 测试。核心与适配包固定到同一个提交：

```json
{
  "dependencies": {
    "com.zwebglframework.runtime": "https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.runtime#2608415a643b37968492118057cbb13482c08538",
    "com.zwebglframework.yooasset": "https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.yooasset#2608415a643b37968492118057cbb13482c08538",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#e5acc106ee196bc5a32fb14cdf2987b0f96d11e0",
    "com.tuyoogame.yooasset": "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset#101236db8a58a327071ff1ab35d28bc1535ca1c1"
  }
}
```

## 编辑器模拟加载

先在 YooAsset 的资源收集器中创建 `Business` 包并配置资源定位地址。以下初始化代码放在消费项目的 Editor 程序集中，该程序集需要显式引用 `ZWebGLFrameWork.Core`、`ZWebGLFrameWork.YooAsset`、`UniTask` 和 `YooAsset`；模拟构建帮助类不能进入 Player。

在编辑器 PlayMode 启动流程中调用一次；YooAssets 的运行时驱动器需要通过 Update 推进操作，不能直接当作 EditMode 菜单脚本等待。不要重复初始化同一包：

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.YooAsset;

public static class BusinessResourceSetup
{
    public static async UniTask<ResourcePackage> PrepareEditorPackage(CancellationToken token)
    {
        YooAssets.Initialize(); // 由应用统一调用一次
        var package = YooAssets.CreatePackage("Business");
        var build = EditorSimulateModeHelper.SimulateBuild("Business");
        var parameters = new EditorSimulateModeParameters
        {
            EditorFileSystemParameters = FileSystemParameters
                .CreateDefaultEditorFileSystemParameters(build.PackageRootDirectory)
        };
        var initialization = package.InitializeAsync(parameters);
        await UniTask.WaitUntil(() => initialization.IsDone, cancellationToken: token);
        token.ThrowIfCancellationRequested();
        if (initialization.Status != EOperationStatus.Succeed)
            throw new System.InvalidOperationException(initialization.Error);

        var manager = ResourceManager.Instance;
        manager.SetBusinessContentUpdater(new YooAssetBusinessContentUpdater(package));
        var result = await manager.PrepareBusinessContentAsync(token);
        if (result.Status == BusinessContentUpdateStatus.Failed)
            throw new System.InvalidOperationException(result.Message);
        return package;
    }
}
```

已有激活清单的包也可以直接 `manager.SetBusinessProvider(new YooAssetProvider(package))`。WebGL Player 应由应用设置 WebPlayModeParameters 及 Web 文件系统，完成初始化/清单准备后使用相同入口；不能把 EditorSimulate 参数带入发布包。

## 加载与所有权

```csharp
var manager = ResourceManager.Instance;
using (var lease = await manager.AcquireAsync<UnityEngine.GameObject>(
    ResourceLayer.Business, "Buildings/TownHall", token))
{
    var instance = UnityEngine.Object.Instantiate(lease.Asset);
    // 实际业务中，保留 lease 直到该实例及其资源引用全部不再使用。
    UnityEngine.Object.Destroy(instance);
    await UniTask.WaitUntil(() => instance == null); // 确认延迟销毁完成，再释放源资源租约
}
```

每次 AcquireAsync 获得独立 YooAsset 句柄；同资源加载合并由 YooAsset 处理。失败、取消及等待期间 provider 退休会释放本次句柄。租约 Dispose 幂等，provider Dispose 不收回已交给调用者的租约。

旧 LoadAsync 入口按 `(资源类型, 定位地址)` 持有一个兼容缓存租约，直到 Unload(path)、UnloadUnused() 或 provider Dispose。不要将旧入口返回的对象当成独立租约；上述清理可能使旧对象失去底层持有。需要跨场景或跨 provider 切换存活的对象应使用 AcquireAsync。Unload 会取消该路径尚未完成的旧缓存请求，避免卸载后被旧结果重新填入；独立租约不受它影响。

所有入口和租约释放都应在 Unity 主线程调用。同步 Load 明确不支持，SupportsSyncLoad 为 false。释放最后一个句柄不保证立刻降低进程内存，实际 bundle 回收仍服从 YooAsset 的包设置；UnloadUnused 请求回收零引用资源。

## 更新准备的边界

YooAssetBusinessContentUpdater 仅执行“请求包版本 → 更新清单 → 返回业务 provider”。它不预下载全部资源；内容在实际加载时按所配置文件系统处理。失败不会向 ResourceManager 安装新 provider，取消会传播 OperationCanceledException。

YooAsset 2.3.17 的清单操作不提供公共单操作 Abort。取消仅停止等待，已经发起的 YooAsset 操作仍可能完成并修改该 ResourcePackage 的激活清单。请准备独立业务包，避免用此入口更新正在被旧 provider 服务的共享包并期待原子回滚。同一 updater 在底层操作尚未完成时会拒绝再次准备。资源包和全局 YooAssets 的最终关闭由应用负责，必须等所有消费者的租约释放后再执行。

此包代码使用 MIT；YooAsset 自身采用 Apache-2.0，应保留其独立许可和 notices。
