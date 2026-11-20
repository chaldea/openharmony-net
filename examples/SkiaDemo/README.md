# SkiaSharp + HarfBuzzSharp on HarmonyOS

此示例从两个 `Chaldea.*.NativeAssets.HarmonyOS` NuGet 包加载真实 HarmonyOS 原生库。
ArkTS 经 N-API 调用 C# NativeAOT，在 640×800 RGBA 缓冲区中绘制渐变、圆、贝塞尔路径
和文字。HarfBuzz 使用系统字体整形 `Hello, HarmonyOS!`，Skia 根据生成的 glyph ID
及位置绘制，再由 ArkUI PixelMap 显示。N-API 检查背景和几何图形的实际像素，页面
显示完成后输出 `SKIA_DEMO rendered=1 pixels=640x800 glyphs=17`。

## 构建、运行

环境沿用仓库 NativeAOT / HarmonyOS 工具链，需要 Python 3.12+、Ninja、Git、
PowerShell 7.3+、API15 Native SDK、官方 command-line-tools 和 JDK 17+。
先构建运行时包（见 [维护说明](../../docs/maintainers.md)），然后执行：

```powershell
git submodule update --init dotnet/SkiaSharp
git -C dotnet/SkiaSharp submodule update --init --depth 1 externals/skia
pwsh ./tools/build-skia.ps1 -SdkRoot ./artifacts/native/sdk
./build.ps1 -Target Pack

# 启动 x86_64 模拟器后，构建两个 ABI 的 HAP 并验证模拟器渲染。
./tools/verify-skia.ps1 -SdkRoot ./artifacts/native/sdk `
  -CommandLineTools F:\.huawei\command-line-tools `
  -JavaHome 'D:\Program Files\Huawei\DevEco Studio\jbr' -RunOnDevice
```

源码下载需访问 GitHub、Google Source 和 GN 下载源。如果需要代理，请在当前终端
设置 `HTTP_PROXY` / `HTTPS_PROXY`（例如 `http://127.0.0.1:7890`）。脚本不修改全局代理。

验证脚本每次使用新的 NuGet 缓存和 Stage 目录，验证包消费、每个 HAP 中的 ELF 架构、
原生库哈希、当前应用 PID 的渲染日志，并保存模拟器截图。
产物位于 `artifacts/skia-validation/<run>/`；省略 `-RunOnDevice` 可只构建两个 HAP。
`-Version` 选择仓库包版本，`-Architectures x64` 可只验证模拟器架构。

## 应用引用

在已有 `net11.0-ohos` workload 项目中添加：

```xml
<PackageReference Include="Chaldea.SkiaSharp.NativeAssets.HarmonyOS" Version="11.0.0-ohos.1-preview.1" />
<PackageReference Include="Chaldea.HarfBuzzSharp.NativeAssets.HarmonyOS" Version="11.0.0-ohos.1-preview.1" />
```

包传递引用 SkiaSharp 4.157.0 / HarfBuzzSharp 14.4.0.200。不要混入 Android/Linux NativeAssets。
`OpenHarmonyAbi` 决定复制哪个原生库；HAP 构建脚本收集发布目录中的 `.so`。
本示例使用 Player.Sdk 和定制 Stage 页面，以便直接展示像素缓冲区。

原生库支持 EGL/OpenGL ES，但此示例仅验收 CPU 栅格渲染；arm64 仅验收构建及 HAP。
示例使用设备自带的 `/system/fonts/HarmonyOS_Sans.ttf`，没有重新分发系统字体。
