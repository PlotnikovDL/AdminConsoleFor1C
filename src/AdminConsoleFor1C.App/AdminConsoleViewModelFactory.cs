using AdminConsoleFor1C.Infrastructure.Services;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.App;

internal static class AdminConsoleViewModelFactory
{
    private static SessionsPageViewModel? sessions;
    public static SessionsPageViewModel GetSessionsPageViewModel() => sessions ??= new(
        new RacServerSessionClient(new TemporaryRasSessionFactory()),
        new JsonOneCServerConnectionStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdminConsoleFor1C", "server-connections.json")));

    public static ServerConnectionEditorViewModel CreateConnectionEditorViewModel(
        AdminConsoleFor1C.Core.Administration.OneCServerConnectionProfile? profile) => new(profile,
            new WindowsOneCServiceInventory(), new WindowsOneCAdministrationToolInventory());

    public static string GetAdministrationPlatformVersion(string directory) => TemporaryRasSessionFactory.GetPlatformVersion(directory);

    public static ServiceRegistrationViewModel CreateServiceRegistrationViewModel(
        IReadOnlyList<OneCServiceInfo> services, IEnumerable<string> paths, string? selectedPath)
        => new(services, paths, selectedPath, new WindowsUserAccountInventory());

    public static AgentsPageViewModel CreateAgentsPageViewModel()
    {
        return new AgentsPageViewModel(
            new WindowsOneCServiceInventory(),
            new WindowsOneCProcessInventory(),
            new WindowsOneCServiceCandidateInventory(),
            new ElevatedWorkerOneCServiceController(
                ElevatedWorkerPaths.ResolveWorkerPath(),
                ElevatedWorkerPaths.ResolveResultDirectory()),
            new AgentComponentPresentationBuilder());
    }

    public static ProcessesPageViewModel CreateProcessesPageViewModel()
    {
        return new ProcessesPageViewModel(
            new WindowsOneCProcessInventory(),
            new AgentComponentPresentationBuilder());
    }
}
