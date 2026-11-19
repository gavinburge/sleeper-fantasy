using Microsoft.EntityFrameworkCore;
using SleeperFantasy.Domain.Entities;

namespace SleeperFantasy.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<ManagerConfiguration> ManagerConfigurations { get; }
    DbSet<ProviderCacheMetadata> ProviderCacheMetadata { get; }
    DbSet<PlayerIdentityMapping> PlayerIdentityMappings { get; }
    DbSet<AnalysisRun> AnalysisRuns { get; }
    DbSet<AnalysisEvidence> AnalysisEvidence { get; }
    DbSet<AnalysisResult> AnalysisResults { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
