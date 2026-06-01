using System.Runtime.Versioning;
using System.ServiceProcess;
using AdminConsoleFor1C.Application.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsOneCServiceController : IOneCServiceController
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public Task StartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => StartService(serviceName, cancellationToken), cancellationToken);
    }

    public Task StopAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => StopService(serviceName, cancellationToken), cancellationToken);
    }

    public Task RestartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var service = OpenService(serviceName);

            StopService(service, cancellationToken);
            StartService(service, cancellationToken);
        }, cancellationToken);
    }

    private static void StartService(string serviceName, CancellationToken cancellationToken)
    {
        using var service = OpenService(serviceName);
        StartService(service, cancellationToken);
    }

    private static void StopService(string serviceName, CancellationToken cancellationToken)
    {
        using var service = OpenService(serviceName);
        StopService(service, cancellationToken);
    }

    private static ServiceController OpenService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Не указано имя службы Windows.", nameof(serviceName));
        }

        return new ServiceController(serviceName);
    }

    private static void StartService(ServiceController service, CancellationToken cancellationToken)
    {
        service.Refresh();

        if (service.Status == ServiceControllerStatus.Running)
        {
            return;
        }

        if (service.Status == ServiceControllerStatus.StartPending)
        {
            WaitForStatus(service, ServiceControllerStatus.Running, cancellationToken);
            return;
        }

        if (service.Status == ServiceControllerStatus.StopPending)
        {
            WaitForStatus(service, ServiceControllerStatus.Stopped, cancellationToken);
        }

        service.Start();
        WaitForStatus(service, ServiceControllerStatus.Running, cancellationToken);
    }

    private static void StopService(ServiceController service, CancellationToken cancellationToken)
    {
        service.Refresh();

        if (service.Status == ServiceControllerStatus.Stopped)
        {
            return;
        }

        if (service.Status == ServiceControllerStatus.StopPending)
        {
            WaitForStatus(service, ServiceControllerStatus.Stopped, cancellationToken);
            return;
        }

        if (!service.CanStop)
        {
            throw new InvalidOperationException("Служба не поддерживает остановку через диспетчер служб Windows.");
        }

        service.Stop();
        WaitForStatus(service, ServiceControllerStatus.Stopped, cancellationToken);
    }

    private static void WaitForStatus(
        ServiceController service,
        ServiceControllerStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(CommandTimeout);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Refresh();

            if (service.Status == expectedStatus)
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new System.TimeoutException($"Служба не перешла в состояние {expectedStatus} за {CommandTimeout.TotalSeconds:0} секунд.");
            }

            Thread.Sleep(PollInterval);
        }
    }
}
