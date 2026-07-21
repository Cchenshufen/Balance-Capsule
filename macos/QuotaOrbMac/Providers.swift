import Foundation

enum ProviderError: LocalizedError {
    case message(String)

    var errorDescription: String? {
        switch self {
        case .message(let text): return text
        }
    }
}

final class LineChannel {
    private let condition = NSCondition()
    private var buffer = Data()
    private var ended = false

    func feed(_ data: Data) {
        condition.lock()
        if data.isEmpty {
            ended = true
        } else {
            buffer.append(data)
        }
        condition.broadcast()
        condition.unlock()
    }

    func finish() {
        condition.lock()
        ended = true
        condition.broadcast()
        condition.unlock()
    }

    func nextLine(timeout: TimeInterval) throws -> String {
        let deadline = Date().addingTimeInterval(timeout)
        condition.lock()
        defer { condition.unlock() }

        while true {
            if let newline = buffer.firstIndex(of: 0x0A) {
                let lineData = buffer.prefix(upTo: newline)
                buffer.removeSubrange(...newline)
                if let line = String(data: lineData, encoding: .utf8) {
                    return line.trimmingCharacters(in: .whitespacesAndNewlines)
                }
            }
            if ended {
                throw ProviderError.message("Codex app-server 在返回额度前退出。")
            }
            if !condition.wait(until: deadline) {
                throw ProviderError.message("Codex 额度请求超时。")
            }
        }
    }
}

enum CodexRPCProvider {
    static func read() throws -> OrbState {
        let executable = try resolveExecutable()
        let process = Process()
        let input = Pipe()
        let output = Pipe()
        let lines = LineChannel()

        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = ["-s", "read-only", "-a", "untrusted", "app-server"]
        process.standardInput = input
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice

        output.fileHandleForReading.readabilityHandler = { handle in
            lines.feed(handle.availableData)
        }
        process.terminationHandler = { _ in lines.finish() }

        do {
            try process.run()
            defer {
                output.fileHandleForReading.readabilityHandler = nil
                if process.isRunning { process.terminate() }
                try? input.fileHandleForWriting.close()
            }

            try writeJSON(
                [
                    "id": 1,
                    "method": "initialize",
                    "params": ["clientInfo": ["name": "balance-capsule-macos", "version": "BalanceCapsule-mac.15"]]
                ],
                to: input.fileHandleForWriting
            )
            _ = try response(id: 1, from: lines)
            try writeJSON(["method": "initialized", "params": [:]], to: input.fileHandleForWriting)
            try writeJSON(["id": 2, "method": "account/rateLimits/read", "params": [:]], to: input.fileHandleForWriting)
            let result = try response(id: 2, from: lines)

            guard let limits = result["rateLimits"] as? [String: Any] else {
                throw ProviderError.message("Codex 返回了无效的额度数据。")
            }
            let primary = mapWindow(limits["primary"])
            let secondary = mapWindow(limits["secondary"])
            guard primary != nil || secondary != nil else {
                throw ProviderError.message("Codex 未返回可用的额度窗口。")
            }
            let fiveHour = windowMatching(minutes: 300, first: primary, second: secondary)
            let weekly = windowMatching(minutes: 10_080, first: primary, second: secondary)
            let percent = weekly?.remainingPercent
                ?? fiveHour?.remainingPercent
                ?? primary?.remainingPercent
                ?? secondary!.remainingPercent
            var state = OrbState(
                sourceName: "Codex 官方",
                agentName: "Codex",
                fiveHour: fiveHour,
                weekly: weekly,
                risk: risk(for: percent),
                updatedAt: Date()
            )
            try writeJSON(["id": 3, "method": "account/usage/read"], to: input.fileHandleForWriting)
            if let usageResult = try? response(id: 3, from: lines) {
                state.tokenUsage = mapAccountTokenUsage(usageResult)
            }
            return state
        } catch {
            if process.isRunning { process.terminate() }
            throw error
        }
    }

