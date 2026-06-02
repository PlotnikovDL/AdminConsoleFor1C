namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCClusterInventoryResult
{
    public required string AdministrationServerAddress { get; init; }

    public required string CommandText { get; init; }

    public required bool IsAvailable { get; init; }

    public required string Message { get; init; }

    public required IReadOnlyList<OneCClusterInfo> Clusters { get; init; }

    public static OneCClusterInventoryResult Unavailable(
        string administrationServerAddress,
        string commandText,
        string message)
    {
        return new OneCClusterInventoryResult
        {
            AdministrationServerAddress = administrationServerAddress,
            CommandText = commandText,
            IsAvailable = false,
            Message = message,
            Clusters = []
        };
    }
}
