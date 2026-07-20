# Quota Orb v1.1 第三方额度实施计划

> 基线：v1.0.1（`268e3d3`）
> 原则：在现有项目上增量修改；先支持明确场景，再根据真实需求扩展。

## 总体流程

1. 先锁定 v1.0.1 基线和现有测试结果。
2. 修复“刷新失败就清空显示”的旧问题。
3. 在现有刷新循环中识别当前额度来源。
4. 接入 DeepSeek 和 OpenRouter 两个明确适配器。
5. 只读接入 CC Switch 当前供应商和模板元数据。
6. 对 Codex++ 做保守识别，聚合模式明确不支持单一余额。
7. 更新 UI、说明文档和发布版本。
8. 运行完整验证，检查密钥泄漏和 Git 差异后再发布。

## Task 0：建立基线

目标：确保升级不会恢复 v1.0.0 已删除的 7D/2D 徽标。

- 确认分支基于 `v1.0.1`。
- 运行现有 Core 和 Windows 测试，记录变更前基线。
- 搜索 `WindowBadgeText`、`ResetBadgeText`、`7D`、`2D`，确认生产 UI 已删除相关绑定。
- 检查工作区，避免混入抖音素材或其他任务文件。

验证：

```powershell
dotnet test QuotaOrb.sln -c Release
rg -n "WindowBadgeText|ResetBadgeText|>7D<|>2D<" src tests
git status --short
```

## Task 1：保留同一来源的上次成功结果

涉及：

- `src/QuotaOrb.Core/Policy/QuotaRiskPolicy.cs`
- `src/QuotaOrb.Core/Refresh/QuotaStateService.cs`
- 对应 Core 测试

步骤：

1. 先增加回归测试：同一来源成功一次后读取失败，仍保留旧值并标记为过期。
2. 区分“从未成功”与“已有旧值”。
3. 切换来源时清除旧来源快照，防止串数据。
4. 不改变现有风险阈值和官方额度计算。

建议提交：`fix: retain stale quota after refresh failure`

## Task 2：加入最小来源识别

新增或调整：

- `ActiveProvider`
- `ActiveProviderDetector`
- 最小 TOML 读取器
- `QuotaStateService` 组合逻辑

步骤：

1. 为当前 `model_provider`、Base URL 和非敏感指纹写解析测试。
2. 识别官方、普通第三方、本机回环代理和未知配置。
3. 指纹中禁止出现 API Key、Token 或完整配置。
4. 把检测放入现有 15 秒刷新循环，不新增第二个常驻计时器。
5. 来源变化时立即刷新，并清理旧来源显示。

依赖选择：

- 优先检查现有解析方式或使用简单、受测试约束的 TOML 读取。
- 只有标准库方案明显不可靠时才引入 `Tomlyn`，不要同时加入其他无关依赖。

建议提交：`feat: detect active quota source`

## Task 3：实现安全余额 HTTP 边界

新增一个共用 HTTP 读取器，限制：

- HTTPS only
- GET only
- 禁止重定向
- 同主机 only
- 10 秒超时
- 256 KB 响应上限
- 日志不包含请求头、密钥和原始响应

测试必须覆盖 HTTP、重定向、跨主机、超时和超大响应。

凭据只在请求作用域内读取。不要宣称能从 .NET 源字符串中彻底擦除密钥；验证重点是设置和日志不落盘。

建议提交：`feat: add safe balance query boundary`

## Task 4：实现 DeepSeek 和 OpenRouter

### DeepSeek

- `GET /user/balance`
- 解析 `balance_infos`
- 多币种分项保留，不自动换汇
- 标记为账户余额

### OpenRouter

- `GET https://openrouter.ai/api/v1/key`
- 解析 `data.limit_remaining`
- 标记为密钥剩余额度
- `null` 显示“密钥未设置额度上限”
- 禁止调用需要 Management Key 的 `/api/v1/credits`

