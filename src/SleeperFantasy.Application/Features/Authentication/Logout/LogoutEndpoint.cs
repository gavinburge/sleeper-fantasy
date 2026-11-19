using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Routing;

namespace SleeperFantasy.Application.Features.Authentication.Logout;

public static class LogoutEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/logout", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IAdministratorSession session) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { error = "A valid antiforgery token is required." });
            }

            await session.SignOutAsync();
            return Results.NoContent();
        })
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Sign out the administrator")
            .WithDescription("Clears the authenticated session cookie. Requires the antiforgery token.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
    }
}
