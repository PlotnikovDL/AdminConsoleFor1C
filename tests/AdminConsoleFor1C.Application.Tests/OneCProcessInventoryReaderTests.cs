using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Application.Tests;

public sealed class OneCProcessInventoryReaderTests
{
    [Fact]
    public async Task PreservesProcessesAndReportsUnavailableCorrelationWhenServiceInventoryFails()
    {
        var reader = new OneCProcessInventoryReader(new ProcessInventory(),
            new ServiceInventory(() => Task.FromException<IReadOnlyList<OneCServiceInfo>>(new UnauthorizedAccessException("Service inventory denied"))));

        var snapshot = await reader.ReadAsync();

        Assert.Equal(10u, Assert.Single(snapshot.Processes).ProcessId);
        Assert.False(snapshot.IsServiceCorrelationAvailable);
        Assert.Equal("Service inventory denied", snapshot.ServiceCorrelationError);
    }

    [Fact]
    public async Task DistinguishesNoServicesFromUnavailableServiceInventory()
    {
        var reader = new OneCProcessInventoryReader(new ProcessInventory(), new ServiceInventory(() => Task.FromResult<IReadOnlyList<OneCServiceInfo>>([])));

        var snapshot = await reader.ReadAsync();

        Assert.Single(snapshot.Processes);
        Assert.True(snapshot.IsServiceCorrelationAvailable);
        Assert.Null(snapshot.ServiceCorrelationError);
        Assert.Null(snapshot.Processes[0].RelatedServiceName);
    }

    [Fact]
    public async Task CorrelatesSuccessfullyReadProcessesAndServices()
    {
        var service = new OneCServiceInfo
        {
            Name = "service-name", DisplayName = "Отображаемое имя", ProcessId = 10,
            State = "Running", Status = "OK", Kind = OneCServiceKind.ServerAgent
        };
        var reader = new OneCProcessInventoryReader(new ProcessInventory(),
            new ServiceInventory(() => Task.FromResult<IReadOnlyList<OneCServiceInfo>>([service])));

        var snapshot = await reader.ReadAsync();

        Assert.True(snapshot.IsServiceCorrelationAvailable);
        Assert.Equal("service-name", Assert.Single(snapshot.Processes).RelatedServiceName);
    }

    [Fact]
    public async Task KeepsProcessInventoryFailureAsTheActualErrorWhenBothInventoriesFail()
    {
        var processError = new InvalidOperationException("Process inventory failed");
        var reader = new OneCProcessInventoryReader(new ProcessInventory(processError),
            new ServiceInventory(() => Task.FromException<IReadOnlyList<OneCServiceInfo>>(new UnauthorizedAccessException("Service inventory denied"))));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync());

        Assert.Same(processError, error);
    }

    [Fact]
    public async Task DoesNotConvertRequestedCancellationIntoUnavailableCorrelation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reader = new OneCProcessInventoryReader(new ProcessInventory(),
            new ServiceInventory(() => Task.FromCanceled<IReadOnlyList<OneCServiceInfo>>(cancellation.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cancellation.Token));
    }

    private sealed class ProcessInventory(Exception? error = null) : IOneCProcessInventory
    {
        public Task<IReadOnlyList<OneCProcessInfo>> GetProcessesAsync(CancellationToken cancellationToken = default)
            => error is not null
                ? Task.FromException<IReadOnlyList<OneCProcessInfo>>(error)
                : Task.FromResult<IReadOnlyList<OneCProcessInfo>>([new() { Name = "ragent.exe", ProcessId = 10, Kind = OneCProcessKind.ServerAgent }]);
    }

    private sealed class ServiceInventory(Func<Task<IReadOnlyList<OneCServiceInfo>>> read) : IOneCServiceInventory
    {
        public Task<IReadOnlyList<OneCServiceInfo>> GetServicesAsync(CancellationToken cancellationToken = default) => read();
    }
}