    private static func resolveExecutable() throws -> String {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        let fixed = [
            "/Applications/Codex.app/Contents/Resources/codex",
            "/Applications/ChatGPT.app/Contents/Resources/codex",
            "\(home)/Applications/Codex.app/Contents/Resources/codex",
            "\(home)/Applications/ChatGPT.app/Contents/Resources/codex",
            "/opt/homebrew/bin/codex",
            "/usr/local/bin/codex"
        ]
        let pathDirectories = (ProcessInfo.processInfo.environment["PATH"] ?? "")
            .split(separator: ":")
            .map(String.init)
        for candidate in fixed + pathDirectories.map({ "\($0)/codex" }) {
            if FileManager.default.isExecutableFile(atPath: candidate) {
                return candidate
            }
        }
        throw ProviderError.message("未找到官方 Codex 桌面运行时或 CLI。")
    }

    private static func writeJSON(_ object: [String: Any], to handle: FileHandle) throws {
        var data = try JSONSerialization.data(withJSONObject: object)
        data.append(0x0A)
        try handle.write(contentsOf: data)
    }

    private static func response(id: Int, from lines: LineChannel) throws -> [String: Any] {
        let deadline = Date().addingTimeInterval(8)
        while Date() < deadline {
            let line = try lines.nextLine(timeout: max(0.1, deadline.timeIntervalSinceNow))
            guard let data = line.data(using: .utf8),
                  let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  (object["id"] as? NSNumber)?.intValue == id else {
                continue
            }
            if let error = object["error"] as? [String: Any] {
                throw ProviderError.message(error["message"] as? String ?? "Codex RPC 请求失败。")
            }
            guard let result = object["result"] as? [String: Any] else {
                throw ProviderError.message("Codex RPC 响应缺少结果。")
            }
            return result
        }
        throw ProviderError.message("Codex 额度请求超时。")
    }

    private static func mapWindow(_ raw: Any?) -> QuotaWindowValue? {
        guard let object = raw as? [String: Any],
              let used = object["usedPercent"] as? NSNumber else { return nil }
        let duration = (object["windowDurationMins"] as? NSNumber)?.intValue
        let timestamp = (object["resetsAt"] as? NSNumber)?.doubleValue
        return QuotaWindowValue(
            remainingPercent: min(100, max(0, 100 - used.doubleValue)),
            durationMinutes: duration,
            resetsAt: timestamp.map { Date(timeIntervalSince1970: $0) }
        )
    }

    private static func mapAccountTokenUsage(_ result: [String: Any]) -> TokenUsageSummary? {
        guard let summary = result["summary"] as? [String: Any],
              let lifetime = (summary["lifetimeTokens"] as? NSNumber)?.int64Value else {
            return nil
        }
        let formatter = DateFormatter()
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = .autoupdatingCurrent
        formatter.dateFormat = "yyyy-MM-dd"
        let todayKey = formatter.string(from: Date())
        let monthKey = String(todayKey.prefix(7))
        let buckets = result["dailyUsageBuckets"] as? [[String: Any]] ?? []
        var todayTokens: Int64 = 0
        var monthTokens: Int64 = 0
        for bucket in buckets {
            guard let date = bucket["startDate"] as? String,
                  let tokens = (bucket["tokens"] as? NSNumber)?.int64Value else { continue }
            if date == todayKey { todayTokens = tokens }
            if date.hasPrefix(monthKey) { monthTokens = addingWithoutOverflow(monthTokens, tokens) }
        }
        return TokenUsageSummary(
            todayTokens: max(0, todayTokens),
            monthTokens: max(0, monthTokens),
            totalTokens: max(0, lifetime)
        )
    }

    private static func addingWithoutOverflow(_ left: Int64, _ right: Int64) -> Int64 {
        let (value, overflow) = left.addingReportingOverflow(right)
        return overflow ? Int64.max : value
    }

