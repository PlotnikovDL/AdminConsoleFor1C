using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace AdminConsoleFor1C.App;

public sealed partial class ProcessesPageViewModel : ObservableObject
{
    private readonly OneCProcessInventoryReader inventoryReader;
    private readonly AgentComponentPresentationBuilder componentBuilder;

    public ProcessesPageViewModel(
        IOneCProcessInventory processInventory,
        IOneCServiceInventory serviceInventory,
        AgentComponentPresentationBuilder componentBuilder)
    {
        inventoryReader = new(processInventory, serviceInventory);
        this.componentBuilder = componentBuilder;
    }

    public ObservableCollection<OneCProcessItemViewModel> Processes { get; } = [];

    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [NotifyPropertyChangedFor(nameof(RefreshProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [ObservableProperty]
    public partial bool HasLoaded { get; set; }

    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [ObservableProperty]
    public partial int ProcessCount { get; set; }

    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ErrorInfoBarVisibility))]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    [NotifyPropertyChangedFor(nameof(HasServiceCorrelationWarning))]
    [NotifyPropertyChangedFor(nameof(ServiceCorrelationWarningVisibility))]
    [ObservableProperty]
    public partial string? ServiceCorrelationWarningText { get; set; }

    public string HeaderSubtitle
    {
        get
        {
            if (IsRefreshing)
            {
                return "Обновление списка процессов";
            }

            if (HasError)
            {
                return "Не удалось обновить процессы 1С";
            }

            if (!HasLoaded)
            {
                return "Сведения о процессах еще не загружены";
            }

            return Processes.Count == 0
                ? "Нет данных о процессах 1С"
                : $"Найдено {AgentComponentTextFormatter.FormatProcessCount(ProcessCount)}";
        }
    }

    public string ProcessesStatusText => IsRefreshing ? "Обновление" : "Нет данных о процессах 1С";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool HasServiceCorrelationWarning => !string.IsNullOrWhiteSpace(ServiceCorrelationWarningText);

    public Visibility ServiceCorrelationWarningVisibility => HasServiceCorrelationWarning
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ErrorInfoBarVisibility => HasError
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RefreshProgressVisibility => IsRefreshing
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ProcessesListVisibility => Processes.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EmptyStateVisibility => HasLoaded && !IsRefreshing && !HasError && Processes.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;

        try
        {
            ErrorText = null;
            ServiceCorrelationWarningText = null;

            var snapshot = await inventoryReader.ReadAsync();
            if (!snapshot.IsServiceCorrelationAvailable)
            {
                ServiceCorrelationWarningText = "Список процессов загружен. Сведения о службах Windows недоступны, поэтому сопоставление со службами не выполнено."
                    + (string.IsNullOrWhiteSpace(snapshot.ServiceCorrelationError) ? string.Empty : $"\n{snapshot.ServiceCorrelationError}");
            }
            var orderedProcesses = componentBuilder.BuildProcessItems(snapshot.Processes, snapshot.IsServiceCorrelationAvailable);

            Processes.Clear();
            foreach (var process in orderedProcesses)
            {
                Processes.Add(process);
            }

            ProcessCount = Processes.Count;
            HasLoaded = true;
            NotifyProcessStateChanged();
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
            HasLoaded = true;
            NotifyProcessStateChanged();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private bool CanRefresh() => !IsRefreshing;

    partial void OnIsRefreshingChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private void NotifyProcessStateChanged()
    {
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(ProcessesStatusText));
        OnPropertyChanged(nameof(ProcessesListVisibility));
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }
}
