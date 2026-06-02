namespace AdminConsoleFor1C.Application.Services;

public interface IOneCAdministrationServerLauncher
{
    Task<int> StartTemporaryAsync(
        string rasPath,
        int administrationServerPort,
        string agentAddress,
        CancellationToken cancellationToken = default);

    Task StopTemporaryAsync(int processId, CancellationToken cancellationToken = default);
}
