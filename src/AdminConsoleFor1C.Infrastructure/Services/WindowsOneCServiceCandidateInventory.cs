using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

public sealed class WindowsOneCServiceCandidateInventory : IOneCServiceCandidateInventory
{
    private readonly IOneCAdministrationToolInventory toolInventory;

    public WindowsOneCServiceCandidateInventory()
        : this(new WindowsOneCAdministrationToolInventory())
    {
    }

    public WindowsOneCServiceCandidateInventory(IOneCAdministrationToolInventory toolInventory)
    {
        this.toolInventory = toolInventory;
    }

    public async Task<IReadOnlyList<OneCServerAgentServiceCandidate>> GetServerAgentCandidatesAsync(
        IReadOnlyCollection<OneCServiceInfo> existingServices,
        CancellationToken cancellationToken = default)
    {
        var knownExecutablePaths = existingServices
            .Select(static service => service.ExecutablePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .ToArray();

        var tools = await toolInventory.GetToolsAsync(knownExecutablePaths, cancellationToken);
        return OneCServiceCandidatePlanner.BuildServerAgentCandidates(tools, existingServices);
    }
}
