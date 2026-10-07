namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCInfobaseEditTarget(Guid ConnectionId, OneCClusterInfo Cluster, string InfobaseUuid);
