using System.Text.Json;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Infrastructure.Services;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    ElevatedWorkerCommand? command = null;

    try
    {
        command = ParseCommand(args);
        await ExecuteCommandAsync(command);
        await WriteResultAsync(command.ResultPath, new ElevatedWorkerCommandResult(true, null));
        return 0;
    }
    catch (Exception exception)
    {
        if (!string.IsNullOrWhiteSpace(command?.ResultPath))
        {
            await WriteResultAsync(command.ResultPath, new ElevatedWorkerCommandResult(false, BuildExceptionMessage(exception)));
        }

        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

static ElevatedWorkerCommand ParseCommand(string[] args)
{
    if (args.Length < 2
        || !string.Equals(args[0], "service", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Неизвестная команда повышенного действия.");
    }

    var action = args[1].ToLowerInvariant() switch
    {
        "start" => OneCServiceControlAction.Start,
        "stop" => OneCServiceControlAction.Stop,
        "restart" => OneCServiceControlAction.Restart,
        "delete" => OneCServiceControlAction.Delete,
        _ => throw new InvalidOperationException("Неизвестное действие со службой.")
    };

    string? serviceName = null;
    string? resultPath = null;

    for (var index = 2; index < args.Length; index++)
    {
        var name = args[index];
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"Не указано значение параметра {name}.");
        }

        var value = args[++index];
        switch (name)
        {
            case "--name":
                serviceName = value;
                break;
            case "--result":
                resultPath = value;
                break;
            default:
                throw new InvalidOperationException($"Неизвестный параметр {name}.");
        }
    }

    if (string.IsNullOrWhiteSpace(serviceName))
    {
        throw new InvalidOperationException("Не указано имя службы Windows.");
    }

    if (string.IsNullOrWhiteSpace(resultPath))
    {
        throw new InvalidOperationException("Не указан файл результата повышенного действия.");
    }

    return new ElevatedWorkerCommand(action, serviceName, resultPath);
}

static async Task ExecuteCommandAsync(ElevatedWorkerCommand command)
{
    var controller = new WindowsOneCServiceController();
    switch (command.Action)
    {
        case OneCServiceControlAction.Start:
            await controller.StartAsync(command.ServiceName);
            break;
        case OneCServiceControlAction.Stop:
            await controller.StopAsync(command.ServiceName);
            break;
        case OneCServiceControlAction.Restart:
            await controller.RestartAsync(command.ServiceName);
            break;
        case OneCServiceControlAction.Delete:
            await controller.DeleteAsync(command.ServiceName);
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(command), command.Action, null);
    }
}

static async Task WriteResultAsync(string resultPath, ElevatedWorkerCommandResult result)
{
    var directory = Path.GetDirectoryName(resultPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result));
}

static string BuildExceptionMessage(Exception exception)
{
    var messages = new List<string>();
    for (var current = exception; current is not null; current = current.InnerException)
    {
        if (!string.IsNullOrWhiteSpace(current.Message)
            && !messages.Contains(current.Message, StringComparer.Ordinal))
        {
            messages.Add(current.Message);
        }
    }

    return string.Join(" ", messages);
}

internal sealed record ElevatedWorkerCommand(
    OneCServiceControlAction Action,
    string ServiceName,
    string ResultPath);

internal sealed record ElevatedWorkerCommandResult(
    bool Success,
    string? ErrorMessage);
