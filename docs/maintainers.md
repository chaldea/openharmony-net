# 项目维护

本文用于修改源码、构建制品和发布。应用开发者请先阅读 [快速开始](../README.md)。

## 目录

```text
artifacts/                 构建产物、NuGet 包、日志和本机工具（不提交）
docs/                      构建说明和历史设计
dotnet/                    第三方源码（Git submodule）
  ClangSharp/
  core-origin/
  runtime/                 OpenHarmony runtime 移植
  runtime-origin/          上游比较基线
  SkiaSharp/               SkiaSharp 2.88.9（包含固定提交的 Skia C API 源码）
src/
  OpenHarmony.NDK.Bindings/
  OpenHarmony.NET.HapTooling/
  OpenHarmony.NET.Player.Sdk/
  OpenHarmony.NET.Runtime/
  OpenHarmony.NET.Sdk/      workload 构建、HAP、签名和运行
  OpenHarmony.NET.Templates/
  OpenHarmony.NET.Workload/
  PublishAotCross/
  SkiaSharp.NativeAssets.HarmonyOS/
  HarfBuzzSharp.NativeAssets.HarmonyOS/
tests/                     绑定、生成器、SDK resolver、runtime packager 和 ABI 测试
tools/                     生成器、打包器、构建和验证脚本
  WorkloadInstaller/       NuGet 分发的 harmonyos-workload 工具
examples/NativeAotSmoke/    ArkTS → N-API → C# NativeAOT 最小应用
examples/SkiaDemo/          Skia 绘图、HarfBuzz 字形整形与 ArkUI PixelMap 显示
OpenHarmony.slnx            统一解决方案
build.ps1                  构建入口
```

## 构建与测试

安装 .NET SDK 10.0.301 或更新的 .NET 10 feature band；`global.json` 固定主版本。
普通构建和测试不需要 Harmony SDK，也不要求先生成 runtime 包。

```powershell
git submodule update --init --recursive
./build.ps1 -Target Test
./build.ps1 -Target PackTools
```

也可直接执行 `dotnet build OpenHarmony.slnx -c Release` 和
`dotnet test OpenHarmony.slnx -c Release`。八个工具/manifest 包输出至 `artifacts/packages/`：
Chaldea.OpenHarmony.NDK.Bindings、PublishAotCross、Player.Sdk、HapTooling（均使用 Chaldea.OpenHarmony 前缀）。
以及 Chaldea.OpenHarmony.Sdk、Chaldea.OpenHarmony.Templates、Chaldea.OpenHarmony.Workload.Manifest-10.0.300 和 Chaldea.OpenHarmony.Workload。
Player.Sdk 包声明两个运行时包依赖，因此完整消费它还需要下面的原生运行时构建。

## 从源码构建运行时

Windows 主机需要 PowerShell 7.3+、Git for Windows、Visual Studio C++ 构建工具、
Windows SDK、.NET 10，以及官方 API 13 和 API 15 Native SDK。
API 13 用于运行时兼容基线；ICU/OpenSSL 依赖按现有移植约定使用 API 15。
完整流程不会以仓库里的旧 .NET 9 二进制代替 .NET 10 构建。

```powershell
# 下载官方 Native SDK，校验服务端公布的 SHA-256 后解压到本地 artifacts。
./tools/install-native-sdk.ps1

# 下载并校验固定版本的 ICU/OpenSSL，构建主机工具与两个目标架构。
pwsh ./tools/prepare-native-dependencies.ps1 -SdkRoot ./artifacts/native/sdk

# 构建 x64 和 arm64 的 .NET 10 NativeAOT、基础库及原生库。
pwsh ./tools/build-runtime.ps1 -SdkRoot ./artifacts/native/sdk

# 编译 SkiaSharp / HarfBuzzSharp，需 Python 3.12+、Ninja 和固定版本上游源码。
git submodule update --init dotnet/SkiaSharp
git -C dotnet/SkiaSharp submodule update --init --depth 1 externals/skia
pwsh ./tools/build-skia.ps1 -SdkRoot ./artifacts/native/sdk

# 生成两个运行时包、八个工具/workload 包和两个图形 NativeAssets 包。
./build.ps1 -Target Pack

# 使用独立 NuGet 缓存验证 Player.Sdk 的两个架构及导出符号。
./tools/verify-packages.ps1 -SdkRoot ./artifacts/native/sdk
```

已有 API 13/15 的 SDK 根目录可以通过 `-SdkRoot` 直接传入。构建产物存于
`artifacts/native/runtime/api13/<arch>/Release/`；运行时打包目录为
`artifacts/runtime/releases/<version>/`。`Pack` 在运行时输入缺失或 provenance
校验失败时直接报错。运行时源码构建会按 `dotnet/runtime/global.json` 获取其所需 SDK。

