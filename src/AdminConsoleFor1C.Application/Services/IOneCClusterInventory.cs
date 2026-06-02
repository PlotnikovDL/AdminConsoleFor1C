using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Application.Services;

public interface IOneCClusterInventory
{
    Task<OneCClusterInventoryResult> GetClustersAsync(
        string racPath,
        string administrationServerAddress,
        CancellationToken cancellationToken = default);
}
