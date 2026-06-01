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
            var kind = OneCServiceCommandLineParser.GetKind(name, displayName, commandLine.ExecutablePath);

            if (!OneCServiceCommandLineParser.IsProbablyOneCService(name, displayName, commandLine.ExecutablePath))
            {
                continue;
            }

            var servicePort = GetInt32Option(commandLine.Arguments, "port");

            services.Add(new OneCServiceInfo
            {
                Name = name,
                DisplayName = displayName,
                Kind = kind,
                State = GetString(service, "State") ?? "Unknown",
                Status = GetString(service, "Status") ?? "Unknown",
                StartMode = GetString(service, "StartMode"),
                Account = GetString(service, "StartName"),
                ProcessId = GetUInt32(service, "ProcessId"),
                ExecutablePath = commandLine.ExecutablePath,
                Arguments = commandLine.Arguments,
                Version = OneCServiceCommandLineParser.GetVersionFromExecutablePath(commandLine.ExecutablePath),
                AgentPort = kind == OneCServiceKind.ServerAgent ? servicePort : null,
                RegPort = kind == OneCServiceKind.ServerAgent ? GetInt32Option(commandLine.Arguments, "regport") : null,
                AdministrationServerPort = kind == OneCServiceKind.AdministrationServer ? servicePort : null,
                DebugServerPort = GetDebugServerPort(kind, commandLine.Arguments, servicePort),
                PortRange = kind == OneCServiceKind.ServerAgent
                    ? OneCServiceCommandLineParser.GetOptionValue(commandLine.Arguments, "range")
                    : null,
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

    private static int? GetDebugServerPort(OneCServiceKind kind, string arguments, int? servicePort)
    {
        var ragentDebugPort = GetInt32Option(arguments, "debugserverport");
        if (ragentDebugPort is not null)
        {
            return ragentDebugPort;
        }

        return kind == OneCServiceKind.DebugServer ? servicePort : null;
    }
}