## 底层示例与源码调试

示例调用 C# 的整数求和、集合分配和 GC，并显示运行时主版本。
模拟器验证必须同时得到 `result=42` 和 `runtime=10`。

```powershell
./tools/build-sample.ps1 `
  -SdkRoot ./artifacts/native/sdk `
  -CommandLineTools F:\.huawei\command-line-tools `
  -JavaHome 'D:\Program Files\Huawei\DevEco Studio\jbr'

# 使用官方 SDK 附带的公开开发证书签署本地模拟器示例。
./tools/sign-sample.ps1 `
  -CommandLineTools F:\.huawei\command-line-tools `
  -JavaHome 'D:\Program Files\Huawei\DevEco Studio\jbr'

./tools/verify-sample.ps1 -CommandLineTools F:\.huawei\command-line-tools
```

签名和验证前需启动 x86_64 模拟器，默认 HDC 目标为 `127.0.0.1:5555`；
其它目标通过两个脚本的 `-Device` 参数指定。启动命令见下面的验证记录。

HAP 输出至 `artifacts/sample/`。本机 SDK/Java 路径通过参数提供，证书及签名配置不提交。
详细验证步骤和实际结果见 [构建与验证记录](ISSUE-001-validation.md)。

## 维护

五个第三方子模块的 URL 和提交由 `.gitmodules` / Git 索引固定。
`src/`、`tests/`、`tools/` 和 `examples/` 中的改动直接提交到本仓库。
统一的 CI 工作流位于 `.github/workflows/build.yml`，由 PR 或手动触发，push 不触发。
`build` 在 Windows 执行托管构建、单元测试和工具打包，并上传 `workload-packages`。
`test` 使用 Windows、Ubuntu、macOS 矩阵下载同一制品，安装独立的可写 .NET SDK、
JDK 17 和 `ErBWs/setup-ohos@v2` 提供的 HarmonyOS CLI 6.1.1.280；Ubuntu 额外安装
`libgl1`。CLI 根目录由 action 的 `HOS_SDK_HOME` 推导，先通过 `hvigorw -v` 检查。
每个平台从制品安装工具和 workload，实际执行 `dotnet new ohos`、build、两种 ABI 的
publish，检查 HAP 内原生库的 ELF 架构，并验证卸载和 workload restore。
各平台独立上传 HAP、日志和 `result.json`；某个平台失败不会取消其他平台。

PR 制品中的八个托管/工具包由当前源码构建。两个原生运行时包复用已发布的固定版本，
由 `tools/prepare-ci-packages.ps1` 下载并按 `tools/ci-runtime-packages.json` 中的 SHA-256
验证，与当前构建的包一起放入空的 `artifacts/ci-packages/`。这些哈希针对 NuGet
下载包（含仓库签名），不能使用发布前本地 nupkg 的哈希代替。更新包版本时需要同步
已发布运行时基线及哈希；PR 矩阵不替代运行时源码修改的完整发布验证。
本地复现使用 `build.ps1 -Target PackTools`、`tools/prepare-ci-packages.ps1`，再给下面的
`verify-workload.ps1` 传入 `-PackageSource ./artifacts/ci-packages`。

原生 runtime 编译、签名和设备验收需要安装 Harmony 工具链的 Windows 主机。

图形库产物存于 `artifacts/native/skia/{x86_64,arm64-v8a}/`，含源码/SDK/哈希 manifest。
普通 `Build`、`Test`、`PackTools` 不要求这些原生产物，完整 `Pack` 缺少任一 ABI 时失败。
参见 [Skia 示例](../examples/SkiaDemo/README.md) 和 [ISSUE-003 验证记录](ISSUE-003-validation.md)。

发布流程及 runner/secrets 设置见 [NuGet 发布流程](releases.md)。验证新打包的工具和 workload：

```powershell
./tools/verify-workload.ps1 -DotnetRoot ./artifacts/dotnet-sdk `
  -CommandLineTools F:\.huawei\command-line-tools `
  -JavaHome 'D:\Program Files\Huawei\DevEco Studio\jbr' `
  -RunOnDevice -KeepInstalled
```

所选独立 SDK 必须尚未安装 harmonyos；脚本使用全新缓存，从本地 NuGet 包安装工具，
验证注册、安装、两个架构的 HAP、卸载和恢复。`-RunOnDevice` 需要已启动的 x86_64 模拟器。

导入来源和历史制品说明见 [迁移记录](repository-migration.md)。
