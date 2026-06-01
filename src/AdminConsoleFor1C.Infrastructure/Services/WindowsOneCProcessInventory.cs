using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsOneCProcessInventory : IOneCProcessInventory
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AddressFamilyInterNetwork = 2;

    public Task<IReadOnlyList<OneCProcessInfo>> GetProcessesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GetProcesses(cancellationToken), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<OneCProcessInfo> GetProcesses(CancellationToken cancellationToken)
    {
        var processes = new List<OneCProcessInfo>();
        var listeningTcpPortsByProcessId = GetListeningTcpPortsByProcessId();

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
            var processId = GetUInt32(process, "ProcessId") ?? 0;

            processes.Add(new OneCProcessInfo
            {
                Name = name,
                Kind = kind,
                ProcessId = processId,
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
                WorkerPort = kind == OneCProcessKind.WorkerProcess
                    ? Prefer(GetFirstListeningTcpPort(listeningTcpPortsByProcessId, processId), port)
                    : null,
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

    private static int? Prefer(int? current, int? fallback)
    {
        return current ?? fallback;
    }

    private static int? GetFirstListeningTcpPort(IReadOnlyDictionary<uint, IReadOnlyList<int>> portsByProcessId, uint processId)
    {
        return portsByProcessId.TryGetValue(processId, out var ports) && ports.Count > 0
            ? ports[0]
            : null;
    }

    private static IReadOnlyDictionary<uint, IReadOnlyList<int>> GetListeningTcpPortsByProcessId()
    {
        var portsByProcessId = new Dictionary<uint, List<int>>();
        var bufferLength = 0;
        var result = GetExtendedTcpTable(
            IntPtr.Zero,
            ref bufferLength,
            sort: true,
            ipVersion: AddressFamilyInterNetwork,
            tblClass: TcpTableClass.OwnerPidListener);

        if (result != ErrorInsufficientBuffer || bufferLength <= 0)
        {
            return ToReadOnlyDictionary(portsByProcessId);
        }

        var buffer = Marshal.AllocHGlobal(bufferLength);
        try
        {
            result = GetExtendedTcpTable(
                buffer,
                ref bufferLength,
                sort: true,
                ipVersion: AddressFamilyInterNetwork,
                tblClass: TcpTableClass.OwnerPidListener);

            if (result != 0)
            {
                return ToReadOnlyDictionary(portsByProcessId);
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();

            for (var index = 0; index < rowCount; index++)
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(rowPointer);
                var port = ConvertNetworkPort(row.LocalPort);

                if (port > 0)
                {
                    if (!portsByProcessId.TryGetValue(row.OwningPid, out var ports))
                    {
                        ports = [];
                        portsByProcessId[row.OwningPid] = ports;
                    }

                    if (!ports.Contains(port))
                    {
                        ports.Add(port);
                    }
                }

                rowPointer = IntPtr.Add(rowPointer, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return ToReadOnlyDictionary(portsByProcessId);
    }

    private static Dictionary<uint, IReadOnlyList<int>> ToReadOnlyDictionary(Dictionary<uint, List<int>> portsByProcessId)
    {
        return portsByProcessId.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<int>)pair.Value.Order().ToList());
    }

    private static int ConvertNetworkPort(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        return (bytes[0] << 8) | bytes[1];
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int tcpTableLength,
        bool sort,
        int ipVersion,
        TcpTableClass tblClass,
        uint reserved = 0);

    private enum TcpTableClass
    {
        OwnerPidListener = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TcpRowOwnerPid
    {
        public readonly uint State;
        public readonly uint LocalAddress;
        public readonly uint LocalPort;
        public readonly uint RemoteAddress;
        public readonly uint RemotePort;
        public readonly uint OwningPid;
    }
}
