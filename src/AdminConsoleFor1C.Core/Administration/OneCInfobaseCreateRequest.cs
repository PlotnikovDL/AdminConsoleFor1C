namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCInfobaseCreateRequest
{
    public required string AdministrationServerAddress { get; init; }

    public required string ClusterUuid { get; init; }

    public required string Name { get; init; }

    public required string Dbms { get; init; }

    public required string DbServer { get; init; }

    public required string DbName { get; init; }

    public required string Locale { get; init; }

    public string? DbUser { get; init; }

    public string? DbPassword { get; init; }

    public string? Description { get; init; }

    public string? DateOffset { get; init; }

    public string? SecurityLevel { get; init; }

    public string? ScheduledJobsDeny { get; init; }

    public string? LicenseDistribution { get; init; }

    public bool CreateDatabase { get; init; }
}
