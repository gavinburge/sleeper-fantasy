extern alias WebProject;

using Web = WebProject::SleeperFantasy.Web;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class AnalysisViewModelParserTests
{
    [Fact]
    public void Parses_active_run_without_incomplete_result()
    {
        var runId = Guid.NewGuid();
        var json = $$"""
        {
          "run": {
            "id": "{{runId}}",
            "status": "running",
            "attempt": 1,
            "stage": "refreshing-sources",
            "createdAt": "2026-11-20T15:00:00Z",
            "startedAt": "2026-11-20T15:00:01Z",
            "lastHeartbeatAt": "2026-11-20T15:00:05Z",
            "retryAvailable": false
          },
          "result": null
        }
        """;

        var latest = Web.AnalysisViewModelParser.ParseLatest(json);

        Assert.NotNull(latest);
        Assert.Equal(runId, latest.Run.Id);
        Assert.Equal("running", latest.Run.Status);
        Assert.Null(latest.Result);
    }

    [Fact]
    public void Parses_completed_review_with_evidence_and_recommendations()
    {
        var json = """
        {
          "run": {
            "id": "00000000-0000-0000-0000-000000000001",
            "status": "completed",
            "attempt": 1,
            "createdAt": "2026-11-20T15:00:00Z",
            "completedAt": "2026-11-20T15:01:00Z",
            "retryAvailable": false
          },
          "result": {
            "deterministic": {
              "rosterRisk": {
                "leagueState": "regular-season",
                "currentWeek": 11,
                "players": [{
                  "playerId": "p1",
                  "playerName": "Example Player",
                  "position": "WR",
                  "isStarter": true,
                  "riskCodes": ["injury"],
                  "unavailableSignals": ["practice-status"],
                  "generatedAt": "2026-11-20T15:00:00Z"
                }],
                "warnings": [],
                "isAvailable": true,
                "generatedAt": "2026-11-20T15:00:00Z"
              },
              "availability": {
                "players": [],
                "snapshotAt": "2026-11-20T15:00:00Z",
                "isComplete": true,
                "warnings": []
              },
              "waivers": {
                "recommendations": [{
                  "playerId": "p2",
                  "playerName": "Waiver Player",
                  "position": "RB",
                  "score": 4.5,
                  "priority": 1,
                  "reasons": ["Addresses roster need"],
                  "evidenceIds": ["evidence-1"],
                  "conflicts": ["Usage data is stale"],
                  "confidence": 0.8,
                  "isStale": true
                }],
                "isComplete": true,
                "warnings": [],
                "generatedAt": "2026-11-20T15:00:00Z"
              },
              "evidence": [{
                "evidenceId": "evidence-1",
                "provider": "nflverse",
                "evidenceType": "performance",
                "subjectKey": "p2",
                "fact": "Recent usage",
                "observedAt": "2026-11-20T15:00:00Z",
                "availability": 0
              }],
              "failures": [],
              "generatedAt": "2026-11-20T15:00:00Z"
            },
            "contextualAnalysisAvailable": false,
            "caveats": ["Contextual analysis unavailable"],
            "confidence": 0.75,
            "isStale": false,
            "generatedAt": "2026-11-20T15:01:00Z"
          }
        }
        """;

        var latest = Web.AnalysisViewModelParser.ParseLatest(json);

        Assert.NotNull(latest?.Result);
        Assert.Equal("Example Player", latest.Result.Deterministic.RosterRisk.Players.Single().PlayerName);
        Assert.Equal("practice-status", latest.Result.Deterministic.RosterRisk.Players.Single().UnavailableSignals.Single());
        var recommendation = Assert.Single(latest.Result.Deterministic.Waivers.Recommendations);
        Assert.Equal("evidence-1", recommendation.EvidenceIds.Single());
        Assert.Equal("nflverse", latest.Result.Deterministic.Evidence.Single().Provider);
        Assert.False(latest.Result.ContextualAnalysisAvailable);
    }

    [Fact]
    public void Parses_failed_retryable_run_and_error_details()
    {
        var json = """
        {
          "id": "00000000-0000-0000-0000-000000000002",
          "status": "retryable",
          "attempt": 2,
          "errorCode": "PROVIDER_UNAVAILABLE",
          "errorMessage": "Sleeper returned HTTP 503.",
          "retryAvailable": true,
          "retryOfRunId": "00000000-0000-0000-0000-000000000001",
          "createdAt": "2026-11-20T15:00:00Z"
        }
        """;

        var run = Web.AnalysisViewModelParser.ParseRun(json);

        Assert.NotNull(run);
        Assert.Equal("retryable", run.Status);
        Assert.True(run.RetryAvailable);
        Assert.Equal("PROVIDER_UNAVAILABLE", run.ErrorCode);
        Assert.Equal("Sleeper returned HTTP 503.", run.ErrorMessage);
    }
}
