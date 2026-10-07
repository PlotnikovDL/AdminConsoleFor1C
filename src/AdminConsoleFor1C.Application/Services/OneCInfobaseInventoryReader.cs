using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Application.Services;

public sealed record OneCInfobaseServerInventory(OneCServerConnectionProfile Profile,
    IReadOnlyList<OneCClusterInfo> Clusters, string? Error, DateTimeOffset UpdatedAt);

public sealed class OneCInfobaseInventoryReader(IOneCInfobaseClient client)
{
    public async Task<IReadOnlyList<OneCInfobaseServerInventory>> ReadAsync(
        IReadOnlyList<OneCServerConnectionProfile> profiles, Func<Guid, string?> credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(credentials);
        cancellationToken.ThrowIfCancellationRequested();
        using var gate = new SemaphoreSlim(3);
        return await Task.WhenAll(profiles.Select(ReadServerAsync));

        async Task<OneCInfobaseServerInventory> ReadServerAsync(OneCServerConnectionProfile profile)
        {
            await gate.WaitAsync(cancellationToken);
            string? password = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                password = credentials(profile.Id);
                var clusters = await client.ReadAsync(profile, password, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return new(profile, clusters, null, DateTimeOffset.Now);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var error = string.IsNullOrEmpty(password) ? ex.Message
                    : ex.Message.Replace(password, "***", StringComparison.Ordinal);
                return new(profile, [], error, DateTimeOffset.Now);
            }
            finally { gate.Release(); }
        }
    }
}
