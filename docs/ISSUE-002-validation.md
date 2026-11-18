# ISSUE-002 workload 验证记录

## 问题列表三项优化（2026-11-18）

已完成 NuGet 发布工作流、dotnet tool 注册和面向已发布包的快速开始。下文最初实现的
旧包名、注册脚本和九包产物记录作为历史证据保留；当前使用 `Chaldea.OpenHarmony.*`，
完整发布为十个包，用户入口改为 `harmonyos-workload init`。

NuGet 官方源查询确认原名 `OpenHarmony.NET.PublishAotCross`、`OpenHarmony.NET.Sdk`
已有包；十个新 ID 查询时均为 HTTP 404。当前命名审计及发布配置见
[发布流程](releases.md)，用户命令见 [README](../README.md)，源码操作见
[项目维护](maintainers.md)。

本次验证结果：

| 验证 | 结果 / 证据 |
| --- | --- |
| 单元测试 | 64 通过；原 55 项 + 9 项注册工具测试；`artifacts/issue002-improvements-tests.log` |
| 官方 manifest 兼容 | 修复官方 JSON 尾随逗号/注释解析后，工具测试 19 项再次通过；`artifacts/issue002-tool-tests.log` |
| NuGet 名称审计 | 十个新 ID 未找到；`artifacts/issue002-nuget-names.json` |
| 发布包集合 | 十个包的 ID、版本、依赖、repository 和 runtime provenance 校验通过；`prepare-release.ps1` |
| GitHub Actions 工作流 | actionlint 1.7.12 检查 build/release 两个工作流通过 |
| 缺少发布包 | 预期失败，尚未创建发布输出目录 |
| 改名后的 Player.Sdk 消费 | x64、arm64 NativeAOT publish 和导出验证通过；`artifacts/issue002-renamed-packages.log` |
| dotnet tool 安装及命令 | 本地源安装 `Chaldea.OpenHarmony.Workload`，生成的命令可执行 |
| 工具生命周期 | init、重复 init、卸载后 unregister、再次 init 均成功 |
| workload 生命周期 | install / list / uninstall / restore 均成功；卸载后得到预期 NETSDK1147 |
| 新包的应用构建与 HAP | x86_64 / arm64-v8a 发布成功，每个 HAP 内 13 个共享库的 ELF 架构正确 |
| 设备运行 | x86_64 / API17，`DOTNET_OHOS result=42 runtime=10 runs=1` |

最终完整工具与 workload 验证目录：`artifacts/wl-test/b6dd4bc1/`，其中 `result.json`、
`tool-install.log`、`tool-command.log`、`tool-init.log`、`tool-unregister.log`、`run.log` 和
`hilog.log` 记录安装与运行证据；汇总日志为 `artifacts/issue002-improvements-verification.log`。
最终发布候选包和 SHA256SUMS 位于 `artifacts/issue002-release-final/`。

本次未触发 GitHub Actions 或推送 NuGet；发布工作流运行需要配置文档中的专用 Windows
runner、`NUGET_SOURCE` 和 `NUGET_KEY`。arm64 仍未进行真机运行验收。

## 最初实现记录

验证日期：2026-11-18。新增 `harmonyos` workload，使用标准
`Microsoft.NET.Sdk` + `net10.0-ohos` 项目完成构建、NativeAOT 发布及 HAP 安装运行。
本次使用本地包源，未上传 NuGet。

## 环境

| 项目 | 实际使用值 |
| --- | --- |
| 主机 | Windows x64 |
| .NET SDK | `artifacts/dotnet-sdk`，10.0.301，feature band 10.0.300 |
| workload manifest | `OpenHarmony.NET.Workload.Manifest-10.0.300`，版本 10.0.10 |
| SDK、模板和运行时 pack | 10.0.10-ohos.2-preview.1 |
| 官方 Harmony CLI | `F:\.huawei\command-line-tools` |
| Native SDK | CLI 内置 API26，26.0.0.105，由 manifest 推导 |
| Java | `D:\Program Files\Huawei\DevEco Studio\jbr` |
| 设备 | API17 / x86_64 模拟器，HDC `127.0.0.1:5555` |
| 应用最低平台版本 | API15（`net10.0-ohos` 默认平台版本 15.0） |

NativeAOT 运行时沿用 ISSUE-001 从源码构建的 API13 baseline，分别使用 arm64 和 x64
运行时包。Harmony SDK 不随 workload 分发，安装 workload 无需预装 Harmony SDK。

## 实现

- manifest 通过 .NET workload resolver 自动导入 `OpenHarmony.NET.Sdk`，无需消费者引用
  仓库源码路径或添加 Player.Sdk NuGet 依赖。
- `dotnet new ohos` 生成目标为 `net10.0-ohos` 的项目和 `App.Run()` 入口。
- `build` 验证 Native SDK 并编译托管代码；`publish` 完成 NativeAOT、Stage 壳生成、
  ohpm/Hvigor 构建，输出未签名 HAP；`run` 在 Debug 模式开发签名后通过 HDC 安装启动。
- 编译用 SDK 从官方 manifest 解析；入口方法、ABI、RID 和应用标识均有验证。
  已安装的 workload pack 在应用构建期间只读。
