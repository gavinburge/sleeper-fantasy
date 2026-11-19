using System.Text.Json;
using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Entities;
using SleeperFantasy.Domain.Providers;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class SourceRefreshService(
    ILeagueDataProvider leagueProvider,
    IPerformanceDataProvider performanceProvider,
    IInjuryDataProvider injuryProvider)
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
        var injuriesTask = CaptureAsync(injuryProvider.ProviderName, () => injuryProvider.GetCurrentReportsAsync(cancellationToken));

        await Task.WhenAll(
            leagueTask, settingsTask, rostersTask, playersTask, matchupsTask,
            transactionsTask, trendingTask, scheduleTask, currentPerformanceTask,
            priorPerformanceTask, usageTask, injuriesTask);

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

        return new SourceRefreshResult(
            snapshot,
            (currentPerformanceTask.Result.Value ?? [])
                .Concat(priorPerformanceTask.Result.Value ?? [])
                .ToArray(),
            scheduleTask.Result.Value ?? [],
            usageTask.Result.Value ?? [],
            injuriesTask.Result.Value ?? [],
            evidence,
            failures,
            DateTimeOffset.UtcNow);
    }

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
