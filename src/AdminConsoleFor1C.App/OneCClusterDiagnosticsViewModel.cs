using AdminConsoleFor1C.Core.Administration;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AdminConsoleFor1C.App;

public sealed class OneCClusterDiagnosticsViewModel
{
    private readonly OneCClusterInventoryResult _result;
    private readonly bool _canStartTemporaryRas;
    private readonly bool _canStopTemporaryRas;

    public OneCClusterDiagnosticsViewModel(
        OneCClusterInventoryResult result,
        string agentAddress,
        bool canStartTemporaryRas,
        bool canStopTemporaryRas)
    {
        _result = result;
        AgentAddressText = agentAddress;
        _canStartTemporaryRas = canStartTemporaryRas;
        _canStopTemporaryRas = canStopTemporaryRas;
        Clusters = result.Clusters
            .Select(static cluster => new OneCClusterViewModel(cluster))
            .ToList();
    }

    public IReadOnlyList<OneCClusterViewModel> Clusters { get; }

    public string StatusText => _result.IsAvailable
        ? _result.Clusters.Count == 0 ? "Пусто" : "Доступны"
        : "Недоступны";

    public string MessageText => _result.Message;

    public string AddressText => _result.AdministrationServerAddress;

    public string AgentAddressText { get; }

    public string CommandText => _result.CommandText;

    public Visibility ClustersVisibility => _result.Clusters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MessageVisibility => _result.Clusters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StartTemporaryRasVisibility => _canStartTemporaryRas ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StopTemporaryRasVisibility => _canStopTemporaryRas ? Visibility.Visible : Visibility.Collapsed;

    public Brush StatusAccentBrush => new SolidColorBrush(_result.IsAvailable
        ? ColorHelper.FromArgb(255, 16, 124, 65)
        : ColorHelper.FromArgb(255, 157, 93, 0));

    public Brush StatusBadgeBackgroundBrush => new SolidColorBrush(_result.IsAvailable
        ? ColorHelper.FromArgb(28, 16, 124, 65)
        : ColorHelper.FromArgb(28, 157, 93, 0));
}
