using AdminConsoleFor1C.Infrastructure.Services;

namespace AdminConsoleFor1C.App;

internal static class AdminConsoleViewModelFactory
{
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
