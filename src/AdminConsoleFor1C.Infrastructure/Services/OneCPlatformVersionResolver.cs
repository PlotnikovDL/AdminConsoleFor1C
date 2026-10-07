using System.ComponentModel;
using System.Diagnostics;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

public static class OneCPlatformVersionResolver
{
    public static string? ResolveExecutableVersion(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        executablePath = executablePath.Trim();

        try
        {
            if (File.Exists(executablePath))
            {
                var version = FileVersionInfo.GetVersionInfo(executablePath);
                if (version.FileMajorPart > 0)
                {
                    return $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}";
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            // A protected or unavailable executable can still have a version in its installation path.
        }

        return OneCServiceCommandLineParser.GetVersionFromExecutablePath(executablePath);
    }
}
