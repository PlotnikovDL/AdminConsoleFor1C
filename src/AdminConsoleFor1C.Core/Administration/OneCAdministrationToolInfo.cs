namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCAdministrationToolInfo
{
    public required OneCAdministrationToolKind Kind { get; init; }

    public required string FilePath { get; init; }

    public string? Version { get; init; }

    public string FileName => Kind switch
    {
        OneCAdministrationToolKind.Rac => "rac.exe",
        OneCAdministrationToolKind.Ras => "ras.exe",
        OneCAdministrationToolKind.Ragent => "ragent.exe",
        _ => "tool.exe"
    };

    public string KindDisplayName => Kind switch
    {
        OneCAdministrationToolKind.Rac => "Утилита командной строки",
        OneCAdministrationToolKind.Ras => "Сервер администрирования",
        OneCAdministrationToolKind.Ragent => "Агент сервера",
        _ => "Инструмент"
    };

    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "—" : Version;

    public string FilePathText => string.IsNullOrWhiteSpace(FilePath) ? "—" : FilePath;
}
