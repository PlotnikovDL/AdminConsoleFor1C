using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Application.Services;

public interface IOneCServiceController
{
    Task RegisterAsync(OneCServiceRegistrationRequest request, string? password = null, CancellationToken cancellationToken = default);

    Task StartAsync(string serviceName, CancellationToken cancellationToken = default);

    Task StopAsync(string serviceName, CancellationToken cancellationToken = default);

    Task RestartAsync(string serviceName, CancellationToken cancellationToken = default);

    Task DeleteAsync(OneCServiceDeletionRequest request, CancellationToken cancellationToken = default);
}
