namespace AdminConsoleFor1C.Core.Services;

public sealed record OneCServiceInfo
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required OneCServiceKind Kind { get; init; }

    public required string State { get; init; }

    public required string Status { get; init; }

    public string? StartMode { get; init; }

    public string? Account { get; init; }

    public uint? ProcessId { get; init; }

    public string? ExecutablePath { get; init; }

    public string? Arguments { get; init; }

    public string? Version { get; init; }

    public int? AgentPort { get; init; }

    public int? RegPort { get; init; }

    public string? PortRange { get; init; }

    public string? DataDirectory { get; init; }

    public string? RawCommandLine { get; init; }

    public string KindDisplayName => Kind switch
    {
        OneCServiceKind.ServerAgent => "Агент сервера",
        OneCServiceKind.AdministrationServer => "Сервер администрирования",
        OneCServiceKind.DebugServer => "Сервер отладки",
        _ => "Служба 1С"
    };

    public string ProcessIdText => ProcessId is null or 0 ? "—" : ProcessId.Value.ToString();

    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "—" : Version;

    public string AgentPortText => AgentPort?.ToString() ?? "—";

    public string RegPortText => RegPort?.ToString() ?? "—";

    public string PortRangeText => string.IsNullOrWhiteSpace(PortRange) ? "—" : PortRange;

    public string DataDirectoryText => string.IsNullOrWhiteSpace(DataDirectory) ? "—" : DataDirectory;

    public string AccountText => string.IsNullOrWhiteSpace(Account) ? "—" : Account;

    public string StartModeText => string.IsNullOrWhiteSpace(StartMode) ? "—" : StartMode;

    public string ExecutablePathText => string.IsNullOrWhiteSpace(ExecutablePath) ? "—" : ExecutablePath;
}
