using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Entities;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class AnalysisOrchestrator(
    SourceRefreshService refreshService,
    RosterRiskAnalyzer rosterRiskAnalyzer,
    AvailablePlayerService availablePlayerService,
    WaiverRankingService waiverRankingService,
    LanguageModelAnalysisService languageModelAnalysisService)
{
    public async Task<AnalysisReview> AnalyzeAsync(
        ManagerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var refreshed = await refreshService.RefreshAsync(configuration, cancellationToken);
        
        if (refreshed.League is null)
        {
            throw new InvalidOperationException("The league snapshot could not be refreshed.");
        }

        var rosterRisk = rosterRiskAnalyzer.Analyze(
            refreshed.League,
            refreshed.Performance,
            refreshed.Schedule,
            refreshed.Injuries,
            configuration.RosterId,
            refreshed.RefreshedAt);
        
        var availability = availablePlayerService.Derive(
            refreshed.League,
            refreshed.Failures,
            refreshed.RefreshedAt);
        
        var waivers = waiverRankingService.Rank(
            refreshed.League,
            rosterRisk,
            availability,
            refreshed.Performance,
            refreshed.Usage,
            refreshed.Injuries,
            refreshed.RefreshedAt,
            refreshed.Evidence);
        
        var deterministic = new DeterministicAnalysis(
            rosterRisk,
            availability,
            waivers,
            refreshed.Evidence,
            refreshed.Failures,
            refreshed.RefreshedAt);
        
        return await languageModelAnalysisService.MergeAsync(
            Guid.NewGuid().ToString("N"),
            deterministic,
            cancellationToken);
    }
}
