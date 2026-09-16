namespace AdminConsoleFor1C.Core.Services;

public sealed record OneCServiceDeletionRequest
{
    public required string ServiceName { get; init; }
    public required string ExpectedCommandLine { get; init; }
    // Null explicitly means keep the data. Never infer a directory when /d is absent.
    public string? DataDirectory { get; init; }

    public string? ValidateAndGetDataDirectory(IReadOnlyList<OneCServiceInfo> services)
    {
        var service = services.SingleOrDefault(s => string.Equals(s.Name, ServiceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Служба больше не найдена. Обновите список.");
        if (string.IsNullOrWhiteSpace(ExpectedCommandLine) || service.RawCommandLine != ExpectedCommandLine)
            throw new InvalidOperationException("Параметры службы изменились. Откройте подтверждение удаления заново.");
        if (DataDirectory is null)
            return null;
        if (service.Kind != OneCServiceKind.ServerAgent || string.IsNullOrWhiteSpace(service.DataDirectory))
            throw new InvalidOperationException("У службы не указан каталог данных агента в параметре /d.");

        var path = NormalizeLocalDirectory(DataDirectory);
        if (!Same(path, NormalizeLocalDirectory(service.DataDirectory)))
            throw new InvalidOperationException("Каталог удаления не совпадает с параметром /d службы.");

        var protectedDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppContext.BaseDirectory
        }.Where(p => !string.IsNullOrWhiteSpace(p));
        if (protectedDirectories.Any(p => Contains(path, Path.GetFullPath(p).TrimEnd('\\')))
            || Same(Path.GetFileName(path), "srvinfo"))
            throw new InvalidOperationException("Нельзя удалить системный каталог, каталог программы или общий каталог srvinfo.");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows) && Contains(windows, path))
            throw new InvalidOperationException("Удаление данных из каталога Windows запрещено.");

        foreach (var other in services)
        {
            if (!string.IsNullOrWhiteSpace(other.ExecutablePath)
                && Contains(path, Path.GetFullPath(other.ExecutablePath)))
                throw new InvalidOperationException("Каталог содержит установленную программу службы.");
            if (Same(other.Name, ServiceName) || string.IsNullOrWhiteSpace(other.DataDirectory))
                continue;
            var otherPath = NormalizeLocalDirectory(other.DataDirectory);
            if (Contains(path, otherPath) || Contains(otherPath, path))
                throw new InvalidOperationException($"Каталог пересекается с данными службы «{other.DisplayName}».");
        }
        return path;
    }

    private static string NormalizeLocalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 4 || !char.IsAsciiLetter(path[0])
            || path[1] != ':' || path[2] != '\\' || path != path.Trim()
            || path.Any(c => char.IsControl(c) || c is '"' or '/' or '*' or '?' or '<' or '>' or '|' or '~')
            || path[2..].Contains(':') || path.Split('\\').Any(p => p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidOperationException("Для удаления нужен полный локальный путь /d без сокращений и переходов к родительским каталогам.");
        var fullPath = Path.GetFullPath(path).TrimEnd('\\');
        if (fullPath.Length <= 3 || Path.GetDirectoryName(fullPath)?.Length <= 3)
            throw new InvalidOperationException("Нельзя удалять корень диска или каталог верхнего уровня.");
        return fullPath;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string parent, string child) => Same(parent, child)
        || child.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
