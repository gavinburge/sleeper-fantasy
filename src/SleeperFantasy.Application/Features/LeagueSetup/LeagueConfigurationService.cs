using Microsoft.EntityFrameworkCore;
using SleeperFantasy.Application.Abstractions;
using SleeperFantasy.Domain.Entities;
using SleeperFantasy.Domain.Providers;

namespace SleeperFantasy.Application.Features.LeagueSetup;

public sealed class LeagueConfigurationService(
    ILeagueDataProvider leagueDataProvider,
    IAppDbContext dbContext)
{
    public async Task<LeagueOperationResult<LeagueDiscoveryResponse>> DiscoverAsync(
        string? username,
        int season,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || season is < 2000 or > 2100)
        {
            return LeagueOperationResult<LeagueDiscoveryResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "INVALID_INPUT",
                "A Sleeper username and season between 2000 and 2100 are required.");
        }

        var userResult = await leagueDataProvider.GetUserAsync(username.Trim(), cancellationToken);
        if (!userResult.IsSuccessful || userResult.Value is null)
        {
            return FromProvider<LeagueDiscoveryResponse, SleeperUser>(userResult, "Sleeper user lookup failed.");
        }

        var leaguesResult = await leagueDataProvider.GetLeaguesAsync(
            userResult.Value.UserId,
            season.ToString(),
            cancellationToken);
        if (!leaguesResult.IsSuccessful)
        {
            return FromProvider<LeagueDiscoveryResponse, IReadOnlyList<LeagueSummary>>(
                leaguesResult,
                "Sleeper league lookup failed.");
        }

        if (leaguesResult.Value is null || leaguesResult.Value.Count == 0)
        {
            return LeagueOperationResult<LeagueDiscoveryResponse>.Failure(
                StatusCodes.Status404NotFound,
                "NO_LEAGUES",
                "No selectable Sleeper leagues were found for that user and season.");
        }

        var discoveredLeagues = new List<LeagueDiscoveryLeague>();
        foreach (var league in leaguesResult.Value)
        {
            var rostersResult = await leagueDataProvider.GetRostersAsync(league.LeagueId, cancellationToken);
            if (!rostersResult.IsSuccessful || rostersResult.Value is null)
            {
                return FromProvider<LeagueDiscoveryResponse, IReadOnlyList<RosterRecord>>(
                    rostersResult,
                    $"Sleeper rosters could not be loaded for league '{league.Name}'.");
            }

            var teams = rostersResult.Value
                .Select(roster => new LeagueTeam(
                    roster.RosterId,
                    roster.OwnerUserId,
                    string.IsNullOrWhiteSpace(roster.TeamName)
                        ? $"Roster {roster.RosterId}"
                        : roster.TeamName,
                    string.Equals(roster.OwnerUserId, userResult.Value.UserId, StringComparison.Ordinal)))
                .ToArray();
            discoveredLeagues.Add(new LeagueDiscoveryLeague(league, teams));
        }

        return LeagueOperationResult<LeagueDiscoveryResponse>.Success(
            new LeagueDiscoveryResponse(userResult.Value, discoveredLeagues));
    }

    public async Task<LeagueOperationResult<LeagueConfigurationResponse>> SaveAsync(
        string applicationUserId,
        LeagueConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationUserId)
            || string.IsNullOrWhiteSpace(request.Username)
            || request.Season is < 2000 or > 2100
            || string.IsNullOrWhiteSpace(request.LeagueId)
            || (string.IsNullOrWhiteSpace(request.RosterId) && string.IsNullOrWhiteSpace(request.TeamName)))
        {
            return LeagueOperationResult<LeagueConfigurationResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "INVALID_INPUT",
                "Username, season, league, and a roster ID or team name are required.");
        }

        var discovery = await DiscoverAsync(request.Username, request.Season, cancellationToken);
        if (!discovery.Succeeded || discovery.Value is null)
        {
            return LeagueOperationResult<LeagueConfigurationResponse>.Failure(
                discovery.StatusCode,
                discovery.ErrorCode,
                discovery.ErrorMessage);
        }

        var selectedLeague = discovery.Value.Leagues
            .FirstOrDefault(x => string.Equals(x.League.LeagueId, request.LeagueId, StringComparison.Ordinal));
        if (selectedLeague is null)
        {
            return LeagueOperationResult<LeagueConfigurationResponse>.Failure(
                StatusCodes.Status404NotFound,
                "LEAGUE_NOT_FOUND",
                "The selected league was not found for that Sleeper user and season.");
        }

        var matchingTeams = selectedLeague.Teams
            .Where(team => string.IsNullOrWhiteSpace(request.RosterId)
                ? string.Equals(team.TeamName, request.TeamName?.Trim(), StringComparison.OrdinalIgnoreCase)
                : string.Equals(team.RosterId, request.RosterId.Trim(), StringComparison.Ordinal))
            .ToArray();

        if (matchingTeams.Length == 0)
        {
            return LeagueOperationResult<LeagueConfigurationResponse>.Failure(
                StatusCodes.Status422UnprocessableEntity,
                "ROSTER_NOT_FOUND",
                "The selected roster was not found in the selected league.");
        }

        if (matchingTeams.Length > 1)
        {
            return LeagueOperationResult<LeagueConfigurationResponse>.Failure(
                StatusCodes.Status409Conflict,
                "AMBIGUOUS_TEAM",
                "Multiple rosters match that team name. Select a roster ID from the discovery results.");
        }

        var selectedTeam = matchingTeams[0];
        if (!selectedTeam.IsManagerTeam)
        {
            return LeagueOperationResult<LeagueConfigurationResponse>.Failure(
                StatusCodes.Status422UnprocessableEntity,
                "ROSTER_NOT_OWNED",
                "The selected roster is not owned by the configured Sleeper user.");
        }

        var now = DateTimeOffset.UtcNow;
        var configuration = await dbContext.ManagerConfigurations
            .SingleOrDefaultAsync(x => x.UserId == applicationUserId, cancellationToken);
        if (configuration is null)
        {
            configuration = new ManagerConfiguration
            {
                Id = Guid.NewGuid(),
                UserId = applicationUserId,
                SleeperUsername = discovery.Value.User.Username,
                SleeperUserId = discovery.Value.User.UserId,
                LeagueId = selectedLeague.League.LeagueId,
                TeamName = selectedTeam.TeamName,
                RosterId = selectedTeam.RosterId,
                Season = request.Season,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.ManagerConfigurations.Add(configuration);
        }
        else
        {
            configuration.SleeperUsername = discovery.Value.User.Username;
            configuration.SleeperUserId = discovery.Value.User.UserId;
            configuration.LeagueId = selectedLeague.League.LeagueId;
            configuration.TeamName = selectedTeam.TeamName;
            configuration.RosterId = selectedTeam.RosterId;
            configuration.Season = request.Season;
            configuration.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return LeagueOperationResult<LeagueConfigurationResponse>.Success(ToResponse(configuration));
    }

    public async Task<LeagueConfigurationResponse?> GetAsync(
        string applicationUserId,
        CancellationToken cancellationToken) =>
        await dbContext.ManagerConfigurations
            .AsNoTracking()
            .Where(x => x.UserId == applicationUserId)
            .Select(x => new LeagueConfigurationResponse(
                x.Id,
                x.SleeperUsername,
                x.SleeperUserId,
                x.LeagueId,
                x.TeamName,
                x.RosterId!,
                x.Season,
                x.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    private static LeagueConfigurationResponse ToResponse(ManagerConfiguration configuration) =>
        new(
            configuration.Id,
            configuration.SleeperUsername,
            configuration.SleeperUserId,
            configuration.LeagueId,
            configuration.TeamName,
            configuration.RosterId!,
            configuration.Season,
            configuration.UpdatedAt);

    private static LeagueOperationResult<T> FromProvider<T, TValue>(
        ProviderResult<TValue> result,
        string fallbackMessage)
    {
        var statusCode = result.Availability switch
        {
            ProviderAvailability.RateLimited => StatusCodes.Status429TooManyRequests,
            ProviderAvailability.Invalid => StatusCodes.Status502BadGateway,
            ProviderAvailability.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway
        };
        return LeagueOperationResult<T>.Failure(
            statusCode,
            result.ErrorCode ?? result.Availability.ToString().ToUpperInvariant(),
            result.ErrorMessage ?? fallbackMessage);
    }
}
