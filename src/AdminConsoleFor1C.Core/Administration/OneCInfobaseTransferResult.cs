namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCInfobaseTransferResult
{
    public required bool IsSuccess { get; init; }

    public required string Message { get; init; }

    public OneCInfobaseSummaryInfo? CreatedInfobase { get; init; }

    public int TerminatedSessions { get; init; }

    public bool OldRegistrationDropped { get; init; }

    public IReadOnlyList<OneCClusterCommandResult> Commands { get; init; } = [];
}