    private static func windowMatching(
        minutes: Int,
        first: QuotaWindowValue?,
        second: QuotaWindowValue?
    ) -> QuotaWindowValue? {
        if first?.durationMinutes == minutes { return first }
        if second?.durationMinutes == minutes { return second }
        if first?.durationMinutes != nil || second?.durationMinutes != nil { return nil }
        return minutes == 300 ? first : second
    }
}

struct ParsedCodexConfig {
    var modelProvider: String?
    var baseURL: String?
    var displayName: String?
    var environmentKey: String?
    var credential: String?
    var requiresOpenAIAuth = false
}

enum CodexConfigReader {
    static func read() -> ParsedCodexConfig? {
        let environment = ProcessInfo.processInfo.environment
        let configuredRoot = environment["CODEX_HOME"]
        let root = configuredRoot.flatMap { FileManager.default.fileExists(atPath: $0) ? $0 : nil }
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".codex").path
        let url = URL(fileURLWithPath: root).appendingPathComponent("config.toml")
        guard let text = try? String(contentsOf: url, encoding: .utf8) else { return nil }

        var rootValues: [String: String] = [:]
        var providers: [String: [String: String]] = [:]
        var currentProvider: String?

        for rawLine in text.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = stripComment(String(rawLine)).trimmingCharacters(in: .whitespaces)
            if line.isEmpty { continue }
            if line.hasPrefix("[") && line.hasSuffix("]") {
                let table = String(line.dropFirst().dropLast()).trimmingCharacters(in: .whitespaces)
                currentProvider = parseProviderTable(table)
                continue
            }
            guard let separator = assignmentIndex(in: line) else { continue }
            let key = String(line[..<separator]).trimmingCharacters(in: .whitespaces)
            let valueText = String(line[line.index(after: separator)...]).trimmingCharacters(in: .whitespaces)
            guard let value = parseString(valueText) ?? (valueText == "true" || valueText == "false" ? valueText : nil) else {
                continue
            }
            if let currentProvider {
                providers[currentProvider, default: [:]][key] = value
            } else {
                rootValues[key] = value
            }
        }

        let provider = rootValues["model_provider"]
        let section = provider.flatMap { providers[$0] }
        return ParsedCodexConfig(
            modelProvider: provider,
            baseURL: section?["base_url"] ?? rootValues["base_url"],
            displayName: section?["name"],
            environmentKey: section?["env_key"],
            credential: section?["experimental_bearer_token"] ?? rootValues["experimental_bearer_token"],
            requiresOpenAIAuth: section?["requires_openai_auth"] == "true"
        )
    }

    private static func stripComment(_ line: String) -> String {
        var quote: Character?
        var escaped = false
        for index in line.indices {
            let character = line[index]
            if character == "\\" && quote == "\"" && !escaped {
                escaped = true
                continue
            }
            if (character == "\"" || character == "'") && !escaped {
                quote = quote == nil ? character : (quote == character ? nil : quote)
            }
            if character == "#" && quote == nil {
                return String(line[..<index])
            }
            escaped = false
        }
        return line
    }

    private static func assignmentIndex(in line: String) -> String.Index? {
        var quote: Character?
        var escaped = false
        for index in line.indices {
            let character = line[index]
            if character == "\\" && quote == "\"" && !escaped {
                escaped = true
                continue
            }
            if (character == "\"" || character == "'") && !escaped {
                quote = quote == nil ? character : (quote == character ? nil : quote)
            }
            if character == "=" && quote == nil { return index }
            escaped = false
        }
        return nil
    }

    private static func parseProviderTable(_ table: String) -> String? {
        let prefix = "model_providers."
        guard table.hasPrefix(prefix) else { return nil }
        let value = String(table.dropFirst(prefix.count)).trimmingCharacters(in: .whitespaces)
        return parseString(value) ?? value
    }

    private static func parseString(_ value: String) -> String? {
        guard value.count >= 2,
              let first = value.first,
              let last = value.last,
              (first == "\"" || first == "'"),
              last == first else { return nil }
        let inner = String(value.dropFirst().dropLast())
        if first == "'" { return inner }
        return inner
            .replacingOccurrences(of: "\\\"", with: "\"")
            .replacingOccurrences(of: "\\\\", with: "\\")
    }
}

