using FluentIcons.Common;

namespace AdminConsoleFor1C.App;

public sealed record AgentServiceCandidateItemViewModel
{
    public required string Title { get; init; }

    public required string SummaryDescription { get; init; }

    public required string ExecutablePathText { get; init; }

    public required string StatusSummaryText { get; init; }

    public required AgentStatusKind StatusKind { get; init; }

    public required string TechnicalSummaryText { get; init; }

    public required Icon Icon { get; init; }
}
