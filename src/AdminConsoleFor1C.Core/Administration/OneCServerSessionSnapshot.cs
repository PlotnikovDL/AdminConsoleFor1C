namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCServerSessionSnapshot(
    IReadOnlyList<OneCClusterInfo> Clusters, DateTimeOffset UpdatedAt);

public sealed record OneCSessionTarget(Guid ConnectionId, string ClusterUuid, OneCSessionInfo Session)
{
    public void Validate(OneCServerConnectionProfile profile)
    {
        if (ConnectionId != profile.Id || !Guid.TryParse(ClusterUuid, out _)
            || !Guid.TryParse(Session.Uuid, out _) || !Guid.TryParse(Session.InfobaseUuid, out _))
            throw new ArgumentException("Сеанс не относится к выбранному подключению или не имеет корректного идентификатора.");
    }

    public bool Matches(OneCSessionInfo current) => Session.Uuid == current.Uuid
        && Session.InfobaseUuid == current.InfobaseUuid && Session.UserName == current.UserName
        && Session.StartedAt == current.StartedAt && Session.Host == current.Host
        && Session.Application == current.Application;
}

public sealed record OneCSessionTerminationResult(string SessionUuid, bool Success, string Message);
