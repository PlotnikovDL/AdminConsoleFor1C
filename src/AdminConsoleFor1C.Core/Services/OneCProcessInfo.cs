namespace AdminConsoleFor1C.Core.Services;

public sealed record OneCProcessInfo
{
    public required string Name { get; init; }

    public required OneCProcessKind Kind { get; init; }

    public required uint ProcessId { get; init; }

    public uint? ParentProcessId { get; init; }

    public string? ExecutablePath { get; init; }

    public string? Arguments { get; init; }

    public string? Version { get; init; }

    public string? Owner { get; init; }

    public int? AgentPort { get; init; }

    public int? ClusterPort { get; init; }

    public int? WorkerPort { get; init; }

    public int? AdministrationServerPort { get; init; }

    public int? DebugServerPort { get; init; }

    public string? PortRange { get; init; }

    public string? DataDirectory { get; init; }

    public string? RawCommandLine { get; init; }

    public string? RelatedServiceDisplayName { get; init; }

    public string KindDisplayName => Kind switch
    {
        OneCProcessKind.ServerAgent => "Агент сервера",
        OneCProcessKind.ClusterManager => "Менеджер кластера",
        OneCProcessKind.WorkerProcess => "Рабочий процесс",
        OneCProcessKind.AdministrationServer => "Сервер администрирования",
        OneCProcessKind.DebugServer => "Сервер отладки",
        _ => "Процесс 1С"
    };

    public string ProcessIdText => ProcessId.ToString();

    public string ParentProcessIdText => ParentProcessId is null or 0 ? "—" : ParentProcessId.Value.ToString();

    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "—" : Version;

    public string OwnerText => string.IsNullOrWhiteSpace(Owner) ? "—" : Owner;

    public string RelatedServiceText => string.IsNullOrWhiteSpace(RelatedServiceDisplayName)
        ? "Не сопоставлен со службой"
        : $"Служба: {RelatedServiceDisplayName}";

    public string RoleText => Kind switch
    {
        OneCProcessKind.ServerAgent => AgentPort is null ? "Агент сервера" : $"Агент: {AgentPort}",
        OneCProcessKind.ClusterManager => ClusterPort is null ? "Менеджер кластера" : $"Кластер: {ClusterPort}",
        OneCProcessKind.WorkerProcess => WorkerPort is not null
            ? $"Рабочий: {WorkerPort}"
            : string.IsNullOrWhiteSpace(PortRange) ? "Рабочий процесс" : $"Диапазон: {PortRange}",
        OneCProcessKind.AdministrationServer => AdministrationServerPort is null ? "Сервер администрирования" : $"RAS: {AdministrationServerPort}",
        OneCProcessKind.DebugServer => DebugServerPort is null ? "Сервер отладки" : $"Отладка HTTP: {DebugServerPort}",
        _ => PortsText
    };

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

            if (ClusterPort is not null)
            {
                ports.Add($"Кластер: {ClusterPort}");
            }

            if (WorkerPort is not null)
            {
                ports.Add($"Рабочий: {WorkerPort}");
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
                ports.Add($"Диапазон: {PortRange}");
            }

            return ports.Count == 0 ? "—" : string.Join(Environment.NewLine, ports);
        }
    }
}
