# CodeTabs

顶部标签栏采用紧凑布局：左侧四宫格打开 Codex 总览，右侧依次为新建（＋）、拖拽接入（准星）、全部接入 VS Code（叠放窗口带箭头）、移出全部 VS Code（叠放窗口带减号）、保存组合（软盘）、恢复组合（回转箭头）、置顶（图钉）和菜单（三横线）。悬停图标可查看完整操作说明；置顶和拖拽接入启用时显示蓝色底色。标签标题使用 13 像素常规字体，减少 150% 显示缩放下的拥挤。

“移出全部 VS Code”仅恢复 VS Code 为独立窗口，不关闭应用，其他标签保留。“保存组合”将所有已接入应用的窗口列表、当前标签和从左到右的顺序保存到 `config/window-layout.json`，覆盖上次保存；空列表不会覆盖原配置。“恢复组合”按保存顺序接入已打开且能唯一识别的窗口，已有的其他标签排在末尾。没有打开或无法唯一识别的窗口会列出提示，不会自动启动应用或恢复应用内的文档；重新打开应用后可再次恢复组合。配置包含窗口标题，应与其他本地配置一样保持私有。

Windows 桌面窗口标签管理器，附带本地 Codex 会话总览。适合同时打开多个 VS Code 项目时集中切换窗口、查看任务状态。

当前版本：**0.16.11-preview**。Windows 10/11 x64，.NET Framework 4.8。便携运行，无需 Node.js 或 Python；程序暂未签名。

## 下载与启动

在 [GitHub Releases](https://github.com/HyranX/codetabs/releases) 的 **Assets** 中下载 `CodeTabs-0.16.11-preview-windows-x64-portable.zip` 及同名 `.sha256` 文件。GitHub 自动提供的 Source code ZIP 是源码，不包含编译后的程序。

1. 将便携包解压到固定、可写的目录。
2. 双击 `CodeTabs.exe`。
3. 点击“全部接入 VS Code”，或通过菜单选择其他窗口。
4. 升级前正常退出旧版，再替换程序；保留旁边的 `config/` 即可保留本地设置。

当前工作区的已编译下载包位于 `releases/`，该目录不进入源码提交。在线下载由版本标签触发的 GitHub Actions 构建生成。

## 主要功能

- 窗口标签切换、拖拽排序、移出和正常关闭；支持置顶、自动尺寸同步。
- VS Code 默认使用**输入法兼容模式**：保留独立窗口，隐藏已接入窗口的任务栏及 Alt+Tab 入口，移出或正常退出后恢复，避免跨进程嵌入引起的输入法冲突。
- 点击当前 VS Code 时，CodeTabs 标签栏所在容器会跟随到它正下方；输入焦点保留在 VS Code。
- 移出窗口或正常退出时，恢复接入前的位置、像素尺寸和最大化状态；支持多屏及混合缩放。
- Codex 总览：按项目或独立对话浏览、搜索、重命名、隐藏和置顶；显示执行状态与完成提醒。
- 全部偏好保存在程序旁的 `config/`，可随便携目录迁移。

## 常用操作

| 操作 | 方法 |
| --- | --- |
| 接入 VS Code | “全部接入 VS Code” |
| 接入其他应用 | 菜单 → 选择应用窗口接入 |
| 直接拖入 | 将普通窗口标题栏拖到标签栏，松开；最大化窗口先还原 |
| 反向接入 | 按住“拖拽接入”，拖到目标窗口后松开；Esc 取消 |
| 切换标签 | 点击标签，或 Win+Alt+鼠标左/右键、Win+Alt+滚轮 |
| 标签排序 | 左右拖动标签；滚轮滚动溢出的标签栏 |
| 移出窗口 | 标签右侧向外箭头，恢复独立窗口及接入前状态 |
| 关闭应用 | 标签上的 ×；保留应用自身的未保存确认 |
| 新建 VS Code | ＋，或双击标签栏空白处 |
| 标签菜单 | 右键标签；点击其他区域收起 |
| 原嵌入模式 | 取消菜单中的“VS Code 输入法兼容模式”；重新接入生效 |

0.16.7 修正右键菜单失效/闪烁，以及移动容器后内容被遮挡、跟随迟缓的问题。位置同步使用移动事件，窗口抬升不会主动切换输入焦点。

## Codex 总览

读取当前用户可访问的本地 Codex 会话与索引。默认从用户目录的 `.codex/` 读取，也支持 `CODEX_HOME`。不改写 Codex 会话，不保存提示词正文；本地配置会保存项目路径、会话关联和自定义标题，因此不应随源码或发布包上传。

双击卡片或点击打开按钮，优先切换已接入窗口，再寻找已打开的项目窗口，否则启动 VS Code。此操作不会自动执行 Codex 任务，也不保证选中 VS Code 内某条对话。远程项目需要现有 SSH 配置与 Remote-SSH 扩展。

完成状态来自本地日志，不代表任务质量验收。“已在 CodeTabs 中打开”仅记录打开动作。没有本地日志的云端/远程会话可能只有索引信息；后台 subagent 会话不作为用户任务发送完成提醒。

## 兼容性与限制

- 容器和目标应用应使用相同权限。微信应用使用独立窗口兼容方式。
- 多屏恢复记录本次接入前的状态，无法追溯旧版已经改变的尺寸；显示器移除或工作区改变时会调整到可见区域。
- 正常退出会等待异步恢复完成；尚无崩溃恢复守护，请使用正常退出。
- 特殊弹窗、自绘窗口、游戏及第三方应用仍可能存在兼容性差异。
- 自动回归使用独立测试窗口，已覆盖多屏混合 DPI、右键菜单、移动跟随和窗口还原；这不等同于所有 VS Code/输入法版本的端到端保证。

## 从源码编译

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Compile.ps1
```

使用 Windows 自带的 .NET Framework x64 C# 编译器，输出根目录 `CodeTabs.exe` 和 `versions/` 版本备份。图标已经随源码提供；只有重新生成图标才需要 Python/Pillow。

生成便携包并运行验证：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1
```

中间文件放在源码目录之外；可通过 `-ScratchRoot` 或 `CODETABS_SCRATCH_ROOT` 指定。打包会验证版本、文件白名单、哈希及测试，再写入 `releases/`。`-SkipUiTests` 仅供没有交互桌面的 CI 使用，会明确跳过桌面回归，仍运行非 UI 测试。

## 目录

```text
src/                  C# 应用源码、内置回归和 manifest
assets/               图标资源
scripts/              打包、公开文件检查、图标生成
.github/workflows/    标签触发的 GitHub Release 流程
docs/                 分发说明、隐私与发布指南
Compile.ps1           编译入口
LICENSE / NOTICE      Apache-2.0 许可证与声明
CHANGELOG.md          版本摘要
```

本地 `config/`、`.codex/`、`.local/`、`versions/`、`releases/` 和编译产物均被 Git 忽略。`.local/` 保存历史验证记录，不随公开仓库分发。

参见 [隐私与公开发布检查](docs/PRIVACY.md)、[发布指南](docs/PUBLISHING.md) 和 [更新记录](CHANGELOG.md)。

## 许可证

[Apache License 2.0](LICENSE)。Copyright 2026 HyranX。CodeTabs 是独立项目，与 Microsoft、OpenAI 或 Tencent 无隶属关系。