- 注册脚本只写入指定独立 SDK 的第三方 manifest，并通过 CLI 将该 SDK 切换到
  manifest 更新模式。安装、卸载、restore 及 pack 记录全部由真正的 `dotnet workload`
  命令管理。

`ComputeRunArguments` 在 CLI 进程内执行，不能直接承载 NativeAOT task host；实现会保存
CLI 全局属性，然后使用同一个 SDK 的独立 MSBuild 进程发布和运行，保留设备、包名等参数。
.NET 10 的 workload-set 默认模式会要求下载微软 workload set，故本地第三方 manifest
注册时显式启用 `dotnet workload config --update-mode manifests`。

## 验证命令及结果

```powershell
./build.ps1 -Target Test
./build.ps1 -Target Pack
./tools/verify-workload.ps1 -DotnetRoot ./artifacts/dotnet-sdk `
  -CommandLineTools F:\.huawei\command-line-tools `
  -JavaHome 'D:\Program Files\Huawei\DevEco Studio\jbr' `
  -RunOnDevice -KeepInstalled
```

`verify-workload.ps1` 要求该 SDK 尚未安装 harmonyos，避免卸载既有用户安装。
它在 `artifacts/wl-test/<随机目录>/` 下生成项目、全新 NuGet 缓存及日志，并用空的
Directory.Build.props / targets 和独立 NuGet.Config 隔离仓库设置。

| 检查 | 结果 |
| --- | --- |
| 解决方案构建 | 20 个项目构建成功 |
| 单元测试 | 55 通过，0 失败；其中新增 workload 测试 10 个 |
| 完整打包 | 原有六个包 + SDK、模板、manifest，共九个 nupkg |
| workload install / list | 安装四个 pack，list 显示 harmonyos |
| dotnet new ohos | 新项目生成成功，无仓库 Import 或 PackageReference |
| 未配置 SDK 的 build | 预期失败，`OHOS1001` 给出 SDK 配置提示 |
| 非法 ABI 的 build | 预期失败，提示 `x86_64` / `arm64-v8a` |
| dotnet build | 成功 |
| dotnet publish，x86_64 | 成功；HAP 中 13 个共享库均为 ELF64 / x86_64 |
| dotnet publish，arm64-v8a | 成功；HAP 中 13 个共享库均为 ELF64 / AArch64 |
| dotnet run，x86_64 | 开发签名、官方验签、HDC 安装、启动成功 |
| workload uninstall | 成功移除安装记录和四个 pack |
| 卸载后 build --no-restore | 预期失败，`NETSDK1147` 提示安装 harmonyos |
| workload restore | 根据项目恢复 harmonyos，之后 build 再次成功 |

设备上执行的是新模板生成的 C# 代码，进程日志确认：

```text
DOTNET_OHOS result=42 runtime=10 runs=1
```

## 本机产物

最终完整验证目录：`artifacts/wl-test/5a078bb4/`。

- `result.json`：SDK、两个 HAP 的 SHA-256、ELF 检查数量及设备运行结果。
- `install.log`、`list.log`、`uninstall.log`、`workload-restore.log`：真实 CLI 记录。
- `missing-sdk.log`、`invalid-abi.log`、`missing-workload.log`：错误路径验证。
- `build.log`、`publish-x64.log`、`publish-arm64.log`、`run.log`、`hilog.log`。
- `app/bin/Release/net10.0-ohos/linux-musl-x64/publish/WorkloadSmoke-unsigned.hap`。
- `app/bin/Release/net10.0-ohos/linux-musl-arm64/publish/WorkloadSmoke-unsigned.hap`。
- `app/bin/Debug/net10.0-ohos/linux-musl-x64/publish/WorkloadSmoke-signed.hap`。

| 未签名 HAP | SHA-256 |
| --- | --- |
| x86_64 | `1755879CF28524F6A36BD04EFF317A5BA19C04D719B0EF5DD583770B846DBB00` |
| arm64-v8a | `1487F1A0519347B4F2AFBFADB2655BE27F8D3603C1A29FC507F5F896D9BD6E50` |

打包日志：`artifacts/workload-pack-all.log`；单元测试日志：`artifacts/workload-tests.log`；
汇总执行日志：`artifacts/workload-verification.log`。验证后 harmonyos 保持安装在仓库内的
独立 SDK，未改动系统的 .NET SDK 安装。

## 范围与限制

主机支持 Windows x64；arm64 HAP 已验证交叉编译和包内 ELF，尚未在 arm64 真机运行。
实际设备验证覆盖 API17 / x86_64，未覆盖其它设备/API、网络、TLS、完整全球化和全部 NDK。
默认 Stage UI 是显示整数结果的最小应用壳，不提供 MAUI，也不支持将桌面程序自动变为
完整移动应用。商店发布需要开发者自己的证书和 profile。

现有 Bindings/Demo 源码、旧工具包和官方 Hvigor/Clang 有非致命 warning；所有列出的
命令均按退出码和实际产物检查。运行结果以当前应用 PID 的日志判断，不复用历史成功日志。
