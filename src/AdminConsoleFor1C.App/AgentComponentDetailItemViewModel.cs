using FluentIcons.Common;

namespace AdminConsoleFor1C.App;

public sealed record AgentComponentDetailItemViewModel
{
    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string Value { get; init; }

    public required Icon Icon { get; init; }
}
