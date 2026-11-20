using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SleeperFantasy.Application.Abstractions;
using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Entities;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class AnalysisPersistenceService(IAppDbContext dbContext)
{
    public async Task PersistSuccessfulAsync(
        AnalysisRun run,
        AnalysisReview review,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        run.Status = Domain.Analysis.AnalysisRunStatus.Completed;
        run.Stage = "persisted";
        run.CompletedAt = now;
        run.LastHeartbeatAt = now;
        NormalizeRunTimestamps(run);
        run.ErrorCode = null;
        run.ErrorMessage = null;

        foreach (var evidence in review.Deterministic.Evidence)
        {
            dbContext.AnalysisEvidence.Add(new AnalysisEvidence
            {
                Id = Guid.NewGuid(),
                AnalysisRunId = run.Id,
                Provider = evidence.Provider,
                EvidenceType = evidence.EvidenceType,
                SubjectKey = evidence.SubjectKey,
                IdentityKey = evidence.IdentityKey,
                PayloadJson = JsonSerializer.Serialize(evidence.Fact),
                Availability = evidence.Availability.ToString(),
                IsStale = evidence.IsStale(now),
                ObservedAt = evidence.ObservedAt.ToUniversalTime(),
                FreshUntil = evidence.FreshUntil?.ToUniversalTime()
            });
        }

        dbContext.AnalysisResults.Add(new AnalysisResult
        {
            Id = Guid.NewGuid(),
            AnalysisRunId = run.Id,
            ResultJson = JsonSerializer.Serialize(review),
            ResultVersion = "1",
            Confidence = review.Confidence,
            IsStale = review.IsStale,
            SourceManifestJson = JsonSerializer.Serialize(review.Deterministic.Evidence
                .Select(x => new { x.EvidenceId, x.Provider, x.ObservedAt, x.FreshUntil, Availability = x.Availability.ToString() })),
            GeneratedAt = review.GeneratedAt.ToUniversalTime()
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PersistFailureAsync(
        AnalysisRun run,
        string errorCode,
        string errorMessage,
        bool retryable,
        CancellationToken cancellationToken)
    {
        dbContext.AnalysisEvidence.RemoveRange(
            dbContext.AnalysisEvidence.Local
                .Where(x => x.AnalysisRunId == run.Id));
        dbContext.AnalysisResults.RemoveRange(
            dbContext.AnalysisResults.Local
                .Where(x => x.AnalysisRunId == run.Id));

        run.Status = retryable
            ? Domain.Analysis.AnalysisRunStatus.Retryable
            : Domain.Analysis.AnalysisRunStatus.Failed;
        run.Stage = "failed";
        run.ErrorCode = errorCode;
        run.ErrorMessage = errorMessage;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.LastHeartbeatAt = run.CompletedAt;
        NormalizeRunTimestamps(run);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void NormalizeRunTimestamps(AnalysisRun run)
    {
        run.CreatedAt = run.CreatedAt.ToUniversalTime();
        run.StartedAt = run.StartedAt?.ToUniversalTime();
        run.CompletedAt = run.CompletedAt?.ToUniversalTime();
        run.LastHeartbeatAt = run.LastHeartbeatAt?.ToUniversalTime();
    }

    public Task<AnalysisResult?> GetPriorSuccessfulResultAsync(
        Guid managerConfigurationId,
        CancellationToken cancellationToken) =>
        dbContext.AnalysisResults
            .AsNoTracking()
            .Where(x => x.AnalysisRun != null
                && x.AnalysisRun.ManagerConfigurationId == managerConfigurationId
                && x.AnalysisRun.Status == "completed")
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
