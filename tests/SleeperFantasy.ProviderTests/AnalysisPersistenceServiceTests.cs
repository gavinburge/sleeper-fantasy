using Microsoft.EntityFrameworkCore;
using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Entities;
using SleeperFantasy.Infrastructure.Persistence;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class AnalysisPersistenceServiceTests
{
    [Fact]
    public async Task Stores_review_manifest_and_evidence_metadata()
    {
        await using var db = CreateDb();
        var fixture = new AnalysisFixture();
        var evidence = fixture.Evidence;
        var review = fixture.Review with
        {
            Deterministic = fixture.Deterministic with { Evidence = [evidence] }
        };
        var run = CreateRun(fixture.Configuration);
        db.ManagerConfigurations.Add(fixture.Configuration);
        db.AnalysisRuns.Add(run);

        await new AnalysisPersistenceService(db).PersistSuccessfulAsync(run, review, CancellationToken.None);

        var storedResult = await db.AnalysisResults.SingleAsync();
        var storedEvidence = await db.AnalysisEvidence.SingleAsync();
        Assert.Equal("completed", run.Status);
        Assert.Equal(review.Confidence, storedResult.Confidence);
        Assert.Contains("evidence-1", storedResult.SourceManifestJson);
        Assert.Equal("Available", storedEvidence.Availability);
        Assert.Equal(evidence.ObservedAt, storedEvidence.ObservedAt);
    }

    [Fact]
    public async Task Failed_persistence_does_not_replace_prior_successful_result()
    {
        await using var db = CreateDb();
        var fixture = new AnalysisFixture();
        var priorRun = CreateRun(fixture.Configuration);
        priorRun.Status = "completed";
        db.ManagerConfigurations.Add(fixture.Configuration);
        db.AnalysisRuns.Add(priorRun);
        db.AnalysisResults.Add(new AnalysisResult
        {
            Id = Guid.NewGuid(),
            AnalysisRunId = priorRun.Id,
            AnalysisRun = priorRun,
            ResultJson = "prior",
            ResultVersion = "1",
            Confidence = 1m,
            IsStale = false,
            SourceManifestJson = "[]",
            GeneratedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        var failedRun = CreateRun(fixture.Configuration);
        db.AnalysisRuns.Add(failedRun);
        await new AnalysisPersistenceService(db).PersistFailureAsync(
            failedRun, "SOURCE_UNAVAILABLE", "fixture unavailable", false, CancellationToken.None);

        Assert.Equal("prior", await db.AnalysisResults.Select(x => x.ResultJson).SingleAsync());
    }

    [Fact]
    public async Task Normalizes_provider_timestamps_to_utc_before_persistence()
    {
        await using var db = CreateDb();
        var fixture = new AnalysisFixture();
        var observedAt = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.FromHours(-5));
        var evidence = fixture.Evidence with
        {
            ObservedAt = observedAt,
            FreshUntil = observedAt.AddHours(1)
        };
        var review = fixture.Review with
        {
            GeneratedAt = observedAt,
            Deterministic = fixture.Deterministic with { Evidence = [evidence] }
        };
        var run = CreateRun(fixture.Configuration);
        db.ManagerConfigurations.Add(fixture.Configuration);
        db.AnalysisRuns.Add(run);

        await new AnalysisPersistenceService(db)
            .PersistSuccessfulAsync(run, review, CancellationToken.None);

        var storedEvidence = await db.AnalysisEvidence.SingleAsync();
        var storedResult = await db.AnalysisResults.SingleAsync();
        Assert.Equal(TimeSpan.Zero, storedEvidence.ObservedAt.Offset);
        Assert.Equal(observedAt, storedEvidence.ObservedAt);
        Assert.Equal(TimeSpan.Zero, storedResult.GeneratedAt.Offset);
        Assert.Equal(observedAt, storedResult.GeneratedAt);
    }

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AnalysisRun CreateRun(ManagerConfiguration configuration) =>
        new()
        {
            Id = Guid.NewGuid(),
            ManagerConfigurationId = configuration.Id,
            Status = "running",
            Attempt = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
}
