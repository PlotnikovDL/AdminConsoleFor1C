using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Application.Services;

public interface IOneCInfobaseClient
{
    Task<IReadOnlyList<OneCClusterInfo>> ReadAsync(OneCServerConnectionProfile profile, string? password,
        CancellationToken cancellationToken = default);
    Task<string> CreateAsync(OneCServerConnectionProfile profile, OneCInfobaseCreationTarget target,
        OneCEmptyInfobaseOptions options, string? clusterPassword, string? databasePassword,
        CancellationToken cancellationToken = default);
}

// Once the command was sent, a transport error cannot prove that creation failed.
public sealed class OneCInfobaseCreationUncertainException(string message) : Exception(message);
