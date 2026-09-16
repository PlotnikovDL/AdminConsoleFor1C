using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Infrastructure.Services;

public interface IOneCRasSession : IAsyncDisposable
{
    Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public interface IOneCRasSessionFactory
{
    Task<IOneCRasSession> OpenAsync(OneCServerConnectionProfile profile, CancellationToken cancellationToken);
}

public sealed class RacServerSessionClient(IOneCRasSessionFactory factory) : IOneCServerSessionClient
{
    public async Task<OneCServerSessionSnapshot> ReadAsync(OneCServerConnectionProfile profile, string? password,
        CancellationToken cancellationToken = default)
    {
        profile.Validate();
        await using var connection = await factory.OpenAsync(profile, cancellationToken);
        var output = await RunAsync(connection, ["cluster", "list"], password, cancellationToken);
        var clusters = OneCRacOutputParser.ParseObjects(output).Select(OneCClusterInfo.FromProperties)
            .Where(c => Guid.TryParse(c.Uuid, out _)).ToList();
        var result = new List<OneCClusterInfo>();
        foreach (var cluster in clusters)
        {
            IReadOnlyList<OneCInfobaseSummaryInfo> infobases = [];
            IReadOnlyList<OneCSessionInfo> sessions = [];
            var errors = new List<string>();
            try
            {
                var text = await RunAsync(connection, WithAuthentication(profile, password, cluster.Uuid,
                    ["infobase", "summary", "list"]), password, cancellationToken);
                infobases = OneCRacOutputParser.ParseObjects(text).Select(OneCInfobaseSummaryInfo.FromProperties).ToArray();
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add("Базы: " + ex.Message); }
            try
            {
                var text = await RunAsync(connection, WithAuthentication(profile, password, cluster.Uuid,
                    ["session", "list"]), password, cancellationToken);
                sessions = OneCRacOutputParser.ParseObjects(text).Select(OneCSessionInfo.FromProperties)
                    .Where(s => Guid.TryParse(s.Uuid, out _)).ToArray();
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add("Сеансы: " + ex.Message); }
            result.Add(cluster with { Infobases = infobases, Sessions = sessions, DetailsMessage = string.Join("\n", errors) });
        }
        return new(result, DateTimeOffset.Now);
    }

    public async Task<IReadOnlyList<OneCSessionTerminationResult>> TerminateAsync(OneCServerConnectionProfile profile,
        string? password, IReadOnlyList<OneCSessionTarget> targets, string message, CancellationToken cancellationToken = default)
    {
        profile.Validate();
        foreach (var target in targets) target.Validate(profile);
        if (targets.Count == 0) return [];
        await using var connection = await factory.OpenAsync(profile, cancellationToken);
        var results = new List<OneCSessionTerminationResult>();
        foreach (var target in targets.DistinctBy(t => (t.ClusterUuid, t.Session.Uuid)))
        {
            try
            {
                // Re-read the exact session before mutation. Never turn a filtered list into "terminate all".
                var text = await RunAsync(connection, WithAuthentication(profile, password, target.ClusterUuid,
                    ["session", "info", $"--session={target.Session.Uuid}"]), password, cancellationToken);
                var current = OneCRacOutputParser.ParseObjects(text).Select(OneCSessionInfo.FromProperties)
                    .SingleOrDefault(s => s.Uuid == target.Session.Uuid);
                if (current is null || !target.Matches(current))
                    throw new InvalidOperationException("Сеанс исчез или изменился. Обновите список; завершение отменено.");
                await RunAsync(connection, WithAuthentication(profile, password, target.ClusterUuid,
                    ["session", "terminate", $"--session={target.Session.Uuid}", $"--error-message={message}"]), password, cancellationToken);
                results.Add(new(target.Session.Uuid, true, "Сеанс завершён"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new(target.Session.Uuid, false, Redact(ex.Message, password)));
            }
        }
        return results;
    }

    private static string[] WithAuthentication(OneCServerConnectionProfile profile, string? password,
        string cluster, string[] command)
    {
        if (!Guid.TryParse(cluster, out _)) throw new ArgumentException("Некорректный идентификатор кластера.");
        var arguments = command.Append($"--cluster={cluster}");
        if (!string.IsNullOrWhiteSpace(profile.ClusterUser))
            arguments = arguments.Concat([$"--cluster-user={profile.ClusterUser}", $"--cluster-pwd={password ?? string.Empty}"]);
        return arguments.ToArray();
    }

    private static async Task<string> RunAsync(IOneCRasSession session, string[] arguments, string? password, CancellationToken token)
    {
        try { return await session.RunAsync(arguments, token); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // rac accepts credentials only as arguments; never echo them to the UI, files or diagnostics.
            throw new InvalidOperationException(Redact(ex.Message, password));
        }
    }

    private static string Redact(string message, string? password) => string.IsNullOrEmpty(password)
        ? message : message.Replace(password, "***", StringComparison.Ordinal);
}
