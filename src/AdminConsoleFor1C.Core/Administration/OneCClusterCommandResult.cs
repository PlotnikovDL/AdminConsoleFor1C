namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCClusterCommandResult
{
    public required string CommandText { get; init; }

    public required bool IsSuccess { get; init; }

    public required string Message { get; init; }
}
