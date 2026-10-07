using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.App;

public sealed record ServerClusterFilter(Guid? ServerId, string? ClusterUuid, string Name, string Details, string? Target = null)
{
    public static ServerClusterFilter All { get; } = new(null, null, "Все кластеры", "");
    public bool IsAll => ServerId is null;
    public string SelectionText => IsAll || string.IsNullOrWhiteSpace(Target) ? Name
        : ServerDataScopePresentation.ClusterCaption(Name, Target);
    public override string ToString() => string.IsNullOrEmpty(Details) ? Name : $"{Name} · {Details}";
}

public sealed record ServerInfobaseFilter(Guid? ServerId, string? ClusterUuid, string? InfobaseUuid, string Name, string Details, string? Target = null)
{
    public static ServerInfobaseFilter All { get; } = new(null, null, null, "Все базы", "");
    public bool IsAll => ServerId is null;
    public string SelectionText => IsAll || string.IsNullOrWhiteSpace(Target) ? Name : $"{Name} · {Target}";
    public override string ToString() => string.IsNullOrEmpty(Details) ? Name : $"{Name} · {Details}";
}

public static class ServerDataScopePresentation
{
    public static bool IsDefaultClusterName(string name)
        => name.Trim().Equals("Локальный кластер", StringComparison.OrdinalIgnoreCase)
            || name.Trim().Equals("Local cluster", StringComparison.OrdinalIgnoreCase);
    public static string ClusterCaption(string name, string address)
        => IsDefaultClusterName(name) ? address : $"{address} · {name}";
    public static string ClusterDetails(OneCServerConnectionProfile profile, OneCClusterInfo cluster)
        => $"{ServerConnectionPresentation.DisplayName(profile, Environment.MachineName)} · Агент: {profile.AgentAddress} · Платформа: {profile.PlatformVersion} · Кластер: {cluster.AddressText} · Имя: {cluster.NameText}";
    public static string InfobaseDetails(OneCServerConnectionProfile profile, OneCClusterInfo cluster)
        => $"{cluster.NameText} · {ClusterDetails(profile, cluster)}";
}
