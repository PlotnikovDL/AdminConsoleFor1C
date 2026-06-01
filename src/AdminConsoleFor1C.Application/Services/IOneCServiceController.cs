namespace AdminConsoleFor1C.Application.Services;

public interface IOneCServiceController
{
    Task StartAsync(string serviceName, CancellationToken cancellationToken = default);

    Task StopAsync(string serviceName, CancellationToken cancellationToken = default);

    Task RestartAsync(string serviceName, CancellationToken cancellationToken = default);
}
