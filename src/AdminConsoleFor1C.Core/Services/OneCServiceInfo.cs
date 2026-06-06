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

    public int? AdministrationServerPort { get; init; }

    public int? DebugServerPort { get; init; }

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

    public string DisplayNameText => string.IsNullOrWhiteSpace(DisplayName)
        ? "—"
        : DisplayName.Trim();

    public string WindowsServicesDisplayNameText => $"Службы Windows: {DisplayNameText}";

    public string TaskManagerServiceNameText => $"Диспетчер задач: {Name}";

    public string ProcessIdText => ProcessId is null or 0 ? "—" : ProcessId.Value.ToString();

    public string StateDisplayName => State switch
    {
        "Running" => "Работает",
        "Stopped" => IsServerComponent ? "Остановлен" : "Остановлена",
        "Paused" => IsServerComponent ? "Приостановлен" : "Приостановлена",
        "Start Pending" => "Запускается",
        "Stop Pending" => "Останавливается",
        "Continue Pending" => "Возобновляется",
        "Pause Pending" => "Приостанавливается",
        _ => string.IsNullOrWhiteSpace(State) ? "—" : State
    };

    private bool IsServerComponent => Kind is
        OneCServiceKind.ServerAgent
        or OneCServiceKind.AdministrationServer
        or OneCServiceKind.DebugServer;

    public string StartModeDisplayName => StartMode switch
    {
        "Auto" => "Автоматически",
        "Manual" => "Вручную",
        "Disabled" => "Отключена",
        "Boot" => "При загрузке",
        "System" => "Системная",
        _ => string.IsNullOrWhiteSpace(StartMode) ? "—" : StartMode
    };

    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "—" : Version;

    public string AgentPortText => AgentPort?.ToString() ?? "—";

    public string RegPortText => RegPort?.ToString() ?? "—";

    public string AdministrationServerPortText => AdministrationServerPort?.ToString() ?? "—";

    public string DebugServerPortText => DebugServerPort?.ToString() ?? "—";

    public string PortRangeText => string.IsNullOrWhiteSpace(PortRange) ? "—" : PortRange;

    public string DataDirectoryText => string.IsNullOrWhiteSpace(DataDirectory) ? "—" : DataDirectory;

    public string AccountText => string.IsNullOrWhiteSpace(Account) ? "—" : Account;

    public string ArgumentsText => string.IsNullOrWhiteSpace(Arguments) ? "—" : Arguments;

    public string ExecutablePathText => string.IsNullOrWhiteSpace(ExecutablePath) ? "—" : ExecutablePath;

    public string PortsText
    {
        get
        {
            var ports = new List<string>();
            if (AgentPort is not null)
            {
                ports.Add($"Агент: {AgentPort}");
            }

            if (RegPort is not null)
            {
                ports.Add($"Кластер: {RegPort}");
            }

            if (AdministrationServerPort is not null)
            {
                ports.Add($"RAS: {AdministrationServerPort}");
            }

            if (DebugServerPort is not null)
            {
                ports.Add($"Отладка HTTP: {DebugServerPort}");
            }

            if (!string.IsNullOrWhiteSpace(PortRange))
            {
                ports.Add($"Процессы: {PortRange}");
            }

            return ports.Count == 0 ? "—" : string.Join(Environment.NewLine, ports);
        }
    }
}
