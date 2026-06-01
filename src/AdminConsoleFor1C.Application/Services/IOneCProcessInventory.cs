using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Application.Services;

public interface IOneCProcessInventory
{
    Task<IReadOnlyList<OneCProcessInfo>> GetProcessesAsync(CancellationToken cancellationToken = default);
}
