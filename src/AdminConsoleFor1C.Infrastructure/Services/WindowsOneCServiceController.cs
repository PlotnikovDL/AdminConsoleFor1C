using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using AdminConsoleFor1C.Application.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsOneCServiceController : IOneCServiceController
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceDeleteAccess = 0x00010000;

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
            using var service = OpenServiceController(serviceName);

            StopService(service, cancellationToken);
            StartService(service, cancellationToken);
        }, cancellationToken);
    }

    public Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => DeleteServiceByName(serviceName, cancellationToken), cancellationToken);
    }

    private static void StartService(string serviceName, CancellationToken cancellationToken)
    {
        using var service = OpenServiceController(serviceName);
        StartService(service, cancellationToken);
    }

    private static void StopService(string serviceName, CancellationToken cancellationToken)
    {
        using var service = OpenServiceController(serviceName);
        StopService(service, cancellationToken);
    }

    private static ServiceController OpenServiceController(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Не указано имя службы Windows.", nameof(serviceName));
        }

        return new ServiceController(serviceName);
    }

    private static void DeleteServiceByName(string serviceName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Не указано имя службы Windows.", nameof(serviceName));
        }

        using (var service = OpenServiceController(serviceName))
        {
            StopService(service, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var serviceControlManager = NativeMethods.OpenSCManager(
            lpMachineName: null,
            lpDatabaseName: null,
            dwDesiredAccess: ScManagerConnect);
        if (serviceControlManager == IntPtr.Zero)
        {
            throw CreateLastWin32Exception();
        }

        try
        {
            var service = NativeMethods.OpenService(
                hSCManager: serviceControlManager,
                lpServiceName: serviceName,
                dwDesiredAccess: ServiceDeleteAccess);
            if (service == IntPtr.Zero)
            {
                throw CreateLastWin32Exception();
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!NativeMethods.DeleteService(service))
                {
                    throw CreateLastWin32Exception();
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(serviceControlManager);
        }
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

    private static Win32Exception CreateLastWin32Exception()
    {
        return new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenSCManager(
            string? lpMachineName,
            string? lpDatabaseName,
            uint dwDesiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenService(
            IntPtr hSCManager,
            string lpServiceName,
            uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteService(IntPtr hService);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseServiceHandle(IntPtr hSCObject);
    }
}
