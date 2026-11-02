# 订阅生命周期与异步资源更新

本次更新集中补齐事件订阅清理和异步资源所有权，保持核心与可选资源后端分离。

## 更新内容

- 新增 `SubscriptionScope`，统一释放一段生命周期内的事件订阅。作用域捕获实际总线；每次注册拥有独立委托身份，注册失败的回滚不会移除其他拥有者的监听。
- `UIBase.OpenSubscriptions` 跟随一次打开周期，在开始关闭、打开失败或销毁时清理。销毁后拒绝重新订阅。首批接入 LoadingPanel 和 ErrorPanel。
- 新增 `ResourceLease<T>`、`IResourceLeaseProvider` 与可取消的 `ResourceManager.AcquireAsync`。每次借用独立持有资源，重复释放无效。
- 默认 Resources 后端也支持租约，保护有效借用，避免未使用资源扫描提前回收。
- 业务 Provider 替换、管理器销毁、取消与过期更新均处理迟到结果；清理错误单独记录，不覆盖原取消/过期异常。
- 新增可选 `com.zwebglframework.yooasset` 包，提供异步业务资源加载、旧入口兼容缓存，以及版本/清单准备。核心运行时不引用 YooAsset。

UniTask 沿用工程已有的 2.5.11；YooAsset 2.3.17 固定至上游提交 `101236db8a58a327071ff1ab35d28bc1535ca1c1`。
资源请求合并由 YooAsset 处理，未额外增加请求调度器、对象池或每帧扫描。

## 接入和兼容

旧 `Load` / `LoadAsync` 入口保留，返回对象服从 Provider 兼容缓存的生命周期。
需要跨 Provider 切换存活的资源或实例应持有独立租约，直到全部消费者停止使用后在 Unity 主线程释放。

框架启动与错误 UI 使用本地 Resources。应用负责资源收集、地址规则、资源包初始化、文件系统和最终关闭。
未自动迁移业务资源或设置 CDN。初始化和持有示例见 [适配包 README](../../Packages/com.zwebglframework.yooasset/README.md)。

## 验证

实际验证环境为 Unity 2022.3.62f3c1；下表按程序集列出最后的通过结果，集成用例单独重跑验收。

| 检查 | 结果 |
| --- | --- |
| Core EditMode | 54/54 通过 |
| YooAsset 句柄与更新流程 | 16/16 通过 |
| Editor 回归 | 45/45 通过 |
| 真实本地 Bundle 集成 | 1/1 通过 |
| Runtime PlayMode | 218/218 通过 |
| Combat PlayMode | 27/27 通过 |
| 独立核心消费项目 | 5/5 通过，实际依赖闭包中无 YooAsset |
| 独立核心＋适配包消费项目 | 7/7 通过 |
| 独立 GitHub UPM Git 消费项目 | 7/7 通过，两个包解析到同一完整提交 SHA |
| 仓库 Node 合同测试 | 112/112 通过 |
| Python CI 校验器测试 | 10/10 通过 |
| Unity meta、C# 文件行数、JSON/asmdef、差异格式、包治理检查 | 通过 |
| 最终 WebGL IL2CPP 构建 | 通过，0 错误、1 警告 |
| HTTPS Chromium 冒烟 | 1/1 通过，验证启动与实际按钮点击 |

真实资源包测试使用当前编辑器活动平台构建两个主 Bundle 和共享纹理依赖，实际执行 OfflinePlayMode
初始化、版本/清单准备及异步加载。它核对租约对象与已加载 Bundle 中的对象身份，并验证独立租约、
业务 Provider 替换后的存活、逐步释放和最终依赖卸载；临时配置、资产与输出在测试后清理。

两个本地消费项目通过 UPM 包路径接入框架，不复制宿主 Assets、不使用反射或友元程序集。
另一个全新项目通过 GitHub Git 子目录同时安装核心和适配包，执行 7 项公开 API 测试，
并核对两个包的锁文件来源与提交均为 `2608415a643b37968492118057cbb13482c08538`。
正式发布冻结和完整发行门禁仍有独立流程。

新增生命周期和资源用例已加入 CI 的必需测试清单，避免测试程序集被遗漏时误报通过。

GitHub Actions 的审计及 ECS、Network、SimulationCore 三组 .NET 检查已通过。
云端 Unity 作业在启动测试前因仓库未配置 `UNITY_LICENSE` 中止，因此依赖它们的构建和 Windows 冒烟未执行。
本地 Unity、WebGL 构建和浏览器验证结果如上。

## 边界

YooAsset 2.3.17 的清单操作没有公开单操作 Abort。取消会停止等待并阻止安装新 Provider，已发起操作仍可能修改该 ResourcePackage。
准备更新时应使用独立业务包，不能对正在服务的共享包承诺原子回滚；包的关闭必须等待消费者租约释放。

本地资源包验收不能替代实际 CDN、弱网、目标浏览器兼容和线上资源版本切换测试。
WebGL 的托管分配、包体和内存收益尚未进行对比测量。
