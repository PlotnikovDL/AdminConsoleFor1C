using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Application.Services;

public interface IOneCAdministrationToolInventory
{
    Task<IReadOnlyList<OneCAdministrationToolInfo>> GetToolsAsync(
        IReadOnlyCollection<string> knownExecutablePaths,
        CancellationToken cancellationToken = default);
}
