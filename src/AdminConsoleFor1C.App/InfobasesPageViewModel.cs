using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class InfobasesPageViewModel(IOneCInfobaseClient client, SessionsPageViewModel connections) : ObservableObject
{
    public SessionsPageViewModel ConnectionOwner { get; } = connections;
    public ObservableCollection<ServerConnectionItemViewModel> Connections => ConnectionOwner.Connections;
    public ObservableCollection<InfobaseClusterChoice> Clusters { get; } = [];
    public ObservableCollection<OneCInfobaseSummaryInfo> Infobases { get; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyPropertyChangedFor(nameof(CanCreate)), NotifyPropertyChangedFor(nameof(CanRefresh))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCreate)), NotifyPropertyChangedFor(nameof(CanRefresh)), NotifyPropertyChangedFor(nameof(ServerDetails))]
    public partial ServerConnectionItemViewModel? SelectedServer { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial InfobaseClusterChoice? SelectedCluster { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "Выберите сервер и кластер для просмотра или создания баз";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] public partial string? Message { get; set; }
    [ObservableProperty] public partial InfoBarSeverity MessageSeverity { get; set; } = InfoBarSeverity.Informational;
    public bool CanEdit => !IsBusy;
    public bool CanRefresh => !IsBusy && SelectedServer is not null;
    public bool CanCreate => CanRefresh && SelectedCluster is { Cluster.DetailsMessage: null or "" }
        && (SelectedServer?.LocalService is null or { State: "Running" });
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public string ServerDetails => SelectedServer is { } server
        ? $"{server.SourceText}   ·   Агент: {server.Profile.AgentAddress}   ·   Платформа: {server.Profile.PlatformVersion}" : "Сервер не выбран";

    public async Task LoadAsync()
    {
        await ConnectionOwner.LoadAsync();
        if (!ConnectionOwner.IsLoaded) ShowMessage(ConnectionOwner.Message ?? "Не удалось загрузить подключения.", InfoBarSeverity.Error);
    }

    public async Task RefreshServersAsync()
    {
        await ConnectionOwner.RefreshLocalConnectionsAsync();
        OnPropertyChanged(nameof(ServerDetails));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnSelectedServerChanged(ServerConnectionItemViewModel? value)
    {
        SelectedCluster = null;
        Clusters.Clear();
        Infobases.Clear();
        Message = null;
        Status = value is null ? "Выберите сервер" : "Нажмите «Обновить», чтобы получить кластеры выбранного сервера";
        RefreshCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedClusterChanged(InfobaseClusterChoice? value)
    {
        RebuildInfobases();
        if (!string.IsNullOrWhiteSpace(value?.Cluster.DetailsMessage)) ShowMessage(value.Cluster.DetailsMessage, InfoBarSeverity.Error);
        else Message = null;
    }
    partial void OnSearchTextChanged(string value) => RebuildInfobases();
    partial void OnIsBusyChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanEdit))]
    public async Task RefreshAsync()
    {
        if (!CanEdit) return;
        await RefreshServersAsync();
        await RefreshSelectedServerAsync();
    }

    public async Task RefreshSelectedServerAsync()
    {
        if (!CanRefresh || SelectedServer is not { } server) return;
        var selectedId = SelectedCluster?.Cluster.Uuid;
        IsBusy = true;
        Message = null;
        SelectedCluster = null;
        Clusters.Clear();
        Infobases.Clear();
        Status = $"Подключение к {server.Profile.AgentAddress}…";
        try
        {
            if (server.LocalService is { State: not "Running" })
                throw new InvalidOperationException("Локальная служба агента не работает. Запустите её в разделе «Службы», затем обновите список.");
            var result = await client.ReadAsync(server.Profile, ConnectionOwner.GetPassword(server.Profile.Id));
            foreach (var cluster in result) Clusters.Add(new(cluster));
            SelectedCluster = Clusters.FirstOrDefault(c => c.Cluster.Uuid == selectedId) ?? (Clusters.Count == 1 ? Clusters[0] : null);
            Status = Clusters.Count == 0 ? "На выбранном сервере нет кластеров"
                : $"Версия платформы проверена · Кластеров: {Clusters.Count} · Обновлено: {DateTime.Now:HH:mm}";
        }
        catch (Exception ex) { Status = "Не удалось подключиться к серверу"; ShowMessage(ex.Message, InfoBarSeverity.Error); }
        finally { IsBusy = false; }
    }

    public void ShowMessage(string text, InfoBarSeverity severity) { MessageSeverity = severity; Message = text; }
    public string ListSummary => SelectedCluster is null ? "Новая база создаётся пустой, без конфигурации."
        : $"Баз: {Infobases.Count} из {SelectedCluster.Cluster.Infobases.Count} · Новая база создаётся пустой, без конфигурации.";
    private void RebuildInfobases()
    {
        Infobases.Clear();
        foreach (var infobase in (SelectedCluster?.Cluster.Infobases ?? [])
            .Where(b => string.IsNullOrWhiteSpace(SearchText) || b.NameText.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || b.DescriptionText.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(b => b.NameText, StringComparer.CurrentCultureIgnoreCase)) Infobases.Add(infobase);
        OnPropertyChanged(nameof(ListSummary));
    }
}

public sealed record InfobaseClusterChoice(OneCClusterInfo Cluster)
{
    public override string ToString() => $"{Cluster.NameText} · {Cluster.AddressText}";
}
