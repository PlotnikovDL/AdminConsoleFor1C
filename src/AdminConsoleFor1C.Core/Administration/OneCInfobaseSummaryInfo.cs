namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCInfobaseSummaryInfo
{
    public required string Uuid { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? Dbms { get; init; }

    public string? DbServer { get; init; }

    public string? DbName { get; init; }

    public string? SessionsDeny { get; init; }

    public string? ScheduledJobsDeny { get; init; }

    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string NameText => string.IsNullOrWhiteSpace(Name) ? "Информационная база" : Name;

    public string UuidText => string.IsNullOrWhiteSpace(Uuid) ? "—" : Uuid;

    public string DescriptionText => string.IsNullOrWhiteSpace(Description) ? "—" : Description;

    public string DatabaseText
    {
        get
        {
            var parts = new[] { Dbms, DbServer, DbName }
                .Where(static value => !string.IsNullOrWhiteSpace(value));

            var text = string.Join(", ", parts);
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }
    }

    public string RestrictionsText
    {
        get
        {
            var restrictions = new List<string>();
            if (!string.IsNullOrWhiteSpace(SessionsDeny))
            {
                restrictions.Add($"Сеансы: {FormatOnOff(SessionsDeny)}");
            }

            if (!string.IsNullOrWhiteSpace(ScheduledJobsDeny))
            {
                restrictions.Add($"Задания: {FormatOnOff(ScheduledJobsDeny)}");
            }

            return restrictions.Count == 0 ? "—" : string.Join(Environment.NewLine, restrictions);
        }
    }

    public static OneCInfobaseSummaryInfo FromProperties(IReadOnlyDictionary<string, string> properties)
    {
        properties.TryGetValue("infobase", out var uuid);
        properties.TryGetValue("name", out var name);
        properties.TryGetValue("descr", out var description);
        properties.TryGetValue("dbms", out var dbms);
        properties.TryGetValue("db-server", out var dbServer);
        properties.TryGetValue("db-name", out var dbName);
        properties.TryGetValue("sessions-deny", out var sessionsDeny);
        properties.TryGetValue("scheduled-jobs-deny", out var scheduledJobsDeny);

        return new OneCInfobaseSummaryInfo
        {
            Uuid = NormalizeValue(uuid) ?? string.Empty,
            Name = NormalizeValue(name),
            Description = NormalizeValue(description),
            Dbms = NormalizeValue(dbms),
            DbServer = NormalizeValue(dbServer),
            DbName = NormalizeValue(dbName),
            SessionsDeny = NormalizeValue(sessionsDeny),
            ScheduledJobsDeny = NormalizeValue(scheduledJobsDeny),
            Properties = properties
        };
    }

    private static string FormatOnOff(string value)
    {
        return value switch
        {
            "on" => "запрещены",
            "off" => "разрешены",
            _ => value
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
