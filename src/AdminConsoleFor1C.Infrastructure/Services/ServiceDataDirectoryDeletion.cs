namespace AdminConsoleFor1C.Infrastructure.Services;

public static class ServiceDataDirectoryDeletion
{
    public static void ValidateTree(string path)
    {
        var rootAttributes = EnsureNotLink(path);
        if (rootAttributes is not null && !rootAttributes.Value.HasFlag(FileAttributes.Directory))
            throw new InvalidOperationException("Путь /d указывает на файл, а не на каталог.");
        // Check ancestors as well: a normal directory can sit behind a junction.
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            EnsureNotLink(current.FullName);
        if (Directory.Exists(path))
            InspectChildren(path);
    }

    public static void Delete(string path)
    {
        ValidateTree(path);
        if (Directory.Exists(path))
            DeleteChildrenAndDirectory(path);
    }

    private static void InspectChildren(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = EnsureNotLink(entry);
            if (attributes?.HasFlag(FileAttributes.Directory) == true)
                InspectChildren(entry);
        }
    }

    private static void DeleteChildrenAndDirectory(string path)
    {
        EnsureNotLink(path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = EnsureNotLink(entry);
            if (attributes?.HasFlag(FileAttributes.Directory) == true)
                DeleteChildrenAndDirectory(entry);
            else
                File.Delete(entry);
        }
        // Non-recursive deletion never follows a newly encountered directory link.
        Directory.Delete(path, recursive: false);
    }

    private static FileAttributes? EnsureNotLink(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException($"Каталог данных содержит ссылку или точку подключения: {path}. Автоматическое удаление отменено.");
        return attributes;
    }
}
