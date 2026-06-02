namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCClusterServerInfo
{
    public required string Uuid { get; init; }

    public string? Name { get; init; }

    public string? AgentHost { get; init; }

    public int? AgentPort { get; init; }

    public int? ClusterPort { get; init; }

    public string? PortRange { get; init; }

    public string? Using { get; init; }

    public string? InfobasesLimit { get; init; }

    public string? ConnectionsLimit { get; init; }

    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string NameText => string.IsNullOrWhiteSpace(Name) ? "Рабочий сервер" : Name;

    public string UuidText => string.IsNullOrWhiteSpace(Uuid) ? "—" : Uuid;

    public string AgentAddressText => AgentHost is null || AgentPort is null ? "—" : $"{AgentHost}:{AgentPort}";

    public string UsageText => Using switch
    {
        "main" => "Центральный сервер",
        "normal" => "Обычный сервер",
        null or "" => "—",
        _ => Using
    };

    public string PortsText
    {
        get
        {
            var ports = new List<string>();
            if (ClusterPort is not null)
            {
                ports.Add($"Кластер: {ClusterPort}");
            }

            if (!string.IsNullOrWhiteSpace(PortRange))
            {
                ports.Add($"Процессы: {PortRange}");
            }

            return ports.Count == 0 ? "—" : string.Join(Environment.NewLine, ports);
        }
    }

    public string LimitsText
    {
        get
        {
            var limits = new List<string>();
            if (!string.IsNullOrWhiteSpace(InfobasesLimit))
            {
                limits.Add($"ИБ на процесс: {InfobasesLimit}");
            }

            if (!string.IsNullOrWhiteSpace(ConnectionsLimit))
            {
                limits.Add($"Соединений: {ConnectionsLimit}");
            }

            return limits.Count == 0 ? "—" : string.Join(Environment.NewLine, limits);
        }
    }

    public static OneCClusterServerInfo FromProperties(IReadOnlyDictionary<string, string> properties)
    {
        properties.TryGetValue("server", out var uuid);
        properties.TryGetValue("name", out var name);
        properties.TryGetValue("agent-host", out var agentHost);
        properties.TryGetValue("agent-port", out var agentPortText);
        properties.TryGetValue("cluster-port", out var clusterPortText);
        properties.TryGetValue("port-range", out var portRange);
        properties.TryGetValue("using", out var usingMode);
        properties.TryGetValue("infobases-limit", out var infobasesLimit);
        properties.TryGetValue("connections-limit", out var connectionsLimit);

        return new OneCClusterServerInfo
        {
            Uuid = NormalizeValue(uuid) ?? string.Empty,
            Name = NormalizeValue(name),
            AgentHost = NormalizeValue(agentHost),
            AgentPort = TryParseInt32(agentPortText),
            ClusterPort = TryParseInt32(clusterPortText),
            PortRange = NormalizeValue(portRange),
            Using = NormalizeValue(usingMode),
            InfobasesLimit = NormalizeValue(infobasesLimit),
            ConnectionsLimit = NormalizeValue(connectionsLimit),
            Properties = properties
        };
    }

    private static int? TryParseInt32(string? value)
    {
        return int.TryParse(NormalizeValue(value), out var parsed) ? parsed : null;
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
