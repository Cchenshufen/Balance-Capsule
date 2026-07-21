# Balance Capsule Windows 安装说明

版本：`1.2.15-win.16`

1. 直接下载并运行 `BalanceCapsule-1.2.15-win.16-x64.exe`，不需要单独安装 .NET。
2. 若安全软件阻止单文件启动，请改用 `BalanceCapsule-1.2.15-win.16-x64.zip`。
3. 完整解压 ZIP 后双击其中的 `BalanceCapsule.exe`；不要在压缩包预览窗口内直接运行。
4. 本地构建没有商业代码签名；若 SmartScreen 出现提示，请选择“更多信息”后确认运行。
5. 拖动悬浮球可调整位置，鼠标滑入自动打开详情，双击悬浮球立即刷新。
6. 右键悬浮球或任务栏托盘图标可切换 Codex/Claude Code、刷新、设置开机启动或退出。

如果启动失败，程序会显示错误提示，并将诊断信息写入 `%LOCALAPPDATA%\BalanceCapsule\startup.log`。

## 数据说明

- Codex 的今日、本月和总计 Token 来自当前登录账号官方 `account/usage/read`。
- 当官方每日数据桶尚未生成时，“今日”可能暂时显示 `0万`，不使用本机会话日志补算。
- Claude Code 个人账号没有同等官方账号 Token API，因此不会显示本机估算值或混用 Codex Token。
- DeepSeek 与 OpenRouter 第三方模式继续显示官方余额或当前 Key 剩余额度。

## 系统要求

- Windows 11（build 22000）或更新版本
- x64 处理器

界面由 WPF 矢量、Windows Acrylic 与实时动画绘制，不使用效果截图贴图。
