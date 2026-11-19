using System.Text.Json;
using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Entities;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Application.Providers;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class SourceRefreshService(
    ILeagueDataProvider leagueProvider,
    IPerformanceDataProvider performanceProvider,
    IInjuryDataProvider injuryProvider,
    IPlayerIdentityResolver? identityResolver = null)
{
    public async Task<SourceRefreshResult> RefreshAsync(
        ManagerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var stateResult = await CaptureAsync(
            leagueProvider.ProviderName,
            () => leagueProvider.GetStateAsync(cancellationToken));
        
        var currentWeek = int.TryParse(stateResult.Value?.Week, out var parsedWeek) ? parsedWeek : 0;
        
        var leagueTask = CaptureAsync(leagueProvider.ProviderName, () => leagueProvider.GetLeagueAsync(configuration.LeagueId, cancellationToken));
        var settingsTask = CaptureAsync(leagueProvider.ProviderName, () => leagueProvider.GetSettingsAsync(configuration.LeagueId, cancellationToken));
        var rostersTask = CaptureAsync(leagueProvider.ProviderName, () => leagueProvider.GetRostersAsync(configuration.LeagueId, cancellationToken));
        var playersTask = CaptureAsync(leagueProvider.ProviderName, () => leagueProvider.GetPlayersAsync(cancellationToken));
        var matchupsTask = CaptureAsync(leagueProvider.ProviderName, () => leagueProvider.GetMatchupsAsync(configuration.LeagueId, currentWeek, cancellationToken));
        var transactionsTask = CaptureAsync(leagueProvider.ProviderName, () => leagueProvider.GetTransactionsAsync(configuration.LeagueId, currentWeek, cancellationToken));
        var trendingTask = CaptureAsync(leagueProvider.ProviderName, () => leagueProvider.GetTrendingPlayersAsync("nfl", "add", 24, cancellationToken));
        var scheduleTask = CaptureAsync(performanceProvider.ProviderName, () => performanceProvider.GetScheduleAsync(configuration.Season.ToString(), cancellationToken));
        var currentPerformanceTask = CaptureAsync(performanceProvider.ProviderName, () => performanceProvider.GetWeeklyPlayerStatsAsync(
            configuration.Season.ToString(),
            currentWeek,
            cancellationToken));
        var priorPerformanceTask = currentWeek > 1
            ? CaptureAsync(performanceProvider.ProviderName, () => performanceProvider.GetWeeklyPlayerStatsAsync(
                configuration.Season.ToString(),
                currentWeek - 1,
                cancellationToken))
            : Task.FromResult(ProviderResult<IReadOnlyList<PlayerPerformanceRecord>>.EmptyResult(
                performanceProvider.ProviderName,
                DateTimeOffset.UtcNow));
        var usageTask = CaptureAsync(performanceProvider.ProviderName, () => performanceProvider.GetUsageAsync(configuration.Season.ToString(), cancellationToken));
        var externalRosterTask = CaptureAsync(performanceProvider.ProviderName, () => performanceProvider.GetRostersAsync(configuration.Season.ToString(), cancellationToken));
        var injuriesTask = CaptureAsync(injuryProvider.ProviderName, () => injuryProvider.GetCurrentReportsAsync(cancellationToken));

        await Task.WhenAll(
            leagueTask, settingsTask, rostersTask, playersTask, matchupsTask,
            transactionsTask, trendingTask, scheduleTask, currentPerformanceTask,
            priorPerformanceTask, usageTask, externalRosterTask, injuriesTask);

        var failures = new List<SourceFailure>();
        var evidence = new List<EvidenceItem>();
        
        AddFailure(stateResult, failures);
        AddFailure(leagueTask.Result, failures);
        AddFailure(settingsTask.Result, failures);
        AddFailure(rostersTask.Result, failures);
        AddFailure(playersTask.Result, failures);
        AddFailure(matchupsTask.Result, failures);
        AddFailure(transactionsTask.Result, failures);
        AddFailure(trendingTask.Result, failures);
        AddFailure(scheduleTask.Result, failures);
        AddFailure(currentPerformanceTask.Result, failures);
        AddFailure(priorPerformanceTask.Result, failures);
        AddFailure(usageTask.Result, failures);
        AddFailure(externalRosterTask.Result, failures);
        AddFailure(injuriesTask.Result, failures);

        if (leagueTask.Result.Value is not null)
        {
            evidence.Add(CreateEvidence(
                leagueTask.Result,
                "league",
                configuration.LeagueId,
                leagueTask.Result.Value,
                "league-summary"));
        }

        if (settingsTask.Result.Value is not null)
        {
            evidence.Add(CreateEvidence(
                settingsTask.Result,
                "league-settings",
                configuration.LeagueId,
                settingsTask.Result.Value,
                "league-settings"));
        }

        if (rostersTask.Result.Value is not null)
        {
            evidence.Add(CreateEvidence(
                rostersTask.Result,
                "rosters",
                configuration.LeagueId,
                rostersTask.Result.Value,
                "roster-snapshot"));
        }

        AddCollectionEvidence(evidence, scheduleTask.Result, "schedule", "schedule");
        AddCollectionEvidence(evidence, currentPerformanceTask.Result, "performance", $"weekly-performance-{currentWeek}");
        AddCollectionEvidence(evidence, priorPerformanceTask.Result, "performance", $"weekly-performance-{currentWeek - 1}");
        AddCollectionEvidence(evidence, usageTask.Result, "usage", "player-usage");
        AddCollectionEvidence(evidence, externalRosterTask.Result, "external-roster", "external-roster");
        AddCollectionEvidence(evidence, injuriesTask.Result, "injury", "injury-report");
        AddCollectionEvidence(evidence, trendingTask.Result, "trending", "trending-players");

        LeagueSnapshot? snapshot = null;
        
        if (leagueTask.Result.Value is not null
            && settingsTask.Result.Value is not null
            && rostersTask.Result.Value is not null
            && stateResult.Value is not null
            && playersTask.Result.Value is not null)
        {
            snapshot = new LeagueSnapshot(
                leagueTask.Result.Value,
                settingsTask.Result.Value,
                rostersTask.Result.Value,
                matchupsTask.Result.Value ?? [],
                transactionsTask.Result.Value ?? [],
                stateResult.Value,
                playersTask.Result.Value,
                trendingTask.Result.Value ?? []);
        }

        var externalRoster = DeduplicateExternalRoster(externalRosterTask.Result.Value ?? []);
        var identityMap = BuildIdentityMap(
            playersTask.Result.Value ?? new Dictionary<string, PlayerRecord>(),
            externalRoster,
            externalRosterTask.Result,
            evidence);

        return new SourceRefreshResult(
            snapshot,
            NormalizePerformance((currentPerformanceTask.Result.Value ?? [])
                .Concat(priorPerformanceTask.Result.Value ?? [])
                .ToArray(), identityMap),
            scheduleTask.Result.Value ?? [],
            NormalizeUsage(usageTask.Result.Value ?? [], identityMap),
            injuriesTask.Result.Value ?? [],
            evidence,
            failures,
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<ExternalPlayerRecord> DeduplicateExternalRoster(
        IReadOnlyList<ExternalPlayerRecord> records) =>
        records
            .Where(record => !string.IsNullOrWhiteSpace(record.GsisId)
                || !string.IsNullOrWhiteSpace(record.ProviderPlayerId))
            .GroupBy(
                record => !string.IsNullOrWhiteSpace(record.GsisId)
                    ? record.GsisId!
                    : record.ProviderPlayerId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(record => record.ObservedAt)
                .First())
            .ToArray();

    private Dictionary<string, string> BuildIdentityMap(
        IReadOnlyDictionary<string, PlayerRecord> players,
        IReadOnlyList<ExternalPlayerRecord> candidates,
        ProviderResult<IReadOnlyList<ExternalPlayerRecord>> rosterResult,
        ICollection<EvidenceItem> evidence)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in players.Values)
        {
            if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(player.PlayerId))
            {
                map[player.PlayerId] = player.PlayerId;
                if (!rosterResult.IsSuccessful)
                {
                    evidence.Add(new EvidenceItem(
                        $"{performanceProvider.ProviderName}:identity:{player.PlayerId}",
                        performanceProvider.ProviderName,
                        "identity-unavailable",
                        player.PlayerId,
                        $"External identity roster was unavailable; only the already-compatible ID {player.PlayerId} was preserved.",
                        rosterResult.ObservedAt,
                        null,
                        null,
                        player.PlayerId,
                        rosterResult.Availability));
                }

                continue;
            }

            var match = (identityResolver ?? new PlayerIdentityResolver()).Resolve(
                player,
                candidates,
                performanceProvider.ProviderName);
            if (match.ProviderPlayerId is not null && !match.IsAmbiguous)
            {
                map[match.ProviderPlayerId] = match.SleeperPlayerId;
                continue;
            }

            if (match.Warning is not null)
            {
                evidence.Add(new EvidenceItem(
                    $"{performanceProvider.ProviderName}:identity:{player.PlayerId}",
                    performanceProvider.ProviderName,
                    match.IsAmbiguous ? "identity-conflict" : "identity-unavailable",
                    player.PlayerId,
                    match.Warning,
                    DateTimeOffset.UtcNow,
                    null,
                    match.Confidence,
                    player.PlayerId,
                    ProviderAvailability.Unavailable));
            }
        }

        return map;
    }

    private static IReadOnlyList<PlayerPerformanceRecord> NormalizePerformance(
        IReadOnlyList<PlayerPerformanceRecord> records,
        IReadOnlyDictionary<string, string> identityMap) =>
        records
            .Where(record => identityMap.ContainsKey(record.PlayerId))
            .Select(record => record with { PlayerId = identityMap[record.PlayerId] })
            .ToArray();

    private static IReadOnlyList<PlayerUsageRecord> NormalizeUsage(
        IReadOnlyList<PlayerUsageRecord> records,
        IReadOnlyDictionary<string, string> identityMap) =>
        records
            .Where(record => identityMap.ContainsKey(record.ProviderPlayerId))
            .Select(record => record with { ProviderPlayerId = identityMap[record.ProviderPlayerId] })
            .ToArray();

    private static EvidenceItem CreateEvidence<T>(
        ProviderResult<T> result,
        string type,
        string subject,
        T value,
        string suffix) =>
        new(
            $"{result.Provider}:{type}:{subject}:{suffix}",
            result.Provider,
            type,
            subject,
            JsonSerializer.Serialize(value),
            result.ObservedAt,
            result.FreshUntil,
            1m,
            subject,
            result.Availability);

    private static void AddCollectionEvidence<T>(
        ICollection<EvidenceItem> evidence,
        ProviderResult<IReadOnlyList<T>> result,
        string type,
        string subject) =>
        evidence.Add(CreateEvidence(
            result,
            type,
            subject,
            result.Value ?? [],
            $"{type}-snapshot"));

    private static void AddFailure<T>(ProviderResult<T> result, ICollection<SourceFailure> failures)
    {
        if (!result.IsSuccessful)
        {
            failures.Add(new SourceFailure(
                result.Provider,
                result.Availability,
                result.ObservedAt,
                result.ErrorCode,
                result.ErrorMessage));
        }
    }

    private static async Task<ProviderResult<T>> CaptureAsync<T>(
        string provider,
        Func<Task<ProviderResult<T>>> operation)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ProviderResult<T>.Failure(
                ProviderAvailability.Unavailable,
                provider,
                DateTimeOffset.UtcNow,
                "PROVIDER_EXCEPTION",
                exception.Message);
        }
    }
}
