<div align="center">

<img src="src/Pinwheel/assets/Pinwheel-128.png" width="96" height="96" alt="Pinwheel Logo" />

# Pinwheel (风车轮盘)

**极简、现代、高效的 Windows 11 Fluent 风格智能径向效率轮盘**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D4.svg)](https://microsoft.com)
[![.NET](https://img.shields.io/badge/.NET-8.0--windows-512BD4.svg)](https://dotnet.microsoft.com/)
[![CI](https://github.com/your-username/Pinwheel/actions/workflows/ci.yml/badge.svg)](https://github.com/your-username/Pinwheel/actions)
[![Release](https://img.shields.io/badge/Release-v0.1.0-brightgreen.svg)](#-下载与使用两种版本供您自选)
[![PRs Welcome](https://img.shields.io/badge/PRs-Welcome-brightgreen.svg)](https://github.com/your-username/Pinwheel/pulls)
[![指令包开发规范](https://img.shields.io/badge/Docs-指令包规范-orange.svg)](指令包规范.md)

*随时随地，鼠标一键呼出双环轮盘。全局高频动作、软件专有指令，指尖触手可及。*

[快速开始](#-下载与使用两种版本供您自选) • [核心特色](#-核心特色) • [快捷键操作](#-默认快捷键与操作) • [指令包开发教程](#-制作与扩展自定义指令包) • [更新日志](CHANGELOG.md)

</div>

---

## 📖 目录

- [🌟 核心特色](#-核心特色)
- [🚀 下载与使用（两种版本供您自选）](#-下载与使用两种版本供您自选)
- [🎮 默认快捷键与操作](#-默认快捷键与操作)
- [🧩 制作与扩展自定义指令包（开放生态）](#-制作与扩展自定义指令包)
  - [1. 指令包结构](#1-指令包结构)
  - [2. manifest.json 格式定义](#2-manifestjson-格式与字段说明)
  - [3. 6 大指令类型代码表](#3-支持的-6-大指令类型type)
  - [4. 统一无感 Toast 通知规范](#4-脚本编写与统一通知契约核心规范)
  - [5. 一键打包与拖拽安装](#5-一键打包与安装)
  - [6. 官方预置指令包模版库](#6-官方预置指令包模版)
- [🛠️ 本地编译构建](#️-自行编译构建)
- [🤝 参与贡献](#-参与贡献与反馈)
- [📄 开源许可证](#-开源许可证)

---

## 🌟 核心特色

### 🎯 双环径向交互与极致性能
- **<1ms 瞬时零延迟呼出**：视觉树持久化与零 DOM 分配（Zero-Allocation），24 个内外圈扇片启动时常驻构建，呼出仅需做微秒级数据绑定，彻底消灭呼出卡顿；
- **双环黄金布局**：
  - **内环 8 格**：核心高频动作，放射对称，快速肌肉记忆盲操；
  - **外环 16 格**：左半区自适应当前前台软件专有指令，右半区固定展示 8 大全局通用动作；
- **几何级等宽平行间隙**：外圈扇片采用平行切线法向偏移算法，从内到外保持严格等宽的视觉流线；
- **轻巧 Q 弹果冻微动效**：呼出保持瞬开，光标掠过分格时以 1.07 比例过冲回弹（`BackEase`），手感软糯清脆。

### 🎨 原生 Windows 11 Fluent 双重主题
- **全新高质感“亚克力毛玻璃 (Acrylic)”**：通透磨砂冰晶半透材质、高亮白色折射倒角反光边（Specular Edge Light）与底层漫反射雾化微光盘，充满通透高级感；
- **“经典白底 (Fluent)”**：纯白实体扎实底板，全局启用硬件级 ClearType 亚像素字体渲染与精致投影，无论何种屏幕缩放，字迹清晰锐利不发虚。

### 🔀 智能场景感知
- 毫秒级识别当前操作的前台软件（Microsoft Word、Excel、PowerPoint、Adobe Photoshop、Illustrator、CorelDRAW、VS Code 等）；
- 呼出时自动切换为该软件的专有指令集，切出软件后无缝回退为全局轮盘；全屏观影或游戏场景自动静默免打扰。

### 🌐 全局独立单例划词翻译
- 任意软件划选文本，轮盘一键呼出单例 Win11 悬浮翻译面板；
- 深度支持 **DeepSeek**（官方 API）、**硅基流动**（SiliconFlow）、**OpenAI** 及免密通道；
- 记录流式卡片持久化呈现，窗口支持置顶锁定与手动清空，绝不抢占前台输入焦点。

### 🧩 开放强大的指令包生态
- 涵盖打开程序、打开网页、打开文件夹、模拟按键（防修饰键串键）、发送文本、运行脚本与组合指令；
- 开放标准 `.zip` 插件机制，任何人只需一个 `manifest.json` + PowerShell 脚本即可自制任意软件的自动化扩展。

### 🍃 绿色纯净便携（Portable）
- 零注册表污染、无需管理员权限，出厂配置纯净，自动维护 20 份历史快照；随 U 盘或网盘即拷即用。

---

## 🚀 下载与使用（两种版本供您自选）

前往 GitHub 仓库的 **[Releases](../../releases)** 页面下载最新版本：

| 版本类型 | 压缩包名称 | 体积 | 特点与适用对象 |
| :--- | :--- | :---: | :--- |
| ⭐ **零依赖独立版（强烈推荐）** | `Pinwheel-v0.1.0-win-x64-standalone-zero-dependency.zip` | ~67 MB | **内置完整运行时，100% 零门槛**！<br>电脑无论是否装过 .NET，解压后双击 `Pinwheel.exe` 直接秒开！适合绝大多数普通用户。 |
| 🪶 **极简便携版** | `Pinwheel-v0.1.0-win-x64-portable.zip` | ~430 KB | **极致轻巧，体积仅几百 KB**！<br>需要电脑已装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)，适合极客与开发者。 |

### 快速上手
1. 下载上述任一 zip 压缩包并解压到任意目录（例如 `D:\Tools\Pinwheel`）；
2. 双击 `Pinwheel.exe` 即可启动运行（系统托盘将常驻轮盘图标）；
3. 若需开机启动，可在 Pinwheel 设置面板中直接勾选“开机自启”。

---

## 🎮 默认快捷键与操作

| 动作 | 默认快捷键 | 操作说明 |
| :--- | :--- | :--- |
| **呼出轮盘** | `Ctrl + 鼠标中键` | 在任意窗口中按下立即弹出双环轮盘，滑动鼠标选择，松开鼠标即触发执行 |
| **划词翻译** | 轮盘选择 `🌐 划词翻译` | 选中任意窗口中的文字后触发，首次使用引导配置 API Key，后续秒开翻译 |
| **清空格子** | `Delete` 或 `Backspace` | 在设置界面选中任意格子时按下，一键清空该槽位 |
| **打开设置** | 双击系统托盘图标 | 打开主配置面板，管理各软件分格、外观主题与指令包 |

---

## 🧩 制作与扩展自定义指令包

Pinwheel 拥有极其简单且强大的指令包扩展生态。任何会压缩 zip 的人，按照规范都能在 2 分钟内做出可分发、可安装的扩展指令包！

> 📘 **完整规范文档已包含在发行包根目录**，也可在线阅读：[**《指令包规范与开发文档》（指令包规范.md）**](指令包规范.md)

### 1. 指令包结构
指令包就是一个标准的 `.zip` 压缩包，解压后文件直接位于 zip 根目录（不要多套一层文件夹）：

```text
我的指令包.zip
├── manifest.json        （必填：指令定义说明文件，UTF-8 编码）
└── 我的脚本.ps1          （可选：仅当指令类型为运行脚本时需要，UTF-8 带 BOM）
```

### 2. `manifest.json` 格式与字段说明

```json
{
  "name": "自动列宽",
  "icon": "📏",
  "type": 5,
  "target": "auto_fit.ps1",
  "process": "EXCEL.EXE",
  "description": "选中整列后触发，自动让 Excel 单元格列宽自适应内容。"
}
```

| 字段 | 必填 | 说明 |
| :--- | :---: | :--- |
| **name** | 是 | 指令显示名称（如：`自动列宽`、`无损转PDF`） |
| **icon** | 是 | 轮盘上显示的图标，推荐使用单个 Emoji（如：`📏`、`📑`、`🌐`） |
| **type** | 是 | 指令类型代码（`0`~`5`，详见下表） |
| **target** | 是 | 目标对象（程序路径、网址、文件夹、快捷键、文本或脚本文件名） |
| **process** | 否 | 归属软件的真实进程名（如 `EXCEL.EXE`、`WINWORD.EXE`、`Photoshop.exe`）。**留空表示全局通用指令** |
| **description** | 否 | 指令功能描述，展示在 Pinwheel 设置面板的指令说明区 |

### 3. 支持的 6 大指令类型（type）

| type | 类型 | target 填写示例 | 典型应用场景 |
| :---: | :--- | :--- | :--- |
| **0** | 打开程序 | `calc.exe` 或 `C:\Tools\app.exe` | 启动特定应用程序 |
| **1** | 打开网址 | `https://github.com` | 浏览器一键跳转常用网站 |
| **2** | 打开文件夹 | `D:\Work\Projects` | 快速唤出常用文件夹目录 |
| **3** | 模拟快捷键 | `^c` (Ctrl+C), `%{F4}` (Alt+F4), `{F5}` | 触发前台软件自带快捷动作 |
| **4** | 发送文本 | `这是一段常用快捷输入文本` | 快速粘贴模板文本 |
| **5** | **运行脚本** | `auto_fit.ps1`（只写包内文件名，不带路径） | **调用 COM/Win32/API，实现高级业务自动化** |

### 4. 脚本编写与统一通知契约（核心规范）
为了保持纯净一致的现代 Windows 11 交互体验，**严禁使用 `[MessageBox]::Show` 阻塞式弹窗或自行弹出托盘气泡**：
- **执行成功**：在 PowerShell 脚本中使用 `Write-Output "操作完成说明"`，Pinwheel 会自动在屏幕右下角以绿色 Win11 Fluent Toast 悬浮卡片呈现，**倒计时 3 秒自动淡出关闭，底层启用 `WS_EX_NOACTIVATE` 绝不抢占前台输入焦点**；
- **执行失败**：在 PowerShell 脚本中使用 `Write-Error "具体失败原因"; exit 1`，Pinwheel 自动弹出红色错误 Toast；
- **规范脚本范例 (`auto_fit.ps1`)**：
  ```powershell
  $ErrorActionPreference = 'Stop'
  try {
      $app = [System.Runtime.InteropServices.Marshal]::GetActiveObject('Excel.Application')
      if ($null -eq $app) { throw '未检测到运行中的 Excel，请先打开工作表' }
      $app.Selection.EntireColumn.AutoFit() | Out-Null
      Write-Output '已自动调整选中列的列宽'
      exit 0
  }
  catch {
      Write-Error ("执行失败: " + $_.Exception.Message)
      exit 1
  }
  ```

### 5. 一键打包与安装
1. **一键压缩**：将 `manifest.json` 与脚本打包为 `.zip`：
   ```powershell
   Compress-Archive -Path .\manifest.json, .\auto_fit.ps1 -DestinationPath .\Excel-自动列宽.zip -Force
   ```
2. **拖拽安装**：直接将生成的 `.zip` 文件**拖拽进 Pinwheel 设置窗口**，或在「指令包」页面点击「安装指令包」即可秒速启用！

### 6. 官方预置指令包模版
项目根目录下的 **[`指令包/`](指令包/)** 文件夹内置了 20+ 个官方标准指令包模板，涵盖：
- **Word / Excel / PPT**：原生无损转 PDF、表格自动行列宽、添加专业边框等；
- **系统与效率工具**：Everything 划词极速搜索、按键映射、媒体静音、窗口置顶、系统录屏等。

欢迎直接解压参考这些模板制作您自己的专属指令包！

---

## 🛠️ 自行编译构建

如果您希望从源代码进行本地编译与开发：

```bash
# 1. 克隆代码库
git clone https://github.com/your-username/Pinwheel.git
cd Pinwheel

# 2. 编译 Release 版本
dotnet build src/Pinwheel/Pinwheel.csproj -c Release

# 3. 运行构建产物
./src/Pinwheel/bin/Release/net8.0-windows/Pinwheel.exe
```

若需一键生成便携版与独立自包含版安装包，在项目根目录运行：
```cmd
一键打包发布.bat
```
打包生成后的文件会自动保存在 `dist/` 目录下。

---

## 🤝 参与贡献与反馈

欢迎提交 Issue 与 Pull Request！
- 发现 Bug 或体验问题？欢迎在 [Issues](../../issues) 中反馈；
- 有好用的新指令包？欢迎将编写好的指令包提 PR 提交到 `指令包/` 目录供大家使用；
- 如果觉得 Pinwheel 对您有帮助，欢迎点亮右上角的 ⭐️ **Star** 支持一下！

---

## 📄 开源许可证

本项目基于 [MIT License](LICENSE) 协议开源，欢迎自由使用、修改、分发及商业应用。