final class NoRedirectDelegate: NSObject, URLSessionTaskDelegate {
    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest,
        completionHandler: @escaping (URLRequest?) -> Void
    ) {
        completionHandler(nil)
    }
}

enum BalanceProvider {
    private static let delegate = NoRedirectDelegate()

    static func read(config: ParsedCodexConfig) throws -> OrbState? {
        guard let provider = config.modelProvider,
              provider.caseInsensitiveCompare("openai") != .orderedSame else { return nil }
        if provider.caseInsensitiveCompare("custom") == .orderedSame,
           config.displayName?.caseInsensitiveCompare("OpenAI") == .orderedSame,
           config.requiresOpenAIAuth,
           config.baseURL == nil {
            return nil
        }
        guard let rawBase = config.baseURL,
              let base = URL(string: rawBase),
              base.scheme?.lowercased() == "https",
              let host = base.host?.lowercased() else {
            throw ProviderError.message("当前第三方供应商缺少安全的 HTTPS Base URL。")
        }
        let credential = config.credential
            ?? config.environmentKey.flatMap { ProcessInfo.processInfo.environment[$0] }
        guard let credential, !credential.isEmpty else {
            throw ProviderError.message("当前第三方供应商缺少余额查询凭据。")
        }

        if host == "api.deepseek.com" {
            let endpoint = URL(string: "/user/balance", relativeTo: base)!.absoluteURL
            let object = try getJSON(providerBase: base, endpoint: endpoint, credential: credential)
            guard let infos = object["balance_infos"] as? [[String: Any]], !infos.isEmpty else {
                throw ProviderError.message("DeepSeek 返回了无效的余额数据。")
            }
            let values = infos.compactMap { info -> String? in
                guard let currency = info["currency"] as? String,
                      let amount = number(info["total_balance"]) else { return nil }
                return String(format: "%.2f %@", amount, currency.uppercased())
            }
            guard values.count == infos.count else {
                throw ProviderError.message("DeepSeek 返回了无效的余额数据。")
            }
            return OrbState(
                sourceName: "DeepSeek",
                agentName: "Codex",
                balanceText: values.joined(separator: " / "),
                balanceCaption: "账户余额",
                risk: .safe,
                updatedAt: Date()
            )
        }

        if host == "openrouter.ai" {
            let endpoint = URL(string: "https://openrouter.ai/api/v1/key")!
            let object = try getJSON(providerBase: base, endpoint: endpoint, credential: credential)
            guard let data = object["data"] as? [String: Any] else {
                throw ProviderError.message("OpenRouter 返回了无效的额度数据。")
            }
            let text: String
            if data["limit_remaining"] is NSNull {
                text = "无限额"
            } else if let remaining = number(data["limit_remaining"]) {
                text = String(format: "%.2f USD", remaining)
            } else {
                throw ProviderError.message("OpenRouter 返回了无效的额度数据。")
            }
            return OrbState(
                sourceName: "OpenRouter",
                agentName: "Codex",
                balanceText: text,
                balanceCaption: "密钥剩余额度",
                risk: .safe,
                updatedAt: Date()
            )
        }

        throw ProviderError.message("当前供应商暂不支持安全余额查询。")
    }

