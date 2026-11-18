# Dotnet for OpenHarmony

面向 OpenHarmony 的 .NET 10 / NativeAOT 工作区。Harmony 组件由本仓库直接维护，
使用根目录的 `OpenHarmony.slnx` 构建和测试；第三方源码仅保留在 `dotnet/` 子模块中。

## 目录

```text
artifacts/                 构建产物、NuGet 包、日志和本机工具（不提交）
docs/                      构建说明和历史设计
dotnet/                    第三方源码（Git submodule）
  ClangSharp/
  core-origin/
  runtime/                 OpenHarmony runtime 移植
  runtime-origin/          上游比较基线
src/
  OpenHarmony.NDK.Bindings/
  OpenHarmony.NET.HapTooling/
  OpenHarmony.NET.Player.Sdk/
  OpenHarmony.NET.Runtime/
  PublishAotCross/
tests/                     绑定、生成器、SDK resolver、runtime packager 和 ABI 测试
tools/                     生成器、打包器、构建和验证脚本
examples/NativeAotSmoke/    ArkTS → N-API → C# NativeAOT 最小应用
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
`dotnet test OpenHarmony.slnx -c Release`。四个工具包输出至 `artifacts/packages/`：
NDK.Bindings、PublishAotCross、Player.Sdk、HapTooling。
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

# 从上述产物生成带 manifest 的两个运行时包以及四个工具包。
./build.ps1 -Target Pack

# 使用独立 NuGet 缓存验证 Player.Sdk 的两个架构及导出符号。
./tools/verify-packages.ps1 -SdkRoot ./artifacts/native/sdk
```

已有 API 13/15 的 SDK 根目录可以通过 `-SdkRoot` 直接传入。构建产物存于
`artifacts/native/runtime/api13/<arch>/Release/`；运行时打包目录为
`artifacts/runtime/releases/<version>/`。`Pack` 在运行时输入缺失或 provenance
校验失败时直接报错。运行时源码构建会按 `dotnet/runtime/global.json` 获取其所需 SDK。

## 构建示例

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
详细验证步骤和实际结果见 [构建与验证记录](docs/ISSUE-001-validation.md)。

## 维护

四个第三方子模块的 URL 和提交仍由 `.gitmodules` / Git 索引固定。
`src/`、`tests/`、`tools/` 和 `examples/` 中的改动直接提交到本仓库。
统一的 CI 工作流位于 `.github/workflows/build.yml`，执行托管构建、测试和工具打包。
原生 runtime 编译、签名和设备验收需要安装 Harmony 工具链的 Windows 主机。

导入来源和历史制品说明见 [迁移记录](docs/repository-migration.md)。
