# ISSUE-006：.NET 11 与 SkiaSharp 升级

## 固定输入

版本核对日期：2026-11-19。使用稳定版 .NET 11.0.0、SDK 11.0.100、
SkiaSharp 4.157.0 和配套 HarfBuzzSharp 14.4.0.200。
本仓库包版本为 `11.0.0-ohos.1-preview.1`，workload feature band 为 `11.0.100`。

| 子模块 | 选择与原因 |
| --- | --- |
| `dotnet/core-origin` | 更新至 `33918e8a`；`release-notes/11.0/releases.json` 确认 2026-11-10 发布 .NET 11.0.0 / SDK 11.0.100 |
| `dotnet/runtime-origin` | 更新至正式版 tag `v11.0.0`：`deae2e32bf0e018e09d64597b7c59b62b73bb3ce` |
| `dotnet/runtime` | 以相同正式版提交为基线，迁入原来的 15 个 OpenHarmony 提交，并将构建 SDK 固定为正式版；最终提交 `4801b7527a2618c2515bbf31d7ba63d68d14a777`，远端分支 `codex/net11-openharmony` |
| `dotnet/SkiaSharp` | 更新至最新稳定版 tag `v4.157.0`：`2a53a3c8641073bc914c63ee158bbf62b8a0bb92` |
| Skia 内嵌子模块 | `51cb594d773b5383a45065ea3c8cbec398ad2c36`，由 SkiaSharp tag 固定 |
| `dotnet/ClangSharp` | 保持 `dbfa51b734a00bd259dc6c197a68818bea5bcc26`；它不参与 runtime / workload 编译，绑定生成使用独立工具清单，升级 .NET 不要求改变 Clang ABI |

