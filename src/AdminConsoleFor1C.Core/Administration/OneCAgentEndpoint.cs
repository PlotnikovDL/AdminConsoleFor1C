namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCAgentEndpoint
{
    public required string DisplayName { get; init; }

    public required string AgentAddress { get; init; }

    public required string AdministrationServerAddress { get; init; }

    public required int AgentPort { get; init; }

    public required string Version { get; init; }

    public required string RacPath { get; init; }

    public string? ServiceName { get; init; }

    public string? WindowsDisplayName { get; init; }

    public string DisplayText => $"{DisplayName} ({AdministrationServerAddress})";
}
