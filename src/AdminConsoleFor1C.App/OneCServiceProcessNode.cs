using System.ComponentModel;
using System.Runtime.CompilerServices;
using AdminConsoleFor1C.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AdminConsoleFor1C.App;

public sealed class OneCServiceProcessNode : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required OneCServiceInfo? Service { get; init; }

    public OneCServerAgentSetupCandidateViewModel? ServerAgentSetupCandidate { get; init; }

    public required IReadOnlyList<OneCProcessInfo> Processes { get; init; }

    public bool IsServerAgentSetupCandidate => ServerAgentSetupCandidate is not null;

    public bool IsOrphanGroup => Service is null && !IsServerAgentSetupCandidate;

    public string Title => IsServerAgentSetupCandidate
        ? "Агент сервера"
        : Service?.KindDisplayName ?? "Процессы без службы";

    public string WindowsServicesDisplayNameText => Service?.DisplayNameText
        ?? ServerAgentSetupCandidate?.VersionText
        ?? "Запущены, но не сопоставлены со службой Windows";

    public string CandidateExecutablePathText => ServerAgentSetupCandidate?.RagentPathText ?? string.Empty;

    public Visibility CandidateExecutablePathVisibility => string.IsNullOrWhiteSpace(CandidateExecutablePathText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string TaskManagerServiceNameText => Service?.Name ?? string.Empty;

    public Visibility TaskManagerServiceNameVisibility => string.IsNullOrWhiteSpace(TaskManagerServiceNameText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility ProcessSummaryVisibility => Processes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ProcessesSectionVisibility => Processes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ServiceActionsVisibility => Service is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SetupActionsVisibility => IsServerAgentSetupCandidate ? Visibility.Visible : Visibility.Collapsed;

    public Visibility WindowsNamesVisibility => Service is null ? Visibility.Collapsed : Visibility.Visible;

    public bool CanStartService => Service?.State == "Stopped";

    public bool CanStopService => Service?.State is "Running" or "Paused";

    public bool CanRestartService => Service?.State == "Running";

    public bool CanDeleteService => Service is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CardBorderBrush));
            OnPropertyChanged(nameof(CardBackgroundBrush));
            OnPropertyChanged(nameof(CardBorderThickness));
        }
    }

    public Brush CardBorderBrush => new SolidColorBrush(IsSelected
        ? ColorHelper.FromArgb(255, 0, 95, 184)
        : ColorHelper.FromArgb(28, 0, 0, 0));

    public Brush CardBackgroundBrush => new SolidColorBrush(IsSelected
        ? ColorHelper.FromArgb(18, 0, 95, 184)
        : ColorHelper.FromArgb(0, 0, 0, 0));

    public Thickness CardBorderThickness => IsSelected ? new Thickness(2) : new Thickness(1);

    public string StateDisplayName => Service?.StateDisplayName
        ?? (IsServerAgentSetupCandidate ? "Без службы" : "Без службы");

    public Brush StatusAccentBrush => new SolidColorBrush(GetStatusAccentColor());

    public Brush StatusBadgeBackgroundBrush => new SolidColorBrush(GetStatusBadgeBackgroundColor());

    public Brush StatusChipBackgroundBrush => new SolidColorBrush(GetStatusChipBackgroundColor());

    public Brush StatusTextBrush => new SolidColorBrush(GetStatusTextColor());

    public Visibility StatusReadyIconVisibility => Service?.State == "Running" ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StatusFallbackDotVisibility => Service?.State == "Running" ? Visibility.Collapsed : Visibility.Visible;

    public string VersionText => Service?.VersionText
        ?? ServerAgentSetupCandidate?.VersionText
        ?? GetFirstProcessValue(static process => process.VersionText);

    public string PortsText => Service?.PortsText
        ?? GetServerAgentSetupPortsText()
        ?? GetFirstProcessValue(static process => process.PortsText);

    public string ProcessIdText => Service?.ProcessIdText ?? GetFirstProcessValue(static process => process.ProcessIdText);

    public string ProcessIdSummaryText => ProcessIdText == "—" ? "PID —" : $"PID {ProcessIdText}";

    public string StartModeDisplayName => Service?.StartModeDisplayName
        ?? (IsServerAgentSetupCandidate ? "Служба не создана" : "—");

    public string AccountText => Service?.AccountText ?? GetFirstProcessValue(static process => process.OwnerText);

    public string DataDirectoryText => Service?.DataDirectoryText
        ?? ServerAgentSetupCandidate?.DataDirectory
        ?? "—";

    public string ExecutablePathText => Service?.ExecutablePathText
        ?? ServerAgentSetupCandidate?.RagentPathText
        ?? "—";

    public string ArgumentsText => Service?.ArgumentsText ?? "—";

    public string ProcessesSummaryText => Processes.Count switch
    {
        0 => "Процессы не найдены",
        1 => "1 процесс",
        >= 2 and <= 4 => $"{Processes.Count} процесса",
        _ => $"{Processes.Count} процессов"
    };

    public string ProcessNamesSummaryText
    {
        get
        {
            if (Processes.Count == 0)
            {
                return string.Empty;
            }

            var names = Processes
                .Select(static process => process.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return string.Join(", ", names);
        }
    }

    private string GetFirstProcessValue(Func<OneCProcessInfo, string> selector)
    {
        var value = Processes
            .Select(selector)
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text) && text != "—");

        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private string? GetServerAgentSetupPortsText()
    {
        if (ServerAgentSetupCandidate is null)
        {
            return null;
        }

        return string.Join(
            Environment.NewLine,
            $"Агент: {ServerAgentSetupCandidate.AgentPortText}",
            $"Кластер: {ServerAgentSetupCandidate.ClusterPortText}",
            $"Процессы: {ServerAgentSetupCandidate.ProcessRangeText}",
            $"Отладка HTTP: {ServerAgentSetupCandidate.DebugServerPortText}");
    }

    private Windows.UI.Color GetStatusAccentColor()
    {
        return Service?.State switch
        {
            "Running" => ColorHelper.FromArgb(255, 16, 124, 16),
            "Stopped" => ColorHelper.FromArgb(255, 115, 115, 115),
            "Paused" => ColorHelper.FromArgb(255, 157, 93, 0),
            "Start Pending" or "Stop Pending" or "Continue Pending" or "Pause Pending" => ColorHelper.FromArgb(255, 139, 105, 20),
            null when IsServerAgentSetupCandidate => ColorHelper.FromArgb(255, 96, 96, 96),
            null => ColorHelper.FromArgb(255, 96, 96, 96),
            _ => ColorHelper.FromArgb(255, 196, 43, 28)
        };
    }

    private Windows.UI.Color GetStatusBadgeBackgroundColor()
    {
        return Service?.State switch
        {
            "Running" => ColorHelper.FromArgb(26, 16, 124, 16),
            "Stopped" => ColorHelper.FromArgb(22, 115, 115, 115),
            "Paused" => ColorHelper.FromArgb(28, 157, 93, 0),
            "Start Pending" or "Stop Pending" or "Continue Pending" or "Pause Pending" => ColorHelper.FromArgb(28, 139, 105, 20),
            null when IsServerAgentSetupCandidate => ColorHelper.FromArgb(18, 96, 96, 96),
            null => ColorHelper.FromArgb(18, 96, 96, 96),
            _ => ColorHelper.FromArgb(28, 196, 43, 28)
        };
    }

    private Windows.UI.Color GetStatusChipBackgroundColor()
    {
        return Service?.State switch
        {
            "Running" => ColorHelper.FromArgb(24, 16, 124, 16),
            "Stopped" => ColorHelper.FromArgb(32, 115, 115, 115),
            "Paused" => ColorHelper.FromArgb(38, 157, 93, 0),
            "Start Pending" or "Stop Pending" or "Continue Pending" or "Pause Pending" => ColorHelper.FromArgb(38, 139, 105, 20),
            null when IsServerAgentSetupCandidate => ColorHelper.FromArgb(24, 96, 96, 96),
            null => ColorHelper.FromArgb(24, 96, 96, 96),
            _ => ColorHelper.FromArgb(38, 196, 43, 28)
        };
    }

    private Windows.UI.Color GetStatusTextColor()
    {
        return Service?.State switch
        {
            "Running" => ColorHelper.FromArgb(255, 10, 95, 10),
            "Stopped" => ColorHelper.FromArgb(255, 67, 67, 67),
            "Paused" => ColorHelper.FromArgb(255, 104, 62, 0),
            "Start Pending" or "Stop Pending" or "Continue Pending" or "Pause Pending" => ColorHelper.FromArgb(255, 92, 70, 14),
            null when IsServerAgentSetupCandidate => ColorHelper.FromArgb(255, 67, 67, 67),
            null => ColorHelper.FromArgb(255, 67, 67, 67),
            _ => ColorHelper.FromArgb(255, 132, 28, 19)
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
