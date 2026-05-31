using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Application.Services;

public interface IOneCServiceInventory
{
    Task<IReadOnlyList<OneCServiceInfo>> GetServicesAsync(CancellationToken cancellationToken = default);
}
