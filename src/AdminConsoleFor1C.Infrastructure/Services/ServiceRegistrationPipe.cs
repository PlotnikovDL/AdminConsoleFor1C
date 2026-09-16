using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using AdminConsoleFor1C.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace AdminConsoleFor1C.Infrastructure.Services;

// Credentials exist only in the connected processes and an authenticated local pipe.
// Never put this payload in command-line arguments, result files, or log messages.
[SupportedOSPlatform("windows")]
public static class ServiceRegistrationPipe
{
    private const int MaximumPayloadLength = 128 * 1024;

    public static NamedPipeServerStream CreateServer(out string pipeName)
    {
        pipeName = $"AdminConsoleFor1C-Registration-{Guid.NewGuid():N}";
        using var identity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security);
    }

    public static async Task SendAsync(NamedPipeServerStream pipe, int expectedWorkerId,
        OneCServiceRegistrationRequest request, string? password, CancellationToken cancellationToken)
    {
        await pipe.WaitForConnectionAsync(cancellationToken);
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientId))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (clientId != expectedWorkerId)
            throw new InvalidOperationException("К каналу регистрации подключился неожиданный процесс.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(new ServiceRegistrationPayload { Request = request, Password = password });
        try
        {
            if (payload.Length > MaximumPayloadLength)
                throw new InvalidOperationException("Параметры регистрации превышают допустимый размер.");
            var length = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
            await pipe.WriteAsync(length, cancellationToken);
            await pipe.WriteAsync(payload, cancellationToken);
            await pipe.FlushAsync(cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static async Task<ServiceRegistrationPayload> ReceiveAsync(string pipeName, int expectedParentId,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(timeout.Token);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverId))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (serverId != expectedParentId)
            throw new InvalidOperationException("Источник параметров регистрации не совпадает с процессом приложения.");

        var lengthBytes = new byte[sizeof(int)];
        await pipe.ReadExactlyAsync(lengthBytes, timeout.Token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is <= 0 or > MaximumPayloadLength)
            throw new InvalidOperationException("Некорректный размер параметров регистрации.");
        var payload = new byte[length];
        try
        {
            await pipe.ReadExactlyAsync(payload, timeout.Token);
            return JsonSerializer.Deserialize<ServiceRegistrationPayload>(payload)
                ?? throw new InvalidOperationException("Не получены параметры регистрации.");
        }
        catch (JsonException)
        {
            // Avoid including fragments of a credential-bearing JSON payload in diagnostics.
            throw new InvalidOperationException("Не удалось прочитать параметры регистрации.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}

// A class deliberately avoids the credential-printing ToString generated for records.
public sealed class ServiceRegistrationPayload
{
    public required OneCServiceRegistrationRequest Request { get; init; }
    public string? Password { get; set; }
}
