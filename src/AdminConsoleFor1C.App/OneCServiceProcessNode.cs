using System.ComponentModel;
using System.Runtime.CompilerServices;
using AdminConsoleFor1C.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AdminConsoleFor1C.App;

public sealed class OneCServiceProcessNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required OneCServiceInfo? Service { get; init; }

    public required IReadOnlyList<OneCProcessInfo> Processes { get; init; }

    public bool IsOrphanGroup => Service is null;

    public string Title => Service?.KindDisplayName ?? "Процессы без службы";

    public string WindowsServicesDisplayNameText => Service?.DisplayNameText
        ?? "Запущены, но не сопоставлены со службой Windows";

    public string TaskManagerServiceNameText => Service?.Name ?? string.Empty;

    public Visibility TaskManagerServiceNameVisibility => string.IsNullOrWhiteSpace(TaskManagerServiceNameText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility ServiceDetailsVisibility => Service is not null && IsExpanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ProcessesVisibility => Processes.Count > 0 && IsExpanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ProcessSummaryVisibility => Processes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ServiceActionsVisibility => Service is null ? Visibility.Collapsed : Visibility.Visible;

    public bool CanStartService => Service?.State == "Stopped";

    public bool CanStopService => Service?.State is "Running" or "Paused";

    public bool CanRestartService => Service?.State == "Running";

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandGlyph));
            OnPropertyChanged(nameof(ServiceDetailsVisibility));
            OnPropertyChanged(nameof(ProcessesVisibility));
        }
    }

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

    public string ExpandGlyph => IsExpanded ? "−" : "+";

    public Brush CardBorderBrush => new SolidColorBrush(IsSelected
        ? ColorHelper.FromArgb(255, 0, 95, 184)
        : ColorHelper.FromArgb(28, 0, 0, 0));

    public Brush CardBackgroundBrush => new SolidColorBrush(IsSelected
        ? ColorHelper.FromArgb(18, 0, 95, 184)
        : ColorHelper.FromArgb(0, 0, 0, 0));

    public Thickness CardBorderThickness => IsSelected ? new Thickness(2) : new Thickness(1);

    public string StateDisplayName => Service?.StateDisplayName ?? "—";

    public Brush StatusAccentBrush => new SolidColorBrush(GetStatusAccentColor());

    public Brush StatusBadgeBackgroundBrush => new SolidColorBrush(GetStatusBadgeBackgroundColor());

    public string VersionText => Service?.VersionText ?? GetFirstProcessValue(static process => process.VersionText);

    public string PortsText => Service?.PortsText ?? GetFirstProcessValue(static process => process.PortsText);

    public string ProcessIdText => Service?.ProcessIdText ?? GetFirstProcessValue(static process => process.ProcessIdText);

    public string ProcessIdSummaryText => ProcessIdText == "—" ? "PID —" : $"PID {ProcessIdText}";

    public string StartModeDisplayName => Service?.StartModeDisplayName ?? "—";

    public string AccountText => Service?.AccountText ?? GetFirstProcessValue(static process => process.OwnerText);

    public string DataDirectoryText => Service?.DataDirectoryText ?? "—";

    public string ExecutablePathText => Service?.ExecutablePathText ?? "—";

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

    private Windows.UI.Color GetStatusAccentColor()
    {
        return Service?.State switch
        {
            "Running" => ColorHelper.FromArgb(255, 16, 124, 65),
            "Stopped" => ColorHelper.FromArgb(255, 115, 115, 115),
            "Paused" => ColorHelper.FromArgb(255, 157, 93, 0),
            "Start Pending" or "Stop Pending" or "Continue Pending" or "Pause Pending" => ColorHelper.FromArgb(255, 139, 105, 20),
            null => ColorHelper.FromArgb(255, 96, 96, 96),
            _ => ColorHelper.FromArgb(255, 196, 43, 28)
        };
    }

    private Windows.UI.Color GetStatusBadgeBackgroundColor()
    {
        return Service?.State switch
        {
            "Running" => ColorHelper.FromArgb(28, 16, 124, 65),
            "Stopped" => ColorHelper.FromArgb(22, 115, 115, 115),
            "Paused" => ColorHelper.FromArgb(28, 157, 93, 0),
            "Start Pending" or "Stop Pending" or "Continue Pending" or "Pause Pending" => ColorHelper.FromArgb(28, 139, 105, 20),
            null => ColorHelper.FromArgb(18, 96, 96, 96),
            _ => ColorHelper.FromArgb(28, 196, 43, 28)
        };
    }

    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
