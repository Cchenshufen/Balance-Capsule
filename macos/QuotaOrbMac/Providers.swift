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
        process.arguments = ["-s", "read-only", "-a", "never", "app-server"]
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
        // A window is shown only when the official Codex response identifies its
        // duration. Do not infer a 5-hour allowance from an unlabeled window:
        // Codex can omit that limit when it is not available for the account.
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
            return try readCodexWithRetry()
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
