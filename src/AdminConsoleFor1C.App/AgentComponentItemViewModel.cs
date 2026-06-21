using FluentIcons.Common;

namespace AdminConsoleFor1C.App;

public sealed record AgentComponentItemViewModel
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string SummaryDescription { get; init; }

    public required string StatusText { get; init; }

    public required string ProcessSummaryText { get; init; }

    public required string StatusSummaryText { get; init; }

    public required AgentStatusKind StatusKind { get; init; }

    public required string PrimaryPortSummaryText { get; init; }

    public required string VersionSummaryText { get; init; }

    public required string StartModeSummaryText { get; init; }

    public required string TechnicalSummaryText { get; init; }

    public required int SortOrder { get; init; }

    public required Icon Icon { get; init; }

    public required IReadOnlyList<AgentComponentDetailItemViewModel> Details { get; init; }

    public required IReadOnlyList<OneCProcessItemViewModel> Processes { get; init; }

    public required string? ServiceName { get; init; }

    public required string ServiceDisplayName { get; init; }

    public required bool CanStartService { get; init; }

    public required bool CanStopService { get; init; }

    public required bool CanRestartService { get; init; }

    public bool HasService => !string.IsNullOrWhiteSpace(ServiceName);
}
