# NuGet 发布流程

命令行工具的包名是 `Chaldea.OpenHarmony.Workload`，workload packs 使用 `Chaldea.OpenHarmony.*` 包名。图形扩展使用
`Chaldea.SkiaSharp.NativeAssets.HarmonyOS` 和 `Chaldea.HarfBuzzSharp.NativeAssets.HarmonyOS`。
源码目录、程序集名、C# 命名空间及
`OpenHarmony*` MSBuild 属性保持原有命名；NuGet 自动导入的 props/targets 文件在打包时
改为与新包名一致。完整发布包集合以 `tools/release-packages.json` 为准。

## 名称检查

2026-11-18 查询 nuget.org v3 flat-container API，已存在：

| 原包名 | 当时最新版本 |
| --- | --- |
| `OpenHarmony.NET.PublishAotCross` | `0.0.0-alpha.7` |
| `OpenHarmony.NET.Sdk` | `0.0.0-alpha.7` |

为避免使用原仓库的发布身份，所有发布包统一使用新前缀。查询时十个新 ID 全部返回
HTTP 404。这个结果表示当时未找到公开包，并不代表已保留名称或具备推送权限。

`tools/check-nuget-names.ps1` 会在发布前重新查询。已存在的包必须在最新 nuspec 的
repository URL 中指向本仓库，否则失败；网络或服务端错误也会失败。该检查用于发现
意外同名包，仓库元数据不是所有权证明，最终推送仍由 NuGet API key 的权限决定。

## Runner 和 secrets

没有专用 runner 时，可使用 `.github/workflows/publish-prepared.yml`：先在已配置的开发主机
从 release tag 构建并完成本文验证，将发布包和 SHA256SUMS 上传为该 GitHub Release
的附件，然后手动运行 **Publish validated release assets**，输入 tag。它在 GitHub 托管
Windows runner 上重新检查哈希、包集合、内部依赖和与 tag 一致的源码 revision，再使用
同一组 NuGet secrets 发布；不会重新执行原生编译或替代本地 HAP 验收。

`.github/workflows/release.yml` 通过 `v*` tag 或 Actions 的手动版本输入触发。
它先在带 `self-hosted, Windows, X64, harmonyos` 标签的专用 runner 构建和验收，再在
GitHub 托管的 Ubuntu runner 推送。不要把这个自托管 runner 用于不可信 PR。

构建 runner 按 [项目维护](maintainers.md) 安装 Visual Studio C++、Git、PowerShell 7.3+、
GitHub CLI、Python 3.12+ 和 Ninja，保留足够空间构建原生图形库，并配置：

- `OHOS_COMMAND_LINE_TOOLS`：官方 command-line-tools 根目录，包含 SDK / ohpm / Hvigor。
- `JAVA_HOME`：JDK 17+ 根目录。
- 网络：允许访问官方 Native SDK、固定版本 ICU/OpenSSL、NuGet、.NET SDK、GitHub、
  Google Source 和 GN 下载源；需要时在 runner 配置 HTTP_PROXY / HTTPS_PROXY。

仓库或组织 Actions secrets：

| secret | 值 |
| --- | --- |
| `NUGET_SOURCE` | 官方源 `https://api.nuget.org/v3/index.json` |
| `NUGET_KEY` | 能发布 catalog 中全部 `Chaldea.*` 包的 NuGet API key |

工具包、runtime 包、图形 NativeAssets、manifest 和 bootstrap tool 使用同一个 NuGet 版本；manifest 不再
固定为基础 runtime 版本。版本来自 tag 去掉 `v`，或手动输入，支持稳定版和预览版。
SDK feature band 固定为当前验证的 `11.0.100`；发布其它 band 前需同步 catalog、
`Directory.Build.props`、打包参数和工具的支持版本，并完成验证。

## 发布顺序与验证

工作流从所选 Git revision 构建，流程如下：

