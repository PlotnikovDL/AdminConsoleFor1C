using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AdminConsoleFor1C.App;

public sealed class AdminConsoleWorkspaceViewModel : INotifyPropertyChanged
{
    private OneCAdministrationToolDiagnosticsViewModel? _administrationToolDiagnostics;
    private OneCClusterDiagnosticsViewModel? _clusterDiagnostics;
    private OneCServiceProcessNode? _selectedNode;
    private int? _temporaryRasProcessId;
    private string _agentsStatusText = "Готово";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<OneCServiceProcessNode> Nodes { get; } = [];

    public OneCAdministrationToolDiagnosticsViewModel? AdministrationToolDiagnostics
    {
        get => _administrationToolDiagnostics;
        set => SetProperty(ref _administrationToolDiagnostics, value);
    }

    public OneCClusterDiagnosticsViewModel? ClusterDiagnostics
    {
        get => _clusterDiagnostics;
        set => SetProperty(ref _clusterDiagnostics, value);
    }

    public OneCServiceProcessNode? SelectedNode
    {
        get => _selectedNode;
        private set => SetProperty(ref _selectedNode, value);
    }

    public int? TemporaryRasProcessId
    {
        get => _temporaryRasProcessId;
        set => SetProperty(ref _temporaryRasProcessId, value);
    }

    public string AgentsStatusText
    {
        get => _agentsStatusText;
        set => SetProperty(ref _agentsStatusText, value);
    }

    public void ReplaceNodes(IEnumerable<OneCServiceProcessNode> nodes)
    {
        Nodes.Clear();
        foreach (var node in nodes)
        {
            Nodes.Add(node);
        }
    }

    public void SelectNode(OneCServiceProcessNode? node)
    {
        if (SelectedNode is not null)
        {
            SelectedNode.IsSelected = false;
        }

        SelectedNode = node;

        if (SelectedNode is not null)
        {
            SelectedNode.IsSelected = true;
        }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