    private static func getJSON(
        providerBase: URL,
        endpoint: URL,
        credential: String
    ) throws -> [String: Any] {
        guard providerBase.host?.caseInsensitiveCompare(endpoint.host ?? "") == .orderedSame else {
            throw ProviderError.message("余额请求不能跨供应商主机。")
        }
        var request = URLRequest(url: endpoint)
        request.httpMethod = "GET"
        request.timeoutInterval = 10
        request.setValue("Bearer \(credential)", forHTTPHeaderField: "Authorization")

        let configuration = URLSessionConfiguration.ephemeral
        configuration.httpCookieAcceptPolicy = .never
        configuration.timeoutIntervalForRequest = 10
        let session = URLSession(configuration: configuration, delegate: delegate, delegateQueue: nil)
        let semaphore = DispatchSemaphore(value: 0)
        var responseData: Data?
        var responseValue: URLResponse?
        var responseError: Error?
        let task = session.dataTask(with: request) { data, response, error in
            responseData = data
            responseValue = response
            responseError = error
            semaphore.signal()
        }
        task.resume()
        guard semaphore.wait(timeout: .now() + 11) == .success else {
            task.cancel()
            throw ProviderError.message("余额请求超时。")
        }
        if responseError != nil {
            throw ProviderError.message("余额请求失败。")
        }
        guard let http = responseValue as? HTTPURLResponse,
              (200..<300).contains(http.statusCode),
              let data = responseData,
              data.count <= 256 * 1024,
              let object = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw ProviderError.message("余额服务返回了无效响应。")
        }
        return object
    }

    private static func number(_ raw: Any?) -> Double? {
        if let number = raw as? NSNumber { return number.doubleValue }
        if let text = raw as? String { return Double(text) }
        return nil
    }
}

enum ClaudeQuotaProvider {
    static var cacheURL: URL {
        SettingsStore.shared.supportDirectory.appendingPathComponent("claude-status.json")
    }

