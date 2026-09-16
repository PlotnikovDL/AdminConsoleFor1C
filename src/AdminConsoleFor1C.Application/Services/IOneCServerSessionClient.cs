using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Application.Services;

public interface IOneCServerSessionClient
{
    Task<OneCServerSessionSnapshot> ReadAsync(OneCServerConnectionProfile profile, string? password,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OneCSessionTerminationResult>> TerminateAsync(OneCServerConnectionProfile profile,
        string? password, IReadOnlyList<OneCSessionTarget> targets, string message,
        CancellationToken cancellationToken = default);
}

public interface IOneCServerConnectionStore
{
    Task<IReadOnlyList<OneCServerConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<OneCServerConnectionProfile> profiles, CancellationToken cancellationToken = default);
}
