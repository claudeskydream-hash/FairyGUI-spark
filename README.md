# FairyGUI for 星火编辑器 (Spark / WasiCore)

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)

> 把 [FairyGUI](https://www.fairygui.com/) 运行时移植到星火编辑器（Spark / WasiCore SDK）的 **C# 共享源码库**。

## 📖 项目简介

本仓库是 FairyGUI UI 框架**运行时**在星火编辑器（WasiCore）平台上的 C# 移植实现。它把 FairyGUI 编辑器导出的 UI 包（Package）在星火运行时中解析、创建并渲染出来，让你可以在星火项目里用 FairyGUI 搭建和驱动界面。

**它是一个「源码库」，不是可独立运行的游戏工程。** 仓库只包含 FGUI 运行时源码与示例，本身没有 `.csproj` / 场景 / 资源。使用方式是被**多个星火游戏项目共同引用**——各项目通过 MSBuild `<Compile Include>` 把这里的源码编进自己的工程,从而多项目复用同一份 FGUI、集中维护。

> 若你在找「克隆后直接 `dotnet build` 就能跑的 Demo 工程」——本仓库已不再提供；它现在是纯库。示例代码见 `FGUI/Samples/`。

## ✨ 核心特性

- ✅ **完整的 UI 组件** —— Button、Label、Image、List、ScrollPane、Component/Window、Loader 等
- ✅ **屏幕自适应** —— 设计分辨率适配,自动缩放到不同屏幕尺寸(内置横屏 1136×640 / 竖屏 640×1136)
- ✅ **Controller 控制器** —— FairyGUI 页面/状态切换
- ✅ **Relation 关系系统** —— UI 元素间的关联布局
- ✅ **Transition 动画 / Tween 补间**
- ✅ **拖拽（Drag & Drop）** —— 含 DragDropManager 拖拽代理
- ✅ **事件系统** —— 完整的事件分发（Click / Drag / Drop 等）
- ✅ **文本样式** —— 描边 Stroke / 阴影 Shadow / 下划线 Underline / 粗体 / 斜体 / 颜色
- ✅ **UBB 内联富文本** —— `[u][b][i][color=#RRGGBB]` 转引擎内联标记渲染,可嵌套（详见 [文本装饰实现文档](docs/文本装饰实现-描边阴影下划线.md)）
- ✅ **资源加载适配** —— 通过 `ISCEAdapter` 对接星火资源/渲染
- ✅ **示例代码** —— Basics / Bag / VirtualList 三个完整 Demo

## 📁 仓库结构

```
FairyGUI-spark/
├── LICENSE
├── README.md
└── FGUI/                    # ← 库本体(命名空间主要为 FairyGUI)
    ├── FGUIManager.cs       # 库入口 / 初始化
    ├── Core/                # 核心:FGUIObject、Package、PackageItem…
    ├── UI/                  # UI 组件:Button/Label/Image/List/ScrollPane/Component…
    ├── Gears/               # Gear(控制器驱动的属性联动)
    ├── Event/               # 事件分发
    ├── Tween/               # GTween 补间
    ├── Utils/               # ByteBuffer、XML、UBB、字体映射…
    ├── Render/              # 渲染适配层:ISCEAdapter / SCEAdapter / SCERenderContext
    ├── SCE/                 # 星火运行时对接
    ├── Client/              # 星火接入层(IGameClass 引导 + 资源加载,#if CLIENT)
    └── Samples/             # 示例 Demo(Basics / Bag / VirtualList)
```

> 说明:命名空间以 `FairyGUI`（及 `FairyGUI.Render`、`FairyGUI.Utils`、`FairyGUI.Gears`）为主；星火接入层 `FGUI/Client/**` 位于 `GameEntry` 命名空间并以 `#if CLIENT` 包裹（UI 是客户端逻辑）。

## 🚀 在星火项目中接入

FGUI 是客户端代码,把它编进你项目的**客户端配置**即可。推荐通过 junction/环境变量引用,保持源码单一真源、多项目共用。

**1. 让项目能定位到本仓库**（二选一）

- Junction（推荐,免提权,路径稳定）：在你的项目根建一个 junction 指到本仓库,例如
  ```powershell
  New-Item -ItemType Junction -Path tools\FairyGUI-spark -Target D:\path\to\FairyGUI-spark
  ```
- 或设环境变量 `FGUI_SHARED_SRC_PATH` 指向本仓库的 `FGUI` 目录。

**2. 在 `.csproj` 里引入源码**（排除 Samples）

```xml
<PropertyGroup>
  <FGUISharedSrcPath Condition="'$(FGUI_SHARED_SRC_PATH)' != ''">$(FGUI_SHARED_SRC_PATH)</FGUISharedSrcPath>
  <FGUISharedSrcPath Condition="'$(FGUI_SHARED_SRC_PATH)' == ''">$(MSBuildProjectDirectory)\..\tools\FairyGUI-spark\FGUI</FGUISharedSrcPath>
</PropertyGroup>

<ItemGroup>
  <Compile Include="$(FGUISharedSrcPath)\**\*.cs"
           Exclude="$(FGUISharedSrcPath)\Samples\**\*.cs"
           LinkBase="FGUI" />
</ItemGroup>
```

> 注意:若把源码放在项目默认编译目录（如 `src/`）下,会与 SDK 默认 glob 冲突产生「重复 Compile」——请让引用路径落在默认编译根之外（如项目根的 `tools/`）。`LinkBase="FGUI"` 只影响 IDE 里的虚拟归类。

**3. 初始化并使用**

`FGUI/Client/FGUIBootstrapClientSys.cs` 是一个 `IGameClass`,会被星火自动发现,在 `MapGameMode` 的 `EventGameStart` 里引导 FGUI。它是最完整的接入范例,可直接参考或替换成你自己的引导逻辑。核心只有一步初始化:

```csharp
#if CLIENT
using FairyGUI;
using FairyGUI.Render;

// 游戏启动时初始化一次(横屏 1136×640;竖屏传 640×1136)
FGUIManager.Initialize(new SCEAdapter(), designWidth: 1136, designHeight: 640);
#endif
```

之后加载 UI 包、创建组件、绑定事件的完整用法,见 `FGUI/Client/FGUIBootstrapClientSys.cs` 与 `FGUI/Samples/`。

## 📝 示例（Samples）

`FGUI/Samples/` 下三个 Demo（**编译时被 `Exclude` 排除,仅作参考,不进你的产物**）：

- **Basics** —— 基础组件演示（Button、Text、Image、List 等）
- **Bag** —— 背包系统示例
- **VirtualList** —— 虚拟列表（大数据量优化）

## ⚠️ 已知限制

文本特性支持情况（详见 [文本装饰实现文档](docs/文本装饰实现-描边阴影下划线.md)）：

| 特性 | 状态 |
|---|---|
| 描边 Stroke / 阴影 Shadow | ✅ 已支持（引擎原生 Label 属性） |
| 下划线 Underline / 粗体 / 斜体 / 颜色 | ✅ 已支持（`<u><b><i><color>` 内联标记） |
| 内联字号（UBB `[size]`） | ❌ 引擎无内联字号能力，被剥离；字号只能用**字段级** FontSize |
| 删除线 Strikethrough | ❌ 未实现 |
| 行间距 / 字间距（Leading / LetterSpacing） | ⚠️ 引擎原生支持，但尚未接线 |
| 部分动画效果 | ⚠️ 部分不支持 |

## 🔧 开发指南

**新增 UI 组件**

1. 在 `FGUI/UI/` 下创建组件类,继承 `FGUIObject` 或 `FGUIComponent`
2. 在 `FGUI/Core/FGUIObjectFactory.cs` 注册组件类型
3. 在 `FGUI/Render/SCERenderContext.cs` 补渲染逻辑

**调试**

- 运行时日志集中在星火安装根的 `logs/`（客户端日志在 `logs/client`）
- 过滤 FGUI 日志:搜索 `[FGUI]`

**约定**（贴合星火 WasiCore 规则）

- UI 代码用 `#if CLIENT` 包裹;禁用 `Task.Run`/`Thread`,延时用 `Game.Delay()`
- 日志用参数化模板:`Game.Logger.LogInformation("... {Id}", id)`

## 🤝 贡献

欢迎 Issue / PR。改动 FGUI 直接在本仓库源码上进行,提交后各引用方重新编译即可获得更新。

## 📄 许可证

MIT,详见 [LICENSE](LICENSE)。

## 🙏 致谢

- [FairyGUI](https://www.fairygui.com/) —— 优秀的 UI 编辑器与框架
- 星火编辑器团队 —— 提供游戏开发平台
- 点点大佬 —— 提供了适配的基础代码

---

**⭐ 如果这个库对你有帮助,欢迎 Star！**
