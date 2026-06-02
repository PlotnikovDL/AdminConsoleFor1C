using System.Diagnostics;
using AdminConsoleFor1C.Application.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

public sealed class RasOneCAdministrationServerLauncher : IOneCAdministrationServerLauncher
{
    public async Task<int> StartTemporaryAsync(
        string rasPath,
        int administrationServerPort,
        string agentAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rasPath) || !File.Exists(rasPath))
        {
            throw new FileNotFoundException("ras.exe не найден", rasPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = rasPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(rasPath)
        };

        startInfo.ArgumentList.Add("cluster");
        startInfo.ArgumentList.Add($"--port={administrationServerPort}");
        startInfo.ArgumentList.Add(agentAddress);

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить ras.exe");

        await Task.Delay(700, cancellationToken);
        if (process.HasExited)
        {
            var exitCode = process.ExitCode;
            process.Dispose();
            throw new InvalidOperationException($"RAS завершился сразу после запуска. Код выхода: {exitCode}");
        }

        var processId = process.Id;
        process.Dispose();
        return processId;
    }

    public async Task StopTemporaryAsync(int processId, CancellationToken cancellationToken = default)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
        }
    }
}
