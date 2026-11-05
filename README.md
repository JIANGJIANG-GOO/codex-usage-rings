# Codex Usage Rings

适用于 Windows Codex 桌面端的本地用量圆环插件与伴随程序。在头像上方显示 `7d` / `5h` 剩余额度，自动定位并对齐。

![动态样式](codex-usage-rings/fast-preview.gif)

## 功能

- 圆周表示剩余额度：大于 15% 为绿色，小于等于 15% 为红色。
- 未返回有效 5h 额度时隐藏该圆环；7d 未知时显示灰色虚线。
- 所有模型的 Ultra 思考强度显示官方风格紫白流动渐变；1.5x Fast 开启时叠加粒子。
- Windows 原生子窗口随 Codex 移动；悬停显示剩余比例、重置时间及采样时间。
- 每 60 秒读取额度，支持当前用户登录自启；默认隐藏托盘图标。

## 安装和启动

需要 Windows x64，以及已安装并登录的 Codex 桌面端。最新安装包包含预编译程序；无需另装开发工具链。

1. 从 [最新 Release](https://github.com/JIANGJIANG-GOO/codex-usage-rings/releases/latest) 下载 `Codex-Usage-Rings-0.4.11.zip` 和 `SHA256SUMS.txt`，校验 SHA-256 后完整解压。
2. 在解压目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

安装器会将文件放到 `%LOCALAPPDATA%\CodexUsageRings\app`，缺少程序时用系统 .NET Framework 编译器构建；自动迁移本插件的旧市场注册、安装插件、更新当前用户登录自启任务并启动。无需管理员权限。重复运行可更新现有安装；旧进程会先收到正常退出信号，不强制终止 Codex。

安装完成后输出 JSON，并保存到安装目录的 `install-result.json`。`running=true` 仅表示实际进程路径、运行版本和新鲜状态记录一致；`display=visible` 才表示圆环已显示。Codex 未打开、最小化或暂未定位时会报告 `waiting-for-Codex-or-anchor`。

找不到 `codex` 命令时，伴随程序仍可安装，但 `pluginRegistration` 会明确报告 `pending-cli-unavailable`；注册失败则报告 `pending-registration-failed`，不能当作插件注册成功。修复 CLI 后再次运行安装器即可。

首次或重启伴随程序后，展开模型菜单一次以读取 Fast 开关；之后快速开关通过状态事件同步。安装器不会更改模型或 1.5x 设置。

### 让 AI 安装

将下面这段话连同仓库链接交给能执行本机命令的 AI：

> 请在这台 Windows 电脑安装 https://github.com/JIANGJIANG-GOO/codex-usage-rings 的最新稳定版。下载仓库源码后，阅读 README 和 Install.ps1，运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -DownloadLatest`。安装器会下载最新 Release 并验证 SHA-256，完成安装、插件注册、自启和运行验证。请检查输出的版本、安装路径、pluginRegistration、running 和 display；如果注册待完成或圆环尚未显示，请如实反馈，不要把进程启动等同于完全安装成功。首次 Fast 检测需要我展开模型菜单，不要代我改变模型或开关。

已保存安装器的用户也可以用同一命令更新最新版，避免固定下载旧版本。

### 安装选项和管理

```powershell
# 从最新 Release 下载、校验并安装
.\Install.ps1 -DownloadLatest
# 自定义固定目录
.\Install.ps1 -InstallDir 'D:\Apps\CodexUsageRings'
# 只安装伴随程序，不注册 Codex 插件
.\Install.ps1 -SkipPluginRegistration
# 不使用登录自启；同时移除已有的本插件自启任务
.\Install.ps1 -NoAutoStart
# 安装后不启动；输出 running=false，不进行启动验证
.\Install.ps1 -NoStart
```

进入安装目录下的 `codex-usage-rings`，运行 `Stop.cmd` 停止、`Start.cmd` 启动、`Status.ps1` 诊断。默认隐藏托盘图标。`AutoStart.ps1 -Remove` 可取消自启。

更换目录时，安装器会更新旧任务；直接运行新目录的 `Start.ps1` 也会核对任务的程序路径、工作目录和参数，发现旧路径先修复再启动。手动安装而不使用统一入口时，可运行根目录 `Register-plugin.ps1`，再运行 `codex plugin add codex-usage-rings@local-usage-rings`。

## 从源码构建

Windows x64，使用系统 .NET Framework 4.x 自带 C# 编译器，不需下载构建依赖。

```powershell
.\codex-usage-rings\Build.ps1
.\codex-usage-rings\Test.ps1
.\codex-usage-rings\Test-Native.ps1
.\codex-usage-rings\Test-Hover.ps1
.\codex-usage-rings\Test-Reset.ps1
.\codex-usage-rings\Test-Install.ps1
```

源码仓库不包含编译产物；`Start.cmd` 会在程序缺失时尝试构建。Release 安装包包含预编译程序。

## 原理与适用范围

额度通过本机 Codex app-server 的 `account/rateLimits/read` 读取，按周期长度区分 7d 与 5h；程序不读取或保存账户凭据。窗口与模式识别使用 Windows UI Automation。

这是独立社区工具，采用原生子窗口附着，不是官方提供的头像区域嵌入接口，也不修改 Codex 安装文件。客户端辅助功能结构变化可能需要适配；当前验证宿主为 OpenAI.Codex 26.1013.4894.0。Ultra 与 Fast 动效参考该版本的本地界面效果，并针对圆环改编。

Codex 窗口可见且定位可靠时显示；切换到其他应用不隐藏圆环，最小化或隐藏宿主时隐藏。多显示器位置和 DPI 已进行计算测试，实际多显示器切换未逐项人工验证。

## 诊断

运行 `codex-usage-rings/Status.ps1`。状态文件位于 `%LOCALAPPDATA%/CodexUsageRings/status.json`，包含定位、模式识别、绘制与悬停诊断。

v0.4.11 验证：319 项常规检查、64,000 帧渲染、135 项悬停检查；原生窗口移动、重连、后台显示、最小化与恢复测试通过；用户实机确认切换应用和切回均正常。

统一安装器另有 33 项隔离检查，涵盖旧任务路径、首次和重复安装、源码构建、下载校验、版本冲突及启动状态验证。已在本机实测插件注册与自启迁移。

当前源码包含后台 Fast 刷新、切换思考强度时保留 Fast 状态，以及手动重置卡面板；Release 安装包为 v0.4.11。

## 重置卡

点击 7d 或有效的 5h 圆环打开卡片列表，展示卡片名称与到期时间；右上角可刷新。每张可用卡片右侧有“重置”按钮。

- 第一次点击“重置”变为“确认”；第二次点击必须严格晚于第一次 0.5 秒且早于 10 秒，才提交所选卡片的重置请求。
- 关闭面板、切换卡片、超时或宿主失去前台焦点会取消确认；处理期间禁止重复提交。
- 只使用明确选中的卡片 ID；操作前再次核对账户、卡片状态和有效期。请求结果不确定时不会自动重试，再次手动操作同一卡片会复用本次进程中的请求标识。
- 操作完成后重新读取卡片和额度；“无可重置周期”等响应会明确显示，不会伪报成功。
- 卡片详情不可用时不会根据数量虚构卡片；不提供购买入口。

开发验证：32 项重置卡/确认/请求校验检查，35 项原生面板检查（模拟服务），真实账户只读查询。未使用真实重置卡测试提交效果。

协议依据：[OpenAI App Server](https://learn.chatgpt.com/docs/app-server#8-earned-rate-limit-resets-chatgpt)。
面板启动后预读取卡片，60 秒内重新打开使用缓存；过期后后台刷新。重置前仍执行实时账户与卡片校验。

重置成功且服务器确认额度增加后，圆环平滑回填、绿色粒子沿圆周切线向末端汇聚；实际回满后轻微白闪。动画使用真实前后额度，仅改变绘制过程。新增 65 项回填检查及模拟重置触发验证。
