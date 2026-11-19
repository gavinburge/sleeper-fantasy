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
        });

        league.MapGet("/", async (
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
        });

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
        });

        return endpoints;
    }
}
