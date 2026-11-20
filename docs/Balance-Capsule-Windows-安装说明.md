# Balance Capsule for Windows

需要 Windows 11（内部版本 22000 或更高）和 x64 处理器。安装包自带运行时。

1. 退出托盘中正在运行的旧版 Balance Capsule。
2. 把 `BalanceCapsule-win.15-x64.exe` 保存到固定位置，双击启动。可选用 ZIP 备用包，完整解压后运行其中的 `BalanceCapsule.exe`。
3. 在已登录的 Codex 桌面应用或 CLI 环境中使用。查看 Claude Code 额度时，可在托盘选择「安装 Claude Code 桥接…」，再到 Claude Code 发一条消息刷新。

托盘或球体右键菜单的「显示来源」可切换 Codex、Claude Code 或同时显示。双球可分别拖动，位置会记住。鼠标悬停展开对应详情，双击球体立即刷新。官方同时提供 5 小时和一周额度时，球体与托盘提示每 3 秒轮换。

菜单可切换标准或深色详情玻璃，也可调整透明度。Windows 版使用同尺寸的矢量球体、液位和连接动画，并用半透明渐变实现玻璃外观。

启动日志位于 `%LOCALAPPDATA%\BalanceCapsule\startup.log`。可将安装包和 SHA-256 文件放在同一目录进行完整性核对。
