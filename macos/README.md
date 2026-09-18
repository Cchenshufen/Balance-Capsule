# Balance Capsule for macOS

这是 Balance Capsule 的原生 AppKit 版本，最低支持 macOS 26，仅支持 Apple Silicon（arm64）。液体玻璃界面、液位、气泡、刻度、连接颈部、展开动画和进度条全部由代码实时绘制，不使用效果图贴图。

## 已移植功能

- 常驻桌面的透明悬浮球、展开胶囊拖动与屏幕边缘吸附
- 鼠标悬停详情面板和菜单栏额度状态
- Codex 官方 `app-server` 只读额度查询
- Codex 当前登录账号的每日 Token 桶与账号累计 Token 查询
- Claude Code `statusLine` 只读额度桥接
- Codex、Claude Code 单独显示或双源同时显示
- 官方返回的 5 小时/一周额度自动轮换、60 秒自动刷新、手动刷新
- 单实例运行、登录时启动和本机设置持久化

macOS 版不会读取聊天记录、本机会话日志、浏览器 Cookie 或 Codex `auth.json`。Codex Token 统计直接使用官方 `account/usage/read` 账号响应。Claude Code 的个人账号没有同等官方统计 API，因此不会用本机数据冒充账号总量；组织版需要单独的 Admin/Analytics API 凭据。

## 构建

```bash
chmod +x scripts/build-macos.sh
scripts/build-macos.sh
```

产物位于 `artifacts/macos/`。未配置签名环境变量时使用 ad-hoc 签名，首次打开需在 Finder 中右键应用并选择“打开”。公开分发时设置 `BALANCE_CAPSULE_CODESIGN_IDENTITY` 为 Developer ID Application 证书，并设置 `BALANCE_CAPSULE_NOTARY_PROFILE` 为已保存的 `notarytool` 钥匙串配置；构建脚本会自动签名、公证和装订票据。
