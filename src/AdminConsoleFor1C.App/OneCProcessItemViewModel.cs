using FluentIcons.Common;

namespace AdminConsoleFor1C.App;

public sealed record OneCProcessItemViewModel
{
    public required uint ProcessId { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string ProcessIdText { get; init; }

    public required string RoleText { get; init; }

    public required Icon Icon { get; init; }

    public required IReadOnlyList<OneCProcessDetailItemViewModel> Details { get; init; }
}