测试使用本地假 HTTP handler，不使用真实密钥或外网账户。

建议提交：`feat: read DeepSeek and OpenRouter balances`

## Task 5：只读接入 CC Switch

目标版本：本机 3.16.5，同时按能力检测兼容后续结构。

步骤：

1. 使用脱敏 SQLite 夹具覆盖当前供应商、代理端口、`settings_config` 和 `meta`。
2. 数据库以只读模式打开，只查询 Codex 当前供应商所需字段。
3. 回环地址必须与 CC Switch Codex 代理端口匹配，不能只因是 localhost 就认定为 CC Switch。
4. 读取 `usage_script.enabled` 和 `template_type`。
5. 白名单内置模板映射到 Quota Orb 原生适配器。
6. 自定义 JavaScript、未知模板或未知数据库结构返回说明状态，不执行、不修改。

只有确认 .NET 运行时没有可用 SQLite 读取能力时，才加入 `Microsoft.Data.Sqlite`；不要为了未来查询引入额外数据库抽象层。

建议提交：`feat: resolve active CC Switch provider`

## Task 6：保守接入 Codex++

步骤：

1. 为官方、混合 API、纯 API、单供应商和聚合供应商建立脱敏夹具。
2. 明确不同模式的凭据位置。
3. 只有单一供应商且主机、凭据边界、余额语义均明确时才返回可查询来源。
4. 聚合、轮转、故障转移和未知 Relay 返回 `Unsupported`。
5. 不把 Relay Token 发送到推断出来的其他上游主机。

本任务不实现聚合余额，也不实现 Relay 自动探测。

建议提交：`feat: identify supported Codex++ providers`

## Task 7：扩展现有刷新与 UI

刷新规则：

- 保持一个 `QuotaStateService`、一个 `PeriodicTimer` 和一个并发门。
- 每 15 秒检测来源。
- 官方额度保持现有读取频率。
- 第三方余额至少间隔 60 秒。
- 来源变化和手动刷新立即读取。

UI 规则：

- 官方继续显示百分比。
- DeepSeek 显示金额和币种。
- OpenRouter 明确显示“Key 剩余额度”。
- 旧值显示“已过期”和最后更新时间。
- 聚合、未知 Relay、自定义脚本显示可理解的说明，不伪装成读取错误。
- 不恢复 v1.0.1 已删除的徽标。

建议提交：`feat: display active provider balance`

## Task 8：文档、版本与最终验证

更新：

- README 支持范围与限制
- CHANGELOG
- 应用版本到 v1.1.0
- 隐私说明：读取哪些本地配置、不会保存哪些内容

最终验证：

```powershell
dotnet restore QuotaOrb.sln
dotnet build QuotaOrb.sln -c Release --no-restore
dotnet test QuotaOrb.sln -c Release --no-build
rg -n --hidden --glob '!**/bin/**' --glob '!**/obj/**' "secret-test-value|OPENAI_API_KEY\s*[:=]\s*\"[^\"]+\"|experimental_bearer_token\s*=\s*\"[^\"]+\"" .
git diff --check
git status --short --branch
```

手动验证矩阵：

- 官方 Codex
- DeepSeek 直连
- OpenRouter 直连和无限额 Key
- CC Switch 直连、代理、内置模板、自定义脚本
- Codex++ 单供应商、纯 API、混合 API、聚合供应商
- 离线、超时、401、429、服务端错误
- 来源切换后无旧供应商余额残留

建议提交：`docs: prepare Quota Orb v1.1 release`

## 第二阶段候选项

以下内容不进入本轮实现，除非出现明确真实需求：

- 同主机自定义 JSON 字段映射
- 更多供应商原生适配器
- 文件系统变化监听以缩短 15 秒检测延迟
- 多供应商余额聚合

跨主机发送密钥、任意脚本执行和通用 HTTP 工作流不作为默认扩展方向。
