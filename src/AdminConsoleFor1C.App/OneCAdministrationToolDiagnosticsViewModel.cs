using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace AdminConsoleFor1C.App;

public sealed class OneCAdministrationToolDiagnosticsViewModel
{
    private readonly IReadOnlyList<OneCAdministrationToolInfo> _tools;
    private readonly IReadOnlyList<OneCServiceInfo> _services;
    private readonly IReadOnlyList<OneCProcessInfo> _processes;

    public OneCAdministrationToolDiagnosticsViewModel(
        IReadOnlyList<OneCAdministrationToolInfo> tools,
        IReadOnlyList<OneCServiceInfo> services,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        _tools = tools;
        _services = services;
        _processes = processes;

        TargetVersion = GetTargetVersion();
        RacTool = GetPreferredTool(OneCAdministrationToolKind.Rac);
        RasTool = GetPreferredTool(OneCAdministrationToolKind.Ras);
        RasService = _services.FirstOrDefault(static service => service.Kind == OneCServiceKind.AdministrationServer);
        RasProcess = _processes.FirstOrDefault(static process => process.Kind == OneCProcessKind.AdministrationServer);
    }

    public string? TargetVersion { get; }

    public OneCAdministrationToolInfo? RacTool { get; }

    public OneCAdministrationToolInfo? RasTool { get; }

    public OneCServiceInfo? RasService { get; }

    public OneCProcessInfo? RasProcess { get; }

    public string SummaryText
    {
        get
        {
            if (RacTool is null && RasTool is null)
            {
                return "rac.exe и ras.exe не найдены";
            }

            return string.IsNullOrWhiteSpace(TargetVersion)
                ? "Найдены инструменты администрирования 1С"
                : $"Подбор под версию сервера {TargetVersion}";
        }
    }

    public string RacStateText => RacTool is null ? "Не найден" : "Найден";

    public string RacVersionText => RacTool?.VersionText ?? "—";

    public string RacPathText => RacTool?.FilePathText ?? "Проверьте установку серверных компонентов 1С";

    public string RasStateText
    {
        get
        {
            if (RasProcess is not null)
            {
                return RasProcess.AdministrationServerPort is null
                    ? "Работает"
                    : $"Работает, порт {RasProcess.AdministrationServerPort}";
            }

            if (RasService is not null)
            {
                return RasService.AdministrationServerPort is null
                    ? RasService.StateDisplayName
                    : $"{RasService.StateDisplayName}, порт {RasService.AdministrationServerPort}";
            }

            return RasTool is null ? "Не найден" : "Не запущен";
        }
    }

    public string RasVersionText => FirstNotEmpty(
        RasProcess?.VersionText,
        RasService?.VersionText,
        RasTool?.VersionText);

    public string RasPathText => FirstNotEmpty(
        RasTool?.FilePathText,
        RasService?.ExecutablePathText,
        RasProcess?.ExecutablePathText,
        "Проверьте установку серверных компонентов 1С");

    public Brush RacStatusAccentBrush => new SolidColorBrush(RacTool is null
        ? ColorHelper.FromArgb(255, 157, 93, 0)
        : ColorHelper.FromArgb(255, 16, 124, 65));

    public Brush RacStatusBadgeBackgroundBrush => new SolidColorBrush(RacTool is null
        ? ColorHelper.FromArgb(28, 157, 93, 0)
        : ColorHelper.FromArgb(28, 16, 124, 65));

    public Brush RasStatusAccentBrush => new SolidColorBrush(RasProcess is not null
        ? ColorHelper.FromArgb(255, 16, 124, 65)
        : ColorHelper.FromArgb(255, 157, 93, 0));

    public Brush RasStatusBadgeBackgroundBrush => new SolidColorBrush(RasProcess is not null
        ? ColorHelper.FromArgb(28, 16, 124, 65)
        : ColorHelper.FromArgb(28, 157, 93, 0));

    private string? GetTargetVersion()
    {
        return _services
                .Where(static service => service.Kind == OneCServiceKind.ServerAgent)
                .Select(static service => service.Version)
                .FirstOrDefault(static version => !string.IsNullOrWhiteSpace(version))
            ?? _processes
                .Where(static process => process.Kind == OneCProcessKind.ServerAgent)
                .Select(static process => process.Version)
                .FirstOrDefault(static version => !string.IsNullOrWhiteSpace(version));
    }

    private OneCAdministrationToolInfo? GetPreferredTool(OneCAdministrationToolKind kind)
    {
        var tools = _tools
            .Where(tool => tool.Kind == kind)
            .ToList();

        if (tools.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(TargetVersion))
        {
            var matchingTool = tools.FirstOrDefault(tool => tool.Version == TargetVersion);
            if (matchingTool is not null)
            {
                return matchingTool;
            }
        }

        return tools
            .OrderByDescending(static tool => ParseVersion(tool.Version))
            .First();
    }

    private static string FirstNotEmpty(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value) && value != "—")
            ?? "—";
    }

    private static Version ParseVersion(string? version)
    {
        return Version.TryParse(version, out var parsed)
            ? parsed
            : new Version();
    }
}