    static func read() throws -> OrbState {
        guard let data = try? Data(contentsOf: cacheURL), data.count <= 64 * 1024,
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw ProviderError.message("尚未同步 Claude Code 额度；请先安装桥接并在 Claude Code 中发送一条消息。")
        }
        let five = mapWindow(root["fiveHour"], duration: 300)
        let weekly = mapWindow(root["sevenDay"], duration: 10_080)
        guard five != nil || weekly != nil else {
            throw ProviderError.message("Claude Code 额度缓存已过期，请发送一条消息刷新。")
        }
        let capturedAt = (root["capturedAt"] as? String).flatMap {
            ISO8601DateFormatter().date(from: $0)
        }
        let percent = weekly?.remainingPercent ?? five!.remainingPercent
        return OrbState(
            sourceName: "Claude Code 官方",
            agentName: "Claude Code",
            fiveHour: five,
            weekly: weekly,
            risk: risk(for: percent),
            updatedAt: capturedAt
        )
    }

    static func installBridge() throws -> String {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let directory = home.appendingPathComponent(".claude", isDirectory: true)
        let settingsURL = directory.appendingPathComponent("settings.json")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)

        var root: [String: Any] = [:]
        if let data = try? Data(contentsOf: settingsURL), !data.isEmpty {
            guard let parsed = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                throw ProviderError.message("Claude settings.json 不是有效的 JSON 对象。")
            }
            root = parsed
        }
        let executable = Bundle.main.executablePath ?? CommandLine.arguments[0]
        let escaped = executable.replacingOccurrences(of: "'", with: "'\\''")
        let command = "'\(escaped)' --claude-statusline"

        if let existing = root["statusLine"] as? [String: Any] {
            if existing["command"] as? String == command { return "Claude Code 桥接已安装。" }
            throw ProviderError.message("Claude 已配置其他 statusLine，Balance Capsule 没有覆盖它。")
        }
        root["statusLine"] = [
            "type": "command",
            "command": command,
            "refreshInterval": 60
        ]
        let data = try JSONSerialization.data(withJSONObject: root, options: [.prettyPrinted, .sortedKeys])
        let temporary = settingsURL.appendingPathExtension("balance-capsule.tmp")
        let backup = settingsURL.appendingPathExtension("balance-capsule.backup")
        if FileManager.default.fileExists(atPath: settingsURL.path) {
            try? FileManager.default.copyItem(at: settingsURL, to: backup)
        }
        try data.write(to: temporary, options: .atomic)
        if FileManager.default.fileExists(atPath: settingsURL.path) {
            _ = try FileManager.default.replaceItemAt(settingsURL, withItemAt: temporary)
        } else {
            try FileManager.default.moveItem(at: temporary, to: settingsURL)
        }
        return "Claude Code 桥接已安装；发送一条消息后额度会同步。"
    }

    static func runStatusLineBridge() -> Int32 {
        do {
            let input = FileHandle.standardInput.readDataToEndOfFile()
            guard let root = try JSONSerialization.jsonObject(with: input) as? [String: Any],
                  let limits = root["rate_limits"] as? [String: Any] else {
                throw ProviderError.message("输入中没有 rate_limits。")
            }
            let five = bridgeWindow(limits["five_hour"])
            let weekly = bridgeWindow(limits["seven_day"])
            var cache: [String: Any] = ["capturedAt": ISO8601DateFormatter().string(from: Date())]
            cache["fiveHour"] = five ?? NSNull()
            cache["sevenDay"] = weekly ?? NSNull()
            try FileManager.default.createDirectory(
                at: SettingsStore.shared.supportDirectory,
                withIntermediateDirectories: true
            )
            let data = try JSONSerialization.data(withJSONObject: cache, options: [.prettyPrinted, .sortedKeys])
            try data.write(to: cacheURL, options: .atomic)

            var labels: [String] = []
            if let used = (five?["usedPercentage"] as? NSNumber)?.doubleValue {
                labels.append("5h \(Int((100 - used).rounded()))%")
            }
            if let used = (weekly?["usedPercentage"] as? NSNumber)?.doubleValue {
                labels.append("7d \(Int((100 - used).rounded()))%")
            }
            writeOutput(labels.isEmpty ? "Claude 用量待同步" : "Claude " + labels.joined(separator: " · "))
            return 0
        } catch {
            writeOutput("Claude 用量同步失败")
            return 1
        }
    }

    private static func mapWindow(_ raw: Any?, duration: Int) -> QuotaWindowValue? {
        guard let object = raw as? [String: Any],
              let used = object["usedPercentage"] as? NSNumber,
              let text = object["resetsAt"] as? String,
              let reset = ISO8601DateFormatter().date(from: text),
              reset > Date() else { return nil }
        return QuotaWindowValue(
            remainingPercent: min(100, max(0, 100 - used.doubleValue)),
            durationMinutes: duration,
            resetsAt: reset
        )
    }

    private static func bridgeWindow(_ raw: Any?) -> [String: Any]? {
        guard let object = raw as? [String: Any],
              let used = object["used_percentage"] as? NSNumber,
              let reset = object["resets_at"] as? NSNumber else { return nil }
        return [
            "usedPercentage": used.doubleValue,
            "resetsAt": ISO8601DateFormatter().string(from: Date(timeIntervalSince1970: reset.doubleValue))
        ]
    }

    private static func writeOutput(_ text: String) {
        if let data = (text + "\n").data(using: .utf8) {
            try? FileHandle.standardOutput.write(contentsOf: data)
        }
    }
}

enum ProviderCoordinator {
    static func read(source: AgentSource) throws -> OrbState {
        switch source {
        case .claudeCode:
            return try ClaudeQuotaProvider.read()
        case .codex:
            if let config = CodexConfigReader.read(),
               let balance = try BalanceProvider.read(config: config) {
                return balance
            } else {
                return try readCodexWithRetry()
            }
        }
    }

    private static func readCodexWithRetry() throws -> OrbState {
        do {
            return try CodexRPCProvider.read()
        } catch {
            guard error.localizedDescription.contains("超时") else { throw error }
            return try CodexRPCProvider.read()
        }
    }
}
