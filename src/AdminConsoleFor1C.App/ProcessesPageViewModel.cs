using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace AdminConsoleFor1C.App;

public sealed partial class ProcessesPageViewModel : ObservableObject
{
    private readonly IOneCProcessInventory processInventory;

    public ProcessesPageViewModel(IOneCProcessInventory processInventory)
    {
        this.processInventory = processInventory;
    }

    public ObservableCollection<AgentProcessItemViewModel> Processes { get; } = [];

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

            var processes = await processInventory.GetProcessesAsync();
            var orderedProcesses = processes
                .OrderBy(static process => process.Kind)
                .ThenBy(static process => process.ProcessId)
                .Select(AgentProcessItemViewModel.FromProcess)
                .ToList();

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
