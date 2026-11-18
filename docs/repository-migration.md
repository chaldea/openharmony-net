# ISSUE-001 迁移记录

Harmony 代码导入时的版本：

| 组件 | 来源 | 提交 |
| --- | --- | --- |
| OpenHarmony.NDK.Bindings | https://github.com/FlameskyDexive/OpenHarmony.NDK.Bindings | 43413d4698dbc2172ad241b14a8c1bfc9e72f35c |
| OpenHarmony.NET.Runtime | https://github.com/FlameskyDexive/OpenHarmony.NET.Runtime | 44c84236cd42a9f4c276818a9dfbca20fddbf181 |
| PublishAotCross | https://github.com/FlameskyDexive/PublishAotCross | 421096729d546a8c8564ce2f79054ef328789281 |

HapTooling 和 Player.Sdk 原本已经是父仓库普通文件。五个组件现均位于 `src/`，
不再包含嵌套 Git 工作区。第三方 .NET 子模块只修改路径，提交保持不变。

原库测试移至根目录 `tests/`，BindingGenerator、RuntimePackager、ClangShim 和脚本移至
`tools/`。SDK resolver 是 PublishAotCross 包中的运行时构建依赖，保留在其 `package/`
目录；生成 NuGet 时随包分发。原有独立 solution、global.json、机器专属 NuGet 源和
子项目 CI 已由根配置与统一 CI 替代。

历史文档 `docs/superpowers/` 按原始日期保留，里面的旧目录与机器路径不作为当前构建入口。
`src/OpenHarmony.NET.Runtime/release/` 是原作者发布记录，不代表本次源码构建产物。
其 JSON 统一为 LF，并重新计算对应的两个文件摘要；`.gitattributes` 禁止 Git 对该目录做
换行转换。历史 nupkg 的摘要没有修改。

旧 Blazor 示例移出版本控制，本机备份位于忽略目录 `artifacts/legacy/`。
新示例不依赖 Blazor。旧 .NET 9 runtime payload 作为迁移前内容保留，但不会被
OpenHarmonyTarget=true 的 .NET 10 发布使用。