上游参考：[runtime tag](https://github.com/dotnet/runtime/releases/tag/v11.0.0)、
[SkiaSharp tag](https://github.com/mono/SkiaSharp/releases/tag/v4.157.0)。
GitHub 的 `/releases/latest` 当时返回维护线 4.156.2；已交叉核对 tags、release 列表和
NuGet 版本列表，4.157.0 是已发布的最高稳定版本，未选择 4.158/4.159 预览版。
安装 SDK 的 `.version` 中的 `297acf930f…` 是产品构建仓库提交，不作为 runtime 源码 tag。

## 移植与兼容处理

原 runtime 提交范围为 `8f030f80c0d..ce6f99dbe71`。迁移保留了 API13 基线、
API13–24/26 SDK 映射、musl RID、NativeAOT emulated TLS、预编译 thunk 和独立构建目录。
13 个文件有冲突，按 .NET 11 实现逐处合并，保留新增的 OpenBSD/Android 条件、
Apple mobile 条件、thunk 映射大小和版本符号 retain/weak 属性。
.NET 11 的私有 libunwind 构建还要求 Git for Windows 的 shell 和 GNU sort。
.NET 11 新增的跨进程命名互斥量依赖 robust pthread mutex 或共享可写 `/tmp`，
HarmonyOS 不提供这些条件。因此原生库选择上游 unsupported 实现，托管侧关闭
`FeatureCrossProcessMutex`，保留此前 NativeAOT 的进程内命名互斥量行为。

托管项目、模板、workload resolver、安装器、打包脚本和验收条件改为 .NET 11。
`global.json` 使用 `latestPatch`，防止选择与 manifest 不匹配的 feature band。
runtime 打包校验构建 provenance 的 `sourceCommit`，拒绝将旧源码产物重新标为新版。
.NET 11 默认给 Unix NativeAOT 共享库添加 `lib` 前缀；公共构建目标显式设置
`UseNativeLibPrefix=false`，保持 Stage 桥接和 HAP 的 `程序集名.so` 约定。
包消费验证和 smoke 发布使用每次独立的输出目录，防止历史产物掩盖命名变化。
历史发布元数据保持原样。
制品验收发现旧 ICU 缓存含错误的 `RUNPATH=RIGIN`；使用仓库脚本清理重建两种 ABI，
不放宽 ELF 验证。依赖准备脚本现在会检查 RPATH/RUNPATH，并将刚编译的 ICU
覆盖到暂存目录，避免仅因版本元数据相同而继续使用旧库。

Skia 157 使用 C++20；API15 libc++ 不支持一处括号式聚合初始化。
构建脚本在导出的源码中把 `fData.emplace(ci, ci)` 等价改为
`fData.emplace(Data{ci, ci})`，先验证固定源码内容；子模块保持干净。
源码缓存按提交隔离，构建 manifest 记录脚本哈希。图形包精确固定托管依赖版本。
SkiaDemo 使用 4.x 的 `SKFont`、`SKPathBuilder`、字形/位置 Span 属性，
并在绘制入口验证实际运行时主版本为 11。
旧 OpenHarmony.Demo 只使用 Avalonia 的 EGL 接口，其引用收敛为 `Avalonia`，
避免通过未使用的 `Avalonia.Skia` 引入旧版 SkiaSharp。
`tools/verify-legacy-examples.ps1` 为该原生库示例生成隔离的 Stage 验证宿主，
检查 ArkTS → C# 加法、运行时版本及 XComponent 的 EGL 初始化；同时验证
CrossCompileHello 的进程内命名互斥量。

尚未发布的 .NET 11 runtime 在 CI 中从源码构建，使用 `PackRuntime` 产生双 ABI 包；
发布后才可切换到经过 NuGet 下载哈希验证的缓存基线。

## 验证记录

首次验证日期：2026-11-19，Windows x64 主机。应用与工具使用 SDK 11.0.100；runtime
当时按上游 `global.json` 引导 SDK 11.0.100-rc.1.26425.128，编译正式版 v11.0.0 源码。
该轮 runtime provenance 对应 `9674bea1fd5eedfeea1abc039a7315e43f40ed68`。
2026-11-20 的 CI SDK 修正与重新验证记录见下文。

| 验证项 | 结果与证据（相对仓库根目录） |
| --- | --- |
| 主解决方案 | 77 通过、0 失败；`artifacts/issue006-tests-final.log` |
| runtime PowerShell 测试 | 88 通过、0 失败、5 跳过；跳过项依赖未构建的 API15 fixture，API13 实际制品另行验证；`artifacts/issue006-runtime-tests.log` |
| API13 NativeAOT runtime | x64 / arm64 均完整构建成功；`artifacts/issue006-runtime-build.log` |
| 稳定 SDK runtime 回归 | runtime `global.json` 的 `sdk.version` 与 `tools.dotnet` 均为 11.0.100；显式/自动 SDK 选择、路径含空格、缺失 SDK 和旧 RC 配置检查通过；`artifacts/issue006-sdk-preflight-tests.log` |
| 稳定 SDK NativeAOT | 使用 SDK 11.0.100 重新构建 API13 x64 / arm64；两个 ABI 的 12 个 AOT 输入、11 个原生库、479 个托管文件校验通过；`artifacts/issue006-runtime-stable-verify-x64.log`、`artifacts/issue006-runtime-stable-verify-arm64.log` |
| runtime 实际制品检查 | 两个 ABI 各 12 个 AOT ELF 输入、11 个原生库、479 个托管文件；源码、架构、符号、依赖和无 RPATH 检查通过；`artifacts/issue006-pack-verify.log` |
| SkiaSharp / HarfBuzz | 两个 ABI 均由固定源码重新编译；`artifacts/issue006-skia-build.log`，`artifacts/native/skia/<ABI>/manifest.json` |
| 全部制品 | 12 个 nupkg；最终内容、依赖闭包和哈希校验通过；`artifacts/issue006-release-final/`（含 `SHA256SUMS`） |
| CI 本地包源 | 源码基线模式通过，10 个 workload 相关包；`artifacts/issue006-ci-packages-final/` |
| release 元数据 | manifest、SPDX SBOM、SHA256SUMS 已导出并验证；`src/OpenHarmony.NET.Runtime/release/11.0.0-ohos.1-preview.1/` |
| Player.Sdk 包消费 | 使用独立 NuGet 缓存和全新输出，x64 / arm64 发布及 ELF/导出符号检查通过；`artifacts/package-validation/d02b4bb2/result.json` |
| workload 生命周期 | init（含重复执行）、安装、模板、负向检查、build、双 ABI publish、run、卸载、重新注册、restore 和清理均通过；`artifacts/wl-test/60f0d44e/result.json` |
| 工作流与差异 | actionlint、PowerShell 语法解析和 `git diff --check` 通过 |

workload 生命周期验证设置 `MSBUILDDISABLENODEREUSE=1`，避免 Windows 上复用的
MSBuild 节点占用 workload task DLL，影响同一轮安装/卸载检查。

设备为 API17 / x86_64 模拟器 `127.0.0.1:5555`，日志均按本次应用 PID 检查。

| 示例 | 实际运行结果 | 证据 |
| --- | --- | --- |
| workload 模板 | `DOTNET_OHOS result=42 runtime=11 runs=1`；x64 / arm64 HAP 架构验证通过 | `artifacts/wl-test/60f0d44e/hilog.log` |
| NativeAotSmoke | `DOTNET_SMOKE result=42 runtime=11 runs=1`；通过命名互斥量、分配、LINQ 和 GC 检查 | `artifacts/sample/evidence/` |
| SkiaDemo | 显示 .NET 11 绘图、渐变、路径和文字，640×800 像素、17 个字形；两个 ABI HAP 验证通过 | `artifacts/skia-validation/3f565e18/` |
| CrossCompileHello | `Hello World` 与 `CROSS_COMPILE_HELLO runtime=11 mutex=ok`；两个 ABI 可执行文件发布/ELF 检查通过 | `artifacts/legacy-examples/2305f33c/hello.log` |
| OpenHarmony.Demo | `CSharpAdd(19,23)=42`、`runtime=11`、`egl init success`，截图确认 XComponent 蓝色画面；两个 ABI 原生库及验证宿主 HAP 生成成功 | `artifacts/legacy-examples/2305f33c/` |

已人工查看 SkiaDemo 和 OpenHarmony.Demo 截图。arm64 本轮验证到交叉编译、ELF 与 HAP，
未连接 arm64 真机；其他 API 设备和 Linux/macOS 主机未在本轮执行。
API13 是 runtime 构建基线，图形库与第三方依赖按现有约定使用 API15；API 映射不等于
在每个 API 镜像上完成运行验收。

上述是本地验收制品，版本为 `11.0.0-ohos.1-preview.1`，尚未发布到 NuGet。
这些制品在主仓库提交前构建；包内主仓库 `RepositoryCommit` / `bindingsCommit` 记录
构建时的 HEAD `c30462caa00cba7e54aec394092b72bdf3dc0aa7`，实际源码包含本次升级变更，
不能当作已发布的干净 release tag。
2026-11-20 提交前已确认 runtime 移植提交可通过
[`chaldea/openharmony-net-runtime` 的 `codex/net11-openharmony` 分支](https://github.com/chaldea/openharmony-net-runtime/tree/codex/net11-openharmony)
获取；主仓库的子模块指针使用同一提交。NuGet 包尚未发布。
