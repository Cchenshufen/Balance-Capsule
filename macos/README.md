# Balance Capsule for macOS

这是 Balance Capsule 的原生 AppKit 版本，最低支持 macOS 26，仅支持 Apple Silicon（arm64）。液体玻璃界面、液位、气泡、刻度、连接颈部、展开动画和进度条全部由代码实时绘制，不使用效果图贴图。

## 已移植功能

- 常驻桌面的透明悬浮球、展开胶囊拖动与屏幕边缘吸附
- 鼠标悬停详情面板和菜单栏额度状态
- Codex 官方 `app-server` 只读额度查询
- Codex 当前登录账号的每日 Token 桶与账号累计 Token 查询
- DeepSeek 账户余额与 OpenRouter 当前 Key 剩余额度
- Claude Code `statusLine` 只读额度桥接
- 5 小时/一周额度切换、60 秒自动刷新、手动刷新
- 登录时启动、动画开关和本机设置持久化

macOS 版不会读取聊天记录、本机会话日志、浏览器 Cookie 或 Codex `auth.json`。Codex Token 统计直接使用官方 `account/usage/read` 账号响应；第三方余额请求仅允许同主机 HTTPS GET，禁止重定向，响应上限 256 KB。Claude Code 的个人账号没有同等官方统计 API，因此不会用本机数据冒充账号总量；组织版需要单独的 Admin/Analytics API 凭据。

## 构建

```bash
chmod +x scripts/build-macos.sh
scripts/build-macos.sh
```

产物位于 `artifacts/macos/`。本地构建使用 ad-hoc 签名；没有 Apple Developer ID 的个人构建首次打开时，需在 Finder 中右键应用并选择“打开”。
