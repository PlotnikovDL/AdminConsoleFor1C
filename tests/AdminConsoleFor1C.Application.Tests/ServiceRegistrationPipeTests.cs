using System.Runtime.Versioning;
using AdminConsoleFor1C.Core.Services;
using AdminConsoleFor1C.Infrastructure.Services;

namespace AdminConsoleFor1C.Application.Tests;

[SupportedOSPlatform("windows")]
public sealed class ServiceRegistrationPipeTests
{
    [Fact]
    public async Task SendsCredentialsOnlyToExpectedProcess()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = ServiceRegistrationPipe.CreateServer(out var name);
        var request = new OneCServiceRegistrationRequest { ServiceName = "test-registration" };
        var receive = ServiceRegistrationPipe.ReceiveAsync(name, Environment.ProcessId, timeout.Token);
        await ServiceRegistrationPipe.SendAsync(server, Environment.ProcessId, request, "test-only-Ж-123", timeout.Token);
        var received = await receive;
        Assert.Equal(request.ServiceName, received.Request.ServiceName);
        Assert.Equal("test-only-Ж-123", received.Password);
        Assert.DoesNotContain("test-only-Ж-123", received.ToString());
        received.Password = null;
    }

    [Fact]
    public async Task RefusesCredentialTransferToUnexpectedClient()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = ServiceRegistrationPipe.CreateServer(out var name);
        var receive = ServiceRegistrationPipe.ReceiveAsync(name, Environment.ProcessId, timeout.Token);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ServiceRegistrationPipe.SendAsync(
            server, int.MaxValue, new OneCServiceRegistrationRequest(), "never-send-this", timeout.Token));
        Assert.DoesNotContain("never-send-this", exception.ToString());
        server.Dispose();
        await Assert.ThrowsAnyAsync<IOException>(async () => await receive);
    }

    [Fact]
    public async Task RefusesParametersFromUnexpectedServer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = ServiceRegistrationPipe.CreateServer(out var name);
        var receive = ServiceRegistrationPipe.ReceiveAsync(name, int.MaxValue, timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await receive);
    }
}