1. 下载与所选提交匹配的 x64/arm64 NativeAOT 基线，核对 SHA-256 和 provenance。
   发布 tag 前应已上传对应的基线 Release，见 [项目维护](maintainers.md#维护)。
2. 检查 NuGet 名称，构建解决方案，运行单元测试，准备官方 API15 Native SDK。
3. 使用 API15 编译 x64/arm64 SkiaSharp 和 HarfBuzzSharp，打包发布制品；验证 Player.Sdk
   依赖及原生导出，以及使用新图形包生成的两个 SkiaDemo HAP。
4. 安装一次性 .NET SDK，从本地源安装 dotnet tool，验证 manifest 注册、workload 安装/卸载/恢复、
   x64 和 arm64 HAP。CI 不要求模拟器；设备运行由维护者额外验收。
5. `prepare-release.ps1` 检查全部包的 ID、版本、仓库、内部依赖、manifest、runtime provenance；
   同时验证图形库两种 ABI 的 ELF、哈希、固定源码 revision、托管 API 版本及许可证文件。
   只把 catalog 中的包复制到空的 release 目录，生成 SHA256SUMS。
6. 上传供 publish job 消费的 `nuget-release`，保留 1 天；不额外上传验证日志或测试报告。
   publish job 验证 SHA-256，按 pack → manifest → bootstrap tool 顺序调用
   `dotnet nuget push --source ... --api-key ... --skip-duplicate`。超过保留期后需重新构建，
   才能重跑 publish job。`publish-prepared.yml` 直接读取 Release 附件，没有 Actions 制品上传。

`--skip-duplicate` 允许部分成功后的重试，不会替换 NuGet 上已有同版本包。发布版本不可
原地修改；代码变更应使用新版本。NuGet 索引更新可能有延迟，工作流结束后应分别记录
推送结果和包的下载状态；包可下载后，再从 nuget.org 验证完整 workload 流程。
手动触发也会真实发布，不是 dry run。

本地只验证、不推送：

```powershell
./build.ps1 -Target Test
pwsh ./tools/build-skia.ps1 -SdkRoot ./artifacts/native/sdk
./build.ps1 -Target Pack -Version 11.0.0-ohos.1-preview.1
./tools/verify-skia.ps1 -SdkRoot ./artifacts/native/sdk `
  -CommandLineTools $env:OHOS_COMMAND_LINE_TOOLS -JavaHome $env:JAVA_HOME -RunOnDevice
./tools/check-nuget-names.ps1 -OutputPath ./artifacts/nuget-name-check.json
# 输出目录必须为空，防止混入旧制品。
./tools/prepare-release.ps1 -Version 11.0.0-ohos.1-preview.1 -OutputDirectory ./artifacts/release-check
```

图形包随本仓库发行线编号，但分别精确依赖 SkiaSharp 4.157.0、HarfBuzzSharp 14.4.0.200。
升级上游时须同步原生源码、托管依赖和 provenance 校验，重新执行模拟器渲染验收。
图形库是可选 NuGet 扩展，不加入 workload 的四个必需 pack。

## 工具开发与旧版本迁移

`tools/WorkloadInstaller` 是 `Chaldea.OpenHarmony.Workload` 工具的源码，命令名为 `ohos`。
工具内嵌共享 manifest 模板及 targets，通过程序集元数据记录版本和 feature band。

本地 pack 后，可使用独立 tool 目录测试，不影响用户全局工具：

```powershell
dotnet tool install Chaldea.OpenHarmony.Workload --tool-path ./artifacts/local-tools `
  --add-source ./artifacts/packages --version 11.0.0-ohos.1-preview.1
./artifacts/local-tools/ohos workload install --dotnet-root ./artifacts/dotnet-sdk11 --source ./artifacts/packages
```

上述 `dotnet` 必须来自同一个独立 SDK，应用验证脚本会显式设置正确 PATH。
`tools/register-workload.ps1` 仅保留为维护者兼容入口，内部调用 C# 工具，不再包含另一套注册逻辑。

若安装过早期的 `openharmony.net.workload` manifest，先用原 SDK 执行
`dotnet workload uninstall harmonyos`，再移除该 SDK 的
`sdk-manifests/10.0.300/openharmony.net.workload/10.0.10` 旧注册目录，然后运行 `ohos workload install`。
新工具检测同名 workload 冲突时会停止，不会覆盖其它 manifest。

同一 SDK feature band 的补丁更新使用 `ohos workload update`。新版 manifest 和 packs
可从配置的 NuGet 源下载后，在已安装 workload 的独立 SDK 环境中执行该命令，再用
`ohos workload list` 核对版本。

从 .NET 10 升级时，使用独立的 .NET 11 SDK 注册新版 workload，并将应用目标框架改为
`net11.0-ohos`。原 .NET 10 SDK 中的 workload 可以保留，两个 feature band 互不替代。
