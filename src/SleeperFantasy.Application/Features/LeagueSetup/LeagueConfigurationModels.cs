using SleeperFantasy.Domain.Entities;
using SleeperFantasy.Domain.Providers;

namespace SleeperFantasy.Application.Features.LeagueSetup;

public sealed record LeagueDiscoveryResponse(
    SleeperUser User,
    IReadOnlyList<LeagueDiscoveryLeague> Leagues);

public sealed record LeagueDiscoveryLeague(
    LeagueSummary League,
    IReadOnlyList<LeagueTeam> Teams);

public sealed record LeagueTeam(
    string RosterId,
    string OwnerUserId,
    string TeamName,
    bool IsManagerTeam);

public sealed record LeagueConfigurationRequest(
    string Username,
    int Season,
    string LeagueId,
    string? RosterId,
    string? TeamName);

public sealed record LeagueConfigurationResponse(
    Guid Id,
    string SleeperUsername,
    string SleeperUserId,
    string LeagueId,
    string TeamName,
    string RosterId,
    int Season,
    DateTimeOffset ConfiguredAt);

public sealed record LeagueOperationResult<T>(
    bool Succeeded,
    T? Value,
    int StatusCode,
    string ErrorCode,
    string ErrorMessage)
{
    public static LeagueOperationResult<T> Success(T value) =>
        new(true, value, StatusCodes.Status200OK, string.Empty, string.Empty);

    public static LeagueOperationResult<T> Failure(
        int statusCode,
        string errorCode,
        string errorMessage) =>
        new(false, default, statusCode, errorCode, errorMessage);
}
