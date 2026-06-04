namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCLicenseUsageInfo
{
    public required OneCLicenseOwnerKind OwnerKind { get; init; }

    public required string SourceKey { get; init; }

    public required string SourceText { get; init; }

    public required string SourceDetailText { get; init; }

    public required string LicenseKindText { get; init; }

    public required string ConsumptionModeText { get; init; }

    public required int OccupiedSeats { get; init; }

    public int? Capacity { get; init; }

    public required int ConsumerCount { get; init; }

    public required string ConsumersDetailText { get; init; }

    public required IReadOnlyList<OneCOccupiedLicenseInfo> Licenses { get; init; }

    public string OwnerKindText => OwnerKind switch
    {
        OneCLicenseOwnerKind.Process => "Серверная",
        OneCLicenseOwnerKind.Session => "Клиентская",
        _ => "Лицензия"
    };

    public string UsageText => Capacity is null ? OccupiedSeats.ToString() : $"{OccupiedSeats}/{Capacity}";

    public string UsageCaptionText => Capacity is null ? "занято" : "занято из лимита";

    public string ConsumersText
    {
        get
        {
            var noun = OwnerKind == OneCLicenseOwnerKind.Session
                ? FormatNoun(ConsumerCount, "сеанс", "сеанса", "сеансов")
                : FormatNoun(ConsumerCount, "процесс", "процесса", "процессов");

            return $"{ConsumerCount} {noun}";
        }
    }

    public string ExplanationText
    {
        get
        {
            if (OwnerKind == OneCLicenseOwnerKind.Session && ConsumerCount != OccupiedSeats)
            {
                return $"{ConsumersText} используют {FormatSeats(OccupiedSeats)}";
            }

            return OwnerKind == OneCLicenseOwnerKind.Session
                ? $"{ConsumersText} занимают {FormatSeats(OccupiedSeats)}"
                : $"{ConsumersText} используют {FormatSeats(OccupiedSeats)}";
        }
    }

    public static IReadOnlyList<OneCLicenseUsageInfo> Create(
        IReadOnlyList<OneCOccupiedLicenseInfo> processLicenses,
        IReadOnlyList<OneCOccupiedLicenseInfo> sessionLicenses)
    {
        return processLicenses
            .GroupBy(GetSourceKey)
            .Select(static group => FromGroup(group.Key, OneCLicenseOwnerKind.Process, group.ToList()))
            .Concat(sessionLicenses
                .GroupBy(GetSourceKey)
                .Select(static group => FromGroup(group.Key, OneCLicenseOwnerKind.Session, group.ToList())))
            .OrderBy(static usage => usage.OwnerKind == OneCLicenseOwnerKind.Session ? 0 : 1)
            .ThenBy(static usage => usage.SourceText, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static OneCLicenseUsageInfo FromGroup(
        string sourceKey,
        OneCLicenseOwnerKind ownerKind,
        IReadOnlyList<OneCOccupiedLicenseInfo> licenses)
    {
        var first = licenses[0];
        var capacity = licenses
            .Select(static license => license.MaxUsersAll ?? license.MaxUsersCurrent)
            .FirstOrDefault(static value => value is not null);
        var isComputerConsumption = IsComputerConsumption(first);
        var occupiedSeats = ownerKind switch
        {
            OneCLicenseOwnerKind.Session when isComputerConsumption => CountDistinctOrItems(licenses, GetConsumerComputer),
            OneCLicenseOwnerKind.Session => CountDistinctOrItems(licenses, static license =>
                FirstNotEmpty(license.SessionUuid, license.RmngrPid, license.Pid)),
            OneCLicenseOwnerKind.Process => CountDistinctOrItems(licenses, static license =>
                FirstNotEmpty(license.ProcessUuid, license.RmngrPid, license.Pid)),
            _ => licenses.Count
        };

        var sourceText = BuildSourceText(first);

        return new OneCLicenseUsageInfo
        {
            OwnerKind = ownerKind,
            SourceKey = sourceKey,
            SourceText = sourceText,
            SourceDetailText = BuildSourceDetailText(first, sourceText),
            LicenseKindText = BuildLicenseKindText(first),
            ConsumptionModeText = BuildConsumptionModeText(ownerKind, first, isComputerConsumption),
            OccupiedSeats = occupiedSeats,
            Capacity = capacity,
            ConsumerCount = licenses.Count,
            ConsumersDetailText = BuildConsumersDetailText(ownerKind, licenses),
            Licenses = licenses
        };
    }

    private static string GetSourceKey(OneCOccupiedLicenseInfo license)
    {
        var sourceId = FirstNotEmpty(
            license.FullName,
            license.Series,
            license.ShortPresentation,
            license.LicenseText);

        var hostPart = IsComputerConsumption(license)
            ? GetConsumerComputer(license)
            : FirstNotEmpty(license.RmngrAddress, license.Server);

        return string.Join(
            "|",
            sourceId,
            license.LicenseType,
            license.Net,
            license.MaxUsersAll?.ToString(),
            hostPart);
    }

    private static string BuildSourceText(OneCOccupiedLicenseInfo license)
    {
        if (!string.IsNullOrWhiteSpace(license.Series))
        {
            return license.Series;
        }

        if (!string.IsNullOrWhiteSpace(license.ShortPresentation))
        {
            return license.ShortPresentation;
        }

        if (!string.IsNullOrWhiteSpace(license.FullName))
        {
            return Path.GetFileName(license.FullName) is { Length: > 0 } fileName
                ? fileName
                : license.FullName;
        }

        return license.LicenseText;
    }

    private static string BuildSourceDetailText(OneCOccupiedLicenseInfo license, string sourceText)
    {
        if (!string.IsNullOrWhiteSpace(license.FullName))
        {
            return license.FullName;
        }

        if (!string.IsNullOrWhiteSpace(license.ShortPresentation)
            && !string.Equals(license.ShortPresentation, sourceText, StringComparison.OrdinalIgnoreCase))
        {
            return license.ShortPresentation;
        }

        return string.Empty;
    }

    private static string BuildLicenseKindText(OneCOccupiedLicenseInfo license)
    {
        var type = license switch
        {
            { IsSoftwareLicense: true } => "программная",
            { IsHaspLicense: true } => "HASP",
            _ when !string.IsNullOrWhiteSpace(license.LicenseType) => license.LicenseType,
            _ => "тип не определен"
        };

        var location = license switch
        {
            { IsNetworkLicense: true } => "сетевая",
            { IsNetworkLicense: false, Net: not null } => "локальная",
            _ => null
        };

        return string.IsNullOrWhiteSpace(location) ? type : $"{type}, {location}";
    }

    private static string BuildConsumptionModeText(
        OneCLicenseOwnerKind ownerKind,
        OneCOccupiedLicenseInfo license,
        bool isComputerConsumption)
    {
        if (ownerKind == OneCLicenseOwnerKind.Process)
        {
            return "серверная";
        }

        return isComputerConsumption ? "на компьютер" : "на сеанс";
    }

    private static bool IsComputerConsumption(OneCOccupiedLicenseInfo license)
    {
        return license.OwnerKind == OneCLicenseOwnerKind.Session
            && !license.IsIssuedByServer
            && !license.IsNetworkLicense
            && license.MaxUsersAll is 1;
    }

    private static string BuildConsumersDetailText(
        OneCLicenseOwnerKind ownerKind,
        IReadOnlyList<OneCOccupiedLicenseInfo> licenses)
    {
        var parts = new List<string>();

        if (ownerKind == OneCLicenseOwnerKind.Session)
        {
            AddPart(parts, "Пользователи", JoinDistinct(licenses.Select(static license => license.UserName)));
            AddPart(parts, "Компьютеры", JoinDistinct(licenses.Select(GetConsumerComputer)));
            AddPart(parts, "Приложения", JoinDistinct(licenses.Select(static license => license.AppId)));
        }
        else
        {
            AddPart(parts, "PID", JoinDistinct(licenses.Select(static license =>
                FirstNotEmpty(license.RmngrPid, license.Pid))));
            AddPart(parts, "Серверы", JoinDistinct(licenses.Select(static license =>
                FirstNotEmpty(license.RmngrAddress, license.Server, license.Host))));
        }

        return parts.Count == 0 ? "—" : string.Join("; ", parts);
    }

    private static void AddPart(List<string> parts, string title, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{title}: {value}");
        }
    }

    private static string JoinDistinct(IEnumerable<string?> values)
    {
        var distinctValues = values
            .Select(NullIfWhiteSpace)
            .Where(static value => value is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        return distinctValues.Count == 0 ? string.Empty : string.Join(", ", distinctValues);
    }

    private static int CountDistinctOrItems(
        IReadOnlyList<OneCOccupiedLicenseInfo> licenses,
        Func<OneCOccupiedLicenseInfo, string?> selector)
    {
        var distinctCount = licenses
            .Select(selector)
            .Select(NullIfWhiteSpace)
            .Where(static value => value is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return distinctCount == 0 ? licenses.Count : distinctCount;
    }

    private static string? GetConsumerComputer(OneCOccupiedLicenseInfo license)
    {
        return FirstNotEmpty(license.Host, license.RmngrAddress, license.Server);
    }

    private static string? FirstNotEmpty(params string?[] values)
    {
        return values.Select(NullIfWhiteSpace).FirstOrDefault(static value => value is not null);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string FormatSeats(int value)
    {
        return $"{value} {FormatNoun(value, "место", "места", "мест")}";
    }

    private static string FormatNoun(int value, string one, string few, string many)
    {
        var lastTwoDigits = Math.Abs(value) % 100;
        if (lastTwoDigits is >= 11 and <= 14)
        {
            return many;
        }

        return (Math.Abs(value) % 10) switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many
        };
    }
}
