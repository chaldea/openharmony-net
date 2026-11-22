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
  SkiaSharp/               SkiaSharp 4.157.0（包含固定提交的 Skia C API 源码）
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

安装 .NET SDK 11.0.100 或同一 11.0.100 feature band 的补丁版；`global.json` 固定 feature band。
普通构建和测试不需要 Harmony SDK，也不要求先生成 runtime 包。

```powershell
git -c core.longpaths=true submodule update --init --recursive
./build.ps1 -Target Test
./build.ps1 -Target PackTools
```

runtime 的部分源码路径在 Windows 上超过 260 字符；初始化子模块时需启用 Git 长路径支持。
CI 和发布任务通过进程环境中的 `core.longpaths=true` 在检出前启用该选项。

也可直接执行 `dotnet build OpenHarmony.slnx -c Release` 和
`dotnet test OpenHarmony.slnx -c Release`。八个工具/manifest 包输出至 `artifacts/packages/`：
Chaldea.OpenHarmony.NDK.Bindings、PublishAotCross、Player.Sdk、HapTooling（均使用 Chaldea.OpenHarmony 前缀）。
以及 Chaldea.OpenHarmony.Sdk、Chaldea.OpenHarmony.Templates、Chaldea.OpenHarmony.Workload.Manifest-11.0.100 和 Chaldea.OpenHarmony.Workload。
Player.Sdk 包声明两个运行时包依赖，因此完整消费它还需要下面的原生运行时构建。

## 从源码构建运行时

Windows 主机需要 PowerShell 7.3+、Git for Windows、Visual Studio C++ 构建工具、
Windows SDK、.NET 11，以及官方 API 13 和 API 15 Native SDK。
API 13 用于运行时兼容基线；ICU/OpenSSL 依赖按现有移植约定使用 API 15。
完整流程不会以仓库里的旧 .NET 9 二进制代替 .NET 11 构建。

```powershell
# 提前检查主仓库、runtime 的 SDK 配置及已安装的正式版 SDK。
pwsh ./tools/resolve-runtime-sdk.ps1

# 下载官方 Native SDK，校验服务端公布的 SHA-256 后解压到本地 artifacts。
./tools/install-native-sdk.ps1

# 下载并校验固定版本的 ICU/OpenSSL，构建主机工具与两个目标架构。
pwsh ./tools/prepare-native-dependencies.ps1 -SdkRoot ./artifacts/native/sdk

# 构建 x64 和 arm64 的 .NET 11 NativeAOT、基础库及原生库。
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
校验失败时直接报错。运行时源码构建要求已安装 SDK 11.0.100，默认复用 PATH 中的
`dotnet`（优先使用 `artifacts/dotnet-sdk11` 中符合版本的本机安装）；也可通过
`-RuntimeDotnetRoot <安装目录>` 指定。`resolve-runtime-sdk.ps1` 在编译前检查主仓库
`sdk.version` 与 runtime 的 `sdk.version`、`tools.dotnet` 一致，并执行 runtime 目录下的
`dotnet --version`。CI 在准备原生依赖前执行相同检查，再通过 `DOTNET_INSTALL_DIR`
将正式 SDK 传给 Arcade，避免重复下载引导 SDK。

## 底层示例与源码调试

示例调用 C# 的整数求和、集合分配和 GC，并显示运行时主版本。
模拟器验证必须同时得到 `result=42` 和 `runtime=11`。

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
当前 .NET 11 升级的输入、兼容处理与验证结果见 [ISSUE-006 记录](ISSUE-006-validation.md)。
历史 .NET 10 构建步骤见 [ISSUE-001 记录](ISSUE-001-validation.md)。

验证 CrossCompileHello 和旧 OpenHarmony.Demo（脚本自动生成隔离的 Stage 宿主）：

```powershell
./tools/verify-legacy-examples.ps1 -SdkRoot ./artifacts/native/sdk `
  -CommandLineTools $env:OHOS_COMMAND_LINE_TOOLS -JavaHome $env:JAVA_HOME -RunOnDevice
```

## 维护

