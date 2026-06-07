namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCClusterInfo
{
    public required string Uuid { get; init; }

    public string? Name { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public string? SecurityLevel { get; init; }

    public string? LoadBalancingMode { get; init; }

    public IReadOnlyList<OneCClusterServerInfo> Servers { get; init; } = [];

    public IReadOnlyList<OneCInfobaseSummaryInfo> Infobases { get; init; } = [];

    public IReadOnlyList<OneCOccupiedLicenseInfo> ProcessLicenses { get; init; } = [];

    public IReadOnlyList<OneCOccupiedLicenseInfo> SessionLicenses { get; init; } = [];

    public IReadOnlyList<OneCSessionInfo> Sessions { get; init; } = [];

    public IReadOnlyList<OneCLicenseUsageInfo> OccupiedLicenseUsages =>
        OneCLicenseUsageInfo.Create(ProcessLicenses, SessionLicenses);

    public string? DetailsMessage { get; init; }

    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string NameText => string.IsNullOrWhiteSpace(Name) ? "Кластер 1С" : Name;

    public string UuidText => string.IsNullOrWhiteSpace(Uuid) ? "—" : Uuid;

    public string HostText => string.IsNullOrWhiteSpace(Host) ? "—" : Host;

    public string PortText => Port?.ToString() ?? "—";

    public string AddressText => Host is null || Port is null ? "—" : $"{Host}:{Port}";

    public string SecurityLevelText => string.IsNullOrWhiteSpace(SecurityLevel) ? "—" : SecurityLevel;

    public string LoadBalancingModeText => string.IsNullOrWhiteSpace(LoadBalancingMode) ? "—" : LoadBalancingMode;

    public string ServersSummaryText => Servers.Count switch
    {
        0 => "Рабочие серверы не найдены",
        1 => "1 рабочий сервер",
        >= 2 and <= 4 => $"{Servers.Count} рабочих сервера",
        _ => $"{Servers.Count} рабочих серверов"
    };

    public string InfobasesSummaryText => Infobases.Count switch
    {
        0 => "Информационные базы не найдены",
        1 => "1 информационная база",
        >= 2 and <= 4 => $"{Infobases.Count} информационные базы",
        _ => $"{Infobases.Count} информационных баз"
    };

    public int OccupiedLicenseCount => ProcessLicenses.Count + SessionLicenses.Count;

    public string OccupiedLicensesSummaryText
    {
        get
        {
            var usages = OccupiedLicenseUsages;
            if (usages.Count == 0)
            {
                return "Занятые лицензии не обнаружены";
            }

            var parts = new List<string>();
            var clientUsages = usages
                .Where(static usage => usage.OwnerKind == OneCLicenseOwnerKind.Session)
                .ToList();
            var serverUsages = usages
                .Where(static usage => usage.OwnerKind == OneCLicenseOwnerKind.Process)
                .ToList();

            if (clientUsages.Count > 0)
            {
                parts.Add($"Клиентские места: {FormatUsage(clientUsages)}, сеансов: {SessionLicenses.Count}");
            }

            if (serverUsages.Count > 0)
            {
                parts.Add($"Серверные лицензии: {FormatUsage(serverUsages)}");
            }

            return string.Join("; ", parts);
        }
    }

    public static OneCClusterInfo FromProperties(IReadOnlyDictionary<string, string> properties)
    {
        properties.TryGetValue("cluster", out var uuid);
        properties.TryGetValue("name", out var name);
        properties.TryGetValue("host", out var host);
        properties.TryGetValue("port", out var portText);
        properties.TryGetValue("security-level", out var securityLevel);
        properties.TryGetValue("load-balancing-mode", out var loadBalancingMode);

        return new OneCClusterInfo
        {
            Uuid = NormalizeValue(uuid) ?? string.Empty,
            Name = NormalizeValue(name),
            Host = NormalizeValue(host),
            Port = int.TryParse(NormalizeValue(portText), out var port) ? port : null,
            SecurityLevel = NormalizeValue(securityLevel),
            LoadBalancingMode = NormalizeValue(loadBalancingMode),
            Properties = properties
        };
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"')
            ? trimmed[1..^1]
            : trimmed;
    }

    private static string FormatUsage(IReadOnlyList<OneCLicenseUsageInfo> usages)
    {
        var occupied = usages.Sum(static usage => usage.OccupiedSeats);
        var capacity = usages.Any(static usage => usage.Capacity is null)
            ? null
            : usages.Sum(static usage => usage.Capacity);

        return capacity is null ? occupied.ToString() : $"{occupied}/{capacity}";
    }
}
