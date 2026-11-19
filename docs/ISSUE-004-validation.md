# ISSUE-004：多平台 workload 验证

## 实现

`.github/workflows/build.yml` 仅响应 `pull_request` 和 `workflow_dispatch`。
Windows `build` job 构建并测试源码、打包八个托管/工具包，再补入固定版本且通过
SHA-256 校验的两个已发布原生运行时包，上传完整的 `workload-packages` 制品。
原生运行时基线和哈希记录在 `tools/ci-runtime-packages.json`；完整运行时源码重建
仍由发布流程负责。

`test` job 在 `windows-latest`、`ubuntu-latest`、`macos-latest` 上分别下载同一制品，
使用独立 .NET SDK 和 NuGet 缓存安装 workload，创建模板工程，并发布 x86_64、
arm64-v8a HAP。检查包含的原生库和 ELF 架构后，继续验证卸载及恢复。
三个 job 独立保存 HAP、日志和结果，任一失败不会取消其他平台。

安装器、manifest、SDK targets 和 PowerShell 脚本已适配 Windows x64、Linux x64、
macOS x64/arm64，包括可执行文件后缀、PATH 分隔符、Node 目录、Arm64 安装记录和
macOS 的 ELF 链接器探测。HarmonyOS 使用官方 SDK 自带的 LLVM 工具链。

## 本机结果（2026-11-19）

- Windows x64、独立 .NET SDK 10.0.301、HarmonyOS SDK 26.0.0.105 / API26。
- 71 项原有及安装记录测试通过，新增 2 项实际执行 MSBuild 的双架构链接参数回归测试通过。
- `PackTools` 成功；CI feed 的十个包齐全，两个 NuGet 运行时包下载及固定哈希校验通过。
- 全新 SDK 完成工具安装、重复注册、workload 安装、模板创建、build、两个 ABI 的 publish、
  uninstall、unregister、重新注册、workload restore、再次 build 和最终卸载。
- 缺少 SDK、非法 ABI、卸载后缺少 workload 的负向检查均按预期失败。
- 两个 HAP 各含 13 个原生库，ELF64 / little-endian 及目标架构检查通过。
- PowerShell 语法解析、MSBuild XML 解析、actionlint 和 `git diff --check` 通过。

本机证据保存在 `artifacts/wl-test/113529a3/`，总日志为
`artifacts/issue004-workload-final.log`。HAP SHA-256：

| ABI | SHA-256 |
| --- | --- |
| x86_64 | `24d210662cde8b308ebfd6f2a783a85a9d33a2ef057b268e4d5881f20aacefff` |
| arm64-v8a | `38063f2f4bdfd53ccbc611f1330359b1bf02b4fd5ba2448f0123142890584167` |

## GitHub Actions 验证（2026-11-19）

主仓 [PR #2](https://github.com/chaldea/openharmony-net/pull/2) 的
[CI 运行 44470831228](https://github.com/chaldea/openharmony-net/actions/runs/44470831228)
验证了提交 `8f659e4`。Windows 构建成功；Windows x64、Ubuntu x64、macOS arm64
均通过 `ErBWs/setup-ohos@v2` 安装 CLI 6.1.1.280 / API24 SDK，并完成上述 workload
安装、模板创建、双架构 HAP 发布、ELF 检查、卸载和恢复流程。

三个平台上传的 `result.json` 均记录 `success: true`、SDK `10.0.301` 和 feature band
`10.0.300`。六个 HAP 各含 13 个原生库，下载后再次核对 SHA-256 均与结果文件一致。
下载证据位于 `artifacts/issue004-ci-results/{windows,ubuntu,macos}/`。

首轮 CI 发现 `setup-dotnet` 的 `global-json-file` 会遵循 `latestFeature`，安装
10.0.404，导致注册工具拒绝不匹配的 feature band。测试 job 已改为从 `global.json`
读取精确 SDK 版本，通过 `dotnet-version` 安装，并在安装 HarmonyOS SDK 前检查版本。
本次矩阵验证未执行设备安装运行，也未覆盖 macOS x64 runner。
