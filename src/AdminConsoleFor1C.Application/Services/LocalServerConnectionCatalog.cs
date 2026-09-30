using System.Security.Cryptography;
using System.Text;
using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Application.Services;

public sealed record ServerConnectionEntry(OneCServerConnectionProfile Profile, bool IsDiscovered, OneCServiceInfo? LocalService);

// Discovery never writes connection settings or replaces saved credentials.
public sealed class LocalServerConnectionCatalog(IOneCServiceInventory inventory, string machineName)
{
    public async Task<IReadOnlyList<ServerConnectionEntry>> ReadAsync(
        IReadOnlyList<OneCServerConnectionProfile> saved, CancellationToken cancellationToken = default)
        => Merge(saved, await inventory.GetServicesAsync(cancellationToken), machineName);

    public static IReadOnlyList<ServerConnectionEntry> Merge(IReadOnlyList<OneCServerConnectionProfile> saved,
        IReadOnlyList<OneCServiceInfo> services, string machineName)
    {
        var agents = services.Where(s => s.Kind == OneCServiceKind.ServerAgent
            && !string.IsNullOrWhiteSpace(s.ExecutablePath)
            && Version.TryParse(s.Version, out var version) && version.Revision >= 0
            && (s.AgentPort ?? 1540) is >= 1 and <= 65535).ToArray();
        var result = saved.Select(p => new ServerConnectionEntry(p, false,
            agents.FirstOrDefault(s => Matches(p, s, machineName)))).ToList();
        foreach (var service in agents.OrderBy(s => s.Version).ThenBy(s => s.AgentPort))
        {
            if (result.Any(e => Matches(e.Profile, service, machineName))) continue;
            var port = service.AgentPort ?? 1540;
            var identity = $"{machineName}\n{service.Name}\n{port}\n{service.Version}".ToUpperInvariant();
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
            var directory = Path.GetDirectoryName(service.ExecutablePath!);
            if (string.IsNullOrWhiteSpace(directory)) continue;
            result.Add(new(new OneCServerConnectionProfile
            {
                Id = id, Name = $"Этот компьютер · {service.Version}", Host = "localhost", AgentPort = port,
                PlatformDirectory = directory, PlatformVersion = service.Version!
            }, true, service));
        }
        return result;
    }

    private static bool Matches(OneCServerConnectionProfile profile, OneCServiceInfo service, string machineName)
        => IsLocalHost(profile.Host, machineName) && profile.AgentPort == (service.AgentPort ?? 1540)
            && profile.PlatformVersion == service.Version;

    private static bool IsLocalHost(string host, string machineName)
        => host.Equals(machineName, StringComparison.OrdinalIgnoreCase)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1";
}
