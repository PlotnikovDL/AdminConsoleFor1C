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
        var commandText = BuildCommandText(racPath, administrationServerAddress);
        if (string.IsNullOrWhiteSpace(racPath) || !File.Exists(racPath))
        {
            return OneCClusterInventoryResult.Unavailable(
                administrationServerAddress,
                commandText,
                "rac.exe не найден");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);

            using var process = StartRac(racPath, administrationServerAddress);
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            var combinedOutput = CombineOutput(output, error);

            if (process.ExitCode != 0)
            {
                return OneCClusterInventoryResult.Unavailable(
                    administrationServerAddress,
                    commandText,
                    NormalizeMessage(combinedOutput, "RAS не отвечает"));
            }

            var clusters = OneCRacOutputParser.ParseObjects(output)
                .Select(OneCClusterInfo.FromProperties)
                .Where(static cluster => !string.IsNullOrWhiteSpace(cluster.Uuid))
                .ToList();

            return new OneCClusterInventoryResult
            {
                AdministrationServerAddress = administrationServerAddress,
                CommandText = commandText,
                IsAvailable = true,
                Message = clusters.Count == 0 ? "Кластеры не найдены" : $"Найдено кластеров: {clusters.Count}",
                Clusters = clusters
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

    private static Process StartRac(string racPath, string administrationServerAddress)
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

        startInfo.ArgumentList.Add(administrationServerAddress);
        startInfo.ArgumentList.Add("cluster");
        startInfo.ArgumentList.Add("list");

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить rac.exe");
    }

    private static string BuildCommandText(string racPath, string administrationServerAddress)
    {
        var executableName = string.IsNullOrWhiteSpace(racPath)
            ? "rac.exe"
            : Path.GetFileName(racPath);

        return $"{executableName} {administrationServerAddress} cluster list";
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
}
