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
        LicenseClusters = Clusters
            .Where(static cluster => cluster.OccupiedLicenseUsages.Count > 0)
            .ToList();
    }

    public IReadOnlyList<OneCClusterViewModel> Clusters { get; }

    public IReadOnlyList<OneCClusterViewModel> LicenseClusters { get; }

    public string StatusText => _result.IsAvailable
        ? _result.Clusters.Count == 0 ? "Пусто" : "Доступны"
        : "Недоступны";

    public string MessageText => _result.Message;

    public string LicenseSummaryText
    {
        get
        {
            if (!_result.IsAvailable)
            {
                return "Лицензии недоступны";
            }

            var usages = Clusters
                .SelectMany(static cluster => cluster.OccupiedLicenseUsages)
                .ToList();

            if (usages.Count == 0)
            {
                return "Занятые лицензии не обнаружены";
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
                var sessionCount = Clusters.Sum(static cluster => cluster.SessionLicenses.Count);
                parts.Add($"Клиентские места: {FormatUsage(clientUsages)}, сеансов: {sessionCount}");
            }

            if (serverUsages.Count > 0)
            {
                parts.Add($"Серверные лицензии: {FormatUsage(serverUsages)}");
            }

            return string.Join("; ", parts);
        }
    }

    public string AddressText => _result.AdministrationServerAddress;

    public string AgentAddressText { get; }

    public string CommandText => _result.CommandText;

    public Visibility ClustersVisibility => _result.Clusters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MessageVisibility => _result.Clusters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LicenseClustersVisibility => LicenseClusters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LicensesEmptyStateVisibility => LicenseClusters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StartTemporaryRasVisibility => _canStartTemporaryRas ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StopTemporaryRasVisibility => _canStopTemporaryRas ? Visibility.Visible : Visibility.Collapsed;

    public Brush StatusAccentBrush => new SolidColorBrush(_result.IsAvailable
        ? ColorHelper.FromArgb(255, 16, 124, 65)
        : ColorHelper.FromArgb(255, 157, 93, 0));

    public Brush StatusBadgeBackgroundBrush => new SolidColorBrush(_result.IsAvailable
        ? ColorHelper.FromArgb(28, 16, 124, 65)
        : ColorHelper.FromArgb(28, 157, 93, 0));

    private static string FormatUsage(IReadOnlyList<OneCLicenseUsageInfo> usages)
    {
        var occupied = usages.Sum(static usage => usage.OccupiedSeats);
        var capacity = usages.Any(static usage => usage.Capacity is null)
            ? null
            : usages.Sum(static usage => usage.Capacity);

        return capacity is null ? occupied.ToString() : $"{occupied}/{capacity}";
    }
}
