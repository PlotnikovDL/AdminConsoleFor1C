using System.Globalization;

namespace AdminConsoleFor1C.Core.Services;

public sealed record OneCServiceRegistrationRequest
{
    public string ServiceName { get; init; } = string.Empty;
    public string DisplayName => ServiceName;
    public string ExecutablePath { get; init; } = string.Empty;
    public string DataDirectory { get; init; } = string.Empty;
    public int AgentPort { get; init; } = 1540;
    public int ClusterPort { get; init; } = 1541;
    public int ProcessPortStart { get; init; } = 1560;
    public int ProcessPortEnd { get; init; } = 1591;
    public int? DebugPort { get; init; }
    public bool AutomaticStart { get; init; } = true;
    public OneCServiceAccount Account { get; init; } = OneCServiceAccount.NetworkService;
    public string? UserName { get; init; }

    public string? ServiceStartName => Account switch
    {
        OneCServiceAccount.NetworkService => @"NT AUTHORITY\NetworkService",
        OneCServiceAccount.LocalService => @"NT AUTHORITY\LocalService",
        OneCServiceAccount.LocalSystem => null,
        OneCServiceAccount.WindowsUser => UserName,
        _ => throw new ArgumentOutOfRangeException(nameof(Account))
    };

    public IReadOnlyList<string> Validate(IEnumerable<OneCServiceInfo>? existingServices = null)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ServiceName) || ServiceName.Length > 256
            || ServiceName.Any(c => char.IsControl(c) || c is '/' or '\\' or '"')
            || ServiceName != ServiceName.Trim())
            errors.Add("Укажите системное имя службы: до 256 символов, без кавычек, слешей и пробелов по краям.");
        if (!IsLocalPath(ExecutablePath) || !ExecutablePath.EndsWith("\\ragent.exe", StringComparison.OrdinalIgnoreCase))
            errors.Add("Укажите полный локальный путь к ragent.exe.");
        if (!IsLocalPath(DataDirectory) || DataDirectory.TrimEnd('\\').Length <= 2)
            errors.Add("Укажите полный путь к отдельному локальному каталогу данных кластера.");
        if (!Enum.IsDefined(Account))
            errors.Add("Выберите учетную запись службы.");
        if (Account == OneCServiceAccount.WindowsUser && !IsQualifiedUserName(UserName))
            errors.Add(@"Выберите пользователя или укажите имя в формате .\пользователь, ДОМЕН\пользователь или пользователь@домен.");

        var ports = new[] { AgentPort, ClusterPort, ProcessPortStart, ProcessPortEnd };
        if (ports.Any(p => p is < 1 or > 65535) || DebugPort is < 1 or > 65535)
            errors.Add("Порты должны быть целыми числами от 1 до 65535.");
        if (ProcessPortStart > ProcessPortEnd)
            errors.Add("Начало диапазона рабочих процессов должно быть не больше конца.");
        if (AgentPort == ClusterPort || DebugPort == AgentPort || DebugPort == ClusterPort
            || InProcessRange(AgentPort) || InProcessRange(ClusterPort)
            || (DebugPort is { } debug && InProcessRange(debug)))
            errors.Add("Порты агента, кластера и отладки должны различаться и находиться вне диапазона рабочих процессов.");

        foreach (var service in existingServices ?? [])
        {
            if (Same(ServiceName, service.Name) || Same(DisplayName, service.DisplayName)
                || Same(ServiceName, service.DisplayName) || Same(DisplayName, service.Name))
                errors.Add($"Имя уже занято службой «{service.DisplayName}» ({service.Name}).");
            if (!string.IsNullOrWhiteSpace(service.DataDirectory)
                && Same(NormalizeDirectory(DataDirectory), NormalizeDirectory(service.DataDirectory)))
                errors.Add($"Каталог данных уже используется службой «{service.DisplayName}».");
            if (ConflictsWithPorts(service))
                errors.Add($"Порты пересекаются с настройками службы «{service.DisplayName}» ({service.Name}).");
        }

        return errors;
    }

    public bool ConflictsWithPorts(OneCServiceInfo service)
    {
        var isAgent = service.Kind == OneCServiceKind.ServerAgent;
        var fixedPorts = new[]
        {
            service.AgentPort ?? (isAgent ? 1540 : (int?)null),
            service.RegPort ?? (isAgent ? 1541 : (int?)null),
            service.AdministrationServerPort ?? (service.Kind == OneCServiceKind.AdministrationServer ? 1545 : (int?)null),
            service.DebugServerPort ?? (service.Kind == OneCServiceKind.DebugServer ? 1550 : (int?)null)
        };
        if (fixedPorts.Any(p => p is { } port && UsesPort(port)))
            return true;

        foreach (var range in (service.PortRange ?? (isAgent ? "1560:1591" : string.Empty)).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = range.Split(':');
            if (int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start)
                && int.TryParse(bounds[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var end)
                && (UsesPort(start) || UsesPort(end) || (start <= ProcessPortStart && end >= ProcessPortEnd)
                    || (start <= AgentPort && end >= AgentPort) || (start <= ClusterPort && end >= ClusterPort)
                    || (DebugPort is { } debug && start <= debug && end >= debug)))
                return true;
        }

        return false;
    }

    public bool UsesPort(int port) => port == AgentPort || port == ClusterPort || port == DebugPort || InProcessRange(port);

    public string BuildCommandLine()
    {
        var errors = Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors));

        // Remove trailing slashes so they cannot escape the closing quote in Windows argv parsing.
        var dataPath = DataDirectory.TrimEnd('\\');
        // 1C 8.5.1 administrator guide, appendix 4, sections 4.12.2 and 4.12.1.
        // -http is the value of /debug, not an independent server switch.
        return $"\"{ExecutablePath}\" /srvc /agent /regport {ClusterPort} /port {AgentPort}"
            + $" /range {ProcessPortStart}:{ProcessPortEnd} /d \"{dataPath}\""
            + (DebugPort is { } debug ? $" /debug -http /debugServerPort {debug}" : string.Empty);
    }

    private static bool IsQualifiedUserName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 256
            || name.Any(c => char.IsControl(c) || c is '"' or '/' or ':' or '*' or '?'))
            return false;
        var separator = name.IndexOf('\\');
        if (separator >= 0)
            return separator > 0 && separator < name.Length - 1 && name.LastIndexOf('\\') == separator;
        var at = name.IndexOf('@');
        return at > 0 && at < name.Length - 1 && name.LastIndexOf('@') == at;
    }

    private bool InProcessRange(int port) => port >= ProcessPortStart && port <= ProcessPortEnd;
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string NormalizeDirectory(string path) => IsLocalPath(path)
        ? Path.GetFullPath(path).TrimEnd('\\')
        : path.TrimEnd('\\');

    private static bool IsLocalPath(string? path) => !string.IsNullOrWhiteSpace(path)
        && path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\'
        && !path.Any(c => char.IsControl(c) || c is '"' or '<' or '>' or '|' or '?' or '*' or '/')
        && !path[2..].Contains(':')
        && path == path.Trim();
}

public enum OneCServiceAccount
{
    NetworkService,
    LocalSystem,
    WindowsUser,
    LocalService
}
