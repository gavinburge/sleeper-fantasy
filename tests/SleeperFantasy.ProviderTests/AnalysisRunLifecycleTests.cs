using Microsoft.EntityFrameworkCore;
using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Entities;
using SleeperFantasy.Infrastructure.Persistence;
using SleeperFantasy.Application.Features.Analysis;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class AnalysisRunLifecycleTests
{
    [Fact]
    public async Task Reuses_an_active_run_and_links_manual_retries()
    {
        await using var db = CreateDb();
        var configuration = CreateConfiguration();
        db.ManagerConfigurations.Add(configuration);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var first = await service.CreateOrReuseAsync(configuration, CancellationToken.None);
        var reused = await service.CreateOrReuseAsync(configuration, CancellationToken.None);

        Assert.Equal(first.Id, reused.Id);
        Assert.Equal(AnalysisRunStatus.Queued, reused.Status);

        first.Status = AnalysisRunStatus.Failed;
        first.ErrorCode = "SOURCE_UNAVAILABLE";
        await db.SaveChangesAsync();

        var retry = await service.CreateRetryAsync(first, CancellationToken.None);

        Assert.Equal(AnalysisRunStatus.Queued, retry.Status);
        Assert.Equal(first.Id, retry.RetryOfRunId);
        Assert.Equal(first.Attempt + 1, retry.Attempt);
    }

    [Fact]
    public async Task Recovers_expired_heartbeats_as_retryable_or_stale()
    {
        await using var db = CreateDb();
        var configuration = CreateConfiguration();
        db.ManagerConfigurations.Add(configuration);
        db.AnalysisRuns.AddRange(
            new AnalysisRun
            {
                Id = Guid.NewGuid(),
                ManagerConfigurationId = configuration.Id,
                Status = AnalysisRunStatus.Running,
                Attempt = 1,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
                LastHeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-20)
            },
            new AnalysisRun
            {
                Id = Guid.NewGuid(),
                ManagerConfigurationId = configuration.Id,
                Status = AnalysisRunStatus.Running,
                Attempt = 3,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
                LastHeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-20)
            });
        await db.SaveChangesAsync();

        var recovered = await CreateService(db)
            .RecoverAbandonedAsync(configuration.Id, CancellationToken.None);

        Assert.Equal(2, recovered);
        Assert.Contains(db.AnalysisRuns, run => run.Status == AnalysisRunStatus.Retryable);
        Assert.Contains(db.AnalysisRuns, run => run.Status == AnalysisRunStatus.Stale);
    }

    [Fact]
    public async Task Does_not_create_duplicate_active_runs_when_requests_race()
    {
        await using var db = CreateDb();
        var configuration = CreateConfiguration();
        db.ManagerConfigurations.Add(configuration);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var runs = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => service.CreateOrReuseAsync(configuration, CancellationToken.None)));

        Assert.Single(runs.Select(x => x.Id).Distinct());
        Assert.Single(db.AnalysisRuns.Where(x =>
            x.Status == AnalysisRunStatus.Queued
            || x.Status == AnalysisRunStatus.Running));
    }

    [Fact]
    public async Task Manual_retry_is_rejected_after_the_attempt_limit()
    {
        await using var db = CreateDb();
        var configuration = CreateConfiguration();
        db.ManagerConfigurations.Add(configuration);
        var run = new AnalysisRun
        {
            Id = Guid.NewGuid(),
            ManagerConfigurationId = configuration.Id,
            Status = AnalysisRunStatus.Failed,
            Attempt = 3,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.AnalysisRuns.Add(run);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(db).CreateRetryAsync(run, CancellationToken.None));

        Assert.Contains("retry limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ManagerConfiguration CreateConfiguration() =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = "manager",
            SleeperUsername = "fixture",
            SleeperUserId = "sleeper-user",
            LeagueId = "league",
            TeamName = "team",
            RosterId = "1",
            Season = 2026,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static AnalysisRunService CreateService(AppDbContext db) =>
        new(db, new AnalysisPersistenceService(db));
}
