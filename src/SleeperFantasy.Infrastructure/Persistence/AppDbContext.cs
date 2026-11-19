using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SleeperFantasy.Application.Abstractions;
using SleeperFantasy.Domain.Entities;
using SleeperFantasy.Infrastructure.Identity;

namespace SleeperFantasy.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IAppDbContext
{
    public DbSet<ManagerConfiguration> ManagerConfigurations => Set<ManagerConfiguration>();
    public DbSet<ProviderCacheMetadata> ProviderCacheMetadata => Set<ProviderCacheMetadata>();
    public DbSet<PlayerIdentityMapping> PlayerIdentityMappings => Set<PlayerIdentityMapping>();
    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();
    public DbSet<AnalysisEvidence> AnalysisEvidence => Set<AnalysisEvidence>();
    public DbSet<AnalysisResult> AnalysisResults => Set<AnalysisResult>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ManagerConfiguration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.SleeperUsername).HasMaxLength(100).IsRequired();
            entity.Property(x => x.SleeperUserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LeagueId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TeamName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RosterId).HasMaxLength(100);
            entity.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<ProviderCacheMetadata>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ResourceKey).HasMaxLength(500).IsRequired();
            entity.Property(x => x.ETag).HasMaxLength(500);
            entity.HasIndex(x => new { x.Provider, x.ResourceKey }).IsUnique();
        });

        builder.Entity<PlayerIdentityMapping>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SleeperPlayerId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Provider).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProviderPlayerId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Confidence).HasPrecision(5, 4);
            entity.HasIndex(x => new { x.SleeperPlayerId, x.Provider }).IsUnique();
        });

        builder.Entity<AnalysisRun>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CloudRunExecutionId).HasMaxLength(200);
            entity.Property(x => x.Stage).HasMaxLength(100);
            entity.Property(x => x.ErrorCode).HasMaxLength(100);
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.HasIndex(x => new { x.ManagerConfigurationId, x.CreatedAt });
            entity.HasOne(x => x.ManagerConfiguration)
                .WithMany()
                .HasForeignKey(x => x.ManagerConfigurationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AnalysisEvidence>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EvidenceType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.SubjectKey).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.AnalysisRunId, x.SubjectKey });
            entity.HasOne<AnalysisRun>()
                .WithMany()
                .HasForeignKey(x => x.AnalysisRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AnalysisResult>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.ResultVersion).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.AnalysisRunId).IsUnique();
            entity.HasOne(x => x.AnalysisRun)
                .WithOne(x => x.Result)
                .HasForeignKey<AnalysisResult>(x => x.AnalysisRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
