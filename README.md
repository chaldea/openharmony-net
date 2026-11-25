# .NET for OpenHarmony

使用 C# 和 .NET 11 NativeAOT 构建 OpenHarmony / HarmonyOS 应用，通过标准
`dotnet build`、`dotnet run` 和 `dotnet publish` 完成编译、设备运行和 HAP 打包。

## 快速开始

以下命令适用于提供 `ohos` 入口的版本。可用版本见
[NuGet 工具包页面](https://www.nuget.org/packages/Chaldea.OpenHarmony.Workload)。

### 1. 准备环境

支持 Windows x64、Linux x64 和 macOS x64/arm64。将可写的独立 .NET SDK 和官方 HarmonyOS
command-line-tools 的 `bin` 目录加入 PATH。`dotnet --version` 应显示正式版 `11.0.1xx`，
`hvigorw -v` 可用于确认 CLI。Windows 需要 ZIP 版 SDK，MSI 管理的 workload 安装器需要 MSI 格式的包。
发布 HAP 和运行应用还需要 JDK 17+，可通过 `JAVA_HOME` 指定安装目录。
[.NET 11 下载](https://dotnet.microsoft.com/download/dotnet/11.0)

### 2. 安装 workload

```powershell
dotnet tool install -g Chaldea.OpenHarmony.Workload
ohos workload install
ohos workload list
```

`install` 注册内置 manifest，并调用 `dotnet workload install` 安装 SDK、模板和运行时包。
使用本地或私有源时可运行 `ohos workload install --source <源>`，多个源可重复指定。
工具遵循当前目录的 `global.json`。需要覆盖 PATH 时可设置 `DOTNET_ROOT`，或在单条命令中传入
`--dotnet-root <目录>`。后续创建和构建应用时使用同一 SDK 的 `dotnet`。

### 模拟器

安装与主机平台匹配的官方 Command Line Tools，把其 `bin` 目录加入 PATH。
当 `Emulator -help` 列出 `-imageList`、`-install` 和 `-create` 时，可以直接下载镜像和创建首台设备。
首次使用先运行 `Emulator -license` 阅读协议，同意后运行 `Emulator -license accept`。

```powershell
ohos emulator create
ohos emulator run
# 使用结束后
ohos emulator stop
```

三个命令省略名称时都操作 `MyPhone`。`create` 默认选择 `phone`，优先使用 `HarmonyOS 6.1.0(23)`。
如果当前主机的镜像列表没有这个版本，工具会从可用版本中优先选择正式版。
镜像已安装时直接复用，缺少时自动下载，确认镜像文件就绪后创建设备。下载中断后可重试同一条命令。
可用 `Emulator -imageList -deviceType phone` 查看可选镜像，将其中的 `deviceType` 和完整 `osVersion`
传给 `--device-type` 和 `--os-version`。下载可用 `--http-proxy <URL>` 指定代理。

命令行工具根目录默认读取 `OHOS_COMMAND_LINE_TOOLS`，否则使用用户目录下的
`.Huawei/command-line-tools`，再从 PATH 中查找官方 CLI 的 `bin` 目录。
三个命令都可传 `--command-line-tools <目录>` 覆盖。实例默认保存在 `~/.Huawei/Emulator/deployed`。
`create` 和 `run` 优先查找 DevEco 的镜像目录，Windows 为 `%LOCALAPPDATA%\Huawei\Sdk`，
macOS 为 `~/Library/Huawei/Sdk`，然后查找 `~/.Huawei/Sdk`。缺少所需镜像时，`create` 下载到 `~/.Huawei/Sdk`。
`--instance-path <目录>` 可指定实例位置，`--image-root <目录>` 可单独指定镜像查找和下载位置。
官方 SDK 位于 `<command-line-tools>/sdk`。

### 3. 创建和运行应用

```powershell
dotnet new ohos -o MyApp
cd MyApp
dotnet build

# 先启动 x86_64 模拟器，然后运行。
dotnet run

# 生成发布用的未签名 HAP。
dotnet publish
```

项目使用标准 `Microsoft.NET.Sdk` 和 `<TargetFramework>net11.0-ohos</TargetFramework>`。
该 TFM 自动引用 OpenHarmony Framework。模板通过 `SurfaceApplication` 启动
`SurfaceView`，并用 OpenGL ES 绘制蓝色背景。[Framework 用法](src/OpenHarmony.Framework/README.md)
HAP 输出在 `bin/Release/net11.0-ohos/linux-musl-x64/publish/`。

运行 arm64 真机时，先连接设备，再选择架构：

```powershell
dotnet run -p:OpenHarmonyAbi=arm64-v8a
dotnet publish -p:OpenHarmonyAbi=arm64-v8a
```

连接多个设备时，通过 `-p:OpenHarmonyDevice=<HDC目标>` 选择；可用
`<CLI 根目录>/sdk/default/openharmony/toolchains/hdc list targets`
查看目标。实际设备运行已验证 API17 / x86_64 模拟器，arm64 已验证交叉编译和 HAP。

## 应用配置

`dotnet build` 编译 C# 并验证 Native SDK；`publish` 执行 NativeAOT 和 Hvigor 打包；
`run` 发布、安装并启动应用。Debug 运行默认使用 SDK 的公开开发身份，生成绑定设备、
有效期 30 天的签名资料并保存在 `obj/`。正式分发需要自己的签名；Release 运行可通过
`OpenHarmonySignedHapPath` 指定已签名 HAP。

属性可以写入 `.csproj` 或通过 `-p:属性=值` 传入：

| 属性 | 用途 / 默认值 |
| --- | --- |
| `OpenHarmonyCommandLineTools` | 官方 CLI 根目录，依次使用属性、`OHOS_COMMAND_LINE_TOOLS` 和 PATH |
| `OpenHarmonyJavaHome` | JDK 根目录，默认使用 `JAVA_HOME` |
| `OpenHarmonyAbi` | `x86_64` 或 `arm64-v8a`；默认 x86_64 |
| `OpenHarmonyDevice` | HDC 目标；只有一个连接时自动选择 |
| `OpenHarmonyBundleName` | 应用包名；默认由项目名派生 |
| `OpenHarmonySurface` | 启用 Framework 渲染宿主；`dotnet new ohos` 生成的项目默认启用 |
| `OpenHarmonyEntryPoint` | 使用旧版 Stage 宿主时的静态 C# `int` 入口方法，默认为 `App.Run` |
| `OpenHarmonySdkRoot` | 可选独立 Native SDK 根目录；默认 `OHOS_SDK_ROOT` |
| `OpenHarmonyApiLevel` | Native SDK API；只有一个 SDK 时从 manifest 推导 |
| `OpenHarmonySignedHapPath` | 用于安装运行的已签名 HAP |

最低应用平台为 API15，CLI 的编译 SDK 可以是更高版本；同时安装多个 Native SDK 时需
显式选择 API。缺少 SDK 会报告 `OHOS1001` 并提示配置路径。

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
          ohos workload install
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
dotnet add package Chaldea.SkiaSharp.NativeAssets.HarmonyOS --version 11.0.0
dotnet add package Chaldea.HarfBuzzSharp.NativeAssets.HarmonyOS --version 11.0.0
```
所建项目为`<TargetFramework>net11.0-ohos</TargetFramework>`的项目，在该项目上直接添加对应的包引用即可。
普通 `net11.0`、桌面或 MAUI 项目，需要添加一个 HarmonyOS 应用宿主，可先创建 `ohos` 项目，再引用的业务项目。

此 CPU 绘制示例使用 `App.Run()` 桥接。先将项目文件中的
`<OpenHarmonySurface>` 设为 `false`，再用下面的代码替换 `App.cs`。

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
# x86_64 模拟器上的 Stage 页面显示返回值 1，表示像素检查通过。
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

```powershell
ohos workload update       # 更新当前 SDK 的所有已安装 workload，可传入 --source
ohos workload uninstall    # 卸载 harmonyos，成功后移除其 manifest
dotnet tool uninstall -g Chaldea.OpenHarmony.Workload
```

单独管理 manifest 可用 `ohos workload register` 和 `ohos workload unregister`。
例如，先注册，再用 `dotnet workload restore --skip-manifest-update` 按项目恢复 workload。

## 项目维护

- [源码布局、构建、测试与运行时编译](docs/maintainers.md)
- [NuGet 发布工作流、包名审计和维护者配置](docs/releases.md)
- [SkiaSharp / HarfBuzzSharp 图形包与渲染示例](examples/SkiaDemo/README.md)
- [workload 实现与验证记录](docs/ISSUE-002-validation.md)
- [多平台 CI 与本机验证记录](docs/ISSUE-004-validation.md)
- [运行时迁移与设备验证](docs/ISSUE-001-validation.md)
- [.NET 11 与 SkiaSharp 升级验证](docs/ISSUE-006-validation.md)
