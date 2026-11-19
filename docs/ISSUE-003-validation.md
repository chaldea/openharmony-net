# ISSUE-003：SkiaSharp / HarfBuzzSharp 验证记录

验证日期：2026-11-19。两个原生 NuGet 包、渲染示例和发布流程已实现。
此次使用本地 NuGet 源，没有推送 NuGet 或触发远端发布。

## 实现和版本

| 项目 | 固定版本 / 提交 |
| --- | --- |
| SkiaSharp 托管 API | 2.88.9 |
| HarfBuzzSharp 托管 API | 7.3.0.3（HarfBuzz 7.3.0） |
| `dotnet/SkiaSharp` 子模块 | `9699cdf046a989423a5e5eca68e7fd813486d81c` |
| 上游 Skia C API 源码 | `4bed689c9c9eb77a120c6a9d54af6a572c85d1c2`，C ABI 88.1 |
| Native SDK | API15 / 5.0.3.135，官方 HarmonyOS Clang/sysroot |
| NuGet 本地验证版本 | 10.0.10-ohos.2-preview.1 |

包名为 `Chaldea.SkiaSharp.NativeAssets.HarmonyOS` 和
`Chaldea.HarfBuzzSharp.NativeAssets.HarmonyOS`，项目放在 `src/`。每包包含 x86_64、
arm64-v8a 两个 ABI、构建 manifest 和第三方许可证；精确依赖对应托管版本。
通过 buildTransitive targets 选择 `OpenHarmonyAbi` 并复制到构建/发布目录，供 HAP 收集。
普通 Linux 项目不自动接收 HarmonyOS 库，避免将相同的 linux-musl RID 误当作系统 ABI 兼容。

构建脚本只获取所需的固定 DEPS 提交，静态链接 libc++、字体和图像编解码依赖，保留
EGL/OpenGL ES 支持。GN 使用上游固定版本；源码导出到 `artifacts/`，上游子模块保持干净。
上游 GN 会自动向部分第三方文件注入 WinRTCompat include，重复构建仅接受这一精确变更。
本机下载使用进程级 HTTP_PROXY / HTTPS_PROXY，未修改全局代理。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| 统一解决方案构建和既有测试 | 64 项通过 |
| 新增真实 MSBuild 消费测试 | 6 项通过：两个包同时导入、两种 ABI、ohos 平台、普通 Linux 隔离、非法 ABI/RID |
| 原生源码构建 | x86_64 和 arm64 全部成功；再次执行可增量复用 |
| 原生库静态检查 | ELF64 machine、C API 导出、动态依赖白名单通过 |
| 完整打包 | 十二个 nupkg 成功生成 |
| 发布集合校验 | ID、版本、依赖、源码/SDK manifest、原生库哈希及 ELF 验证通过 |
| 缺失原生输入 | pack 按预期失败，提示先构建两个 ABI |
| 篡改原生库 | 将 nupkg 内库的末尾一字节改动后，发布校验按预期拒绝，未创建 staging 目录 |
| NuGet 名称审计 | 两个新图形 ID 返回 404；其余十个包来自当前仓库 |
| GitHub Actions | actionlint 检查三个工作流通过 |
| 实际 NuGet 消费 | 每次使用新的包缓存；发布输出哈希与本次原生构建一致 |
| x86_64 / arm64 HAP | 均成功生成；每个 HAP 的 15 个共享库全部匹配目标架构 |
| 模拟器显示 | API17 / x86_64，Skia 实际像素检查通过，HarfBuzz 输出 17 个有效 glyph，ArkUI 显示成功 |

最终应用当前 PID 的日志：

```text
11-19 10:35:19.798 18504 18504 I A00001/SkiaDemo: SKIA_DEMO rendered=1 pixels=640x800 glyphs=17
```

截图已人工查看，确认深色背景、青色至紫色渐变、圆、贝塞尔路径和文字。
ArkUI `InitializationOptions.srcPixelFormat` 显式设为 RGBA_8888，避免其默认 BGRA
源格式造成红蓝通道互换。渲染成功标记由 Image 的显示完成回调发出。

| 最终未签名 HAP | SHA-256 |
| --- | --- |
| x86_64 | `9E23A87C882D6C6D74764DC348DD31E2AB0CEC8F27517DBC7DE45EA4BD364079` |
| arm64-v8a | `77E5BADCC9F68904D054A0BB15980195EC940DF0779F6C72CBC7858CFE85DB3E` |

## 本机制品

- `artifacts/skia-validation/a5d714f9/`：最终两个 HAP、结果 JSON、当前 PID 日志和截图。
- `artifacts/issue003-device-final.log`：发布、HAP、开发签名、安装、运行完整日志。
- `artifacts/native/skia/{x86_64,arm64-v8a}/`：原生库及源码/SDK/依赖/哈希 manifest。
- `artifacts/packages/`：两个图形包及其它发布包。
- `artifacts/issue003-release-final/`：十二包验证集合与 SHA256SUMS。
- `artifacts/issue003-tests.log`、`issue003-graphics-tests.log`：64 项既有测试和 6 项新增测试。
- `artifacts/issue003-missing-assets.log`、`issue003-tampered-assets.log`：失败路径证据。
- `artifacts/issue003-nuget-names.json`：包名称审计。

复现命令见 [SkiaDemo](../examples/SkiaDemo/README.md)。`build.ps1 -Target Pack` 包含新包，
发布 workflow 包含双 ABI 源码构建和示例 HAP 验证；prepared-release 路径使用同一份
十二包清单及校验规则。普通 `PackTools` 仍可在没有 HarmonyOS 原生产物时执行。

## 范围

已实际验证 API17/x86_64 上的 CPU 栅格渲染和 HarfBuzz 整形。
arm64 已验证交叉编译及 HAP，未运行真机；EGL/OpenGL ES 已编译但未进行 GPU 渲染验收。
Vulkan、ICU、GIF、DNG/RAW 未启用。此版本保持 SkiaSharp 2.88 ABI，不支持直接替换为 3.x。
