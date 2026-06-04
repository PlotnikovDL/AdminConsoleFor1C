namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCInfobaseRestrictionsUpdateRequest
{
    public required string AdministrationServerAddress { get; init; }

    public required string ClusterUuid { get; init; }

    public required string InfobaseUuid { get; init; }

    public required string InfobaseName { get; init; }

    public string? SessionsDeny { get; init; }

    public string? ScheduledJobsDeny { get; init; }
}
