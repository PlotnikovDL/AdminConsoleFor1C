using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Infrastructure.Services;

namespace AdminConsoleFor1C.App;

internal sealed class ElevatedWorkerOneCServiceController : IOneCServiceController
{
    private const string WorkerFileName = "AdminConsoleFor1C.ElevatedWorker.exe";

    private readonly IOneCServiceController _directController = new WindowsOneCServiceController();

    public Task StartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(serviceName, OneCServiceControlAction.Start, cancellationToken);
    }

    public Task StopAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(serviceName, OneCServiceControlAction.Stop, cancellationToken);
    }

    public Task RestartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(serviceName, OneCServiceControlAction.Restart, cancellationToken);
    }

    public Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(serviceName, OneCServiceControlAction.Delete, cancellationToken);
    }

    private async Task ExecuteAsync(
        string serviceName,
        OneCServiceControlAction action,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteDirectAsync(serviceName, action, cancellationToken);
        }
        catch (Exception exception) when (IsAccessDenied(exception))
        {
            await ExecuteElevatedAsync(serviceName, action, cancellationToken);
        }
    }

    private async Task ExecuteDirectAsync(
        string serviceName,
        OneCServiceControlAction action,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case OneCServiceControlAction.Start:
                await _directController.StartAsync(serviceName, cancellationToken);
                break;
            case OneCServiceControlAction.Stop:
                await _directController.StopAsync(serviceName, cancellationToken);
                break;
            case OneCServiceControlAction.Restart:
                await _directController.RestartAsync(serviceName, cancellationToken);
                break;
            case OneCServiceControlAction.Delete:
                await _directController.DeleteAsync(serviceName, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private static async Task ExecuteElevatedAsync(
        string serviceName,
        OneCServiceControlAction action,
        CancellationToken cancellationToken)
    {
        var workerPath = FindWorkerPath();
        var resultPath = CreateResultPath();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(workerPath) ?? string.Empty
            };
            startInfo.ArgumentList.Add("service");
            startInfo.ArgumentList.Add(GetActionArgument(action));
            startInfo.ArgumentList.Add("--name");
            startInfo.ArgumentList.Add(serviceName);
            startInfo.ArgumentList.Add("--result");
            startInfo.ArgumentList.Add(resultPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Не удалось запустить процесс повышенного действия.");
            await process.WaitForExitAsync(cancellationToken);

            var result = await ReadResultAsync(resultPath, cancellationToken);
            if (process.ExitCode != 0 || result?.Success != true)
            {
                throw new InvalidOperationException(result?.ErrorMessage
                    ?? $"Повышенное действие завершилось с кодом {process.ExitCode}.");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Запрос прав администратора был отменен.", exception, cancellationToken);
        }
        finally
        {
            TryDeleteResultFile(resultPath);
        }
    }

    private static async Task<ElevatedWorkerCommandResult?> ReadResultAsync(
        string resultPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(resultPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(resultPath);
        return await JsonSerializer.DeserializeAsync<ElevatedWorkerCommandResult>(
            stream,
            cancellationToken: cancellationToken);
    }

    private static string FindWorkerPath()
    {
        foreach (var candidate in EnumerateWorkerPathCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Не найден модуль повышенных действий {WorkerFileName}.");
    }

    private static IEnumerable<string> EnumerateWorkerPathCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "ElevatedWorker", WorkerFileName);
        yield return Path.Combine(baseDirectory, WorkerFileName);

        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            yield return Path.Combine(
                directory.FullName,
                "src",
                "AdminConsoleFor1C.ElevatedWorker",
                "bin",
                "Debug",
                "net10.0",
                WorkerFileName);
            yield return Path.Combine(
                directory.FullName,
                "src",
                "AdminConsoleFor1C.ElevatedWorker",
                "bin",
                "Release",
                "net10.0",
                WorkerFileName);
        }
    }

    private static string CreateResultPath()
    {
        var tempDirectory = GetElevatedWorkerTempDirectory();
        Directory.CreateDirectory(tempDirectory);
        return Path.Combine(tempDirectory, $"elevated-service-{Guid.NewGuid():N}.json");
    }

    private static string GetElevatedWorkerTempDirectory()
    {
        try
        {
            var localCachePath = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;
            if (!string.IsNullOrWhiteSpace(localCachePath))
            {
                return Path.Combine(localCachePath, "AdminConsoleFor1C", "Temp");
            }
        }
        catch (InvalidOperationException)
        {
            // Unpackaged fallback.
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AdminConsoleFor1C",
            "Temp");
    }

    private static void TryDeleteResultFile(string resultPath)
    {
        try
        {
            File.Delete(resultPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetActionArgument(OneCServiceControlAction action)
    {
        return action switch
        {
            OneCServiceControlAction.Start => "start",
            OneCServiceControlAction.Stop => "stop",
            OneCServiceControlAction.Restart => "restart",
            OneCServiceControlAction.Delete => "delete",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private static bool IsAccessDenied(Exception exception)
    {
        return exception is UnauthorizedAccessException
            || exception is Win32Exception { NativeErrorCode: 5 }
            || exception.InnerException is not null && IsAccessDenied(exception.InnerException);
    }

    private sealed record ElevatedWorkerCommandResult(
        bool Success,
        string? ErrorMessage);
}
