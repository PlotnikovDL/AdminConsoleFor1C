using System.Diagnostics;
using System.Globalization;
using System.Text;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Infrastructure.Services;

public sealed class RacOneCClusterInventory : IOneCClusterInventory
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(6);

    static RacOneCClusterInventory()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<OneCClusterInventoryResult> GetClustersAsync(
        string racPath,
        string administrationServerAddress,
        CancellationToken cancellationToken = default)
    {
        string[] clusterArguments = [administrationServerAddress, "cluster", "list"];
        var commandText = BuildCommandText(racPath, clusterArguments);
        if (string.IsNullOrWhiteSpace(racPath) || !File.Exists(racPath))
        {
            return OneCClusterInventoryResult.Unavailable(
                administrationServerAddress,
                commandText,
                "rac.exe не найден");
        }

        try
        {
            var clusterCommand = await RunRacAsync(racPath, clusterArguments, cancellationToken);
            var combinedOutput = CombineOutput(clusterCommand.Output, clusterCommand.Error);

            if (clusterCommand.ExitCode != 0)
            {
                return OneCClusterInventoryResult.Unavailable(
                    administrationServerAddress,
                    commandText,
                    NormalizeMessage(combinedOutput, "RAS не отвечает"));
            }

            var clusters = OneCRacOutputParser.ParseObjects(clusterCommand.Output)
                .Select(OneCClusterInfo.FromProperties)
                .Where(static cluster => !string.IsNullOrWhiteSpace(cluster.Uuid))
                .ToList();
            var detailedClusters = new List<OneCClusterInfo>(clusters.Count);

            foreach (var cluster in clusters)
            {
                var servers = await GetClusterServersAsync(racPath, administrationServerAddress, cluster.Uuid, cancellationToken);
                var infobases = await GetClusterInfobasesAsync(racPath, administrationServerAddress, cluster.Uuid, cancellationToken);
                var detailsMessage = CombineDetailsMessages(servers.Message, infobases.Message);

                detailedClusters.Add(cluster with
                {
                    Servers = servers.Items,
                    Infobases = infobases.Items,
                    DetailsMessage = detailsMessage
                });
            }

            var serverCount = detailedClusters.Sum(static cluster => cluster.Servers.Count);
            var infobaseCount = detailedClusters.Sum(static cluster => cluster.Infobases.Count);

            return new OneCClusterInventoryResult
            {
                AdministrationServerAddress = administrationServerAddress,
                CommandText = commandText,
                IsAvailable = true,
                Message = clusters.Count == 0
                    ? "Кластеры не найдены"
                    : $"Найдено кластеров: {clusters.Count}, серверов: {serverCount}, баз: {infobaseCount}",
                Clusters = detailedClusters
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OneCClusterInventoryResult.Unavailable(
                administrationServerAddress,
                commandText,
                "Команда rac не ответила за отведенное время");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return OneCClusterInventoryResult.Unavailable(
                administrationServerAddress,
                commandText,
                exception.Message);
        }
    }

    private static async Task<(IReadOnlyList<OneCClusterServerInfo> Items, string? Message)> GetClusterServersAsync(
        string racPath,
        string administrationServerAddress,
        string clusterUuid,
        CancellationToken cancellationToken)
    {
        string[] arguments = [administrationServerAddress, "server", "list", $"--cluster={clusterUuid}"];
        return await GetClusterItemsAsync(
            racPath,
            arguments,
            static output => OneCRacOutputParser.ParseObjects(output)
                .Select(OneCClusterServerInfo.FromProperties)
                .Where(static server => !string.IsNullOrWhiteSpace(server.Uuid))
                .ToList(),
            "Рабочие серверы не были прочитаны",
            cancellationToken);
    }

    private static async Task<(IReadOnlyList<OneCInfobaseSummaryInfo> Items, string? Message)> GetClusterInfobasesAsync(
        string racPath,
        string administrationServerAddress,
        string clusterUuid,
        CancellationToken cancellationToken)
    {
        string[] arguments = [administrationServerAddress, "infobase", "summary", "list", $"--cluster={clusterUuid}"];
        return await GetClusterItemsAsync(
            racPath,
            arguments,
            static output => OneCRacOutputParser.ParseObjects(output)
                .Select(OneCInfobaseSummaryInfo.FromProperties)
                .Where(static infobase => !string.IsNullOrWhiteSpace(infobase.Uuid))
                .ToList(),
            "Информационные базы не были прочитаны",
            cancellationToken);
    }

    private static async Task<(IReadOnlyList<T> Items, string? Message)> GetClusterItemsAsync<T>(
        string racPath,
        IReadOnlyList<string> arguments,
        Func<string, IReadOnlyList<T>> parser,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = await RunRacAsync(racPath, arguments, cancellationToken);
            if (command.ExitCode != 0)
            {
                return ([], NormalizeMessage(CombineOutput(command.Output, command.Error), fallbackMessage));
            }

            return (parser(command.Output), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ([], $"{fallbackMessage}: команда rac не ответила за отведенное время");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return ([], $"{fallbackMessage}: {exception.Message}");
        }
    }

    private static async Task<RacCommandResult> RunRacAsync(
        string racPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);

        using var process = StartRac(racPath, arguments);
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

        await process.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;

        return new RacCommandResult(
            output,
            error,
            process.ExitCode);
    }

    private static Process StartRac(string racPath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = racPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = GetOemEncoding(),
            StandardErrorEncoding = GetOemEncoding()
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить rac.exe");
    }

    private static string BuildCommandText(string racPath, IReadOnlyList<string> arguments)
    {
        var executableName = string.IsNullOrWhiteSpace(racPath)
            ? "rac.exe"
            : Path.GetFileName(racPath);

        return $"{executableName} {string.Join(' ', arguments)}";
    }

    private static Encoding GetOemEncoding()
    {
        return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }

    private static string CombineOutput(string output, string error)
    {
        return string.Join(
            Environment.NewLine,
            new[] { output, error }.Where(static text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string NormalizeMessage(string output, string fallback)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return fallback;
        }

        var lines = output
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2);

        return string.Join(". ", lines);
    }

    private static string? CombineDetailsMessages(params string?[] messages)
    {
        var details = messages.Where(static message => !string.IsNullOrWhiteSpace(message));
        var text = string.Join(Environment.NewLine, details);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private sealed record RacCommandResult(string Output, string Error, int ExitCode);
}
