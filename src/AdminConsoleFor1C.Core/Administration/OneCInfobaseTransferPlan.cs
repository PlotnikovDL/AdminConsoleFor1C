namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCInfobaseTransferPlan
{
    public required OneCAgentEndpoint SourceAgent { get; init; }

    public required string SourceClusterUuid { get; init; }

    public required OneCInfobaseSummaryInfo SourceInfobase { get; init; }

    public required OneCAgentEndpoint TargetAgent { get; init; }

    public required string TargetClusterUuid { get; init; }

    public string? DbPassword { get; init; }

    public string SessionTerminationMessage { get; init; } =
        "Сеанс завершен перед переносом информационной базы на другой агент 1С.";
}
