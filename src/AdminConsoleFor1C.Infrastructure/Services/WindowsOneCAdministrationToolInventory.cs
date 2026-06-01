using System.Diagnostics;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

public sealed class WindowsOneCAdministrationToolInventory : IOneCAdministrationToolInventory
{
    public Task<IReadOnlyList<OneCAdministrationToolInfo>> GetToolsAsync(
        IReadOnlyCollection<string> knownExecutablePaths,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GetTools(knownExecutablePaths, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<OneCAdministrationToolInfo> GetTools(
        IReadOnlyCollection<string> knownExecutablePaths,
        CancellationToken cancellationToken)
    {
        var toolsByPath = new Dictionary<string, OneCAdministrationToolInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in GetCandidateDirectories(knownExecutablePaths))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddToolIfExists(toolsByPath, directory, OneCAdministrationToolKind.Rac);
            AddToolIfExists(toolsByPath, directory, OneCAdministrationToolKind.Ras);
        }

        return toolsByPath.Values
            .OrderBy(static tool => tool.Kind)
            .ThenByDescending(static tool => ParseVersion(tool.Version))
            .ThenBy(static tool => tool.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> GetCandidateDirectories(IReadOnlyCollection<string> knownExecutablePaths)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var executablePath in knownExecutablePaths)
        {
            AddDirectoryFromExecutablePath(directories, executablePath);
        }

        foreach (var root in GetProgramFiles1CRoots())
        {
            foreach (var versionDirectory in SafeEnumerateDirectories(root))
            {
                AddDirectory(directories, Path.Combine(versionDirectory, "bin"));
            }
        }

        foreach (var pathDirectory in GetPathDirectories())
        {
            if (pathDirectory.Contains("1cv8", StringComparison.OrdinalIgnoreCase))
            {
                AddDirectory(directories, pathDirectory);
            }
        }

        return directories;
    }

    private static void AddToolIfExists(
        Dictionary<string, OneCAdministrationToolInfo> toolsByPath,
        string directory,
        OneCAdministrationToolKind kind)
    {
        var filePath = Path.Combine(directory, GetToolFileName(kind));
        if (!File.Exists(filePath))
        {
            return;
        }

        var fullPath = GetFullPath(filePath);
        toolsByPath[fullPath] = new OneCAdministrationToolInfo
        {
            Kind = kind,
            FilePath = fullPath,
            Version = OneCServiceCommandLineParser.GetVersionFromExecutablePath(fullPath)
                ?? GetFileVersion(fullPath)
        };
    }

    private static string GetToolFileName(OneCAdministrationToolKind kind)
    {
        return kind switch
        {
            OneCAdministrationToolKind.Rac => "rac.exe",
            OneCAdministrationToolKind.Ras => "ras.exe",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static void AddDirectoryFromExecutablePath(HashSet<string> directories, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(executablePath);
        AddDirectory(directories, directory);
    }

    private static void AddDirectory(HashSet<string> directories, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        directories.Add(GetFullPath(directory));
    }

    private static IEnumerable<string> GetProgramFiles1CRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddRoot(roots, Environment.GetEnvironmentVariable("ProgramW6432"));
        AddRoot(roots, @"C:\Program Files");
        AddRoot(roots, @"C:\Program Files (x86)");

        foreach (var root in roots)
        {
            var oneCRoot = Path.Combine(root, "1cv8");
            if (Directory.Exists(oneCRoot))
            {
                yield return oneCRoot;
            }
        }
    }

    private static void AddRoot(HashSet<string> roots, string? root)
    {
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            roots.Add(GetFullPath(root));
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> GetPathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string GetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static string? GetFileVersion(string filePath)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(filePath).FileVersion?.Replace(',', '.');
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Version ParseVersion(string? version)
    {
        return Version.TryParse(version, out var parsed)
            ? parsed
            : new Version();
    }
}
