namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCClusterInfo
{
    public required string Uuid { get; init; }

    public string? Name { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public string? SecurityLevel { get; init; }

    public string? LoadBalancingMode { get; init; }

    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string NameText => string.IsNullOrWhiteSpace(Name) ? "Кластер 1С" : Name;

    public string UuidText => string.IsNullOrWhiteSpace(Uuid) ? "—" : Uuid;

    public string HostText => string.IsNullOrWhiteSpace(Host) ? "—" : Host;

    public string PortText => Port?.ToString() ?? "—";

    public string AddressText => Host is null || Port is null ? "—" : $"{Host}:{Port}";

    public string SecurityLevelText => string.IsNullOrWhiteSpace(SecurityLevel) ? "—" : SecurityLevel;

    public string LoadBalancingModeText => string.IsNullOrWhiteSpace(LoadBalancingMode) ? "—" : LoadBalancingMode;

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
}
