namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCSessionInfo
{
    public required string Uuid { get; init; }

    public string? InfobaseUuid { get; init; }

    public string? UserName { get; init; }

    public string? Host { get; init; }

    public string? Application { get; init; }

    public string? StartedAt { get; init; }

    public string? LastActiveAt { get; init; }

    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string UserText => string.IsNullOrWhiteSpace(UserName) ? "Пользователь" : UserName;

    public string ContextText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Host))
            {
                parts.Add(Host);
            }

            if (!string.IsNullOrWhiteSpace(Application))
            {
                parts.Add(Application);
            }

            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }
    }

    public static OneCSessionInfo FromProperties(IReadOnlyDictionary<string, string> properties)
    {
        properties.TryGetValue("session", out var uuid);
        properties.TryGetValue("session-id", out var sessionId);
        properties.TryGetValue("infobase", out var infobaseUuid);
        properties.TryGetValue("user-name", out var userName);
        properties.TryGetValue("host", out var host);
        properties.TryGetValue("app-id", out var application);
        properties.TryGetValue("started-at", out var startedAt);
        properties.TryGetValue("last-active-at", out var lastActiveAt);

        return new OneCSessionInfo
        {
            Uuid = NormalizeValue(uuid) ?? NormalizeValue(sessionId) ?? string.Empty,
            InfobaseUuid = NormalizeValue(infobaseUuid),
            UserName = NormalizeValue(userName),
            Host = NormalizeValue(host),
            Application = NormalizeValue(application),
            StartedAt = NormalizeValue(startedAt),
            LastActiveAt = NormalizeValue(lastActiveAt),
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
