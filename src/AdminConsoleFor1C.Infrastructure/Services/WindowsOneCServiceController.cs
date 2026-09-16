using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Net.NetworkInformation;
using System.Security.AccessControl;
using System.Security.Principal;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsOneCServiceController : IOneCServiceController
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceDeleteAccess = 0x00010000;

    public async Task RegisterAsync(OneCServiceRegistrationRequest request, string? password = null, CancellationToken cancellationToken = default)
    {
        // Re-read in the elevated process: the UI snapshot may already be stale.
        var services = await new WindowsOneCServiceInventory().GetServicesAsync(cancellationToken);
        var errors = request.Validate(services);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors));
        if (request.Account == OneCServiceAccount.WindowsUser && string.IsNullOrEmpty(password))
            throw new ArgumentException("Укажите пароль учетной записи Windows.");

        await Task.Run(() => RegisterService(request, password, cancellationToken), cancellationToken);
    }

    private static void RegisterService(OneCServiceRegistrationRequest request, string? password, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ExecutablePath))
            throw new FileNotFoundException("Файл ragent.exe не найден.", request.ExecutablePath);
        if (Directory.Exists(request.DataDirectory) || File.Exists(request.DataDirectory))
            throw new InvalidOperationException("Каталог данных уже существует. Укажите новый каталог для новой службы.");

        var occupiedPort = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .FirstOrDefault(endpoint => request.UsesPort(endpoint.Port));
        if (occupiedPort is not null)
            throw new InvalidOperationException($"TCP-порт {occupiedPort.Port} уже занят. Измените порты новой службы.");

        cancellationToken.ThrowIfCancellationRequested();
        using var logon = request.Account == OneCServiceAccount.WindowsUser
            ? WindowsServiceLogon.Prepare(request.UserName!, password!) : null;
        const uint scManagerCreateService = 0x0002;
        const uint serviceQueryStatus = 0x0004;
        const uint serviceWin32OwnProcess = 0x0010;
        const uint serviceErrorNormal = 0x0001;
        var manager = NativeMethods.OpenSCManager(null, null, scManagerCreateService);
        if (manager == IntPtr.Zero)
            throw CreateLastWin32Exception();

        var createdDirectory = false;
        var registered = false;
        try
        {
            // Only a new cluster directory receives permissions; existing data is never reused.
            var directory = new DirectoryInfo(request.DataDirectory);
            Directory.CreateDirectory(directory.Parent!.FullName);
            if (!NativeMethods.CreateDirectory(directory.FullName, IntPtr.Zero))
                throw CreateLastWin32Exception();
            createdDirectory = true;
            var accountSid = logon?.Sid ?? new SecurityIdentifier(request.Account switch
            {
                OneCServiceAccount.NetworkService => WellKnownSidType.NetworkServiceSid,
                OneCServiceAccount.LocalService => WellKnownSidType.LocalServiceSid,
                _ => WellKnownSidType.LocalSystemSid
            }, null);
            var security = directory.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                accountSid,
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(security);

            var service = NativeMethods.CreateService(manager, request.ServiceName, request.DisplayName,
                serviceQueryStatus, serviceWin32OwnProcess, request.AutomaticStart ? 2u : 3u,
                serviceErrorNormal, request.BuildCommandLine(), null, IntPtr.Zero, null,
                logon?.AccountName ?? request.ServiceStartName,
                request.Account == OneCServiceAccount.WindowsUser ? password : null);
            if (service == IntPtr.Zero)
                throw CreateLastWin32Exception();
            registered = true;
            logon?.Complete();
            NativeMethods.CloseServiceHandle(service);
        }
        finally
        {
            NativeMethods.CloseServiceHandle(manager);
            if (createdDirectory && !registered)
            {
                // Non-recursive cleanup; preserve anything that appeared in the meantime.
                try { Directory.Delete(request.DataDirectory, recursive: false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

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

    public async Task DeleteAsync(OneCServiceDeletionRequest request, CancellationToken cancellationToken = default)
    {
        var inventory = new WindowsOneCServiceInventory();
        var services = await inventory.GetServicesAsync(cancellationToken);
        var directory = request.ValidateAndGetDataDirectory(services);
        if (directory is not null)
            await Task.Run(() => ServiceDataDirectoryDeletion.ValidateTree(directory), cancellationToken);

        var processInventory = new WindowsOneCProcessInventory();
        var processes = await processInventory.GetProcessesAsync(cancellationToken);
        var relatedIds = new HashSet<uint>();
        var rootPid = services.Single(s => string.Equals(s.Name, request.ServiceName, StringComparison.OrdinalIgnoreCase)).ProcessId;
        if (rootPid is > 0) relatedIds.Add(rootPid.Value);
        while (true)
        {
            var children = processes.Where(p => p.ParentProcessId is { } parent && relatedIds.Contains(parent))
                .Select(p => p.ProcessId).Where(id => !relatedIds.Contains(id)).ToArray();
            if (children.Length == 0) break;
            relatedIds.UnionWith(children);
        }
        await StopAsync(request.ServiceName, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.Add(CommandTimeout);
        while (true)
        {
            processes = await processInventory.GetProcessesAsync(cancellationToken);
            if (!processes.Any(p => relatedIds.Contains(p.ProcessId) || directory is not null
                && !string.IsNullOrWhiteSpace(p.DataDirectory)
                && (string.Equals(p.DataDirectory.TrimEnd('\\'), directory, StringComparison.OrdinalIgnoreCase)
                    || p.DataDirectory.StartsWith(directory + "\\", StringComparison.OrdinalIgnoreCase)))) break;
            if (DateTimeOffset.UtcNow >= deadline)
                throw new System.TimeoutException("Процессы службы ещё работают. Удаление службы и каталога отменено.");
            await Task.Delay(PollInterval, cancellationToken);
        }
        // Check the confirmation again after waiting for the service to stop.
        services = await inventory.GetServicesAsync(cancellationToken);
        request.ValidateAndGetDataDirectory(services);
        await Task.Run(() => DeleteServiceByName(request.ServiceName, cancellationToken), cancellationToken);
        try
        {
            await Task.Run(() => WaitForServiceDeletion(request.ServiceName, cancellationToken), cancellationToken);
            if (directory is not null)
            {
                var remaining = await inventory.GetServicesAsync(cancellationToken);
                // Reuse the snapshot only for the deleted service; check all other services live.
                request.ValidateAndGetDataDirectory(remaining.Append(services.Single(s =>
                    string.Equals(s.Name, request.ServiceName, StringComparison.OrdinalIgnoreCase))).ToArray());
                await Task.Run(() => ServiceDataDirectoryDeletion.Delete(directory), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            var message = directory is null
                ? "Служба помечена на удаление, но Windows ещё не подтвердила завершение операции."
                : $"Служба удалена или помечена на удаление, но очистка каталога «{directory}» не завершена.";
            throw new InvalidOperationException($"{message} {exception.Message}", exception);
        }
    }

    private static void WaitForServiceDeletion(string serviceName, CancellationToken cancellationToken)
    {
        var manager = NativeMethods.OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero) throw CreateLastWin32Exception();
        try
        {
            var deadline = DateTimeOffset.UtcNow.Add(CommandTimeout);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var service = NativeMethods.OpenService(manager, serviceName, 0x0004);
                if (service != IntPtr.Zero) NativeMethods.CloseServiceHandle(service);
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 1060) return; // ERROR_SERVICE_DOES_NOT_EXIST
                    if (error != 1072) throw new Win32Exception(error); // ERROR_SERVICE_MARKED_FOR_DELETE
                }
                if (DateTimeOffset.UtcNow >= deadline)
                    throw new System.TimeoutException("Windows ещё удерживает службу. Закройте её свойства в оснастках управления; каталог сохранён.");
                Thread.Sleep(PollInterval);
            }
        }
        finally { NativeMethods.CloseServiceHandle(manager); }
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
        [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateDirectory(string path, IntPtr securityAttributes);

        [DllImport("advapi32.dll", EntryPoint = "CreateServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateService(
            IntPtr manager, string serviceName, string displayName, uint desiredAccess,
            uint serviceType, uint startType, uint errorControl, string binaryPath,
            string? loadOrderGroup, IntPtr tagId, string? dependencies, string? account, string? password);

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
