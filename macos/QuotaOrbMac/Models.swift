import AppKit
import Foundation

enum AgentSource: String, Codable {
    case codex
    case claudeCode

    var displayName: String {
        switch self {
        case .codex: return "Codex"
        case .claudeCode: return "Claude Code"
        }
    }
}

enum QuotaWindowMode: String, Codable {
    case fiveHour
    case weekly
}

enum QuotaRisk {
    case loading
    case safe
    case warning
    case critical
    case error

    var color: NSColor {
        switch self {
        case .loading, .safe: return NSColor(calibratedRed: 0.55, green: 0.78, blue: 0.95, alpha: 1)
        case .warning: return NSColor(calibratedRed: 0.96, green: 0.79, blue: 0.54, alpha: 1)
        case .critical: return NSColor(calibratedRed: 0.96, green: 0.57, blue: 0.52, alpha: 1)
        case .error: return NSColor(calibratedRed: 0.95, green: 0.48, blue: 0.53, alpha: 1)
        }
    }

    var statusText: String {
        switch self {
        case .loading: return "加载中"
        case .safe: return "状态良好"
        case .warning: return "需要留意"
        case .critical: return "即将耗尽"
        case .error: return "读取失败"
        }
    }
}

struct QuotaWindowValue {
    let remainingPercent: Double
    let durationMinutes: Int?
    let resetsAt: Date?
}

struct TokenUsageSummary {
    let todayTokens: Int64
    let monthTokens: Int64
    let totalTokens: Int64
}

struct OrbState {
    var sourceName = "Codex 官方"
    var agentName = "Codex"
    var fiveHour: QuotaWindowValue?
    var weekly: QuotaWindowValue?
    var balanceText: String?
    var balanceCaption: String?
    var tokenUsage: TokenUsageSummary?
    var risk: QuotaRisk = .loading
    var message: String?
    var updatedAt: Date?

    func selectedPercent(mode: QuotaWindowMode) -> Double? {
        if mode == .weekly, let weekly {
            return weekly.remainingPercent
        }
        return fiveHour?.remainingPercent ?? weekly?.remainingPercent
    }

    func displayText(mode: QuotaWindowMode) -> String {
        if let balanceText {
            return balanceText
        }
        guard let percent = selectedPercent(mode: mode) else {
            return risk == .loading ? "…" : "!"
        }
        return String(Int(percent.rounded()))
    }

    func caption(mode: QuotaWindowMode) -> String {
        if let balanceCaption {
            return balanceCaption
        }
        if risk == .error {
            return "读取失败"
        }
        if mode == .weekly, weekly != nil {
            return "一周剩余"
        }
        return fiveHour == nil && weekly != nil ? "一周剩余" : "5h 剩余"
    }
}

struct AppSettings: Codable {
    var orbX: Double?
    var orbY: Double?
    var selectedAgent: AgentSource = .codex
    var quotaWindow: QuotaWindowMode = .weekly
    var animationsEnabled = true
    var startAtLogin = false
}

final class SettingsStore {
    static let shared = SettingsStore()

    let supportDirectory: URL
    private let settingsURL: URL

    private init() {
        let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        supportDirectory = root.appendingPathComponent("BalanceCapsule", isDirectory: true)
        settingsURL = supportDirectory.appendingPathComponent("settings.json")
    }

    func load() -> AppSettings {
        guard let data = try? Data(contentsOf: settingsURL),
              let value = try? JSONDecoder().decode(AppSettings.self, from: data) else {
            return AppSettings()
        }
        return value
    }

    func save(_ settings: AppSettings) {
        do {
            try FileManager.default.createDirectory(
                at: supportDirectory,
                withIntermediateDirectories: true
            )
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            let data = try encoder.encode(settings)
            let temporary = supportDirectory.appendingPathComponent("settings.tmp")
            try data.write(to: temporary, options: .atomic)
            if FileManager.default.fileExists(atPath: settingsURL.path) {
                _ = try FileManager.default.replaceItemAt(settingsURL, withItemAt: temporary)
            } else {
                try FileManager.default.moveItem(at: temporary, to: settingsURL)
            }
        } catch {
            NSLog("Balance Capsule: failed to save settings: %@", error.localizedDescription)
        }
    }
}

func risk(for remainingPercent: Double) -> QuotaRisk {
    if remainingPercent < 20 { return .critical }
    if remainingPercent <= 40 { return .warning }
    return .safe
}

func formatWindow(_ value: QuotaWindowValue?) -> String {
    guard let value else { return "暂不可用" }
    return "\(Int(value.remainingPercent.rounded()))% 剩余"
}

func formatReset(_ value: QuotaWindowValue?) -> String {
    guard let date = value?.resetsAt else { return "暂不可用" }
    let formatter = DateFormatter()
    formatter.locale = Locale(identifier: "zh_CN")
    formatter.dateFormat = Calendar.current.isDateInToday(date) ? "HH:mm" : "M月d日 HH:mm"
    return formatter.string(from: date)
}