五个第三方子模块的 URL 和提交由 `.gitmodules` / Git 索引固定。
`src/`、`tests/`、`tools/` 和 `examples/` 中的改动直接提交到本仓库。
统一的 CI 工作流位于 `.github/workflows/build.yml`，由 PR 或手动触发，push 不触发。
`build` 在 Windows 执行托管构建、单元测试和工具打包，并上传 `workload-packages`，
仅用于传递给测试 job，保留 1 天。
`test` 使用 Windows、Ubuntu、macOS 矩阵下载同一制品，按 `global.json` 中的精确版本
安装独立的可写 .NET SDK（避免 `latestFeature` 切换到不匹配的 workload feature band）、
JDK 17 和 `ErBWs/setup-ohos@v2` 提供的 HarmonyOS CLI 6.1.1.280；Ubuntu 额外安装
`libgl1`。Linux 和 macOS 直接从 `ErBWs/ohos-sdk` 的 GitHub Release 下载、校验并解压，
省去 Actions cache 的归档和上传。Windows 安装耗时更长，可以恢复由默认分支共享的
已解压 `command-line-tools` 目录。维护者可在默认分支手动运行 **Build and test** 来填充
当前 CLI 版本的 Windows 缓存；PR 仅恢复这个缓存，未命中时从上游 Release 安装。
缓存中只有 Windows CLI 目录，源 ZIP 留在 runner 的临时磁盘上。CLI 根目录由 action 的
`HOS_SDK_HOME` 推导，先通过 `hvigorw -v` 检查。
每个平台从制品安装工具和 workload，实际执行 `dotnet new ohos`、build、两种 ABI 的
publish，检查 HAP 内原生库的 ELF 架构，并验证卸载和 workload restore。
测试 job 不上传 HAP、日志或测试报告；结果（含 HAP 哈希）写入 job 日志。
预期失败场景验证通过后显示 `PASS: negative test`，只有实际异常才打印原始失败日志并
终止任务。某个平台失败不会取消其他平台。

PR 制品中的托管和工具包由当前源码构建。`tools/ci-runtime-packages.json` 中的
`buildFromSource: true` 表示使用从固定源码构建的 runtime 基线。PR 和 tag 发布从同一份
GitHub Release 下载基线，在当前主仓库提交上重新生成 manifest 和两个 NuGet 包。
`tools/prepare-ci-packages.ps1` 核对版本、runtime 源码提交和干净构建 provenance，再组成
`artifacts/ci-packages/`。GitHub Actions 在 PR 中保存的缓存属于 `refs/pull/<n>/merge`，
其他 PR 和 tag 发布无法恢复该缓存。基线 Release 可以跨这些引用下载，也独立于 Actions
cache 配额及工作流制品保留期。

Runtime 提交或 SDK、原生依赖构建输入变化时，维护者在有原生工具链的 Windows 主机上
按「从源码构建运行时」一节生成新基线。已完成同一构建的原生产物可直接导出。
以下命令在对应主仓库提交的工作目录执行，并将 `--repo` 指向 CI 使用的仓库。

```powershell
$tag = ./tools/runtime-baseline.ps1 -Action GetTag
./tools/runtime-baseline.ps1 -Action Export
$assets = "./artifacts/runtime-baselines/$tag"
gh release create $tag "$assets/runtime.zip" "$assets/runtime.zip.sha256" `
  --repo chaldea/openharmony-net --prerelease --latest=false `
  --title "NativeAOT runtime baseline $tag" --notes 'API13 NativeAOT build outputs for x64 and arm64.'
```

`runtime-` tag 包含 runtime 子模块提交和构建输入指纹。同一基线供所有 workload 版本复用。
发布资产后重跑对应的 PR 或 tag 工作流。CI 默认读取自身仓库的 Release；fork 可将
Actions 变量 `RUNTIME_BASELINE_REPOSITORY` 设为存放基线的公开仓库，例如
`chaldea/openharmony-net`。缺少基线、校验和不匹配或 provenance 不符时，CI 会报错。

也可以将 `buildFromSource` 改为 `false`，在 CI 配置中填入从 NuGet 下载的同版本签名包的
SHA-256。本地复现可构建或恢复基线，执行 `PackTools`、`PackRuntime` 和
`tools/prepare-ci-packages.ps1`，再给下面的 `verify-workload.ps1` 传入
`-PackageSource ./artifacts/ci-packages`。使用恢复目录时，将其作为 `PackRuntime` 的
`-RuntimeArtifacts` 参数传入。

原生 runtime 编译、签名和设备验收需要安装 Harmony 工具链的 Windows 主机。

图形库产物存于 `artifacts/native/skia/{x86_64,arm64-v8a}/`，含源码/SDK/哈希 manifest。
普通 `Build`、`Test`、`PackTools` 不要求这些原生产物，完整 `Pack` 缺少任一 ABI 时失败。
参见 [Skia 示例](../examples/SkiaDemo/README.md) 和 [ISSUE-003 验证记录](ISSUE-003-validation.md)。

发布流程及 runner/secrets 设置见 [NuGet 发布流程](releases.md)。验证新打包的工具和 workload：

```powershell
./tools/verify-workload.ps1 -DotnetRoot ./artifacts/dotnet-sdk11 `
  -CommandLineTools F:\.huawei\command-line-tools `
  -JavaHome 'D:\Program Files\Huawei\DevEco Studio\jbr' `
  -RunOnDevice -KeepInstalled
```

所选独立 SDK 必须尚未安装 harmonyos；脚本使用全新缓存，从本地 NuGet 包安装工具，
验证注册、安装、两个架构的 HAP、卸载和恢复。`-RunOnDevice` 需要已启动的 x86_64 模拟器。

导入来源和历史制品说明见 [迁移记录](repository-migration.md)。
