namespace AdminConsoleFor1C.Core.Services;

public static class OneCServiceProcessCorrelator
{
    public static IReadOnlyList<OneCProcessInfo> Correlate(
        IReadOnlyList<OneCServiceInfo> services,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var servicesByProcessId = services
            .Where(static service => service.ProcessId is not null and not 0)
            .GroupBy(static service => service.ProcessId!.Value)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var processesById = processes
            .Where(static process => process.ProcessId != 0)
            .GroupBy(static process => process.ProcessId)
            .ToDictionary(static group => group.Key, static group => group.First());

        return processes.Select(process =>
        {
            var service = FindByProcessId(process, servicesByProcessId, processesById);
            if (service is null)
            {
                var portMatches = services.Where(candidate => IsPortMatch(candidate, process)).Take(2).ToArray();
                service = portMatches.Length == 1 ? portMatches[0] : null;
            }

            return process with
            {
                RelatedServiceName = service?.Name,
                RelatedServiceDisplayName = service?.DisplayNameText
            };
        }).ToArray();
    }

    private static OneCServiceInfo? FindByProcessId(
        OneCProcessInfo process,
        IReadOnlyDictionary<uint, OneCServiceInfo[]> servicesByProcessId,
        IReadOnlyDictionary<uint, OneCProcessInfo> processesById)
    {
        var processId = process.ProcessId;
        var visited = new HashSet<uint>();
        while (processId != 0 && visited.Add(processId))
        {
            if (servicesByProcessId.TryGetValue(processId, out var services))
            {
                return services.Length == 1 ? services[0] : null;
            }

            if (!processesById.TryGetValue(processId, out var parent) || parent.ParentProcessId is null)
            {
                return null;
            }

            processId = parent.ParentProcessId.Value;
        }

        return null;
    }

    private static bool IsPortMatch(OneCServiceInfo service, OneCProcessInfo process)
    {
        if (!string.Equals(service.State, "Running", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(service.Version) && !string.IsNullOrWhiteSpace(process.Version)
                && !string.Equals(service.Version, process.Version, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return service.Kind switch
        {
            OneCServiceKind.ServerAgent => process.Kind switch
            {
                OneCProcessKind.ServerAgent => process.EffectiveAgentPort == service.EffectiveAgentPort,
                OneCProcessKind.ClusterManager => service.RegPort is not null && process.ClusterPort == service.RegPort,
                OneCProcessKind.WorkerProcess => !string.IsNullOrWhiteSpace(service.PortRange)
                    && string.Equals(service.PortRange, process.PortRange, StringComparison.OrdinalIgnoreCase),
                OneCProcessKind.DebugServer => service.DebugServerPort is not null
                    && process.DebugServerPort == service.DebugServerPort,
                _ => false
            },
            OneCServiceKind.AdministrationServer => service.AdministrationServerPort is not null
                && process.AdministrationServerPort == service.AdministrationServerPort,
            OneCServiceKind.DebugServer => service.DebugServerPort is not null
                && process.DebugServerPort == service.DebugServerPort,
            _ => false
        };
    }
}
