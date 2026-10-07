using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Application.Services;

public sealed record OneCProcessInventorySnapshot(
    IReadOnlyList<OneCProcessInfo> Processes,
    bool IsServiceCorrelationAvailable,
    string? ServiceCorrelationError);

public sealed class OneCProcessInventoryReader(
    IOneCProcessInventory processInventory,
    IOneCServiceInventory serviceInventory)
{
    public async Task<OneCProcessInventorySnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var processesTask = processInventory.GetProcessesAsync(cancellationToken);
        var servicesTask = ReadServicesAsync(cancellationToken);
        await Task.WhenAll(processesTask, servicesTask);
        var processes = await processesTask;
        var (services, error) = await servicesTask;

        return services is null
            ? new(processes, false, error)
            : new(OneCServiceProcessCorrelator.Correlate(services, processes), true, null);
    }

    private async Task<(IReadOnlyList<OneCServiceInfo>? Services, string? Error)> ReadServicesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return (await serviceInventory.GetServicesAsync(cancellationToken), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return (null, exception.Message);
        }
    }
}
