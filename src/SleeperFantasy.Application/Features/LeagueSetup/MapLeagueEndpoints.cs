using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;

namespace SleeperFantasy.Application.Features.LeagueSetup;

public static class LeagueEndpointMapping
{
    public static IEndpointRouteBuilder MapLeagueEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var league = endpoints.MapGroup("/api/v1/league")
            .RequireAuthorization()
            .WithTags("League");

        league.MapGet("/discovery", async (
            string? username,
            int season,
            LeagueConfigurationService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DiscoverAsync(username, season, cancellationToken);
            return result.Succeeded
                ? Results.Ok(result.Value)
                : Results.Json(
                    new { error = result.ErrorCode, message = result.ErrorMessage },
                    statusCode: result.StatusCode);
        })
            .WithName("DiscoverLeagues")
            .WithSummary("Discover a manager's Sleeper leagues")
            .WithDescription("Resolves a Sleeper username for a season and returns selectable leagues and rosters. No configuration is saved.")
            .Produces<LeagueDiscoveryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        league.MapGet("", async (
            ClaimsPrincipal user,
            LeagueConfigurationService service,
            CancellationToken cancellationToken) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            var configuration = await service.GetAsync(userId, cancellationToken);
            return configuration is null
                ? Results.NotFound(new { error = "CONFIGURATION_NOT_FOUND" })
                : Results.Ok(configuration);
        })
            .WithName("GetLeagueConfiguration")
            .WithSummary("Get the saved league configuration")
            .WithDescription("Returns the authenticated manager's selected Sleeper user, league, roster, season, and configuration timestamp.")
            .Produces<LeagueConfigurationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        league.MapPost("/configuration", async (
            LeagueConfigurationRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            ClaimsPrincipal user,
            LeagueConfigurationService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new
                {
                    error = "ANTIFORGERY_REQUIRED",
                    message = "A valid antiforgery token is required."
                });
            }

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.SaveAsync(userId, request, cancellationToken);
            return result.Succeeded
                ? Results.Ok(result.Value)
                : Results.Json(
                    new { error = result.ErrorCode, message = result.ErrorMessage },
                    statusCode: result.StatusCode);
        })
            .WithName("SaveLeagueConfiguration")
            .WithSummary("Save the selected Sleeper league and roster")
            .WithDescription("Validates the selected league and manager-owned roster, then persists the configuration. Sleeper credentials are never accepted or stored.")
            .Accepts<LeagueConfigurationRequest>("application/json")
            .Produces<LeagueConfigurationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}
