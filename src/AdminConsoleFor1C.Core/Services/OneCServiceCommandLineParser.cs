using System.Text.RegularExpressions;

namespace AdminConsoleFor1C.Core.Services;

public static partial class OneCServiceCommandLineParser
{
    public static OneCServiceCommandLine Parse(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return new OneCServiceCommandLine(null, string.Empty);
        }

        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuoteIndex = trimmed.IndexOf('"', 1);
            if (closingQuoteIndex > 0)
            {
                return new OneCServiceCommandLine(
                    trimmed[1..closingQuoteIndex],
                    trimmed[(closingQuoteIndex + 1)..].Trim());
            }
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            var executableEnd = exeIndex + ".exe".Length;
            return new OneCServiceCommandLine(
                trimmed[..executableEnd],
                trimmed[executableEnd..].Trim());
        }

        return new OneCServiceCommandLine(null, trimmed);
    }

    public static string? GetOptionValue(string? arguments, string optionName)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        var match = OptionRegex(optionName).Match(arguments);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["quoted"].Success
            ? match.Groups["quoted"].Value
            : match.Groups["value"].Value;
    }

    public static string? GetVersionFromExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var match = VersionInPathRegex().Match(executablePath);
        return match.Success ? match.Groups["version"].Value : null;
    }

    public static OneCServiceKind GetKind(string? serviceName, string? displayName, string? executablePath)
    {
        var executableName = Path.GetFileName(executablePath ?? string.Empty);
        if (executableName.Equals("ragent.exe", StringComparison.OrdinalIgnoreCase))
        {
            return OneCServiceKind.ServerAgent;
        }

        if (executableName.Equals("ras.exe", StringComparison.OrdinalIgnoreCase))
        {
            return OneCServiceKind.AdministrationServer;
        }

        if (executableName.Equals("dbgs.exe", StringComparison.OrdinalIgnoreCase))
        {
            return OneCServiceKind.DebugServer;
        }

        return OneCServiceKind.Unknown;
    }

    public static bool IsProbablyOneCService(string? serviceName, string? displayName, string? executablePath)
    {
        return ContainsOneCMarker(serviceName)
            || ContainsOneCMarker(displayName)
            || ContainsOneCPathMarker(executablePath);
    }

    private static bool ContainsOneCMarker(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.Contains("1C", StringComparison.OrdinalIgnoreCase)
                || value.Contains("1С", StringComparison.OrdinalIgnoreCase)
                || value.Contains("1cv8", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsOneCPathMarker(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.Contains(@"\1cv8\", StringComparison.OrdinalIgnoreCase)
                || value.Contains("/1cv8/", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ragent.exe", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ras.exe", StringComparison.OrdinalIgnoreCase)
                || value.Contains("dbgs.exe", StringComparison.OrdinalIgnoreCase));
    }

    private static Regex OptionRegex(string optionName)
    {
        var escapedName = Regex.Escape(optionName);
        return new Regex(
            $@"(?ix)(?:^|\s)(?:/|-{{1,2}}){escapedName}(?:\s+|[:=])(?:""(?<quoted>[^""]*)""|(?<value>\S+))",
            RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"[\\/](?<version>\d+\.\d+\.\d+\.\d+)[\\/]bin[\\/]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionInPathRegex();
}
