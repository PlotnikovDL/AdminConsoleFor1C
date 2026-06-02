using AdminConsoleFor1C.Core.Administration;
using Microsoft.UI.Xaml;

namespace AdminConsoleFor1C.App;

public sealed class OneCClusterViewModel
{
    private readonly OneCClusterInfo _cluster;

    public OneCClusterViewModel(OneCClusterInfo cluster)
    {
        _cluster = cluster;
    }

    public IReadOnlyList<OneCClusterServerInfo> Servers => _cluster.Servers;

    public IReadOnlyList<OneCInfobaseSummaryInfo> Infobases => _cluster.Infobases;

    public string NameText => _cluster.NameText;

    public string UuidText => _cluster.UuidText;

    public string AddressText => _cluster.AddressText;

    public string SecurityLevelText => _cluster.SecurityLevelText;

    public string LoadBalancingModeText => _cluster.LoadBalancingModeText;

    public string ServersSummaryText => _cluster.ServersSummaryText;

    public string InfobasesSummaryText => _cluster.InfobasesSummaryText;

    public string DetailsMessageText => _cluster.DetailsMessage ?? string.Empty;

    public Visibility ServersVisibility => Servers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility InfobasesVisibility => Infobases.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility InfobaseEmptyStateVisibility => Infobases.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DetailsMessageVisibility => string.IsNullOrWhiteSpace(_cluster.DetailsMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;
}
