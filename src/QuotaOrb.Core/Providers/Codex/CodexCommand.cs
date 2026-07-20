namespace QuotaOrb.Core.Providers.Codex;

public sealed record CodexCommand(
    string FileName,
    IReadOnlyList<string> PrefixArguments);
