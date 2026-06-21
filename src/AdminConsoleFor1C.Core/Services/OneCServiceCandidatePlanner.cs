using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Core.Services;

public static class OneCServiceCandidatePlanner
{
    public static IReadOnlyList<OneCServerAgentServiceCandidate> BuildServerAgentCandidates(
        IReadOnlyCollection<OneCAdministrationToolInfo> tools,
        IReadOnlyCollection<OneCServiceInfo> existingServices)
    {
        var existingServerAgentPaths = existingServices
            .Where(static service => service.Kind == OneCServiceKind.ServerAgent)
            .Select(static service => NormalizePath(service.ExecutablePath))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidatesByPath = new Dictionary<string, OneCServerAgentServiceCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools.Where(static tool => tool.Kind == OneCAdministrationToolKind.Ragent))
        {
            if (string.IsNullOrWhiteSpace(tool.FilePath))
            {
                continue;
            }

            var executablePath = NormalizePath(tool.FilePath);
            if (string.IsNullOrWhiteSpace(executablePath) || existingServerAgentPaths.Contains(executablePath))
            {
                continue;
            }

            candidatesByPath[executablePath] = new OneCServerAgentServiceCandidate
            {
                ExecutablePath = executablePath,
                Version = tool.Version ?? OneCServiceCommandLineParser.GetVersionFromExecutablePath(executablePath)
            };
        }

        return candidatesByPath.Values
            .OrderByDescending(static candidate => ParseVersion(candidate.Version))
            .ThenBy(static candidate => candidate.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim();
        }
    }

    private static Version ParseVersion(string? version)
    {
        return Version.TryParse(version, out var parsed)
            ? parsed
            : new Version();
    }
}
