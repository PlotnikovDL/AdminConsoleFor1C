using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Application.Services;

public interface IOneCServiceCandidateInventory
{
    Task<IReadOnlyList<OneCServerAgentServiceCandidate>> GetServerAgentCandidatesAsync(
        IReadOnlyCollection<OneCServiceInfo> existingServices,
        CancellationToken cancellationToken = default);
}
