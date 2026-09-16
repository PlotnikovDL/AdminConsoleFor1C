using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class ElevatedWorkerOneCServiceController : IOneCServiceController
{
    private const int OperationCanceledByUserErrorCode = 1223;
    private readonly string workerPath;
    private readonly string resultDirectory;

    public ElevatedWorkerOneCServiceController(string workerPath, string resultDirectory)
    {
        this.workerPath = string.IsNullOrWhiteSpace(workerPath)
            ? throw new ArgumentException("Не указан путь к elevated worker.", nameof(workerPath))
            : workerPath;
        this.resultDirectory = string.IsNullOrWhiteSpace(resultDirectory)
            ? throw new ArgumentException("Не указан каталог результатов elevated worker.", nameof(resultDirectory))
            : resultDirectory;
    }

    public Task StartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(OneCServiceControlAction.Start, serviceName, cancellationToken);
    }

    public Task RegisterAsync(OneCServiceRegistrationRequest request, string? password = null, CancellationToken cancellationToken = default)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors));
        if (request.Account == OneCServiceAccount.WindowsUser && string.IsNullOrEmpty(password))
            throw new ArgumentException("Укажите пароль учетной записи Windows.");

        return ExecuteAsync(OneCServiceControlAction.Register, request.ServiceName, cancellationToken, request,
            request.Account == OneCServiceAccount.WindowsUser ? password : null);
    }

    public Task StopAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(OneCServiceControlAction.Stop, serviceName, cancellationToken);
    }

    public Task RestartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(OneCServiceControlAction.Restart, serviceName, cancellationToken);
    }

    public Task DeleteAsync(OneCServiceDeletionRequest request, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(OneCServiceControlAction.Delete, request.ServiceName, cancellationToken, deletion: request);
    }

    private async Task ExecuteAsync(
        OneCServiceControlAction action,
        string serviceName,
        CancellationToken cancellationToken,
        OneCServiceRegistrationRequest? registration = null,
        string? password = null,
        OneCServiceDeletionRequest? deletion = null)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Не указано имя службы Windows.", nameof(serviceName));
        }

        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException("Не найден elevated worker для управления службами.", workerPath);
        }

        Directory.CreateDirectory(resultDirectory);
        var resultPath = Path.Combine(resultDirectory, $"{Guid.NewGuid():N}.json");

        try
        {
            string? pipeName = null;
            using var pipe = registration is null ? null : ServiceRegistrationPipe.CreateServer(out pipeName);
            using var process = StartWorker(action, serviceName, resultPath, pipeName, deletion);
            if (pipe is not null)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    await ServiceRegistrationPipe.SendAsync(pipe, process.Id, registration!, password, timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Не удалось передать параметры регистрации процессу с правами администратора.");
                }
            }
            await process.WaitForExitAsync(cancellationToken);
            await ValidateResultAsync(process.ExitCode, resultPath, cancellationToken);
        }
        finally
        {
            TryDeleteResult(resultPath);
        }
    }

    private Process StartWorker(
        OneCServiceControlAction action,
        string serviceName,
        string resultPath,
        string? registrationPipe,
        OneCServiceDeletionRequest? deletion)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        startInfo.ArgumentList.Add("service");
        startInfo.ArgumentList.Add(ToWorkerActionName(action));
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add(serviceName);
        startInfo.ArgumentList.Add("--result");
        startInfo.ArgumentList.Add(resultPath);
        if (registrationPipe is not null)
        {
            startInfo.ArgumentList.Add("--registration-pipe");
            startInfo.ArgumentList.Add(registrationPipe);
            startInfo.ArgumentList.Add("--parent-pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (deletion is not null)
        {
            startInfo.ArgumentList.Add("--deletion");
            startInfo.ArgumentList.Add(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(deletion)));
        }

        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Не удалось запустить elevated worker.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == OperationCanceledByUserErrorCode)
        {
            throw new OperationCanceledException("Действие отменено пользователем.", exception);
        }
    }

    private static async Task ValidateResultAsync(
        int exitCode,
        string resultPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(resultPath))
        {
            throw new InvalidOperationException($"Elevated worker завершился с кодом {exitCode}, но не записал результат действия.");
        }

        var json = await File.ReadAllTextAsync(resultPath, cancellationToken);
        var result = JsonSerializer.Deserialize<ElevatedWorkerCommandResult>(json)
            ?? throw new InvalidOperationException("Elevated worker вернул пустой результат действия.");

        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? $"Elevated worker завершился с кодом {exitCode}."
                : result.ErrorMessage);
        }
    }

    private static string ToWorkerActionName(OneCServiceControlAction action)
    {
        return action switch
        {
            OneCServiceControlAction.Start => "start",
            OneCServiceControlAction.Stop => "stop",
            OneCServiceControlAction.Restart => "restart",
            OneCServiceControlAction.Delete => "delete",
            OneCServiceControlAction.Register => "register",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private static void TryDeleteResult(string resultPath)
    {
        try
        {
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ElevatedWorkerCommandResult(
        bool Success,
        string? ErrorMessage);
}
