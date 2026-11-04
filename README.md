# Codex Usage Rings

适用于 Windows Codex 桌面端的本地用量圆环插件与伴随程序。在头像上方显示 `7d` / `5h` 剩余额度，自动定位并对齐。

![动态样式](codex-usage-rings/fast-preview.gif)

## 功能

- 圆周表示剩余额度：大于 15% 为绿色，小于等于 15% 为红色。
- 未返回有效 5h 额度时隐藏该圆环；7d 未知时显示灰色虚线。
- 所有模型的 Ultra 思考强度显示官方风格紫白流动渐变；1.5x Fast 开启时叠加粒子。
- Windows 原生子窗口随 Codex 移动；悬停显示剩余比例、重置时间及采样时间。
- 每 60 秒读取额度，支持托盘刷新和当前用户登录自启。

## 安装和启动

1. 从本仓库 Releases 下载 `Codex-Usage-Rings-0.3.2.zip` 并完整解压到固定目录。
2. 运行 `codex-usage-rings/Start.cmd`。停止时运行 `Stop.cmd`，或从托盘菜单退出。
3. 在 Codex 中展开模型菜单约 2 秒，让程序读取 Fast 开关。随后可关闭菜单；每次重启伴随程序后需重新读取一次。

需要登录自启时，在 PowerShell 中运行：

```powershell
.\codex-usage-rings\AutoStart.ps1
```

取消登录自启：

```powershell
.\codex-usage-rings\AutoStart.ps1 -Remove
```

可选的 Codex 插件注册（需要可用的 `codex` 命令）：

```powershell
.\Register-plugin.ps1
codex plugin add codex-usage-rings@local-usage-rings
```

## 从源码构建

Windows x64，使用系统 .NET Framework 4.x 自带 C# 编译器，不需下载构建依赖。

```powershell
.\codex-usage-rings\Build.ps1
.\codex-usage-rings\Test.ps1
.\codex-usage-rings\Test-Native.ps1
.\codex-usage-rings\Test-Hover.ps1
```

源码仓库不包含编译产物；`Start.cmd` 会在程序缺失时尝试构建。Release 安装包包含预编译程序。

## 原理与适用范围

额度通过本机 Codex app-server 的 `account/rateLimits/read` 读取，按周期长度区分 7d 与 5h；程序不读取或保存账户凭据。窗口与模式识别使用 Windows UI Automation。

这是独立社区工具，采用原生子窗口附着，不是官方提供的头像区域嵌入接口，也不修改 Codex 安装文件。客户端辅助功能结构变化可能需要适配；当前验证宿主为 OpenAI.Codex 26.1013.4894.0。Ultra 与 Fast 动效参考该版本的本地界面效果，并针对圆环改编。

只有 Codex 位于前台且定位可靠时才显示。多显示器位置和 DPI 已进行计算测试，实际多显示器切换未逐项人工验证。

## 诊断

运行 `codex-usage-rings/Status.ps1`。状态文件位于 `%LOCALAPPDATA%/CodexUsageRings/status.json`，包含定位、模式识别、绘制与悬停诊断。

v0.3.2 验证：192 项常规检查、64,000 帧渲染、135 项悬停检查；原生窗口移动与重连测试通过。
