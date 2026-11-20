using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SleeperFantasy.Application.Abstractions;
using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Entities;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class AnalysisRunService(
    IAppDbContext dbContext,
    AnalysisPersistenceService persistenceService)
{
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> CreationLocks = [];
    private const int MaxAttempts = 3;

    public async Task<AnalysisRun> CreateOrReuseAsync(
        ManagerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var creationLock = CreationLocks.GetOrAdd(configuration.Id, _ => new SemaphoreSlim(1, 1));
        await creationLock.WaitAsync(cancellationToken);
        
        try
        {
            await RecoverAbandonedAsync(configuration.Id, cancellationToken);

            var active = await FindActiveAsync(configuration.Id, cancellationToken);
            if (active is not null)
            {
                return active;
            }

            var now = DateTimeOffset.UtcNow;
            
            var run = new AnalysisRun
            {
                Id = Guid.NewGuid(),
                ManagerConfigurationId = configuration.Id,
                Status = AnalysisRunStatus.Queued,
                Attempt = 1,
                Stage = "queued",
                CreatedAt = now,
                LastHeartbeatAt = now
            };
            
            dbContext.AnalysisRuns.Add(run);
            
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return run;
            }
            catch (DbUpdateException)
            {
                dbContext.AnalysisRuns.Entry(run).State = EntityState.Detached;
                var concurrent = await FindActiveAsync(configuration.Id, cancellationToken);
                
                if (concurrent is not null)
                {
                    return concurrent;
                }

                throw;
            }
        }
        finally
        {
            creationLock.Release();
        }
    }

    public async Task<bool> ClaimAsync(
        Guid runId,
        string? executionId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        
        var updated = await dbContext.AnalysisRuns
            .Where(x => x.Id == runId
                && (x.Status == AnalysisRunStatus.Queued
                    || x.Status == AnalysisRunStatus.Retryable
                    || x.Status == AnalysisRunStatus.Stale))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, AnalysisRunStatus.Running)
                    .SetProperty(x => x.Stage, "starting")
                    .SetProperty(x => x.StartedAt, x => x.StartedAt ?? now)
                    .SetProperty(x => x.CompletedAt, (DateTimeOffset?)null)
                    .SetProperty(x => x.LastHeartbeatAt, now)
                    .SetProperty(x => x.CloudRunExecutionId, x => executionId ?? x.CloudRunExecutionId),
                cancellationToken);

        return updated == 1;
    }

    public async Task HeartbeatAsync(
        Guid runId,
        string stage,
        string? provider,
        string? executionId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.AnalysisRuns
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Analysis run '{runId}' was not found.");
        
        run.Status = AnalysisRunStatus.Running;
        run.Stage = stage;
        run.Provider = provider;
        run.CloudRunExecutionId = executionId ?? run.CloudRunExecutionId;
        run.LastHeartbeatAt = DateTimeOffset.UtcNow;
        
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RecoverAbandonedAsync(
        Guid managerConfigurationId,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(HeartbeatTimeout);
        
        var abandoned = await dbContext.AnalysisRuns
            .Where(x => x.ManagerConfigurationId == managerConfigurationId
                && x.Status == AnalysisRunStatus.Running
                && (x.LastHeartbeatAt == null || x.LastHeartbeatAt < cutoff))
            .ToListAsync(cancellationToken);

        foreach (var run in abandoned)
        {
            run.Status = run.Attempt < MaxAttempts
                ? AnalysisRunStatus.Retryable
                : AnalysisRunStatus.Stale;
            run.Stage = "recovered";
            run.ErrorCode = "HEARTBEAT_EXPIRED";
            run.ErrorMessage = "The analysis execution stopped reporting heartbeats.";
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.LastHeartbeatAt = run.CompletedAt;
        }

        if (abandoned.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return abandoned.Count;
    }

    public async Task<AnalysisRun> CreateRetryAsync(
        AnalysisRun previous,
        CancellationToken cancellationToken)
    {
        if (previous.Status is not (AnalysisRunStatus.Failed
            or AnalysisRunStatus.Retryable
            or AnalysisRunStatus.Stale))
        {
            throw new InvalidOperationException("Only failed or stale analysis runs can be retried.");
        }

        if (previous.Attempt >= MaxAttempts)
        {
            throw new InvalidOperationException($"Analysis retry limit of {MaxAttempts} attempts has been reached.");
        }

        if (await FindActiveAsync(previous.ManagerConfigurationId, cancellationToken) is not null)
        {
            throw new InvalidOperationException("An analysis execution is already active.");
        }

        var run = new AnalysisRun
        {
            Id = Guid.NewGuid(),
            ManagerConfigurationId = previous.ManagerConfigurationId,
            Status = AnalysisRunStatus.Queued,
            Attempt = previous.Attempt + 1,
            RetryOfRunId = previous.Id,
            Stage = "queued",
            CreatedAt = DateTimeOffset.UtcNow,
            LastHeartbeatAt = DateTimeOffset.UtcNow
        };
        
        dbContext.AnalysisRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        
        return run;
    }

    public async Task<AnalysisLatestResponse?> GetLatestAsync(
        Guid managerConfigurationId,
        CancellationToken cancellationToken)
    {
        await RecoverAbandonedAsync(managerConfigurationId, cancellationToken);
        
        var run = await dbContext.AnalysisRuns
            .AsNoTracking()
            .Include(x => x.Result)
            .Where(x => x.ManagerConfigurationId == managerConfigurationId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        
        if (run is null)
        {
            return null;
        }

        AnalysisReview? result = null;
        
        switch (run.Status)
        {
            case AnalysisRunStatus.Completed when run.Result is not null:
                result = JsonSerializer.Deserialize<AnalysisReview>(run.Result.ResultJson);
                break;
            
            case AnalysisRunStatus.Failed
                or AnalysisRunStatus.Retryable
                or AnalysisRunStatus.Stale:
            {
                var prior = await persistenceService.GetPriorSuccessfulResultAsync(
                    managerConfigurationId,
                    cancellationToken);
                
                if (prior is not null)
                {
                    result = JsonSerializer.Deserialize<AnalysisReview>(prior.ResultJson);
                }

                break;
            }
        }

        return new AnalysisLatestResponse(ToResponse(run), result);
    }

    public static AnalysisRunResponse ToResponse(AnalysisRun run) =>
        new(
            run.Id,
            run.Status,
            run.Attempt,
            run.Stage,
            run.CreatedAt,
            run.StartedAt,
            run.CompletedAt,
            run.LastHeartbeatAt,
            run.CloudRunExecutionId,
            run.Provider,
            run.ErrorCode,
            run.ErrorMessage,
            run.Status is AnalysisRunStatus.Failed
                or AnalysisRunStatus.Retryable
                or AnalysisRunStatus.Stale,
            run.RetryOfRunId);

    private Task<AnalysisRun?> FindActiveAsync(
        Guid managerConfigurationId,
        CancellationToken cancellationToken) =>
        dbContext.AnalysisRuns
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(
                x => x.ManagerConfigurationId == managerConfigurationId
                    && (x.Status == AnalysisRunStatus.Queued || x.Status == AnalysisRunStatus.Running),
                cancellationToken);
}
