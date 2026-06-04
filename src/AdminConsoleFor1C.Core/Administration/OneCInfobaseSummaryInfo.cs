namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCInfobaseSummaryInfo
{
    public required string Uuid { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? Dbms { get; init; }

    public string? DbServer { get; init; }

    public string? DbName { get; init; }

    public string? DbUser { get; init; }

    public string? SecurityLevel { get; init; }

    public string? LicenseDistribution { get; init; }

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
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Dbms))
            {
                parts.Add($"СУБД: {FormatDbms(Dbms)}");
            }

            if (!string.IsNullOrWhiteSpace(DbServer))
            {
                parts.Add($"Сервер: {DbServer}");
            }

            if (!string.IsNullOrWhiteSpace(DbName))
            {
                parts.Add($"База: {DbName}");
            }

            if (!string.IsNullOrWhiteSpace(DbUser))
            {
                parts.Add($"Пользователь: {DbUser}");
            }

            var text = string.Join(Environment.NewLine, parts);
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }
    }

    public string SecurityText => string.IsNullOrWhiteSpace(SecurityLevel)
        ? "—"
        : $"Защищенное соединение: {FormatSecurityLevel(SecurityLevel)}";

    public string RestrictionsText
    {
        get
        {
            var restrictions = new List<string>();
            if (!string.IsNullOrWhiteSpace(LicenseDistribution))
            {
                restrictions.Add($"Лицензии: {FormatLicenseDistribution(LicenseDistribution)}");
            }

            if (!string.IsNullOrWhiteSpace(SessionsDeny))
            {
                restrictions.Add($"Сеансы: {FormatDenyFlag(SessionsDeny)}");
            }

            if (!string.IsNullOrWhiteSpace(ScheduledJobsDeny))
            {
                restrictions.Add($"Регл. задания: {FormatDenyFlag(ScheduledJobsDeny)}");
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
        properties.TryGetValue("db-user", out var dbUser);
        properties.TryGetValue("security-level", out var securityLevel);
        properties.TryGetValue("license-distribution", out var licenseDistribution);
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
            DbUser = NormalizeValue(dbUser),
            SecurityLevel = NormalizeValue(securityLevel),
            LicenseDistribution = NormalizeValue(licenseDistribution),
            SessionsDeny = NormalizeValue(sessionsDeny),
            ScheduledJobsDeny = NormalizeValue(scheduledJobsDeny),
            Properties = properties
        };
    }

    private static string FormatDbms(string value)
    {
        return value switch
        {
            "MSSQLServer" => "MS SQL Server",
            "PostgreSQL" => "PostgreSQL",
            "IBMDB2" => "IBM DB2",
            "OracleDatabase" => "Oracle Database",
            _ => value
        };
    }

    private static string FormatSecurityLevel(string value)
    {
        return value switch
        {
            "0" => "выключено",
            "1" => "только соединение",
            "2" => "постоянно",
            _ => value
        };
    }

    private static string FormatLicenseDistribution(string value)
    {
        return value switch
        {
            "allow" => "выдаются",
            "deny" => "запрещены",
            _ => value
        };
    }

    private static string FormatDenyFlag(string value)
    {
        return value switch
        {
            "on" => "заблокированы",
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
