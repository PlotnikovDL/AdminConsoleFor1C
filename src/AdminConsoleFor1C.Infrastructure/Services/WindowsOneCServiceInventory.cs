using System.Management;
using System.Runtime.Versioning;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsOneCServiceInventory : IOneCServiceInventory
{
    public Task<IReadOnlyList<OneCServiceInfo>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GetServices(cancellationToken), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<OneCServiceInfo> GetServices(CancellationToken cancellationToken)
    {
        var services = new List<OneCServiceInfo>();

        using var searcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT Name, DisplayName, State, Status, StartMode, StartName, PathName, ProcessId FROM Win32_Service");

        foreach (ManagementObject service in searcher.Get().Cast<ManagementObject>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = GetString(service, "Name") ?? string.Empty;
            var displayName = GetString(service, "DisplayName") ?? name;
            var pathName = GetString(service, "PathName");
            var commandLine = OneCServiceCommandLineParser.Parse(pathName);

            if (!OneCServiceCommandLineParser.IsProbablyOneCService(name, displayName, commandLine.ExecutablePath))
            {
                continue;
            }

            services.Add(new OneCServiceInfo
            {
                Name = name,
                DisplayName = displayName,
                Kind = OneCServiceCommandLineParser.GetKind(name, displayName, commandLine.ExecutablePath),
                State = GetString(service, "State") ?? "Unknown",
                Status = GetString(service, "Status") ?? "Unknown",
                StartMode = GetString(service, "StartMode"),
                Account = GetString(service, "StartName"),
                ProcessId = GetUInt32(service, "ProcessId"),
                ExecutablePath = commandLine.ExecutablePath,
                Arguments = commandLine.Arguments,
                Version = OneCServiceCommandLineParser.GetVersionFromExecutablePath(commandLine.ExecutablePath),
                AgentPort = GetInt32Option(commandLine.Arguments, "port"),
                RegPort = GetInt32Option(commandLine.Arguments, "regport"),
                PortRange = OneCServiceCommandLineParser.GetOptionValue(commandLine.Arguments, "range"),
                DataDirectory = OneCServiceCommandLineParser.GetOptionValue(commandLine.Arguments, "d"),
                RawCommandLine = pathName
            });
        }

        return services
            .OrderBy(static service => service.Kind)
            .ThenBy(static service => service.Version)
            .ThenBy(static service => service.AgentPort)
            .ThenBy(static service => service.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    [SupportedOSPlatform("windows")]
    private static string? GetString(ManagementBaseObject service, string propertyName)
    {
        return service[propertyName]?.ToString();
    }

    [SupportedOSPlatform("windows")]
    private static uint? GetUInt32(ManagementBaseObject service, string propertyName)
    {
        return service[propertyName] is uint value ? value : null;
    }

    private static int? GetInt32Option(string arguments, string optionName)
    {
        return int.TryParse(OneCServiceCommandLineParser.GetOptionValue(arguments, optionName), out var value)
            ? value
            : null;
    }
}
