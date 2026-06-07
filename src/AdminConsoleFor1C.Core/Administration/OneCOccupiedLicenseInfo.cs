namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCOccupiedLicenseInfo
{
    public required OneCLicenseOwnerKind OwnerKind { get; init; }

    public string? ProcessUuid { get; init; }

    public string? SessionUuid { get; init; }

    public string? SessionId { get; init; }

    public string? InfobaseUuid { get; init; }

    public string? UserName { get; init; }

    public string? Host { get; init; }

    public string? AppId { get; init; }

    public string? Pid { get; init; }

    public string? Server { get; init; }

    public string? Port { get; init; }

    public string? License { get; init; }

    public string? LicenseFile { get; init; }

    public string? FullName { get; init; }

    public string? Series { get; init; }

    public string? IssuedByServer { get; init; }

    public string? LicenseType { get; init; }

    public string? Net { get; init; }

    public int? MaxUsersAll { get; init; }

    public int? MaxUsersCurrent { get; init; }

    public string? RmngrAddress { get; init; }

    public string? RmngrPort { get; init; }

    public string? RmngrPid { get; init; }

    public string? ShortPresentation { get; init; }

    public string? FullPresentation { get; init; }

    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string OwnerKindText => OwnerKind switch
    {
        OneCLicenseOwnerKind.Process => "Сервер",
        OneCLicenseOwnerKind.Session => "Пользователь",
        _ => "Лицензия"
    };

    public string OwnerText
    {
        get
        {
            if (OwnerKind == OneCLicenseOwnerKind.Session)
            {
                var user = string.IsNullOrWhiteSpace(UserName) ? "Пользователь" : UserName;
                return string.IsNullOrWhiteSpace(SessionId) ? user : $"{user}, сеанс {SessionId}";
            }

            if (!string.IsNullOrWhiteSpace(Pid))
            {
                return $"PID {Pid}";
            }

            if (!string.IsNullOrWhiteSpace(RmngrPid))
            {
                return $"PID {RmngrPid}";
            }

            return string.IsNullOrWhiteSpace(ProcessUuid) ? "Рабочий процесс" : ProcessUuid;
        }
    }

    public string ContextText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Host))
            {
                parts.Add(Host);
            }

            if (!string.IsNullOrWhiteSpace(AppId))
            {
                parts.Add(AppId);
            }

            if (!string.IsNullOrWhiteSpace(Server))
            {
                parts.Add($"сервер {Server}");
            }
            else if (!string.IsNullOrWhiteSpace(RmngrAddress))
            {
                parts.Add($"сервер {RmngrAddress}");
            }

            if (!string.IsNullOrWhiteSpace(Port))
            {
                parts.Add($"порт {Port}");
            }
            else if (!string.IsNullOrWhiteSpace(RmngrPort) && RmngrPort != "0")
            {
                parts.Add($"порт {RmngrPort}");
            }

            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }
    }

    public string LicenseText => string.IsNullOrWhiteSpace(License)
        ? !string.IsNullOrWhiteSpace(FullPresentation)
            ? FullPresentation
            : !string.IsNullOrWhiteSpace(ShortPresentation)
                ? ShortPresentation
                : BuildFallbackLicenseText()
        : License;

    public string LicenseFileText => string.IsNullOrWhiteSpace(LicenseFile)
        ? string.IsNullOrWhiteSpace(FullName) ? "—" : FullName
        : LicenseFile;

    public string InfobaseText => string.IsNullOrWhiteSpace(InfobaseUuid) ? "—" : InfobaseUuid;

    public bool IsIssuedByServer => string.Equals(IssuedByServer, "yes", StringComparison.OrdinalIgnoreCase);

    public bool IsNetworkLicense => string.Equals(Net, "yes", StringComparison.OrdinalIgnoreCase);

    public bool IsHaspLicense => string.Equals(LicenseType, "HASP", StringComparison.OrdinalIgnoreCase);

    public bool IsSoftwareLicense => string.Equals(LicenseType, "soft", StringComparison.OrdinalIgnoreCase);

    public static OneCOccupiedLicenseInfo FromProperties(
        IReadOnlyDictionary<string, string> properties,
        OneCLicenseOwnerKind ownerKind)
    {
        properties.TryGetValue("process", out var processUuid);
        properties.TryGetValue("session", out var sessionUuid);
        properties.TryGetValue("session-id", out var sessionId);
        properties.TryGetValue("infobase", out var infobaseUuid);
        properties.TryGetValue("user-name", out var userName);
        properties.TryGetValue("host", out var host);
        properties.TryGetValue("app-id", out var appId);
        properties.TryGetValue("pid", out var pid);
        properties.TryGetValue("server", out var server);
        properties.TryGetValue("port", out var port);
        properties.TryGetValue("license", out var license);
        properties.TryGetValue("client-license", out var clientLicense);
        properties.TryGetValue("server-license", out var serverLicense);
        properties.TryGetValue("license-file", out var licenseFile);
        properties.TryGetValue("full-name", out var fullName);
        properties.TryGetValue("series", out var series);
        properties.TryGetValue("issued-by-server", out var issuedByServer);
        properties.TryGetValue("license-type", out var licenseType);
        properties.TryGetValue("net", out var net);
        properties.TryGetValue("max-users-all", out var maxUsersAll);
        properties.TryGetValue("max-users-cur", out var maxUsersCurrent);
        properties.TryGetValue("rmngr-address", out var rmngrAddress);
        properties.TryGetValue("rmngr-port", out var rmngrPort);
        properties.TryGetValue("rmngr-pid", out var rmngrPid);
        properties.TryGetValue("short-presentation", out var shortPresentation);
        properties.TryGetValue("full-presentation", out var fullPresentation);

        return new OneCOccupiedLicenseInfo
        {
            OwnerKind = ownerKind,
            ProcessUuid = NormalizeValue(processUuid),
            SessionUuid = NormalizeValue(sessionUuid),
            SessionId = NormalizeValue(sessionId),
            InfobaseUuid = NormalizeValue(infobaseUuid),
            UserName = NormalizeValue(userName),
            Host = NormalizeValue(host),
            AppId = NormalizeValue(appId),
            Pid = NormalizeValue(pid),
            Server = NormalizeValue(server),
            Port = NormalizeValue(port),
            License = NormalizeValue(license)
                ?? NormalizeValue(clientLicense)
                ?? NormalizeValue(serverLicense),
            LicenseFile = NormalizeValue(licenseFile),
            FullName = NormalizeValue(fullName),
            Series = NormalizeValue(series),
            IssuedByServer = NormalizeValue(issuedByServer),
            LicenseType = NormalizeValue(licenseType),
            Net = NormalizeValue(net),
            MaxUsersAll = TryParseInt32(maxUsersAll),
            MaxUsersCurrent = TryParseInt32(maxUsersCurrent),
            RmngrAddress = NormalizeValue(rmngrAddress),
            RmngrPort = NormalizeValue(rmngrPort),
            RmngrPid = NormalizeValue(rmngrPid),
            ShortPresentation = NormalizeValue(shortPresentation),
            FullPresentation = NormalizeValue(fullPresentation),
            Properties = properties
        };
    }

    private string BuildFallbackLicenseText()
    {
        var values = Properties
            .Where(static pair => !KnownKeys.Contains(pair.Key))
            .Select(static pair => NormalizeValue(pair.Value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return values.Count == 0 ? "—" : string.Join(", ", values);
    }

    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "process",
        "session",
        "session-id",
        "infobase",
        "user-name",
        "host",
        "app-id",
        "pid",
        "server",
        "port",
        "license",
        "client-license",
        "server-license",
        "license-file",
        "full-name",
        "series",
        "issued-by-server",
        "license-type",
        "net",
        "max-users-all",
        "max-users-cur",
        "rmngr-address",
        "rmngr-port",
        "rmngr-pid",
        "short-presentation",
        "full-presentation"
    };

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
