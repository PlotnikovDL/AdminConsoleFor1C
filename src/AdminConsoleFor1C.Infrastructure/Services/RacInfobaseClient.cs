using System.Text.RegularExpressions;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Infrastructure.Services;

public sealed class RacInfobaseClient(IOneCRasSessionFactory factory) : IOneCInfobaseClient
{
    public async Task<IReadOnlyList<OneCClusterInfo>> ReadAsync(OneCServerConnectionProfile profile, string? password,
        CancellationToken cancellationToken = default)
    {
        profile.Validate();
        try
        {
            await using var session = await factory.OpenAsync(profile, cancellationToken);
            await CheckVersionAsync(session, profile, cancellationToken);
            var clusters = await ReadClustersAsync(session, cancellationToken);
            var result = new List<OneCClusterInfo>();
            foreach (var cluster in clusters)
            {
                try
                {
                    var text = await session.RunAsync(Authenticate(profile, password, cluster.Uuid,
                        ["infobase", "summary", "list"]), cancellationToken);
                    result.Add(cluster with { Infobases = ParseInfobases(text) });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { result.Add(cluster with { DetailsMessage = Redact(ex.Message, password) }); }
            }
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new InvalidOperationException(Redact(ex.Message, password)); }
    }

    public async Task<string> CreateAsync(OneCServerConnectionProfile profile, OneCInfobaseCreationTarget target,
        OneCEmptyInfobaseOptions options, string? clusterPassword, string? databasePassword,
        CancellationToken cancellationToken = default)
    {
        profile.Validate();
        options.Validate();
        if (profile.Id != target.ConnectionId || !Guid.TryParse(target.Cluster.Uuid, out _))
            throw new ArgumentException("Выбранный кластер не относится к этому подключению.");
        var sent = false;
        try
        {
            await using var session = await factory.OpenAsync(profile, cancellationToken);
            await CheckVersionAsync(session, profile, cancellationToken);
            var clusters = await ReadClustersAsync(session, cancellationToken);
            var current = clusters.SingleOrDefault(c => c.Uuid == target.Cluster.Uuid);
            if (current is null || !string.Equals(current.Host, target.Cluster.Host, StringComparison.OrdinalIgnoreCase)
                || current.Port != target.Cluster.Port)
                throw new InvalidOperationException("Кластер исчез или его адрес изменился. Обновите список и выберите кластер заново.");
            var existing = await session.RunAsync(Authenticate(profile, clusterPassword, current.Uuid,
                ["infobase", "summary", "list"]), cancellationToken);
            if (ParseInfobases(existing).Any(b => string.Equals(b.Name, options.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"В выбранном кластере уже есть база «{options.Name}».");
            var args = new List<string>
            {
                "infobase", "create", $"--name={options.Name}", $"--dbms={options.Dbms}",
                $"--db-server={options.DbServer}", $"--db-name={options.DbName}", $"--locale={options.Locale}",
                $"--db-user={options.DbUser}", $"--db-pwd={databasePassword ?? ""}", $"--descr={options.Description}",
                $"--date-offset={options.DateOffset}", $"--security-level={options.SecurityLevel}",
                $"--scheduled-jobs-deny={(options.ScheduledJobsDeny ? "on" : "off")}",
                $"--license-distribution={(options.LicenseDistribution ? "allow" : "deny")}"
            };
            if (options.CreateDatabase) args.Add("--create-database");
            cancellationToken.ThrowIfCancellationRequested();
            sent = true;
            var output = await session.RunAsync(Authenticate(profile, clusterPassword, current.Uuid, args), cancellationToken);
            var created = ParseInfobases(output).SingleOrDefault(b => Guid.TryParse(b.Uuid, out _));
            if (created is null) throw new InvalidOperationException("Сервер не вернул идентификатор созданной базы.");
            return created.Uuid;
        }
        catch (Exception ex) when (sent)
        {
            throw new OneCInfobaseCreationUncertainException(
                "Команда создания отправлена, но результат не подтверждён. Обновите список баз и проверьте СУБД перед повторной попыткой.\n"
                + Redact(ex.Message, clusterPassword, databasePassword));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new InvalidOperationException(Redact(ex.Message, clusterPassword, databasePassword)); }
    }

    private static async Task CheckVersionAsync(IOneCRasSession session, OneCServerConnectionProfile profile, CancellationToken token)
    {
        var output = await session.RunAsync(["agent", "version"], token);
        var version = Regex.Match(output, @"(?m)^\s*(?:version\s*:\s*)?""?(\d+\.\d+\.\d+\.\d+)""?\s*$");
        if (!version.Success) throw new InvalidOperationException("Не удалось определить версию агента сервера 1С.");
        if (version.Groups[1].Value != profile.PlatformVersion)
            throw new InvalidOperationException($"На сервере {profile.AgentAddress} установлена платформа {version.Groups[1].Value}, "
                + $"а в подключении выбрана {profile.PlatformVersion}. Выберите соответствующую версию и порт агента.");
    }

    private static async Task<IReadOnlyList<OneCClusterInfo>> ReadClustersAsync(IOneCRasSession session, CancellationToken token)
        => OneCRacOutputParser.ParseObjects(await session.RunAsync(["cluster", "list"], token))
            .Select(OneCClusterInfo.FromProperties).Where(c => Guid.TryParse(c.Uuid, out _)).ToArray();
    private static OneCInfobaseSummaryInfo[] ParseInfobases(string output) => OneCRacOutputParser.ParseObjects(output)
        .Select(OneCInfobaseSummaryInfo.FromProperties).ToArray();
    private static string[] Authenticate(OneCServerConnectionProfile profile, string? password, string cluster, IEnumerable<string> args)
    {
        var result = args.Append($"--cluster={cluster}");
        if (!string.IsNullOrWhiteSpace(profile.ClusterUser))
            result = result.Concat([$"--cluster-user={profile.ClusterUser}", $"--cluster-pwd={password ?? ""}"]);
        return result.ToArray();
    }
    private static string Redact(string text, params string?[] secrets)
    {
        foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s!.Length))
            text = text.Replace(secret!, "***", StringComparison.Ordinal);
        return text;
    }
}
