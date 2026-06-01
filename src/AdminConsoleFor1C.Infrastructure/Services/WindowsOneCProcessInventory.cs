using System.Management;
using System.Runtime.Versioning;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsOneCProcessInventory : IOneCProcessInventory
{
    public Task<IReadOnlyList<OneCProcessInfo>> GetProcessesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GetProcesses(cancellationToken), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<OneCProcessInfo> GetProcesses(CancellationToken cancellationToken)
    {
        var processes = new List<OneCProcessInfo>();

        using var searcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT Name, ProcessId, ParentProcessId, ExecutablePath, CommandLine FROM Win32_Process");

        foreach (ManagementObject process in searcher.Get().Cast<ManagementObject>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = GetString(process, "Name") ?? string.Empty;
            var kind = GetKind(name);
            if (kind == OneCProcessKind.Unknown)
            {
                continue;
            }

            var rawCommandLine = GetString(process, "CommandLine");
            var executablePath = GetString(process, "ExecutablePath");
            var commandLine = OneCServiceCommandLineParser.Parse(rawCommandLine ?? executablePath);
            var arguments = commandLine.Arguments;
            var port = GetInt32Option(arguments, "port");

            processes.Add(new OneCProcessInfo
            {
                Name = name,
                Kind = kind,
                ProcessId = GetUInt32(process, "ProcessId") ?? 0,
                ParentProcessId = GetUInt32(process, "ParentProcessId"),
                ExecutablePath = commandLine.ExecutablePath ?? executablePath,
                Arguments = arguments,
                Version = OneCServiceCommandLineParser.GetVersionFromExecutablePath(commandLine.ExecutablePath ?? executablePath),
                Owner = GetOwner(process),
                AgentPort = kind == OneCProcessKind.ServerAgent ? port : null,
                ClusterPort = kind switch
                {
                    OneCProcessKind.ServerAgent => GetInt32Option(arguments, "regport"),
                    OneCProcessKind.ClusterManager => port,
                    _ => null
                },
                WorkerPort = kind == OneCProcessKind.WorkerProcess ? port : null,
                AdministrationServerPort = kind == OneCProcessKind.AdministrationServer ? port : null,
                DebugServerPort = GetDebugServerPort(kind, arguments, port),
                PortRange = kind == OneCProcessKind.ServerAgent
                    ? OneCServiceCommandLineParser.GetOptionValue(arguments, "range")
                    : null,
                DataDirectory = OneCServiceCommandLineParser.GetOptionValue(arguments, "d"),
                RawCommandLine = rawCommandLine
            });
        }

        return processes
            .OrderBy(static process => process.Kind)
            .ThenBy(static process => process.Version)
            .ThenBy(static process => process.ProcessId)
            .ToList();
    }

    private static OneCProcessKind GetKind(string processName)
    {
        return processName.ToLowerInvariant() switch
        {
            "ragent.exe" => OneCProcessKind.ServerAgent,
            "rmngr.exe" => OneCProcessKind.ClusterManager,
            "rphost.exe" => OneCProcessKind.WorkerProcess,
            "ras.exe" => OneCProcessKind.AdministrationServer,
            "dbgs.exe" => OneCProcessKind.DebugServer,
            _ => OneCProcessKind.Unknown
        };
    }

    [SupportedOSPlatform("windows")]
    private static string? GetString(ManagementBaseObject process, string propertyName)
    {
        try
        {
            return process[propertyName]?.ToString();
        }
        catch (Exception exception) when (IsExpectedWmiAccessException(exception))
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static uint? GetUInt32(ManagementBaseObject process, string propertyName)
    {
        try
        {
            return process[propertyName] switch
            {
                uint value => value,
                int value when value >= 0 => (uint)value,
                IConvertible value => Convert.ToUInt32(value),
                _ => null
            };
        }
        catch (Exception exception) when (IsExpectedWmiAccessException(exception) || exception is FormatException or OverflowException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? GetOwner(ManagementObject process)
    {
        try
        {
            object[] ownerInfo = [string.Empty, string.Empty];
            var result = Convert.ToUInt32(process.InvokeMethod("GetOwner", ownerInfo));
            if (result != 0)
            {
                return null;
            }

            var user = ownerInfo[0]?.ToString();
            var domain = ownerInfo[1]?.ToString();
            return string.IsNullOrWhiteSpace(domain) ? user : $@"{domain}\{user}";
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsExpectedWmiAccessException(Exception exception)
    {
        return exception is ManagementException
            or InvalidOperationException
            or UnauthorizedAccessException
            or NotSupportedException;
    }

    private static int? GetInt32Option(string arguments, string optionName)
    {
        return int.TryParse(OneCServiceCommandLineParser.GetOptionValue(arguments, optionName), out var value)
            ? value
            : null;
    }

    private static int? GetDebugServerPort(OneCProcessKind kind, string arguments, int? port)
    {
        var ragentDebugPort = GetInt32Option(arguments, "debugserverport");
        if (ragentDebugPort is not null)
        {
            return ragentDebugPort;
        }

        return kind == OneCProcessKind.DebugServer ? port : null;
    }
}
