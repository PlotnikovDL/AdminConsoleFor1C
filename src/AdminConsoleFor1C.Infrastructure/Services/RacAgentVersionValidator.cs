using System.Text.RegularExpressions;
using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Infrastructure.Services;

internal static class RacAgentVersionValidator
{
    // Use the caller's command wrapper to retain its credential redaction and cancellation behavior.
    public static async Task CheckAsync(Func<string[], CancellationToken, Task<string>> runAsync,
        OneCServerConnectionProfile profile, CancellationToken cancellationToken)
    {
        var output = await runAsync(["agent", "version"], cancellationToken);
        var version = Regex.Match(output, @"(?m)^\s*(?:version\s*:\s*)?""?(\d+\.\d+\.\d+\.\d+)""?\s*$");
        if (!version.Success) throw new InvalidOperationException("Не удалось определить версию агента сервера 1С.");
        if (version.Groups[1].Value != profile.PlatformVersion)
            throw new InvalidOperationException($"На сервере {profile.AgentAddress} установлена платформа {version.Groups[1].Value}, "
                + $"а в подключении выбрана {profile.PlatformVersion}. Выберите соответствующую версию и порт агента.");
    }
}
