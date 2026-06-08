using FluentIcons.Common;

namespace AdminConsoleFor1C.App;

public sealed record AdminConsoleSectionInfo(
    AdminConsoleSection Section,
    string Title,
    string Description,
    Icon Icon);
