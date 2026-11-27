# .NET for OpenHarmony

使用 C# 和 .NET 11 NativeAOT 构建 OpenHarmony / HarmonyOS 应用，通过标准
`dotnet build`、`dotnet run` 和 `dotnet publish` 完成编译、设备运行和 HAP 打包。

## 快速开始

### 1. 准备环境

默认开发环境不需要安装DevEco软件，只需要安装最新的[Command Line Tools](https://developer.huawei.com/consumer/cn/download)就可以，然后将bin目录添加到全局的PATH中。

当前Workload只支持最新的[.NET 11](https://dotnet.microsoft.com/download/dotnet/11.0)及后续版本。低版本的workload需要手动回退runtime分支并重新打包才行，目前无自动化流程。

环境验证：
```bash
$ dotnet --version
11.0.1xx

$ hvigorw -v
6.26.9

$ java --version
openjdk 21.0.12.1
```
正式开始前，请先确认上述命令都能正常输出。


### 2. 安装 workload

```bash
dotnet tool install -g Chaldea.OpenHarmony.Workload
ohos workload install # 安装当前dotnet版本的workload
ohos workload list    # 查看是否安装成功(如果安装成功会显示: harmonyos)
```

`install` 注册内置 manifest，并调用 `dotnet workload install` 安装 SDK、模板和运行时包。
使用本地或私有源时可运行 `ohos workload install --source <源>`，多个源可重复指定。
工具遵循当前目录的 `global.json`。需要覆盖 PATH 时可设置 `DOTNET_ROOT`，或在单条命令中传入
`--dotnet-root <目录>`。后续创建和构建应用时使用同一 SDK 的 `dotnet`。

### 模拟器

启动模拟器设备，默认`ohos`命令提供了简化模拟器操作的指令。

```bash
ohos emulator create  # 创建模拟器(默认MyPhone)
ohos emulator run     # 运行模拟器实例(默认MyPhone)
ohos emulator stop    # 停止当前运行的模拟器实例(默认MyPhone)
ohos emulator delete  # 删除指定实例(默认MyPhone)
ohos emulator list    # 显示所有实例
ohos emulator images  # 查看所有可用镜像
```

`create`,`run`,`stop`三个命令省略名称时，会默认使用 `MyPhone` 作为默认名称。`create` 默认选择 `phone`，当前默认机型为 `Pura 90 Pro`，优先使用 `HarmonyOS 7.0.0(26.0.0)`。如果当前主机的镜像列表没有这个版本，工具会从可用版本中优先选择正式版。

三个命令都可传 `--command-line-tools <目录>` 覆盖。实例默认保存在 `~/.Huawei/Emulator/deployed`。
`create` 和 `run` 优先查找 DevEco 的镜像目录，Windows 为 `%LOCALAPPDATA%\Huawei\Sdk`，
macOS 为 `~/Library/Huawei/Sdk`，然后查找 `~/.Huawei/Sdk`。

当上述目录中缺少所需镜像时，`create` 指令会自动下载镜像到 `~/.Huawei/Sdk`。`--instance-path <目录>` 可指定实例位置，`--image-root <目录>` 可单独指定镜像查找和下载位置。

### 3. 创建和运行应用

```bash
dotnet new ohos -o MyApp
cd MyApp
dotnet build

# 需要先启动模拟器
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
          # 镜像很大启用后会占用github的cache容量，按需启用
          cache: false
      # Install workload
      - name: Install workload
        run: |
          dotnet tool install -g Chaldea.OpenHarmony.Workload
          ohos workload install
      # Build and publish your app
      - name: Publish HAP
        run: >-
          dotnet publish
```

`setup-java` 提供 `JAVA_HOME`；`setup-ohos` 安装官方 CLI 和 SDK。`dotnet publish` 会自动执行NativeAOT编译并调用 Hvigor 打包，无需连接设备或启动模拟器。

## 在应用中使用 SkiaSharp

对于已经能构建的 `net11.0-ohos` workload 项目，添加`Chaldea.SkiaSharp.NativeAssets.HarmonyOS`包即可
在项目中使用 SkiaSharp。该图形包是独立的nuget包，不随 workload 安装，直接添加到项目中即可。

```powershell
dotnet new ohos -n SkiaApp
cd SkiaApp
dotnet add package Chaldea.SkiaSharp.NativeAssets.HarmonyOS
dotnet add package Chaldea.HarfBuzzSharp.NativeAssets.HarmonyOS
```
所建项目为`<TargetFramework>net11.0-ohos</TargetFramework>`的项目，在该项目上直接添加对应的包引用即可。
普通 `net11.0`、桌面或 MAUI 项目，需要添加一个 HarmonyOS 应用宿主，可先创建 `ohos` 项目，再引用的业务项目。

保留模板的 `<OpenHarmonySurface>true</OpenHarmonySurface>`，将 `App.cs` 替换为以下代码。Framework 的 XComponent 宿主通过 EGL 创建 OpenGL ES 2 上下文。Skia 使用当前上下文绘制到窗口的默认帧缓冲区，宿主在帧回调返回 `true` 后调用 `eglSwapBuffers` 显示画面。

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenHarmony.Hosting;
using OpenHarmony.Views;
using SkiaSharp;

public static class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "openharmony_initialize", CallConvs = [typeof(CallConvCdecl)])]
    public static nint Initialize() => SurfaceApplication.Initialize(() => new SkiaView());
}

sealed class SkiaView : SurfaceView
{
    private GRContext context = null!;
    private (int Width, int Height) drawnSize;

    public override void OnCreated()
    {
        using var gl = GRGlInterface.CreateGles(ResolveGl)
            ?? throw new InvalidOperationException("Cannot load the GLES interface.");
        context = GRContext.CreateGl(gl) ?? throw new InvalidOperationException("Cannot initialize Skia GLES.");
    }

    private nint ResolveGl(string name)
    {
        // Skia also probes optional extension functions.
        try { return GraphicsContext.GetProcAddress(name); }
        catch (NotSupportedException) { return 0; }
    }

    public override bool OnFrame(SurfaceFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || drawnSize == (frame.Width, frame.Height))
            return false;

        // The EGL window surface exposes framebuffer 0 with RGBA8 and an 8-bit stencil.
        using var target = new GRBackendRenderTarget(frame.Width, frame.Height, 0, 8,
            new GRGlFramebufferInfo(0, 0x8058)); // GL_RGBA8
        using var surface = SKSurface.Create(context, target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new InvalidOperationException("Cannot wrap the window framebuffer.");
        var canvas = surface.Canvas;
        float scale = Math.Min(frame.Width / 640f, frame.Height / 800f);
        canvas.Translate((frame.Width - 640 * scale) / 2, (frame.Height - 800 * scale) / 2);
        canvas.Scale(scale);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.CornflowerBlue, IsAntialias = true };
        canvas.DrawCircle(320, 400, 160, paint);
        context.Flush();
        drawnSize = (frame.Width, frame.Height);
        return true;
    }

    public override void OnDestroyed() => context?.Dispose();
}
```

运行 `dotnet run` 可在设备上查看圆形。包含渐变、路径和 HarfBuzz 文字整形的完整示例见 [SkiaDemo](examples/SkiaDemo/README.md)。

## 更新和卸载

```bash
ohos workload update       # 更新当前 SDK 的所有已安装 workload，可传入 --source
ohos workload uninstall    # 卸载 harmonyos，成功后移除其 manifest
dotnet tool uninstall -g Chaldea.OpenHarmony.Workload # 卸载工具
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
