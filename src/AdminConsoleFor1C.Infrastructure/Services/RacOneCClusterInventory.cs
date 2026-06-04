using System.Diagnostics;
using System.Globalization;
using System.Text;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Infrastructure.Services;

public sealed class RacOneCClusterInventory : IOneCClusterInventory
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CreateInfobaseCommandTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UpdateInfobaseCommandTimeout = TimeSpan.FromSeconds(20);

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
                var processLicenses = await GetClusterProcessLicensesAsync(racPath, administrationServerAddress, cluster.Uuid, cancellationToken);
                var sessionLicenses = await GetClusterSessionLicensesAsync(racPath, administrationServerAddress, cluster.Uuid, cancellationToken);
                var detailsMessage = CombineDetailsMessages(
                    servers.Message,
                    infobases.Message,
                    processLicenses.Message,
                    sessionLicenses.Message);

                detailedClusters.Add(cluster with
                {
                    Servers = servers.Items,
                    Infobases = infobases.Items,
                    ProcessLicenses = processLicenses.Items,
                    SessionLicenses = sessionLicenses.Items,
                    DetailsMessage = detailsMessage
                });
            }

            var serverCount = detailedClusters.Sum(static cluster => cluster.Servers.Count);
            var infobaseCount = detailedClusters.Sum(static cluster => cluster.Infobases.Count);
            var occupiedLicensesText = BuildOccupiedLicensesText(detailedClusters);

            return new OneCClusterInventoryResult
            {
                AdministrationServerAddress = administrationServerAddress,
                CommandText = commandText,
                IsAvailable = true,
                Message = clusters.Count == 0
                    ? "Кластеры не найдены"
                    : $"Найдено кластеров: {clusters.Count}, серверов: {serverCount}, баз: {infobaseCount}, {occupiedLicensesText}",
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

    public async Task<OneCClusterCommandResult> CreateInfobaseAsync(
        string racPath,
        OneCInfobaseCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var arguments = BuildCreateInfobaseArguments(request);
        var commandText = BuildSafeCommandText(racPath, arguments);
        if (string.IsNullOrWhiteSpace(racPath) || !File.Exists(racPath))
        {
            return new OneCClusterCommandResult
            {
                CommandText = commandText,
                IsSuccess = false,
                Message = "rac.exe не найден"
            };
        }

        try
        {
            var command = await RunRacAsync(racPath, arguments, CreateInfobaseCommandTimeout, cancellationToken);
            var combinedOutput = CombineOutput(command.Output, command.Error);

            return new OneCClusterCommandResult
            {
                CommandText = commandText,
                IsSuccess = command.ExitCode == 0,
                Message = command.ExitCode == 0
                    ? NormalizeMessage(combinedOutput, "Информационная база была создана")
                    : NormalizeMessage(combinedOutput, "Информационная база не была создана")
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OneCClusterCommandResult
            {
                CommandText = commandText,
                IsSuccess = false,
                Message = "Команда rac не ответила за отведенное время"
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new OneCClusterCommandResult
            {
                CommandText = commandText,
                IsSuccess = false,
                Message = exception.Message
            };
        }
    }

    public async Task<OneCClusterCommandResult> UpdateInfobaseRestrictionsAsync(
        string racPath,
        OneCInfobaseRestrictionsUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var arguments = BuildUpdateInfobaseRestrictionsArguments(request);
        var commandText = BuildSafeCommandText(racPath, arguments);
        if (string.IsNullOrWhiteSpace(racPath) || !File.Exists(racPath))
        {
            return new OneCClusterCommandResult
            {
                CommandText = commandText,
                IsSuccess = false,
                Message = "rac.exe не найден"
            };
        }

        try
        {
            var command = await RunRacAsync(racPath, arguments, UpdateInfobaseCommandTimeout, cancellationToken);
            var combinedOutput = CombineOutput(command.Output, command.Error);
            if (command.ExitCode != 0 && IsTransientTcpDisconnect(combinedOutput))
            {
                await Task.Delay(800, cancellationToken);
                command = await RunRacAsync(racPath, arguments, UpdateInfobaseCommandTimeout, cancellationToken);
                combinedOutput = CombineOutput(command.Output, command.Error);
            }

            return new OneCClusterCommandResult
            {
                CommandText = commandText,
                IsSuccess = command.ExitCode == 0,
                Message = command.ExitCode == 0
                    ? NormalizeMessage(combinedOutput, "Параметры информационной базы были обновлены")
                    : NormalizeMessage(combinedOutput, "Параметры информационной базы не были обновлены")
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OneCClusterCommandResult
            {
                CommandText = commandText,
                IsSuccess = false,
                Message = "Команда rac не ответила за отведенное время"
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new OneCClusterCommandResult
            {
                CommandText = commandText,
                IsSuccess = false,
                Message = exception.Message
            };
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
        var summaries = await GetClusterItemsAsync(
            racPath,
            arguments,
            static output => OneCRacOutputParser.ParseObjects(output)
                .Select(OneCInfobaseSummaryInfo.FromProperties)
                .Where(static infobase => !string.IsNullOrWhiteSpace(infobase.Uuid))
                .ToList(),
            "Информационные базы не были прочитаны",
            cancellationToken);

        if (summaries.Message is not null || summaries.Items.Count == 0)
        {
            return summaries;
        }

        var infobases = summaries.Items
            .Select(infobase => infobase with { ClusterUuid = clusterUuid })
            .ToList();
        var detailedInfobases = new List<OneCInfobaseSummaryInfo>(infobases.Count);
        var detailMessages = new List<string>();
        foreach (var summary in infobases)
        {
            var details = await GetInfobaseDetailsAsync(
                racPath,
                administrationServerAddress,
                clusterUuid,
                summary,
                cancellationToken);

            detailedInfobases.Add(details.Item);
            if (!string.IsNullOrWhiteSpace(details.Message))
            {
                detailMessages.Add(details.Message);
            }
        }

        return (detailedInfobases, detailMessages.Count == 0 ? null : string.Join(Environment.NewLine, detailMessages));
    }

    private static async Task<(IReadOnlyList<OneCOccupiedLicenseInfo> Items, string? Message)> GetClusterProcessLicensesAsync(
        string racPath,
        string administrationServerAddress,
        string clusterUuid,
        CancellationToken cancellationToken)
    {
        string[] arguments =
        [
            administrationServerAddress,
            "process",
            "list",
            $"--cluster={clusterUuid}",
            "--licenses"
        ];

        return await GetClusterItemsAsync(
            racPath,
            arguments,
            static output => OneCRacOutputParser.ParseObjects(output)
                .Select(static properties => OneCOccupiedLicenseInfo.FromProperties(properties, OneCLicenseOwnerKind.Process))
                .ToList(),
            "Занятые серверные лицензии не были прочитаны",
            cancellationToken);
    }

    private static async Task<(IReadOnlyList<OneCOccupiedLicenseInfo> Items, string? Message)> GetClusterSessionLicensesAsync(
        string racPath,
        string administrationServerAddress,
        string clusterUuid,
        CancellationToken cancellationToken)
    {
        string[] arguments =
        [
            administrationServerAddress,
            "session",
            "list",
            $"--cluster={clusterUuid}",
            "--licenses"
        ];

        return await GetClusterItemsAsync(
            racPath,
            arguments,
            static output => OneCRacOutputParser.ParseObjects(output)
                .Select(static properties => OneCOccupiedLicenseInfo.FromProperties(properties, OneCLicenseOwnerKind.Session))
                .ToList(),
            "Занятые пользовательские лицензии не были прочитаны",
            cancellationToken);
    }

    private static async Task<(OneCInfobaseSummaryInfo Item, string? Message)> GetInfobaseDetailsAsync(
        string racPath,
        string administrationServerAddress,
        string clusterUuid,
        OneCInfobaseSummaryInfo summary,
        CancellationToken cancellationToken)
    {
        string[] arguments =
        [
            administrationServerAddress,
            "infobase",
            "info",
            $"--cluster={clusterUuid}",
            $"--infobase={summary.Uuid}"
        ];

        try
        {
            var command = await RunRacAsync(racPath, arguments, cancellationToken);
            if (command.ExitCode != 0)
            {
                return (
                    summary,
                    NormalizeMessage(
                        CombineOutput(command.Output, command.Error),
                        $"Подробности базы {summary.NameText} не были прочитаны"));
            }

            var details = OneCRacOutputParser.ParseObjects(command.Output).FirstOrDefault();
            if (details is null)
            {
                return (summary, $"Подробности базы {summary.NameText} не были найдены");
            }

            return (OneCInfobaseSummaryInfo.FromProperties(MergeProperties(summary.Properties, details, clusterUuid)), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (summary, $"Подробности базы {summary.NameText} не были прочитаны: команда rac не ответила за отведенное время");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return (summary, $"Подробности базы {summary.NameText} не были прочитаны: {exception.Message}");
        }
    }

    private static IReadOnlyDictionary<string, string> MergeProperties(
        IReadOnlyDictionary<string, string> summary,
        IReadOnlyDictionary<string, string> details)
    {
        var merged = new Dictionary<string, string>(summary, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in details)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> MergeProperties(
        IReadOnlyDictionary<string, string> summary,
        IReadOnlyDictionary<string, string> details,
        string clusterUuid)
    {
        var merged = new Dictionary<string, string>(MergeProperties(summary, details), StringComparer.OrdinalIgnoreCase)
        {
            ["cluster"] = clusterUuid
        };

        return merged;
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
        return await RunRacAsync(racPath, arguments, CommandTimeout, cancellationToken);
    }

    private static async Task<RacCommandResult> RunRacAsync(
        string racPath,
        IReadOnlyList<string> arguments,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(commandTimeout);

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

    private static string BuildSafeCommandText(string racPath, IReadOnlyList<string> arguments)
    {
        var safeArguments = arguments
            .Select(static argument => argument.StartsWith("--db-pwd=", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--cluster-pwd=", StringComparison.OrdinalIgnoreCase)
                    ? $"{argument[..argument.IndexOf('=', StringComparison.Ordinal)]}=***"
                    : QuoteCommandArgument(argument));

        return BuildCommandText(racPath, safeArguments.ToList());
    }

    private static bool IsTransientTcpDisconnect(string output)
    {
        return output.Contains("10054", StringComparison.OrdinalIgnoreCase)
            || output.Contains("принудительно разорвал", StringComparison.OrdinalIgnoreCase)
            || output.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildCreateInfobaseArguments(OneCInfobaseCreateRequest request)
    {
        var arguments = new List<string>
        {
            request.AdministrationServerAddress,
            "infobase",
            "create",
            $"--cluster={request.ClusterUuid}"
        };

        if (request.CreateDatabase)
        {
            arguments.Add("--create-database");
        }

        AddRequiredOption(arguments, "--name", request.Name);
        AddRequiredOption(arguments, "--dbms", request.Dbms);
        AddRequiredOption(arguments, "--db-server", request.DbServer);
        AddRequiredOption(arguments, "--db-name", request.DbName);
        AddRequiredOption(arguments, "--locale", request.Locale);
        AddOptionalOption(arguments, "--db-user", request.DbUser);
        AddOptionalOption(arguments, "--db-pwd", request.DbPassword);
        AddOptionalOption(arguments, "--descr", request.Description);
        AddOptionalOption(arguments, "--date-offset", request.DateOffset);
        AddOptionalOption(arguments, "--security-level", request.SecurityLevel);
        AddOptionalOption(arguments, "--scheduled-jobs-deny", request.ScheduledJobsDeny);
        AddOptionalOption(arguments, "--license-distribution", request.LicenseDistribution);

        return arguments;
    }

    private static List<string> BuildUpdateInfobaseRestrictionsArguments(OneCInfobaseRestrictionsUpdateRequest request)
    {
        var arguments = new List<string>
        {
            request.AdministrationServerAddress,
            "infobase",
            "update",
            $"--cluster={request.ClusterUuid}",
            $"--infobase={request.InfobaseUuid}"
        };

        AddOptionalOption(arguments, "--sessions-deny", request.SessionsDeny);
        AddOptionalOption(arguments, "--scheduled-jobs-deny", request.ScheduledJobsDeny);

        return arguments;
    }

    private static void AddRequiredOption(List<string> arguments, string name, string value)
    {
        arguments.Add($"{name}={value.Trim()}");
    }

    private static void AddOptionalOption(List<string> arguments, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            arguments.Add($"{name}={value.Trim()}");
        }
    }

    private static string QuoteCommandArgument(string argument)
    {
        return argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
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

    private static string BuildOccupiedLicensesText(IReadOnlyList<OneCClusterInfo> clusters)
    {
        var usages = clusters
            .SelectMany(static cluster => cluster.OccupiedLicenseUsages)
            .ToList();

        if (usages.Count == 0)
        {
            return "лицензии не заняты";
        }

        var parts = new List<string>();
        var clientUsages = usages
            .Where(static usage => usage.OwnerKind == OneCLicenseOwnerKind.Session)
            .ToList();
        var serverUsages = usages
            .Where(static usage => usage.OwnerKind == OneCLicenseOwnerKind.Process)
            .ToList();

        if (clientUsages.Count > 0)
        {
            var sessionCount = clusters.Sum(static cluster => cluster.SessionLicenses.Count);
            parts.Add($"клиентские места: {FormatUsage(clientUsages)}, сеансов: {sessionCount}");
        }

        if (serverUsages.Count > 0)
        {
            parts.Add($"серверные лицензии: {FormatUsage(serverUsages)}");
        }

        return string.Join(", ", parts);
    }

    private static string FormatUsage(IReadOnlyList<OneCLicenseUsageInfo> usages)
    {
        var occupied = usages.Sum(static usage => usage.OccupiedSeats);
        var capacity = usages.Any(static usage => usage.Capacity is null)
            ? null
            : usages.Sum(static usage => usage.Capacity);

        return capacity is null ? occupied.ToString() : $"{occupied}/{capacity}";
    }

    private static string? CombineDetailsMessages(params string?[] messages)
    {
        var details = messages.Where(static message => !string.IsNullOrWhiteSpace(message));
        var text = string.Join(Environment.NewLine, details);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private sealed record RacCommandResult(string Output, string Error, int ExitCode);
}
