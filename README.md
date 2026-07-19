# FairyGUI for 星火编辑器 (Spark / WasiCore)

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Spark%20%2F%20WasiCore-orange.svg)](#)

> 将 [FairyGUI](https://www.fairygui.com/) 运行时移植到星火编辑器（Spark / WasiCore SDK）的 **C# 共享源码库**。
> 把 FairyGUI 导出的 UI 包在星火运行时中解析、创建、渲染，让你在星火项目里用 FairyGUI 搭建界面。

- **是什么**：纯运行时源码库，被多个星火游戏项目**共同引用**（`<Compile Include>` 编进各自工程），一份源码集中维护、多项目复用。
- **不是什么**：不是可独立运行的游戏工程——仓库不含 `.csproj` / 场景 / 资源，只有运行时源码与示例。

---

## ✨ 特性

**UI 能力**
- 完整组件：Button、Label、Image、List、ScrollPane、Component/Window、Loader…
- 屏幕自适应：设计分辨率适配，自动缩放（内置横屏 1136×640 / 竖屏 640×1136）
- Controller 控制器、Relation 关系布局、Transition 动画、Tween 补间
- 拖拽（Drag & Drop，含 DragDropManager 代理）、完整事件分发（Click / Drag / Drop…）
- 通过 `ISCEAdapter` 对接星火资源与渲染

**文本与富文本**
- 文本样式：描边 / 阴影 / 下划线 / 粗体 / 斜体 / 颜色
- UBB 内联富文本：`[u][b][i][color=#RRGGBB]` → 引擎内联标记渲染，**可嵌套**
- 详见 [文本装饰实现文档](docs/文本装饰实现-描边阴影下划线.md)

---

## 🚀 快速接入（3 步）

FGUI 是客户端代码，编进你项目的**客户端配置**即可。推荐用 junction / 环境变量引用，保持单一真源、多项目共用。

**① 定位到本仓库**（二选一）

```powershell
# 方式 A：在你的项目根建 junction（推荐，免提权、路径稳定）
New-Item -ItemType Junction -Path tools\FairyGUI-spark -Target D:\path\to\FairyGUI-spark
```
```
# 方式 B：设环境变量 FGUI_SHARED_SRC_PATH 指向本仓库的 FGUI 目录
```

**② `.csproj` 引入源码**（排除 Samples）

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

> ⚠️ 引用路径须落在项目**默认编译根之外**（如项目根的 `tools/`）。若放进 `src/` 等默认编译目录会与 SDK 默认 glob 冲突、报「重复 Compile」。`LinkBase="FGUI"` 只影响 IDE 里的虚拟归类。

**③ 初始化**

```csharp
#if CLIENT
using FairyGUI;
using FairyGUI.Render;

// 游戏启动时初始化一次（横屏 1136×640；竖屏传 640×1136）
FGUIManager.Initialize(new SCEAdapter(), designWidth: 1136, designHeight: 640);
#endif
```

`FGUI/Client/FGUIBootstrapClientSys.cs` 是一个 `IGameClass`，会被星火自动发现，在 `MapGameMode` 的 `EventGameStart` 里引导 FGUI——**最完整的接入范例**，可直接参考或替换成自己的引导逻辑。加载 UI 包、创建组件、绑定事件的完整用法见它与 `FGUI/Samples/`。

---

## 📁 仓库结构

```
FairyGUI-spark/
├── LICENSE
├── README.md
├── docs/                    # 实现文档（文本装饰等）
└── FGUI/                    # 库本体（命名空间主要为 FairyGUI）
    ├── FGUIManager.cs       # 库入口 / 初始化
    ├── Core/                # FGUIObject、Package、PackageItem…
    ├── UI/                  # Button/Label/Image/List/ScrollPane/Component…
    ├── Gears/               # Gear（控制器驱动的属性联动）
    ├── Event/               # 事件分发
    ├── Tween/               # GTween 补间
    ├── Utils/               # ByteBuffer、XML、UBB、字体映射…
    ├── Render/              # 渲染适配层：ISCEAdapter / SCEAdapter / SCERenderContext
    ├── SCE/                 # 星火运行时对接
    ├── Client/              # 星火接入层（IGameClass 引导 + 资源加载，#if CLIENT）
    └── Samples/             # 示例 Demo（Basics / Bag / VirtualList，编译时被 Exclude）
```

> 命名空间以 `FairyGUI`（及 `.Render`、`.Utils`、`.Gears`）为主；接入层 `FGUI/Client/**` 位于 `GameEntry` 命名空间、`#if CLIENT` 包裹。
> 示例 `FGUI/Samples/`：**Basics**（基础组件）、**Bag**（背包）、**VirtualList**（虚拟列表）——仅作参考，编译时被排除、不进产物。

---

## 📋 文本特性支持

| 特性 | 状态 | 说明 |
|---|:---:|---|
| 描边 Stroke / 阴影 Shadow | ✅ | 引擎原生 Label 属性，零额外开销 |
| 下划线 / 粗体 / 斜体 / 颜色 | ✅ | `<u><b><i><color>` 内联标记，可嵌套 |
| 内联字号（UBB `[size]`） | ❌ | 引擎无内联字号能力，标签被剥离；字号用**字段级** FontSize |
| 删除线 Strikethrough | ❌ | 未实现 |
| 行间距 / 字间距 | ⚠️ | 引擎原生支持，尚未接线 |
| 部分动画效果 | ⚠️ | 部分不支持 |

---

## 🔧 开发指南

**新增 UI 组件**
1. 在 `FGUI/UI/` 创建组件类，继承 `FGUIObject` 或 `FGUIComponent`
2. 在 `FGUI/Core/FGUIObjectFactory.cs` 注册组件类型
3. 在 `FGUI/Render/SCERenderContext.cs` 补渲染逻辑

**调试**：运行时日志在星火安装根 `logs/`（客户端在 `logs/client`），过滤 `[FGUI]`。

**约定**（贴合星火 WasiCore）：UI 代码 `#if CLIENT` 包裹；禁用 `Task.Run`/`Thread`，延时用 `Game.Delay()`；日志用参数化模板 `Game.Logger.LogInformation("... {Id}", id)`。

**贡献**：直接在本仓库源码上改动，提交后各引用方重新编译即可获得更新。欢迎 Issue / PR。

---

## 📄 许可证

MIT，详见 [LICENSE](LICENSE)。

---

**⭐ 如果这个库对你有帮助，欢迎 Star！**
