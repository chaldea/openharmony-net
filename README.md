# .NET for OpenHarmony

使用 C# 和 .NET 11 NativeAOT 构建 OpenHarmony / HarmonyOS 应用，通过标准
`dotnet build`、`dotnet run` 和 `dotnet publish` 完成编译、设备运行和 HAP 打包。

## 快速开始

以下展示 NuGet 安装流程，发布后不需要克隆或构建本仓库。当前发行线为预览版；
包名统一使用 `Chaldea.OpenHarmony.*`。可用版本见 [NuGet 工具包页面](https://www.nuget.org/packages/Chaldea.OpenHarmony.Workload)。

本次 .NET 11 升级版本为 `11.0.0-ohos.1-preview.1`。发布到 NuGet 前，请按
[维护说明](docs/maintainers.md) 构建并使用本地 `artifacts/packages` 源安装和验证。

### 1. 准备环境

需要 Windows x64、**ZIP 解压安装的 .NET SDK 11.0.100**（feature band 11.0.100）、
官方 HarmonyOS command-line-tools 及 SDK，以及 JDK 17+。
MSI/Visual Studio 管理的 workload 安装不支持这里的第三方包，请使用独立 SDK。

安装并配置好路径后，先在 PowerShell 中确认环境变量。将示例路径替换为实际安装位置：

```powershell
$env:DOTNET_ROOT = 'C:\dotnet11'
$env:PATH = "$env:DOTNET_ROOT;$env:USERPROFILE\.dotnet\tools;$env:PATH"
$env:OHOS_COMMAND_LINE_TOOLS = 'c:\command-line-tools'
$env:JAVA_HOME = 'c:\Program Files\Huawei\DevEco Studio\jbr'

dotnet --version
Test-Path "$env:OHOS_COMMAND_LINE_TOOLS\sdk\default\openharmony\native\oh-uni-package.json"
Test-Path "$env:JAVA_HOME\bin\java.exe"
```

版本应为 `11.0.1xx`，两个路径检查均为 `True`。SDK 下载入口：
[.NET 11](https://dotnet.microsoft.com/download/dotnet/11.0)。
这些赋值对当前 PowerShell 窗口有效；长期使用可配置为用户环境变量。

### 2. 安装 workload

```powershell
dotnet tool install -g Chaldea.OpenHarmony.Workload --version 11.0.0-ohos.1-preview.1
harmonyos-workload init
dotnet workload install harmonyos --skip-manifest-update
dotnet workload list
```

`init` 将工具内置的 manifest 注册到当前 `dotnet` 所选 SDK，并启用 manifest 模式；
随后由 `dotnet workload install` 从 NuGet 下载 SDK、模板和两个架构的运行时包。
工具版本与这些依赖版本一致。注册不依赖 Harmony SDK，应用构建时才检查 SDK 是否齐全。

### 3. 创建和运行应用

```powershell
dotnet new ohos -n HelloHarmony
cd HelloHarmony
dotnet build

# 先启动 x86_64 模拟器，然后运行。
dotnet run

# 生成发布用的未签名 HAP。
dotnet publish
```

模板中的 `App.Run()` 返回 `42`，默认页面显示执行结果及 `.NET 11` 运行时版本。
项目使用标准 `Microsoft.NET.Sdk` 和 `<TargetFramework>net11.0-ohos</TargetFramework>`。
HAP 输出在 `bin/Release/net11.0-ohos/linux-musl-x64/publish/`。

运行 arm64 真机时，先连接设备，再选择架构：

```powershell
dotnet run -p:OpenHarmonyAbi=arm64-v8a
dotnet publish -p:OpenHarmonyAbi=arm64-v8a
```

连接多个设备时，通过 `-p:OpenHarmonyDevice=<HDC目标>` 选择；可用
`& "$env:OHOS_COMMAND_LINE_TOOLS\sdk\default\openharmony\toolchains\hdc.exe" list targets`
查看目标。实际设备运行已验证 API17 / x86_64 模拟器，arm64 已验证交叉编译和 HAP。

## 应用配置

`dotnet build` 编译 C# 并验证 Native SDK；`publish` 执行 NativeAOT 和 Hvigor 打包；
`run` 发布、安装并启动应用。Debug 运行默认使用 SDK 的公开开发身份，生成绑定设备、
有效期 30 天的签名资料并保存在 `obj/`。正式分发需要自己的签名；Release 运行可通过
`OpenHarmonySignedHapPath` 指定已签名 HAP。

属性可以写入 `.csproj` 或通过 `-p:属性=值` 传入：

| 属性 | 用途 / 默认值 |
| --- | --- |
| `OpenHarmonyCommandLineTools` | 官方 CLI 根目录；默认 `OHOS_COMMAND_LINE_TOOLS` |
| `OpenHarmonyJavaHome` | JDK 根目录；默认 `JAVA_HOME` |
| `OpenHarmonyAbi` | `x86_64` 或 `arm64-v8a`；默认 x86_64 |
| `OpenHarmonyDevice` | HDC 目标；只有一个连接时自动选择 |
| `OpenHarmonyBundleName` | 应用包名；默认由项目名派生 |
| `OpenHarmonyEntryPoint` | 静态 C# `int` 入口方法；默认 `App.Run` |
| `OpenHarmonySdkRoot` | 可选独立 Native SDK 根目录；默认 `OHOS_SDK_ROOT` |
| `OpenHarmonyApiLevel` | Native SDK API；只有一个 SDK 时从 manifest 推导 |
| `OpenHarmonySignedHapPath` | 用于安装运行的已签名 HAP |

最低应用平台为 API15，CLI 的编译 SDK 可以是更高版本；同时安装多个 Native SDK 时需
显式选择 API。缺少 SDK 会报告 `OHOS1001` 并提示配置路径。当前模板提供最小 Stage
应用壳，不包含 MAUI UI。

## CI 集成：GitHub Actions 发布示例

和android等发布流程一样，当前workload也支持在CI中集成，以github编译为例：

```yaml
name: Publish HarmonyOS app
jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      # Setup dotnet
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      # Setup java
      - uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: '17'
      # Setup HarmonyOS SDK
      - uses: ErBWs/setup-ohos@v2
        with:
          version: '6.1.1.280'
          cache: true
      # Install workload
      - name: Install workload
        run: |
          dotnet tool install -g Chaldea.OpenHarmony.Workload
          harmonyos-workload init
          dotnet workload install harmonyos --skip-manifest-update
      # Build and publish your app
      - name: Publish HAP
        run: >-
          dotnet publish xxxx
```

`setup-java` 提供 `JAVA_HOME`；`setup-ohos` 安装官方 CLI 和 SDK。`dotnet publish` 会自动执行NativeAOT编译并调用 Hvigor 打包，无需连接设备或启动模拟器。

## 在应用中使用 SkiaSharp

对于已经能构建的 `net11.0-ohos` workload 项目，添加`Chaldea.SkiaSharp.NativeAssets.HarmonyOS`包即可
在项目中使用 SkiaSharp。该图形包是独立的nuget包，不随 workload 安装，直接添加到项目中即可。

```powershell
dotnet new ohos -n SkiaApp
cd SkiaApp
dotnet add package Chaldea.SkiaSharp.NativeAssets.HarmonyOS --version 11.0.0-ohos.1-preview.1
dotnet add package Chaldea.HarfBuzzSharp.NativeAssets.HarmonyOS --version 11.0.0-ohos.1-preview.1
```
所建项目为`<TargetFramework>net11.0-ohos</TargetFramework>`的项目，在该项目上直接添加对应的包引用即可。
普通 `net11.0`、桌面或 MAUI 项目，需要添加一个 HarmonyOS 应用宿主，可先创建 `ohos` 项目，再引用的业务项目。

在模板的 `App.cs` 替换为下面代码，可验证 Skia 的 CPU 绘制：

```csharp
using SkiaSharp;

public static class App
{
    public static int Run()
    {
        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.CornflowerBlue };
        canvas.DrawCircle(32, 32, 16, paint);
        return bitmap.GetPixel(32, 32) == SKColors.CornflowerBlue ? 1 : 0;
    }
}
```

```powershell
# x86_64 模拟器：默认页面显示返回值 1，表示像素检查通过。
dotnet run

# 发布 arm64 真机用 HAP。
dotnet publish -p:OpenHarmonyAbi=arm64-v8a
```

`OpenHarmonyAbi` 自动选择对应的原生库，发布时会将 `libSkiaSharp.so`（以及引用了
HarfBuzz 包时的 `libHarfBuzzSharp.so`）放入 HAP 的 `libs/<ABI>/`，无需手工配置复制。
支持 API15+ 的 x86_64 和 arm64-v8a。

**添加包提供绘图 API，不会自动创建 Skia 页面或 UI 控件。** 上面的代码只在内存中
绘图，模板页面显示的是返回值。要在 ArkUI 中显示图像，还需将像素缓冲区通过 N-API
传给 ArkTS，并创建 PixelMap 等显示对象。完整实现见
[SkiaDemo](examples/SkiaDemo/README.md)：该示例使用 Player.Sdk 和自定义 Stage 页面，
包含文字整形、像素传递和显示代码，可作为接入参考。

## 更新和卸载

正常更新或卸载 workload 时，可以直接使用 dotnet 指令:
```powershell
# 会更新列表中所有的workload
dotnet workload update

# 卸载当前workload
dotnet workload uninstall harmonyos
```

更新`Chaldea.OpenHarmony.Workload`工具时，需要先使用原版本解除注册：
```powershell
dotnet workload uninstall harmonyos
harmonyos-workload unregister
dotnet tool update -g Chaldea.OpenHarmony.Workload
harmonyos-workload init
dotnet workload install harmonyos --skip-manifest-update
```

仅卸载 workload 后可用 `dotnet workload restore --skip-manifest-update` 根据项目恢复。
完全移除时执行 workload uninstall、工具 unregister，再执行
`dotnet tool uninstall -g Chaldea.OpenHarmony.Workload`。
每个 SDK feature band 需要对应注册，工具遇到不支持的 band 会给出提示。

## 项目维护

- [源码布局、构建、测试与运行时编译](docs/maintainers.md)
- [NuGet 发布工作流、包名审计和维护者配置](docs/releases.md)
- [SkiaSharp / HarfBuzzSharp 图形包与渲染示例](examples/SkiaDemo/README.md)
- [workload 实现与验证记录](docs/ISSUE-002-validation.md)
- [多平台 CI 与本机验证记录](docs/ISSUE-004-validation.md)
- [运行时迁移与设备验证](docs/ISSUE-001-validation.md)
- [.NET 11 与 SkiaSharp 升级验证](docs/ISSUE-006-validation.md)
