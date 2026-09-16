using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using AdminConsoleFor1C.Core.Administration;
using Microsoft.Win32.SafeHandles;

namespace AdminConsoleFor1C.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class TemporaryRasSessionFactory : IOneCRasSessionFactory
{
    public static string GetPlatformVersion(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Укажите полный путь к каталогу bin платформы.");
        string Read(string name)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) throw new FileNotFoundException($"В выбранной платформе не найден {name}.", path);
            var version = FileVersionInfo.GetVersionInfo(path);
            return $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}";
        }
        var rac = Read("rac.exe");
        if (rac != Read("ras.exe")) throw new InvalidOperationException("Версии rac.exe и ras.exe в каталоге различаются.");
        return rac;
    }

    public async Task<IOneCRasSession> OpenAsync(OneCServerConnectionProfile profile, CancellationToken cancellationToken)
    {
        profile.Validate();
        if (GetPlatformVersion(profile.PlatformDirectory) != profile.PlatformVersion)
            throw new InvalidOperationException("Версия платформы в сохранённом каталоге изменилась. Измените подключение.");
        var session = new TemporaryRasSession(profile);
        try { await session.StartAsync(cancellationToken); return session; }
        catch { await session.DisposeAsync(); throw; }
    }

    private sealed class TemporaryRasSession(OneCServerConnectionProfile profile) : IOneCRasSession
    {
        private readonly SafeFileHandle job = CreateOwnedJob();
        private Process? ras;
        private Task<string>? rasOutput;
        private Task<string>? rasError;
        private int port;

        public async Task StartAsync(CancellationToken token)
        {
            port = FreePort();
            var monitorPort = FreePort();
            while (monitorPort == port) monitorPort = FreePort();
            ras = StartOwned("ras.exe", ["cluster", $"--port={port}", $"--monitor-port={monitorPort}", profile.AgentAddress]);
            rasOutput = ras.StandardOutput.ReadToEndAsync();
            rasError = ras.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                while (true)
                {
                    if (ras.HasExited) throw new InvalidOperationException("Сервер администрирования не запустился: " + await rasError);
                    using var client = new TcpClient();
                    try { await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token); break; }
                    catch (SocketException) { await Task.Delay(100, timeout.Token); }
                }
                await Task.Delay(200, timeout.Token);
                if (ras.HasExited) throw new InvalidOperationException("Сервер администрирования завершился: " + await rasError);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException("Сервер администрирования не запустился за 8 секунд."); }
        }

        public async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken token)
        {
            if (ras is null || ras.HasExited) throw new InvalidOperationException("Сервер администрирования завершился.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var process = StartOwned("rac.exe", new[] { $"127.0.0.1:{port}" }.Concat(arguments));
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var text = await output;
                var diagnostic = await error;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(diagnostic) ? text.Trim() : diagnostic.Trim());
                return text;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException($"Сервер {profile.AgentAddress} не ответил за 20 секунд."); }
            finally
            {
                KillOwned(process);
                await process.WaitForExitAsync();
                await Task.WhenAll(output, error);
            }
        }

        private Process StartOwned(string executable, IEnumerable<string> arguments)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding((int)GetOEMCP());
            var start = new ProcessStartInfo(Path.Combine(profile.PlatformDirectory, executable))
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = profile.PlatformDirectory,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = encoding, StandardErrorEncoding = encoding
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var process = Process.Start(start) ?? throw new InvalidOperationException($"Не удалось запустить {executable}.");
            if (AssignProcessToJobObject(job, process.Handle)) return process;
            var exception = new Win32Exception(Marshal.GetLastWin32Error());
            KillOwned(process);
            process.Dispose();
            throw exception;
        }

        public async ValueTask DisposeAsync()
        {
            job.Dispose(); // Kills only this operation's RAS/RAC, also on app termination.
            if (ras is not null)
            {
                KillOwned(ras);
                await ras.WaitForExitAsync();
                ras.Dispose();
                ras = null;
            }
            if (rasOutput is not null) await rasOutput;
            if (rasError is not null) await rasError;
        }
    }

    private static void KillOwned(Process process)
    {
        try { if (!process.HasExited) process.Kill(); }
        catch (InvalidOperationException) { }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static SafeFileHandle CreateOwnedJob()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new JobExtendedLimits { Basic = new JobBasicLimits { Flags = 0x2000 } };
        if (SetInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf<JobExtendedLimits>())) return job;
        var error = new Win32Exception(Marshal.GetLastWin32Error());
        job.Dispose();
        throw error;
    }

    [StructLayout(LayoutKind.Sequential)] private struct JobBasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcesses;
        public UIntPtr Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct JobExtendedLimits
    {
        public JobBasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref JobExtendedLimits info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern uint GetOEMCP();
}
