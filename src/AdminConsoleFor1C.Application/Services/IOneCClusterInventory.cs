using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Application.Services;

public interface IOneCClusterInventory
{
    Task<OneCClusterInventoryResult> GetClustersAsync(
        string racPath,
        string administrationServerAddress,
        CancellationToken cancellationToken = default);

    Task<OneCClusterInventoryResult> GetAgentClustersAsync(
        OneCAgentEndpoint agent,
        CancellationToken cancellationToken = default);

    Task<OneCInfobaseSummaryInfo?> GetInfobaseDetailsAsync(
        string racPath,
        string administrationServerAddress,
        string clusterUuid,
        OneCInfobaseSummaryInfo summary,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OneCSessionInfo>> GetInfobaseSessionsAsync(
        string racPath,
        string administrationServerAddress,
        string clusterUuid,
        string infobaseUuid,
        CancellationToken cancellationToken = default);

    Task<OneCClusterCommandResult> CreateInfobaseAsync(
        string racPath,
        OneCInfobaseCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<OneCClusterCommandResult> CreateInfobaseRegistrationAsync(
        string racPath,
        OneCInfobaseTransferPlan plan,
        CancellationToken cancellationToken = default);

    Task<OneCClusterCommandResult> UpdateInfobaseRestrictionsAsync(
        string racPath,
        OneCInfobaseRestrictionsUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<OneCClusterCommandResult> TerminateInfobaseSessionsAsync(
        string racPath,
        string administrationServerAddress,
        string clusterUuid,
        string infobaseUuid,
        string message,
        CancellationToken cancellationToken = default);

    Task<OneCClusterCommandResult> DropInfobaseRegistrationAsync(
        string racPath,
        string administrationServerAddress,
        string clusterUuid,
        string infobaseUuid,
        bool dropDatabase = false,
        CancellationToken cancellationToken = default);
}
